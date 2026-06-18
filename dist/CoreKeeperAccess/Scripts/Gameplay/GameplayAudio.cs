using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Audio;

namespace CoreKeeperAccess.Gameplay
{
    // Couche audio a11y. Joue un son NATIF du jeu (par SfxID) sur NOTRE propre
    // AudioSource, ce qui donne le controle fin que SfxUI ne permet pas :
    //  - pan stereo (-1 gauche .. +1 droite),
    //  - pitch libre (ex. 1 demi-ton par ligne).
    // On recupere l'AudioClip du SfxID via le audioFieldMap interne de l'AudioManager
    // (reflection) -> GetNextAudioClip(). Aucun fichier embarque.
    internal static class GameplayAudio
    {
        private const int PoolSize = 8;
        private static AudioSource[] _pool;
        private static int _poolIdx;
        private static AudioField[] _fields;
        private static AudioMixerGroup _mixerGroup;
        private static GameObject _audioGo;
        private static bool _init;

        // Joue le son a la hauteur (pitch) et au panoramique (pan, -1..+1) donnes.
        public static void PlaySpatial(SfxID id, float pan, float pitch, float volume = 1f)
        {
            EnsureInit();
            if (_pool == null || _fields == null) return;

            int idx = (int)id;
            if (idx < 0 || idx >= _fields.Length || _fields[idx] == null) return;
            var clip = _fields[idx].GetNextAudioClip();
            if (clip == null) return;

            // Source dediee prise dans un pool round-robin. Le pitch est une propriete de
            // l'AudioSource, partagee par TOUS les PlayOneShot en cours dessus : deux sons
            // simultanes (ex. tick porteur a pitch vertical + marqueur a pitch fixe) sur la
            // MEME source -> le 2e pitch ecrase celui du 1er encore en train de jouer. Une
            // source par son => pitchs independants.
            float gain = NormalizeGain(idx, ref clip);

            // panStereo (et pas le filtre custom) : OnAudioFilterRead recoit les clips
            // MONO sur un seul canal (l'upmix stereo arrive APRES le filtre) -> un
            // filtre de pan custom ne peut pas panner nos bips mono (essaye build 66,
            // tuait la stereo). Le residu d'oreille opposee constate a pan plein est
            // a chercher HORS du jeu (virtualisation Windows), pas dans cette loi.
            var src = _pool[_poolIdx];
            _poolIdx = (_poolIdx + 1) % PoolSize;
            src.panStereo = Mathf.Clamp(pan, -1f, 1f);
            src.pitch = Mathf.Clamp(pitch, 0.05f, 4f);
            src.PlayOneShot(clip, volume * gain * A11ySettings.MasterVolume);
        }

        private static AudioSource _beaconSource;

        // Earcon de guidage REPETE (beacon de navigation) : source DEDIEE qu'on COUPE et
        // relance a chaque ping. Ainsi la CADENCE de repetition gouverne le rythme percu -
        // la duree / la traine du clip ne deborde JAMAIS sur le ping suivant (demande
        // utilisateur : la duree du son ne doit pas affecter la frequence de repetition).
        // Un seul beacon a la fois -> une source unique suffit, pitch/pan libres a chaque ping.
        public static void PlayBeacon(SfxID id, float pan, float pitch, float volume = 1f)
        {
            EnsureInit();
            if (_beaconSource == null || _fields == null) return;
            int idx = (int)id;
            if (idx < 0 || idx >= _fields.Length || _fields[idx] == null) return;
            var clip = _fields[idx].GetNextAudioClip();
            if (clip == null) return;
            float gain = NormalizeGain(idx, ref clip);
            _beaconSource.Stop(); // coupe le ping precedent : la cadence decoupe le son
            _beaconSource.clip = clip;
            _beaconSource.panStereo = Mathf.Clamp(pan, -1f, 1f);
            _beaconSource.pitch = Mathf.Clamp(pitch, 0.05f, 4f);
            _beaconSource.volume = Mathf.Clamp01(volume * gain * A11ySettings.MasterVolume);
            _beaconSource.Play();
        }

