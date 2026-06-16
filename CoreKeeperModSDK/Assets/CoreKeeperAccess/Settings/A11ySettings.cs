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
            // Volume maitre des SONS du mod (bips, marqueurs, tons...), 0..1. Le TTS n'est
            // pas concerne (NVDA a son propre volume).
            public float masterVolume = 1f;
            // Volume du bip de pas (la boussole de locomotion, ex-tic directionnel).
            public float directionTickVolume = 0.125f;
            // Bip de pas : boussole de locomotion permanente (un bip par case franchie,
            // direction encodee en pan/pitch). DECOUPLE du snap le 16 juin -> actif par
            // defaut (c'est devenu la boussole de l'utilisateur), regle au panneau.
            public bool stepBeep = true;
            // Snap directionnel (marche forcee au cardinal pour poser en rangs). Aussi
            // basculee par Triangle+L3 : meme source de verite, donc persistee. PONCTUEL,
            // donc inactif par defaut.
            public bool snapDirectional = false;
            // Ralenti automatique quand des ennemis chassent (CombatSlowMotion).
            public bool combatSlowMo = true;
            // Normalisation RMS des sons natifs du jeu (egalise des masterisations tres
            // inegales). Off = sons bruts.
            public bool normalizeAudio = true;
        }

        private static Data _d = new Data();

        public static float MasterVolume => _d.masterVolume;
        public static float DirectionTickVolume => _d.directionTickVolume;
        public static bool StepBeep => _d.stepBeep;

        // Snap : source de verite partagee entre Triangle+L3 (DirectionAssist) et le
        // panneau. Le set persiste -> l'etat survit au relancement.
        public static bool SnapDirectional
        {
            get => _d.snapDirectional;
            set { _d.snapDirectional = value; Save(); }
        }

        public static bool CombatSlowMo => _d.combatSlowMo;
        public static bool NormalizeAudio => _d.normalizeAudio;

        // Mutateurs du panneau de reglages : clamp + sauvegarde immediate.
        public static void SetMasterVolume(float v) { _d.masterVolume = Mathf.Clamp01(v); Save(); }
        public static void SetDirectionTickVolume(float v) { _d.directionTickVolume = Mathf.Clamp01(v); Save(); }
        public static void SetStepBeep(bool v) { _d.stepBeep = v; Save(); }
        public static void SetSnapDirectional(bool v) { _d.snapDirectional = v; Save(); }
        public static void SetCombatSlowMo(bool v) { _d.combatSlowMo = v; Save(); }
        public static void SetNormalizeAudio(bool v) { _d.normalizeAudio = v; Save(); }

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
            d.masterVolume = Mathf.Clamp01(d.masterVolume);
            d.directionTickVolume = Mathf.Clamp01(d.directionTickVolume);
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
