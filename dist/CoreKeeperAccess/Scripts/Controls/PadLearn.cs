using System.Collections.Generic;
using CoreKeeperAccess.Gameplay;
using CoreKeeperAccess.Localization;
using DavyKager;
using Rewired;
using UnityEngine;

namespace CoreKeeperAccess.Controls
{
    // Mode DECOUVERTE de la manette : on presse un bouton / bouge un stick, le mod annonce son
    // NOM (nomenclature PS/Xbox reglee, via Glyphs) + sa POSITION physique -> apprendre la
    // manette sans deja connaitre les noms. MODAL (input jeu gele, cf. InputContext.ModalA11yOpen).
    // Force UNE fois a la 1re entree en jeu (flag A11ySettings.ControllerTutorialSeen) ;
    // relancable depuis la 1re entree du menu d'aide. Sortie = double-tap du bouton de droite
    // (Rond / B). Lecture brute Rewired (ids physiques confirmes F9), aucun patch Harmony.
    internal static class PadLearn
    {
        public static bool Active { get; private set; }

        private const int FaceRightId = 7;       // Rond (PS) / B (Xbox) : double-tap pour terminer
        private const int FaceDownId = 6;        // Croix (PS) / A (Xbox) : valider l'ecran final
        private const int BackId = 12;           // Back / Creer / Vue : petit bouton central gauche
        private const int StartId = 13;          // Start / Options / Menu : petit bouton central droit
        private const float DoubleTapWindow = 0.4f;
        private const float AxisThreshold = 0.6f;

        private static float _lastFaceRightTap = -10f;
        private static bool _skipButtons; // saute la frame d'ouverture (le bouton qui a valide)
        private static bool _stickHintGiven; // rappel "on peut cliquer les sticks" donne une fois
        private static bool _finishing;   // ecran final modal : message protege, attend validation
        private static bool _pressedBack;  // le joueur a-t-il trouve le petit bouton central gauche ?
        private static bool _pressedStart; // ... et le central droit ? (sinon filet a l'ecran final)
        private static readonly Dictionary<int, int> _axisState = new Dictionary<int, int>();

        public static void Start()
        {
            if (Active) return;
            Active = true;
            _lastFaceRightTap = -10f;
            _skipButtons = true;   // ignore l'appui qui vient d'ouvrir le mode (sinon il ecrase l'intro)
            _stickHintGiven = false;
            _finishing = false;
            _pressedBack = false;
            _pressedStart = false;
            _axisState.Clear();
            Tolk.Output(Strings.L("learn.intro").Replace("{btn}", Glyphs.Name(Btn.FaceRight)), true);
        }

        public static void Stop()
        {
            if (!Active) return;
            Active = false;
            _finishing = false;
            A11ySettings.SetControllerTutorialSeen(true);
        }

        // Passe a l'ecran FINAL, toujours MODAL (jeu gele) -> le message ne peut PAS etre ecrase
        // par le TTS du jeu qui reprend. On annonce la sortie + le filet des boutons centraux +
        // LE raccourci du menu d'aide, puis on attend la validation (Croix) pour rendre la main.
        // C'est l'onboarding garanti, facon popup a acquitter.
        private static void EnterFinish()
        {
            _finishing = true;
            SayFinishMessage();
        }

        // Compose et annonce le message de fin. Appele a l'entree de l'ecran final ET a chaque
        // demande de repetition (tout bouton sauf Croix), donc idempotent : CenterButtonsHint
        // depend de drapeaux figes une fois en finishing. {dir} = "croix directionnelle haut" /
        // "D-pad haut" selon la nomenclature reglee.
        private static void SayFinishMessage()
        {
            string center = CenterButtonsHint();
            string hint = Strings.L("learn.helphint")
                .Replace("{btn}", Glyphs.Name(Btn.FaceUp))
                .Replace("{dir}", Glyphs.Name(Btn.Up));
            string confirm = Strings.L("learn.confirm").Replace("{btn}", Glyphs.Name(Btn.FaceDown));
            Tolk.Output(Strings.L("learn.outro") + center + " " + hint + " " + confirm, true);
        }

        // Filet : les deux petits boutons centraux (Back/Creer a gauche, Start/Options a droite)
        // sont durs a reperer au toucher et on ne soupconne pas qu'ils servent. Invariant fiable
        // sur toutes les manettes courantes : retour a gauche, start a droite, vers le centre.
        // Si le joueur ne les a pas presses pendant l'exploration, on lui dit ou ils sont a la
        // sortie ; s'il les a trouves, rien (qui connait zappe).
        private static string CenterButtonsHint()
        {
            string back = Glyphs.Name(Btn.Back), start = Glyphs.Name(Btn.Start);
            if (!_pressedBack && !_pressedStart)
                return " " + Strings.L("learn.centerboth").Replace("{back}", back).Replace("{start}", start);
            if (!_pressedBack)
                return " " + Strings.L("learn.centerback").Replace("{back}", back);
            if (!_pressedStart)
                return " " + Strings.L("learn.centerstart").Replace("{start}", start);
            return "";
        }

