using CoreKeeperAccess.Gameplay;
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
        private const int CircleButton = 7;    // Rond / B (id physique template Rewired Gamepad)

        public static bool ModifierHeld;    // Triangle physiquement tenu
        public static bool DetailRequested; // combo Triangle + haut declenche cette frame
        public static bool ComboRight;      // Triangle + droite (reparer, selon contexte)
        public static bool ComboDown;       // Triangle + bas (transferer)
        public static bool ComboLeft;       // Triangle + gauche (tout recycler)
        public static bool ComboLB;         // Triangle + L1 (barre rapide precedente)
        public static bool ComboR1;         // Triangle + R1 (barre rapide suivante)
        public static bool ComboL3;         // Triangle + L3 (toggle direction assistee)
        public static bool ComboBack;       // Triangle + Back (ouvrir le panneau de reglages)
        public static bool ComboO;          // Triangle + Rond (saut direct barre 1 / slot 1)
        public static bool DoubleTapped;    // double-tap bref de Triangle (ouvrir la carte)
        public static bool RecallRequested; // double-tap D-pad haut sous Triangle (menu d'aide)

        // Roue de saut barre rapide (miroir R1/L1), UNIQUEMENT quand Triangle est relache
        // (sinon R1/L1 restent Triangle+R1=barre suivante / Triangle+L1=barre precedente)
        // et le reglage active. R1 = stick gauche pilote (marche gelee) ; L1 = stick droit
        // pilote (canne laser coupee le temps de la tenue). Cf. HotbarJumpWheel.
        // Vrai seulement une fois le seuil de latence (A11ySettings.HotbarWheelHoldMs)
        // atteint (0 = seuil nul, actif des l'appui comme avant). Sous ce seuil, on ne
        // touche a RIEN : le pas-a-pas natif (NEXT_SLOT/PREVIOUS_SLOT) part normalement des
        // l'appui, sans latence ajoutee (cf. UpdateHotbarBumper) - attendre que le delai
        // s'ecoule suffit, pas besoin d'intercepter ou de rejouer quoi que ce soit.
        public static bool HotbarWheelRight;
        public static bool HotbarWheelLeft;
        private static bool _r1Down, _l1Down;
        private static float _r1HoldStart, _l1HoldStart;

        // Pose par la roue de stats (stick droit pousse pendant la tenue de Triangle) :
        // empeche le relachement d'etre lu comme un tap -> pas de double-tap parasite quand
        // on consulte une stat. Reset au front montant de la tenue.
        public static bool StickEngagedDuringHold;

        // Double-tap : deux TAPS courts (< TapMaxDuration, sans combo D-pad pendant la
        // tenue) espaces de moins de DoubleTapWindow. Un appui long ou un combo n'est
        // jamais un tap -> aucun conflit avec le role de modificateur.
        private const float TapMaxDuration = 0.30f;
        private const float DoubleTapWindow = 0.40f;
        private static bool _wasHeld;
        private static float _holdStart;
        private static bool _comboDuringHold;
        private static float _lastTap = -10f;
        private static float _lastDpadUpTap = -10f; // pour le double-tap D-pad haut (menu d'aide)

        public static void Tick()
        {
            ModifierHeld = false;
            DetailRequested = false;
            ComboRight = ComboDown = ComboLeft = ComboLB = ComboR1 = ComboL3 = ComboBack = ComboO = false;
            DoubleTapped = false;
            RecallRequested = false;
            HotbarWheelRight = false;
            HotbarWheelLeft = false;
            if (!ReInput.isReady) return;
            int tri = TriangleModifier.TriangleButtonId;
            if (tri < 0) return; // id Triangle pas encore capte
            var joy = ReInput.controllers.GetLastActiveController<Joystick>();
            if (joy == null) return;

            ModifierHeld = GetButtonById(joy, tri);
            if (ModifierHeld)
            {
                if (InputContext.PlayingInstrument)
                {
                    // Instrument en cours de lecture : seul Triangle+Rond reste une commande
                    // (echappatoire pour fermer/sauter de slot). Tous les autres boutons sont
                    // rendus au jeu tels quels (notes/accords natifs), on ne les lit meme pas
                    // ici pour eviter tout parasitage.
                    if (GetButtonDownById(joy, CircleButton)) ComboO = true;
                }
                else if (GetButtonDownById(joy, DpadUp))
                {
                    // Double-tap du D-pad haut = menu d'aide. Le 1er tap reste "details"
                    // (commande frequente -> aucune latence) ; un 2e tap rapide leve plutot
                    // RecallRequested (qui interrompt l'annonce details a peine commencee).
                    if (Time.unscaledTime - _lastDpadUpTap <= DoubleTapWindow)
                    {
                        RecallRequested = true;
                        _lastDpadUpTap = -10f;
                    }
                    else
                    {
                        DetailRequested = true;
                        _lastDpadUpTap = Time.unscaledTime;
                    }
                }
                else if (GetButtonDownById(joy, DpadRight)) ComboRight = true;
                else if (GetButtonDownById(joy, DpadDown)) ComboDown = true;
                else if (GetButtonDownById(joy, DpadLeft)) ComboLeft = true;
                else if (GetButtonDownById(joy, BumperLeft)) ComboLB = true;
                else if (GetButtonDownById(joy, BumperRight)) ComboR1 = true;
                else if (GetButtonDownById(joy, LeftStickClick)) ComboL3 = true;
                else if (GetButtonDownById(joy, BackButton)) ComboBack = true;
                else if (GetButtonDownById(joy, CircleButton)) ComboO = true;
                // Triangle tient la main sur R1/L1 (pivoter / sonar) : oublie tout appui
                // R1/L1 en cours de suivi pour la roue de saut.
                _r1Down = false; _l1Down = false;
            }
            else if (A11ySettings.HotbarWheelEnabled)
            {
                // Roue de saut barre rapide, Triangle relache : R1 = stick gauche pilote,
                // L1 = stick droit pilote (mode miroir, cf. HotbarJumpWheel). Latence de
                // declenchement : sous le seuil regle, on ne touche a rien (pas-a-pas natif
                // libre) ; la roue s'active d'elle-meme une fois le delai ecoule, sans avoir
                // besoin d'intercepter le relachement.
                HotbarWheelRight = UpdateHotbarBumper(joy, BumperRight, ref _r1Down, ref _r1HoldStart);
                HotbarWheelLeft = UpdateHotbarBumper(joy, BumperLeft, ref _l1Down, ref _l1HoldStart);
            }
            else
            {
                _r1Down = false; _l1Down = false;
            }

            // Suivi tap / double-tap (fronts montant et descendant de Triangle).
            if (ModifierHeld && !_wasHeld) { _holdStart = Time.unscaledTime; _comboDuringHold = false; StickEngagedDuringHold = false; _lastDpadUpTap = -10f; }
            if (ModifierHeld && (DetailRequested || ComboRight || ComboDown || ComboLeft || ComboLB || ComboR1 || ComboL3 || ComboBack || ComboO || RecallRequested))
                _comboDuringHold = true;
            // Stick droit pousse pendant la tenue (roue de stats) : ce n'est pas un tap.
            if (StickEngagedDuringHold) _comboDuringHold = true;
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

        // Lecture brute d'un front montant de bouton PHYSIQUE, hors de tout modificateur :
        // pour les consommateurs qui ont besoin d'un bouton que le jeu ignore de toute facon
        // dans leur contexte (ex. Start pendant l'instrument, cf. InstrumentExit).
        internal static bool PhysicalButtonDown(int id)
        {
            if (!ReInput.isReady) return false;
            var joy = ReInput.controllers.GetLastActiveController<Joystick>();
            return joy != null && GetButtonDownById(joy, id);
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

        // Suivi de tenue d'un bumper (R1 ou L1) pour la roue de saut barre rapide : chrono
        // depuis le front montant, actif (retour true) une fois A11ySettings.HotbarWheelHoldMs
        // ecoule. En dessous du seuil on ne fait RIEN d'autre - aucun blocage, aucune
        // relecture - le pas-a-pas natif (NEXT_SLOT/PREVIOUS_SLOT) part de lui-meme des
        // l'appui comme avant, sans latence ajoutee sur un tap bref.
        private static bool UpdateHotbarBumper(Joystick joy, int buttonId, ref bool held, ref float holdStart)
        {
            bool down = GetButtonById(joy, buttonId);
            if (down && !held) holdStart = Time.unscaledTime;
            held = down;
            return down && (Time.unscaledTime - holdStart) * 1000f >= A11ySettings.HotbarWheelHoldMs;
        }
    }
}
