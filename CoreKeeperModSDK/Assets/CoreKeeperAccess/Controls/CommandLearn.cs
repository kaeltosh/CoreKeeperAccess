using System.Collections.Generic;
using CoreKeeperAccess.Localization;
using DavyKager;
using Rewired;
using UnityEngine;

namespace CoreKeeperAccess.Controls
{
    // Mode COMMANDES : on presse un bouton ou un combo touche access / scanner, le mod annonce
    // ce qu'il fait PAR DEFAUT, sans l'executer (facon "input help" NVDA). Complement de PadLearn
    // (qui nomme le bouton, pas l'action). Table STATIQUE : pas de lecture live du contexte ni du
    // mapping Rewired reel, juste un texte fixe par declencheur, en tenant compte des entrees que
    // le mod a confisquees au jeu (ne jamais decrire l'ancien sens vanilla d'un bouton vole).
    // MODAL (input jeu gele, cf. InputContext.ModalA11yOpen), meme famille que PadLearn/SoundGuide.
    internal static class CommandLearn
    {
        public static bool Active { get; private set; }

        private const int FaceRightId = 7; // Rond (PS) / B (Xbox) : double-tap pour sortir
        private const float DoubleTapWindow = 0.4f;
        private const float AxisThreshold = 0.6f;

        private static float _lastFaceRightTap = -10f;
        private static bool _skipButtons; // saute la frame d'ouverture (le bouton qui a valide)
        private static bool _wasScannerHeld; // pour l'annonce unique a la prise de R3
        private static bool _statsWheelAnnounced; // pour l'annonce unique par poussee de stick
        private static readonly Dictionary<int, int> _axisState = new Dictionary<int, int>();

        public static void Start()
        {
            if (Active) return;
            Active = true;
            _lastFaceRightTap = -10f;
            _skipButtons = true;
            _wasScannerHeld = false;
            _statsWheelAnnounced = false;
            _axisState.Clear();
            Tolk.Output(Strings.L("cmdlearn.intro").Replace("{btn}", Glyphs.Name(Btn.FaceRight)), true);
        }

        public static void Stop()
        {
            if (!Active) return;
            Active = false;
            Tolk.Output(Strings.L("cmdlearn.outro"), true);
        }

