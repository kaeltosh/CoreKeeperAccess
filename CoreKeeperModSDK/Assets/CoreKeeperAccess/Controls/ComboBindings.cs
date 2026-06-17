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
            // Triangle + haut = details de l'element focalise, selon contexte. En jeu, c'est
            // la commande de LOCALISATION (info tuile + coordonnees fusionnees ici, le D-pad
            // droite ne porte plus la position) : curseur detache -> details de la case
            // pointee, qui finit deja par ses coordonnees (AnnounceCursorDetails) ; sinon ->
            // ma position. Le fallback position est enregistre APRES le curseur detache pour
            // ne primer que quand le curseur est attache.
            ComboDispatcher.Register(ComboDispatcher.Combo.Detail,
                () => InventoryNavState.SuppressNativeInput, InventoryNavigator.AnnounceDetail);
            ComboDispatcher.Register(ComboDispatcher.Combo.Detail,
                () => InputContext.MapOpen, TeleportNavigator.AnnounceDetail);
            ComboDispatcher.Register(ComboDispatcher.Combo.Detail,
                () => InputContext.InGameFree && BuildModeNavigator.CursorDetached,
                BuildModeNavigator.AnnounceCursorDetails);
            ComboDispatcher.Register(ComboDispatcher.Combo.Detail,
                () => InputContext.InGameFree && Player() != null, () => VitalsReadout.AnnouncePosition(Player()));

            // Onglet "Mes balises" de la carte : Triangle+bas = supprimer, Triangle+droite =
            // renommer la balise selectionnee. Enregistres AVANT vitals/position pour primer
            // dans ce contexte ; hors de l'onglet, ces combos gardent leur role habituel.
            ComboDispatcher.Register(ComboDispatcher.Combo.Down,
                () => TeleportNavigator.BeaconCategoryActive, TeleportNavigator.DeleteSelectedBeacon);
            ComboDispatcher.Register(ComboDispatcher.Combo.Right,
                () => TeleportNavigator.BeaconCategoryActive, TeleportNavigator.RenameSelectedBeacon);

            // Triangle + bas = transferer (nav inventaire). En jeu, vitals est passe sur la
            // roue de stats (stick droit) - le D-pad bas n'a plus de role gameplay. Sur la
            // carte : supprime une balise (onglet balises), sinon rien.
            ComboDispatcher.Register(ComboDispatcher.Combo.Down,
                () => InventoryNavState.SuppressNativeInput, GameplayInput.TransferSelected);

            // Triangle + droite = reparer (nav inventaire). En jeu, la position est fusionnee
            // sur le D-pad haut (localisation) - plus de role gameplay ici. Sur la carte :
            // renomme une balise (onglet balises), sinon rien.
            ComboDispatcher.Register(ComboDispatcher.Combo.Right,
                () => InventoryNavState.SuppressNativeInput, GameplayInput.RepairSelected);

            // Triangle + gauche = action "de masse" : tout vendre (marchand ouvert) / tout
            // recycler (station). La prospection minerai est passee sur la roue de stats
            // (stick droit) - plus de role gameplay ici. Vente en premier : marchand et
            // station jamais ouverts ensemble, l'ordre rend juste l'intention explicite.
            ComboDispatcher.Register(ComboDispatcher.Combo.Left,
                () => InventoryNavState.SuppressNativeInput && Manager.ui != null && Manager.ui.isSellUIShowing,
                GameplayInput.SellAllToMerchant);
            ComboDispatcher.Register(ComboDispatcher.Combo.Left,
                () => InventoryNavState.SuppressNativeInput, GameplayInput.SalvageStation);

            // Triangle + L1 = ping sonar (jeu normal seulement : pas en inventaire /
            // fiche perso / carte - sur la carte, les bumpers naviguent les categories).
            ComboDispatcher.Register(ComboDispatcher.Combo.BumperL,
                () => !UiBusy() && InputContext.InGameFree && Player() != null,
                () => PingSonar.Trigger(Player()));

            // Triangle + L3 = bascule "direction assistee" (snap cardinal du deplacement),
            // jeu normal seulement. Toggle : reste actif jusqu'a re-bascule.
            ComboDispatcher.Register(ComboDispatcher.Combo.LeftStick,
                () => !UiBusy() && InputContext.InGameFree && Player() != null,
                DirectionAssist.Toggle);

            // Triangle + R1 = pivoter l'objet en main / changer la taille de zone (houe,
            // pelle). On arme l'action ROTATE native -> le jeu tourne/redimensionne lui-meme
            // (meme canal que la pose). Croix reste libre (action Rotate retiree de la manette
            // par TriangleModifier, plus de pivot intempestif).
            ComboDispatcher.Register(ComboDispatcher.Combo.BumperR,
                () => !UiBusy() && InputContext.InGameFree && Player() != null, () =>
                {
                    InventoryNavState.ArmedInput = PlayerInput.InputType.ROTATE;
                    InventoryNavState.ArmedTtl = 2;
                });

            // Triangle + Back = ouvrir le panneau de reglages a11y (maison, TTS). Dispo en
            // jeu (hors menu de pause natif) ; le panneau gele ensuite l'input et navigue au
            // D-pad. Re-fermeture par Rond/Back depuis le panneau lui-meme.
            ComboDispatcher.Register(ComboDispatcher.Combo.Back,
                () => InputContext.InWorld && !InputContext.MenuOpen,
                CoreKeeperAccess.Settings.SettingsMenu.Open);

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
