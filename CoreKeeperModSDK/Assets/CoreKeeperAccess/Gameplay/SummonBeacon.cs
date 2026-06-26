using CoreKeeperAccess.Controls;
using Unity.Mathematics;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Guide sonore vers le sigil d'invocation du boss le plus proche. Quand une
    // SummonArea est a portee, un drone sinusoidal continu indique sa direction :
    // pan est-ouest + pitch nord-sud. TTS "Rune d'invocation" a 1,5 case.
    // Detection = SummonScan, publie par TileReaderSystem (~4 Hz).
    internal static class SummonBeacon
    {
        private const float Range = 10f;
        private const float Volume = 0.12f;

        private static bool _announced;

        public static void Tick()
        {
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null || !InputContext.InGameFree || !SummonScan.Found)
            {
                GameplayAudio.SetCenterDrone(false, 0f, 1f, 0f);
                _announced = false;
                return;
            }

            float2 me = new float2(player.WorldPosition.x, player.WorldPosition.z);
            float2 d = SummonScan.Pos - me;
            float dist = math.length(d);

            if (dist > Range)
            {
                GameplayAudio.SetCenterDrone(false, 0f, 1f, 0f);
                _announced = false;
                return;
            }

            if (dist < 1.5f && !_announced)
            {
                _announced = true;
                CoreKeeperAccess.Patches.TtsText.Say(
                    CoreKeeperAccess.Localization.Strings.L("obj.SummonArea"), true);
            }
            if (dist >= 3f) _announced = false;

            float pan = GameplayAudio.PanFromTiles(d.x);
            float pitch = Mathf.Clamp(Mathf.Pow(2f, d.y / 12f), 0.5f, 2f);
            GameplayAudio.SetCenterDrone(true, pan, pitch, Volume);
        }
    }
}