        // Pan commun a tous les sons positionnels du mod (12 juin, choix utilisateur) :
        // BAREME FIXE EN CASES, lineaire - 1/PanRangeTiles par case d'ecart, 100 %
        // (= extinction totale de l'oreille opposee, vrai hard pan Unity sur clip
        // mono) atteint a PanRangeTiles. Independant du zoom/largeur d'ecran
        // (contrairement aux anciennes normalisations par demi-ecran). 12 cases =
        // la portee du laser, le curseur va rarement plus loin.
        private const float PanRangeTiles = 12f;

        public static float PanFromTiles(float dxTiles)
            => Mathf.Clamp(dxTiles / PanRangeTiles, -1f, 1f);

        // Attenuation par la distance : loi COMMUNE a tous les sons positionnels du mod
        // (celle de la sentinelle d'aggro historique). Lineaire jusqu'a 30 cases,
        // plancher a 15 % - jamais muet, l'info de presence reste.
        public static float DistanceTrim(float distTiles)
            => Mathf.Clamp(1f - distTiles / 30f, 0.15f, 1f);

        // --- Normalisation de volume (peak SYMETRIQUE, 16 juin 2026) ---
        // On amene chaque SfxID joue par le mod a une meme cible de CRETE (peak) : on ATTENUE
        // les sons au-dessus, on AMPLIFIE (make-up gain) ceux en dessous. Resultat : tout son
        // sort a "volume_de_base x TargetPeak" quel que soit son mastering d'origine -> les
        // volumes de base deviennent un vrai mix fiable, plus tributaire de comment Pugstorm a
        // masterise tel son. On juge a la CRETE (pas a un RMS) car c'est le claquement que
        // l'oreille percoit : un "plouf" d'eau claque a -0,6 dB mais son RMS sur 100 ms tombe
        // a -16,9 dB -> avec un trim RMS il echappait au rabotage et dominait tout (ancien
        // bug). Le make-up est PLAFONNE (MakeupMax) pour ne pas remonter a l'infini le souffle
        // d'un son masterise tres bas. Les VRAIS stereo (image G/D qui fausserait notre pan)
        // sont replies en mono en RAM (gain cuit dans les echantillons). Mesure a la PREMIERE
        // lecture puis cache. Les tons generes (PlayTone/PlayBossTone) ne sont PAS normalises
        // (amplitude maitrisee par construction). Limites assumees : le peak ignore la DUREE
        // (un son long reste un poil plus present a peak egal) ; gain mesure sur la 1re variante.
        private const float TargetPeak = 0.5f;   // -6 dBFS : cible de CRETE commune a tous les sons normalises
        private const float MakeupMax = 4f;       // +12 dB max : plafond d'amplification (anti-souffle)
        private const float RetryDelay = 3f;      // GetData peut refuser un clip pas encore charge : on retente
        private static readonly Dictionary<int, float> _gain = new Dictionary<int, float>();
        private static readonly Dictionary<int, AudioClip> _rebuilt = new Dictionary<int, AudioClip>();
        private static readonly Dictionary<int, float> _retryAt = new Dictionary<int, float>();

        // Gain de normalisation, et substitution eventuelle du clip par sa version RAM.
        private static float NormalizeGain(int idx, ref AudioClip clip)
        {
            // Normalisation desactivee par l'utilisateur : son brut (gain 1, clip d'origine).
            if (!A11ySettings.NormalizeAudio) return 1f;
            AudioClip ram;
            if (_rebuilt.TryGetValue(idx, out ram))
            {
                if (ram != null) clip = ram;
                return 1f; // correction deja cuite dans les echantillons
            }
            float g;
            if (_gain.TryGetValue(idx, out g)) return g;
            if (InRetryWindow(idx)) return 1f;
            return Measure(idx, ref clip);
        }

