using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace CoreKeeperAccess.Gameplay
{
    // Canal sonore auquel un pilier de foudre (BirdBossBeam) a ete affecte, decide UNE FOIS
    // a son apparition (voir AzeosScanSystem.ClassifyWave) d'apres la FORME du groupe qui
    // spawn avec lui - jamais d'apres son etat interne (BirdBossBeamCD est confirme non
    // replique au client : internalState/moveDirection restent figes a leur valeur de
    // fabrication, verifie en jeu). Seuls ColonneV/LigneHaut/LigneBas ont un consommateur
    // dedie (AzeosBoss.TickRangees). None (anneau, pattern 1) et Aleatoire (pattern 0, la
    // "sentinelle" dediee a ete retiree - juge trop bruyante en jeu) tombent tous les deux
    // sur le filet de securite generique FireProximity (BirdBossBeam ajoute a sa liste
    // surveillee) - la distinction reste calculee pour un usage futur eventuel, mais aucun
    // canal ne les consomme aujourd'hui.
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
    // BossEggScanSystem pour la Hive Mother). Cadence 10 Hz : assez frais pour suivre un
    // pilier qui glisse et classer une vague des sa premiere frame de spawn.
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class AzeosScanSystem : SystemBase
    {
        private const float ScanInterval = 0.1f;
        private const float AxisTight = 1.5f;      // ecart-type max (cases) pour dire "meme axe" (ligne)
        private const float RingMinRadius = 1f;    // sous ce rayon moyen, pas assez de recul pour juger un cercle
        private const float RingDevRatio = 0.25f;  // ecart-type / rayon moyen sous ce seuil -> cercle net (anneau)
        private const int MinWaveForShape = 3;     // sous ce nombre, pas assez de points -> aleatoire par defaut

        private EntityQuery _objQuery;
        private float _next;

        private readonly HashSet<long> _knownBeamKeys = new HashSet<long>();
        private readonly Dictionary<long, AzeosBeamChannel> _beamChannel = new Dictionary<long, AzeosBeamChannel>();
        private readonly HashSet<long> _currentBeamKeys = new HashSet<long>();

        // Vague en attente de classification : les ~30 piliers d'une meme vague n'arrivent
        // PAS tous au client dans le meme tick reseau (replication etalee sur plusieurs
        // snapshots) - classer des paquets de 1-2 piliers a la fois les fait tous tomber par
        // defaut dans "aleatoire" (bug constate en jeu). On accumule donc une COURTE fenetre
        // (WaveWindow) depuis le 1er pilier vu avant de juger la forme du groupe complet.
        // Sans risque : les piliers restent IMMOBILES pendant tout le telegraphe (~1,1-1,3s,
        // confirme en jeu), tres au-dela de cette fenetre - la position captee a la 1re vue
        // reste valide au moment du flush.
        private const float WaveWindow = 0.3f;
        private readonly List<(long key, float2 pos)> _pendingWave = new List<(long, float2)>();
        private float _pendingDeadline;

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
            _currentBeamKeys.Clear();

            int beamCount = 0;
            int stoneCount = 0;
            bool bossFound = false; float2 bossPos = default; bool hasAppeared = false; bool enraged = false;

            var ents = _objQuery.ToEntityArray(Allocator.Temp);
            foreach (var e in ents)
            {
                var od = EntityManager.GetComponentData<ObjectDataCD>(e);

                if (od.objectID == ObjectID.BirdBossBeam)
                {
                    if (!EntityManager.HasComponent<LocalTransform>(e)) continue;
                    var pos3 = EntityManager.GetComponentData<LocalTransform>(e).Position;
                    float2 pos = new float2(pos3.x, pos3.z);
                    long key = EntityKey.Of(e);
                    _currentBeamKeys.Add(key);
                    if (_knownBeamKeys.Add(key))
                    {
                        if (_pendingWave.Count == 0) _pendingDeadline = now + WaveWindow;
                        _pendingWave.Add((key, pos));
                    }
                    if (beamCount < AzeosBeamScan.MaxBeams && _beamChannel.TryGetValue(key, out var ch))
                        AzeosBeamScan.Beams[beamCount++] = new AzeosBeamScan.Beam { Key = key, Pos = pos, Channel = ch };
                }
                else if (od.objectID == ObjectID.BirdBossStone)
                {
                    if (!EntityManager.HasComponent<LocalTransform>(e)) continue;
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
            }
            ents.Dispose();

            // Nettoyage des piliers disparus (detruits en fin de vie) : evite une fuite lente
            // des dictionnaires/sets au fil des vagues d'un combat qui dure.
            _knownBeamKeys.RemoveWhere(k => !_currentBeamKeys.Contains(k));
            if (_beamChannel.Count > 0)
            {
                var stale = new List<long>();
                foreach (var kv in _beamChannel)
                    if (!_currentBeamKeys.Contains(kv.Key)) stale.Add(kv.Key);
                foreach (var k in stale) _beamChannel.Remove(k);
            }

            // Flush de la vague en attente une fois la fenetre ecoulee (cf. commentaire sur
            // _pendingWave) : classification PAR SA FORME (positions de spawn brutes, avant
            // tout mouvement) - jamais par un champ d'etat du jeu (non fiable, cf. commentaire
            // AzeosBeamChannel). Injectee ensuite dans le tableau publie de cette meme frame
            // (ces piliers n'y etaient pas encore lors du remplissage ci-dessus, TryGetValue
            // avait echoue faute d'entree dans _beamChannel).
            if (_pendingWave.Count > 0 && now >= _pendingDeadline)
            {
                if (_pendingWave.Count >= MinWaveForShape) ClassifyWave(_pendingWave);
                else foreach (var nb in _pendingWave) _beamChannel[nb.key] = AzeosBeamChannel.Aleatoire;

                foreach (var nb in _pendingWave)
                {
                    if (beamCount >= AzeosBeamScan.MaxBeams) break;
                    if (_beamChannel.TryGetValue(nb.key, out var ch2))
                        AzeosBeamScan.Beams[beamCount++] = new AzeosBeamScan.Beam { Key = nb.key, Pos = nb.pos, Channel = ch2 };
                }
                _pendingWave.Clear();
            }

            AzeosBeamScan.Count = beamCount;
            AzeosBeamScan.ResultValid = true;

            AzeosStoneScan.Count = stoneCount;
            AzeosStoneScan.ResultValid = true;

            AzeosBossScan.Found = bossFound;
            AzeosBossScan.Pos = bossPos;
            AzeosBossScan.HasAppeared = hasAppeared;
            AzeosBossScan.Enraged = enraged;
        }

        // Forme du groupe qui vient d'apparaitre ensemble (meme frame de spawn) :
        //  - ecart-type de X quasi nul, Z variable -> ligne VERTICALE (rangee V, pattern 3)
        //    -> canal colonne (pan est-ouest).
        //  - ecart-type de Z quasi nul, X variable -> ligne HORIZONTALE (rangee H, pattern 2)
        //    -> canal ligne du dessus OU du dessous selon la position du groupe par rapport
        //    au joueur au moment du spawn.
        //  - ni l'un ni l'autre : distances au centroide quasi CONSTANTES -> anneau
        //    (pattern 1), delibrement SANS canal dedie (laisse a FireProximity, cf. tache
        //    "brancher BirdBossBeam sur FireProximity").
        //  - sinon : disperse -> aleatoire (pattern 0), file de bips dediee cote AzeosBoss.
        private void ClassifyWave(List<(long key, float2 pos)> wave)
        {
            float2 centroid = float2.zero;
            foreach (var w in wave) centroid += w.pos;
            centroid /= wave.Count;

            float sumX2 = 0f, sumZ2 = 0f;
            foreach (var w in wave)
            {
                float dx = w.pos.x - centroid.x, dz = w.pos.y - centroid.y;
                sumX2 += dx * dx; sumZ2 += dz * dz;
            }
            float stdX = Unity.Mathematics.math.sqrt(sumX2 / wave.Count);
            float stdZ = Unity.Mathematics.math.sqrt(sumZ2 / wave.Count);

            AzeosBeamChannel channel;
            if (stdX < AxisTight && stdZ >= AxisTight)
            {
                channel = AzeosBeamChannel.ColonneV;
            }
            else if (stdZ < AxisTight && stdX >= AxisTight)
            {
                float2 player = ObjectIndex.Center;
                channel = (centroid.y > player.y) ? AzeosBeamChannel.LigneHaut : AzeosBeamChannel.LigneBas;
            }
            else
            {
                float sumDist = 0f;
                var dists = new float[wave.Count];
                for (int i = 0; i < wave.Count; i++)
                {
                    dists[i] = math.length(wave[i].pos - centroid);
                    sumDist += dists[i];
                }
                float meanDist = sumDist / wave.Count;
                float sumDevSq = 0f;
                foreach (var d in dists) { float dd = d - meanDist; sumDevSq += dd * dd; }
                float stdDist = Unity.Mathematics.math.sqrt(sumDevSq / wave.Count);
                bool isRing = meanDist > RingMinRadius && (stdDist / meanDist) < RingDevRatio;
                channel = isRing ? AzeosBeamChannel.None : AzeosBeamChannel.Aleatoire;
            }

            foreach (var w in wave) _beamChannel[w.key] = channel;
        }
    }
}
