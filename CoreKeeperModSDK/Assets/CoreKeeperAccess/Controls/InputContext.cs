namespace CoreKeeperAccess.Controls
{
    // Registre central des contextes d'input (moteur de keymaps v1). Une seule source
    // de verite pour "dans quel etat d'UI est-on" et "qui possede le D-pad", a la place
    // des gardes recalculees dans chaque consommateur (migration iso-fonctionnelle :
    // memes signaux, memes valeurs, zero comportement nouveau).
    //
    // Les etats d'UI sont figes une fois par frame par Refresh() (tete de l'Update du
    // mod). Owner est une propriete VIVE : elle depend de signaux poses en cours de
    // frame (InfoKey.ModifierHeld, LaserCane.Active) et doit etre lue apres eux -
    // la figer en tete de frame introduirait un retard d'une frame (non-iso).
    internal static class InputContext
    {
        // Proprietaire du D-pad, par priorite decroissante. La touche access traverse
        // toutes les couches : Triangle tenu prime sur tout autre proprietaire.
        internal enum PadOwner
        {
            None,       // pas en jeu (pas de joueur / UI pas prete)
            AccessKey,  // Triangle tenu : D-pad reserve aux combos
            Menu,       // menu (pause, options...) : le jeu navigue, on ne touche a rien
            Map,        // carte ouverte : TeleportNavigator
            Inventory,  // fenetre inventaire / fiche perso : nav par sections
            Laser,      // canne laser (stick droit pousse)
            Cursor,     // jeu normal : curseur de tuile
        }

        // --- Etats d'UI (figes par Refresh, une fois par frame) ---
        public static bool InWorld { get; private set; }             // joueur charge, UI prete
        public static bool MenuOpen { get; private set; }            // menu pause/options (monde fige)
        public static bool MapOpen { get; private set; }
        public static bool AnyInventoryOpen { get; private set; }    // fenetre inventaire du jeu
        public static bool CharacterWindowOpen { get; private set; }
        public static bool StationOpen { get; private set; }         // station de reparation/recyclage
        public static bool InventoryNavActive { get; private set; }  // notre nav inventaire tient la main

        // Composites des anciennes gardes.
        public static bool UiBusy { get; private set; }     // nav inventaire OU menu : combos gameplay muets
        public static bool InGameFree { get; private set; } // gameplay nu : ni fenetre, ni fiche perso, ni carte

        public static void Refresh()
        {
            var ui = Manager.ui;
            InWorld = Manager.main != null && Manager.main.player != null && ui != null;
            MenuOpen = Manager.menu != null && Manager.menu.IsAnyMenuActive();
            MapOpen = InWorld && ui.isShowingMap;
            AnyInventoryOpen = InWorld && ui.isAnyInventoryShowing;
            CharacterWindowOpen = InWorld && ui.characterWindow != null && ui.characterWindow.isShowing;
            StationOpen = InWorld && ui.isSalvageAndRepairUIShowing;
            InventoryNavActive = Navigation.InventoryNavState.SuppressNativeInput;

            UiBusy = InventoryNavActive || MenuOpen;
            InGameFree = InWorld && !AnyInventoryOpen && !CharacterWindowOpen && !MapOpen;
        }

        // Proprietaire COURANT du D-pad (vif, voir note de classe sur l'ordre de lecture).
        public static PadOwner Owner =>
            !InWorld ? PadOwner.None
            : InfoKey.ModifierHeld ? PadOwner.AccessKey
            : MenuOpen ? PadOwner.Menu
            : MapOpen ? PadOwner.Map
            : (AnyInventoryOpen || CharacterWindowOpen) ? PadOwner.Inventory
            : Gameplay.LaserCane.Active ? PadOwner.Laser
            : PadOwner.Cursor;
    }
}
