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
            var src = _pool[_poolIdx];
            _poolIdx = (_poolIdx + 1) % PoolSize;
            src.panStereo = Mathf.Clamp(pan, -1f, 1f);
            src.pitch = Mathf.Clamp(pitch, 0.05f, 4f);
            src.PlayOneShot(clip, volume);
        }

        private static AudioSource _toneSource;
        private static AudioClip _toneClip;

        // Bip sinusoidal GENERE (aucun asset) : 40 ms de sinus a 440 Hz, fondu
        // d'attaque/sortie de 5 ms contre les clics. Source DEDIEE hors pool : les
        // appelants (sentinelle d'aggro) sequencent leurs bips en file, jamais
        // superposes, donc une seule source suffit et son pitch est libre a chaque bip.
        public static void PlayTone(float pan, float pitch, float volume = 1f)
        {
            EnsureInit();
            if (_toneSource == null) return;

            if (_toneClip == null)
            {
                const int rate = 44100;
                const int len = rate * 40 / 1000;   // 40 ms
                const int fade = rate * 5 / 1000;   // 5 ms
                var data = new float[len];
                double w = 2.0 * System.Math.PI * 440.0 / rate;
                for (int i = 0; i < len; i++)
                {
                    float env = 1f;
                    if (i < fade) env = i / (float)fade;
                    else if (i >= len - fade) env = (len - 1 - i) / (float)fade;
                    data[i] = (float)System.Math.Sin(w * i) * env;
                }
                _toneClip = AudioClip.Create("A11ySineBeep", len, 1, rate, false);
                _toneClip.SetData(data, 0);
            }

            _toneSource.panStereo = Mathf.Clamp(pan, -1f, 1f);
            _toneSource.pitch = Mathf.Clamp(pitch, 0.05f, 4f);
            _toneSource.PlayOneShot(_toneClip, volume);
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
                        AudioManager.Sfx(id, pos, s.volume * volume, s.pitch * pitchMul, 0f);
                    }
                    return;
                }
            }

            // Reflection indisponible : on laisse le jeu tout gerer (timbre/couches fideles)
            // au prix du random pitch reinjecte par la table. Mieux que pas de son.
            AudioManager.Sfx(sfxTableID, pos, volume, pitchMul);
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
                s.playOnAwake = false;
                s.spatialBlend = 0f; // 2D : on gere le pan/pitch nous-memes
                _pool[i] = s;
            }

            _toneSource = go.AddComponent<AudioSource>();
            _toneSource.playOnAwake = false;
            _toneSource.spatialBlend = 0f;

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
