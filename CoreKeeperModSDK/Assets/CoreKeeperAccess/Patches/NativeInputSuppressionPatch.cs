using System.Collections.Generic;
using CoreKeeperAccess.Gameplay;
using CoreKeeperAccess.Navigation;
using HarmonyLib;

namespace CoreKeeperAccess.Patches
{
    // Tant que la navigation a11y de l'inventaire tient la main, on neutralise pour
    // le jeu les actions portees par le D-pad et les bumpers (qu'on a "voles" pour
    // notre navigation) : tri, empiler vite, ramasser objets / moitie, changement de
    // page de barre rapide. On lit ces boutons en brut de notre cote (Rewired), donc
    // les bloquer ici ne nous gene pas. A / RT / LT (prendre-poser, transferer,
    // lacher) ne sont PAS dans la liste : ils restent fonctionnels.
    internal static class StolenInputTypes
    {
        public static readonly HashSet<PlayerInput.InputType> Set = new HashSet<PlayerInput.InputType>
        {
            PlayerInput.InputType.PICK_UP_ITEMS,
            PlayerInput.InputType.PICK_UP_HALF,
            PlayerInput.InputType.QUICK_STACK,
            PlayerInput.InputType.SORT,
            PlayerInput.InputType.SWAP_NEXT_HOTBAR,
            PlayerInput.InputType.SWAP_PREVIOUS_HOTBAR,
            // Navigation UI native (D-pad dans les fenetres compétences/talents/stats,
            // et stick). Neutralisee pour que seule NOTRE navigation pilote la selection
            // (sinon double deplacement : nous + le jeu -> annonces qui oscillent).
            PlayerInput.InputType.MENU_UP,
            PlayerInput.InputType.MENU_DOWN,
            PlayerInput.InputType.MENU_LEFT,
            PlayerInput.InputType.MENU_RIGHT,
        };

        // En jeu (curseur de tuile actif), seules les 4 actions portees par le D-pad
        // sont a voler (tri, empiler, swap de page de barre rapide).
        private static readonly HashSet<PlayerInput.InputType> DpadInGame = new HashSet<PlayerInput.InputType>
        {
            PlayerInput.InputType.QUICK_STACK,
            PlayerInput.InputType.SORT,
            PlayerInput.InputType.SWAP_NEXT_HOTBAR,
            PlayerInput.InputType.SWAP_PREVIOUS_HOTBAR,
        };

        public static bool Blocks(PlayerInput.InputType t)
        {
            if (InventoryNavState.SuppressNativeInput && Set.Contains(t)) return true;
            if (BuildModeNavigator.StealsDpad && DpadInGame.Contains(t)) return true;
            // Curseur detache : on vole Croix pour que l'interaction passe par la case
            // visee, pas l'objet adjacent natif (sinon impossible d'agir pres d'un coffre).
            if (BuildModeNavigator.StealsCross && t == PlayerInput.InputType.INTERACT_WITH_OBJECT) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.WasButtonPressedDownThisFrame))]
    internal static class PlayerInputWasButtonPressedPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayerInput.InputType inputType, ref bool __result)
        {
            // Action armee par la roue : on simule un appui (une seule lecture).
            if (InventoryNavState.ArmedInput.HasValue && InventoryNavState.ArmedInput.Value == inputType)
            {
                InventoryNavState.ArmedInput = null; // consomme
                __result = true;
                return false;
            }
            if (!StolenInputTypes.Blocks(inputType)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.IsButtonCurrentlyDown))]
    internal static class PlayerInputIsButtonDownPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayerInput.InputType inputType, ref bool __result)
        {
            if (!StolenInputTypes.Blocks(inputType)) return true;
            __result = false;
            return false;
        }
    }
}
