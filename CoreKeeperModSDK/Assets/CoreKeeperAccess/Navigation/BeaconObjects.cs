using System.Collections.Generic;

namespace CoreKeeperAccess.Navigation
{
    // Classification PARTAGEE des objets du réseau de navigation (cf. fiche beacon-navigation).
    // Centralise « qu'est-ce qu'un nœud » et « qu'est-ce qui est franchissable » pour que le
    // tissage au passage (BeaconTracker), le recalcul local (NetworkWeaver) et l'élagage
    // anti-isolement parlent tous le même langage.
    //
    // NŒUDS (à plat, seul le libellé TTS diffère) : torches (balises lumineuses posées en
    // route) + portes (un passage franchi devient naturellement un nœud).
    // FRANCHISSABLES (liste blanche, classés par TYPE d'objet, jamais sur la présence d'un
    // collider) : portes + ponts / passerelles, traités comme sol libre dans le recalcul.
    internal static class BeaconObjects
    {
        private static readonly HashSet<ObjectID> Torches = new HashSet<ObjectID>
        {
            ObjectID.Torch, ObjectID.FishoilTorch,
        };

        private static readonly HashSet<ObjectID> Doors = new HashSet<ObjectID>
        {
            ObjectID.WoodDoor, ObjectID.StoneDoor, ObjectID.ScarletDoor, ObjectID.GleamWoodDoor,
            ObjectID.CoralDoor, ObjectID.GalaxiteDoor, ObjectID.PuzzleDoor, ObjectID.ExcavationDoor,
            ObjectID.ExcavationDrillDoor, ObjectID.ElectricalDoor,
        };

        private static readonly HashSet<ObjectID> Bridges = new HashSet<ObjectID>
        {
            ObjectID.WoodBridge, ObjectID.StoneBridge, ObjectID.ScarletBridge, ObjectID.CoralBridge,
            ObjectID.GalaxiteBridge, ObjectID.GlassBridge, ObjectID.GleamWoodBridge,
            ObjectID.MetalGrateBridge, ObjectID.ExcavationBridge,
        };

        // L'objet est-il un NŒUD du réseau ? Sort son type (torche / porte).
        public static bool IsNode(ObjectID id, out NodeType type)
        {
            if (Torches.Contains(id)) { type = NodeType.Torch; return true; }
            if (Doors.Contains(id)) { type = NodeType.Door; return true; }
            type = default;
            return false;
        }

        // L'objet rend-il sa case FRANCHISSABLE malgré une tuile bloquante dessous (pont sur
        // eau/pit) ou son propre collider (porte) ? Liste blanche = portes + ponts.
        public static bool IsPassable(ObjectID id) => Doors.Contains(id) || Bridges.Contains(id);
    }
}
