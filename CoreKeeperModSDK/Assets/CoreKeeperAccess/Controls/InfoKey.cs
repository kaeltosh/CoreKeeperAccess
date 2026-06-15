using Rewired;
using UnityEngine;

namespace CoreKeeperAccess.Controls
{
    // Dispatcher de la "touche access" du mod. Triangle (libere par TriangleModifier) sert de
    // MODIFICATEUR : tant qu'il est tenu, le D-pad ne navigue plus mais declenche des commandes
    // a11y. Premier combo : Triangle + D-pad haut = "plus de details" sur l'element focalise,
    // pris en charge par le contexte actif (ici le curseur de tuile). Tick() doit tourner AVANT
    // les consommateurs du D-pad (ils consultent ModifierHeld / DetailRequested).
    internal static class InfoKey
    {
        private const int DpadUp = 16, DpadRight = 17, DpadDown = 18, DpadLeft = 19;
        private const int BumperLeft = 10;     // LB / L1 (id physique template Rewired Gamepad)
        private const int BumperRight = 11;    // RB / R1 (id physique template Rewired Gamepad)
        private const int LeftStickClick = 14; // L3 (id physique template Rewired Gamepad)
        private const int BackButton = 12;     // Back / Select (id physique template Rewired Gamepad)

        public static bool ModifierHeld;    // Triangle physiquement tenu
        public static bool DetailRequested; // combo Triangle + haut declenche cette frame
        public static bool ComboRight;      // Triangle + droite (reparer, selon contexte)
        public static bool ComboDown;       // Triangle + bas (transferer)
        public static bool ComboLeft;       // Triangle + gauche (tout recycler)
        public static bool ComboLB;         // Triangle + L1 (ping sonar)
        public static bool ComboR1;         // Triangle + R1 (pivoter / changer taille de zone)
        public static bool ComboL3;         // Triangle + L3 (toggle direction assistee)
        public static bool ComboBack;       // Triangle + Back (ouvrir le panneau de reglages)
        public static bool DoubleTapped;    // double-tap bref de Triangle (ouvrir la carte)

        // Double-tap : deux TAPS courts (< TapMaxDuration, sans combo D-pad pendant la
        // tenue) espaces de moins de DoubleTapWindow. Un appui long ou un combo n'est
        // jamais un tap -> aucun conflit avec le role de modificateur.
        private const float TapMaxDuration = 0.30f;
        private const float DoubleTapWindow = 0.40f;
        private static bool _wasHeld;
        private static float _holdStart;
        private static bool _comboDuringHold;
        private static float _lastTap = -10f;

        public static void Tick()
        {
            ModifierHeld = false;
            DetailRequested = false;
            ComboRight = ComboDown = ComboLeft = ComboLB = ComboR1 = ComboL3 = ComboBack = false;
            DoubleTapped = false;
            if (!ReInput.isReady) return;
            int tri = TriangleModifier.TriangleButtonId;
            if (tri < 0) return; // id Triangle pas encore capte
            var joy = ReInput.controllers.GetLastActiveController<Joystick>();
            if (joy == null) return;

            ModifierHeld = GetButtonById(joy, tri);
            if (ModifierHeld)
            {
                if (GetButtonDownById(joy, DpadUp)) DetailRequested = true;
                else if (GetButtonDownById(joy, DpadRight)) ComboRight = true;
                else if (GetButtonDownById(joy, DpadDown)) ComboDown = true;
                else if (GetButtonDownById(joy, DpadLeft)) ComboLeft = true;
                else if (GetButtonDownById(joy, BumperLeft)) ComboLB = true;
                else if (GetButtonDownById(joy, BumperRight)) ComboR1 = true;
                else if (GetButtonDownById(joy, LeftStickClick)) ComboL3 = true;
                else if (GetButtonDownById(joy, BackButton)) ComboBack = true;
            }

            // Suivi tap / double-tap (fronts montant et descendant de Triangle).
            if (ModifierHeld && !_wasHeld) { _holdStart = Time.unscaledTime; _comboDuringHold = false; }
            if (ModifierHeld && (DetailRequested || ComboRight || ComboDown || ComboLeft || ComboLB || ComboR1 || ComboL3 || ComboBack))
                _comboDuringHold = true;
            if (!ModifierHeld && _wasHeld)
            {
                bool tap = Time.unscaledTime - _holdStart <= TapMaxDuration && !_comboDuringHold;
                if (tap)
                {
                    if (Time.unscaledTime - _lastTap <= DoubleTapWindow)
                    {
                        DoubleTapped = true;
                        _lastTap = -10f;
                    }
                    else
                    {
                        _lastTap = Time.unscaledTime;
                    }
                }
            }
            _wasHeld = ModifierHeld;
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
