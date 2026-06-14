using System.Collections.Generic;
using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Gameplay;
using CoreKeeperAccess.Navigation;
using HarmonyLib;
using UnityEngine;

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
            // Croix sur un slot de bourse masque : l'action native taperait sur la barre
            // rapide (le curseur ne tient pas le slot) -> on l'etouffe, InventoryNavigator
            // route prendre/poser sur le bon slot lui-meme.
            if (InventoryNavState.SuppressNativeInput && InventoryNavState.OnMaskedSlot
                && t == PlayerInput.InputType.PICK_UP_ALL_ITEMS) return true;
            if (BuildModeNavigator.StealsDpad && DpadInGame.Contains(t)) return true;
            // Curseur detache : on vole Croix pour que l'interaction passe par la case
            // visee, pas l'objet adjacent natif (sinon impossible d'agir pres d'un coffre).
            if (BuildModeNavigator.StealsCross && t == PlayerInput.InputType.INTERACT_WITH_OBJECT) return true;
            // Triangle tenu : L1 = ping sonar -> son action native (slot precedent)
            // ne doit pas partir en meme temps. RB n'est pas vole (pas de combo dessus).
            if (InfoKey.ModifierHeld && t == PlayerInput.InputType.PREVIOUS_SLOT) return true;
            // Triangle tenu : R1 = pivoter / changer taille -> ne pas changer de slot.
            if (InfoKey.ModifierHeld && t == PlayerInput.InputType.NEXT_SLOT) return true;
            // Triangle tenu : L3 = bascule direction assistee -> ne pas changer de torche.
            if (InfoKey.ModifierHeld && t == PlayerInput.InputType.QUICK_SWAP_TORCH) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.WasButtonPressedDownThisFrame))]
    internal static class PlayerInputWasButtonPressedPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayerInput.InputType inputType, ref bool __result)
        {
            // Action gameplay armee (pose / mine / interagir) : on simule l'appui SANS
            // consommer. SendClientInputSystem lit ce bouton plusieurs fois dans la meme
            // passe (ex. INTERACT) ; consommer a la 1re lecture casserait la 2e (celle
            // qui pose le button state). PlayerMoveToSystem desarme apres la passe.
            if (GameplayAction.Pressed.HasValue && GameplayAction.Pressed.Value == inputType)
            {
                __result = true;
                return false;
            }
            // Action armee par la roue d'inventaire : on simule un appui (une seule lecture).
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
            // Bouton "maintenu" arme par une action gameplay (ex. SECOND_INTERACT pour
            // poser, qui exige le held). Non consomme : desarme par PlayerMoveToSystem.
            if (GameplayAction.Held.HasValue && GameplayAction.Held.Value == inputType)
            {
                __result = true;
                return false;
            }
            if (!StolenInputTypes.Blocks(inputType)) return true;
            __result = false;
            return false;
        }
    }

    // Injection de visee : quand une action gameplay est armee, on force le stick droit
    // virtuel vers la case ciblee. SendClientInputSystem en derive facingDirection /
    // targetingDirection / mouseOrJoystickWorldPoint -> pose et minage frappent la bonne
    // case. On ne patche que la surcharge a deux axes (la paire de visee).
    [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.GetInputAxisValue),
        new[] { typeof(PlayerInput.InputAxisType), typeof(PlayerInput.InputAxisType) })]
    internal static class PlayerInputAimInjectionPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(PlayerInput.InputAxisType horizontalAxisType,
            PlayerInput.InputAxisType verticalAxisType, ref Vector2 __result)
        {
            if (GameplayAction.AimActive
                && horizontalAxisType == PlayerInput.InputAxisType.CHARACTER_AIM_HORIZONTAL
                && verticalAxisType == PlayerInput.InputAxisType.CHARACTER_AIM_VERTICAL)
            {
                __result = GameplayAction.AimDir;
                return false;
            }
            return true;
        }
    }
}
