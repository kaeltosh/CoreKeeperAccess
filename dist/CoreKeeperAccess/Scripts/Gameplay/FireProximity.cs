using CoreKeeperAccess.Controls;
using Unity.Mathematics;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Detecteur de proximite des ZONES DE FEU (premier jet, 13 juin). Le feu au sol
    // (zones AoE de Malugaz, pieges de flammes...) n'est PAS une tuile : ce sont des
    // ENTITES (ObjectID FireAoeDamage / FireTrap / OilFireTrap) -> on les lit dans
    // l'index case->objet existant (il capte les entites sans collider, deja reconstruit
    // ~4 Hz). Alerte positionnelle (pan est-ouest + pitch nord-sud) quand une zone est a
    // <= AlertRange cases ; rappel a la cadence tant qu'elle reste proche. Le joueur
    // ESQUIVE lui-meme (perception, pas assist). Son = SfxID.dg2 (choix utilisateur, son
    // natif court du jeu).
    // PREMIER JET : la zone la plus proche seulement (agglomeration
    // en clusters + alerte de contact dediee a ajouter apres validation de la detection).
    internal static class FireProximity
    {
        private const float AlertRange = 2f;     // cases : seuil d'alerte de proximite
        private const float ScanInterval = 0.1f; // ~10 Hz : frequence d'iteration de l'index
        private const float BeepInterval = 0.15f; // rappel rapide (crepitement dense, zone proche)
        private const SfxID FireSfx = SfxID.dg2;   // son de proximite feu (choix utilisateur)
        private const float Volume = 1.2f;         // pousse : dg2 est attenue par la normalisation

        private static float _nextScan;
        private static float _nextBeep;

        public static void Tick()
        {
            var p = Manager.main != null ? Manager.main.player : null;
            if (p == null || !InputContext.InGameFree) return;
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + ScanInterval;

            int2 me = new int2(
                (int)math.round(p.WorldPosition.x),
                (int)math.round(p.WorldPosition.z));

            // Zone de feu la plus proche dans l'index (cle = case encodee, cf. ObjectIndex.Key).
            bool found = false;
            float best = float.MaxValue;
            int2 bestTile = default;
            foreach (var kv in ObjectIndex.Map)
            {
                if (!IsFire(kv.Value.Id)) continue;
                int2 t = new int2((int)(kv.Key >> 32), (int)(kv.Key & 0xFFFFFFFF));
                float d2 = math.distancesq(new float2(t.x, t.y), new float2(me.x, me.y));
                if (d2 < best) { best = d2; bestTile = t; found = true; }
            }
            if (!found) return;

            float dist = math.sqrt(best);
            if (dist > AlertRange) return;
            if (Time.unscaledTime < _nextBeep) return;
            _nextBeep = Time.unscaledTime + BeepInterval;

            int dx = bestTile.x - me.x, dy = bestTile.y - me.y;
            // Portee courte (<= AlertRange) -> on EXAGERE pan et pitch, sinon la direction
            // est imperceptible (le bareme commun 1/12 par case ne bouge presque pas sur
            // 2 cases) : plein pan a la portee max, ~1 octave de pitch a la portee max.
            float pan = Mathf.Clamp((float)dx / AlertRange, -1f, 1f);
            float pitch = Mathf.Clamp(Mathf.Pow(2f, dy / 2f), 0.5f, 2f);
            GameplayAudio.PlaySpatial(FireSfx, pan, pitch, Volume * GameplayAudio.DistanceTrim(dist));
        }

        private static bool IsFire(ObjectID id)
            => id == ObjectID.FireAoeDamage
            || id == ObjectID.FireTrap
            || id == ObjectID.OilFireTrap;
    }
}
