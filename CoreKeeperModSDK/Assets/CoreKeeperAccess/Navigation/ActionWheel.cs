using CoreKeeperAccess.Controls;
using Rewired;

namespace CoreKeeperAccess.Navigation
{
    // Roue d'actions au stick gauche (uniquement quand l'inventaire est ouvert, donc
    // le stick est libre). On pointe une des 8 directions -> annonce de l'action ;
    // un clic R3 l'execute. Les actions "volees" (tri, empiler, ramasser moitie,
    // pages de barre rapide, poubelle) sont rejouees en armant leur InputType natif.
    // Portee sur le socle generique CommandWheel ; positions HISTORIQUES conservees
    // (AddAtSector : les reflexes appris priment sur l'indexation des roues futures).
    // Le transfert a ete RELOGE sur Triangle + D-pad bas -> O et NO restent libres.
    internal static class ActionWheel
    {
        private const int LeftStickX = 0, LeftStickY = 1, R3 = 15;

        private static readonly CommandWheel Wheel = Build();

        private static CommandWheel Build()
        {
            var w = new CommandWheel(LeftStickX, LeftStickY, R3);
            // Secteurs physiques 0=N,1=NE,2=E,3=SE,4=S,5=SO (disposition apprise).
            w.AddAtSector(0, "wheel.sort", () => Arm(PlayerInput.InputType.SORT));
            w.AddAtSector(1, "wheel.hotbarnext", () => Arm(PlayerInput.InputType.SWAP_NEXT_HOTBAR));
            w.AddAtSector(2, "wheel.quickstack", () => Arm(PlayerInput.InputType.QUICK_STACK));
            w.AddAtSector(3, "wheel.hotbarprev", () => Arm(PlayerInput.InputType.SWAP_PREVIOUS_HOTBAR));
            w.AddAtSector(4, "wheel.pickuphalf", () => Arm(PlayerInput.InputType.PICK_UP_HALF));
            w.AddAtSector(5, "wheel.trash", () => Arm(PlayerInput.InputType.TRASH_ITEM));
            // Secteur 6 = Ouest (cardinal libre). Lacher l'objet du slot survole au sol.
            // Le drop natif est un geste a deux boutons (maintien L2 + Croix) lu en continu,
            // donc PAS armable : appel direct du canal serveur officiel (comme le transfert).
            w.AddAtSector(6, "wheel.drop", DropSelected);
            return w;
        }

        // Reproduit UIMouse.DropSelectedObjectToWorld (private cote jeu) : DropItem passe
        // par QueueInputAction -> server-authoritative respecte. Gardes natives reprises :
        // pas de coffre verrouille en place, pas l'inventaire d'achat du marchand.
        internal static void DropSelected()
        {
            var slot = (Manager.ui != null ? Manager.ui.currentSelectedUIElement : null) as InventorySlotUI;
            var player = Manager.main != null ? Manager.main.player : null;
            if (slot == null || player == null) return;
            var handler = slot.GetInventoryHandler();
            if (handler == null || handler.objectsGetLockedInPlace || handler.isBuyInventory) return;
            handler.DropItem(player, slot.inventorySlotIndex,
                EntityMonoBehaviour.ToWorldFromRender(player.transform.position), player.entity);
        }

        public static void Tick()
        {
            var joy = ReInput.isReady ? ReInput.controllers.GetLastActiveController<Joystick>() : null;
            Wheel.Tick(joy);
        }

        private static void Arm(PlayerInput.InputType input)
        {
            InventoryNavState.ArmedInput = input;
            InventoryNavState.ArmedTtl = 2;
        }
    }
}
