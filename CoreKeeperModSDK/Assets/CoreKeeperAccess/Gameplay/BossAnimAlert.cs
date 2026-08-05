using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using HarmonyLib;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Alerte des actions de la Hive Mother via Animation Events du MonoBehaviour.
    //
    // - Tir acide : AE_AnticipationSound() est appellée par le système d'animation
    //   Unity juste avant le tir → TTS + son spatial + scan des œufs actifs.
    //
    // L'ENRAGE n'est plus traité ici (5 août 2026) : EnrageStateCD est commun à la moitié
    // des boss du jeu, il est annoncé par le socle générique (BossAnnounce) — plus de
    // recherche du MonoBehaviour à chaque frame non plus.
    //
    // Architecture : patch Harmony (tir) + BossEggScanSystem ECS (query précompilée pour
    // les œufs, déclenchée à la demande).
    internal static class BossAnimAlert
    {
        private static bool _eggResultPending;

        public static void Tick()
        {
            if (!InputContext.InGameFree) { _eggResultPending = false; return; }

            // Consomme le scan d'œufs déclenché par AE_AnticipationSound
            if (_eggResultPending && BossEggScan.ResultReady)
            {
                _eggResultPending = false;
                AnnounceEggs();
            }
        }

        // Appelé par HiveBossAnticipationPatch (Animation Event AE_AnticipationSound)
        internal static void OnAnticipation(LarvaHiveBoss boss)
        {
            if (!InputContext.InGameFree) return;
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null) return;

            // Télégraphe = urgent : passe par le goulot commun en priorité danger (donc
            // sans attendre le créneau), le son spatial part lui immédiatement.
            BossAnnounce.Enqueue(Strings.L("boss.hive.acid"), BossAnnounce.PrioDanger);
            PlayBossSound(boss);

            // Déclenche le scan des œufs (résultat disponible la frame suivante)
            float2 pp = new float2(player.WorldPosition.x, player.WorldPosition.z);
            BossEggScan.PlayerPos   = pp;
            BossEggScan.ResultReady = false;
            BossEggScan.Requested   = true;
            _eggResultPending = true;
        }

        private static void AnnounceEggs()
        {
            int n = BossEggScan.Count;
            if (n == 0) return;
            string key = n == 1 ? "boss.hive.egg_one" : "boss.hive.eggs_n";
            BossAnnounce.Enqueue(Strings.L(key).Replace("{0}", n.ToString()), BossAnnounce.PrioState);
            float2 pp = BossEggScan.PlayerPos;
            for (int i = 0; i < n; i++)
            {
                float2 d = BossEggScan.Positions[i] - pp;
                GameplayAudio.PlaySpatial(SfxID.proximity_sensor_set,
                    GameplayAudio.PanFromTiles(d.x),
                    Mathf.Pow(2f, d.y / 12f),
                    0.6f * A11ySettings.SentinelBossVolume);
            }
        }

        private static void PlayBossSound(LarvaHiveBoss boss)
        {
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null) return;
            float2 pp = new float2(player.WorldPosition.x, player.WorldPosition.z);
            float2 d  = new float2(boss.transform.position.x, boss.transform.position.z) - pp;
            GameplayAudio.PlaySpatial(SfxID.dg2,
                GameplayAudio.PanFromTiles(d.x),
                Mathf.Pow(2f, d.y / 12f),
                0.8f * A11ySettings.SentinelBossVolume);
        }
    }

    // Pont BossAnimAlert ↔ BossEggScanSystem
    internal static class BossEggScan
    {
        public static bool    Requested;
        public static bool    ResultReady;
        public static float2  PlayerPos;
        public static int     Count;
        public static readonly float2[] Positions = new float2[16];
    }

    // Système ECS client-side : scanne les LarvaHiveEgg actifs (health>0) à la demande.
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class BossEggScanSystem : SystemBase
    {
        private EntityQuery _eggQuery;

        protected override void OnCreate()
        {
            _eggQuery = GetEntityQuery(
                ComponentType.ReadOnly<ObjectDataCD>(),
                ComponentType.ReadOnly<HealthCD>(),
                ComponentType.ReadOnly<LocalTransform>());
        }

        protected override void OnUpdate()
        {
            if (!BossEggScan.Requested) return;
            BossEggScan.Requested = false;

            try
            {
                int n = 0;
                var ents = _eggQuery.ToEntityArray(Allocator.Temp);
                foreach (var e in ents)
                {
                    if (n >= BossEggScan.Positions.Length) break;
                    var od = EntityManager.GetComponentData<ObjectDataCD>(e);
                    if (od.objectID != ObjectID.LarvaHiveEgg) continue;
                    var hp = EntityManager.GetComponentData<HealthCD>(e);
                    if (hp.health <= 0) continue;
                    var pos = EntityManager.GetComponentData<LocalTransform>(e).Position;
                    BossEggScan.Positions[n++] = new float2(pos.x, pos.z);
                }
                ents.Dispose();
                BossEggScan.Count       = n;
                BossEggScan.ResultReady = true;
            }
            catch (System.Exception ex) { Diag.Error("A11yBossEggDiag", ex); }
        }
    }

    // Patch sur l'Animation Event du boss : appelé par l'animator juste avant le tir acide.
    [HarmonyPatch(typeof(LarvaHiveBoss), "AE_AnticipationSound")]
    internal static class HiveBossAnticipationPatch
    {
        [HarmonyPostfix]
        public static void Postfix(LarvaHiveBoss __instance)
        {
            try { BossAnimAlert.OnAnticipation(__instance); }
            catch (System.Exception ex) { Diag.Error("A11yBossAnimPatch", ex); }
        }
    }
}
