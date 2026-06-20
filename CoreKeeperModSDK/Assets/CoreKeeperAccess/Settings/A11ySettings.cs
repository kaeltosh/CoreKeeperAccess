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
            // Volume maitre des SONS du mod (bips, marqueurs, tons...), 0..2 (defaut 1 = niveau
            // de reference, jusqu'a 2 = +6 dB). Le TTS n'est pas concerne (NVDA a son volume).
            public float masterVolume = 1f;
            // Volume du bip de pas (la boussole de locomotion, ex-tic directionnel).
            public float directionTickVolume = 0.125f;
            // Bip de pas : boussole de locomotion permanente (un bip par case franchie,
            // direction encodee en pan/pitch). DECOUPLE du snap le 16 juin -> actif par
            // defaut (c'est devenu la boussole de l'utilisateur), regle au panneau.
            public bool stepBeep = true;
            // Volume du bloc NAVIGATION (canne laser + curseur de tuile : tick, surfaces,
            // materiaux, marqueurs, bips de cibles). 0..2 (defaut 1 = niveau de reference, deja
            // bon grace a la cible de normalisation TargetPeak ; jusqu'a 2 = +6 dB pour qui veut
            // la nav plus forte). Sans risque de clip : a 200 %, le pic d'un son reste
            // <= base x TargetPeak x 2 < 1. S'applique en plus du volume maitre.
            public float navigationVolume = 1f;
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
            public bool proximitySonar = false;
            // Volume GENERAL du sonar de proximite, 0..2. Defaut = equilibre valide en jeu (16 juin).
            public float sonarVolume = 0.9f;
            // Volumes INDEPENDANTS des 2 timbres du sonar (v1), pour equilibrer a l'oreille : le
            // grave est percu plus faible que le medium a niveau egal (equi-sonie). 0..2. Defauts
            // = equilibre VALIDE en jeu par l'utilisateur (16 juin).
            public float sonarVolMedium = 0.7f;  // medium (neutre) : nord, est, ouest
            public float sonarVolGrave = 2f;     // grave (sombre) : sud (pousse au max a l'oreille)
            // Couche OBJETS du sonar (le ding sur les objets poses), togglable a part du bruit.
            public bool objectDing = true;
            // Annonce vocale du nom des objets ramasses (a CHAQUE ramassage, pas seulement au
            // premier comme le jeu). Actif par defaut.
            public bool pickupAnnounce = true;
            // Filtre les BLOCS (tag TileBlock : terre, pierre, murs posables) hors de l'annonce
            // de ramassage, pour calmer le bruit en minant. Inactif par defaut (on entend tout).
            public bool pickupFilterBlocks = false;
            // Suffixe la quantite TOTALE possedee a l'annonce de ramassage ("47 en tout").
            // Inactif par defaut (plus verbeux).
            public bool pickupTotal = false;
            // Earcons d'alerte a l'apparition d'un etat subi (DoT feu/acide/radiation, stun).
            // Une alerte sonore au front montant, cf. ConditionAlerts. Actif par defaut.
            public bool conditionEarcons = true;
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
        public static bool ConditionEarcons => _d.conditionEarcons;

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
        public static void SetConditionEarcons(bool v) { _d.conditionEarcons = v; Save(); }

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
