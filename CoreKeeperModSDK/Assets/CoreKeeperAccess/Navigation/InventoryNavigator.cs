using System.Collections.Generic;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using Rewired;
using UnityEngine;

namespace CoreKeeperAccess.Navigation
{
    // Etat partage avec les patches Harmony (neutralisation de l'input natif +
    // etouffement de l'annonce passive quand c'est nous qui pilotons la selection).
    internal static class InventoryNavState
    {
        public static bool SuppressNativeInput;     // vrai tant que notre nav inventaire tient la main
        public static bool SuppressPassiveAnnounce;  // vrai le temps d'une selection forcee
    }

    // Navigation a11y de l'inventaire par sections verrouillees.
    // Bumpers = section precedente / suivante ; D-pad = deplacement en grille,
    // borne a la section. Lecture des boutons en brut via Rewired (ids physiques
    // du template Gamepad), l'action native de ces boutons etant neutralisee par
    // NativeInputSuppressionPatch tant que la nav est active.
    internal static class InventoryNavigator
    {
        // Ids physiques (template Rewired Gamepad, confirmes en jeu).
        private const int DpadUp = 16, DpadRight = 17, DpadDown = 18, DpadLeft = 19;
        private const int Lb = 10, Rb = 11;

        private static bool _active;
        private static List<SlotSection> _sections = new List<SlotSection>();
        private static int _sectionIndex;
        private static UIelement _current;

        // Suivi du contenu du slot courant, pour reannoncer apres une prise/pose.
        private static UIelement _watchedSlot;
        private static ObjectID _watchedId;
        private static int _watchedAmount;

        // Derniere selection "jeu" hors de nos sections, pour ne reconstruire qu'une
        // seule fois quand une nouvelle vue s'ouvre (anti-spam de rebuild).
        private static UIelement _lastSyncTarget;

        public static void Update()
        {
            bool open = Manager.main != null && Manager.main.player != null && Manager.ui != null
                        && (Manager.ui.isAnyInventoryShowing
                            || (Manager.ui.characterWindow != null && Manager.ui.characterWindow.isShowing));

            if (open && !_active) Enter();
            else if (!open && _active) Exit();

            if (!_active) return;

            // Si les emplacements n'etaient pas prets a l'ouverture, on retente.
            if (_sections.Count == 0)
            {
                Rebuild();
                if (_sections.Count == 0) return;
                SelectSection(0, announceSectionName: true);
                return;
            }

            SyncWithGameSelection();
            HandleInput();
            WatchSlotChange();
        }

        // Apres une prise/pose/deplacement, la selection ne bouge pas mais le contenu
        // du slot change : on reannonce alors simplement le nouvel etat du slot
        // (ex. "vide" apres avoir grab, ou "Terre" apres avoir pose).
        private static void WatchSlotChange()
        {
            ObjectID id = ObjectID.None;
            int amount = 0;
            if (_current != null)
            {
                var data = _current.GetContainedObject().objectData;
                id = data.objectID;
                amount = data.amount;
            }

            if (_current != null && _current == _watchedSlot
                && (id != _watchedId || amount != _watchedAmount)
                && _sectionIndex < _sections.Count)
            {
                Announce(_sections[_sectionIndex], _current, announceSectionName: false);
            }

            _watchedSlot = _current;
            _watchedId = id;
            _watchedAmount = amount;
        }

        private static void Enter()
        {
            _active = true;
            InventoryNavState.SuppressNativeInput = true;
            Rebuild();
            if (_sections.Count > 0)
                SelectSection(0, announceSectionName: true);
        }

        private static void Exit()
        {
            _active = false;
            InventoryNavState.SuppressNativeInput = false;
            _sections = new List<SlotSection>();
            _sectionIndex = 0;
            _current = null;
            _watchedSlot = null;
            _watchedId = ObjectID.None;
            _watchedAmount = 0;
            _lastSyncTarget = null;
        }

        private static void Rebuild()
        {
            _sections = SlotSections.Build();
            if (_sectionIndex >= _sections.Count) _sectionIndex = 0;
        }

        private static void HandleInput()
        {
            var joy = ReInput.isReady ? ReInput.controllers.GetLastActiveController<Joystick>() : null;
            if (joy == null) return;

            // Boutons presses durant cette frame (front montant).
            int pressed = -1;
            for (int i = 0; i < joy.buttonCount; i++)
            {
                if (!joy.GetButtonDown(i)) continue;
                int id = joy.ButtonElementIdentifiers[i].id;
                if (id == DpadUp || id == DpadDown || id == DpadLeft || id == DpadRight || id == Lb || id == Rb)
                {
                    pressed = id;
                    break;
                }
            }
            if (pressed < 0) return;

            switch (pressed)
            {
                case Rb: ChangeSection(+1); break;
                case Lb: ChangeSection(-1); break;
                case DpadUp: Move(NavDir.Up); break;
                case DpadDown: Move(NavDir.Down); break;
                case DpadLeft: Move(NavDir.Left); break;
                case DpadRight: Move(NavDir.Right); break;
            }
        }

