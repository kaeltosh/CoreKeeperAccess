using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Settings;
using Unity.Mathematics;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Detecteur de collision directionnel (etage 3 de la navigation) : une sonde a 360 degres
    // dans l'axe ou l'on POUSSE le stick gauche (pas la visee), portee reglable 2-6 cases, qui
    // alerte AVANT d'atteindre un infranchissable (mur, pit, eau). Meme technique DDA que la
    // canne laser (LaserCane/LaserCaneSystem), mais suit l'INTENTION DE MARCHE
    // (MovementReader.RawIntent, la MEME source que le bip de pas) au lieu de la visee ->
    // respecte AUTOMATIQUEMENT le snap directionnel (DirectionAssist reecrit movementDirection
    // AVANT que le jeu en deduise cette vitesse cible) : stick en diagonale + snap actif = la
    // sonde suit deja le cardinal verrouille, sans code dedie.
    //
    // Feedback : UNE nappe de bruit blanc filtre aigu (pre-emphase qui attenue le grave, PAS de
    // pan - la direction est deja celle ou l'on marche, l'info utile est la PROXIMITE) dont le
    // volume suit une courbe EXPONENTIELLE normalisee sur la portee reglee (ratio proximite =
    // (portee-distance)/(portee-1), 0 au bord de portee, 1 au contact) : montee lente au loin,
    // franche en se rapprochant. Rien dans la portee ou stick au neutre -> silence (Stop, meme
    // raison que ProximitySonar : ne pas monopoliser une voix audio pour rien).
    internal static class CollisionRadar
    {
        private const float StickDeadzone = 0.15f;
        // ~20 Hz, ALIGNE SUR LA CANNE LASER (pas le sonar de proximite a 10 Hz) : le retour
        // ici est un volume CONTINU qui doit sembler fluide a l'approche, la meme reactivite
        // que le point d'impact du laser - une cadence plus lente rendait les paliers de
        // volume perceptibles (retour signale trop "en escalier" a l'oreille, 2 juillet).
        private const float ScanInterval = 0.05f;

        private const int Rate = 44100;
        private static readonly int LoopLen = Rate * 2;   // boucle de 2 s (candidat valide a l'oreille)
        private const float TargetRms = 0.15f;             // meme cible que le sonar de proximite
        private const float PreEmphasis = 0.95f;           // attenue le grave -> aigu (valide a l'oreille)
        // Steepness de la courbe exponentielle de volume. A 9, les premieres cases d'une
        // portee de 6 sortaient a 7-17 % du volume max (quasi inaudibles) - tempere a 3
        // (2 juillet, retour utilisateur) : encore une montee qui s'accelere en approchant,
        // mais un signal audible des l'entree en portee.
        private const float ExpCurve = 3f;

        private static AudioSource _src;
        private static bool _init;
        private static float _nextScan;

        public static void Tick(PlayerController player)
        {
            if (_previewing) return;
            if (SettingsMenu.Active || SoundGuide.Active) { Stop(); return; }
            if (!InputContext.InWorld || InputContext.MenuOpen) { Stop(); return; }
            if (!A11ySettings.CollisionRadar || player == null) { Stop(); return; }

            Vector3 intent = MovementReader.RawIntent(player);
            float2 dirRaw = new float2(intent.x, intent.z);
            float mag = math.length(dirRaw);
            if (mag < StickDeadzone) { Stop(); return; }
            float2 dir = dirRaw / mag;
            float range = A11ySettings.CollisionRadarRange;

            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + ScanInterval;
                CollisionScan.Center = new int2(Mathf.RoundToInt(player.WorldPosition.x),
                                                 Mathf.RoundToInt(player.WorldPosition.z));
                CollisionScan.Direction = dir;
                CollisionScan.MaxRange = range;
                CollisionScan.OnWater = PlayerRide.OnBoat(player);
                CollisionScan.Requested = true;
            }

            if (!CollisionScan.ResultValid) return;
            ApplyVolume(CollisionScan.Found, CollisionScan.Dist, range);
        }

        // Plancher de volume TEST (2 juillet, a l'essai) : la 1re case de portee ne descend
        // plus pres de 0, meme a portee 6 - a garder ou retirer selon le retour en jeu.
        private const float MinCurve = 0.2f;

        // Volume exponentiel normalise sur la portee reglee : t=0 au bord de portee, t=1 au
        // contact. La forme (courbe) est constante, mais l'echelle de distance qu'elle couvre
        // suit la portee choisie au panneau (2..6).
        private static void ApplyVolume(bool found, float dist, float range)
        {
            EnsureInit();
            if (_src == null) return;
            if (!found) { if (_src.isPlaying) _src.Stop(); return; }

            float t = range > 1f ? Mathf.Clamp01((range - dist) / (range - 1f)) : 1f;
            float curve = (Mathf.Pow(ExpCurve, t) - 1f) / (ExpCurve - 1f);
            curve = MinCurve + (1f - MinCurve) * curve;
            float vol = curve * A11ySettings.CollisionRadarVolume * A11ySettings.MasterVolume;

            if (vol <= 0f) { if (_src.isPlaying) _src.Stop(); return; }
            if (!_src.isPlaying) _src.Play();
            _src.volume = Mathf.Clamp01(vol);
        }

        public static void Stop()
        {
            if (_src != null && _src.isPlaying) _src.Stop();
        }

        // --- Apercu (panneau de reglages) : nappe tenue au volume actuellement regle. ---
        private static bool _previewing;

        public static void StartPreview()
        {
            EnsureInit();
            if (_src == null) return;
            _previewing = true;
            if (!_src.isPlaying) _src.Play();
            _src.volume = Mathf.Clamp01(A11ySettings.CollisionRadarVolume * A11ySettings.MasterVolume);
        }

        public static void StopPreview()
        {
            _previewing = false;
            Stop();
        }

        // --- Construction de la nappe (bruit blanc pre-accentue aigu) au 1er besoin ---
        private static void EnsureInit()
        {
            if (_init) return;
            _init = true;

            var rng = new System.Random(20260702);
            int len = LoopLen, xf = Rate / 20;   // fondu de boucle 50 ms
            float[] white = BuildWhite(len + xf, rng);
            float[] bright = PreEmph(white, PreEmphasis);
            float[] loop = Finalize(bright, len, xf);

            var clip = GameplayAudio.BakePan("A11yCollisionRadar", loop, 0f);   // centre : pas de pan
            _src = GameplayAudio.CreateModSource(true);
            if (_src != null) _src.clip = clip;
        }

        private static float[] BuildWhite(int len, System.Random rng)
        {
            var d = new float[len];
            for (int i = 0; i < len; i++) d[i] = (float)(rng.NextDouble() * 2.0 - 1.0);
            return d;
        }

        // Pre-emphase (filtre a un zero) : y[i] = x[i] - k*x[i-1], attenue le grave et laisse
        // dominer l'aigu. Distinct du filtrage PASSE-BAS du sonar de proximite (grave/medium) :
        // ici on veut l'oppose, un timbre clair.
        private static float[] PreEmph(float[] x, float k)
        {
            var y = new float[x.Length];
            float prev = 0f;
            for (int i = 0; i < x.Length; i++) { y[i] = x[i] - k * prev; prev = x[i]; }
            return y;
        }

        // Normalisation RMS + extraction d'une boucle sans clic (fondu enchaine queue->tete),
        // meme methode que ProximitySonar.Finalize.
        private static float[] Finalize(float[] x, int len, int xf)
        {
            double sum = 0; for (int i = 0; i < len; i++) sum += x[i] * x[i];
            float rms = (float)System.Math.Sqrt(sum / len);
            float g = rms > 1e-6f ? TargetRms / rms : 1f;
            var loop = new float[len];
            for (int i = 0; i < len; i++) loop[i] = x[i] * g;
            for (int i = 0; i < xf; i++) { float w = (float)i / xf; loop[i] = (x[i] * w + x[len + i] * (1f - w)) * g; }
            return loop;
        }
    }
}
