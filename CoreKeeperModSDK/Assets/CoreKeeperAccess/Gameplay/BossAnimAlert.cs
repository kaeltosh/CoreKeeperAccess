using System.Collections.Generic;
using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Alerte des actions du boss de la ruche (Hive Mother) via lecture de l'AnimationBuffer
    // replique cote client (GhostField confirme). Deux signaux detectables :
    //   - hash 1203776827 : tir acide (ShootMortarProjectileStateSystem / MeleeAttackStateSystem)
    //   - hash 1354651601 : enrage (EnrageStateSystem)
    // Le tir acide declenche egalement un scan immediat des oeufs actifs (LarvaHiveEgg
    // health>0) et les annonce positionnellement. Le scan des oeufs est ponctuel (declenche
    // par l'evenement, pas permanent) -> pas de cout hors combat du boss.
    //
    // Architecture pont : BossAnimScan publie les donnees ECS,
    // BossAnimAlertSystem fait le scan, BossAnimAlert.Tick() consomme et sonifie.
    internal static class BossAnimAlert
    {
        private const int AcidAttackAnimID = 1203776827;
        private const int EnrageAnimID     = 1354651601;

        private static int _lastVersion;

        public static void Tick()
        {
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null) { BossAnimScan.Active = false; return; }
            if (!InputContext.InGameFree) { BossAnimScan.Active = false; return; }

            BossAnimScan.PlayerPos = new float2(player.WorldPosition.x, player.WorldPosition.z);
            BossAnimScan.Active = true;

            if (!BossAnimScan.ResultValid) return;
            if (BossAnimScan.Version == _lastVersion) return;
            _lastVersion = BossAnimScan.Version;
            if (BossAnimScan.EventCount == 0) return;

            float2 playerPos = BossAnimScan.PlayerPos;
            for (int i = 0; i < BossAnimScan.EventCount; i++)
            {
                var ev = BossAnimScan.Events[i];
                if (ev.AnimID == AcidAttackAnimID)
                {
                    TtsText.Say(Strings.L("boss.hive.acid"), true);
                    PlayAlert(ev.BossPos, playerPos);
                    AnnounceEggs(playerPos);
                }
                else if (ev.AnimID == EnrageAnimID)
                {
                    TtsText.Say(Strings.L("boss.hive.enrage"), true);
                    PlayAlert(ev.BossPos, playerPos);
                }
            }
        }

        private static void AnnounceEggs(float2 playerPos)
        {
            int n = BossAnimScan.ActiveEggCount;
            if (n == 0) return;
            string key = n == 1 ? "boss.hive.egg_one" : "boss.hive.eggs_n";
            TtsText.Say(Strings.L(key).Replace("{0}", n.ToString()), false);
            for (int i = 0; i < n; i++)
            {
                float2 d = BossAnimScan.ActiveEggs[i] - playerPos;
                float pan   = GameplayAudio.PanFromTiles(d.x);
                float pitch = Mathf.Pow(2f, d.y / 12f);
                GameplayAudio.PlaySpatial(SfxID.proximity_sensor_set, pan, pitch,
                    0.6f * A11ySettings.SentinelBossVolume);
            }
        }

        private static void PlayAlert(float2 bossPos, float2 playerPos)
        {
            float2 d   = bossPos - playerPos;
            float pan   = GameplayAudio.PanFromTiles(d.x);
            float pitch = Mathf.Pow(2f, d.y / 12f);
            GameplayAudio.PlaySpatial(SfxID.dg2, pan, pitch,
                0.8f * A11ySettings.SentinelBossVolume);
        }
    }

    // Donnees publiees par BossAnimAlertSystem, consommees par BossAnimAlert.Tick.
    internal static class BossAnimScan
    {
        public struct AnimEvent
        {
            public int    AnimID;
            public float2 BossPos;
        }

        public static bool   Active;
        public static float2 PlayerPos;
        public static bool   ResultValid;
        public static int    Version;
        public static int    EventCount;
        public static readonly AnimEvent[] Events = new AnimEvent[8];

        public static int    ActiveEggCount;
        public static readonly float2[] ActiveEggs = new float2[16];
    }

    // Systeme ECS client-side. Scan a 10 Hz des entites LarvaHiveBoss : detecte les
    // nouvelles entrees dans leur AnimationBuffer (via AnimationBufferPointer.NextIndex).
    // Si un tir acide est detecte, scanne immediatement les LarvaHiveEgg actifs.
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class BossAnimAlertSystem : SystemBase
    {
        private EntityQuery _bossQuery;
        private EntityQuery _eggQuery;
        private readonly Dictionary<long, byte> _lastIdx = new Dictionary<long, byte>();
        private const float ScanInterval = 0.1f; // 10 Hz
        private float _next;

        protected override void OnCreate()
        {
            _bossQuery = GetEntityQuery(
                ComponentType.ReadOnly<ObjectDataCD>(),
                ComponentType.ReadOnly<AnimationBufferPointer>(),
                ComponentType.ReadOnly<LocalTransform>());
            _eggQuery = GetEntityQuery(
                ComponentType.ReadOnly<ObjectDataCD>(),
                ComponentType.ReadOnly<HealthCD>(),
                ComponentType.ReadOnly<LocalTransform>());
            Diag.Log("A11yBossAnimDiag", "BossAnimAlertSystem cree dans " + World.Name);
        }

        protected override void OnUpdate()
        {
            if (!BossAnimScan.Active) return;
            if (UnityEngine.Time.unscaledTime < _next) return;
            _next = UnityEngine.Time.unscaledTime + ScanInterval;

            try
            {
                int  eventCount = 0;
                int  eggCount   = 0;
                bool scanEggs   = false;

                var bossEnts = _bossQuery.ToEntityArray(Allocator.Temp);
                foreach (var e in bossEnts)
                {
                    var obj = EntityManager.GetComponentData<ObjectDataCD>(e);
                    if (obj.objectID != ObjectID.LarvaHiveBoss) continue;

                    var ptr = EntityManager.GetComponentData<AnimationBufferPointer>(e);
                    long key = EntityKey.Of(e);
                    byte ni  = ptr.NextIndex;
                    byte last;
                    bool known = _lastIdx.TryGetValue(key, out last);
                    _lastIdx[key] = ni;
                    if (!known || ni == last) continue;

                    var buf = EntityManager.GetBuffer<AnimationBuffer>(e);
                    if (buf.Length == 0) continue;
                    // NextIndex = prochaine position d'ecriture dans le ring buffer ;
                    // le dernier animID ecrit est a (NextIndex-1+256) % buf.Length.
                    int idx    = ((int)ni - 1 + 256) % buf.Length;
                    int animID = buf[idx].animID;

                    if (eventCount >= BossAnimScan.Events.Length) continue;
                    var pos = EntityManager.GetComponentData<LocalTransform>(e).Position;
                    BossAnimScan.Events[eventCount++] = new BossAnimScan.AnimEvent
                    {
                        AnimID  = animID,
                        BossPos = new float2(pos.x, pos.z),
                    };
                    if (animID == 1203776827) scanEggs = true;
                }
                bossEnts.Dispose();

                if (scanEggs)
                {
                    var eggEnts = _eggQuery.ToEntityArray(Allocator.Temp);
                    foreach (var e in eggEnts)
                    {
                        if (eggCount >= BossAnimScan.ActiveEggs.Length) break;
                        var obj = EntityManager.GetComponentData<ObjectDataCD>(e);
                        if (obj.objectID != ObjectID.LarvaHiveEgg) continue;
                        var hp = EntityManager.GetComponentData<HealthCD>(e);
                        if (hp.health <= 0) continue;
                        var pos = EntityManager.GetComponentData<LocalTransform>(e).Position;
                        BossAnimScan.ActiveEggs[eggCount++] = new float2(pos.x, pos.z);
                    }
                    eggEnts.Dispose();
                }

                BossAnimScan.EventCount     = eventCount;
                BossAnimScan.ActiveEggCount = eggCount;
                BossAnimScan.Version++;
                BossAnimScan.ResultValid = true;
            }
            catch (System.Exception ex) { Diag.Error("A11yBossAnimDiag", ex); }
        }
    }
}
