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
        private static AudioSource _src;
        private static AudioField[] _fields;
        private static bool _init;

        // Joue le son a la hauteur (pitch) et au panoramique (pan, -1..+1) donnes.
        public static void PlaySpatial(SfxID id, float pan, float pitch, float volume = 1f)
        {
            EnsureInit();
            if (_src == null || _fields == null) return;

            int idx = (int)id;
            if (idx < 0 || idx >= _fields.Length || _fields[idx] == null) return;
            var clip = _fields[idx].GetNextAudioClip();
            if (clip == null) return;

            _src.panStereo = Mathf.Clamp(pan, -1f, 1f);
            _src.pitch = Mathf.Clamp(pitch, 0.05f, 4f);
            _src.PlayOneShot(clip, volume);
        }

        private static void EnsureInit()
        {
            if (_init) return;
            _init = true;

            var go = new GameObject("A11yGameplayAudio");
            Object.DontDestroyOnLoad(go);
            _src = go.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.spatialBlend = 0f; // 2D : on gere le pan/pitch nous-memes

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
