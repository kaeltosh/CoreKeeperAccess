using CoreKeeperAccess.Gameplay;
using CoreKeeperAccess.Localization;
using DavyKager;
using Rewired;

namespace CoreKeeperAccess.Controls
{
    // Message d'accueil UNE fois a la 1re entree en jeu : indique juste COMMENT rouvrir le
    // menu d'aide du mod (qui propose lui-meme "Apprendre les boutons"/PadLearn en tete).
    // Remplace l'ancien forçage direct du mode decouverte manette (PadLearn), qui ne
    // remplissait pas sa fonction (signale par l'utilisateur, 8 juillet 2026) : PadLearn
    // reste disponible tel quel, juste plus jamais lance de force. MODAL, popup a acquitter,
    // meme mecanique que l'ecran final de PadLearn : Croix ferme, tout autre bouton repete
    // (utile si NVDA a ecrase l'annonce par une notification exterieure).
    internal static class OnboardingHint
    {
        public static bool Active { get; private set; }

        private const int FaceDownId = 6; // Croix (PS) / A (Xbox) : ferme le message
        private static bool _skipButtons; // saute la frame d'ouverture (le bouton qui a valide)

        public static void Start()
        {
            if (Active) return;
            Active = true;
            _skipButtons = true;
            Say();
        }

        public static void Stop()
        {
            if (!Active) return;
            Active = false;
            A11ySettings.SetControllerTutorialSeen(true);
        }

        // {btn}/{dir} resolus comme dans PadLearn.SayFinishMessage : meme geste, meme phrase.
        private static void Say()
        {
            string hint = Strings.L("learn.helphint")
                .Replace("{btn}", Glyphs.Name(Btn.FaceUp))
                .Replace("{dir}", Glyphs.Name(Btn.Up));
            string confirm = Strings.L("learn.confirm").Replace("{btn}", Glyphs.Name(Btn.FaceDown));
            Tolk.Output(Strings.L("onboarding.intro") + " " + hint + " " + confirm, true);
        }

        public static void Tick()
        {
            if (!Active)
            {
                // Forçage unique a la 1re entree en jeu (apres ce passage, le flag est pose) :
                // meme condition que l'ancien forçage de PadLearn, juste un message court a
                // la place d'un tutoriel complet impose.
                if (!A11ySettings.ControllerTutorialSeen && InputContext.InGameFree) Start();
                return;
            }
            if (!ReInput.isReady) return;
            var joy = ReInput.controllers.GetLastActiveController<Joystick>();
            if (joy == null) return;

            if (_skipButtons) { _skipButtons = false; return; }

            for (int i = 0; i < joy.buttonCount; i++)
            {
                if (!joy.GetButtonDown(i)) continue;
                if (joy.ButtonElementIdentifiers[i].id == FaceDownId) { Stop(); return; }
                Say();
                return;
            }
        }
    }
}