        // Variante pour la voie NATIVE (sons de table) : pas de substitution de clip possible
        // (le jeu joue les siens), donc gain de lecture seul. NB : le jeu clampe le volume
        // natif a 1, donc un make-up > 1 sur un son natif tres faible est partiellement ecrete
        // (sans incidence : les materiaux de mur claquent deja a ~0 dBFS -> ils sont attenues).
        private static float NativeGain(SfxID id)
        {
            if (!A11ySettings.NormalizeAudio) return 1f;
            int idx = (int)id;
            float g;
            if (_gain.TryGetValue(idx, out g)) return g;
            if (_rebuilt.ContainsKey(idx)) return 1f;
            if (InRetryWindow(idx)) return 1f;
            if (_fields == null || idx < 0 || idx >= _fields.Length || _fields[idx] == null) return 1f;
            var clip = _fields[idx].GetNextAudioClip();
            if (clip == null) return 1f;
            return Measure(idx, ref clip);
        }

        private static bool InRetryWindow(int idx)
        {
            float t;
            return _retryAt.TryGetValue(idx, out t) && Time.unscaledTime < t;
        }

        // RMS lineaire -> dBFS lisible dans les logs.
        private static string Fmt(float rms)
            => (20f * Mathf.Log10(Mathf.Max(rms, 1e-6f))).ToString("0.0") + "dB";

        private static float Measure(int idx, ref AudioClip clip)
        {
            try
            {
                // GetData ne marche que sur les clips decompresses en RAM ; un clip
                // streame/compresse reste tel quel (gain neutre) - TRACE pour savoir
                // quelle part de la flotte echappe a la normalisation.
                if (clip.loadType != AudioClipLoadType.DecompressOnLoad || clip.samples <= 0)
                {
                    Diag.Log("A11yAudioNorm", "non mesurable " + clip.name + " loadType=" + clip.loadType);
                    _gain[idx] = 1f; return 1f;
                }

                int ch = clip.channels;
                var data = new float[clip.samples * ch];
                if (!clip.GetData(data, 0))
                {
                    // PAS de cache definitif : le clip n'est peut-etre simplement pas
                    // encore charge (vu sur proximity_sensor_set au boot, build 60).
                    Diag.Log("A11yAudioNorm", "GetData refuse " + clip.name + ", retry dans " + RetryDelay + "s");
                    _retryAt[idx] = Time.unscaledTime + RetryDelay;
                    return 1f;
                }
                _retryAt.Remove(idx);

                // On mesure la CRETE (peak) qui pilote le gain, plus un RMS global (log
                // seulement) et l'energie par canal (detection vrai stereo).
                double sumSq = 0, sumL = 0, sumR = 0;
                float peak = 0f;
                for (int i = 0; i < data.Length; i++)
                {
                    float v = data[i];
                    sumSq += v * v;
                    float a = v < 0f ? -v : v;
                    if (a > peak) peak = a;
                    if (ch == 2) { if ((i & 1) == 0) sumL += v * v; else sumR += v * v; }
                }
                if (peak < 1e-6f) { _gain[idx] = 1f; return 1f; }
                float rms = (float)System.Math.Sqrt(sumSq / data.Length);

                // Vrai stereo = plus de 1 dB d'ecart d'energie entre canaux (les
                // "faux stereo" du jeu, canaux identiques, passent sans reconstruction).
                bool trueStereo = false;
                if (ch == 2 && sumL > 0 && sumR > 0)
                {
                    double balDb = 10.0 * System.Math.Log10(sumL / sumR);
                    trueStereo = balDb > 1.0 || balDb < -1.0;
                }

                // Gain peak SYMETRIQUE : attenue (peak > cible) OU amplifie (peak < cible)
                // vers TargetPeak, make-up plafonne a MakeupMax. peak x g <= TargetPeak (< 1)
                // -> jamais de clipping, ni a la lecture ni dans un clip reconstruit.
                float g = Mathf.Min(TargetPeak / peak, MakeupMax);

                if (trueStereo)
                {
                    int frames = clip.samples;
                    var mono = new float[frames];
                    for (int f = 0; f < frames; f++)
                    {
                        float v = 0f;
                        for (int c = 0; c < ch; c++) v += data[f * ch + c];
                        mono[f] = Mathf.Clamp(v / ch * g, -1f, 1f);
                    }
                    var ram = AudioClip.Create(clip.name + "_a11yNorm", frames, 1, clip.frequency, false);
                    ram.SetData(mono, 0);
                    _rebuilt[idx] = ram;
                    _gain.Remove(idx);
                    Diag.Log("A11yAudioNorm", "rebuilt " + clip.name + " peak=" + Fmt(peak)
                        + " rms=" + Fmt(rms) + " gain=" + g.ToString("0.00") + " STEREO->mono");
                    clip = ram;
                    return 1f;
                }
                Diag.Log("A11yAudioNorm", "mesure " + clip.name + " peak=" + Fmt(peak) + " rms=" + Fmt(rms) + " gain=" + g.ToString("0.00"));
                _gain[idx] = g;
                return g;
            }
            catch (System.Exception ex)
            {
                Diag.Error("A11yAudioNorm", ex);
                _gain[idx] = 1f;
                return 1f;
            }
        }