        private static void ChangeSection(int delta)
        {
            Rebuild(); // l'ouverture d'un coffre/atelier peut avoir ajoute des sections
            if (_sections.Count == 0) return;
            int next = (_sectionIndex + delta % _sections.Count + _sections.Count) % _sections.Count;
            SelectSection(next, announceSectionName: true);
        }

        private static void Move(NavDir dir)
        {
            if (_sectionIndex >= _sections.Count) return;
            var section = _sections[_sectionIndex];
            var target = section.IsList
                ? ListNeighbour(section, dir)
                : SlotSections.BestNeighbour(section, _current, dir);
            if (target == null)
            {
                // Bord de section : on ne sort pas, on re-annonce la position courante.
                Announce(section, _current, announceSectionName: false);
                return;
            }
            _current = target;
            ForceSelect(target);
            Announce(section, target, announceSectionName: false);
        }

        // Navigation en liste : haut/gauche = precedent, bas/droite = suivant, en boucle
        // (depuis le 1er, "haut" ramene au dernier, et inversement).
        private static UIelement ListNeighbour(SlotSection section, NavDir dir)
        {
            int count = section.Slots.Count;
            if (count == 0) return null;
            int idx = section.Slots.IndexOf(_current);
            if (idx < 0) return section.Slots[0];
            int step = (dir == NavDir.Up || dir == NavDir.Left) ? -1 : 1;
            return section.Slots[(idx + step + count) % count];
        }

        private static void SelectSection(int index, bool announceSectionName)
        {
            _sectionIndex = index;
            var section = _sections[index];
            _current = section.Slots.Count > 0 ? section.Slots[0] : null;
            if (_current != null) ForceSelect(_current);
            Announce(section, _current, announceSectionName);
        }

        // Le jeu a change la selection (ex. ouverture de la vue compétences qui
        // auto-selectionne son 1er element) : on se recale dessus, en reconstruisant
        // les sections si c'est un element qu'on ne connaissait pas encore. Evite le
        // "bump manuel" pour prendre en compte une nouvelle vue.
        private static void SyncWithGameSelection()
        {
            var sel = Manager.ui != null ? Manager.ui.currentSelectedUIElement : null;
            if (sel == null || sel == _current) return;

            int idx = FindSectionIndex(sel);
            if (idx < 0)
            {
                if (sel == _lastSyncTarget) return; // deja tente, element non captable
                _lastSyncTarget = sel;
                Rebuild();
                idx = FindSectionIndex(sel);
                if (idx < 0) return;
            }
            _sectionIndex = idx;
            _current = sel;
            Announce(_sections[idx], sel, announceSectionName: true);
        }

        private static int FindSectionIndex(UIelement e)
        {
            for (int i = 0; i < _sections.Count; i++)
                if (_sections[i].Slots.Contains(e)) return i;
            return -1;
        }

        private static void ForceSelect(UIelement slot)
        {
            if (Manager.ui == null) return;
            InventoryNavState.SuppressPassiveAnnounce = true;
            try
            {
                Manager.ui.OnUIElementSelected(slot);
                // Recaler le curseur manette virtuel sur l'element : sinon UIMouse
                // refait un raycast a l'ancienne position au frame suivant et
                // re-selectionne l'ancien slot (le focus "ne suit pas").
                if (Manager.ui.mouse != null)
                    Manager.ui.mouse.PlaceMousePositionOnSelectedUIElementWhenControlledByJoystick();
            }
            finally { InventoryNavState.SuppressPassiveAnnounce = false; }
        }

        private static void Announce(SlotSection section, UIelement slot, bool announceSectionName)
        {
            string body;
            if (slot == null)
            {
                body = Strings.L("ingame.slot.empty");
            }
            else
            {
                int idx = section.Slots.IndexOf(slot);
                string role = SlotSections.RoleLabel(section, slot, idx);
                string content = InGameTtsCore.BuildElementAnnouncement(slot);
                if (string.IsNullOrEmpty(content)) content = Strings.L("ingame.slot.empty");
                body = string.IsNullOrEmpty(role) ? content : role + ", " + content;
            }

            string text = announceSectionName ? section.SectionName + ". " + body : body;
            TtsText.Say(text, true);
        }
    }
}
