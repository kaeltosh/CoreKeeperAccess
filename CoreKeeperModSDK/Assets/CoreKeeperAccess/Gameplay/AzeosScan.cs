using System.Collections.Generic;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace CoreKeeperAccess.Gameplay
{
    // Canal sonore d'un pilier de foudre (BirdBossBeam). Determine EXCLUSIVEMENT depuis le
    // ServerWorld (cf. AzeosScanSystem.ScanServer) via le comportement reel du pilier
    // (BirdBossBeamCD.moveDirection/moveSideWays) - jamais par geometrie/statistiques sur les
    // positions (ancienne approche, abandonnee : trop fragile face a la replication reseau
    // qui fragmentait les vagues). BirdBossBeamCD est confirme NON REPLIQUE au ClientWorld
    // (fige a sa valeur de fabrication) - sans ServerWorld (client distant qui rejoint la
    // partie d'un hote), aucune distinction de forme n'est possible : tout tombe sur
    // Aleatoire, qui partage de toute facon le meme filet de securite generique (FireProximity)
    // que None (anneau) - aucun consommateur ne les distingue a ce jour.
    public enum AzeosBeamChannel : byte { None = 0, ColonneV = 1, LigneHaut = 2, LigneBas = 3, Aleatoire = 4 }

    // Pont mod <-> AzeosScanSystem pour les piliers actifs a portee.
    internal static class AzeosBeamScan
    {
        public struct Beam
        {
            public long Key;
            public float2 Pos;
            public AzeosBeamChannel Channel;
        }

        public const int MaxBeams = 64;
        public static bool ResultValid;
        public static int Count;
        public static readonly Beam[] Beams = new Beam[MaxBeams];
    }

    // Pont mod <-> AzeosScanSystem pour les cristaux (BirdBossStone).
    internal static class AzeosStoneScan
    {
        public struct Stone
        {
            public long Key;
            public float2 Pos;
            public bool Active; // HealNearbyEntitiesCD.isActive (repliqu, confirme GhostField)
        }

        public const int MaxStones = 16;
        public static bool ResultValid;
        public static int Count;
        public static readonly Stone[] Stones = new Stone[MaxStones];
    }

    // Pont mod <-> AzeosScanSystem pour le boss lui-meme (etat atterri/enrage).
    internal static class AzeosBossScan
    {
        public static bool Found;
        public static bool HasAppeared; // BirdBossHasAppearedCD.Value (GhostField confirme)
        public static bool Enraged;     // EnrageStateCD.isEnraged (GhostField confirme, meme composant que la Hive Mother)
        public static float2 Pos;
    }

    // Systeme de detection/classification pour le combat Azeos, dedie (separe de
    // TileReaderSystem pour ne pas alourdir un fichier deja consequent - meme choix que
    // BossEggScanSystem pour la Hive Mother). Cadence 10 Hz.
    //
    // Piliers de foudre (BirdBossBeam) : lecture DIRECTE du ServerWorld a CHAQUE poll quand on
    // heberge (solo ou hote multi - le seul cas ou ServerWorld existe dans notre process, cf.
    // ECSManager.StartEcs). Ni replication reseau, ni devinette geometrique : la position et le
    // comportement (moveDirection/moveSideWays) du pilier viennent direct de la simulation qui
    // fait autorite. Client distant (rejoint la partie d'un hote sans le mod) : ServerWorld est
    // null chez lui, fallback minimal ci-dessous (position seule, pas de forme).
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class AzeosScanSystem : SystemBase
    {
        private const float ScanInterval = 0.1f;
        private const int MinWaveForShape = 3;      // sous ce nombre, pas assez de points -> aleatoire par defaut
        private const float BeamMajority = 0.7f;     // 100% attendu sur un motif structure, ~50/50 sur l'aleatoire
        private const float AwaitTimeout = 2f;       // abandon si les piliers ne se materialisent jamais (rate)

        private EntityQuery _objQuery;
        private float _next;

        // Etat d'attaque du boss (screech + armement de la classification).
        private int _prevSpawnBeamsState = -1;
        private bool _awaitingServerWave;
        private float _awaitingSince;

        // Classification des piliers, cle = entite SERVEUR (ServerWorld only).
        private readonly Dictionary<long, AzeosBeamChannel> _serverBeamChannel = new Dictionary<long, AzeosBeamChannel>();
        private readonly HashSet<long> _pendingKeys = new HashSet<long>();
        private readonly List<(long key, float2 pos, float3 dir, bool sideways)> _pendingBatch = new List<(long, float2, float3, bool)>();

        protected override void OnCreate()
        {
            _objQuery = GetEntityQuery(ComponentType.ReadOnly<ObjectDataCD>());
        }

        protected override void OnUpdate()
        {
            float now = UnityEngine.Time.unscaledTime;
            if (now < _next) return;
            _next = now + ScanInterval;

            try { Scan(now); }
            catch (System.Exception ex) { Diag.Error("A11yAzeosScan", ex); }
        }

        private void Scan(float now)
        {
            var serverWorld = Manager.ecs != null ? Manager.ecs.ServerWorld : null;
            bool hosting = serverWorld != null && serverWorld.IsCreated;

            int stoneCount = 0;
            bool bossFound = false; float2 bossPos = default; bool hasAppeared = false; bool enraged = false;

            var ents = _objQuery.ToEntityArray(Allocator.Temp);
            foreach (var e in ents)
            {
                var od = EntityManager.GetComponentData<ObjectDataCD>(e);

                if (od.objectID == ObjectID.BirdBossStone)
                {
                    if (!EntityManager.HasComponent<LocalTransform>(e)) continue;
                    // Cadavre ecarte : vie a zero mais entite encore presente ~2s (destroyTimer
                    // natif, cf. SetEntitiesDestroyedSystem) le temps de l'anim de destruction.
                    if (EntityManager.HasComponent<HealthCD>(e)
                        && EntityManager.GetComponentData<HealthCD>(e).health <= 0) continue;
                    if (stoneCount >= AzeosStoneScan.MaxStones) continue;
                    var pos3 = EntityManager.GetComponentData<LocalTransform>(e).Position;
                    bool active = EntityManager.HasComponent<HealNearbyEntitiesCD>(e)
                        && EntityManager.GetComponentData<HealNearbyEntitiesCD>(e).isActive;
                    AzeosStoneScan.Stones[stoneCount++] = new AzeosStoneScan.Stone
                    {
                        Key = EntityKey.Of(e),
                        Pos = new float2(pos3.x, pos3.z),
                        Active = active,
                    };
                }
                else if (od.objectID == ObjectID.BirdBoss)
                {
                    bossFound = true;
                    if (EntityManager.HasComponent<LocalTransform>(e))
                    {
                        var p3 = EntityManager.GetComponentData<LocalTransform>(e).Position;
                        bossPos = new float2(p3.x, p3.z);
                    }
                    if (EntityManager.HasComponent<BirdBossHasAppearedCD>(e))
                        hasAppeared = EntityManager.GetComponentData<BirdBossHasAppearedCD>(e).Value;
                    if (EntityManager.HasComponent<EnrageStateCD>(e))
                        enraged = EntityManager.GetComponentData<EnrageStateCD>(e).isEnraged;
                }
                // BirdBossBeam n'est PLUS lu ici : cf. ScanServer (ServerWorld) / fallback client.
            }
            ents.Dispose();

            int beamCount = hosting ? ScanServer(serverWorld, now) : ScanClientBeamsFallback();

            AzeosBeamScan.Count = beamCount;
            AzeosBeamScan.ResultValid = true;

            AzeosStoneScan.Count = stoneCount;
            AzeosStoneScan.ResultValid = true;

            AzeosBossScan.Found = bossFound;
            AzeosBossScan.Pos = bossPos;
            AzeosBossScan.HasAppeared = hasAppeared;
            AzeosBossScan.Enraged = enraged;
        }

        // Fallback client distant (pas d'hote local) : aucune forme lisible (BirdBossBeamCD figee,
        // non repliquee) - juste la position brute, canal Aleatoire uniforme pour le filet de
        // securite generique (FireProximity). Pas de bookkeeping de vague : rien a classer.
        private int ScanClientBeamsFallback()
        {
            int count = 0;
            var ents = _objQuery.ToEntityArray(Allocator.Temp);
            foreach (var e in ents)
            {
                if (count >= AzeosBeamScan.MaxBeams) break;
                var od = EntityManager.GetComponentData<ObjectDataCD>(e);
                if (od.objectID != ObjectID.BirdBossBeam) continue;
                if (!EntityManager.HasComponent<LocalTransform>(e)) continue;
                var pos3 = EntityManager.GetComponentData<LocalTransform>(e).Position;
                AzeosBeamScan.Beams[count++] = new AzeosBeamScan.Beam
                {
                    Key = EntityKey.Of(e),
                    Pos = new float2(pos3.x, pos3.z),
                    Channel = AzeosBeamChannel.Aleatoire,
                };
            }
            ents.Dispose();
            return count;
        }

        // Coeur de la classification (ServerWorld uniquement) :
        //  - moveSideWays == true sur un pilier => motif 1 (anneau), decide par le jeu lui-meme,
        //    aucune ambiguite possible.
        //  - sinon, l'axe DOMINANT du moveDirection (X ou Z, chaque pilier tire un cardinal exact,
        //    cf. BirdBossSpawnBeamsJob) tranche colonne/ligne. Un motif structure (2 ou 3) a TOUS
        //    ses piliers sur le MEME axe ; l'aleatoire (motif 0) pioche parmi les 4 cardinaux
        //    independamment par pilier (GetRandomBeamMoveDirection) => environ moitie X, moitie Z,
        //    jamais une majorite nette.
        private AzeosBeamChannel ClassifyShapeFromBeamData(List<(long key, float2 pos, float3 dir, bool sideways)> beams)
        {
            int n = beams.Count;
            int ringN = 0, xN = 0, zN = 0;
            foreach (var b in beams)
            {
                if (b.sideways) { ringN++; continue; }
                if (math.abs(b.dir.x) > math.abs(b.dir.z)) xN++; else zN++;
            }

            if (ringN >= n * BeamMajority) return AzeosBeamChannel.None;
            if (xN >= n * BeamMajority) return AzeosBeamChannel.ColonneV;
            if (zN >= n * BeamMajority)
            {
                float2 centroid = float2.zero;
                foreach (var b in beams) centroid += b.pos;
                centroid /= n;
                float2 player = ObjectIndex.Center;
                return (centroid.y > player.y) ? AzeosBeamChannel.LigneHaut : AzeosBeamChannel.LigneBas;
            }
            return AzeosBeamChannel.Aleatoire;
        }

        // Lit le ServerWorld A CHAQUE poll (non-nul UNIQUEMENT si on heberge, cf. ECSManager.
        // StartEcs) : position ET comportement du pilier viennent direct de la simulation qui
        // fait autorite, sans replication ni devinette. Deux usages du champ prive
        // BirdBossSpawnBeamsStateCD.internalState (jamais reppliqu au client) :
        //  - 0 -> 1 : le cri de telegraphe vient de partir -> alerte precoce (avant meme que
        //    les piliers n'existent).
        //  - franchissement de 2 : les piliers viennent d'etre crees -> armement de la fenetre
        //    de classification (_awaitingServerWave). Le lot est classe des qu'on a assez de
        //    piliers NON ENCORE connus (_serverBeamChannel) ou apres un delai d'abandon.
        private int ScanServer(Unity.Entities.World serverWorld, float now)
        {
            var em = serverWorld.EntityManager;

            var stateQuery = Manager.ecs.GetServerEntityQuery(typeof(BirdBossSpawnBeamsStateCD), typeof(StateInfoCD));
            var stateEnts = stateQuery.ToEntityArray(Allocator.Temp);
            bool inSpawnBeamsState = false;
            int internalState = -1;
            if (stateEnts.Length > 0)
            {
                var stateInfo = em.GetComponentData<StateInfoCD>(stateEnts[0]);
                inSpawnBeamsState = stateInfo.IsCurrentState(StateID.BirdBossSpawnBeams);
                if (inSpawnBeamsState) internalState = em.GetComponentData<BirdBossSpawnBeamsStateCD>(stateEnts[0]).internalState;
            }
            stateEnts.Dispose();

            if (!inSpawnBeamsState) { _prevSpawnBeamsState = -1; }
            else
            {
                if (internalState == 1 && _prevSpawnBeamsState == 0)
                    TtsText.Say(Strings.L("boss.azeos.beams_incoming"), true);

                // Franchissement (pas juste ==2) : si notre poll (10 Hz) saute par-dessus
                // l'etat 1, on arme quand meme des qu'on constate qu'on y est deja.
                if (internalState >= 2 && _prevSpawnBeamsState < 2)
                {
                    _awaitingServerWave = true;
                    _awaitingSince = now;
                    _pendingBatch.Clear();
                    _pendingKeys.Clear();
                }
                _prevSpawnBeamsState = internalState;
            }

            var objQuery = Manager.ecs.GetServerEntityQuery(typeof(ObjectDataCD));
            var objEnts = objQuery.ToEntityArray(Allocator.Temp);
            var currentKeys = new HashSet<long>();
            int count = 0;
            foreach (var se in objEnts)
            {
                var sod = em.GetComponentData<ObjectDataCD>(se);
                if (sod.objectID != ObjectID.BirdBossBeam) continue;
                if (!em.HasComponent<LocalTransform>(se) || !em.HasComponent<BirdBossBeamCD>(se)) continue;

                long key = EntityKey.Of(se);
                currentKeys.Add(key);
                var p3 = em.GetComponentData<LocalTransform>(se).Position;
                var pos = new float2(p3.x, p3.z);

                if (!_serverBeamChannel.TryGetValue(key, out var channel))
                {
                    if (_awaitingServerWave)
                    {
                        if (_pendingKeys.Add(key))
                        {
                            var beamCD = em.GetComponentData<BirdBossBeamCD>(se);
                            _pendingBatch.Add((key, pos, beamCD.moveDirection, beamCD.moveSideWays));
                        }
                        channel = AzeosBeamChannel.Aleatoire; // provisoire, ecrase des que le lot est tranche
                    }
                    else
                    {
                        // Pilier non classe hors fenetre d'attente (cas limite : ne devrait pas
                        // arriver, l'armement precede toujours la creation) - filet generique.
                        channel = AzeosBeamChannel.Aleatoire;
                        _serverBeamChannel[key] = channel;
                    }
                }

                if (count < AzeosBeamScan.MaxBeams)
                    AzeosBeamScan.Beams[count++] = new AzeosBeamScan.Beam { Key = key, Pos = pos, Channel = channel };
            }
            objEnts.Dispose();

            if (_serverBeamChannel.Count > 0)
            {
                var stale = new List<long>();
                foreach (var kv in _serverBeamChannel) if (!currentKeys.Contains(kv.Key)) stale.Add(kv.Key);
                foreach (var k in stale) _serverBeamChannel.Remove(k);
            }

            if (_awaitingServerWave)
            {
                if (_pendingBatch.Count >= MinWaveForShape)
                {
                    var channel = ClassifyShapeFromBeamData(_pendingBatch);
                    foreach (var b in _pendingBatch) _serverBeamChannel[b.key] = channel;
                    Diag.Log("A11yAzeosWave", "vague de " + _pendingBatch.Count + " pilier(s) -> canal " + channel);
                    GameplayAudio.PlayAzeosShapeCallout(channel);
                    _awaitingServerWave = false;
                    _pendingBatch.Clear();
                    _pendingKeys.Clear();
                }
                else if (now - _awaitingSince > AwaitTimeout)
                {
                    foreach (var b in _pendingBatch) _serverBeamChannel[b.key] = AzeosBeamChannel.Aleatoire;
                    _awaitingServerWave = false;
                    _pendingBatch.Clear();
                    _pendingKeys.Clear();
                }
            }

            return count;
        }
    }
}
