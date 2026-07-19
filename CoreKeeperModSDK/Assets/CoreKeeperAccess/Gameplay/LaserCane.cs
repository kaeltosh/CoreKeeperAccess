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
            // tomeContextValid=false : etat ambigu (pas de joueur/UI) ou franchement hors jeu
            // (menu/inventaire/carte) -> on force TomeFocus.Active a false plutot que de risquer
            // le mode souris force pendant un ecran qui LIT ce flag (piege deja identifie sur
            // le mortier : "aucun effet de bord" valide uniquement hors menu).
            if (player == null || Manager.ui == null) { Reset(false); return; }

            // Jeu normal seulement (comme le curseur) : pas en inventaire / fiche perso / carte.
            if (!InputContext.InGameFree) { Reset(false); return; }

            // Roue de saut barre rapide en mode L1 (stick droit vole a la roue, cf.
            // HotbarJumpWheel) : la canne se tait le temps de la tenue, sinon elle
            // continuerait a scanner/biper des ennemis pendant qu'on vise un slot. On reste
            // en jeu direct -> le tome (si equipe) retombe sur le joueur, pas de coupure forcee.
            if (InfoKey.HotbarWheelLeft) { Reset(); return; }

            var input = player.inputModule;
            if (input == null) { Reset(false); return; }

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
            // Case sombre : on force la sonification (le son du noir) meme si le contenu REEL
            // serait normalement "survolable" (pit/eau) - la darkness prime, on ne revele rien
            // du tout sur cette case.
            bool impactIsSurvolable = !LaserScan.ImpactIsDark && LaserScan.Impact.HasWall
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

            // Focus tome d'invocation : canne tenue -> cible ce qu'elle vise (ennemi, sinon
            // le point d'impact). Cf. UpdateTomeFocus pour le cas canne relachee (centre).
            UpdateTomeFocus(player, true);
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

        // Un tome de commande (invocation) est equipe ? Meme cache que isMortar, meme champ
        // que celui qui affiche l'icone dediee sur le bouton principal (CommandMinionWeaponCD).
        private static bool IsCommandMinionEquipped(PlayerController player)
        {
            try
            {
                if (player == null) return false;
                return EntityUtility.HasComponentData<AimIndicatorCachedStatesCD>(player.entity, player.world)
                    && EntityUtility.GetComponentData<AimIndicatorCachedStatesCD>(player.entity, player.world).isCommandMinion;
            }
            catch { return false; }
        }

        // Focus tome : voir le commentaire de TomeFocus (NativeInputSuppressionPatch.cs) pour
        // la regle. canneActive=true (fin de Tick, canne tenue) -> cible l'ennemi accroche par
        // la canne, sinon son point d'impact (mur/portee - couvre le minion pioche, qui commande
        // une CASE et non un ennemi). canneActive=false (canne relachee, appele depuis Reset) ->
        // cible la position du joueur, jamais le viseur natif qui traine.
        private static bool _tomeFocusWas;
        private static void UpdateTomeFocus(PlayerController player, bool canneActive)
        {
            if (player == null) player = Manager.main != null ? Manager.main.player : null;
            bool tomeEquipped = IsCommandMinionEquipped(player);
            if (!tomeEquipped)
            {
                TomeFocus.Active = false;
                _tomeFocusWas = false;
                return;
            }

            bool haveEnemyTarget = canneActive && LaserScan.ResultValid && LaserScan.HasEnemy;
            float2 target;
            if (haveEnemyTarget)
                target = LaserScan.EnemyPos;
            else if (canneActive && LaserScan.ResultValid)
                target = new float2(LaserScan.ImpactTile.x, LaserScan.ImpactTile.y);
            else
                target = new float2(player.WorldPosition.x, player.WorldPosition.z);

            TomeFocus.TargetWorld = target;
            TomeFocus.Active = true;
            if (!_tomeFocusWas)
            {
                _tomeFocusWas = true;
                Diag.Log("A11yTomeFocus", "ON cible=" + target.x + "," + target.y);
            }
        }

        // tomeContextValid=true (defaut) : toujours en jeu direct, juste canne relachee -> le
        // tome (si equipe) retombe sur le joueur. false : etat hors jeu (menu/inventaire/carte/
        // pas de joueur) -> on coupe TomeFocus net, jamais de mode souris force hors gameplay.
        private static void Reset(bool tomeContextValid = true)
        {
            Active = false;
            MortarFocus.Active = false; // pas de laser -> pas de focus mortier
            LaserScan.Active = false;
            LaserScan.ResultValid = false; // ne pas agir sur un resultat perime a la reactivation
            _lastImpact = NoImpact;
            _lastSpecial = NoImpact;
            _lastEnemyKey = 0;
            _lastPassiveKey = 0;
            if (tomeContextValid)
                UpdateTomeFocus(null, false); // canne relachee -> le tome (si equipe) retombe sur le joueur
            else
            {
                TomeFocus.Active = false;
                _tomeFocusWas = false;
            }
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
        public static int2 ImpactTile;   // case d'impact (premier mur, premiere case sombre, ou portee max)
        public static TileInfo Impact;   // contenu de la case d'impact
        // Detecteur d'obscurite (design fige 16 juillet 2026) : le rayon s'est arrete sur une
        // case SOMBRE (traitee comme un mur, v1), pas un vrai mur. Force la sonification de
        // l'impact meme si son contenu REEL serait normalement "survolable" (pit/eau) - on ne
        // sait pas ce qu'il y a la, le son du noir doit jouer quoi qu'il arrive.
        public static bool ImpactIsDark;

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
                // Compagnon (familier/serviteur) : candidat BASSE PRIORITE, promu en cible
                // passive seulement si rien de mieux (creature paisible/objet) sur tout le
                // trajet - cf. commentaire ScanCreatures.
                bool foundCompanion = false;
                float2 companionPos = default;
                long companionKey = 0;
                ObjectID companionObj = ObjectID.None;
                bool foundSpecial = false;
                int2 specialTile = default;
                TileInfo specialInfo = default;
                bool impactIsDark = false;

                for (float dd = 1f; dd <= MaxRange + 0.001f; dd += Step)
                {
                    int2 c = new int2(
                        (int)math.round(origin.x + dir.x * dd),
                        (int)math.round(origin.y + dir.y * dd));
                    if (c.Equals(last)) continue; // meme case qu'au pas precedent
                    last = c;

                    // Detecteur d'obscurite (v1, design fige 16 juillet 2026) : une case
                    // sombre stoppe le rayon EXACTEMENT comme un mur - on ne sait pas ce qu'il
                    // y a derriere (chemin ouvert ou mur), donc on ne le decouvre pas. Testee
                    // AVANT tout le reste (mur/ennemi/objet) : rien n'est revele sur cette case
                    // ni au-dela. Cf. core-keeper-darkness-gate.md (dette "voir a travers
                    // l'ombre" explicitement differee).
                    if (!LightIndex.IsLit(ref ta, c))
                    {
                        impact = c;
                        impactIsDark = true;
                        break;
                    }

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
                            ref passiveCreature, ref passiveInteractable,
                            ref foundCompanion, ref companionPos, ref companionKey, ref companionObj);
                    }

                    // Objet pose (champignon, drop, meuble...) : via l'INDEX case->objet
                    // (gratuit, couvre aussi les objets sans collider ; rayon d'index 24 >
                    // portee 12, tout le faisceau est couvert). Une creature deja accrochee
                    // garde la main (plus saillante qu'un objet immobile).
                    // Stalagmite / OasisStalagmite : decor de terrain pur, jamais interactif
                    // ni minable (confirme en jeu) - exclues ICI seulement (le faisceau les
                    // ignore) pour ne pas accrocher/biper en rafale dans les zones ou elles
                    // s'entassent (ex. arene d'Azeos). BirdBossBeam (pilier de foudre) : deja
                    // sonorise en dedie (colonne/lignes, AzeosBoss.TickRangees) + filet
                    // FireProximity - le laser n'a rien a y ajouter, exclu pour ne pas biper
                    // en double/rafale au passage du faisceau dans une vague. L'index partage
                    // n'est pas touche : le sonar de proximite et les autres consommateurs les
                    // voient toujours.
                    if (!foundPassive && ObjectIndex.TryGet(c, out var entry)
                        && entry.Id != ObjectID.Stalagmite && entry.Id != ObjectID.OasisStalagmite
                        && entry.Id != ObjectID.BirdBossBeam)
                    {
                        foundPassive = true;
                        passivePos = new float2(c.x, c.y);
                        passiveKey = ObjectIndex.Key(c);
                        passiveObj = entry.Id;
                        passiveCreature = false;
                        passiveInteractable = entry.Interactable;
                    }
                }

                // Compagnon promu SEULEMENT si aucune vraie cible passive (creature paisible
                // ou objet pose) n'a ete trouvee sur tout le trajet - basse priorite demandee.
                if (!foundPassive && foundCompanion)
                {
                    foundPassive = true;
                    passivePos = companionPos;
                    passiveKey = companionKey;
                    passiveObj = companionObj;
                    passiveCreature = true;
                    passiveInteractable = false;
                }

                LaserScan.Impact = TileScan.Read(ref ta, impact, World);
                LaserScan.ImpactTile = impact;
                LaserScan.ImpactIsDark = impactIsDark;
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

        // Creatures sur la case, classees en TROIS bords. HOSTILE : entite a FactionCD non
        // exclue, hors CritterCD et hors slime dormant (l'existant). PAISIBLE : le reste du
        // regne animal - CritterCD (lucioles, insectes...), EnemyCD a faction non hostile
        // (chevres, betail), slime dormant (plante dans sa flaque, le jeu lui-meme l'exclut
        // de "ennemis a proximite" via ClaimBedSystem ; reveille = en combat -> sentinelle
        // d'aggro, pas de perte de securite). COMPAGNON (familier/serviteur, FactionID.
        // PlayerMinion) : BASSE PRIORITE dediee (demande utilisateur, 4 juillet) - colle au
        // joueur, quasi toujours la creature la plus proche du faisceau, masquait sans ca
        // une vraie plante/un coffre plus loin. Ne revendique PAS foundPassive ici ; simple
        // candidat retenu (le plus proche), promu par l'appelant SEULEMENT si rien d'autre
        // (creature paisible ou objet pose) n'a ete trouve sur tout le trajet. Les entites
        // sans EnemyCD ni CritterCD ni FactionCD hostile (PNJ, meubles a collider) ne sont
        // pas des creatures : elles passent par l'index d'objets.
        private void ScanCreatures(int2 c, World world,
            ref bool foundEnemy, ref float2 enemyPos, ref long enemyKey, ref ObjectID enemyObj,
            ref bool foundPassive, ref float2 passivePos, ref long passiveKey,
            ref ObjectID passiveObj, ref bool passiveCreature, ref bool passiveInteractable,
            ref bool foundCompanion, ref float2 companionPos, ref long companionKey, ref ObjectID companionObj)
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

                    bool hasFaction = EntityUtility.HasComponentData<FactionCD>(h.Entity, world);
                    FactionID faction = hasFaction
                        ? EntityUtility.GetComponentData<FactionCD>(h.Entity, world).faction
                        : FactionID.None;
                    bool hostile = !critter && hasFaction && IsEnemy(faction) && !IsDormantSlime(oid);
                    bool companion = !hostile && hasFaction && faction == FactionID.PlayerMinion;

                    if (hostile && !foundEnemy)
                    {
                        foundEnemy = true;
                        enemyPos = new float2(c.x, c.y);
                        enemyKey = EntityKey.Of(h.Entity);
                        enemyObj = oid;
                    }
                    else if (companion && !foundCompanion)
                    {
                        foundCompanion = true;
                        companionPos = new float2(c.x, c.y);
                        companionKey = EntityKey.Of(h.Entity);
                        companionObj = oid;
                    }
                    else if (!hostile && !companion && !foundPassive)
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
