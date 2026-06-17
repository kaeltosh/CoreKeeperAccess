using System.Collections.Generic;
using CoreKeeperAccess.Navigation;
using Pug.Automation;
using Pug.Properties;
using PugTilemap;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace CoreKeeperAccess.Gameplay
{
    // Etat de croissance d'une plante posee sur la case (agriculture). None = pas une
    // plante. Lu depuis l'ObjectIndex (qui balaye deja toutes les entites) : GrowingCD =
    // plante en croissance, + tag HasFinishedGrowingCD a maturite = recoltable.
    public enum PlantState : byte { None = 0, Growing = 1, Ready = 2 }

    // Etat d'alimentation electrique d'un objet d'automation (cable, machine).
    // None = pas un objet electrique. On = energie suffisante pour alimenter (ElectricityCD).
    public enum PowerState : byte { None = 0, Off = 1, On = 2 }

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
        public PlantState Plant;       // si une plante est posee la : etat de croissance
        public bool Conveyor;          // l'objet est un convoyeur (MoverCD)
        public int2 ConveyorDir;       // sens de transport (signe de stop - start), si convoyeur
        public PowerState Power;       // alimentation electrique (cable / machine), None si pas electrique
        public int Connections;        // cotes connectes au reseau electrique (ElectricityDirectionMask brut), 0 = aucun
        public bool HasStorage;        // l'objet est un stockage d'automation (StorageCD)
        public int StorageCount;       // nombre d'objets dedans (0 = vide), si HasStorage
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
        public static PlantState Plant;
        public static bool Conveyor;
        public static int2 ConveyorDir;
        public static PowerState Power;
        public static int Connections;
        public static bool HasStorage;
        public static int StorageCount;

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
            Plant = Plant,
            Conveyor = Conveyor,
            ConveyorDir = ConveyorDir,
            Power = Power,
            Connections = Connections,
            HasStorage = HasStorage,
            StorageCount = StorageCount,
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

    // Pont recalcul local du reseau de navigation (tranche C, "mise a jour du reseau"). Le
    // mod pose une demande (centre = case joueur, rayon de revision) ; le systeme la traite
    // avec son TileAccessor (NetworkWeaver tisse les aretes manquantes par LIGNE DE VUE
    // franchissable) et publie le nombre d'aretes ajoutees. Le mod l'annonce. AJOUT seulement.
    internal static class NetworkRecalc
    {
        public static bool Requested;   // demande posee par le mod (consommee par le systeme)
        public static int2 Center;      // case du joueur
        public static float Radius;     // rayon de revision en cases

        public static bool ResultValid; // reponse publiee (consommee par le mod)
        public static int AddedEdges;   // aretes ajoutees (lignes de vue degagees)
        public static int RemovedEdges; // aretes coupees (lignes de vue obstruees)
        public static int LostNodes;    // noeuds fantomes elagues (balise disparue, feuille)
    }

    // Pont DUMP ASCII du reseau local (dev). Le mod pose une demande (centre, rayon) ; le
    // systeme dessine dans Player.log une grille "vue par le mod" : # = mur LU, . = sol,
    // = passage (pont/porte sur tuile bloquante), lettre = noeud, @ = joueur, + la liste des
    // aretes intra-fenetre (par lettres). A comparer a une capture carte : valide a la fois la
    // coherence interne (aucune arete a travers un #) ET la lecture des murs (# lus vs reels).
    internal static class NetworkDump
    {
        public static bool Requested;
        public static int2 Center;
        public static float Radius;
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
            public PlantState Plant; // si l'entite est une plante : son etat de croissance
            public bool Conveyor;    // automation : convoyeur (MoverCD)
            public int2 ConveyorDir; // sens de transport
            public PowerState Power; // automation : alimentation electrique
            public int Connections;  // automation : cotes connectes (ElectricityDirectionMask brut)
            public bool HasStorage;  // automation : stockage (StorageCD)
            public int StorageCount; // nombre d'objets dans le stockage
            public Entity Ent;       // entite source (pour le diagnostic automation dev)
        }

        public static float2 Center; // position joueur, publiee par le mod (GameplayInput)
        public static readonly Dictionary<long, Entry> Map = new Dictionary<long, Entry>();

        public static long Key(int2 t) => ((long)t.x << 32) ^ (uint)t.y;

        public static bool TryGet(int2 t, out Entry e) => Map.TryGetValue(Key(t), out e);
    }

    // Pont du repere de centre : position de la zone d'invocation (SummonArea = centre
    // de l'arene de boss) la plus proche, captee par TileReaderSystem au fil de son scan
    // d'objets (aucun scan dedie). Found=false = aucune SummonArea a portee (cas normal
    // hors arene) -> le drone du repere se tait.
    internal static class CenterScan
    {
        public static bool Found;
        public static float2 Pos;
    }

    // Pont diagnostic AUTOMATION (mode dev seulement). Pose par le combo details
    // (Triangle+haut de BuildModeNavigator) quand le curseur est sur une machine
    // industrielle : le systeme dumpe dans Player.log TOUS les composants de l'entite
    // + les valeurs des composants automation connus. Sert a finaliser l'a11y industrie
    // sur du concret (le contenu d'automation n'est atteignable en jeu qu'avec l'ecarlate
    // - impossible a tester autrement). Cf. methode gravee : log = seule verite.
    internal static class AutomationDiag
    {
        public static bool Requested;
        public static int2 Tile;
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
            // Etats portes par l'index (l'index voit toutes les entites, avec ou sans
            // collider) : plante (agriculture) + automation (convoyeur, electricite). Lus
            // pour la case, independamment de la sonde physique.
            if (ObjectIndex.TryGet(t, out var pe))
            {
                info.Plant = pe.Plant;
                info.Conveyor = pe.Conveyor;
                info.ConveyorDir = pe.ConveyorDir;
                info.Power = pe.Power;
                info.Connections = pe.Connections;
                info.HasStorage = pe.HasStorage;
                info.StorageCount = pe.StorageCount;
            }
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
                catch (System.Exception ex)
                {
                    PingScan.Count = 0;
                    PingScan.ResultValid = true;
                    Diag.Error("A11yPingDiag", ex);
                }
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
                catch (System.Exception ex)
                {
                    OreScan.Found = false;
                    OreScan.ResultValid = true;
                    Diag.Error("A11yOreDiag", ex);
                }
            }

            // Recalcul local du reseau de navigation (tranche C) : tisse les aretes manquantes
            // par ligne de vue dans le rayon de revision. Independant du curseur.
            if (NetworkRecalc.Requested)
            {
                NetworkRecalc.Requested = false;
                try
                {
                    var taNet = new TileAccessor(ref CheckedStateRef, true);
                    CoreKeeperAccess.Navigation.NetworkWeaver.Weave(
                        ref taNet, NetworkRecalc.Center, NetworkRecalc.Radius,
                        out int added, out int removed, out int lost);
                    NetworkRecalc.AddedEdges = added;
                    NetworkRecalc.RemovedEdges = removed;
                    NetworkRecalc.LostNodes = lost;
                    NetworkRecalc.ResultValid = true;
                }
                catch (System.Exception ex)
                {
                    NetworkRecalc.AddedEdges = 0;
                    NetworkRecalc.RemovedEdges = 0;
                    NetworkRecalc.LostNodes = 0;
                    NetworkRecalc.ResultValid = true;
                    Diag.Error("A11yNetRecalc", ex);
                }
            }

            // Dump ASCII du reseau local (dev) : dessine la zone vue par le mod dans le log.
            if (NetworkDump.Requested)
            {
                NetworkDump.Requested = false;
                try { DumpNetwork(); }
                catch (System.Exception ex) { Diag.Error("A11yNetDump", ex); }
            }

            // Diagnostic automation a la demande (dev) : independant du curseur actif.
            if (AutomationDiag.Requested)
            {
                AutomationDiag.Requested = false;
                try { DumpAutomation(); }
                catch (System.Exception ex) { Diag.Error("A11yAutoDiag", ex); }
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
                TileQuery.Plant = info.Plant;
                TileQuery.Conveyor = info.Conveyor;
                TileQuery.ConveyorDir = info.ConveyorDir;
                TileQuery.Power = info.Power;
                TileQuery.Connections = info.Connections;
                TileQuery.HasStorage = info.HasStorage;
                TileQuery.StorageCount = info.StorageCount;
                TileQuery.ResultTile = t;
                TileQuery.ResultValid = true;
            }
            catch (System.Exception ex) { Diag.Error("A11yTileDiag", ex); }
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

                // Repere de centre : on capte au passage la SummonArea (sigil
                // d'invocation = centre de l'arene de boss) la plus proche, sans
                // scan dedie (ce balayage d'objets tourne deja a ~4 Hz).
                bool caFound = false; float caBest = float.MaxValue; float2 caPos = default;
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

                    if (od.objectID == ObjectID.SummonArea)
                    {
                        float caD2 = math.lengthsq(p - center);
                        if (caD2 < caBest) { caBest = caD2; caPos = p; caFound = true; }
                    }

                    int2 size;
                    int2 corner;
                    try
                    {
                        // ref obligatoire (analyseur Unity EA0001) : la donnee vit en blob storage.
                        ref var info = ref PugDatabase.GetEntityObjectInfo(od.objectID, bank.databaseBankBlob, od.variation);
                        size = math.max(info.prefabTileSize, new int2(1, 1));
                        corner = info.prefabCornerOffset;
                    }
                    catch { size = new int2(1, 1); corner = int2.zero; }

                    // Plante (agriculture) : GrowingCD = en croissance ; le tag
                    // HasFinishedGrowingCD apparait a maturite (PlantsGrowingSystem) =
                    // recoltable. Lu une fois ici, porte par l'entree d'index.
                    // Maturite = la MEME condition que la recolte du jeu
                    // (HoeSlot.EntityIsPlantReadyForHarvest) : GrowingCD.HasFinishedGrowing
                    // (currentStage >= nb de stades, lu dans ObjectPropertiesCD). PAS le tag
                    // HasFinishedGrowingCD : il n'est pas pose sur les plantes qui repoussent
                    // (ex. baie en coeur -> annoncee "a soif" a tort une fois mure).
                    PlantState plant = PlantState.None;
                    if (EntityUtility.HasComponentData<GrowingCD>(e, World))
                    {
                        bool ready = false;
                        if (EntityUtility.HasComponentData<ObjectPropertiesCD>(e, World))
                        {
                            try
                            {
                                ready = EntityUtility.GetComponentData<GrowingCD>(e, World)
                                    .HasFinishedGrowing(EntityUtility.GetComponentData<ObjectPropertiesCD>(e, World));
                            }
                            catch { ready = false; }
                        }
                        plant = ready ? PlantState.Ready : PlantState.Growing;
                    }

                    // Automation : convoyeur (sens = stop - start, ramene au cardinal) +
                    // electricite (ElectricityCD.hasEnoughElectricityToPowerStuff = sous
                    // tension ; ElectricityConnectionCD.direction = cotes connectes).
                    bool conveyor = false; int2 convDir = default;
                    if (EntityUtility.HasComponentData<MoverCD>(e, World))
                    {
                        var m = EntityUtility.GetComponentData<MoverCD>(e, World);
                        int2 d = m.stop - m.start;
                        convDir = new int2(math.clamp(d.x, -1, 1), math.clamp(d.y, -1, 1));
                        conveyor = true;
                    }
                    PowerState power = PowerState.None; int conns = 0;
                    if (EntityUtility.HasComponentData<ElectricityCD>(e, World))
                        power = EntityUtility.GetComponentData<ElectricityCD>(e, World).hasEnoughElectricityToPowerStuff
                            ? PowerState.On : PowerState.Off;
                    if (EntityUtility.HasComponentData<ElectricityConnectionCD>(e, World))
                        conns = (int)EntityUtility.GetComponentData<ElectricityConnectionCD>(e, World).direction;

                    // Stockage d'automation : remplissage. L'inventaire vit sur une entite
                    // separee (StorageCD.inventoryEntity) -> on compte ses slots occupes.
                    bool hasStorage = false; int storageCount = 0;
                    if (EntityUtility.HasComponentData<StorageCD>(e, World))
                    {
                        hasStorage = true;
                        Entity inv = EntityUtility.GetComponentData<StorageCD>(e, World).inventoryEntity;
                        if (inv != Entity.Null && EntityManager.HasBuffer<ContainedObjectsBuffer>(inv))
                        {
                            var buf = EntityManager.GetBuffer<ContainedObjectsBuffer>(inv, true);
                            for (int i = 0; i < buf.Length; i++)
                                if (buf[i].objectID != ObjectID.None) storageCount++;
                        }
                    }

                    var entry = new ObjectIndex.Entry
                    {
                        Id = od.objectID,
                        Interactable = EntityUtility.HasComponentData<InteractableObjectReferenceCD>(e, World),
                        Plant = plant,
                        Conveyor = conveyor,
                        ConveyorDir = convDir,
                        Power = power,
                        Connections = conns,
                        HasStorage = hasStorage,
                        StorageCount = storageCount,
                        Ent = e,
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
                CenterScan.Found = caFound;
                CenterScan.Pos = caPos;
            }
            catch (System.Exception ex) { Diag.Error("A11yIndexDiag", ex); }
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

        // Dump ASCII du reseau local (dev) : grille "vue par le mod" dans Player.log, Nord en
        // haut. # = mur LU (TryGetBlockingTile), . = sol, = = passage (pont/porte sur tuile
        // bloquante), lettre = noeud du reseau, @ = joueur. Puis la liste des aretes dont les
        // DEUX extremites sont dans la fenetre (par lettres). A croiser avec une capture carte.
        private const string DumpAlphabet =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

        private void DumpNetwork()
        {
            int r = (int)NetworkDump.Radius;
            int2 c = NetworkDump.Center;
            var ta = new TileAccessor(ref CheckedStateRef, true);

            // Noeuds de la fenetre -> une lettre chacun (au-dela de 52 : '*').
            var nodes = new List<int2>();
            BeaconGraph.NodesInRadius(new float2(c.x, c.y), r, nodes);
            var label = new Dictionary<long, char>();
            for (int i = 0; i < nodes.Count; i++)
                label[ObjectIndex.Key(nodes[i])] = i < DumpAlphabet.Length ? DumpAlphabet[i] : '*';

            Diag.Log("A11yNetDump", "=== zone " + c.x + "," + c.y + " rayon " + r
                + " : " + nodes.Count + " noeuds (Nord en haut) ===");

            var sb = new System.Text.StringBuilder();
            for (int y = c.y + r; y >= c.y - r; y--)
            {
                sb.Clear();
                for (int x = c.x - r; x <= c.x + r; x++)
                {
                    int2 t = new int2(x, y);
                    char ch;
                    if (x == c.x && y == c.y) ch = '@';
                    else if (label.TryGetValue(ObjectIndex.Key(t), out char lc)) ch = lc;
                    else if (!ta.TryGetBlockingTile(t, out _, true)) ch = '.';
                    else if (ObjectIndex.TryGet(t, out var e) && BeaconObjects.IsPassable(e.Id)) ch = '=';
                    else ch = '#';
                    sb.Append(ch);
                }
                Diag.Log("A11yNetDump", sb.ToString());
            }

            // Aretes intra-fenetre (les deux extremites dans le rayon) par lettres.
            var edges = new List<BeaconGraph.Edge>();
            BeaconGraph.EdgesInRadius(new float2(c.x, c.y), r, edges);
            var es = new System.Text.StringBuilder();
            foreach (var e in edges)
            {
                char la = label.TryGetValue(ObjectIndex.Key(new int2(e.ax, e.ay)), out char a) ? a : '?';
                char lb = label.TryGetValue(ObjectIndex.Key(new int2(e.bx, e.by)), out char b) ? b : '?';
                if (es.Length > 0) es.Append(' ');
                es.Append(la).Append('-').Append(lb);
            }
            Diag.Log("A11yNetDump", "aretes (" + edges.Count + ") : " + es);
        }

        // Dump dev : liste TOUS les composants de l'entite sous le curseur + les valeurs
        // des composants automation connus. But : capturer la verite terrain d'une machine
        // (atteignable seulement avec l'ecarlate) pour finaliser l'a11y industrie sans
        // deviner. Sortie dans Player.log, prefixe [A11yAutoDiag].
        private void DumpAutomation()
        {
            if (!ObjectIndex.TryGet(AutomationDiag.Tile, out var e)
                || e.Ent == Entity.Null || !EntityManager.Exists(e.Ent))
            {
                Diag.Log("A11yAutoDiag", "case " + AutomationDiag.Tile.x + "," + AutomationDiag.Tile.y
                    + " : aucune entite indexee");
                return;
            }

            Entity ent = e.Ent;
            var types = EntityManager.GetComponentTypes(ent, Allocator.Temp);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < types.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                var mt = types[i].GetManagedType();
                sb.Append(mt != null ? mt.Name : types[i].ToString());
            }
            types.Dispose();
            Diag.Log("A11yAutoDiag", e.Id + " @ " + AutomationDiag.Tile.x + "," + AutomationDiag.Tile.y
                + " : " + sb);

            if (EntityManager.HasComponent<MoverCD>(ent))
            {
                var m = EntityManager.GetComponentData<MoverCD>(ent);
                Diag.Log("A11yAutoDiag", "  MoverCD start=" + m.start + " stop=" + m.stop
                    + " moveTime=" + m.moveTime);
            }
            if (EntityManager.HasComponent<ElectricityCD>(ent))
            {
                var el = EntityManager.GetComponentData<ElectricityCD>(ent);
                Diag.Log("A11yAutoDiag", "  ElectricityCD L=" + el.electricityAmountLeft
                    + " R=" + el.electricityAmountRight + " U=" + el.electricityAmountUp
                    + " D=" + el.electricityAmountDown + " src=" + el.sourceEnergy
                    + " blocks=" + el.blocksElectricity);
            }
            if (EntityManager.HasComponent<ElectricityConnectionCD>(ent))
                Diag.Log("A11yAutoDiag", "  ElectricityConnectionCD dir="
                    + EntityManager.GetComponentData<ElectricityConnectionCD>(ent).direction);
            if (EntityManager.HasComponent<StorageCD>(ent))
                Diag.Log("A11yAutoDiag", "  StorageCD inv="
                    + EntityManager.GetComponentData<StorageCD>(ent).inventoryEntity.Index);
        }
    }
}
