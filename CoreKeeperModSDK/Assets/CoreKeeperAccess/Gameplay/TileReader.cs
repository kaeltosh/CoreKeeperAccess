using PugTilemap;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

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
            ObjectId = ObjectId,
            ObjectInteractable = ObjectInteractable,
        };
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
            info.ObjectId = ObjectAt(t, world, out bool interactable);
            info.ObjectInteractable = interactable;
            return info;
        }

        // Objet/construction pose sur la case : requete spatiale (les objets sont des
        // entites, pas des tuiles). None si rien. interactable = l'entite trouvee porte
        // InteractableObjectReferenceCD (vrai interactible vs deco passive).
        public static ObjectID ObjectAt(int2 t, World world, out bool interactable)
        {
            interactable = false;
            var cw = PhysicsManager.GetCollisionWorld();
            var hits = new NativeList<DistanceHit>(8, Allocator.Temp);
            ObjectID id = ObjectID.None;
            if (cw.OverlapSphere(new float3(t.x, 0f, t.y), 0.4f, ref hits,
                    AnyObjectFilter, QueryInteraction.Default))
            {
                foreach (var h in hits)
                {
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
        // PROVISOIRE : derniere case loggee par le diagnostic (evite le spam frame/frame).
        private int2 _lastDiag = new int2(int.MinValue, int.MinValue);

        protected override void OnUpdate()
        {
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
    }
}
