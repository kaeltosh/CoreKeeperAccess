using System.Collections.Generic;
using PugTilemap;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace CoreKeeperAccess.Gameplay
{
    // Contenu remarquable d'une case, decouple de tout systeme : se passe en parametre.
    // Rempli par TileScan.Read et consomme par la sonification partagee
    // (BuildModeNavigator.SonifyTile), utilisee par le curseur ET la canne laser.
    public struct TileInfo
    {
        public TileType Ground;        // type de sol de la case
        public bool HasWall;           // tuile bloquante (mur) presente
        public TileType WallType;
        public int WallTileset;
        public bool HasOre;            // couche minerai (ore / ancientCrystal), independante du mur
        public bool IsImmune;          // couche immune (Grande Muraille...) : mur INVULNERABLE
        public ObjectID ObjectId;      // objet/construction pose sur la case (ou None)
        public bool ObjectInteractable; // l'entite porte InteractableObjectReferenceCD (vrai interactible)
    }

    // Pont mod <-> systeme ECS. Le TileAccessor ne se construit que depuis un
    // SystemState : seul notre systeme ECS peut lire une case. Le curseur (cote mod)
    // depose la case a lire dans TileQuery, le systeme la lit chaque frame et publie
    // le resultat ici. (Valide en jeu : un systeme du mod s'enregistre en hot-compile,
    // pas de build Unity necessaire.)
    internal static class TileQuery
    {
        public static bool Active;        // le curseur veut une lecture
        public static int2 Tile;          // case demandee (coordonnee monde)

        public static bool ResultValid;   // un resultat a ete publie
        public static int2 ResultTile;    // case effectivement lue
        public static TileType Ground;
        public static bool HasWall;
        public static TileType WallType;
        public static int WallTileset;
        public static bool HasOre;
        public static bool IsImmune;
        public static ObjectID ObjectId;
        public static bool ObjectInteractable;

        // Vue figee de la case courante, pour la passer a la sonification partagee.
        public static TileInfo Snapshot() => new TileInfo
        {
            Ground = Ground,
            HasWall = HasWall,
            WallType = WallType,
            WallTileset = WallTileset,
            HasOre = HasOre,
            IsImmune = IsImmune,
            ObjectId = ObjectId,
            ObjectInteractable = ObjectInteractable,
        };
    }

    // Pont prospection minerai (commande Triangle + gauche). Le mod pose une demande
    // (centre = case du joueur, rayon = stat VisibleOreDistance du perso - la MEME
    // valeur que le shader des paillettes cote voyant, donc equite stricte et talents
    // de minage respectes) ; le systeme balaye la zone et publie la tuile de minerai
    // la plus proche (couche ore / ancientCrystal, a travers les murs, comme les
    // paillettes du jeu qui s'affichent meme filon enfoui).
    internal static class OreScan
    {
        public static bool Requested;   // demande posee par le mod (consommee par le systeme)
        public static int2 Center;      // case du joueur
        public static int Radius;       // rayon en cases

        public static bool ResultValid; // reponse publiee (consommee par le mod)
        public static bool Found;
        public static int2 Tile;        // tuile de minerai la plus proche
    }

    // Pont ping sonar (Triangle + L1). Le mod pose une demande (centre, rayon) ; le
    // systeme balaye les CREATURES en query ECS (EnemyCD / CritterCD) et publie
    // position + bord (hostile ou paisible). Les trouvailles (objets type zone de
    // fouille) sont lues cote mod directement dans ObjectIndex - pas besoin d'ECS.
    internal static class PingScan
    {
        public struct Target
        {
            public float2 Pos;
            public bool Hostile;
        }

        public const int MaxTargets = 24;

        public static bool Requested;   // demande posee par le mod (consommee par le systeme)
        public static float2 Center;    // position joueur
        public static float Radius;     // rayon en cases

        public static bool ResultValid; // reponse publiee (consommee par le mod)
        public static int Count;
        public static readonly Target[] Targets = new Target[MaxTargets];
    }

    // Index case -> objet pose, reconstruit periodiquement depuis les ENTITES
    // (position + emprise prefab lue dans PugDatabase). Capte les objets SANS
    // collider physique - etabli en fer, generateur, Core, torches... - que les
    // sondes physiques de TileScan.ObjectAt ratent (confirme par [A11yTileDiag] :
    // obj=None sur leurs cases). Rempli par TileReaderSystem (~4 Hz, rayon borne
    // autour du joueur), consulte en dernier recours par ObjectAt.
    internal static class ObjectIndex
    {
        public struct Entry
        {
            public ObjectID Id;
            public bool Interactable;
        }

        public static float2 Center; // position joueur, publiee par le mod (GameplayInput)
        public static readonly Dictionary<long, Entry> Map = new Dictionary<long, Entry>();

        public static long Key(int2 t) => ((long)t.x << 32) ^ (uint)t.y;

        public static bool TryGet(int2 t, out Entry e) => Map.TryGetValue(Key(t), out e);
    }

    // Lecture d'une case (sol / mur / minerai / objet pose), partagee par les systemes
    // ECS du mod (curseur de tuile, canne laser). Doit etre appelee depuis un systeme
    // (le TileAccessor vient de son SystemState) ; le CollisionWorld et le World sont
    // passes pour la requete spatiale d'objet.
    internal static class TileScan
    {
        // Filtre large : requiredObjectFilter ne couvre que quelques couches (etabli,
        // coffre) et rate le Core. On ratisse toutes les couches et on filtre ensuite
        // par "a un ObjectDataCD nomme" -> capte tout objet/construction pose, Core inclus.
        private static readonly CollisionFilter AnyObjectFilter = new CollisionFilter
        {
            BelongsTo = uint.MaxValue,
            CollidesWith = uint.MaxValue,
        };

        public static TileInfo Read(ref TileAccessor ta, int2 t, World world)
        {
            var info = new TileInfo();
            info.Ground = ta.GetTopType(t);
            bool hasWall = ta.TryGetBlockingTile(t, out TileCD wall, true);
            info.HasWall = hasWall;
            info.WallType = hasWall ? wall.tileType : default;
            info.WallTileset = hasWall ? wall.tileset : 0;
            info.HasOre = ta.HasType(t, TileType.ore) || ta.HasType(t, TileType.ancientCrystal);
            info.IsImmune = ta.HasType(t, TileType.immune);
            info.ObjectId = ObjectAt(t, world, out bool interactable);
            info.ObjectInteractable = interactable;
            return info;
        }

        // Objet/construction pose sur la case : requete spatiale (les objets sont des
        // entites, pas des tuiles). None si rien. interactable = l'entite trouvee porte
        // InteractableObjectReferenceCD (vrai interactible vs deco passive).
        // DEUX sondes : au sol (objets simples), puis a MI-HAUTEUR si rien - les
        // grosses structures (Core, generateur, etabli en fer...) ont leur collider
        // centre a y+0.5 (confirme par PlacementHandler : le jeu teste l'occupation
        // d'une case avec un box cast a tuile + 0.5 en Y) et la sonde au sol passe
        // litteralement SOUS elles.
        public static ObjectID ObjectAt(int2 t, World world, out bool interactable)
        {
            var cw = PhysicsManager.GetCollisionWorld();
            ObjectID id = Probe(cw, new float3(t.x, 0f, t.y), 0.4f, world, out interactable);
            if (id == ObjectID.None)
                id = Probe(cw, new float3(t.x, 0.5f, t.y), 0.45f, world, out interactable);
            // Index d'entites (objets sans collider physique - etabli en fer,
            // generateur, Core, torches...). Il complete les sondes ET les CORRIGE :
            // une machine posee SUR le cable ancien (qui, lui, a un collider) etait
            // masquee par lui -> un INTERACTIBLE de l'index prime sur un
            // non-interactible rendu par la sonde.
            if (ObjectIndex.TryGet(t, out var e)
                && (id == ObjectID.None || (e.Interactable && !interactable)))
            {
                interactable = e.Interactable;
                id = e.Id;
            }
            return id;
        }

        private static ObjectID Probe(CollisionWorld cw, float3 pos, float radius,
            World world, out bool interactable)
        {
            interactable = false;
            var hits = new NativeList<DistanceHit>(8, Allocator.Temp);
            ObjectID id = ObjectID.None;
            if (cw.OverlapSphere(pos, radius, ref hits, AnyObjectFilter, QueryInteraction.Default))
            {
                foreach (var h in hits)
                {
                    // Les creatures et joueurs portent aussi un ObjectDataCD : on ne
                    // veut que les objets POSES (la sonde a mi-hauteur attraperait un
                    // slime de passage et l'annoncerait comme un meuble). Idem pour les
                    // projectiles en vol (fleches du joueur, mortiers...).
                    if (EntityUtility.HasComponentData<EnemyCD>(h.Entity, world)
                        || EntityUtility.HasComponentData<CritterCD>(h.Entity, world)
                        || EntityUtility.HasComponentData<PlayerGhost>(h.Entity, world)
                        || EntityUtility.HasComponentData<ProjectileCD>(h.Entity, world)
                        || EntityUtility.HasComponentData<MortarProjectileCD>(h.Entity, world)) continue;
                    if (EntityUtility.HasComponentData<ObjectDataCD>(h.Entity, world))
                    {
                        var od = EntityUtility.GetComponentData<ObjectDataCD>(h.Entity, world);
                        if (od.objectID != ObjectID.None)
                        {
                            id = od.objectID;
                            interactable = EntityUtility.HasComponentData<InteractableObjectReferenceCD>(h.Entity, world);
                            break;
                        }
                    }
                }
            }
            hits.Dispose();
            return id;
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class TileReaderSystem : SystemBase
    {
        private const float IndexInterval = 0.25f; // ~4 Hz, assez frais pour un curseur humain
        private const float IndexRadius = 24f;     // cases autour du joueur (couvre l'ecran)

        // PROVISOIRE : derniere case loggee par le diagnostic (evite le spam frame/frame).
        private int2 _lastDiag = new int2(int.MinValue, int.MinValue);

        private EntityQuery _objQuery;
        private EntityQuery _dbQuery;
        private EntityQuery _creatureQuery;
        private float _nextIndex;

        protected override void OnCreate()
        {
            // ObjectDataCD SEUL : exiger un composant de transform dans la query
            // excluait mysterieusement certaines entites (le generateur electrique
            // matchait Query(ObjectDataCD) et HasComponent<LocalToWorld> rendait true,
            // mais Query(ObjectDataCD, LocalToWorld) ne le voyait pas). On requete
            // large et on lit la position composant par composant dans la boucle.
            _objQuery = GetEntityQuery(ComponentType.ReadOnly<ObjectDataCD>());
            _dbQuery = GetEntityQuery(ComponentType.ReadOnly<PugDatabase.DatabaseBankCD>());
            // Creatures pour le ping sonar : tout ce qui porte EnemyCD OU CritterCD
            // (exclusifs entre eux ; le joueur n'a ni l'un ni l'autre).
            _creatureQuery = GetEntityQuery(new EntityQueryDesc
            {
                Any = new[]
                {
                    ComponentType.ReadOnly<EnemyCD>(),
                    ComponentType.ReadOnly<CritterCD>(),
                },
            });
        }

        protected override void OnUpdate()
        {
            RebuildObjectIndex();
            // Ping sonar : scan des creatures a la demande (independant du curseur).
            if (PingScan.Requested)
            {
                PingScan.Requested = false;
                try { ScanCreaturesForPing(); }
                catch { PingScan.Count = 0; PingScan.ResultValid = true; }
            }
            // Prospection minerai : independante du curseur (TileQuery peut etre inactif).
            if (OreScan.Requested)
            {
                OreScan.Requested = false;
                try
                {
                    var taOre = new TileAccessor(ref CheckedStateRef, true);
                    ScanOre(ref taOre);
                }
                catch { OreScan.Found = false; OreScan.ResultValid = true; }
            }

            if (!TileQuery.Active) return;
            try
            {
                var ta = new TileAccessor(ref CheckedStateRef, true);
                int2 t = TileQuery.Tile;
                var info = TileScan.Read(ref ta, t, World);
                TileQuery.Ground = info.Ground;
                TileQuery.HasWall = info.HasWall;
                TileQuery.WallType = info.WallType;
                TileQuery.WallTileset = info.WallTileset;
                TileQuery.HasOre = info.HasOre;
                TileQuery.IsImmune = info.IsImmune;
                TileQuery.ObjectId = info.ObjectId;
                TileQuery.ObjectInteractable = info.ObjectInteractable;
                TileQuery.ResultTile = t;
                TileQuery.ResultValid = true;

                // PROVISOIRE [A11yTileDiag] : a chaque NOUVELLE case, on crache toutes les
                // couches de tuile + le bloquant/minerai/objet, pour identifier ce que le
                // jeu voit reellement (ex. un filon superpose qu'on raterait). A RETIRER.
                if (!t.Equals(_lastDiag))
                {
                    _lastDiag = t;
                    var layers = ta.Get(t, Allocator.Temp);
                    var sb = new System.Text.StringBuilder();
                    foreach (var l in layers) sb.Append(l.tileType).Append('/').Append(l.tileset).Append(' ');
                    layers.Dispose();
                    string block = info.HasWall ? (info.WallType + "/" + info.WallTileset) : "none";
                    UnityEngine.Debug.Log($"[A11yTileDiag] tile={t.x},{t.y} block={block} ore={info.HasOre} obj={info.ObjectId} interact={info.ObjectInteractable} layers=[{sb}]");
                }
            }
            catch { }
        }

        // Reconstruit l'index case -> objet depuis les entites proches du joueur :
        // position + emprise prefab (prefabTileSize / prefabCornerOffset de la base,
        // memes champs que le placement du jeu). Throttle ~4 Hz, rayon borne. On
        // ecarte creatures et joueurs (eux aussi portent un ObjectDataCD). NB : la
        // rotation des objets n'est pas appliquee (footprint xy brut) - a affiner si
        // un objet long pivote remonte decale.
        private void RebuildObjectIndex()
        {
            if (UnityEngine.Time.unscaledTime < _nextIndex) return;
            _nextIndex = UnityEngine.Time.unscaledTime + IndexInterval;

            try
            {
                ObjectIndex.Map.Clear();
                if (_dbQuery.IsEmptyIgnoreFilter) return;
                var bank = _dbQuery.GetSingleton<PugDatabase.DatabaseBankCD>();
                float2 center = ObjectIndex.Center;
                float r2 = IndexRadius * IndexRadius;

                var ents = _objQuery.ToEntityArray(Allocator.Temp);
                foreach (var e in ents)
                {
                    // Position : LocalToWorld d'abord (toute entite rendue), sinon
                    // LocalTransform, sinon l'entite n'est pas localisable -> on passe.
                    float3 pos;
                    if (EntityManager.HasComponent<LocalToWorld>(e))
                        pos = EntityManager.GetComponentData<LocalToWorld>(e).Position;
                    else if (EntityManager.HasComponent<LocalTransform>(e))
                        pos = EntityManager.GetComponentData<LocalTransform>(e).Position;
                    else continue;
                    float2 p = new float2(pos.x, pos.z);
                    if (math.lengthsq(p - center) > r2) continue;

                    if (EntityUtility.HasComponentData<EnemyCD>(e, World)
                        || EntityUtility.HasComponentData<CritterCD>(e, World)
                        || EntityUtility.HasComponentData<PlayerGhost>(e, World)) continue;
                    // Projectiles en vol (fleches du joueur, mortiers...) : des entites
                    // ObjectDataCD ephemeres, pas des objets POSES -> hors index (sinon
                    // chaque fleche tiree bipait au laser et au curseur).
                    if (EntityUtility.HasComponentData<ProjectileCD>(e, World)
                        || EntityUtility.HasComponentData<MortarProjectileCD>(e, World)) continue;

                    var od = EntityManager.GetComponentData<ObjectDataCD>(e);
                    if (od.objectID == ObjectID.None) continue;

                    int2 size;
                    int2 corner;
                    try
                    {
                        var info = PugDatabase.GetEntityObjectInfo(od.objectID, bank.databaseBankBlob, od.variation);
                        size = math.max(info.prefabTileSize, new int2(1, 1));
                        corner = info.prefabCornerOffset;
                    }
                    catch { size = new int2(1, 1); corner = int2.zero; }

                    var entry = new ObjectIndex.Entry
                    {
                        Id = od.objectID,
                        Interactable = EntityUtility.HasComponentData<InteractableObjectReferenceCD>(e, World),
                    };
                    // Emprise : on ne lit pas la rotation de l'objet -> pour un prefab
                    // RECTANGULAIRE on marque l'UNION des deux orientations (xy et yx,
                    // la regle du jeu echange les axes selon la direction). Sur-couvrir
                    // d'une case adjacente est sans gravite pour une annonce ; rater la
                    // moitie d'une machine pivotee ne l'etait pas (vecu : scie/etabli
                    // en fer muets une case sur deux).
                    int2 anchor = new int2((int)math.round(pos.x), (int)math.round(pos.z)) + corner;
                    int2 span = math.max(size, size.yx);
                    for (int dx = 0; dx < span.x; dx++)
                        for (int dy = 0; dy < span.y; dy++)
                        {
                            bool inXy = dx < size.x && dy < size.y;
                            bool inYx = dx < size.y && dy < size.x;
                            if (!inXy && !inYx) continue;
                            long k = ObjectIndex.Key(new int2(anchor.x + dx, anchor.y + dy));
                            // Deux entites sur la meme case (machine posee SUR le cable
                            // ancien) : l'INTERACTIBLE prime, il ne se fait pas ecraser.
                            if (ObjectIndex.Map.TryGetValue(k, out var old)
                                && old.Interactable && !entry.Interactable) continue;
                            ObjectIndex.Map[k] = entry;
                        }
                }
                ents.Dispose();
            }
            catch { }
        }

        // Balaye le disque (rayon en cases) autour du centre et retient la tuile de
        // minerai la plus proche. Couche ore/ancientCrystal lue par TileAccessor.HasType,
        // independante des murs (le filon enfoui est detecte, comme ses paillettes).
        private static void ScanOre(ref TileAccessor ta)
        {
            int r = OreScan.Radius;
            int2 c = OreScan.Center;
            int r2 = r * r;
            bool found = false;
            int best = int.MaxValue;
            int2 bestTile = default;

            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    int d2 = dx * dx + dy * dy;
                    if (d2 > r2 || d2 >= best) continue;
                    int2 t = new int2(c.x + dx, c.y + dy);
                    if ((ta.HasType(t, TileType.ore) || ta.HasType(t, TileType.ancientCrystal))
                        && !ta.HasType(t, TileType.immune)) // filon SCELLE (Grande Muraille) : iminable, ne pas y guider
                    {
                        found = true;
                        best = d2;
                        bestTile = t;
                    }
                }
            }

            OreScan.Found = found;
            OreScan.Tile = bestTile;
            OreScan.ResultValid = true;
        }

        // Balaye les creatures dans le rayon du ping et publie position + bord.
        // Memes regles que le laser : CritterCD = paisible ; EnemyCD a faction
        // hostile = hostile, sauf slime dormant (paisible) ; EnemyCD a faction
        // neutre (chevres, betail) = paisible. Cadavres ecartes (HealthCD a 0 :
        // l'entite persiste quelques secondes apres la mort).
        private void ScanCreaturesForPing()
        {
            int count = 0;
            float r2 = PingScan.Radius * PingScan.Radius;
            float2 center = PingScan.Center;

            var ents = _creatureQuery.ToEntityArray(Allocator.Temp);
            foreach (var e in ents)
            {
                if (count >= PingScan.MaxTargets) break;

                float3 pos;
                if (EntityManager.HasComponent<LocalToWorld>(e))
                    pos = EntityManager.GetComponentData<LocalToWorld>(e).Position;
                else if (EntityManager.HasComponent<LocalTransform>(e))
                    pos = EntityManager.GetComponentData<LocalTransform>(e).Position;
                else continue;
                float2 p = new float2(pos.x, pos.z);
                if (math.lengthsq(p - center) > r2) continue;

                if (EntityManager.HasComponent<HealthCD>(e)
                    && EntityManager.GetComponentData<HealthCD>(e).health <= 0) continue;

                bool critter = EntityManager.HasComponent<CritterCD>(e);
                ObjectID oid = EntityManager.HasComponent<ObjectDataCD>(e)
                    ? EntityManager.GetComponentData<ObjectDataCD>(e).objectID
                    : ObjectID.None;
                bool hostile = !critter
                    && EntityManager.HasComponent<FactionCD>(e)
                    && HostileFilter.IsHostile(EntityManager.GetComponentData<FactionCD>(e).faction)
                    && !HostileFilter.IsDormantSlime(oid);

                PingScan.Targets[count++] = new PingScan.Target { Pos = p, Hostile = hostile };
            }
            ents.Dispose();

            PingScan.Count = count;
            PingScan.ResultValid = true;
        }
    }
}
