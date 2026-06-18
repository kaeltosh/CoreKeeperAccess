using System.Collections.Generic;
using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Gameplay;
using CoreKeeperAccess.Navigation;
using CoreKeeperAccess.Settings;
using HarmonyLib;
using Unity.Mathematics;
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
            // Saisie d'un nom de balise, panneau de reglages OU menu contextuel ouvert : tout
            // l'input gameplay est gele (modal). Ces modaux lisent les boutons physiques de
            // leur cote. NB : le menu contextuel se ferme AVANT de rearmer un input (fermeture
            // de carte) -> l'armement passe ensuite, ActionMenu.Active etant deja faux.
            if (TextEntry.Active || SettingsMenu.Active || ActionMenu.Active) { __result = false; return false; }
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
            if (TextEntry.Active || SettingsMenu.Active || ActionMenu.Active) { __result = false; return false; }
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
            // Saisie, panneau de reglages OU menu contextuel : axes a deux canaux geles.
            if (TextEntry.Active || SettingsMenu.Active || ActionMenu.Active) { __result = Vector2.zero; return false; }
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

    // Pendant la saisie d'un nom de balise : on coupe aussi les axes a un seul canal
    // (le mouvement perso passe par CHARACTER_MOVEMENT_HORIZONTAL/VERTICAL). Combine au
    // gel des boutons et de l'axe de visee, le clavier ne pilote plus rien en jeu pendant
    // qu'on tape (le mod lit Input.inputString de son cote, canal independant).
    [HarmonyPatch(typeof(PlayerInput), nameof(PlayerInput.GetInputAxisValue),
        new[] { typeof(PlayerInput.InputAxisType) })]
    internal static class PlayerInputAxisSilencePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref float __result)
        {
            if (!TextEntry.Active && !SettingsMenu.Active && !ActionMenu.Active) return true;
            __result = 0f;
            return false;
        }
    }

    // === Focus mortier (canne laser) ===
    // Quand un mortier est equipe et que la canne laser vise un ennemi (LaserCane publie
    // l'intention ici), on bascule le viseur du mortier en "mode souris" et on pose son
    // point d'impact DIRECTEMENT sur l'ennemi. Le mode souris pose la cible instantanement ;
    // le viseur manette, lui, rampe a vitesse finie (offset integre dans le temps) -> pas
    // reactif. Actif uniquement en combat ; relache des que le laser lache l'ennemi.
    internal static class MortarFocus
    {
        public static bool Active;
        public static float2 TargetWorld; // position monde (xz) de l'ennemi a viser
    }

    // Point d'impact "souris" du joueur. En mode souris, le mortier (cercle ET tir, client
    // + serveur via l'input replique) vise ce point -> on l'ecrase sur l'ennemi. Postfix
    // sur la methode privee qui calcule ce point dans SendClientInputSystem.
    [HarmonyPatch(typeof(SendClientInputSystem), "CalculateMouseOrJoystickWorldPoint")]
    internal static class MortarMousePointPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref float2 __result)
        {
            if (MortarFocus.Active) __result = MortarFocus.TargetWorld;
        }
    }

    // Bascule "mode souris" : sans ce flag, le calcul de cible du mortier prend la branche
    // manette (offset accumule, integre dans le temps -> rampe, pas reactif) et ignore notre
    // point. On ne le force QUE pendant le focus (en combat). VALIDE en jeu : hors menu, les
    // seuls autres lecteurs sont le rendu du ghost (qu'on veut en souris) et le curseur souris,
    // qui ne s'est PAS manifeste en combat -> aucun effet de bord observe. La quasi-totalite
    // des autres lecteurs du flag sont des ecrans de menu, inactifs quand on tire au mortier.
    [HarmonyPatch(typeof(InputManager), nameof(InputManager.SystemPrefersKeyboardAndMouse))]
    internal static class MortarPrefersMousePatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result)
        {
            if (MortarFocus.Active) __result = true;
        }
    }
}
