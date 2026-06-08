using PugTilemap;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

namespace CoreKeeperAccess.Gameplay
{
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
        public static TileType Ground;    // type de sol de la case
        public static bool HasWall;       // tuile bloquante (mur) presente
        public static TileType WallType;
        public static int WallTileset;
        public static bool HasOre;        // couche minerai (ore / ancientCrystal) sur la case,
                                          // independante du mur bloquant (un filon peut etre
                                          // superpose a un mur de terre sans en etre la tuile
                                          // bloquante -> on le detecte via une couche dediee).
        public static ObjectID ObjectId;  // objet/construction pose sur la case (ou None)
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
                TileQuery.Ground = ta.GetTopType(t);
                bool hasWall = ta.TryGetBlockingTile(t, out TileCD wall, true);
                TileQuery.HasWall = hasWall;
                TileQuery.WallType = hasWall ? wall.tileType : default;
                TileQuery.WallTileset = hasWall ? wall.tileset : 0;
                bool hasOre = ta.HasType(t, TileType.ore) || ta.HasType(t, TileType.ancientCrystal);
                TileQuery.HasOre = hasOre;
                TileQuery.ObjectId = ObjectAt(t);
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
                    string block = hasWall ? (wall.tileType + "/" + wall.tileset) : "none";
                    UnityEngine.Debug.Log($"[A11yTileDiag] tile={t.x},{t.y} block={block} ore={hasOre} obj={TileQuery.ObjectId} layers=[{sb}]");
                }
            }
            catch { }
        }

        // Filtre large : requiredObjectFilter ne couvre que quelques couches (etabli,
        // coffre) et rate le Core. On ratisse toutes les couches et on filtre ensuite
        // par "a un ObjectDataCD nomme" -> capte tout objet/construction pose, Core inclus.
        private static readonly CollisionFilter AnyObjectFilter = new CollisionFilter
        {
            BelongsTo = uint.MaxValue,
            CollidesWith = uint.MaxValue,
        };

        // Objet/construction pose sur la case : requete spatiale (les objets sont des
        // entites, pas des tuiles). None si rien.
        private ObjectID ObjectAt(int2 t)
        {
            var cw = PhysicsManager.GetCollisionWorld();
            var hits = new NativeList<DistanceHit>(8, Allocator.Temp);
            ObjectID id = ObjectID.None;
            if (cw.OverlapSphere(new float3(t.x, 0f, t.y), 0.4f, ref hits,
                    AnyObjectFilter, QueryInteraction.Default))
            {
                foreach (var h in hits)
                {
                    if (EntityUtility.HasComponentData<ObjectDataCD>(h.Entity, World))
                    {
                        var od = EntityUtility.GetComponentData<ObjectDataCD>(h.Entity, World);
                        if (od.objectID != ObjectID.None) { id = od.objectID; break; }
                    }
                }
            }
            hits.Dispose();
            return id;
        }
    }
}