        public static void Tick()
        {
            if (!Active) return;
            if (!ReInput.isReady) return;
            var joy = ReInput.controllers.GetLastActiveController<Joystick>();
            if (joy == null) return;

            // Touche access tenue : combos Triangle+X, table figee, memes flags que le
            // dispatcher reel mais SANS lancer l'action. Un combo non reconnu ici reste
            // muet (meme regle que le dispatcher : "un combo qui n'existe pas ne dit rien").
            if (InfoKey.ModifierHeld)
            {
                if (InfoKey.DetailRequested) { Say("cmdlearn.detail"); return; }
                if (InfoKey.ComboRight) { Say("cmdlearn.right"); return; }
                if (InfoKey.ComboDown) { Say("cmdlearn.down"); return; }
                if (InfoKey.ComboLeft) { Say("cmdlearn.left"); return; }
                if (InfoKey.ComboLB) { Say("cmdlearn.bumperl"); return; }
                if (InfoKey.ComboR1) { Say("cmdlearn.bumperr"); return; }
                if (InfoKey.ComboL3) { Say("cmdlearn.leftstick"); return; }
                if (InfoKey.ComboBack) { Say("cmdlearn.back"); return; }
                if (InfoKey.ComboO) { Say("cmdlearn.circle"); return; }
                if (InfoKey.RecallRequested) { Say("cmdlearn.recall"); return; }
                CheckStatsWheelAxis(joy); // Triangle + stick gauche : mecanique a part, hors ComboDispatcher
                return;
            }
            _statsWheelAnnounced = false;

            // Triangle vient d'etre relache apres un tap bref : double-tap = carte.
            if (InfoKey.DoubleTapped) { Say("cmdlearn.doubletap"); return; }

            // R3 tenu (Triangle relache) : second modificateur, scanner de proximite.
            if (ScannerModifier.Held)
            {
                if (!_wasScannerHeld) { Say("cmdlearn.bare.r3"); _wasScannerHeld = true; }
                if (ScannerModifier.DpadUpPressed) { Say("cmdlearn.scanup"); return; }
                if (ScannerModifier.DpadDownPressed) { Say("cmdlearn.scandown"); return; }
                if (ScannerModifier.DpadRightPressed) { Say("cmdlearn.scanright"); return; }
                if (ScannerModifier.DpadLeftPressed) { Say("cmdlearn.scanleft"); return; }
                if (ScannerModifier.ToggleBeacon) { Say("cmdlearn.scanbeacon"); return; }
                return;
            }
            _wasScannerHeld = false;

            // Aucun modificateur tenu : boutons et axes "nus", role par defaut du mod ou du
            // jeu (jamais l'ancien sens d'un bouton confisque, cf. commentaire de classe).
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
                        if (Time.unscaledTime - _lastFaceRightTap <= DoubleTapWindow) { Stop(); return; }
                        _lastFaceRightTap = Time.unscaledTime;
                    }
                    Say(DescribeBareButton(id));
                    return;
                }
            }

            for (int i = 0; i < joy.axisCount; i++)
            {
                float v = joy.GetAxis(i);
                int now = v > AxisThreshold ? 1 : (v < -AxisThreshold ? -1 : 0);
                int prev; _axisState.TryGetValue(i, out prev);
                if (now != 0 && now != prev) Say(DescribeBareAxis(joy.AxisElementIdentifiers[i].id));
                _axisState[i] = now;
            }
        }

        private static void Say(string key) => Tolk.Output(Strings.L(key), true);

        private static string DescribeBareButton(int id)
        {
            switch (id)
            {
                case 6: return Strings.L("cmdlearn.bare.facedown");
                case 7: return Strings.L("cmdlearn.bare.faceright");
                case 8: return Strings.L("cmdlearn.bare.faceleft");
                case 10: return Strings.L("cmdlearn.bare.l1");
                case 11: return Strings.L("cmdlearn.bare.r1");
                case 13: return Strings.L("cmdlearn.bare.start");
                case 14: return Strings.L("cmdlearn.bare.l3");
                case 16: case 17: case 18: case 19: return Strings.L("cmdlearn.bare.dpad");
                default:
                    Btn? b = Glyphs.FromPhysicalId(id);
                    string name = b.HasValue ? Glyphs.Name(b.Value) : Strings.L("learn.unknown");
                    return Strings.L("cmdlearn.unbound").Replace("{btn}", name);
            }
        }

        private static string DescribeBareAxis(int aid)
        {
            switch (aid)
            {
                case 0: case 1: return Strings.L("cmdlearn.bare.stickleft");
                case 2: case 3: return Strings.L("cmdlearn.bare.stickright");
                case 4: return Strings.L("cmdlearn.bare.l2");
                case 5: return Strings.L("cmdlearn.bare.r2");
                default: return Strings.L("learn.unknown");
            }
        }

        // Roue de stats (Triangle + stick gauche pousse) : mecanique a part, lue en direct
        // par StatsWheel.cs, pas enregistree dans ComboDispatcher -> propre detection de
        // seuil ici, une seule annonce par poussee (reset au relachement du stick).
        private static void CheckStatsWheelAxis(Joystick joy)
        {
            float x = joy.GetAxis(0), y = joy.GetAxis(1);
            bool engaged = (x * x + y * y) >= AxisThreshold * AxisThreshold;
            if (engaged && !_statsWheelAnnounced) { Say("cmdlearn.statswheel"); _statsWheelAnnounced = true; }
            if (!engaged) _statsWheelAnnounced = false;
        }
    }
}
