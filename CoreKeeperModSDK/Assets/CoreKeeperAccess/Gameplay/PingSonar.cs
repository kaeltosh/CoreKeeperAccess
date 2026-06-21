using System.Collections.Generic;
using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Navigation;
using CoreKeeperAccess.Patches;
using Interaction;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Ping sonar (Triangle + L1) : la "photo sonore" de l'environnement - le coup d'oeil
    // circulaire du voyant. Un appui = une salve de bips spatialises, un par cible
    // notable autour du joueur, egrenes du PLUS PROCHE au PLUS LOIN (l'ordre temporel
    // encode la distance). Trois timbres, langage du laser reutilise : hostile, creature
    // paisible, trouvaille (zone de fouille). Pas de TTS dans la salve (le timbre donne
    // la categorie) ; "Rien autour" si vide. Pendant la salve, le laser et la sentinelle
    // se taisent (fenetre sonore reservee, via Silencing).
    // Creatures via PingScan (systeme ECS) ; trouvailles lues directement dans
    // ObjectIndex (rempli par le meme systeme, main thread -> lecture sure).
    internal static class PingSonar
    {
        private const float Radius = 12f;          // rayon en cases (= portee du laser)
        private const float SlotInterval = 0.12f;  // espacement des bips de la salve
        private const int MaxBeeps = 12;           // plafond (au-dela : le plus proche d'abord)
        private const float ResultTimeout = 1f;    // garde-fou : ne jamais rester gele

        // Timbres : memes placeholders que le laser (l'utilisateur choisira les vrais).
        private const SfxID HostileSfx = SfxID.proximity_sensor_set;
        private const SfxID CreatureSfx = SfxID.inventory_doot;
        private const SfxID FindSfx = SfxID.inventory_ding;
        private const float HostileVolume = 0.5f;
        private const float PassiveVolume = 0.35f;

        private struct Beep
        {
            public float2 Pos;
            public SfxID Sfx;
            public float Volume;
            public float DistSq; // tri proche -> loin
        }

        private static readonly List<Beep> _salvo = new List<Beep>();
        private static bool _pending;     // demande posee, en attente du scan systeme
        private static float _requestedAt;
        private static int _next;         // prochain bip de la salve
        private static float _nextTime;

        // Fenetre sonore reservee : laser et sentinelle consultent ce flag.
        public static bool Silencing => _pending || _next < _salvo.Count;

        public static void Trigger(PlayerController player)
        {
            if (Silencing) return; // salve en cours : on ne rearme pas par-dessus
            PingScan.Center = new float2(player.WorldPosition.x, player.WorldPosition.z);
            PingScan.Radius = Radius;
            PingScan.ResultValid = false;
            PingScan.Requested = true;
            _pending = true;
            _requestedAt = Time.unscaledTime;
            _salvo.Clear();
            _next = 0;
        }

        public static void Tick(PlayerController player)
        {
            if (player == null) { _pending = false; _salvo.Clear(); _next = 0; return; }

            if (_pending)
            {
                if (PingScan.ResultValid) BuildSalvo(player);
                else if (Time.unscaledTime - _requestedAt > ResultTimeout) _pending = false;
                else return;
            }

            // Egrene la salve : un bip par creneau, pan/pitch recalcules a la position
            // COURANTE du joueur (s'il marche pendant la salve, l'image reste juste).
            if (_next < _salvo.Count && Time.unscaledTime >= _nextTime)
            {
                var b = _salvo[_next++];
                float2 d = b.Pos - new float2(player.WorldPosition.x, player.WorldPosition.z);
                float pan = GameplayAudio.PanFromTiles(d.x);
                float pitch = Mathf.Clamp(Mathf.Pow(2f, d.y / 12f), 0.5f, 2f);
                GameplayAudio.PlaySpatial(b.Sfx, pan, pitch,
                    b.Volume * GameplayAudio.DistanceTrim(math.length(d)));
                _nextTime = Time.unscaledTime + SlotInterval;
            }
        }

        // Fusionne creatures (PingScan) + trouvailles (ObjectIndex), trie du plus
        // proche au plus loin, tronque au plafond.
        private static void BuildSalvo(PlayerController player)
        {
            _pending = false;
            _salvo.Clear();
            _next = 0;
            float2 center = new float2(player.WorldPosition.x, player.WorldPosition.z);

            for (int i = 0; i < PingScan.Count; i++)
            {
                var t = PingScan.Targets[i];
                _salvo.Add(new Beep
                {
                    Pos = t.Pos,
                    Sfx = t.Hostile ? HostileSfx : CreatureSfx,
                    Volume = t.Hostile ? HostileVolume : PassiveVolume,
                    DistSq = math.lengthsq(t.Pos - center),
                });
            }

            // Trouvailles : objets de l'index dans le rayon. Un spot multi-cases est
            // marque sur plusieurs cases -> dedup grossiere (une trouvaille deja
            // retenue a moins de 2 cases absorbe la suivante).
            float r2 = Radius * Radius;
            foreach (var kv in ObjectIndex.Map)
            {
                if (!IsFind(kv.Value.Id)) continue;
                float2 p = new float2((int)(kv.Key >> 32), (int)(uint)kv.Key);
                float distSq = math.lengthsq(p - center);
                if (distSq > r2) continue;
                bool dup = false;
                for (int i = 0; i < _salvo.Count; i++)
                {
                    if (_salvo[i].Sfx == FindSfx && math.lengthsq(_salvo[i].Pos - p) <= 4f)
                    { dup = true; break; }
                }
                if (dup) continue;
                _salvo.Add(new Beep { Pos = p, Sfx = FindSfx, Volume = PassiveVolume, DistSq = distSq });
            }

            if (_salvo.Count == 0)
            {
                TtsText.Say(Strings.L("ping.none"), true);
                return;
            }

            _salvo.Sort((a, b) => a.DistSq.CompareTo(b.DistSq));
            if (_salvo.Count > MaxBeeps) _salvo.RemoveRange(MaxBeeps, _salvo.Count - MaxBeeps);
            _nextTime = Time.unscaledTime; // premier bip immediat
        }

        // Trouvailles reconnues : les zones de fouille (toutes variantes de biome).
        // Liste a etendre au fil des decouvertes du meme genre.
        private static bool IsFind(ObjectID id)
            => id == ObjectID.DiggingSpot
            || id == ObjectID.DiggingSpotNature
            || id == ObjectID.DiggingSpotSea
            || id == ObjectID.DiggingSpotDesert
            || id == ObjectID.DiggingSpotLava
            || id == ObjectID.DiggingSpotExcavation;
    }
}
