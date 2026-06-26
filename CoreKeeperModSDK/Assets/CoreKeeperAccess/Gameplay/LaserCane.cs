using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Patches;
using PugTilemap;
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
    // declenche un son positionnel dedie ; les cibles NON hostiles (creatures paisibles,
    // objets poses : champignons, drops, meubles...) ont leur propre piste, plus douce,
    // ECRASEE par tout hostile present. On LIT la visee, on ne vole PAS le stick droit : le
    // joueur continue de viser/frapper normalement (RT), la canne ne fait qu'ecouter.
    //
    // Architecture : pont mod <-> systeme ECS facon TileReader. Le mod (Tick) lit le stick et
    // pose la requete (LaserScan) ; le systeme LaserCaneSystem fait le DDA + la lecture de
    // tuile + la detection d'ennemi (il a le TileAccessor / CollisionWorld) et publie le
    // resultat ; le mod le sonifie (1 frame de latence, sans incidence pour de l'audio).
    internal static class LaserCane
    {
        private const float StickDeadzone = 0.25f;          // deadzone stick droit

        // Cadence de rappel du bip ennemi tant qu'une cible reste dans le faisceau. Volontairement
        // RAPIDE : ce son est un signal d'ALIGNEMENT (le rayon est sur l'ennemi -> mon tir part
        // dessus, je peux frapper), pas une simple presence. A cadence serree il devient quasi
        // tenu -> ca crepite vite = t'es dessus, ca s'arrete = t'as glisse. C'est le "viseur".
        private const float EnemyBeepInterval = 0.13f;

        // Son ennemi du laser. proximity_sensor_set = DEFINITIF (decision utilisateur).
        private const SfxID EnemySfxPlaceholder = SfxID.proximity_sensor_set;
        private const float EnemyVolume = 0.8f; // a regler a l'oreille

        // Cibles NON hostiles sur le trajet (creatures passives : insectes, chevres,
        // slimes dormants... / objets poses : champignons, drops, meubles). Un son par
        // CATEGORIE (pas par chose) ; placeholders, l'utilisateur choisira. Plus doux
        // que l'ennemi, et un hostile present les ECRASE (jamais masquer une menace).
        private const SfxID PassiveCreatureSfxPlaceholder = SfxID.inventory_doot;
        private const SfxID PassiveObjectSfxPlaceholder = SfxID.inventory_ding;
        private const float PassiveVolume = 0.35f; // a regler a l'oreille

        // Une creature passive bouge -> rappel (plus lent que l'ennemi : presence, pas
        // menace). Un objet pose ne bouge pas -> UN bip a l'accroche, pas de rappel.
        private const float PassiveBeepInterval = 1.0f;

        // Expose si la canne est active (stick droit pousse) : le curseur de construction
        // cede alors le D-pad (priorite au laser).
        public static bool Active;

        private static readonly int2 NoImpact = new int2(int.MinValue, int.MinValue);
        private static int2 _lastImpact = NoImpact;
        private static int2 _lastSpecial = NoImpact;
        private static long _lastEnemyKey;
        private static float _nextEnemyBeep;
        private static long _lastPassiveKey;
        private static float _nextPassiveBeep;

        public static void Tick()
        {
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null || Manager.ui == null) { Reset(); return; }

            // Salve du ping sonar en cours : fenetre sonore reservee, le laser se tait
            // sans perdre son etat (pas de Reset : a la reprise, pas de reannonce).
            if (PingSonar.Silencing) return;

            // Jeu normal seulement (comme le curseur) : pas en inventaire / fiche perso / carte.
            if (!InputContext.InGameFree) { Reset(); return; }

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

            // Bord de gouffre / d'eau sur le trajet : sonifie au changement de case
            // (plop / splash spatialise, meme langage que le curseur). Le rayon continue
            // au-dela : c'est l'info "il y a un vide a franchir ici" sans masquer la suite.
            if (LaserScan.HasSpecial)
            {
                if (!LaserScan.SpecialTile.Equals(_lastSpecial))
                {
                    _lastSpecial = LaserScan.SpecialTile;
                    int sx = LaserScan.SpecialTile.x - LaserScan.PlayerTile.x;
                    int sy = LaserScan.SpecialTile.y - LaserScan.PlayerTile.y;
                    BuildModeNavigator.SonifyTile(LaserScan.SpecialTile, in LaserScan.Special, sx, sy, false);
                }
            }
            else
            {
                _lastSpecial = NoImpact;
            }

            // Point d'impact : on ne (re)sonifie qu'au CHANGEMENT de case (comme le curseur
            // ne parle qu'au deplacement) -> pas de spam en visee stable. Si le rayon est
            // mort en portee max DANS l'eau / le gouffre, le bord a deja parle : silence.
            bool impactIsSurvolable = LaserScan.Impact.HasWall
                && (LaserScan.Impact.WallType == TileType.pit
                    || LaserScan.Impact.WallType == TileType.water);
            if (!LaserScan.ImpactTile.Equals(_lastImpact))
            {
                _lastImpact = LaserScan.ImpactTile;
                if (!impactIsSurvolable)
                {
                    int dx = LaserScan.ImpactTile.x - LaserScan.PlayerTile.x;
                    int dy = LaserScan.ImpactTile.y - LaserScan.PlayerTile.y;
                    BuildModeNavigator.SonifyTile(LaserScan.ImpactTile, in LaserScan.Impact, dx, dy, false);
                }
            }

            // Ennemi : bip immediat sur une NOUVELLE cible, puis rappel a la cadence tant
            // qu'une cible reste dans le faisceau (cible mobile suivie a l'oreille).
            // Un hostile present ECRASE la cible passive (une menace ne se partage pas
            // l'antenne) ; le passif se reannoncera quand la menace sera sortie du faisceau.
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
                _lastPassiveKey = 0;
            }
            else
            {
                _lastEnemyKey = 0;

                // Cible passive (creature paisible ou objet pose) : meme grammaire que
                // l'ennemi - son a l'accroche, TTS du nom sur NOUVELLE cible seulement.
                // Creature (mobile) : rappel lent pour la suivre ; objet (immobile) : un
                // seul bip, le re-balayer apres etre passe ailleurs re-accroche.
                if (LaserScan.HasPassive)
                {
                    bool isNew = LaserScan.PassiveKey != _lastPassiveKey;
                    bool beep = isNew
                        || (LaserScan.PassiveIsCreature && Time.unscaledTime >= _nextPassiveBeep);
                    if (beep)
                    {
                        PlayPassive(LaserScan.PassivePos, LaserScan.PassiveIsCreature,
                            LaserScan.PassiveInteractable);
                        _nextPassiveBeep = Time.unscaledTime + PassiveBeepInterval;
                    }
                    if (isNew)
                    {
                        string name = InGameTtsCore.ResolveObjectName(LaserScan.PassiveObjectId);
                        if (!string.IsNullOrEmpty(name)) TtsText.Say(name, true);
                    }
                    _lastPassiveKey = LaserScan.PassiveKey;
                }
                else
                {
                    _lastPassiveKey = 0;
                }
            }

            // Focus mortier : si un mortier est equipe et qu'un ennemi est dans le
            // faisceau, on cale le viseur du jeu dessus (placement instantane cote patch).
            UpdateMortarFocus(player);
        }

        // Cale le viseur du mortier sur l'ennemi vise par le laser. N'a de sens que si un
        // mortier est equipe ; sinon (autre arme / pas d'ennemi) on relache et le jeu reprend
        // sa visee normale. Le placement INSTANTANE se fait cote patch (mode souris + point
        // pose sur l'ennemi) : la visee manette classique rampe a vitesse finie (offset
        // integre dans le temps) -> pas reactive, d'ou le mode souris. Ici on ne fait que
        // publier l'intention (MortarFocus), les patches d'input s'occupent du reste.
        private static bool _mortarFocusWas;
        private static void UpdateMortarFocus(PlayerController player)
        {
            bool want = LaserScan.HasEnemy && IsMortarEquipped(player);
            if (want) MortarFocus.TargetWorld = LaserScan.EnemyPos;
            MortarFocus.Active = want;
            if (want != _mortarFocusWas)
            {
                _mortarFocusWas = want;
                Diag.Log("A11yMortarFocus", want
                    ? "ON cible=" + LaserScan.EnemyPos.x + "," + LaserScan.EnemyPos.y
                    : "OFF");
            }
        }

        // Un mortier est equipe ? Lu sur l'entite joueur via AimIndicatorCachedStatesCD.isMortar
        // (le jeu y met en cache le type d'arme en main pour son indicateur de visee).
        private static bool IsMortarEquipped(PlayerController player)
        {
            try
            {
                if (player == null) return false;
                return EntityUtility.HasComponentData<AimIndicatorCachedStatesCD>(player.entity, player.world)
                    && EntityUtility.GetComponentData<AimIndicatorCachedStatesCD>(player.entity, player.world).isMortar;
            }
            catch { return false; }
        }

        private static void Reset()
        {
            Active = false;
            MortarFocus.Active = false; // pas de laser -> pas de focus mortier
            LaserScan.Active = false;
            LaserScan.ResultValid = false; // ne pas agir sur un resultat perime a la reactivation
            _lastImpact = NoImpact;
            _lastSpecial = NoImpact;
            _lastEnemyKey = 0;
            _lastPassiveKey = 0;
        }

        // Bip ennemi positionnel : pan gauche-droite + pitch vertical (+1 demi-ton/ligne),
        // par rapport au joueur, comme les sons du curseur. PAS de trim distance ici (contrairement
        // aux autres sons positionnels) : ce son confirme l'ALIGNEMENT du tir, pas la distance (la
        // distance est deja portee par le bip boss / la sentinelle). Le faire faiblir avec
        // l'eloignement rendait le "t'es dessus" inaudible sur une cible lointaine -> volume plein.
        // Apercu sonore (panneau de reglages) : le son du VISEUR (cible ennemie au stick droit),
        // au centre, au volume de navigation actuellement regle.
        public static void Preview() => GameplayAudio.PlaySpatial(EnemySfxPlaceholder, 0f, 1f, EnemyVolume * A11ySettings.NavigationVolume);

        // Apercu d'une cible NON hostile (creature paisible) pour le menu d'apprentissage des
        // sons : timbre des passifs, centre, au volume regle.
        public static void PreviewPassive() => GameplayAudio.PlaySpatial(PassiveCreatureSfxPlaceholder, 0f, 1f, PassiveVolume * A11ySettings.NavigationVolume);

        // Apercu d'un OBJET passif pose au sol (champignon, drop, meuble) pour le menu
        // d'apprentissage : son distinct de la creature, centre, au volume regle.
        public static void PreviewObject() => GameplayAudio.PlaySpatial(PassiveObjectSfxPlaceholder, 0f, 1f, PassiveVolume * A11ySettings.NavigationVolume);

        private static void PlayEnemy(float2 worldPos)
        {
            var p = Manager.main != null ? Manager.main.player : null;
            if (p == null) return;
            float2 d = worldPos - new float2(p.WorldPosition.x, p.WorldPosition.z);
            float pan = GameplayAudio.PanFromTiles(d.x);
            float pitch = Mathf.Pow(2f, d.y / 12f);
            GameplayAudio.PlaySpatial(EnemySfxPlaceholder, pan, pitch, EnemyVolume * A11ySettings.NavigationVolume);
        }

        // Bip passif positionnel (meme grammaire pan/pitch que l'ennemi), timbre par
        // CATEGORIE (creature vs objet). Si l'objet est un vrai interactible, on greffe
        // le marqueur "on peut agir ici" du curseur (charge_bar_ui_1, hauteur FIXE,
        // faible volume - add-on d'identite, jamais porteur d'info, regle gravee).
        private static void PlayPassive(float2 worldPos, bool isCreature, bool interactable)
        {
            var p = Manager.main != null ? Manager.main.player : null;
            if (p == null) return;
            float2 d = worldPos - new float2(p.WorldPosition.x, p.WorldPosition.z);
            float pan = GameplayAudio.PanFromTiles(d.x);
            float pitch = Mathf.Pow(2f, d.y / 12f);
            float trim = GameplayAudio.DistanceTrim(math.length(d));
            GameplayAudio.PlaySpatial(
                isCreature ? PassiveCreatureSfxPlaceholder : PassiveObjectSfxPlaceholder,
                pan, pitch, PassiveVolume * A11ySettings.NavigationVolume * trim);
            if (interactable)
                GameplayAudio.PlaySpatial(SfxID.charge_bar_ui_1, pan, 1f, 0.1f * A11ySettings.NavigationVolume * trim);
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

        // Premier BORD de gouffre (pit) ou d'eau sur le trajet. Ces surfaces sont
        // bloquantes pour la MARCHE mais transparentes pour la VUE et les projectiles
        // (une fleche passe au-dessus, un voyant vise au travers) : le rayon les
        // signale puis continue, seuls les murs pleins l'arretent.
        public static bool HasSpecial;
        public static int2 SpecialTile;
        public static TileInfo Special;
        public static bool HasEnemy;     // un ennemi est sur le trajet (avant le mur)
        public static float2 EnemyPos;   // position monde (xz) de l'ennemi le plus proche
        public static long EnemyKey;     // cle d'entite index+version (NOUVELLE cible)
        public static ObjectID EnemyObjectId; // type de la creature (pour le TTS du nom)

        // Cible NON hostile la plus proche sur le trajet (creature passive ou objet pose),
        // independante de l'ennemi : un hostile plus loin ne doit jamais etre masque par
        // un champignon proche, donc les deux pistes sont publiees separement.
        public static bool HasPassive;
        public static float2 PassivePos;
        public static long PassiveKey;          // cle d'entite (creature) ou cle de case (objet)
        public static ObjectID PassiveObjectId; // pour le TTS du nom
        public static bool PassiveIsCreature;   // creature (timbre + rappel) vs objet (un bip)
        public static bool PassiveInteractable; // objet interactible -> marqueur du curseur
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
                long enemyKey = 0;
                ObjectID enemyObj = ObjectID.None;
                bool foundPassive = false;
                float2 passivePos = default;
                long passiveKey = 0;
                ObjectID passiveObj = ObjectID.None;
                bool passiveCreature = false;
                bool passiveInteractable = false;
                bool foundSpecial = false;
                int2 specialTile = default;
                TileInfo specialInfo = default;

                for (float dd = 1f; dd <= MaxRange + 0.001f; dd += Step)
                {
                    int2 c = new int2(
                        (int)math.round(origin.x + dir.x * dd),
                        (int)math.round(origin.y + dir.y * dd));
                    if (c.Equals(last)) continue; // meme case qu'au pas precedent
                    last = c;

                    // Mur -> point d'impact ici, on s'arrete (occlusion). Teste AVANT le
                    // scan ennemi : l'OverlapSphere (rayon 0.5) lance sur la case du mur
                    // attrapait un mob colle de L'AUTRE COTE de la paroi -> faux positif
                    // "a travers le mur" (mobile et introuvable, ex. slime en galerie
                    // voisine). Sur une case de mur on ne cherche donc JAMAIS d'ennemi.
                    // EXCEPTION : gouffre (pit) et eau sont bloquants pour la MARCHE
                    // mais transparents pour la vue et les projectiles (un voyant vise
                    // a travers, la fleche passe au-dessus) -> on note le premier BORD
                    // (sonifie cote mod) et le rayon CONTINUE ; le scan creatures reste
                    // actif sur ces cases (ennemis nageurs legitimes, ils sont visibles).
                    if (ta.TryGetBlockingTile(c, out TileCD blocking, true))
                    {
                        bool seeThrough = blocking.tileType == TileType.pit
                                       || blocking.tileType == TileType.water;
                        if (!seeThrough) { impact = c; break; }
                        if (!foundSpecial)
                        {
                            foundSpecial = true;
                            specialTile = c;
                            specialInfo = TileScan.Read(ref ta, c, World);
                        }
                    }
                    impact = c; // derniere case atteinte (libre, ou surface survolable)

                    // Premiere creature rencontree de chaque bord (hostile / paisible) =
                    // la plus proche (cases parcourues proche->loin). Les deux pistes sont
                    // independantes : un champignon proche ne masque pas l'ennemi derriere.
                    if (!foundEnemy || !foundPassive)
                    {
                        ScanCreatures(c, World,
                            ref foundEnemy, ref enemyPos, ref enemyKey, ref enemyObj,
                            ref foundPassive, ref passivePos, ref passiveKey, ref passiveObj,
                            ref passiveCreature, ref passiveInteractable);
                    }

                    // Objet pose (champignon, drop, meuble...) : via l'INDEX case->objet
                    // (gratuit, couvre aussi les objets sans collider ; rayon d'index 24 >
                    // portee 12, tout le faisceau est couvert). Une creature deja accrochee
                    // garde la main (plus saillante qu'un objet immobile).
                    if (!foundPassive && ObjectIndex.TryGet(c, out var entry))
                    {
                        foundPassive = true;
                        passivePos = new float2(c.x, c.y);
                        passiveKey = ObjectIndex.Key(c);
                        passiveObj = entry.Id;
                        passiveCreature = false;
                        passiveInteractable = entry.Interactable;
                    }
                }

                LaserScan.Impact = TileScan.Read(ref ta, impact, World);
                LaserScan.ImpactTile = impact;
                LaserScan.HasSpecial = foundSpecial;
                LaserScan.SpecialTile = specialTile;
                LaserScan.Special = specialInfo;
                LaserScan.HasEnemy = foundEnemy;
                LaserScan.EnemyPos = enemyPos;
                LaserScan.EnemyKey = enemyKey;
                LaserScan.EnemyObjectId = enemyObj;
                LaserScan.HasPassive = foundPassive;
                LaserScan.PassivePos = passivePos;
                LaserScan.PassiveKey = passiveKey;
                LaserScan.PassiveObjectId = passiveObj;
                LaserScan.PassiveIsCreature = passiveCreature;
                LaserScan.PassiveInteractable = passiveInteractable;
                LaserScan.ResultValid = true;
            }
            catch (System.Exception ex) { Diag.Error("A11yLaserDiag", ex); }
        }

        // Creatures sur la case, classees en deux bords. HOSTILE : entite a FactionCD non
        // exclue, hors CritterCD et hors slime dormant (l'existant). PAISIBLE : tout le
        // reste du regne animal - CritterCD (lucioles, insectes...), EnemyCD a faction
        // non hostile (chevres, betail), slime dormant (plante dans sa flaque, le jeu
        // lui-meme l'exclut de "ennemis a proximite" via ClaimBedSystem ; reveille = en
        // combat -> sentinelle d'aggro, pas de perte de securite). Les entites sans
        // EnemyCD ni CritterCD ni FactionCD hostile (PNJ, meubles a collider) ne sont
        // pas des creatures : elles passent par l'index d'objets.
        private void ScanCreatures(int2 c, World world,
            ref bool foundEnemy, ref float2 enemyPos, ref long enemyKey, ref ObjectID enemyObj,
            ref bool foundPassive, ref float2 passivePos, ref long passiveKey,
            ref ObjectID passiveObj, ref bool passiveCreature, ref bool passiveInteractable)
        {
            var cw = PhysicsManager.GetCollisionWorld();
            var hits = new NativeList<DistanceHit>(8, Allocator.Temp);
            if (cw.OverlapSphere(new float3(c.x, 0f, c.y), 0.5f, ref hits,
                    AnyFilter, QueryInteraction.Default))
            {
                foreach (var h in hits)
                {
                    bool critter = EntityUtility.HasComponentData<CritterCD>(h.Entity, world);
                    bool enemyCd = EntityUtility.HasComponentData<EnemyCD>(h.Entity, world);
                    if (!critter && !enemyCd) continue; // pas une creature
                    ObjectID oid = EntityUtility.HasComponentData<ObjectDataCD>(h.Entity, world)
                        ? EntityUtility.GetComponentData<ObjectDataCD>(h.Entity, world).objectID
                        : ObjectID.None;

                    // Oeuf de la ruche dormant (health==0, collider inactif) : ni ennemi ni
                    // passif, le faisceau le traverse en silence. Actif (health>0, active par
                    // le boss) : ennemi normal -> bip de verrouillage comme tout hostile.
                    if (oid == ObjectID.LarvaHiveEgg
                        && EntityUtility.HasComponentData<HealthCD>(h.Entity, world)
                        && EntityUtility.GetComponentData<HealthCD>(h.Entity, world).health <= 0)
                        continue;

                    bool hostile = !critter
                        && EntityUtility.HasComponentData<FactionCD>(h.Entity, world)
                        && IsEnemy(EntityUtility.GetComponentData<FactionCD>(h.Entity, world).faction)
                        && !IsDormantSlime(oid);

                    if (hostile && !foundEnemy)
                    {
                        foundEnemy = true;
                        enemyPos = new float2(c.x, c.y);
                        enemyKey = EntityKey.Of(h.Entity);
                        enemyObj = oid;
                    }
                    else if (!hostile && !foundPassive)
                    {
                        foundPassive = true;
                        passivePos = new float2(c.x, c.y);
                        // Cle d'entite dans le meme champ long que les cles de case
                        // (ObjectIndex.Key) : collision croisee theoriquement possible,
                        // consequence benigne (une annonce dedupliquee a tort), assumee.
                        passiveKey = EntityKey.Of(h.Entity);
                        passiveObj = oid;
                        passiveCreature = true;
                        passiveInteractable = false;
                    }
                    if (foundEnemy && foundPassive) break;
                }
            }
            hits.Dispose();
        }

        // Listes d'EXCLUSION partagees avec la sentinelle d'aggro et le ping sonar
        // (HostileFilter, dans AggroSentinel.cs) : tout ce qui n'est pas exclu compte
        // comme ennemi ; les slimes dormants sont rangees cote paisible.
        private static bool IsEnemy(FactionID f) => HostileFilter.IsHostile(f);
        private static bool IsDormantSlime(ObjectID id) => HostileFilter.IsDormantSlime(id);
    }
}
