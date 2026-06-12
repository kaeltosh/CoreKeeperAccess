using CoreKeeperAccess.Gameplay;
using CoreKeeperAccess.Navigation;

namespace CoreKeeperAccess.Controls
{
    // Table des combos de la touche access : QUI repond, DANS QUEL contexte, par
    // priorite decroissante (premier enregistre = premier servi). C'est la photo
    // complete des commandes Triangle du mod - ajouter une commande = une ligne ici
    // + une action chez le fournisseur.
    internal static class ComboBindings
    {
        public static void RegisterAll()
        {
            // Triangle + haut = details de l'element focalise, selon contexte.
            ComboDispatcher.Register(ComboDispatcher.Combo.Detail,
                () => InventoryNavState.SuppressNativeInput, InventoryNavigator.AnnounceDetail);
            ComboDispatcher.Register(ComboDispatcher.Combo.Detail,
                () => InputContext.MapOpen, TeleportNavigator.AnnounceDetail);
            ComboDispatcher.Register(ComboDispatcher.Combo.Detail,
                () => InputContext.InGameFree && BuildModeNavigator.CursorDetached,
                BuildModeNavigator.AnnounceCursorDetails);

            // Triangle + bas = transferer (nav inventaire) / vitals (ailleurs, carte comprise).
            ComboDispatcher.Register(ComboDispatcher.Combo.Down,
                () => InventoryNavState.SuppressNativeInput, GameplayInput.TransferSelected);
            ComboDispatcher.Register(ComboDispatcher.Combo.Down,
                () => !UiBusy() && Player() != null, () => VitalsReadout.AnnounceVitals(Player()));

            // Triangle + droite = reparer (nav inventaire) / position (ailleurs).
            ComboDispatcher.Register(ComboDispatcher.Combo.Right,
                () => InventoryNavState.SuppressNativeInput, GameplayInput.RepairSelected);
            ComboDispatcher.Register(ComboDispatcher.Combo.Right,
                () => !UiBusy() && Player() != null, () => VitalsReadout.AnnouncePosition(Player()));

            // Triangle + gauche = tout recycler (nav inventaire) / prospection minerai (ailleurs).
            ComboDispatcher.Register(ComboDispatcher.Combo.Left,
                () => InventoryNavState.SuppressNativeInput, GameplayInput.SalvageStation);
            ComboDispatcher.Register(ComboDispatcher.Combo.Left,
                () => !UiBusy() && Player() != null, () => GameplayInput.RequestProspect(Player()));

            // Triangle + L1 = ping sonar (jeu normal seulement : pas en inventaire /
            // fiche perso / carte - sur la carte, les bumpers naviguent les categories).
            ComboDispatcher.Register(ComboDispatcher.Combo.BumperL,
                () => !UiBusy() && InputContext.InGameFree && Player() != null,
                () => PingSonar.Trigger(Player()));

            // Double-tap Triangle = ouvrir/fermer la carte : on rejoue l'action native
            // TOGGLE_MAP (dont on a confisque le bouton) via l'armement d'input - le
            // jeu fait le reste (toggle, fermeture au B aussi).
            ComboDispatcher.Register(ComboDispatcher.Combo.DoubleTap,
                () => !UiBusy() && Player() != null, () =>
                {
                    InventoryNavState.ArmedInput = PlayerInput.InputType.TOGGLE_MAP;
                    InventoryNavState.ArmedTtl = 2;
                });
        }

        // Gardes vives (lues au moment du combo, pas figees en tete de frame) : la nav
        // inventaire peut basculer pendant l'Update, on route sur son etat REEL.
        private static bool UiBusy() =>
            InventoryNavState.SuppressNativeInput || InputContext.MenuOpen;

        private static PlayerController Player() =>
            Manager.main != null ? Manager.main.player : null;
    }
}
