using System.Collections.Generic;
using System.Reflection;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using PugMod;
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

        // Son d'UI 2D (clics du panneau de reglages) : NORMALISE (egalise les niveaux natifs
        // tres inegaux des clics) ET pilote par le volume maitre comme tout le reste du mod -
        // le general est le master, il gouverne tout. Couper le maitre n'enferme pas l'utilisateur :
        // le TTS (NVDA) reste audible et porte la navigation du panneau.
        public static void PlayUi(SfxID id, float pitch = 1f, float volume = 1f)
        {
            EnsureInit();
            if (_pool == null || _fields == null) return;
            int idx = (int)id;
            if (idx < 0 || idx >= _fields.Length || _fields[idx] == null) return;
            var clip = _fields[idx].GetNextAudioClip();
            if (clip == null) return;
            float gain = NormalizeGain(idx, ref clip);
            var src = _pool[_poolIdx];
            _poolIdx = (_poolIdx + 1) % PoolSize;
            src.panStereo = 0f;
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

        private static AudioSource _pingSource;

        // Ping de reperage d'un autre joueur (cf. PlayerPing) : meme principe que PlayBeacon
        // (Stop puis Play a chaque ping -> la cadence gouverne le rythme, la traine du clip
        // ne deborde jamais), mais sur une source DEDIEE SEPAREE. Sinon le ping joueur et le
        // guidage par balises se couperaient mutuellement : ce sont deux earcons repetes qui
        // doivent pouvoir tourner EN MEME TEMPS (suivre un coequipier tout en suivant un
        // itineraire). Un seul joueur suivi a la fois -> une source suffit.
        public static void PlayPing(SfxID id, float pan, float pitch, float volume = 1f)
        {
            EnsureInit();
            if (_pingSource == null || _fields == null) return;
            int idx = (int)id;
            if (idx < 0 || idx >= _fields.Length || _fields[idx] == null) return;
            var clip = _fields[idx].GetNextAudioClip();
            if (clip == null) return;
            float gain = NormalizeGain(idx, ref clip);
            _pingSource.Stop(); // coupe le ping precedent : la cadence decoupe le son
            _pingSource.clip = clip;
            _pingSource.panStereo = Mathf.Clamp(pan, -1f, 1f);
            _pingSource.pitch = Mathf.Clamp(pitch, 0.05f, 4f);
            _pingSource.volume = Mathf.Clamp01(volume * gain * A11ySettings.MasterVolume);
            _pingSource.Play();
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
        private static AudioSource _relayL, _relayR;
        private static bool _relayOn;
        private static float[] _toneData;
        private static float[] _bossData;
        private static AudioClip[] _toneBank;
        private static AudioClip[] _bossBank;
        private static AudioSource _darkSource;
        private static float[] _darkData;
        private static AudioClip[] _darkBank;

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

        // --- "Son du noir" (detecteur d'obscurite, design fige 16 juillet 2026) ---
        // UN SEUL earcon (decision utilisateur : pas de distinction entree/sortie, pas de
        // second timbre pour la canne) - joue par SonifyTile (curseur ET canne, cf.
        // BuildModeNavigator) quand la case ciblee/atteinte n'est pas eclairee. Meme grammaire
        // spatiale que le tone standard (pan continu via banc de clips pre-pannes, pitch par
        // le pitch de la source) - source DEDIEE (ne vole pas le pitch de _toneSource, qui
        // sequence les bips de sentinelle). PLACEHOLDER (chirp descendant etouffe) : timbre
        // definitif a choisir a l'oreille par l'utilisateur (cf. core-keeper-sound-audition.md).
        public static void PlayDarknessEarcon(float pan, float pitch, float volume = 1f)
        {
            EnsureInit();
            if (_darkSource == null) return;
            if (_darkData == null) _darkData = BuildDarknessData();
            var clip = PannedTone(ref _darkBank, _darkData, "A11yDarkness", pan);
            _darkSource.pitch = Mathf.Clamp(pitch, 0.05f, 4f);
            _darkSource.PlayOneShot(clip, volume * A11ySettings.MasterVolume);
        }

        // PLACEHOLDER : chirp descendant doux (300 -> 90 Hz), attaque 5 ms / sortie 80 ms,
        // 220 ms. Distinct des autres earcons (aucun n'est un simple sweep descendant doux) -
        // evoque "ca s'eteint / le vide", a affiner par audition.
        private static float[] BuildDarknessData()
        {
            const int rate = 44100;
            int len = rate * 220 / 1000;
            int atk = rate * 5 / 1000, rel = rate * 80 / 1000;
            var data = new float[len];
            double phase = 0.0;
            for (int i = 0; i < len; i++)
            {
                double frac = (double)i / len;
                double f = 300.0 - (300.0 - 90.0) * frac; // descend 300 -> 90 Hz
                phase += 2.0 * System.Math.PI * f / rate;
                double v = System.Math.Sin(phase);
                float env = 1f;
                if (i < atk) env = i / (float)atk;
                else if (i >= len - rel) env = (len - 1 - i) / (float)rel;
                data[i] = (float)v * env;
            }
            NormalizePeak(data, 0.5f);
            return data;
        }

        // --- Earcons d'ALERTE d'etat (conditions subies) ---
        // NON spatialises (c'est TOI qui subis -> centre). Source DEDIEE pour ne pas voler le
        // pitch des bips de la sentinelle (_toneSource sequence ses bips). Deux timbres GENERES
        // (aucun asset) choisis par l'utilisateur a l'oreille, construits paresseusement et
        // gardes en RAM. Le buffer est normalise a -6 dBFS de crete (anti-clip + niveau coherent
        // avec le mastering du mod : les tons generes n'ont pas la marge des sons normalises).
        private static AudioSource _condSource;
        private static AudioClip _stunBuzzClip, _dotMinorClip;

        // stun = buzz (porteuse grave hachee). DoT = accord grave mineur dissonant.
        public static void PlayConditionEarcon(bool stun, float volume = 1f)
        {
            EnsureInit();
            if (_condSource == null) return;
            AudioClip clip;
            if (stun) { if (_stunBuzzClip == null) _stunBuzzClip = BuildBuzzClip(); clip = _stunBuzzClip; }
            else { if (_dotMinorClip == null) _dotMinorClip = BuildMinorChordClip(); clip = _dotMinorClip; }
            if (clip == null) return;
            _condSource.pitch = 1f;
            _condSource.PlayOneShot(clip, volume * A11ySettings.MasterVolume);
        }

        private static void NormalizePeak(float[] data, float target)
        {
            float peak = 1e-9f;
            for (int i = 0; i < data.Length; i++) { float a = data[i] < 0f ? -data[i] : data[i]; if (a > peak) peak = a; }
            float g = target / peak;
            for (int i = 0; i < data.Length; i++) data[i] *= g;
        }

        private static AudioClip MonoClip(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, 44100, false);
            clip.SetData(data, 0);
            return clip;
        }

        // Earcon STUN : porteuse 180 Hz hachee par un tremolo carre a 40 Hz (1 / 0.3), 250 ms,
        // attaque 3 ms / sortie 30 ms. "Buzz d'immobilisation".
        private static AudioClip BuildBuzzClip()
        {
            const int rate = 44100;
            int len = rate * 250 / 1000;
            int atk = rate * 3 / 1000, rel = rate * 30 / 1000;
            var data = new float[len];
            for (int i = 0; i < len; i++)
            {
                double t = (double)i / rate;
                double car = System.Math.Sin(2.0 * System.Math.PI * 180.0 * t);
                double tr = System.Math.Sin(2.0 * System.Math.PI * 40.0 * t) > 0.0 ? 1.0 : 0.3;
                float env = 1f;
                if (i < atk) env = i / (float)atk;
                else if (i >= len - rel) env = (len - 1 - i) / (float)rel;
                data[i] = (float)(car * tr) * env;
            }
            NormalizePeak(data, 0.5f);
            return MonoClip("A11yCondStun", data);
        }

        // Earcon DoT : accord grave MINEUR dissonant (165 + 196 + 247 Hz), sature (tanh),
        // montee progressive (swell 120 ms), 460 ms, sortie 130 ms - le plus dramatique du lot.
        private static AudioClip BuildMinorChordClip()
        {
            const int rate = 44100;
            int len = rate * 460 / 1000;
            int rel = rate * 130 / 1000;
            int swell = rate * 120 / 1000;
            var data = new float[len];
            for (int i = 0; i < len; i++)
            {
                double t = (double)i / rate;
                double mix = System.Math.Sin(2.0 * System.Math.PI * 165.0 * t)
                           + 0.8 * System.Math.Sin(2.0 * System.Math.PI * 196.0 * t)
                           + 0.7 * System.Math.Sin(2.0 * System.Math.PI * 247.0 * t);
                double v = System.Math.Tanh(2.2 * mix);
                float env = 1f;
                if (i < swell) env = i / (float)swell;
                if (i >= len - rel) { float r = (len - 1 - i) / (float)rel; if (r < env) env = r; }
                data[i] = (float)v * env;
            }
            NormalizePeak(data, 0.5f);
            return MonoClip("A11yCondDot", data);
        }

        // --- Earcons d'ALERTE DE VIE (seuils de sante) ---
        // TROIS earcons GENERES (aucun asset), choisis par l'utilisateur a l'oreille, sur une
        // source DEDIEE (ne vole pas le pitch des earcons de condition / sentinelle). Buffers
        // normalises a -6 dBFS de crete (les tons generes n'ont pas la marge des sons normalises).
        //  - WARN (seuil ~60 %) : DEUX bips serres a onde presque carree, joues UNE fois.
        //  - ALERTE (entree ~20 %) : sirene saturee MONTANTE, jouee UNE fois au passage critique.
        //  - CRITIQUE (sous ~20 %) : battement de coeur grave resonant, REPETE par VitalsReadout
        //    (la CADENCE de repetition est pilotee par l'appelant, jamais par la duree du clip).
        private static AudioSource _healthSource;
        private static AudioClip _healthWarnClip, _healthAlertClip, _healthCritClip;

        public static void PlayHealthWarn(float volume = 1f)
        {
            EnsureInit();
            if (_healthSource == null) return;
            if (_healthWarnClip == null) _healthWarnClip = BuildHealthWarnClip();
            if (_healthWarnClip == null) return;
            _healthSource.pitch = 1f;
            _healthSource.PlayOneShot(_healthWarnClip, volume * A11ySettings.MasterVolume);
        }

        public static void PlayHealthAlert(float volume = 1f)
        {
            EnsureInit();
            if (_healthSource == null) return;
            if (_healthAlertClip == null) _healthAlertClip = BuildHealthAlertClip();
            if (_healthAlertClip == null) return;
            _healthSource.pitch = 1f;
            _healthSource.PlayOneShot(_healthAlertClip, volume * A11ySettings.MasterVolume);
        }

        public static void PlayHealthCritical(float volume = 1f)
        {
            EnsureInit();
            if (_healthSource == null) return;
            if (_healthCritClip == null) _healthCritClip = BuildHealthCritClip();
            if (_healthCritClip == null) return;
            _healthSource.pitch = 1f;
            _healthSource.PlayOneShot(_healthCritClip, volume * A11ySettings.MasterVolume);
        }

        // ALERTE : chirp (sweep de frequence) 500 -> 1050 Hz, sature tanh, 300 ms, attaque 10 ms /
        // sortie 50 ms. La montee de hauteur dit "ca se degrade", la saturation donne de la
        // presence sans tomber dans le TTS. "Urgent mais pas pressant" (one-shot).
        private static AudioClip BuildHealthAlertClip()
        {
            const int rate = 44100;
            int len = rate * 300 / 1000;
            int atk = rate * 10 / 1000, rel = rate * 50 / 1000;
            var data = new float[len];
            double phase = 0.0;
            for (int i = 0; i < len; i++)
            {
                double frac = (double)i / len;
                double f = 500.0 + (1050.0 - 500.0) * frac;
                phase += 2.0 * System.Math.PI * f / rate;
                double v = System.Math.Tanh(2.0 * System.Math.Sin(phase));
                float env = 1f;
                if (i < atk) env = i / (float)atk;
                else if (i >= len - rel) env = (len - 1 - i) / (float)rel;
                data[i] = (float)v * env;
            }
            NormalizePeak(data, 0.5f);
            return MonoClip("A11yHealthAlert", data);
        }

        // WARN (seuil 60 %) : DEUX bips serres a onde presque carree (medium 550 Hz), joues UNE
        // fois. Signal sec et synthetique - ni melodique ni alarmant, distinct de la sirene
        // (montante) et du battement (grave). Variante D1 retenue a l'oreille.
        private static AudioClip BuildHealthWarnClip()
        {
            const int rate = 44100;
            int len = rate * 180 / 1000;
            var data = new float[len];
            AddSqBip(data, rate, 0,                550.0, rate * 50 / 1000);
            AddSqBip(data, rate, rate * 90 / 1000, 550.0, rate * 50 / 1000);
            NormalizePeak(data, 0.5f);
            return MonoClip("A11yHealthWarn", data);
        }

        // Un bip a onde PRESQUE CARREE : tanh(5 x sin) aplatit la sinusoide vers le carre sans
        // l'aliasing dur d'un vrai signe, attaque 3 ms / sortie 20 ms anti-clic.
        private static void AddSqBip(float[] data, int rate, int startSample, double freq, int durSamples)
        {
            int atk = rate * 3 / 1000, rel = rate * 20 / 1000;
            for (int i = 0; i < durSamples; i++)
            {
                int idx = startSample + i;
                if (idx < 0 || idx >= data.Length) continue;
                double t = (double)i / rate;
                double v = System.Math.Tanh(5.0 * System.Math.Sin(2.0 * System.Math.PI * freq * t));
                float env = 1f;
                if (i < atk) env = i / (float)atk;
                else if (i >= durSamples - rel) env = (durSamples - 1 - i) / (float)rel;
                data[idx] += (float)v * env;
            }
        }

        // CRITIQUE : "battement de coeur" - deux thumps GRAVES resonants (lub-dub) RESSERRES
        // (variante B2 retenue a l'oreille : dub a 160 ms, ~340 ms au total). Grave et corporel
        // = "impossible a ignorer", repete par l'appelant tant que la vie reste critique.
        private static AudioClip BuildHealthCritClip()
        {
            const int rate = 44100;
            int len = rate * 340 / 1000;
            int rel = rate * 110 / 1000;
            var data = new float[len];
            AddThump(data, rate, 0,                 rate * 160 / 1000, rel, 1.00); // lub
            AddThump(data, rate, rate * 160 / 1000, rate * 150 / 1000, rel, 0.85); // dub (resserre)
            NormalizePeak(data, 0.5f);
            return MonoClip("A11yHealthCrit", data);
        }

        // Un "thump" cardiaque : chirp descendant 110 -> 44 Hz sature tanh (le pitch qui plonge =
        // l'impact organique), attaque 3 ms puis longue resonance (sortie rel). Additionne dans
        // data a partir de startSample.
        private static void AddThump(float[] data, int rate, int startSample, int durSamples, int relSamples, double gain)
        {
            int atk = rate * 3 / 1000;
            double phase = 0.0;
            for (int i = 0; i < durSamples; i++)
            {
                int idx = startSample + i;
                if (idx < 0 || idx >= data.Length) continue;
                double frac = (double)i / durSamples;
                double f = 110.0 + (44.0 - 110.0) * frac;
                phase += 2.0 * System.Math.PI * f / rate;
                double v = System.Math.Tanh(2.0 * System.Math.Sin(phase));
                float env = 1f;
                if (i < atk) env = i / (float)atk;
                else if (i >= durSamples - relSamples) env = (durSamples - 1 - i) / (float)relSamples;
                data[idx] += (float)(v * gain) * env;
            }
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
        private static AudioClip BakeHardPan(float[] mono, bool left, string baseName = "A11yCenterDrone", int sampleRate = 44100)
        {
            var data = new float[mono.Length * 2];
            for (int i = 0; i < mono.Length; i++)
            {
                data[2 * i] = left ? mono[i] : 0f;
                data[2 * i + 1] = left ? 0f : mono[i];
            }
            var clip = AudioClip.Create(baseName + (left ? "L" : "R"), mono.Length, 2, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // Pilote le drone des relais non actives. Meme mecanique que SetCenterDrone mais
        // timbre battement 440+444 Hz. Source independante -> coexiste avec le drone de centre.
        public static void SetRelayDrone(bool active, float pan, float pitch, float volume)
        {
            EnsureInit();
            if (_relayL == null || _relayR == null) return;
            if (!active)
            {
                if (_relayOn) { _relayL.Stop(); _relayR.Stop(); _relayOn = false; }
                return;
            }
            if (!_relayOn) { _relayL.Play(); _relayR.Play(); _relayOn = true; }
            float ang = (Mathf.Clamp(pan, -1f, 1f) + 1f) * Mathf.PI * 0.25f;
            float gl = Mathf.Cos(ang), gr = Mathf.Sin(ang);
            float p = Mathf.Clamp(pitch, 0.05f, 4f);
            float lfo = 0.5f + 0.5f * Mathf.Sin(Time.time * 2f * Mathf.PI * 4f);
            float v = volume * lfo * A11ySettings.MasterVolume;
            _relayL.volume = v * gl;
            _relayR.volume = v * gr;
            _relayL.pitch = p;
            _relayR.pitch = p;
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

        // --- Sons Azeos charges a la volee (WAV du mod, aucun asset Unity) ---
        // Les 3 sons livres durent ~4s chacun (pas de courts bips) : on les fait BOUCLER en
        // continu tant que le danger est present, on pilote juste pan/volume en direct
        // pendant la boucle - meme mecanique que SetCenterDrone/SetRelayDrone, pas de
        // PlayOneShot repete (empilerait des dizaines de copies d'un son de 4s).

        private const string ModNameForFiles = "CoreKeeperAccess";

        private static byte[] ReadModFile(string relativePath)
        {
            var loader = API.ModLoader;
            if (loader == null) return null;
            foreach (var m in loader.LoadedMods)
                if (m?.Metadata != null && m.Metadata.name == ModNameForFiles)
                    return m.GetFile(relativePath);
            return null;
        }

        // Colonne verticale (pattern "rangee V") : pan est-ouest en direct -> deux sources
        // hard-pannees comme le repere de centre (une seule ne pourrait pas suivre un pan
        // qui glisse sans couper/redemarrer le clip a chaque pas).
        private static AudioSource _azeosColL, _azeosColR;
        private static bool _azeosColOn;
        private static float[] _azeosColMono;
        private static int _azeosColRate;
        private static bool _azeosColLoadAttempted;

        private static void EnsureAzeosColonneLoaded()
        {
            if (_azeosColLoadAttempted) return;
            _azeosColLoadAttempted = true;
            var bytes = ReadModFile("Sounds/azeos_colonne.wav");
            if (bytes == null) { Diag.Log("A11yWav", "Sounds/azeos_colonne.wav introuvable dans l'install du mod"); return; }
            if (!WavLoader.TryParse(bytes, "azeos_colonne.wav", out var r)) return;
            if (r.Channels != 1)
            {
                Diag.Log("A11yWav", "azeos_colonne.wav : attendu mono, recu " + r.Channels + " canaux - ignore");
                return;
            }
            NormalizePeak(r.Samples, TargetPeak);
            _azeosColMono = r.Samples;
            _azeosColRate = r.SampleRate;
            // PROVISOIRE (diagnostic verif sons colonne/ligne)
            Diag.Log("A11yWav", "azeos_colonne.wav charge (rate=" + r.SampleRate + ", " + r.Samples.Length + " echantillons)");
        }

        // active=false coupe. pan -1..+1 est-ouest, volume pilote par l'appelant (distance).
        public static void SetAzeosColonne(bool active, float pan, float volume)
        {
            EnsureInit();
            EnsureAzeosColonneLoaded();
            if (_azeosColMono == null || _azeosColL == null || _azeosColR == null)
            {
                if (_azeosColOn && _azeosColL != null && _azeosColR != null) { _azeosColL.Stop(); _azeosColR.Stop(); _azeosColOn = false; }
                return;
            }
            if (!active)
            {
                if (_azeosColOn)
                {
                    _azeosColL.Stop(); _azeosColR.Stop(); _azeosColOn = false;
                    Diag.Log("A11yAzeosWave", "colonne : source arretee"); // PROVISOIRE
                }
                return;
            }
            if (!_azeosColOn)
            {
                if (_azeosColL.clip == null)
                {
                    _azeosColL.clip = BakeHardPan(_azeosColMono, true, "A11yAzeosColonne", _azeosColRate);
                    _azeosColR.clip = BakeHardPan(_azeosColMono, false, "A11yAzeosColonne", _azeosColRate);
                }
                _azeosColL.Play(); _azeosColR.Play(); _azeosColOn = true;
                Diag.Log("A11yAzeosWave", "colonne : source demarree"); // PROVISOIRE
            }
            float ang = (Mathf.Clamp(pan, -1f, 1f) + 1f) * Mathf.PI * 0.25f;
            float gl = Mathf.Cos(ang), gr = Mathf.Sin(ang);
            float v = volume * A11ySettings.MasterVolume;
            _azeosColL.volume = v * gl;
            _azeosColR.volume = v * gr;
        }

        // Lignes horizontales (pattern "rangee H", au-dessus/en-dessous du joueur) : volume
        // seul (pas de pan) -> une source en boucle chacune suffit, le fichier stereo garde
        // son image d'origine (pas besoin de le replier en mono, rien a y cuire).
        private static AudioSource _azeosLigneHautSource, _azeosLigneBasSource;
        private static bool _azeosLigneHautOn, _azeosLigneBasOn;
        private static AudioClip _azeosLigneHautClip, _azeosLigneBasClip;
        private static bool _azeosLigneHautLoadAttempted, _azeosLigneBasLoadAttempted;

        private static AudioClip LoadAzeosLigneClip(string relPath, string clipName)
        {
            var bytes = ReadModFile(relPath);
            if (bytes == null) { Diag.Log("A11yWav", relPath + " introuvable dans l'install du mod"); return null; }
            if (!WavLoader.TryParse(bytes, relPath, out var r)) return null;
            NormalizePeak(r.Samples, TargetPeak);
            var clip = AudioClip.Create(clipName, r.Samples.Length / r.Channels, r.Channels, r.SampleRate, false);
            clip.SetData(r.Samples, 0);
            // PROVISOIRE (diagnostic verif sons colonne/ligne)
            Diag.Log("A11yWav", relPath + " charge (rate=" + r.SampleRate + ", canaux=" + r.Channels + ")");
            return clip;
        }

        public static void SetAzeosLigneHaut(bool active, float volume) =>
            SetAzeosLigne(ref _azeosLigneHautSource, ref _azeosLigneHautOn, ref _azeosLigneHautClip,
                ref _azeosLigneHautLoadAttempted, "Sounds/azeos_ligne_haut.wav", "A11yAzeosLigneHaut", active, volume);

        public static void SetAzeosLigneBas(bool active, float volume) =>
            SetAzeosLigne(ref _azeosLigneBasSource, ref _azeosLigneBasOn, ref _azeosLigneBasClip,
                ref _azeosLigneBasLoadAttempted, "Sounds/azeos_ligne_bas.wav", "A11yAzeosLigneBas", active, volume);

        private static void SetAzeosLigne(ref AudioSource src, ref bool on, ref AudioClip clip,
            ref bool loadAttempted, string relPath, string clipName, bool active, float volume)
        {
            EnsureInit();
            if (!loadAttempted) { loadAttempted = true; clip = LoadAzeosLigneClip(relPath, clipName); }
            if (src == null || clip == null)
            {
                if (on && src != null) { src.Stop(); on = false; }
                return;
            }
            if (!active)
            {
                if (on)
                {
                    src.Stop(); on = false;
                    Diag.Log("A11yAzeosWave", clipName + " : source arretee"); // PROVISOIRE
                }
                return;
            }
            if (!on)
            {
                if (src.clip == null) src.clip = clip;
                src.Play();
                Diag.Log("A11yAzeosWave", clipName + " : source demarree"); // PROVISOIRE
                on = true;
            }
            src.volume = volume * A11ySettings.MasterVolume;
        }

        // Annonce de vie du boss tous les 10% (3 juillet 2026, VALIDEE en combat reel) : WAV
        // de voix pre-rendu, un fichier par palier. Canal AUDIO dedie (volume propre, hors
        // file NVDA) - seule annonce assez frequente pour le meriter, tout le reste des
        // annonces de boss est parle (cf. BossAnnounce). Generique : n'importe quel boss
        // marque BossCD, cf. BossHealthAnnounce.
        //
        // MULTILINGUE depuis le 5 aout 2026 : Sounds/voice/<langue>/hp_<palier>.wav, repli
        // sur l'anglais, puis - si aucun fichier n'existe pour cette langue - sur la parole
        // normale. Une langue ajoutee au mod parle donc immediatement ; fr et en gardent le
        // canal dedie. Generateur des fichiers : tools/gen-voice-sounds.ps1.
        private static AudioSource _bossHealthSource;
        private static readonly Dictionary<string, AudioClip> _voiceClips = new Dictionary<string, AudioClip>();

        public static void PlayBossHealthCallout(int percent)
        {
            EnsureInit();
            var clip = _bossHealthSource == null ? null : LoadVoiceClip("hp_" + percent);
            if (clip == null)
            {
                TtsText.Say(Strings.L("voice.hp." + percent), false);
                return;
            }
            _bossHealthSource.PlayOneShot(clip, A11ySettings.BossHealthVolume * A11ySettings.MasterVolume);
        }

        // Langue courante, puis anglais. L'absence est memoisee elle aussi -> un fichier
        // manquant n'est pas retente a chaque palier.
        private static AudioClip LoadVoiceClip(string baseName)
        {
            string lang = Strings.LanguageCode;
            var clip = LoadVoiceClipAt("Sounds/voice/" + lang + "/" + baseName + ".wav");
            if (clip == null && lang != "en")
                clip = LoadVoiceClipAt("Sounds/voice/en/" + baseName + ".wav");
            return clip;
        }

        private static AudioClip LoadVoiceClipAt(string relPath)
        {
            if (_voiceClips.TryGetValue(relPath, out var cached)) return cached;
            _voiceClips[relPath] = null;

            var bytes = ReadModFile(relPath);
            if (bytes == null) { Diag.Log("A11yWav", relPath + " introuvable"); return null; }
            if (!WavLoader.TryParse(bytes, relPath, out var r)) return null;

            // Gain PUR (pas d'ecretage) : cale la CRETE a 0,9, zero distorsion possible.
            // Volume final regle a l'appel (independant, ajustable sans re-traiter le sample
            // ni toucher aux autres sons du mod).
            NormalizePeak(r.Samples, 0.9f);
            var clip = AudioClip.Create(relPath, r.Samples.Length / r.Channels, r.Channels, r.SampleRate, false);
            clip.SetData(r.Samples, 0);
            _voiceClips[relPath] = clip;
            return clip;
        }

        // Balise de guidage vers le cristal (BirdBossStone) le plus pertinent : GENEREE (pas
        // de son fourni pour celui-la), meme mecanique que SetCenterDrone/SetRelayDrone -
        // deux sources hard-pannees, timbre 330 Hz distinct du repere (220) et des relais
        // (440). urgent=true (cristal deja actif, soigne le boss) -> pulsation LFO deux fois
        // plus rapide, signal plus pressant, sans changer de timbre (le carillon reste
        // reconnaissable, seule l'urgence change).
        private static AudioSource _cristalL, _cristalR;
        private static bool _cristalOn;

        public static void SetCristalGuide(bool active, float pan, float pitch, float volume, bool urgent)
        {
            EnsureInit();
            if (_cristalL == null || _cristalR == null) return;
            if (!active)
            {
                if (_cristalOn) { _cristalL.Stop(); _cristalR.Stop(); _cristalOn = false; }
                return;
            }
            if (!_cristalOn) { _cristalL.Play(); _cristalR.Play(); _cristalOn = true; }
            float ang = (Mathf.Clamp(pan, -1f, 1f) + 1f) * Mathf.PI * 0.25f;
            float gl = Mathf.Cos(ang), gr = Mathf.Sin(ang);
            float p = Mathf.Clamp(pitch, 0.05f, 4f);
            float lfoHz = urgent ? 8f : 4f;
            float lfo = 0.5f + 0.5f * Mathf.Sin(Time.time * 2f * Mathf.PI * lfoHz);
            float v = volume * lfo * A11ySettings.MasterVolume;
            _cristalL.volume = v * gl;
            _cristalR.volume = v * gr;
            _cristalL.pitch = p;
            _cristalR.pitch = p;
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

            // Source dediee du ping de reperage joueur (cf. PlayerPing) : separee du beacon
            // pour que guidage et ping joueur puissent sonner en meme temps.
            _pingSource = go.AddComponent<AudioSource>();
            ConfigureSource(_pingSource);

            // Source dediee des earcons d'alerte d'etat (DoT / stun), non spatialises.
            _condSource = go.AddComponent<AudioSource>();
            ConfigureSource(_condSource);

            // Source dediee des earcons d'alerte de VIE (sirene + battement de coeur), non spatialises.
            _healthSource = go.AddComponent<AudioSource>();
            ConfigureSource(_healthSource);

            // Source dediee du "son du noir" (detecteur d'obscurite), SPATIALISEE (pan+pitch
            // par case, comme le tone standard) - ne vole pas le pitch de _toneSource.
            _darkSource = go.AddComponent<AudioSource>();
            ConfigureSource(_darkSource);

            // Source dediee de l'annonce de vie boss (VALIDEE en combat, cf. PlayBossHealthCallout).
            _bossHealthSource = go.AddComponent<AudioSource>();
            ConfigureSource(_bossHealthSource);

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

            _relayL = go.AddComponent<AudioSource>();
            _relayR = go.AddComponent<AudioSource>();
            ConfigureSource(_relayL);
            ConfigureSource(_relayR);
            _relayL.loop = true;
            _relayR.loop = true;
            var relaySine = BuildLoopSine(440.0);
            _relayL.clip = BakeHardPan(relaySine, true);
            _relayR.clip = BakeHardPan(relaySine, false);

            // Sons Azeos (colonne + 2 lignes) : clips charges paresseusement au premier
            // Set*, sources creees ici (vides pour l'instant) et mises en boucle.
            _azeosColL = go.AddComponent<AudioSource>();
            _azeosColR = go.AddComponent<AudioSource>();
            ConfigureSource(_azeosColL);
            ConfigureSource(_azeosColR);
            _azeosColL.loop = true;
            _azeosColR.loop = true;

            _azeosLigneHautSource = go.AddComponent<AudioSource>();
            _azeosLigneBasSource = go.AddComponent<AudioSource>();
            ConfigureSource(_azeosLigneHautSource);
            ConfigureSource(_azeosLigneBasSource);
            _azeosLigneHautSource.loop = true;
            _azeosLigneBasSource.loop = true;

            _cristalL = go.AddComponent<AudioSource>();
            _cristalR = go.AddComponent<AudioSource>();
            ConfigureSource(_cristalL);
            ConfigureSource(_cristalR);
            _cristalL.loop = true;
            _cristalR.loop = true;
            var cristalSine = BuildLoopSine(330.0);
            _cristalL.clip = BakeHardPan(cristalSine, true);
            _cristalR.clip = BakeHardPan(cristalSine, false);
        }
    }
}
