using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Settings;
using Unity.Mathematics;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Sonar de proximite (etage 2 de la navigation) : des nappes de BRUIT FILTRE en boucle
    // signalent les obstacles autour du joueur, sans passer en mode curseur (le laser est
    // inefficace en zone confinee). Texture de bruit (et NON un ton) -> categorie perceptive
    // distincte des earcons tonaux du mod (segregation de flux par timbre : le cerveau range
    // le bruit en "ambiance" et garde les tons comme "evenements").
    //
    // 8 directions LOCKEES, encodage coherent avec le reste du mod :
    //   - axe est-ouest -> le PAN (cuit en stereo via GameplayAudio.BakePan : gauche / centre
    //     / droite), un canal a zero ne peut pas fuir dans l'oreille opposee ;
    //   - axe nord-sud  -> la COULEUR du filtre (clair = nord, neutre = est/ouest, sombre =
    //     sud), parce qu'un bruit n'a pas de hauteur tonale a decaler.
    // Deux textures : mur = mat (couleur brute), trou/eau = clapotis (amplitude modulee facon
    // vague). Une AudioSource en boucle par direction ; le volume code la distance.
    //
    // Coefficients (filtres, clapotis, adoucissement, trim du nord, normalisation RMS) = ceux
    // valides a l'oreille sur les previews hors-jeu (tools/gen_preview_sons.ps1).
    //
    // ETAT : Tick = smoke test (les 8 directions sonnent en 'mur' a bas volume quand le sonar
    // est actif) pour valider en jeu le bruit + la spatialisation + le contraste nord/sud.
    // L'increment suivant remplace ce stub par le SCAN reel : ne sonner que sur les directions
    // murees, volume = distance (<= 2 cases), texture mur (bloqueur dur) vs trou (pit/eau).
    internal static class ProximitySonar
    {
        private const int Rate = 44100;
        private static readonly int LoopLen = Rate * 4;     // boucle de 4 s
        private const float TargetRms = 0.15f;              // loudness commune des timbres
        private const float GravePitch = 0.5f;              // sud = bruit pitche 1 octave plus bas (vrai grave)

        // 4 directions cardinales (x=est, y=nord). Reduit de 8 a 4 (16 juin) : moins d'infos
        // simultanees. N et S au CENTRE (differenciees par la couleur), E et O PANNEES (timbre
        // neutre). L'equilibre de volume entre couleurs se regle au panneau (ColorVolume).
        private static readonly int[] Dx = { 0, 1, 0, -1 };
        private static readonly int[] Dy = { 1, 0, -1, 0 };

        private static AudioSource[] _src;      // 8, une par direction, en boucle
        private static AudioClip[,] _clips;     // [direction, texture] : 0=mur, 1=trou
        private static bool _init;

        private const float ScanInterval = 0.1f;   // ~10 Hz : assez frais pour la marche
        private const float NearVol = 0.3f;         // mur a 1 case (volume de base, regle par le panneau)
        private const float FarVol = 0.15f;         // mur a 2 cases
        // Couche OBJETS (v2) : ding "objet" de la nav, rejoue a CHAQUE case franchie tant qu'un
        // objet est dans la direction (<= 2 cases). Pitche grave quand l'objet est au sud.
        private const SfxID ObjectDingSfx = SfxID.inventory_ding;
        private const float DingNearVol = 0.6f;     // objet a 1 case
        private const float DingFarVol = 0.35f;     // objet a 2 cases
        private static int _cx, _cz;                 // suivi de case pour declencher au pas
        private static bool _hasCell;
        private static float _nextScan;

        // --- Pilotage (appele chaque frame depuis GameplayInput.Tick) ---
        // On pose une demande de scan a intervalle (les nappes tiennent entre deux scans) et on
        // applique le dernier resultat : une nappe de bruit par direction muree, volume =
        // distance. Espace ouvert = 4 directions libres = silence total.
        public static void Tick(PlayerController player)
        {
            // Apercu en cours (panneau) : NE PAS toucher aux sources, sinon le Tick couperait
            // la nappe d'apercu des la frame suivante (sonar desactive ou panneau ouvert font
            // tous deux un Stop ci-dessous). StopPreview les coupera a la fin de la fenetre.
            if (_previewing) return;
            // Panneau de reglages ouvert (hors apercu) : sonar EN PAUSE (le joueur ne bouge pas,
            // l'input est gele) -> pas de nappe parasite pendant qu'on regle.
            if (SettingsMenu.Active || SoundGuide.Active) { Stop(); return; }
            // Menu principal (pas encore en jeu) ou menu pause (monde fige) : sonar EN PAUSE.
            if (!InputContext.InWorld || InputContext.MenuOpen) { Stop(); _hasCell = false; return; }
            if (!A11ySettings.ProximitySonar || player == null) { Stop(); _hasCell = false; return; }
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + ScanInterval;
                SonarScan.Center = new int2(Mathf.RoundToInt(player.WorldPosition.x),
                                            Mathf.RoundToInt(player.WorldPosition.z));
                SonarScan.Requested = true;
            }
            // Case franchie (le pas) : declenche le ding des objets a chaque nouvelle case.
            int cx = Mathf.RoundToInt(player.WorldPosition.x), cz = Mathf.RoundToInt(player.WorldPosition.z);
            bool stepped = _hasCell && (cx != _cx || cz != _cz);
            _cx = cx; _cz = cz; _hasCell = true;

            if (SonarScan.ResultValid)
            {
                for (int d = 0; d < 4; d++)
                {
                    // Couche MURS : nappe de bruit continue, volume = distance.
                    float dist = SonarScan.Dist[d] == 1 ? NearVol : (SonarScan.Dist[d] == 2 ? FarVol : 0f);
                    SetLayer(d, SonarScan.Tex[d], dist * ColorVolume(d));

                    // Couche OBJETS (v2) : ding REJOUE a chaque case franchie tant qu'un objet est
                    // present dans la direction (pan est/ouest + volume = distance). Un seul ding.
                    if (stepped && SonarScan.Obj[d] && A11ySettings.ObjectDing)
                    {
                        float od = SonarScan.ObjDist[d] == 1 ? DingNearVol : DingFarVol;
                        float pitch = Dy[d] < 0 ? GravePitch : 1f;   // objet au sud = ding grave
                        GameplayAudio.PlaySpatial(ObjectDingSfx, Dx[d], pitch, od * A11ySettings.SonarVolume);
                    }
                }
            }
        }

        // Volume INDEPENDANT par couleur (axe nord-sud), reglable au panneau pour equilibrer a
        // l'oreille : le sud (sombre/grave) est percu bien plus faible que le nord/neutre a
        // niveau electrique egal (equi-sonie) -> defauts pre-compenses (sud remonte), ajustables.
        private static float ColorVolume(int d)
        {
            // v1 : grave pour le sud, medium pour nord/est/ouest.
            return Dy[d] < 0 ? A11ySettings.SonarVolGrave : A11ySettings.SonarVolMedium;
        }

        // Pilote une nappe directionnelle. tex : 0=silence, 1=mur, 2=trou. vol 0..1 (la
        // distance) ; module ensuite par le volume sonar et le volume maitre.
        public static void SetLayer(int dir, int tex, float vol)
        {
            EnsureInit();
            if (_src == null || dir < 0 || dir >= 4) return;
            var s = _src[dir];
            if (s == null) return;
            // Direction libre : on COUPE la source (Stop) pour liberer la voix audio - sinon 8
            // nappes en boucle a volume 0 monopolisent des voix et peuvent voler celles des
            // autres sons du mod/jeu (curseur, laser, pas).
            if (tex <= 0 || vol <= 0f) { if (s.isPlaying) s.Stop(); return; }
            var clip = _clips[dir, tex - 1];
            if (s.clip != clip) { s.clip = clip; s.Play(); }
            else if (!s.isPlaying) s.Play();
            s.volume = Mathf.Clamp01(vol) * A11ySettings.SonarVolume * A11ySettings.MasterVolume;
        }

        public static void Stop()
        {
            if (_src == null) return;
            for (int d = 0; d < 4; d++) if (_src[d] != null && _src[d].isPlaying) _src[d].Stop();
        }

        // --- Apercu (panneau de reglages) ---
        // Joue une nappe au volume actuellement regle. which : 0 = medium (avant/cotes, nord
        // centre), 1 = grave (arriere, sud centre), 2 = les deux (volume general). Le drapeau
        // _previewing empeche Tick de couper la nappe ; StopPreview() la coupe en fin de fenetre.
        private static bool _previewing;
        public static void StartPreview(int which)
        {
            EnsureInit();
            _previewing = true;
            if (which == 0 || which == 2) SetLayer(0, 1, NearVol * A11ySettings.SonarVolMedium);
            if (which == 1 || which == 2) SetLayer(2, 1, NearVol * A11ySettings.SonarVolGrave);
        }

        // Apercu d'un GOUFFRE (clapotis) au lieu d'un mur : texture trou (tex=2) sur la couche
        // medium centree. Pour le menu d'apprentissage des sons (SoundGuide).
        public static void StartPreviewPit()
        {
            EnsureInit();
            _previewing = true;
            SetLayer(0, 2, NearVol * A11ySettings.SonarVolMedium);
        }

        public static void StopPreview() { _previewing = false; Stop(); }

        // Apercu du ding d'objet (couche objets) au centre, au volume regle.
        public static void PreviewDing()
            => GameplayAudio.PlaySpatial(ObjectDingSfx, 0f, 1f, DingNearVol * A11ySettings.SonarVolume);

        // --- Construction des nappes (4 directions x mur/trou) au 1er besoin ---
        private static void EnsureInit()
        {
            if (_init) return;
            _init = true;

            var rng = new System.Random(20260616);
            int len = LoopLen, xf = Rate / 20;     // fondu de boucle 50 ms
            float[] pink = BuildPink(len + xf, rng);
            // Bruit FILTRE (NE PAS toucher, valide en jeu) : medium (neutre) pour nord/est/ouest,
            // grave (sombre) pour le sud. Timbres DISTINCTS par filtrage. mur = mat, trou = clapotis.
            float[] medium = LowPass(pink, 0.25f);
            float[] grave = LowPass(LowPass(pink, 0.10f), 0.25f);
            float[] mediumMur = Finalize(medium, false, len, xf, 1f);
            float[] mediumTrou = Finalize(medium, true, len, xf, 1f);
            float[] graveMur = Finalize(grave, false, len, xf, 1f);
            float[] graveTrou = Finalize(grave, true, len, xf, 1f);

            _clips = new AudioClip[4, 2];
            _src = new AudioSource[4];
            for (int d = 0; d < 4; d++)
            {
                bool sud = Dy[d] < 0;
                float[] mur = sud ? graveMur : mediumMur;
                float[] trou = sud ? graveTrou : mediumTrou;
                float pan = Dx[d]; // -1 ouest, 0 centre, +1 est (cuit en stereo)
                _clips[d, 0] = GameplayAudio.BakePan("A11ySonar" + d + "Mur", mur, pan);
                _clips[d, 1] = GameplayAudio.BakePan("A11ySonar" + d + "Trou", trou, pan);
                _src[d] = GameplayAudio.CreateModSource(true);
            }
        }

        // === Generation du bruit (replique exacte des previews) ===

        // Bruit ROSE (filtre de Paul Kellet, ~-3 dB/octave) : repose l'oreille vs le blanc.
        private static float[] BuildPink(int len, System.Random rng)
        {
            var d = new float[len];
            double b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;
            for (int i = 0; i < len; i++)
            {
                double w = rng.NextDouble() * 2.0 - 1.0;
                b0 = 0.99886 * b0 + w * 0.0555179; b1 = 0.99332 * b1 + w * 0.0750759;
                b2 = 0.96900 * b2 + w * 0.1538520; b3 = 0.86650 * b3 + w * 0.3104856;
                b4 = 0.55000 * b4 + w * 0.5329522; b5 = -0.7616 * b5 - w * 0.0168980;
                d[i] = (float)(b0 + b1 + b2 + b3 + b4 + b5 + b6 + w * 0.5362);
                b6 = w * 0.115926;
            }
            return d;
        }

        private static float[] LowPass(float[] x, float a)
        {
            var y = new float[x.Length]; double p = 0;
            for (int i = 0; i < x.Length; i++) { p += a * (x[i] - p); y[i] = (float)p; }
            return y;
        }

        // Clapotis (trou/eau) : modulation d'amplitude lente irreguliere. Frequences calees sur
        // des multiples de 1/4 s (0,75 et 2,25 Hz) pour boucler proprement sur 4 s.
        private static float[] Clapotis(float[] x)
        {
            var y = new float[x.Length];
            for (int i = 0; i < x.Length; i++)
            {
                double t = i / (double)Rate;
                double m = 0.5 + 0.5 * (0.6 * System.Math.Sin(2 * System.Math.PI * 2.25 * t)
                                      + 0.4 * System.Math.Sin(2 * System.Math.PI * 0.75 * t));
                if (m < 0) m = 0; else if (m > 1) m = 1;
                y[i] = (float)(x[i] * m);
            }
            return y;
        }

        // Finalise un timbre mono : clapotis optionnel, normalisation RMS commune, trim
        // perceptif, et extraction d'une BOUCLE sans clic (fondu enchaine de la queue dans la
        // tete -> le raccord loop[fin]->loop[debut] devient continu).
        private static float[] Finalize(float[] color, bool clapotis, int len, int xf, float trim)
        {
            float[] x = clapotis ? Clapotis(color) : color;   // longueur = len + xf
            double sum = 0; for (int i = 0; i < len; i++) sum += x[i] * x[i];
            float rms = (float)System.Math.Sqrt(sum / len);
            float g = (rms > 1e-6f ? TargetRms / rms : 1f) * trim;
            var loop = new float[len];
            for (int i = 0; i < len; i++) loop[i] = x[i] * g;
            for (int i = 0; i < xf; i++) { float w = (float)i / xf; loop[i] = (x[i] * w + x[len + i] * (1f - w)) * g; }
            return loop;
        }
    }
}
