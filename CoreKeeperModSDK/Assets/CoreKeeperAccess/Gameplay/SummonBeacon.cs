using CoreKeeperAccess.Controls;
using Unity.Mathematics;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Guide sonore vers le sigil d'invocation du boss le plus proche. DEUX regimes :
    // - Boss EN COMBAT (AggroScan.Chasers a un IsBoss, meme critere que la sentinelle
    //   d'aggro) -> drone INCONDITIONNEL, meme hors ecran. Une arene peut deborder de
    //   l'ecran ; se faire kite loin du centre en plein combat est precisement le
    //   moment ou il faut pouvoir y revenir. L'attenuation par distance (plancher 15 %,
    //   jamais muette) fait deja le "ca s'attenue au loin" sans jamais couper le fil.
    // - Hors combat (exploration, on cherche juste la rune) -> VISIBLE A L'ECRAN
    //   (meme boite ecran orthographicSize/aspect que RelayBeacon), comme un relais :
    //   pas de guidage fantome vers une arene qu'on ne visite pas encore.
    // Pan est-ouest + pitch nord-sud. TTS "Rune d'invocation" a 1,5 case.
    // Detection = SummonScan, publie par TileReaderSystem (~4 Hz).
    internal static class SummonBeacon
    {
        private const float Volume = 0.12f;

        private static bool _announced;

        private static bool BossInCombat()
        {
            if (!AggroScan.ResultValid) return false;
            for (int i = 0; i < AggroScan.Count; i++)
                if (AggroScan.Chasers[i].IsBoss) return true;
            return false;
        }

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

            if (!BossInCombat())
            {
                var gameCam = Manager.camera != null ? Manager.camera.gameCamera : null;
                float halfH = gameCam != null ? gameCam.orthographicSize : 11f;
                float halfW = gameCam != null ? gameCam.orthographicSize * gameCam.aspect : 22f;
                if (Mathf.Abs(d.x) > halfW || Mathf.Abs(d.y) > halfH)
                {
                    GameplayAudio.SetCenterDrone(false, 0f, 1f, 0f);
                    _announced = false;
                    return;
                }
            }

            float dist = math.length(d);

            if (dist < 1.5f && !_announced)
            {
                _announced = true;
                CoreKeeperAccess.Patches.TtsText.Say(
                    CoreKeeperAccess.Localization.Strings.L("obj.SummonArea"), true);
            }
            if (dist >= 3f) _announced = false;

            // Pan/pitch bornes a une case max de 20x20 (+-10 cases par axe), DEDIE a ce
            // drone - contrairement au pan/pitch partages (GameplayAudio.PanFromTiles,
            // echelle 12) utilises par le curseur/laser/tick. Au-dela de +-10, les valeurs
            // saturent (plus de pan/pitch supplementaire), seul le VOLUME continue de
            // baisser (DistanceTrim, plancher 15%) tant qu'on est en combat de boss.
            float pan = Mathf.Clamp(d.x / 10f, -1f, 1f);
            float pitch = Mathf.Clamp(Mathf.Pow(2f, d.y / 10f), 0.5f, 2f);
            GameplayAudio.SetCenterDrone(true, pan, pitch, Volume);
        }
    }
}
