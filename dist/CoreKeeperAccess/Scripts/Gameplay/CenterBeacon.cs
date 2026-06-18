using CoreKeeperAccess.Controls;
using Unity.Mathematics;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Repere de centre d'arene (placeholder, 13 juin). Quand une zone d'invocation
    // (SummonArea = centre de l'arene de boss) est a portee, un drone sinusoidal doux et
    // CONTINU indique sa direction : pan est-ouest + pitch nord-sud (langage positionnel
    // du mod), par rapport au joueur. Sert AVANT le combat (poser le crane pile au centre,
    // fini le tatonnement) ET pendant (se situer dans le disque). Volume tres faible
    // (ambiance, jamais couvrant). Portee calee sur l'arene (~16 cases de diametre ->
    // rayon ~8, + marge). Detection = CenterScan, publie par TileReaderSystem au fil de
    // son scan d'objets. Son = sinus genere placeholder (l'utilisateur choisira).
    internal static class CenterBeacon
    {
        private const float Range = 10f;     // portee max (rayon arene ~8 + marge)
        private const float Volume = 0.12f;  // tres faible (placeholder, a regler a l'oreille)

        public static void Tick()
        {
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null || !InputContext.InGameFree || !CenterScan.Found)
            {
                GameplayAudio.SetCenterDrone(false, 0f, 1f, 0f);
                return;
            }

            float2 me = new float2(player.WorldPosition.x, player.WorldPosition.z);
            float2 d = CenterScan.Pos - me;
            float dist = math.length(d);
            if (dist > Range)
            {
                GameplayAudio.SetCenterDrone(false, 0f, 1f, 0f);
                return;
            }

            float pan = GameplayAudio.PanFromTiles(d.x);
            float pitch = Mathf.Clamp(Mathf.Pow(2f, d.y / 12f), 0.5f, 2f);
            GameplayAudio.SetCenterDrone(true, pan, pitch, Volume);
        }
    }
}