        public static void Tick()
        {
            if (!Active)
            {
                // Forçage unique a la 1re entree en jeu (apres ce passage, le flag est pose).
                if (!A11ySettings.ControllerTutorialSeen && InputContext.InGameFree) Start();
                return;
            }
            if (!ReInput.isReady) return;
            var joy = ReInput.controllers.GetLastActiveController<Joystick>();
            if (joy == null) return;

            // Ecran final (modal, jeu gele) : Croix valide et rend la main ; TOUT autre bouton
            // REPETE l'annonce -> si NVDA l'a ecrasee (annonce exterieure au jeu), le joueur
            // coince appuie n'importe ou et la reentend, au lieu de rester bloque sans savoir.
            if (_finishing)
            {
                for (int i = 0; i < joy.buttonCount; i++)
                {
                    if (!joy.GetButtonDown(i)) continue;
                    if (joy.ButtonElementIdentifiers[i].id == FaceDownId) { Stop(); return; }
                    SayFinishMessage();
                    return;
                }
                return;
            }

            // Boutons (front montant). Le bouton de droite (Rond) en double-tap = sortie.
            // On saute la frame d'ouverture : le bouton (Croix) qui a valide l'entree du menu
            // d'aide serait sinon relu et ecraserait l'intro a peine annoncee.
            if (_skipButtons)
            {
                _skipButtons = false;
            }
            else
            {
                for (int i = 0; i < joy.buttonCount; i++)
                {
                    if (!joy.GetButtonDown(i)) continue;
                    int id = joy.ButtonElementIdentifiers[i].id;
                    if (id == FaceRightId)
                    {
                        if (Time.unscaledTime - _lastFaceRightTap <= DoubleTapWindow) { EnterFinish(); return; }
                        _lastFaceRightTap = Time.unscaledTime;
                    }
                    if (id == BackId) _pressedBack = true;
                    else if (id == StartId) _pressedStart = true;
                    Say(ButtonName(id));
                }
            }

            // Axes (sticks / gachettes) : annonce une fois au franchissement d'un seuil.
            for (int i = 0; i < joy.axisCount; i++)
            {
                float v = joy.GetAxis(i);
                int now = v > AxisThreshold ? 1 : (v < -AxisThreshold ? -1 : 0);
                int prev; _axisState.TryGetValue(i, out prev);
                if (now != 0 && now != prev)
                {
                    int aid = joy.AxisElementIdentifiers[i].id;
                    string msg = AxisName(aid, now > 0);
                    // Rappel UNE fois (a la 1re manipulation d'un stick, ids 0-3) : les sticks
                    // se cliquent aussi (L3/R3).
                    if (!_stickHintGiven && aid >= 0 && aid <= 3) { msg += " " + Strings.L("learn.stickclick"); _stickHintGiven = true; }
                    Say(msg);
                }
                _axisState[i] = now;
            }
        }

        private static void Say(string text) => Tolk.Output(text, true);

        // Nom regle PS/Xbox SEUL (ex. "Croix", "R1") : la manette est dans les mains du joueur,
        // pas besoin de decrire la position physique. Qui connait le bouton zappe, qui l'ignore
        // l'explore (c'est tout l'interet de ce mode). Pour les directions du D-pad, Glyphs.Name
        // compose deja "croix directionnelle haut".
        private static string ButtonName(int id)
        {
            Btn? b = IdToBtn(id);
            if (b == null) return Strings.L("learn.unknown");
            return Glyphs.Name(b.Value);
        }

        private static string AxisName(int id, bool positive)
        {
            switch (id)
            {
                case 0: return Glyphs.Name(Btn.StickLeft) + " " + Strings.L(positive ? "learn.toright" : "learn.toleft");
                case 1: return Glyphs.Name(Btn.StickLeft) + " " + Strings.L(positive ? "learn.toup" : "learn.todown");
                case 2: return Glyphs.Name(Btn.StickRight) + " " + Strings.L(positive ? "learn.toright" : "learn.toleft");
                case 3: return Glyphs.Name(Btn.StickRight) + " " + Strings.L(positive ? "learn.toup" : "learn.todown");
                case 4: return Glyphs.Name(Btn.L2);
                case 5: return Glyphs.Name(Btn.R2);
                default: return Strings.L("learn.unknown");
            }
        }

        private static Btn? IdToBtn(int id)
        {
            switch (id)
            {
                case 6: return Btn.FaceDown;
                case 7: return Btn.FaceRight;
                case 8: return Btn.FaceLeft;
                case 9: return Btn.FaceUp;
                case 10: return Btn.L1;
                case 11: return Btn.R1;
                case 12: return Btn.Back;
                case 13: return Btn.Start;
                case 14: return Btn.L3;
                case 15: return Btn.R3;
                case 16: return Btn.Up;
                case 17: return Btn.Right;
                case 18: return Btn.Down;
                case 19: return Btn.Left;
                default: return null;
            }
        }
    }
}
