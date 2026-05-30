using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using Rewired;
using UnityEngine;

namespace CoreKeeperAccess.Navigation
{
    // Roue d'actions au stick gauche (uniquement quand l'inventaire est ouvert, donc
    // le stick est libre). On pointe une des 8 directions -> annonce de l'action ;
    // un clic R3 l'execute. Les actions "volees" (tri, empiler, ramasser moitie,
    // pages de barre rapide, poubelle) sont rejouees en armant leur InputType natif ;
    // le transfert passe par un appel direct (geste natif = maintien + A, pas simulable).
    internal static class ActionWheel
    {
        private const float Deadzone = 0.5f;
        private const int LeftStickX = 0, LeftStickY = 1, R3 = 15;

        private static int _lastSector = -1;

        public static void Tick()
        {
            var joy = ReInput.isReady ? ReInput.controllers.GetLastActiveController<Joystick>() : null;
            if (joy == null) { _lastSector = -1; return; }

            int sector = SectorOf(AxisById(joy, LeftStickX), AxisById(joy, LeftStickY));
            if (sector != _lastSector)
            {
                _lastSector = sector;
                if (sector >= 0)
                {
                    var key = LabelKey(sector);
                    if (key != null) TtsText.Say(Strings.L(key), true);
                }
            }

            if (sector >= 0 && ButtonDownById(joy, R3)) Execute(sector);
        }

        private static void Execute(int sector)
        {
            switch (sector)
            {
                case 0: Arm(PlayerInput.InputType.SORT); break;
                case 1: Arm(PlayerInput.InputType.SWAP_NEXT_HOTBAR); break;
                case 2: Arm(PlayerInput.InputType.QUICK_STACK); break;
                case 3: Arm(PlayerInput.InputType.SWAP_PREVIOUS_HOTBAR); break;
                case 4: Arm(PlayerInput.InputType.PICK_UP_HALF); break;
                case 5: Arm(PlayerInput.InputType.TRASH_ITEM); break;
                case 6: Transfer(); break;
                default: return; // NO : libre
            }
            // Confirmation au clic : "Trier, lancé". Distinct du simple survol qui ne
            // dit que "Trier". Donne un retour meme quand l'action n'a pas d'effet sonore.
            var key = LabelKey(sector);
            if (key != null) TtsText.Say(Strings.L(key) + ", " + Strings.L("wheel.done"), true);
        }

        private static string LabelKey(int sector)
        {
            switch (sector)
            {
                case 0: return "wheel.sort";
                case 1: return "wheel.hotbarnext";
                case 2: return "wheel.quickstack";
                case 3: return "wheel.hotbarprev";
                case 4: return "wheel.pickuphalf";
                case 5: return "wheel.trash";
                case 6: return "wheel.transfer";
                default: return null; // NO (7) : libre
            }
        }

        private static void Arm(PlayerInput.InputType input)
        {
            InventoryNavState.ArmedInput = input;
            InventoryNavState.ArmedTtl = 2;
        }

        private static void Transfer()
        {
            var slot = Manager.ui != null ? Manager.ui.currentSelectedUIElement as InventorySlotUI : null;
            if (slot != null) slot.TryToSendItemToOtherInventoryOrEquip();
        }

        // Secteur 0=N,1=NE,2=E,3=SE,4=S,5=SO,6=O,7=NO ; -1 = centre (deadzone).
        private static int SectorOf(float x, float y)
        {
            if (x * x + y * y < Deadzone * Deadzone) return -1;
            float ang = Mathf.Atan2(x, y) * Mathf.Rad2Deg; // 0 = haut (Nord), 90 = droite (Est)
            if (ang < 0f) ang += 360f;
            return ((int)Mathf.Round(ang / 45f)) % 8;
        }

        private static float AxisById(Joystick joy, int id)
        {
            for (int i = 0; i < joy.axisCount; i++)
                if (joy.AxisElementIdentifiers[i].id == id) return joy.GetAxis(i);
            return 0f;
        }

        private static bool ButtonDownById(Joystick joy, int id)
        {
            for (int i = 0; i < joy.buttonCount; i++)
                if (joy.ButtonElementIdentifiers[i].id == id) return joy.GetButtonDown(i);
            return false;
        }
    }
}
