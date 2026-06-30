using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Patches;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Drone directionnel vers le relais non active visible a l'ecran le plus proche.
    // Timbre sine 440 Hz + LFO volume 4 Hz (SetRelayDrone).
    // Scan ECS a basse frequence (~1 Hz) ; update audio chaque frame.
    internal static class RelayBeacon
    {
        private const float Volume = 0.12f;
        private const float ScanHz = 1f;

        private static float2 _target;
        private static bool   _hasTarget;
        private static float  _scanTimer;
        private static bool   _announced;

        private static float _previewStopAt;

        public static void StartPreview()
        {
            _previewStopAt = Time.unscaledTime + 2f;
            GameplayAudio.SetRelayDrone(true, 0f, 1f, Volume);
        }

        public static void StopPreview()
        {
            if (_previewStopAt <= 0f) return;
            GameplayAudio.SetRelayDrone(false, 0f, 1f, 0f);
            _previewStopAt = 0f;
        }

        public static void PreviewTick()
        {
            if (_previewStopAt > 0f && Time.unscaledTime >= _previewStopAt) StopPreview();
        }

        public static void Tick()
        {
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null || !InputContext.InGameFree)
            {
                GameplayAudio.SetRelayDrone(false, 0f, 1f, 0f);
                _hasTarget = false;
                _announced = false;
                return;
            }

            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = 1f / ScanHz;
                float2 pp = new float2(player.WorldPosition.x, player.WorldPosition.z);
                _hasTarget = TryFindNearestVisibleRelay(pp, out _target);
            }

            if (!_hasTarget)
            {
                GameplayAudio.SetRelayDrone(false, 0f, 1f, 0f);
                _announced = false;
                return;
            }

            float2 me   = new float2(player.WorldPosition.x, player.WorldPosition.z);
            float2 d    = _target - me;
            float dist  = math.length(d);

            if (dist < 1.5f && !_announced)
            {
                _announced = true;
                TtsText.Say(Localization.Strings.L("relay.nearby"), true);
            }
            if (dist >= 3f) _announced = false;

            float pan   = GameplayAudio.PanFromTiles(d.x);
            float pitch = Mathf.Clamp(Mathf.Pow(2f, d.y / 12f), 0.5f, 2f);
            GameplayAudio.SetRelayDrone(true, pan, pitch, Volume);
        }

        private static bool TryFindNearestVisibleRelay(float2 playerPos, out float2 result)
        {
            result = float2.zero;
            var world = Manager.ecs != null ? Manager.ecs.ClientWorld : null;
            if (world == null) return false;

            var gameCam = Manager.camera != null ? Manager.camera.gameCamera : null;
            float halfH = gameCam != null ? gameCam.orthographicSize             : 11f;
            float halfW = gameCam != null ? gameCam.orthographicSize * gameCam.aspect : 22f;

            var markers = Object.FindObjectsByType<MapMarkerUIElement>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            float bestDist = float.MaxValue;
            bool  found    = false;

            foreach (var m in markers)
            {
                if (m.markerType != MapMarkerType.Portal && m.markerType != MapMarkerType.Waypoint)
                    continue;
                if (m.mapMarkerEntity == Entity.Null) continue;

                if (!EntityUtility.HasComponentData<MapMarkerActivatedCD>(m.mapMarkerEntity, world))
                    continue;
                if (EntityUtility.GetComponentData<MapMarkerActivatedCD>(m.mapMarkerEntity, world).Value)
                    continue;

                if (!EntityUtility.HasComponentData<LocalTransform>(m.mapMarkerEntity, world))
                    continue;

                var pos3 = EntityUtility.GetComponentData<LocalTransform>(m.mapMarkerEntity, world).Position;
                float2 pos = new float2(pos3.x, pos3.z);

                // Relais du Core fixe pres de l'origine
                if (math.length(pos) < 20f) continue;

                float2 delta = pos - playerPos;
                if (math.abs(delta.x) > halfW || math.abs(delta.y) > halfH) continue;

                float d2 = math.lengthsq(pos - playerPos);
                if (d2 < bestDist) { bestDist = d2; result = pos; found = true; }
            }

            return found;
        }
    }
}
