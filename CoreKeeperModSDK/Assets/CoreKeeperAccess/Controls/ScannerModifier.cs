using Rewired;

namespace CoreKeeperAccess.Controls
{
    // Second modificateur a11y du mod (a cote de Triangle) : R3 (id physique 15) tenu reserve
    // le D-pad au scanner de proximite (cf. Gameplay/ProximityScanner.cs). Contrairement a
    // Triangle, aucune action native n'est retiree de la manette (pas de DeleteElementMap) :
    // R3 seul sert a courir (MOVE_FASTER), suppression au vol via StolenInputTypes pendant la
    // tenue (cf. NativeInputSuppressionPatch), pas de binding a liberer en dur.
    //
    // Triangle PRIME : si Triangle est deja tenu, R3 ne fait rien (evite un double sens sur
    // les memes boutons D-pad/L3 - Triangle garde tous ses combos existants inchanges).
    internal static class ScannerModifier
    {
        private const int R3ButtonId = 15;
        private const int L3ButtonId = 14;
        private const int DpadUp = 16, DpadRight = 17, DpadDown = 18, DpadLeft = 19;

        public static bool Held;            // R3 physiquement tenu (et Triangle relache)
        public static bool DpadUpPressed;   // categorie suivante
        public static bool DpadDownPressed; // categorie precedente
        public static bool DpadLeftPressed; // objet precedent
        public static bool DpadRightPressed;// objet suivant
        public static bool ToggleBeacon;    // R3 + L3 : bascule le beacon continu

        public static void Tick()
        {
            Held = false;
            DpadUpPressed = DpadDownPressed = DpadLeftPressed = DpadRightPressed = false;
            ToggleBeacon = false;
            if (!ReInput.isReady) return;
            var joy = ReInput.controllers.GetLastActiveController<Joystick>();
            if (joy == null) return;

            bool r3 = GetButtonById(joy, R3ButtonId);
            Held = r3 && !InfoKey.ModifierHeld;
            if (!Held) return;

            DpadUpPressed = GetButtonDownById(joy, DpadUp);
            DpadDownPressed = GetButtonDownById(joy, DpadDown);
            DpadLeftPressed = GetButtonDownById(joy, DpadLeft);
            DpadRightPressed = GetButtonDownById(joy, DpadRight);
            ToggleBeacon = GetButtonDownById(joy, L3ButtonId);
        }

        private static bool GetButtonById(Joystick joy, int id)
        {
            for (int i = 0; i < joy.buttonCount; i++)
                if (joy.ButtonElementIdentifiers[i].id == id) return joy.GetButton(i);
            return false;
        }

        private static bool GetButtonDownById(Joystick joy, int id)
        {
            for (int i = 0; i < joy.buttonCount; i++)
                if (joy.ButtonElementIdentifiers[i].id == id) return joy.GetButtonDown(i);
            return false;
        }
    }
}
