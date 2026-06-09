using CoreKeeperAccess.Patches;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Canne laser de combat / exploration. Le faisceau part du joueur dans la direction de
    // VISEE (stick droit, player.aimDirection), avance case par case jusqu'au premier mur
    // (ou portee max) : c'est le POINT D'IMPACT. On le sonifie EXACTEMENT comme le curseur de
    // construction (son partage BuildModeNavigator.SonifyTile) mais SANS TTS (balayage continu).
    // En plus, tout ENNEMI rencontre sur le trajet (avant le mur, donc occlus naturellement)
    // declenche un son positionnel dedie. On LIT la visee, on ne vole PAS le stick droit : le
    // joueur continue de viser/frapper normalement (RT), la canne ne fait qu'ecouter.
    //
    // Architecture : pont mod <-> systeme ECS facon TileReader. Le mod (Tick) lit le stick et
    // pose la requete (LaserScan) ; le systeme LaserCaneSystem fait le DDA + la lecture de
    // tuile + la detection d'ennemi (il a le TileAccessor / CollisionWorld) et publie le
    // resultat ; le mod le sonifie (1 frame de latence, sans incidence pour de l'audio).
    internal static class LaserCane
    {
        private const float StickDeadzone = 0.25f;          // deadzone stick droit

        // Cadence de rappel du bip ennemi tant qu'une cible reste dans le faisceau (un ennemi
        // immobile ne ferait sinon qu'un seul bip puis silence). A regler a l'oreille.
        private const float EnemyBeepInterval = 0.4f;

        // Placeholder : l'utilisateur choisira le vrai son ennemi ensuite. proximity_sensor_set
        // = un son de detection, plausible en attendant.
        private const SfxID EnemySfxPlaceholder = SfxID.proximity_sensor_set;
        private const float EnemyVolume = 0.5f; // a regler a l'oreille

        // Expose si la canne est active (stick droit pousse) : le curseur de construction
        // cede alors le D-pad (priorite au laser).
        public static bool Active;

        private static readonly int2 NoImpact = new int2(int.MinValue, int.MinValue);
        private static int2 _lastImpact = NoImpact;
        private static int _lastEnemyKey;
        private static float _nextEnemyBeep;

        public static void Tick()
        {
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null || Manager.ui == null) { Reset(); return; }

            // Jeu normal seulement (comme le curseur) : pas en inventaire / fiche perso / carte.
            if (Manager.ui.isAnyInventoryShowing
                || (Manager.ui.characterWindow != null && Manager.ui.characterWindow.isShowing)
                || Manager.ui.isShowingMap)
            { Reset(); return; }

            var input = player.inputModule;
            if (input == null) { Reset(); return; }

            // Visee = stick droit BRUT (GetAxis2D 59/60, memes axes que la visee du jeu ->
            // mapping monde correct x=est, y=nord). On lit le RAW et PAS GetInputAxisValue :
            // notre propre patch Harmony sur GetInputAxisValue injecte une visee virtuelle
            // quand le curseur a arme AimActive -> ma lecture recevait cette injection et le
            // laser restait actif stick au neutre (le conflit entre les deux modes). On NE lit
            // pas non plus player.aimDirection (bloque au sud hors UpdateAim). discardForce
            // Movement=true pour ignorer un eventuel deplacement force (cinematique/auto-move).
            Vector2 aim = input.GetRawAxisInput(false, true, true);

            // Activation : stick droit pousse au-dela de la deadzone.
            if (aim.sqrMagnitude < StickDeadzone * StickDeadzone) { Reset(); return; }
            float2 dir = math.normalize(new float2(aim.x, aim.y));

            Active = true;
            LaserScan.AimDir = dir;
            LaserScan.PlayerTile = new int2(
                (int)math.round(player.WorldPosition.x),
                (int)math.round(player.WorldPosition.z));
            LaserScan.Active = true;

            // Consomme le resultat publie par le systeme (scan de la frame precedente).
            if (!LaserScan.ResultValid) return;

            // Point d'impact : on ne (re)sonifie qu'au CHANGEMENT de case (comme le curseur
            // ne parle qu'au deplacement) -> pas de spam en visee stable.
            if (!LaserScan.ImpactTile.Equals(_lastImpact))
            {
                _lastImpact = LaserScan.ImpactTile;
                int dx = LaserScan.ImpactTile.x - LaserScan.PlayerTile.x;
                int dy = LaserScan.ImpactTile.y - LaserScan.PlayerTile.y;
                BuildModeNavigator.SonifyTile(LaserScan.ImpactTile, in LaserScan.Impact, dx, dy, false);
            }

            // Ennemi : bip immediat sur une NOUVELLE cible, puis rappel a la cadence tant
            // qu'une cible reste dans le faisceau (cible mobile suivie a l'oreille).
            if (LaserScan.HasEnemy)
            {
                bool isNew = LaserScan.EnemyKey != _lastEnemyKey;
                if (isNew || Time.unscaledTime >= _nextEnemyBeep)
                {
                    PlayEnemy(LaserScan.EnemyPos);
                    _nextEnemyBeep = Time.unscaledTime + EnemyBeepInterval;
                }
                // TTS du nom du monstre sur une NOUVELLE cible seulement (pas a chaque bip :
                // le bip porte la position, le nom identifie). Balayer plusieurs ennemis
                // egrene donc leurs noms ; rester sur la meme cible ne repete pas.
                if (isNew)
                {
                    string name = InGameTtsCore.ResolveObjectName(LaserScan.EnemyObjectId);
                    if (!string.IsNullOrEmpty(name)) TtsText.Say(name, true);
                }
                _lastEnemyKey = LaserScan.EnemyKey;
            }
            else
            {
                _lastEnemyKey = 0;
            }
        }

        private static void Reset()
        {
            Active = false;
            LaserScan.Active = false;
            LaserScan.ResultValid = false; // ne pas agir sur un resultat perime a la reactivation
            _lastImpact = NoImpact;
            _lastEnemyKey = 0;
        }

        // Bip ennemi positionnel : pan gauche-droite + pitch vertical (+1 demi-ton/ligne),
        // par rapport au joueur, comme les sons du curseur.
        private static void PlayEnemy(float2 worldPos)
        {
            var p = Manager.main != null ? Manager.main.player : null;
            if (p == null) return;
            float2 d = worldPos - new float2(p.WorldPosition.x, p.WorldPosition.z);
            float halfW = HalfWidthTiles();
            float pan = halfW > 0.1f ? Mathf.Clamp(d.x / halfW, -1f, 1f) : 0f;
            float pitch = Mathf.Pow(2f, d.y / 12f);
            GameplayAudio.PlaySpatial(EnemySfxPlaceholder, pan, pitch, EnemyVolume);
        }

        // Demi-largeur visible en cases (range pour normaliser le pan -1..+1), comme le curseur.
        private static float HalfWidthTiles()
        {
            var cam = Manager.camera != null ? Manager.camera.gameCamera : null;
            return cam != null ? cam.orthographicSize * cam.aspect : 0f;
        }
    }

    // Pont mod <-> LaserCaneSystem. Le mod pose la requete (Active/AimDir/PlayerTile), le
    // systeme publie le point d'impact + l'eventuel ennemi sur le trajet.
    internal static class LaserScan
    {
        public static bool Active;       // le mod veut un scan cette frame
        public static float2 AimDir;     // direction de visee (xz, normalisee)
        public static int2 PlayerTile;   // case du joueur (origine du faisceau)

        public static bool ResultValid;
        public static int2 ImpactTile;   // case d'impact (premier mur, ou portee max)
        public static TileInfo Impact;   // contenu de la case d'impact
        public static bool HasEnemy;     // un ennemi est sur le trajet (avant le mur)
        public static float2 EnemyPos;   // position monde (xz) de l'ennemi le plus proche
        public static int EnemyKey;      // index d'entite (pour detecter une NOUVELLE cible)
        public static ObjectID EnemyObjectId; // type de la creature (pour le TTS du nom)
    }

    // Avance le faisceau case par case (DDA) dans la direction de visee jusqu'au premier mur
    // (les murs sont des TUILES, pas forcement des colliders -> on marche la tilemap, comme le
    // curseur, ce qui garantit un son identique sur la meme case). En parallele, OverlapSphere
    // par case pour reperer le premier ennemi (entite a FactionCD hostile) sur le trajet :
    // un ennemi derriere le mur d'impact n'est jamais atteint -> occlusion gratuite.
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class LaserCaneSystem : SystemBase
    {
        private const int MaxRange = 12;        // portee max en cases (espace ouvert)
        private const float Step = 0.34f;       // pas d'echantillonnage le long du rayon
        private const float ScanInterval = 0.05f; // ~20 Hz : assez pour de l'audio, menage le CPU

        private float _next;

        private static readonly CollisionFilter AnyFilter = new CollisionFilter
        {
            BelongsTo = uint.MaxValue,
            CollidesWith = uint.MaxValue,
        };

        protected override void OnUpdate()
        {
            if (!LaserScan.Active) return;
            // Dans un SystemBase, "Time" = TimeData ECS -> qualifier UnityEngine.Time.
            if (UnityEngine.Time.unscaledTime < _next) return; // on garde le dernier resultat publie
            _next = UnityEngine.Time.unscaledTime + ScanInterval;

            try
            {
                var ta = new TileAccessor(ref CheckedStateRef, true);
                int2 start = LaserScan.PlayerTile;
                float2 origin = new float2(start.x, start.y);
                float2 dir = LaserScan.AimDir;

                int2 impact = start;
                int2 last = start;
                bool foundEnemy = false;
                float2 enemyPos = default;
                int enemyKey = 0;
                ObjectID enemyObj = ObjectID.None;

                for (float dd = 1f; dd <= MaxRange + 0.001f; dd += Step)
                {
                    int2 c = new int2(
                        (int)math.round(origin.x + dir.x * dd),
                        (int)math.round(origin.y + dir.y * dd));
                    if (c.Equals(last)) continue; // meme case qu'au pas precedent
                    last = c;

                    // Premier ennemi rencontre = le plus proche (cases parcourues proche->loin).
                    if (!foundEnemy && TryFindEnemy(c, out int ek, out ObjectID eid))
                    {
                        foundEnemy = true;
                        enemyPos = new float2(c.x, c.y);
                        enemyKey = ek;
                        enemyObj = eid;
                    }

                    // Mur -> point d'impact ici, on s'arrete (occlusion).
                    if (ta.TryGetBlockingTile(c, out _, true)) { impact = c; break; }
                    impact = c; // derniere case libre atteinte
                }

                LaserScan.Impact = TileScan.Read(ref ta, impact, World);
                LaserScan.ImpactTile = impact;
                LaserScan.HasEnemy = foundEnemy;
                LaserScan.EnemyPos = enemyPos;
                LaserScan.EnemyKey = enemyKey;
                LaserScan.EnemyObjectId = enemyObj;
                LaserScan.ResultValid = true;
            }
            catch { }
        }

        // Un ennemi sur la case ? Entite a FactionCD dont la faction n'est pas exclue.
        // out objId = type de la creature (pour le TTS du nom), None si indispo.
        private bool TryFindEnemy(int2 c, out int key, out ObjectID objId)
        {
            key = 0;
            objId = ObjectID.None;
            var cw = PhysicsManager.GetCollisionWorld();
            var hits = new NativeList<DistanceHit>(8, Allocator.Temp);
            bool found = false;
            if (cw.OverlapSphere(new float3(c.x, 0f, c.y), 0.5f, ref hits,
                    AnyFilter, QueryInteraction.Default))
            {
                foreach (var h in hits)
                {
                    if (!EntityUtility.HasComponentData<FactionCD>(h.Entity, World)) continue;
                    // Creatures PASSIVES (lucioles, vers...) : marquees CritterCD par le jeu
                    // (distinct de EnemyCD, les deux s'excluent). On les ecarte du repere
                    // ennemi -> elles ne declenchent plus le bip de menace.
                    if (EntityUtility.HasComponentData<CritterCD>(h.Entity, World)) continue;
                    var faction = EntityUtility.GetComponentData<FactionCD>(h.Entity, World).faction;
                    if (IsEnemy(faction))
                    {
                        key = h.Entity.Index;
                        if (EntityUtility.HasComponentData<ObjectDataCD>(h.Entity, World))
                            objId = EntityUtility.GetComponentData<ObjectDataCD>(h.Entity, World).objectID;
                        found = true;
                        break;
                    }
                }
            }
            hits.Dispose();
            return found;
        }

        // Liste d'EXCLUSION v1 : tout ce qui n'est pas la-dedans compte comme ennemi. None
        // (pas de faction), Player / PlayerMinion (soi et ses alliés), Neutral, Merchant
        // (PNJ), Explosion (volume de degats transitoire, pas une creature a cibler).
        private static bool IsEnemy(FactionID f)
        {
            switch (f)
            {
                case FactionID.None:
                case FactionID.Player:
                case FactionID.Neutral:
                case FactionID.Merchant:
                case FactionID.PlayerMinion:
                case FactionID.Explosion:
                    return false;
                default:
                    return true;
            }
        }
    }
}
