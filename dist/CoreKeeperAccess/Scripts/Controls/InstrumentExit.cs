using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using UnityEngine;

namespace CoreKeeperAccess.Controls
{
    // Sortie accessible du mode instrument de musique (retour testeur 29 juillet 2026 :
    // joueur invite TOTALEMENT bloque en pleine lecture, ni Start manette ni Echap clavier
    // ne faisaient quoi que ce soit).
    //
    // CAUSE VANILLA (confirmee au decompile, MenuManager) : le jeu s'INTERDIT lui-meme de
    // mettre en pause pendant qu'on joue d'un instrument - l'etat instrument figure dans la
    // liste des blocages de pause, au meme titre que l'inventaire ou la carte ouverts. Start
    // et Echap sont donc ignores PAR DESIGN ; la seule sortie native est le bouton Fermer de
    // l'interface (CloseButton), inaccessible sans voir l'ecran.
    //
    // Comme le jeu ignore ces deux touches dans cet etat, on ne lui vole RIEN en les lisant :
    // on reproduit juste ce que fait son bouton Fermer (poser stopPlayingInstrument, le jeu
    // gere la sortie propre la frame suivante via SendClientInputSystem).
    //
    // Triangle+Rond (saut barre 1/slot 1, cf. ComboBindings) reste une echappatoire valable
    // et fait deja la meme chose ; ici on ajoute le geste NATUREL, plus l'annonce d'entree
    // dans le mode pour que la sortie soit decouvrable sans documentation.
    internal static class InstrumentExit
    {
        private const int StartButton = 13; // Start / Options (id physique template Rewired Gamepad)

        private static bool _wasPlaying;

        public static void Tick()
        {
            bool playing = InputContext.PlayingInstrument;

            // Front montant : on entre en mode instrument -> dire comment en sortir.
            if (playing && !_wasPlaying) TtsText.Say(Strings.L("instrument.entered"), true);
            _wasPlaying = playing;

            if (!playing) return;

            var p = Manager.main != null ? Manager.main.player : null;
            if (p == null) return;
            if (p.stopPlayingInstrument) return; // sortie deja demandee, laisser le jeu finir

            if (!InfoKey.PhysicalButtonDown(StartButton) && !Input.GetKeyDown(KeyCode.Escape)) return;

            p.stopPlayingInstrument = true;
            TtsText.Say(Strings.L("instrument.closed"), true);
        }
    }
}
