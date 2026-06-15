using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

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

        // --- Normalisation de volume (12 juin 2026) ---
        // Les sons du jeu sont masterises tres inegalement (jusqu'a 40 dB d'ecart
        // releves sur le dump). On ramene chaque SfxID vers une reference RMS par
        // ATTENUATION (gain <= 1, choix utilisateur) ; seuls les cas extremes (quasi
        // inaudibles sous BoostFloor) et les VRAIS stereo (image G/D qui fausserait
        // notre pan) sont reconstruits en RAM (mono, gain cuit dans les echantillons,
        // crete bornee). Mesure a la PREMIERE lecture puis cache. Les tons generes
        // (PlayTone/PlayBossTone) ne sont PAS normalises : amplitude maitrisee par
        // construction. Limite assumee : le gain d'un SfxID est mesure sur la
        // premiere variante servie (les variantes d'un meme son sont masterisees
        // ensemble), et un clip reconstruit fige cette variante-la.
        private const float TargetRms = 0.15f;   // ~-16,5 dBFS sur la fenetre d'attaque : reference, on attenue vers elle
        private const float BoostFloor = 0.012f; // -38 dBFS : en-dessous, inaudible -> boost RAM
        private const float BoostPeak = 0.9f;    // plafond de crete apres amplification
        private const float RetryDelay = 3f;     // GetData peut refuser un clip pas encore charge : on retente
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

        // Variante pour la voie NATIVE (sons de table) : pas de substitution de clip
        // possible (le jeu joue les siens), donc attenuation seule.
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

                // RMS sur la FENETRE de 100 ms la plus forte (l'attaque), pas sur tout
                // le fichier : un impact a longue traine a une moyenne basse mais une
                // attaque forte, c'est l'attaque que l'oreille juge. Sans ca, les sons
                // de materiaux passaient intouches pendant que les bips courts et
                // denses se faisaient raboter (constate en jeu, build 60).
                int win = Mathf.Max(1, clip.frequency / 10 * ch);
                double sumWin = 0, maxWin = 0, sumL = 0, sumR = 0;
                float peak = 0f;
                for (int i = 0; i < data.Length; i++)
                {
                    float v = data[i];
                    sumWin += v * v;
                    if (i >= win) { float o = data[i - win]; sumWin -= o * o; }
                    if (sumWin > maxWin) maxWin = sumWin;
                    float a = v < 0f ? -v : v;
                    if (a > peak) peak = a;
                    if (ch == 2) { if ((i & 1) == 0) sumL += v * v; else sumR += v * v; }
                }
                float rms = (float)System.Math.Sqrt(maxWin / System.Math.Min(win, data.Length));
                if (rms < 1e-6f || peak < 1e-6f) { _gain[idx] = 1f; return 1f; }

                // Vrai stereo = plus de 1 dB d'ecart d'energie entre canaux (les
                // "faux stereo" du jeu, canaux identiques, passent sans reconstruction).
                bool trueStereo = false;
                if (ch == 2 && sumL > 0 && sumR > 0)
                {
                    double balDb = 10.0 * System.Math.Log10(sumL / sumR);
                    trueStereo = balDb > 1.0 || balDb < -1.0;
                }
                bool boost = rms < BoostFloor;
                float g = rms > TargetRms ? TargetRms / rms : 1f;

                if (boost || trueStereo)
                {
                    float baked = boost ? Mathf.Min(TargetRms / rms, BoostPeak / peak) : g;
                    int frames = clip.samples;
                    var mono = new float[frames];
                    for (int f = 0; f < frames; f++)
                    {
                        float v = 0f;
                        for (int c = 0; c < ch; c++) v += data[f * ch + c];
                        mono[f] = Mathf.Clamp(v / ch * baked, -1f, 1f);
                    }
                    var ram = AudioClip.Create(clip.name + "_a11yNorm", frames, 1, clip.frequency, false);
                    ram.SetData(mono, 0);
                    _rebuilt[idx] = ram;
                    _gain.Remove(idx);
                    Diag.Log("A11yAudioNorm", "rebuilt " + clip.name + " rms=" + Fmt(rms)
                        + (boost ? " BOOST x" + baked.ToString("0.0") : "") + (trueStereo ? " STEREO->mono" : ""));
                    clip = ram;
                    return 1f;
                }
                Diag.Log("A11yAudioNorm", "mesure " + clip.name + " rms=" + Fmt(rms) + " gain=" + g.ToString("0.00"));
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

        private static AudioClip BakePan(string name, float[] mono, float pan)
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

        // Sources du mod : 2D (pan/pitch geres par nous) et SECHES - on contourne la
        // reverb de caverne et les effets listener du jeu. Sans ca, meme un pan a
        // 100 % laisse le RETOUR de reverb (stereo large) dans l'oreille opposee
        // (constate en jeu build 64 : fuite residuelle a pan plein).
        private static void ConfigureSource(AudioSource s)
        {
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.bypassReverbZones = true;
            s.bypassListenerEffects = true;
            s.bypassEffects = true;
            s.reverbZoneMix = 0f;
        }

        private static void EnsureInit()
        {
            if (_init) return;
            _init = true;

            var go = new GameObject("A11yGameplayAudio");
            Object.DontDestroyOnLoad(go);
            _pool = new AudioSource[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var s = go.AddComponent<AudioSource>();
                ConfigureSource(s);
                _pool[i] = s;
            }

            _toneSource = go.AddComponent<AudioSource>();
            ConfigureSource(_toneSource);

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

            // audioFieldMap : SfxID -> AudioField (champ prive de l'AudioManager).
            var audio = Manager.audio;
            if (audio != null)
            {
                var f = typeof(AudioManager).GetField("audioFieldMap",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                _fields = f != null ? f.GetValue(audio) as AudioField[] : null;
            }
        }
    }
}
