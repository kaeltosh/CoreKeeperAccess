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
            // Fiche perso / inventaire simple (pas station) : profil d'equipement suivant.
            // DOIT etre enregistre AVANT le combo categorie d'artisanat ci-dessous : le
            // panneau d'artisanat "simple" (recettes a main nue) est TOUJOURS actif en
            // fond des qu'on ouvre juste son sac (CraftingHandler.craftingType.Simple,
            // cf. UIManager.OnPlayerInventoryOpen), donc CraftingUIOpen est vrai EN MEME
            // TEMPS que CharacterWindowOpen dans ce cas precis - sans cet ordre, le combo
            // categorie (muet, une seule categorie en simple) gagnait en silence et celui-ci
            // n'etait jamais atteint (bug rapporte par testeur). A une VRAIE station de
            // craft (etabli, enclume...), CharacterWindowOpen est FAUX (le jeu masque la
            // fiche perso des que craftingHandler != player.playerCraftingHandler), donc
            // aucun risque de voler la categorie suivante a une vraie station.
            // SetActiveEquipmentPreset() est le meme canal que les onglets natifs
            // (CharacterWindowUI, clic d'onglet) - appel direct, pas besoin d'armer une
            // action native. Deja annonce par le postfix existant sur SetActiveEquipmentPreset
            // (InGameTtsPatch), rien a ajouter cote TTS ici.
            ComboDispatcher.Register(ComboDispatcher.Combo.Right,
                () => InputContext.CharacterWindowOpen && Player() != null,
                () => CycleEquipmentPreset(1), "cmd.equippreset.next");
            // Etabli/station de craft classique (pas station reparation/forge) : categorie
            // de recettes suivante, si plusieurs batiments sont inclus (ex. enclume fer qui
            // embarque aussi les recettes cuivre). Muet si une seule categorie.
            ComboDispatcher.Register(ComboDispatcher.Combo.Right,
                () => InputContext.CraftingUIOpen, () => StationCommands.SwitchCraftingCategory(true), "cmd.craftcategory.next");
            // En jeu pur : pivoter l'objet en main / changer la taille de zone (houe, pelle).
            // Recase ici le 7 juillet (etait sur Triangle+R1, desormais barre rapide) - on
            // arme l'action ROTATE native, le jeu tourne/redimensionne lui-meme (meme canal
            // que la pose). Un seul sens de rotation existe cote natif (PlaceObjectSlot.Rotate
            // ne prend pas de parametre de sens) -> pas d'equivalent gauche, D-pad gauche
            // reste libre en jeu pur.
            ComboDispatcher.Register(ComboDispatcher.Combo.Right,
                () => !UiBusy() && InputContext.InGameFree && Player() != null, () =>
                {
                    InventoryNavState.ArmedInput = PlayerInput.InputType.ROTATE;
                    InventoryNavState.ArmedTtl = 2;
                }, "cmd.rotate");

            // Triangle + gauche = action "de masse" : tout vendre (marchand ouvert) / tout
            // recycler (station). La prospection minerai est passee sur la roue de stats
            // (stick gauche) - plus de role gameplay ici. Vente en premier : marchand et
            // station jamais ouverts ensemble, l'ordre rend juste l'intention explicite.
            ComboDispatcher.Register(ComboDispatcher.Combo.Left,
                () => InventoryNavState.SuppressNativeInput && Manager.ui != null && Manager.ui.isSellUIShowing,
                StationCommands.SellAllToMerchant, "cmd.sellall");
            ComboDispatcher.Register(ComboDispatcher.Combo.Left,
                () => InputContext.StationOpen, StationCommands.SalvageStation, "cmd.salvageall");
            // Fiche perso / inventaire simple : profil d'equipement precedent. Meme piege
            // et meme ordre que Triangle+droite ci-dessus (doit primer sur la categorie
            // d'artisanat simple, toujours active en fond dans l'inventaire nu).
            ComboDispatcher.Register(ComboDispatcher.Combo.Left,
                () => InputContext.CharacterWindowOpen && Player() != null,
                () => CycleEquipmentPreset(-1), "cmd.equippreset.prev");
            // Etabli/station de craft classique : categorie de recettes precedente
            // (symetrique du Triangle+droite ci-dessus).
            ComboDispatcher.Register(ComboDispatcher.Combo.Left,
                () => InputContext.CraftingUIOpen, () => StationCommands.SwitchCraftingCategory(false), "cmd.craftcategory.prev");
            // En jeu pur, D-pad gauche est libre (pas d'equivalent au pivoter de droite -
            // ROTATE n'a qu'un seul sens cote natif). La barre rapide precedente est
            // desormais sur Triangle+L1 (cf. plus bas).

            // Triangle + L3 = bascule "direction assistee" (snap cardinal du deplacement),
            // jeu normal seulement. Toggle : reste actif jusqu'a re-bascule.
            ComboDispatcher.Register(ComboDispatcher.Combo.LeftStick,
                () => !UiBusy() && InputContext.InGameFree && Player() != null,
                DirectionAssist.Toggle, "cmd.dirassist");

            // Triangle + R1/L1 = barre rapide suivante/precedente (recase ici le 7 juillet,
            // etait sur D-pad droite/gauche - le pivoter a pris leur place, cf. plus haut).
            // L1 recupere le role laisse vacant par le ping sonar (retire le 6 juillet, juge
            // redondant avec le scanner de proximite R3). On arme l'action native, le jeu
            // change la barre et EquipSlot est appele -> HeldItemAnnouncePatch annonce le
            // nouvel objet en main. Muet si une seule rangee (vanilla CanSwapHotBar).
            ComboDispatcher.Register(ComboDispatcher.Combo.BumperR,
                () => !UiBusy() && InputContext.InGameFree && Player() != null, () =>
                {
                    InventoryNavState.ArmedInput = PlayerInput.InputType.SWAP_PREVIOUS_HOTBAR;
                    InventoryNavState.ArmedTtl = 2;
                }, "cmd.hotbar.prev");
            ComboDispatcher.Register(ComboDispatcher.Combo.BumperL,
                () => !UiBusy() && InputContext.InGameFree && Player() != null, () =>
                {
                    InventoryNavState.ArmedInput = PlayerInput.InputType.SWAP_NEXT_HOTBAR;
                    InventoryNavState.ArmedTtl = 2;
                }, "cmd.hotbar.next");

            // Triangle + Back = ouvrir le panneau de reglages a11y (maison, TTS). Dispo en
            // jeu (hors menu de pause natif) ; le panneau gele ensuite l'input et navigue au
            // D-pad. Re-fermeture par Rond/Back depuis le panneau lui-meme.
            ComboDispatcher.Register(ComboDispatcher.Combo.Back,
                () => InputContext.InWorld && !InputContext.MenuOpen,
                CoreKeeperAccess.Settings.SettingsMenu.Open, "cmd.settings");

            // Triangle + Rond = saut direct a la barre 1, slot 1. Le bouton est bloque cote
            // natif quelle que soit son action (TriangleModifier.CircleActionIds +
            // NativeInputSuppressionPatch) SAUF pendant l'instrument (cf. Blocks), donc pas
            // de double-declenchement a craindre hors instrument.
            // PIEGE ORIGINAL (crash mode instrument) : lance en pleine lecture d'instrument,
            // le saut de slot changeait l'objet visuellement equipe SANS que le jeu ait
            // proprement quitte l'etat PlayingInstrument -> plantage. Fix : on reproduit ce
            // que fait le bouton Fermer natif (CloseButton.OnLeftClicked) AVANT le saut -
            // fermer inventaire/artisanat, fermer la carte, et si un instrument est en cours,
            // poser stopPlayingInstrument (le jeu s'occupe de la sortie propre la frame
            // suivante). Filet de securite meme si UiBusy/InGameFree bloquent deja les autres
            // cas UI ouverte.
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
                    Manager.ui.HideAllInventoryAndCraftingUI(true);
                    Manager.ui.HideMap();
                    if (p.instrumentHandler != null && p.instrumentHandler.IsPlayingInstrument)
                        p.stopPlayingInstrument = true;
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

        private const int EquipmentPresetCount = 3; // EquipmentPresetsBuffer.MaxEquipmentPresets (natif)

        private static void CycleEquipmentPreset(int dir)
        {
            var p = Player();
            int next = ((p.activeEquipmentPreset + dir) % EquipmentPresetCount + EquipmentPresetCount) % EquipmentPresetCount;
            p.SetActiveEquipmentPreset(next);
        }
    }
}
