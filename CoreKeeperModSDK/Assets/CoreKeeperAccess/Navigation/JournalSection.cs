using System.Collections.Generic;
using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;

namespace CoreKeeperAccess.Navigation
{
    // Section JOURNAL : dialogues archives (Coeur + tutoriels). Contrairement aux autres (listes
    // de marqueurs), c'est du TEXTE -> nav en MENU CONTEXTUEL en CASCADE (demande utilisateur,
    // pas un arbre deplie en place) : niveau 0 = liste des conversations (titres + nombre de
    // repliques), D-pad droite / Croix = ouvrir -> niveau 1 = repliques, D-pad gauche = revenir.
    // Un seul niveau s'entend a la fois. Le contenu vient de DialogueLog.
    internal sealed class JournalSection : MapSection
    {
        private const int DpadRight = 17, DpadLeft = 19;

        private readonly List<DialogueGroup> _groups = new List<DialogueGroup>();
        private int _jLevel;  // 0 = liste des conversations, 1 = repliques de la conversation ouverte
        private int _jGroup;  // conversation courante (niveau 0)
        private int _jItem;   // replique courante (niveau 1)

        public override string TitleKey => "map.journal";
        public override string EmptyKey => "map.journal.none";
        public override bool IsEmpty => _groups.Count == 0;

        public override void Rebuild(List<MapMarkerUIElement> scanned)
        {
            _groups.Clear();
            _groups.AddRange(DialogueLog.BuildGroups());
        }

        public override void Clear() => _groups.Clear();

        // Entree dans la section : on se place au niveau 0 (liste des conversations).
        public override void Enter()
        {
            _jLevel = 0; _jGroup = 0; _jItem = 0;
            if (_groups.Count > 0) AnnounceGroup(interrupt: false);
        }

        // D-pad haut/bas : parcourt le niveau courant (conversations au niveau 0, repliques de la
        // conversation ouverte au niveau 1), avec cyclage + son de butee.
        public override void Move(int step)
        {
            if (_groups.Count == 0) return;
            if (_jLevel == 0)
            {
                int n = _groups.Count;
                int raw = _jGroup + step;
                bool wrapped = raw < 0 || raw >= n;
                _jGroup = (raw + n) % n;
                if (wrapped) UiSfx.Cycle(); else UiSfx.Entry();
                AnnounceGroup(interrupt: true);
            }
            else
            {
                var g = _groups[_jGroup];
                if (g.lines.Count == 0) return;
                int n = g.lines.Count;
                int raw = _jItem + step;
                bool wrapped = raw < 0 || raw >= n;
                _jItem = (raw + n) % n;
                if (wrapped) UiSfx.Cycle(); else UiSfx.Entry();
                AnnounceItem(interrupt: true);
            }
        }

        // Croix = ouvrir la conversation focalisee (ou relire la replique au niveau 1).
        public override void Confirm() => JournalOpen();

        // Triangle + haut = relire le focus courant.
        public override void Detail()
        {
            if (_jLevel == 1) AnnounceItem(true); else AnnounceGroup(true);
        }

        // D-pad droite = ouvrir la conversation ; gauche = revenir. Capte SEULEMENT sur cet
        // onglet -> les autres sections gardent leur D-pad gauche/droite natif.
        public override bool HandleDpadLR(int dpadId)
        {
            if (dpadId == DpadRight) { JournalOpen(); return true; }
            if (dpadId == DpadLeft) { JournalBack(); return true; }
            return false;
        }

        // OUVRE la conversation focalisee (niveau 0 -> 1). Au niveau 1, Croix relit la replique.
        private void JournalOpen()
        {
            if (_groups.Count == 0) return;
            if (_jLevel == 0)
            {
                if (_groups[_jGroup].lines.Count == 0) return;
                _jLevel = 1; _jItem = 0;
                UiSfx.Validate();   // ouvrir une conversation = entrer dans une section
                AnnounceItem(interrupt: true);
            }
            else { UiSfx.Entry(); AnnounceItem(interrupt: true); } // relire
        }

        // REVIENT a la liste des conversations (niveau 1 -> 0). Butee au niveau 0.
        private void JournalBack()
        {
            if (_jLevel == 1) { _jLevel = 0; UiSfx.Entry(); AnnounceGroup(interrupt: true); }
        }

        // Niveau 0 : "n sur m, titre de la conversation, K repliques".
        private void AnnounceGroup(bool interrupt)
        {
            if (_jGroup < 0 || _jGroup >= _groups.Count) return;
            var g = _groups[_jGroup];
            string pos = (_jGroup + 1) + " " + Strings.L("journal.of") + " " + _groups.Count;
            TtsText.Say(pos + ", " + g.label + ", " + g.lines.Count + " " + Strings.L("journal.replies"), interrupt);
        }

        // Niveau 1 : "n sur m, texte de la replique".
        private void AnnounceItem(bool interrupt)
        {
            if (_jGroup < 0 || _jGroup >= _groups.Count) return;
            var g = _groups[_jGroup];
            if (_jItem < 0 || _jItem >= g.lines.Count) return;
            string pos = (_jItem + 1) + " " + Strings.L("journal.of") + " " + g.lines.Count;
            TtsText.Say(pos + ", " + g.lines[_jItem], interrupt);
        }
    }
}
