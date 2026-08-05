using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Reglages utilisateur du mod, persistes dans "a11y-settings.json" sous
    // persistentDataPath/CoreKeeperAccess/ (donnees utilisateur, JAMAIS touchees par un
    // build) - cf. FilePath plus bas. File IO DIRECT (jamais via LoadedMod.GetFile) ->
    // fast-build suffit pour iterer, pas de declaration au ModManifest. Cree avec ses
    // defauts au premier boot s'il est absent.
    //
    // JsonUtility ne serialise QUE des primitives (lecon gravee : il jette silencieusement
    // les types definis par le mod) -> Data ne contient que des float/bool. Le panneau
    // in-game (SettingsMenu) ecrit via les mutateurs, chacun clampe et SAUVEGARDE
    // immediatement (robuste a un crash). Extrait de GameplayAudio.cs au build du 15 juin
    // (dette d'extraction purgee). Namespace conserve (CoreKeeperAccess.Gameplay) pour ne
    // pas casser les references existantes.
    internal static class A11ySettings
    {
        [System.Serializable]
        private class Data
        {
            // Volume maitre des SONS du mod (bips, marqueurs, tons...), 0..2 (jusqu'a 2 = +6 dB).
            // Le TTS n'est pas concerne (NVDA a son volume). Defaut d'usine 0.6, calibre a l'usage.
            public float masterVolume = 0.6f;
            // Volume du bip de pas (la boussole de locomotion, ex-tic directionnel). Defaut 0.1.
            public float directionTickVolume = 0.1f;
            // Bip de pas : boussole de locomotion permanente (un bip par case franchie,
            // direction encodee en pan/pitch). DECOUPLE du snap le 16 juin -> actif par
            // defaut (c'est devenu la boussole de l'utilisateur), regle au panneau.
            public bool stepBeep = true;
            // Volume du bloc NAVIGATION (canne laser + curseur de tuile : tick, surfaces,
            // materiaux, marqueurs, bips de cibles). 0..2 (defaut 1 = niveau de reference, deja
            // bon grace a la cible de normalisation TargetPeak ; jusqu'a 2 = +6 dB pour qui veut
            // la nav plus forte). Sans risque de clip : a 200 %, le pic d'un son reste
            // <= base x TargetPeak x 2 < 1. S'applique en plus du volume maitre. Defaut
            // d'usine 1.5 (nav poussee au-dessus du reste), calibre a l'usage.
            public float navigationVolume = 1.5f;
            // Volume de l'earcon de GUIDAGE par balises (le ping repete d'un itineraire actif).
            // Decouple de la distance (la distance pilote la CADENCE), c'est un niveau fixe.
            // 0..2 (defaut 1 ; jusqu'a 2 = +6 dB). S'applique en plus du volume maitre.
            public float guideVolume = 1f;
            // Snap directionnel (marche forcee au cardinal pour poser en rangs). Aussi
            // basculee par Triangle+L3 : meme source de verite, donc persistee. PONCTUEL,
            // donc inactif par defaut.
            public bool snapDirectional = false;
            // Ralenti automatique quand des ennemis chassent (CombatSlowMotion).
            public bool combatSlowMo = true;
            // Normalisation RMS des sons natifs du jeu (egalise des masterisations tres
            // inegales). Off = sons bruts.
            public bool normalizeAudio = true;
            // Sonar de proximite : nappes de bruit filtre sur les obstacles a <= 2 cases.
            // Defaut d'usine ACTIVE (aide de deplacement jugee utile d'entree).
            public bool proximitySonar = true;
            // Volume GENERAL du sonar de proximite, 0..2. Defaut = equilibre valide en jeu (16 juin).
            public float sonarVolume = 0.9f;
            // Volumes INDEPENDANTS des 2 timbres du sonar (v1), pour equilibrer a l'oreille : le
            // grave est percu plus faible que le medium a niveau egal (equi-sonie). 0..2. Defauts
            // = equilibre VALIDE en jeu par l'utilisateur (16 juin).
            public float sonarVolMedium = 0.7f;  // medium (neutre) : nord, est, ouest
            public float sonarVolGrave = 2f;     // grave (sombre) : sud (pousse au max a l'oreille)
            // Couche OBJETS du sonar (le ding sur les objets poses), togglable a part du bruit.
            // Defaut d'usine ETEINT (le bruit des nappes suffit, le ding alourdit).
            public bool objectDing = false;
            // Annonce vocale du nom des objets ramasses (a CHAQUE ramassage, pas seulement au
            // premier comme le jeu). Actif par defaut.
            public bool pickupAnnounce = true;
            // Filtre les BLOCS (tag TileBlock : terre, pierre, murs posables) hors de l'annonce
            // de ramassage, pour calmer le bruit en minant. Inactif par defaut (on entend tout).
            public bool pickupFilterBlocks = false;
            // Suffixe la quantite TOTALE possedee a l'annonce de ramassage ("47 en tout").
            // Defaut d'usine ACTIVE (suivi du stock juge utile a l'usage).
            public bool pickupTotal = true;
            // Sentinelle d'aggro : bips sur les monstres qui nous ont repere. Coupe-circuit +
            // volumes DEDIES SEPARES (multiplicateurs sur le BaseVolume du bip, 0..2 ; defaut 1)
            // pour les monstres ordinaires et pour les boss (timbres distincts -> reglages a part).
            public bool sentinelEnabled = true;
            public float sentinelVolume = 1f;       // monstres ordinaires
            public float sentinelBossVolume = 1f;   // boss (dent de scie)
            // Detecteur de zones de feu (AoE/pieges). Coupe-circuit + volume dedie (0..2).
            public bool fireEnabled = true;
            public float fireVolume = 1f;
            // Annonce vocale (TTS pre-rendu) de la vie du boss tous les 10 %, dans les deux
            // sens (descend ET remonte, pour ne jamais masquer un soin). PROVISOIRE,
            // generique (BossCD), cf. BossHealthAnnounce. Actif par defaut.
            public bool bossHealthCallouts = true;
            public float bossHealthVolume = 1f;   // 0..2
            // Vitesse du jeu pendant le ralenti de combat (Time.timeScale applique quand des
            // ennemis chassent). 0.30..0.70 (defaut 0.50 = mi-vitesse) : plus bas = plus lent.
            // Bornee a 0.30 mini pour ne jamais figer le jeu.
            public float slowMoSpeed = 0.5f;
            // Earcons d'alerte a l'apparition d'un etat subi (DoT feu/acide/radiation, stun).
            // Une alerte sonore au front montant, cf. ConditionAlerts. Actif par defaut.
            public bool conditionEarcons = true;
            public float conditionEarconsVolume = 1f;   // 0..2
            // Alertes SONORES de vie faible : sirene au seuil d'alerte (une fois) + battement
            // de coeur repete au seuil critique. Earcons generes, cf. VitalsReadout. Actif.
            public bool healthAlerts = true;
            public float healthAlertsVolume = 0.5f;     // sirene (60 %) + bips d'avertissement, 0..2 (defaut d'usine 0.5)
            public float healthBeatVolume = 1f;         // battement de coeur repete, volume SEPARE, 0..2
            // Seuils de declenchement (ratio de PV), reglables. Plages disjointes (alerte >=
            // 0.40, critique <= 0.35) -> le critique reste toujours sous l'alerte, sans clamp croise.
            public float healthAlertThreshold = 0.60f;  // 0.40..0.90
            public float healthCritThreshold = 0.25f;   // 0.10..0.35 (defaut d'usine 0.25)
            // Detecteur de collision directionnel (etage 3 sonar) : sonde 360 degres dans
            // l'axe ou l'on pousse le stick gauche, alerte (bruit aigu, volume exponentiel)
            // avant un infranchissable (mur/pit/eau). Defaut d'usine ETEINT (aide
            // supplementaire, comme le sonar de proximite a son lancement).
            public bool collisionRadar = false;
            public float collisionRadarVolume = 1f;   // 0..2
            public float collisionRadarRange = 4f;    // cases, 2..6
            // Nomenclature des boutons dans le menu d'aide : false = PlayStation
            // (Croix/Triangle/L2...), true = Xbox (A/Y/LT...). Defaut PlayStation.
            public bool xboxButtons = false;
            // Mode decouverte de la manette deja vu : force UNE fois a la 1re entree en jeu.
            public bool controllerTutorialSeen = false;
            // Roue de saut direct dans la barre rapide active (R1/L1 tenu, stick miroir,
            // cf. HotbarJumpWheel). Actif par defaut (feature demandee explicitement) ;
            // desactivable si R1/L1 pas-a-pas natif est prefere.
            public bool hotbarWheelEnabled = true;
            // Latence de declenchement de la roue (ms) : sous ce seuil, R1/L1 reste le
            // pas-a-pas natif (systeme classique, aucune latence ajoutee) ; au-dela, la
            // roue s'ouvre. 0..300, defaut 120 (calibre a l'usage, coexistence des 2 systemes).
            public float hotbarWheelHoldMs = 120f;
            // Coupe l'annonce "X, interaction disponible" (WatchInteractable) UNIQUEMENT
            // pendant que le curseur de tuile est detache (BuildModeNavigator.CursorDetached) :
            // evite le doublon avec le TTS du curseur lui-meme. Hors curseur, comportement
            // inchange. Defaut d'usine ETEINT (comportement historique garde).
            public bool muteInteractInCursor = false;
            // Scanner de proximite (R3 tenu) : earcon positionnel navigable par categorie sur
            // les choses visibles a l'ecran. Feature PERMANENTE (comme la roue de stats ou la
            // roue d'actions) : pas de coupe-circuit, juste un volume.
            public float scannerVolume = 1f; // 0..2, volume de l'earcon de navigation (pas le beacon, qui reutilise GuideVolume)
            // Ping de reperage d'un autre joueur en multi (R3 + stick gauche, cf. PlayerPing).
            // Niveau FIXE (la distance pilote la cadence, et le volume seulement HORS ecran) :
            // 0..2, defaut 1. Comme le guidage, pas de coupe-circuit - le ping ne sonne que si
            // une cible est armee, "desactive" est une position de la roue.
            public float playerPingVolume = 1f;
            // Detecteur d'obscurite (design fige 16 juillet 2026, cf. core-keeper-darkness-gate.md) :
            // bride curseur/canne/scanner/sonar/collision par-case si non eclaire, parite voyant.
            // Toggle present EN PHASE DE TEST (a retirer ou garder selon retour d'experience une
            // fois valide en jeu - decision explicite, PAS un toggle de confort comme les autres
            // alertes). Actif par defaut.
            public bool darknessGate = true;
        }

        private static Data _d = new Data();

        public static float MasterVolume => _d.masterVolume;
        public static float DirectionTickVolume => _d.directionTickVolume;
        public static bool StepBeep => _d.stepBeep;
        public static float NavigationVolume => _d.navigationVolume;
        public static float GuideVolume => _d.guideVolume;

        // Snap : source de verite partagee entre Triangle+L3 (DirectionAssist) et le
        // panneau. Le set persiste -> l'etat survit au relancement.
        public static bool SnapDirectional
        {
            get => _d.snapDirectional;
            set { _d.snapDirectional = value; Save(); }
        }

        public static bool CombatSlowMo => _d.combatSlowMo;
        public static bool NormalizeAudio => _d.normalizeAudio;
        public static bool ProximitySonar => _d.proximitySonar;
        public static float SonarVolume => _d.sonarVolume;
        public static float SonarVolMedium => _d.sonarVolMedium;
        public static float SonarVolGrave => _d.sonarVolGrave;
        public static bool ObjectDing => _d.objectDing;
        public static bool PickupAnnounce => _d.pickupAnnounce;
        public static bool PickupFilterBlocks => _d.pickupFilterBlocks;
        public static bool PickupTotal => _d.pickupTotal;
        public static bool SentinelEnabled => _d.sentinelEnabled;
        public static float SentinelVolume => _d.sentinelVolume;
        public static float SentinelBossVolume => _d.sentinelBossVolume;
        public static bool FireEnabled => _d.fireEnabled;
        public static float FireVolume => _d.fireVolume;
        public static bool BossHealthCallouts => _d.bossHealthCallouts;
        public static float BossHealthVolume => _d.bossHealthVolume;
        public static float SlowMoSpeed => _d.slowMoSpeed;
        public static bool ConditionEarcons => _d.conditionEarcons;
        public static float ConditionEarconsVolume => _d.conditionEarconsVolume;
        public static bool HealthAlerts => _d.healthAlerts;
        public static float HealthAlertsVolume => _d.healthAlertsVolume;
        public static float HealthBeatVolume => _d.healthBeatVolume;
        public static float HealthAlertThreshold => _d.healthAlertThreshold;
        public static float HealthCritThreshold => _d.healthCritThreshold;
        public static bool XboxButtons => _d.xboxButtons;
        public static bool ControllerTutorialSeen => _d.controllerTutorialSeen;
        public static bool CollisionRadar => _d.collisionRadar;
        public static float CollisionRadarVolume => _d.collisionRadarVolume;
        public static float CollisionRadarRange => _d.collisionRadarRange;
        public static bool HotbarWheelEnabled => _d.hotbarWheelEnabled;
        public static float HotbarWheelHoldMs => _d.hotbarWheelHoldMs;
        public static bool MuteInteractInCursor => _d.muteInteractInCursor;
        public static float ScannerVolume => _d.scannerVolume;
        public static float PlayerPingVolume => _d.playerPingVolume;
        public static bool DarknessGate => _d.darknessGate;

        // Mutateurs du panneau de reglages : clamp + sauvegarde immediate.
        public static void SetMasterVolume(float v) { _d.masterVolume = Mathf.Clamp(v, 0f, 2f); Save(); }
        public static void SetDirectionTickVolume(float v) { _d.directionTickVolume = Mathf.Clamp(v, 0f, 2f); Save(); }
        public static void SetNavigationVolume(float v) { _d.navigationVolume = Mathf.Clamp(v, 0f, 2f); Save(); }
        public static void SetGuideVolume(float v) { _d.guideVolume = Mathf.Clamp(v, 0f, 2f); Save(); }
        public static void SetStepBeep(bool v) { _d.stepBeep = v; Save(); }
        public static void SetSnapDirectional(bool v) { _d.snapDirectional = v; Save(); }
        public static void SetCombatSlowMo(bool v) { _d.combatSlowMo = v; Save(); }
        public static void SetNormalizeAudio(bool v) { _d.normalizeAudio = v; Save(); }
        public static void SetProximitySonar(bool v) { _d.proximitySonar = v; Save(); }
        public static void SetSonarVolume(float v) { _d.sonarVolume = Mathf.Clamp(v, 0f, 2f); Save(); }
        public static void SetSonarVolMedium(float v) { _d.sonarVolMedium = Mathf.Clamp(v, 0f, 2f); Save(); }
        public static void SetSonarVolGrave(float v) { _d.sonarVolGrave = Mathf.Clamp(v, 0f, 2f); Save(); }
        public static void SetObjectDing(bool v) { _d.objectDing = v; Save(); }
        public static void SetPickupAnnounce(bool v) { _d.pickupAnnounce = v; Save(); }
        public static void SetPickupFilterBlocks(bool v) { _d.pickupFilterBlocks = v; Save(); }
        public static void SetPickupTotal(bool v) { _d.pickupTotal = v; Save(); }
        public static void SetSentinelEnabled(bool v) { _d.sentinelEnabled = v; Save(); }
        public static void SetSentinelVolume(float v) { _d.sentinelVolume = Mathf.Clamp(v, 0f, 2f); Save(); }
        public static void SetSentinelBossVolume(float v) { _d.sentinelBossVolume = Mathf.Clamp(v, 0f, 2f); Save(); }
        public static void SetFireEnabled(bool v) { _d.fireEnabled = v; Save(); }
        public static void SetFireVolume(float v) { _d.fireVolume = Mathf.Clamp(v, 0f, 2f); Save(); }
        public static void SetBossHealthCallouts(bool v) { _d.bossHealthCallouts = v; Save(); }
        public static void SetBossHealthVolume(float v) { _d.bossHealthVolume = Mathf.Clamp(v, 0f, 2f); Save(); }
        public static void SetSlowMoSpeed(float v) { _d.slowMoSpeed = Mathf.Clamp(v, 0.3f, 0.7f); Save(); }
        public static void SetConditionEarcons(bool v) { _d.conditionEarcons = v; Save(); }
        public static void SetConditionEarconsVolume(float v) { _d.conditionEarconsVolume = Mathf.Clamp(v, 0f, 2f); Save(); }
        public static void SetHealthAlerts(bool v) { _d.healthAlerts = v; Save(); }
        public static void SetHealthAlertsVolume(float v) { _d.healthAlertsVolume = Mathf.Clamp(v, 0f, 2f); Save(); }
        public static void SetHealthBeatVolume(float v) { _d.healthBeatVolume = Mathf.Clamp(v, 0f, 2f); Save(); }
        public static void SetHealthAlertThreshold(float v) { _d.healthAlertThreshold = Mathf.Clamp(v, 0.4f, 0.9f); Save(); }
        public static void SetHealthCritThreshold(float v) { _d.healthCritThreshold = Mathf.Clamp(v, 0.1f, 0.35f); Save(); }
        public static void SetXboxButtons(bool v) { _d.xboxButtons = v; Save(); }
        public static void SetControllerTutorialSeen(bool v) { _d.controllerTutorialSeen = v; Save(); }
        public static void SetCollisionRadar(bool v) { _d.collisionRadar = v; Save(); }
        public static void SetCollisionRadarVolume(float v) { _d.collisionRadarVolume = Mathf.Clamp(v, 0f, 2f); Save(); }
        public static void SetCollisionRadarRange(float v) { _d.collisionRadarRange = Mathf.Clamp(Mathf.Round(v), 2f, 6f); Save(); }
        public static void SetHotbarWheelEnabled(bool v) { _d.hotbarWheelEnabled = v; Save(); }
        public static void SetHotbarWheelHoldMs(float v) { _d.hotbarWheelHoldMs = Mathf.Clamp(Mathf.Round(v), 0f, 300f); Save(); }
        public static void SetMuteInteractInCursor(bool v) { _d.muteInteractInCursor = v; Save(); }
        public static void SetScannerVolume(float v) { _d.scannerVolume = Mathf.Clamp(v, 0f, 2f); Save(); }
        public static void SetPlayerPingVolume(float v) { _d.playerPingVolume = Mathf.Clamp(v, 0f, 2f); Save(); }
        public static void SetDarknessGate(bool v) { _d.darknessGate = v; Save(); }

        // Le fichier vit dans persistentDataPath (DONNEES UTILISATEUR), PAS dans le dossier
        // d'install du mod : un build (Unity ou fast-build) reconstruit ce dossier et
        // ecraserait les reglages a chaque fois. Meme emplacement-philosophie que BeaconStore.
        private static string FilePath() => System.IO.Path.Combine(
            Application.persistentDataPath, "CoreKeeperAccess", "a11y-settings.json");

        // Ancien emplacement (dans le dossier du mod) : migre une seule fois si present,
        // pour ne pas perdre les reglages deja faits.
        private static string LegacyPath() => System.IO.Path.Combine(
            Application.streamingAssetsPath, "Mods", "CoreKeeperAccess", "a11y-settings.json");

        public static void Load()
        {
            try
            {
                string path = FilePath();
                if (System.IO.File.Exists(path)) { Apply(System.IO.File.ReadAllText(path)); return; }

                string legacy = LegacyPath();
                if (System.IO.File.Exists(legacy))
                {
                    Apply(System.IO.File.ReadAllText(legacy));
                    Save();   // recopie vers le nouvel emplacement persistant
                    return;
                }

                Save();       // premier boot : cree le fichier avec les defauts
            }
            catch (System.Exception ex)
            {
                // Fichier illisible/corrompu : defauts + trace, le mod ne doit jamais
                // dependre de la sante d'un JSON.
                Diag.Error("A11ySettings", ex);
            }
        }

        private static void Apply(string json)
        {
            var d = JsonUtility.FromJson<Data>(json);
            if (d == null) return;
            d.masterVolume = Mathf.Clamp(d.masterVolume, 0f, 2f);
            d.directionTickVolume = Mathf.Clamp(d.directionTickVolume, 0f, 2f);
            d.navigationVolume = Mathf.Clamp(d.navigationVolume, 0f, 2f);
            d.guideVolume = Mathf.Clamp(d.guideVolume, 0f, 2f);
            d.sonarVolume = Mathf.Clamp(d.sonarVolume, 0f, 2f);
            d.sonarVolMedium = Mathf.Clamp(d.sonarVolMedium, 0f, 2f);
            d.sonarVolGrave = Mathf.Clamp(d.sonarVolGrave, 0f, 2f);
            d.sentinelVolume = Mathf.Clamp(d.sentinelVolume, 0f, 2f);
            d.sentinelBossVolume = Mathf.Clamp(d.sentinelBossVolume, 0f, 2f);
            d.fireVolume = Mathf.Clamp(d.fireVolume, 0f, 2f);
            d.slowMoSpeed = Mathf.Clamp(d.slowMoSpeed, 0.3f, 0.7f);
            d.conditionEarconsVolume = Mathf.Clamp(d.conditionEarconsVolume, 0f, 2f);
            d.healthAlertsVolume = Mathf.Clamp(d.healthAlertsVolume, 0f, 2f);
            d.healthBeatVolume = Mathf.Clamp(d.healthBeatVolume, 0f, 2f);
            d.healthAlertThreshold = Mathf.Clamp(d.healthAlertThreshold, 0.4f, 0.9f);
            d.healthCritThreshold = Mathf.Clamp(d.healthCritThreshold, 0.1f, 0.35f);
            d.collisionRadarVolume = Mathf.Clamp(d.collisionRadarVolume, 0f, 2f);
            d.collisionRadarRange = Mathf.Clamp(Mathf.Round(d.collisionRadarRange), 2f, 6f);
            d.scannerVolume = Mathf.Clamp(d.scannerVolume, 0f, 2f);
            d.hotbarWheelHoldMs = Mathf.Clamp(d.hotbarWheelHoldMs, 0f, 300f);
            _d = d;
        }

        private static void Save()
        {
            try
            {
                string path = FilePath();
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.WriteAllText(path, JsonUtility.ToJson(_d, true));
            }
            catch (System.Exception ex) { Diag.Error("A11ySettings", ex); }
        }
    }
}
