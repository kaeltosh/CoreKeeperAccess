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
            Settings,   // panneau de reglages a11y ouvert : modal, prime sur tout
            AccessKey,  // Triangle tenu : D-pad reserve aux combos
            Scanner,    // R3 tenu (et Triangle relache) : D-pad reserve au scanner de proximite
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
        public static bool ForgeOpen { get; private set; }           // forge d'amelioration (1 slot)
        public static bool CraftingUIOpen { get; private set; }      // etabli/station de craft classique (categories de tier)
        public static bool InventoryNavActive { get; private set; }  // notre nav inventaire tient la main
        public static bool SettingsOpen { get; private set; }        // panneau de reglages a11y ouvert
        public static bool ActionMenuOpen { get; private set; }      // menu contextuel / menu d'aide ouvert
        public static bool PadLearnActive { get; private set; }      // mode decouverte de la manette
        public static bool SoundGuideOpen { get; private set; }      // menu d'apprentissage des sons

        // Composites des anciennes gardes.
        public static bool UiBusy { get; private set; }     // nav inventaire OU menu : combos gameplay muets
        public static bool InGameFree { get; private set; } // gameplay nu : ni fenetre, ni fiche perso, ni carte, ni menu

        // Un menu MODAL a11y est ouvert (panneau de reglages, menu contextuel / menu d'aide,
        // saisie de nom) : tout navigateur du mod qui lit le D-pad EN DIRECT doit s'effacer
        // (le modal lit les boutons physiques lui-meme). Lecture VIVE (pas de retard d'une
        // frame a l'ouverture). UiBusy ne suffit pas en inventaire : il y est deja vrai.
        public static bool ModalA11yOpen => Settings.SettingsMenu.Active || ActionMenu.Active || TextEntry.Active || PadLearn.Active || SoundGuide.Active;

        private static bool _prevStationOpen;

        public static void Refresh()
        {
            var ui = Manager.ui;
            InWorld = Manager.main != null && Manager.main.player != null && ui != null;
            MenuOpen = Manager.menu != null && Manager.menu.IsAnyMenuActive();
            MapOpen = InWorld && ui.isShowingMap;
            AnyInventoryOpen = InWorld && ui.isAnyInventoryShowing;
            CharacterWindowOpen = InWorld && ui.characterWindow != null && ui.characterWindow.isShowing;
            StationOpen = InWorld && ui.isSalvageAndRepairUIShowing;
            ForgeOpen = InWorld && ui.isUpgradeForgeUIShowing;
            CraftingUIOpen = InWorld && ui.isCraftingUIShowing;
            // Remise a zero du mode reparation/renforcement a chaque (re)ouverture de la
            // station : jamais de renforcement laisse arme d'une visite precedente.
            if (StationOpen && !_prevStationOpen) Gameplay.StationCommands.ResetRepairMode();
            _prevStationOpen = StationOpen;
            InventoryNavActive = Navigation.InventoryNavState.SuppressNativeInput;
            SettingsOpen = Settings.SettingsMenu.Active;
            ActionMenuOpen = ActionMenu.Active;
            PadLearnActive = PadLearn.Active;
            SoundGuideOpen = SoundGuide.Active;

            // Modaux a11y (panneau de reglages, menu contextuel / menu d'aide) : on les
            // compte dans UiBusy ET on retire InGameFree -> laser, curseur, sentinelle,
            // sonar, feu... se taisent tant qu'un de ces menus est ouvert (y compris en jeu
            // libre, ou le menu d'aide peut s'ouvrir hors carte).
            UiBusy = InventoryNavActive || MenuOpen || SettingsOpen || ActionMenuOpen || PadLearnActive || SoundGuideOpen;
            // !MenuOpen : un menu overlay qui ne gele pas le jeu (ex. "gerer les
            // joueurs", pousse par-dessus le monde) laissait laser/curseur/feu tourner
            // par-dessus. Un menu pause normal gelait le jeu -> trou masque jusqu'ici.
            InGameFree = InWorld && !AnyInventoryOpen && !CharacterWindowOpen && !MapOpen && !SettingsOpen && !MenuOpen && !ActionMenuOpen && !PadLearnActive && !SoundGuideOpen;
        }

        // Proprietaire COURANT du D-pad (vif, voir note de classe sur l'ordre de lecture).
        public static PadOwner Owner =>
            !InWorld ? PadOwner.None
            : Settings.SettingsMenu.Active ? PadOwner.Settings
            : ActionMenu.Active ? PadOwner.Settings // modal a11y : possede le D-pad comme le panneau
            : SoundGuide.Active ? PadOwner.Settings // idem : menu d'apprentissage des sons
            : InfoKey.ModifierHeld ? PadOwner.AccessKey
            : ScannerModifier.Held ? PadOwner.Scanner
            : MenuOpen ? PadOwner.Menu
            : MapOpen ? PadOwner.Map
            : (AnyInventoryOpen || CharacterWindowOpen) ? PadOwner.Inventory
            : Gameplay.LaserCane.Active ? PadOwner.Laser
            : PadOwner.Cursor;
    }
}