        private static AudioSource _toneSource;
        private static AudioSource _droneL, _droneR;
        private static bool _droneOn;
        private static float[] _toneData;
        private static float[] _bossData;
        private static AudioClip[] _toneBank;
        private static AudioClip[] _bossBank;

        // Bip sinusoidal GENERE (aucun asset) : sinus a freqHz pendant ms, fondu
        // d'attaque/sortie de 5 ms contre les clics. Rend les ECHANTILLONS mono ;
        // le clip jouable est fabrique par BakePan (pan cuit en stereo).
        private static float[] BuildSineData(double freqHz, int ms)
        {
            const int rate = 44100;
            int len = rate * ms / 1000;
            const int fade = rate * 5 / 1000;   // 5 ms
            var data = new float[len];
            double w = 2.0 * System.Math.PI * freqHz / rate;
            for (int i = 0; i < len; i++)
            {
                float env = 1f;
                if (i < fade) env = i / (float)fade;
                else if (i >= len - fade) env = (len - 1 - i) / (float)fade;
                data[i] = (float)System.Math.Sin(w * i) * env;
            }
            return data;
        }

        // --- Pan CUIT dans les echantillons (build 69) ---
        // Le panStereo d'Unity s'est revele INOPERANT dans le contexte du jeu
        // (diag build 68 : bips forces a -1 et +1 quasi identiques a l'oreille
        // droite, alors que le meme test en WAV systeme est parfait). On ne pan
        // donc plus par propriete : chaque bip est un clip STEREO dont les gains
        // gauche/droite (puissance constante) sont ecrits dans les echantillons -
        // un canal a zero ne peut pas fuir. Banque par pas d'une case du bareme
        // (PanSteps = 2 x PanRangeTiles + 1), construite a la demande.
        private const int PanSteps = 25;

