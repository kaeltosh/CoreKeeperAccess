using CoreKeeperAccess.Gameplay;
using CoreKeeperAccess.Navigation;

namespace CoreKeeperAccess.Controls
{
    // Table des combos de la touche access : QUI repond, DANS QUEL contexte, par
    // priorite decroissante (premier enregistre = premier servi). C'est la photo
    // complete des commandes Triangle du mod - ajouter une commande = une ligne ici
    // + une action chez le fournisseur. Le dernier argument est la cle i18n du LIBELLE
    // (pour le menu d'aide : "Triangle + ... : libelle").
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
                () => InventoryNavState.SuppressNativeInput, InventoryNavigator.AnnounceDetail, "cmd.detail.item");
            ComboDispatcher.Register(ComboDispatcher.Combo.Detail,
                () => InputContext.MapOpen, TeleportNavigator.AnnounceDetail, "cmd.detail.marker");
            ComboDispatcher.Register(ComboDispatcher.Combo.Detail,
                () => InputContext.InGameFree && BuildModeNavigator.CursorDetached,
                BuildModeNavigator.AnnounceCursorDetails, "cmd.detail.tile");
            ComboDispatcher.Register(ComboDispatcher.Combo.Detail,
                () => InputContext.InGameFree && Player() != null, () => VitalsReadout.AnnouncePosition(Player()), "cmd.position");

            // NB : renommer / supprimer une balise passent desormais par le MENU CONTEXTUEL
            // (Croix sur une balise -> liste d'actions, cf. TeleportNavigator). Les combos
            // Triangle+bas/droite de l'onglet balises ont donc ete retires : keymaps liberes.

            // Triangle + bas = bascule mode reparation/renforcement (station recyclage/
            // reparation uniquement). Le transfert est retire de ce combo : deja couvert
            // par RT (gachette, InventoryNavigator.HandleInput) partout ailleurs, y compris
            // dans cette station - rien perdu en liberant Triangle+bas ici.
            ComboDispatcher.Register(ComboDispatcher.Combo.Down,
                () => InputContext.StationOpen, StationCommands.ToggleRepairMode, "cmd.repairmode.toggle");

            // Triangle + droite = ameliorer (forge ouverte) / reparer (autre station) /
            // position (jeu normal). La forge prime sur la reparation : les deux stations
            // ne sont jamais ouvertes ensemble, mais l'ordre rend l'intention explicite.
            // Sur la carte : renomme une balise (onglet balises), sinon rien.
            ComboDispatcher.Register(ComboDispatcher.Combo.Right,
                () => InputContext.ForgeOpen, StationCommands.UpgradeForgeAction, "cmd.upgrade");
            ComboDispatcher.Register(ComboDispatcher.Combo.Right,
                () => InputContext.StationOpen, StationCommands.RepairSelected, "cmd.repair");
            // Etabli/station de craft classique (pas station reparation/forge) : categorie
            // de recettes suivante, si plusieurs batiments sont inclus (ex. enclume fer qui
            // embarque aussi les recettes cuivre). Muet si une seule categorie.
            ComboDispatcher.Register(ComboDispatcher.Combo.Right,
                () => InputContext.CraftingUIOpen, () => StationCommands.SwitchCraftingCategory(true), "cmd.craftcategory.next");
            // En jeu pur : barre rapide suivante. Le D-pad droite vanilla (qui portait SORT,
            // inutile hors inventaire) est confisque par le curseur de tuile -> on remet le
            // swap de barre rapide ici. On arme l'action native, le jeu change la barre et
            // EquipSlot est appele -> HeldItemAnnouncePatch annonce le nouvel objet en main.
            // Muet si une seule rangee (vanilla CanSwapHotBar). Enregistre apres forge/station
            // -> en station c'est reparer qui gagne, en jeu pur c'est nous (gardes exclusives).
            ComboDispatcher.Register(ComboDispatcher.Combo.Right,
                () => !UiBusy() && InputContext.InGameFree && Player() != null, () =>
                {
                    InventoryNavState.ArmedInput = PlayerInput.InputType.SWAP_NEXT_HOTBAR;
                    InventoryNavState.ArmedTtl = 2;
                }, "cmd.hotbar.next");

            // Triangle + gauche = action "de masse" : tout vendre (marchand ouvert) / tout
            // recycler (station). La prospection minerai est passee sur la roue de stats
            // (stick gauche) - plus de role gameplay ici. Vente en premier : marchand et
            // station jamais ouverts ensemble, l'ordre rend juste l'intention explicite.
            ComboDispatcher.Register(ComboDispatcher.Combo.Left,
                () => InventoryNavState.SuppressNativeInput && Manager.ui != null && Manager.ui.isSellUIShowing,
                StationCommands.SellAllToMerchant, "cmd.sellall");
            ComboDispatcher.Register(ComboDispatcher.Combo.Left,
                () => InputContext.StationOpen, StationCommands.SalvageStation, "cmd.salvageall");
            // Etabli/station de craft classique : categorie de recettes precedente
            // (symetrique du Triangle+droite ci-dessus).
            ComboDispatcher.Register(ComboDispatcher.Combo.Left,
                () => InputContext.CraftingUIOpen, () => StationCommands.SwitchCraftingCategory(false), "cmd.craftcategory.prev");
            // En jeu pur : barre rapide precedente (symetrique du Triangle+droite ci-dessus).
            ComboDispatcher.Register(ComboDispatcher.Combo.Left,
                () => !UiBusy() && InputContext.InGameFree && Player() != null, () =>
                {
                    InventoryNavState.ArmedInput = PlayerInput.InputType.SWAP_PREVIOUS_HOTBAR;
                    InventoryNavState.ArmedTtl = 2;
                }, "cmd.hotbar.prev");

            // Triangle + L3 = bascule "direction assistee" (snap cardinal du deplacement),
            // jeu normal seulement. Toggle : reste actif jusqu'a re-bascule.
            ComboDispatcher.Register(ComboDispatcher.Combo.LeftStick,
                () => !UiBusy() && InputContext.InGameFree && Player() != null,
                DirectionAssist.Toggle, "cmd.dirassist");

            // Triangle + R1 = pivoter l'objet en main / changer la taille de zone (houe,
            // pelle). On arme l'action ROTATE native -> le jeu tourne/redimensionne lui-meme
            // (meme canal que la pose). Croix reste libre (action Rotate retiree de la manette
            // par TriangleModifier, plus de pivot intempestif).
            ComboDispatcher.Register(ComboDispatcher.Combo.BumperR,
                () => !UiBusy() && InputContext.InGameFree && Player() != null, () =>
                {
                    InventoryNavState.ArmedInput = PlayerInput.InputType.ROTATE;
                    InventoryNavState.ArmedTtl = 2;
                }, "cmd.rotate");

            // Triangle + Back = ouvrir le panneau de reglages a11y (maison, TTS). Dispo en
            // jeu (hors menu de pause natif) ; le panneau gele ensuite l'input et navigue au
            // D-pad. Re-fermeture par Rond/Back depuis le panneau lui-meme.
            ComboDispatcher.Register(ComboDispatcher.Combo.Back,
                () => InputContext.InWorld && !InputContext.MenuOpen,
                CoreKeeperAccess.Settings.SettingsMenu.Open, "cmd.settings");

            // Triangle + Rond = saut direct a la barre 1, slot 1. Le bouton est bloque cote
            // natif quelle que soit son action (TriangleModifier.CircleActionIds +
            // NativeInputSuppressionPatch), donc pas de double-declenchement a craindre.
            // PIEGE (casse la barre en boucle si oublie) : hotbarStartIndex ET
            // equippedSlotIndex doivent rester coherents a chaque instant - EquipSlot lit
            // equippedSlotIndex - hotbarStartIndex des sa 1re ligne (IsAnySlotEquipped) AVANT
            // de rien ecrire. Changer hotbarStartIndex seul laisse un equippedSlotIndex perime
            // (ex. barre 2) hors bornes -> exception a CHAQUE frame ensuite (UpdateHitArea).
            // On remet donc equippedSlotIndex a jour AVANT d'appeler EquipSlot.
            ComboDispatcher.Register(ComboDispatcher.Combo.Circle,
                () => !UiBusy() && InputContext.InGameFree && Player() != null, () =>
                {
                    var p = Player();
                    p.hotbarStartIndex = 0;
                    p.hotbarEndIndex = 10;
                    p.equippedSlotIndex = 0;
                    p.EquipSlot(0);
                }, "cmd.hotbar.jumpfirst");

            // Double-tap Triangle = ouvrir/fermer la carte : on rejoue l'action native
            // TOGGLE_MAP (dont on a confisque le bouton) via l'armement d'input - le
            // jeu fait le reste (toggle, fermeture au B aussi).
            ComboDispatcher.Register(ComboDispatcher.Combo.DoubleTap,
                () => !UiBusy() && Player() != null, () =>
                {
                    InventoryNavState.ArmedInput = PlayerInput.InputType.TOGGLE_MAP;
                    InventoryNavState.ArmedTtl = 2;
                }, "cmd.togglemap");
        }

        // Gardes vives (lues au moment du combo, pas figees en tete de frame) : la nav
        // inventaire peut basculer pendant l'Update, on route sur son etat REEL.
        private static bool UiBusy() =>
            InventoryNavState.SuppressNativeInput || InputContext.MenuOpen;

        private static PlayerController Player() =>
            Manager.main != null ? Manager.main.player : null;
    }
}
