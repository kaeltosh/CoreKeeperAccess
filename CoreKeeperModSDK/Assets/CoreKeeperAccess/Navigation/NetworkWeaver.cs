using System.Collections.Generic;
using CoreKeeperAccess.Gameplay;
using PugTilemap;
using Unity.Mathematics;

namespace CoreKeeperAccess.Navigation
{
    // Recalcul LOCAL du réseau (tranche C, « mise à jour du réseau »). Dans la zone CHARGÉE
    // autour du joueur (= l'équivalent de la minicarte d'un voyant), on a le droit de CALCULER
    // les arêtes manquantes au lieu d'attendre qu'elles se tissent au passage. On lit les vrais
    // murs via le TileAccessor (donc appelé depuis un système ECS, seul à pouvoir le construire).
    //
    // MODÈLE « ligne de vue » (décision validée) : deux torches sont reliées SEULEMENT si la
    // ligne droite qui les sépare ne traverse que des cases franchissables. C'est ce qui rend le
    // guidage vol-d'oiseau fiable (l'earcon pointe la torche suivante en direct) ; en échange, le
    // joueur doit poser une torche à chaque coude. On ne relie PAS deux torches par un chemin qui
    // contourne un mur — ce serait une arête sur laquelle le guidage enverrait dans le mur.
    //
    // SÛRETÉ (invariants fiche beacon-navigation) :
    //  - AJOUT SEULEMENT : on ne supprime jamais d'arête → zéro risque d'isoler le réseau distant
    //    (frontière intouchable gratuite), et « seule une observation positive modifie le graphe ».
    //  - On ne tisse qu'entre nœuds tous deux DANS le rayon de révision (cœur bien chargé).
    //
    // CLASSIFICATION SÉMANTIQUE : une case est bloquante si le jeu y voit une tuile bloquante
    // (mur, mais AUSSI pit/eau, qui remontent comme bloquantes — confirmé par SonifyTile). Un
    // collider ≠ infranchissable : une PORTE / un PONT posé sur une case bloquante la rend
    // franchissable (liste blanche par type d'objet).
    internal static class NetworkWeaver
    {
        // Liste blanche des objets traités comme sol libre malgré une tuile bloquante dessous
        // (pont au-dessus d'eau/pit) ou un collider (porte). Classés par TYPE d'objet.
        private static readonly HashSet<ObjectID> Passable = new HashSet<ObjectID>
        {
            // Portes (gap dans un mur) — un collider, mais un passage.
            ObjectID.WoodDoor, ObjectID.StoneDoor, ObjectID.ScarletDoor, ObjectID.GleamWoodDoor,
            ObjectID.CoralDoor, ObjectID.GalaxiteDoor, ObjectID.PuzzleDoor, ObjectID.ExcavationDoor,
            ObjectID.ExcavationDrillDoor, ObjectID.ElectricalDoor,
            // Ponts / passerelles (au-dessus d'eau ou de pit).
            ObjectID.WoodBridge, ObjectID.StoneBridge, ObjectID.ScarletBridge, ObjectID.CoralBridge,
            ObjectID.GalaxiteBridge, ObjectID.GlassBridge, ObjectID.GleamWoodBridge,
            ObjectID.MetalGrateBridge, ObjectID.ExcavationBridge,
        };

        // Longueur max d'une arête tissée (cases). Au-delà, on ne relie pas en direct : un
        // long segment droit traversant tout sans torche intermédiaire est improbable en base,
        // et borne le coût.
        private const float MaxEdgeTiles = 20f;

        private static readonly List<int2> _nodes = new List<int2>();

        // Tisse les arêtes manquantes par ligne de vue entre les nœuds du rayon. Retourne le
        // nombre d'arêtes AJOUTÉES (pour l'annonce).
        public static int Weave(ref TileAccessor ta, int2 center, float radius)
        {
            BeaconGraph.NodesInRadius(new float2(center.x, center.y), radius, _nodes);
            int added = 0;
            for (int i = 0; i < _nodes.Count; i++)
                for (int j = i + 1; j < _nodes.Count; j++)
                {
                    int2 a = _nodes[i], b = _nodes[j];
                    float d = math.distance(new float2(a.x, a.y), new float2(b.x, b.y));
                    if (d > MaxEdgeTiles) continue;
                    if (BeaconGraph.HasEdge(a, b)) continue;          // déjà reliée : on ne retouche pas
                    if (!LineWalkable(ref ta, a, b)) continue;        // ligne de vue obstruée
                    BeaconGraph.AddOrReinforceEdge(a, b, d, BeaconGraph.TypeOf(a), BeaconGraph.TypeOf(b));
                    added++;
                }
            return added;
        }

        // Toutes les cases traversées par le segment a→b sont-elles franchissables ?
        // Traversée de grille (Amanatides-Woo 2D) : on visite chaque cellule que la droite
        // entame, du départ à l'arrivée.
        private static bool LineWalkable(ref TileAccessor ta, int2 a, int2 b)
        {
            int x = a.x, y = a.y;
            int dx = b.x - a.x, dy = b.y - a.y;
            int stepX = dx > 0 ? 1 : (dx < 0 ? -1 : 0);
            int stepY = dy > 0 ? 1 : (dy < 0 ? -1 : 0);
            float adx = math.abs(dx), ady = math.abs(dy);
            float tDeltaX = adx > 0 ? 1f / adx : float.MaxValue;
            float tDeltaY = ady > 0 ? 1f / ady : float.MaxValue;
            float tMaxX = adx > 0 ? 0.5f / adx : float.MaxValue; // 1re frontière à une demi-case
            float tMaxY = ady > 0 ? 0.5f / ady : float.MaxValue;

            if (!IsWalkable(ref ta, new int2(x, y))) return false;
            int guard = (int)(adx + ady) + 2; // garde-fou anti-boucle
            while ((x != b.x || y != b.y) && guard-- > 0)
            {
                if (tMaxX < tMaxY) { tMaxX += tDeltaX; x += stepX; }
                else { tMaxY += tDeltaY; y += stepY; }
                if (!IsWalkable(ref ta, new int2(x, y))) return false;
            }
            return true;
        }

        // Une case est franchissable si le jeu n'y voit pas de tuile bloquante (mur / pit / eau),
        // OU si un pont / une porte (liste blanche) y est posé.
        private static bool IsWalkable(ref TileAccessor ta, int2 t)
        {
            if (!ta.TryGetBlockingTile(t, out _, true)) return true; // sol libre (porte sur sol incluse)
            return ObjectIndex.TryGet(t, out var e) && Passable.Contains(e.Id);
        }
    }
}