        public static AudioClip BakePan(string name, float[] mono, float pan)
        {
            float ang = (Mathf.Clamp(pan, -1f, 1f) + 1f) * Mathf.PI * 0.25f;
            float l = Mathf.Cos(ang), r = Mathf.Sin(ang);
            var data = new float[mono.Length * 2];
            for (int i = 0; i < mono.Length; i++)
            {
                data[2 * i] = mono[i] * l;
                data[2 * i + 1] = mono[i] * r;
            }
            var clip = AudioClip.Create(name, mono.Length, 2, 44100, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip PannedTone(ref AudioClip[] bank, float[] mono, string name, float pan)
        {
            if (bank == null) bank = new AudioClip[PanSteps];
            int idx = Mathf.RoundToInt((Mathf.Clamp(pan, -1f, 1f) + 1f) * 0.5f * (PanSteps - 1));
            if (bank[idx] == null)
                bank[idx] = BakePan(name + "_p" + idx, mono, idx / (float)(PanSteps - 1) * 2f - 1f);
            return bank[idx];
        }

        // Bip standard de la sentinelle : 40 ms a 440 Hz. Source DEDIEE hors pool : les
        // appelants (sentinelle d'aggro) sequencent leurs bips en file, jamais
        // superposes, donc une seule source suffit et son pitch est libre a chaque bip.
        public static void PlayTone(float pan, float pitch, float volume = 1f)
        {
            EnsureInit();
            if (_toneSource == null) return;
            if (_toneData == null) _toneData = BuildSineData(440.0, 40);
            var clip = PannedTone(ref _toneBank, _toneData, "A11ySineBeep", pan);
            _toneSource.pitch = Mathf.Clamp(pitch, 0.05f, 4f);
            _toneSource.PlayOneShot(clip, volume * A11ySettings.MasterVolume);
        }

        // Dent de scie GENEREE : meme enveloppe que BuildSineData, timbre riche en
        // harmoniques. Amplitude reduite a 0.8 (la scie sort plus fort qu'un sinus a
        // crete egale, ratio valide a l'oreille sur les demos du 12 juin).
        private static float[] BuildSawData(double freqHz, int ms)
        {
            const int rate = 44100;
            int len = rate * ms / 1000;
            const int fade = rate * 5 / 1000;   // 5 ms
            var data = new float[len];
            double step = freqHz / rate;
            double phase = 0.0;
            for (int i = 0; i < len; i++)
            {
                float env = 1f;
                if (i < fade) env = i / (float)fade;
                else if (i >= len - fade) env = (len - 1 - i) / (float)fade;
                data[i] = (float)(2.0 * phase - 1.0) * 0.8f * env;
                phase += step;
                if (phase >= 1.0) phase -= 1.0;
            }
            return data;
        }

        // Bip BOSS de la sentinelle : MEME base 440 Hz que le bip standard (le 110 Hz
        // d'origine cassait le referentiel pitch/pan appris - diagnostic utilisateur,
        // 12 juin) ; l'identite "boss" passe par le TIMBRE (dent de scie, agressif)
        // et la duree (90 ms), pas par le registre. Le langage positionnel est donc
        // identique aux mobs. Hypothese utilisateur a valider en jeu : "desagreable
        // seul mais doit sortir du mix". Plans B : twitch, jump2 (dossier candidats).
        public static void PlayBossTone(float pan, float pitch, float volume = 1f)
        {
            EnsureInit();
            if (_toneSource == null) return;
            if (_bossData == null) _bossData = BuildSawData(440.0, 90);
            var clip = PannedTone(ref _bossBank, _bossData, "A11yBossBeep", pan);
            _toneSource.pitch = Mathf.Clamp(pitch, 0.05f, 4f);
            _toneSource.PlayOneShot(clip, volume * A11ySettings.MasterVolume);
        }

        // --- Drone CONTINU du repere de centre (placeholder, 13 juin) ---
        // Sinus doux joue en BOUCLE. Le pan se fait par BALANCE de volume entre deux
        // sources hard-pannees (puissance constante), pas par panStereo (inoperant ici,
        // cf. BakePan) ; le pitch encode l'axe nord-sud. Tres faible volume = ambiance.

        // Sinus MONO bouclable proprement : un nombre ENTIER de cycles sur la longueur
        // (sinon clic au raccord de boucle). Pas d'enveloppe (le drone est continu).
        private static float[] BuildLoopSine(double freqHz)
        {
            const int rate = 44100;
            int len = rate / 10; // ~0,1 s
            int cycles = Mathf.Max(1, Mathf.RoundToInt((float)(len * freqHz / rate)));
            var data = new float[len];
            double w = 2.0 * System.Math.PI * cycles / len; // cycles entiers -> raccord net
            for (int i = 0; i < len; i++) data[i] = (float)System.Math.Sin(w * i);
            return data;
        }

        // Clip STEREO a pan DUR : le signal sur UN canal, zero sur l'autre. Deux de ces
        // clips (gauche + droit) joues en boucle sur deux sources -> pan par balance de
        // leurs volumes (un canal a zero ne peut pas fuir, comme BakePan).
        private static AudioClip BakeHardPan(float[] mono, bool left)
        {
            var data = new float[mono.Length * 2];
            for (int i = 0; i < mono.Length; i++)
            {
                data[2 * i] = left ? mono[i] : 0f;
                data[2 * i + 1] = left ? 0f : mono[i];
            }
            var clip = AudioClip.Create("A11yCenterDrone" + (left ? "L" : "R"), mono.Length, 2, 44100, false);
            clip.SetData(data, 0);
            return clip;
        }

        // Pilote le drone du repere de centre. active=false coupe. pan -1..+1 (est-ouest
        // du centre), pitch libre (nord-sud), volume tres faible. Idempotent par frame.
        public static void SetCenterDrone(bool active, float pan, float pitch, float volume)
        {
            EnsureInit();
            if (_droneL == null || _droneR == null) return;
            if (!active)
            {
                if (_droneOn) { _droneL.Stop(); _droneR.Stop(); _droneOn = false; }
                return;
            }
            if (!_droneOn) { _droneL.Play(); _droneR.Play(); _droneOn = true; }
            float ang = (Mathf.Clamp(pan, -1f, 1f) + 1f) * Mathf.PI * 0.25f;
            float gl = Mathf.Cos(ang), gr = Mathf.Sin(ang);
            float p = Mathf.Clamp(pitch, 0.05f, 4f);
            float v = volume * A11ySettings.MasterVolume;
            _droneL.volume = v * gl;
            _droneR.volume = v * gr;
            _droneL.pitch = p;
            _droneR.pitch = p;
        }

        private static MethodInfo _getNextSounds;
        private static bool _getNextSoundsResolved;

        // Joue un SON DE TABLE (SfxTableID, ex. destruction de tuile) spatialise NATIVEMENT
        // (position -> pan + distance, gracieusete du jeu) mais avec un pitch EXACT impose :
        // on force pitchDev a 0, sinon le random pitch de la table brouillerait l'info qu'on
        // encode dans le pitch (axe vertical). On NE reimplemente PAS la selection des sons
        // (cycle/variants/multi-couches) : on reutilise la methode privee GetNextSfxInfoSounds
        // du jeu par reflection (qui rend LA liste de sons a jouer pour cette lecture), et on
        // joue chacun via la surcharge SfxID native avec pitchDev:0. pitchMul multiplie le
        // pitch de base de chaque son. Fallback : methode native (timbre OK mais random pitch).
        public static void PlayTableSpatialNoPitchDev(int sfxTableID, Vector3 pos, float volume, float pitchMul)
        {
            var audio = Manager.audio;
            if (audio == null || audio.sfxTable == null) return;

            if (!_getNextSoundsResolved)
            {
                _getNextSoundsResolved = true;
                _getNextSounds = typeof(AudioManager).GetMethod("GetNextSfxInfoSounds",
                    BindingFlags.NonPublic | BindingFlags.Static);
            }

            if (_getNextSounds != null)
            {
                var list = _getNextSounds.Invoke(null, new object[] { audio, sfxTableID })
                           as List<SfxTable.SFXSound>;
                if (list != null)
                {
                    foreach (var s in list)
                    {
                        SfxID id = audio.InspectorFriendlySfxIDToSfxID(s.sfx);
                        AudioManager.Sfx(id, pos,
                            s.volume * volume * NativeGain(id) * A11ySettings.MasterVolume,
                            s.pitch * pitchMul, 0f);
                    }
                    return;
                }
            }

            // Reflection indisponible : on laisse le jeu tout gerer (timbre/couches fideles)
            // au prix du random pitch reinjecte par la table. Mieux que pas de son.
            AudioManager.Sfx(sfxTableID, pos, volume * A11ySettings.MasterVolume, pitchMul);
        }

        // Sources du mod : 2D (on gere pan/pitch nous-memes via clips pre-pannes), routees
        // sur le mixer du jeu. On ne bypasse PLUS reverb/effets/listener : la "fuite
        // d'oreille opposee" du build 64 venait en realite du SON SURROUND Windows (Dolby
        // Atmos casque), pas de la reverb de caverne -> le bypass etait une rustine invasive
        // contre un faux coupable. Nos sons se comportent donc comme les sons natifs.
        private static void ConfigureSource(AudioSource s)
        {
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            // Router via le MEME groupe de mixer que les sons natifs du jeu (EFFECTS).
            // Sinon nos PlayOneShot court-circuitent le mixer du jeu (-> master Unity
            // direct) et sortent a un autre niveau que les sons qu'on delegue a
            // AudioManager.Sfx (les materiaux de mur passent, eux, par ce groupe). C'est
            // l'etage de gain qui manquait a l'eau/trou/tons generes face aux sons de mur.
            if (_mixerGroup != null) s.outputAudioMixerGroup = _mixerGroup;
        }

        // Cree une AudioSource du mod sur notre GameObject audio, routee sur le mixer EFFECTS
        // (via ConfigureSource). loop=true pour les nappes continues. Utilisee par
        // ProximitySonar (sonar de proximite) pour ses 8 sources directionnelles.
        public static AudioSource CreateModSource(bool loop = false)
        {
            EnsureInit();
            if (_audioGo == null) return null;
            var s = _audioGo.AddComponent<AudioSource>();
            ConfigureSource(s);
            s.loop = loop;
            return s;
        }

        private static void EnsureInit()
        {
            if (_init) return;
            _init = true;

            // A recuperer AVANT de configurer nos sources : le audioFieldMap (SfxID -> clip)
            // et le groupe de mixer EFFECTS du jeu, sur lequel ConfigureSource route nos
            // sources pour qu'elles subissent le meme etage de gain que les sons natifs.
            var audio = Manager.audio;
            if (audio != null)
            {
                var f = typeof(AudioManager).GetField("audioFieldMap",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                _fields = f != null ? f.GetValue(audio) as AudioField[] : null;
                _mixerGroup = audio.EnumToMixerGroup(AudioManager.MixerGroupEnum.EFFECTS);
            }

            var go = new GameObject("A11yGameplayAudio");
            Object.DontDestroyOnLoad(go);
            _audioGo = go;
            _pool = new AudioSource[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var s = go.AddComponent<AudioSource>();
                ConfigureSource(s);
                _pool[i] = s;
            }

            _toneSource = go.AddComponent<AudioSource>();
            ConfigureSource(_toneSource);

            // Source dediee du beacon de navigation (coupee/relancee a chaque ping).
            _beaconSource = go.AddComponent<AudioSource>();
            ConfigureSource(_beaconSource);

            // Repere de centre : deux sources hard-pannees jouant en boucle un sinus
            // doux ; pan par balance de leurs volumes, pitch par l'axe nord-sud.
            _droneL = go.AddComponent<AudioSource>();
            _droneR = go.AddComponent<AudioSource>();
            ConfigureSource(_droneL);
            ConfigureSource(_droneR);
            _droneL.loop = true;
            _droneR.loop = true;
            var droneSine = BuildLoopSine(220.0);
            _droneL.clip = BakeHardPan(droneSine, true);
            _droneR.clip = BakeHardPan(droneSine, false);
        }
    }
}
