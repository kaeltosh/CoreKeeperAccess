using System.Collections.Generic;
using I2.Loc;
using Rewired;

namespace CoreKeeperAccess.Controls
{
    // Commandes VANILLA du jeu (celles que le mod ne remplace pas) lues sur le VRAI mapping
    // Rewired du joueur, par contexte. Avantages : libelles d'action localises par le jeu
    // (FR/EN auto), boutons EXACTS meme apres un remap, et les actions que le mod a confisquees
    // (carte sur Triangle, rotation...) ont vu leur binding retire -> elles n'apparaissent pas.
    // Le bouton est traduit en nomenclature reglee (PS/Xbox) via Glyphs. Entrees DESCRIPTIVES
    // (Run null) : on ne fait que les rappeler. Tout sous try/catch : jamais casser le menu.
    internal static class VanillaControls
    {
        // mapCategoryId Rewired par contexte (cf. CoreKeeper_Controls.json) : jeu general (0)
        // + monde (13), inventaire/UI (10), carte (11).
        private static readonly int[] CatGame = { 0, 13 };
        private static readonly int[] CatInventory = { 10 };
        private static readonly int[] CatMap = { 11 };

        // Actions a NE PAS lister : gerees/recablees par le mod, navigation UI nous appartenant,
        // ou bruit (zoom carte, ping...). Evite les doublons avec les couches mod du menu d'aide.
        private static readonly HashSet<int> Excluded = BuildExcluded();

        private static HashSet<int> BuildExcluded()
        {
            var s = new HashSet<int>();
            void Ex(PlayerInput.InputType t) => s.Add((int)t);
            Ex(PlayerInput.InputType.TOGGLE_MAP);
            Ex(PlayerInput.InputType.ROTATE);
            Ex(PlayerInput.InputType.QUICK_MOVE_ITEMS);
            Ex(PlayerInput.InputType.DROP_SELECTED_ITEM);
            Ex(PlayerInput.InputType.TRASH_ITEM);
            Ex(PlayerInput.InputType.SORT);
            Ex(PlayerInput.InputType.QUICK_STACK);
            Ex(PlayerInput.InputType.SWAP_NEXT_HOTBAR);
            Ex(PlayerInput.InputType.SWAP_PREVIOUS_HOTBAR);
            Ex(PlayerInput.InputType.HOT_BAR_SWAP_MODIFIER);
            Ex(PlayerInput.InputType.PICK_UP_HALF);
            Ex(PlayerInput.InputType.PICK_UP_10);
            Ex(PlayerInput.InputType.PICK_UP_ITEMS);
            Ex(PlayerInput.InputType.PICK_UP_ALL_ITEMS);
            Ex(PlayerInput.InputType.INTERACT_WITH_OBJECT);
            Ex(PlayerInput.InputType.MENU_UP);
            Ex(PlayerInput.InputType.MENU_DOWN);
            Ex(PlayerInput.InputType.MENU_LEFT);
            Ex(PlayerInput.InputType.MENU_RIGHT);
            Ex(PlayerInput.InputType.MAP_PING);
            Ex(PlayerInput.InputType.ZOOM_IN_MAP);
            Ex(PlayerInput.InputType.ZOOM_OUT_MAP);
            Ex(PlayerInput.InputType.SELECT_NEXT_MAP_MARKER);
            Ex(PlayerInput.InputType.SELECT_PREVIOUS_MAP_MARKER);
            Ex(PlayerInput.InputType.OPEN_CHAT);
            Ex(PlayerInput.InputType.TOGGLE_UI);
            Ex(PlayerInput.InputType.TOGGLE_SHORTCUTS_WINDOW);
            return s;
        }

        public static void Collect(List<HelpItem> outList)
        {
            try
            {
                int[] cats = CategoriesForContext();
                if (cats == null) return;

                var pi = Manager.main != null && Manager.main.player != null ? Manager.main.player.inputModule : null;
                var rp = pi != null ? pi.rewiredPlayer : null;
                if (rp == null || !ReInput.isReady) return;

                var seen = new HashSet<int>();
                var maps = rp.controllers.maps.GetAllMaps(ControllerType.Joystick);
                if (maps == null) return;

                foreach (var map in maps)
                {
                    if (map == null || !map.enabled) continue;
                    if (System.Array.IndexOf(cats, map.categoryId) < 0) continue;

                    foreach (var aem in map.AllMaps)
                    {
                        if (aem == null) continue;
                        int actionId = aem.actionId;
                        if (Excluded.Contains(actionId) || !seen.Add(actionId)) continue;

                        var action = ReInput.mapping.GetAction(actionId);
                        if (action == null || !action.userAssignable) continue;

                        var btn = Glyphs.FromRewired(aem.elementIdentifierName);
                        if (btn == null) continue;

                        string label = ResolveActionLabel(action, aem);
                        if (string.IsNullOrEmpty(label)) continue;

                        outList.Add(new HelpItem { Gesture = Glyphs.Name(btn.Value), Label = label, Run = null });
                    }
                }
            }
            catch (System.Exception ex) { Diag.Error("A11yVanillaControls", ex); }
        }

        private static int[] CategoriesForContext()
        {
            if (InputContext.MapOpen) return CatMap;
            if (InputContext.InventoryNavActive) return CatInventory;
            if (InputContext.InGameFree) return CatGame;
            return null;
        }

        // Libelle localise par le JEU (meme table que le menu de remap natif) : terme
        // "ControlMapper/<identifiant>", avec le repli "<identifiant>PC" comme le jeu.
        private static string ResolveActionLabel(InputAction action, ActionElementMap aem)
        {
            string id = action.name;
            if (aem.elementType == ControllerElementType.Axis)
            {
                if (aem.axisContribution == Pole.Positive && !string.IsNullOrEmpty(action.positiveDescriptiveName))
                    id = action.positiveDescriptiveName;
                else if (aem.axisContribution == Pole.Negative && !string.IsNullOrEmpty(action.negativeDescriptiveName))
                    id = action.negativeDescriptiveName;
            }

            string t = Loc("ControlMapper/" + id);
            if (string.IsNullOrEmpty(t)) t = Loc("ControlMapper/" + id + "PC");
            if (string.IsNullOrEmpty(t)) t = action.descriptiveName;
            return t;
        }

        private static string Loc(string term)
            => LocalizationManager.GetTranslation(term, true, 0, true, false, null, null, true);
    }
}
