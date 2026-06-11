using System.Collections.Generic;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using Rewired;
using UnityEngine;

namespace CoreKeeperAccess.Navigation
{
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

        // Suivi du contenu de la MAIN (mouseInventoryHandler) : une prise ou un craft met
        // l'objet en main sans changer le slot courant -> on surveille la main a part pour
        // annoncer ce qu'on tient ("N nom en main").
        private static ObjectID _handId = ObjectID.None;
        private static int _handAmount;

        // Derniere selection "jeu" hors de nos sections, pour ne reconstruire qu'une
        // seule fois quand une nouvelle vue s'ouvre (anti-spam de rebuild).
        private static UIelement _lastSyncTarget;

        // Dernier titre de section annonce dans la fiche de stats (pour ne le repeter
        // qu'au changement de section). Remis a null a chaque changement de section.
        private static string _lastStatTitle;

        // Etat precedent de l'overlay de la fiche de stats, pour detecter son
        // ouverture (front montant) et sauter dessus automatiquement.
        private static bool _statsOverlayOpen;

        public static void Update()
        {
            // Filet temporel de l'armement de la roue (consomme par le patch a la
            // premiere lecture ; annule ici si jamais lu en 2 frames).
            if (InventoryNavState.ArmedInput.HasValue && --InventoryNavState.ArmedTtl <= 0)
                InventoryNavState.ArmedInput = null;

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

            HandleStatsOverlay();
            SyncWithGameSelection();
            HandleInput();
            ActionWheel.Tick();
            WatchSlotChange();
            WatchHandChange();
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

        // Apres une prise / un craft, l'objet va dans la MAIN (curseur d'inventaire), pas
        // dans le slot courant -> on surveille la main a part et on annonce ce qu'on tient
        // ("N nom en main") des qu'elle change. Pose (main videe) : pas d'annonce ici, le
        // slot rempli etant deja annonce par WatchSlotChange.
        private static void WatchHandChange()
        {
            var player = Manager.main != null ? Manager.main.player : null;
            var mouse = player != null ? player.mouseInventoryHandler : null;
            ObjectID id = ObjectID.None;
            int amount = 0;
            if (mouse != null)
            {
                var data = mouse.GetObjectData(0);
                id = data.objectID;
                amount = data.amount;
            }

            if ((id != _handId || amount != _handAmount) && id != ObjectID.None)
            {
                string nom = InGameTtsCore.ResolveObjectName(id);
                if (!string.IsNullOrEmpty(nom))
                {
                    string qty = amount > 1 ? amount + " " : "";
                    TtsText.Say(qty + nom + " " + Strings.L("craft.inhand"), true);
                }
            }

            _handId = id;
            _handAmount = amount;
        }

        // Commande touche access (Triangle + haut) dans l'inventaire : details de l'element
        // courant. En craft = liste des composants REQUIS de la recette (tout, pas seulement
        // ce qui manque). Station de reparation ouverte = en plus, cout de reparation et
        // gain de recyclage estime de l'objet. Sans effet sinon.
        private static void AnnounceDetail()
        {
            if (_current == null) return;
            string info = BuildRecipeComponents(_current);
            string station = Gameplay.GameplayInput.BuildStationDetail(_current);
            if (!string.IsNullOrEmpty(station))
                info = string.IsNullOrEmpty(info) ? station : info + ". " + station;
            if (!string.IsNullOrEmpty(info)) TtsText.Say(info, true);
        }

        // "Requiert 3 Bois, 2 Cuivre" pour une recette ; null si l'element n'est pas une
        // recette (GetRequiredMaterials renvoie null).
        private static string BuildRecipeComponents(UIelement element)
        {
            List<PugDatabase.MaterialInfo> mats;
            try { mats = element.GetRequiredMaterials(false, false); }
            catch { return null; }
            if (mats == null || mats.Count == 0) return null;

            var parts = new List<string>();
            foreach (var m in mats)
            {
                if (m == null) continue;
                string nom = InGameTtsCore.ResolveObjectName(m.objectID);
                if (!string.IsNullOrEmpty(nom)) parts.Add(m.amountNeeded + " " + nom);
            }
            if (parts.Count == 0) return null;
            return Strings.L("craft.requires") + " " + string.Join(", ", parts);
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
            _handId = ObjectID.None;
            _handAmount = 0;
            _lastSyncTarget = null;
            _lastStatTitle = null;
            _statsOverlayOpen = false;
        }

        private static void Rebuild()
        {
            _sections = SlotSections.Build();
            if (_sectionIndex >= _sections.Count) _sectionIndex = 0;
        }

        // L'ouverture de l'overlay stats (A sur l'etoile) ne deplace pas le focus cote
        // jeu : on detecte le front montant et on saute nous-memes sur la section stats.
        // On reessaie chaque frame tant que les lignes ne sont pas peuplees (elles le
        // sont au LateUpdate du scroll, donc souvent 1 frame apres l'ouverture). Front
        // descendant : on retire la section du cycle.
        private static void HandleStatsOverlay()
        {
            bool open = SlotSections.StatsOverlayActive();
            if (open && !_statsOverlayOpen)
            {
                if (JumpToSection("stats")) _statsOverlayOpen = true;
            }
            else if (!open && _statsOverlayOpen)
            {
                _statsOverlayOpen = false;
                Rebuild();
            }
        }

        // Reconstruit et selectionne la section du type donne. Vrai si elle existe.
        private static bool JumpToSection(string kind)
        {
            Rebuild();
            int idx = _sections.FindIndex(s => s.Kind == kind);
            if (idx < 0) return false;
            SelectSection(idx, announceSectionName: true);
            return true;
        }

        private static void HandleInput()
        {
            var joy = ReInput.isReady ? ReInput.controllers.GetLastActiveController<Joystick>() : null;
            if (joy == null) return;

            // Touche access tenue : le D-pad est reserve aux commandes (pas la nav).
            // Triangle + haut = details de l'element courant (en craft : composants
            // requis) ; + bas = transferer ; + droite = reparer ; + gauche = tout
            // recycler (les deux derniers exigent la station de reparation ouverte).
            if (InfoKey.ModifierHeld)
            {
                if (InfoKey.DetailRequested) AnnounceDetail();
                else if (InfoKey.ComboDown) Gameplay.GameplayInput.TransferSelected();
                else if (InfoKey.ComboRight) Gameplay.GameplayInput.RepairSelected();
                else if (InfoKey.ComboLeft) Gameplay.GameplayInput.SalvageStation();
                return;
            }

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
            _lastStatTitle = null; // tout changement de section reamorce le prefixe de titre
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

            // Overlay stats ouvert mais pas encore bascule dessus (ses lignes ne sont
            // peuplees qu'une frame apres l'ouverture) : on ignore les derapages du
            // curseur (slot vide derriere l'overlay) le temps que le saut se fasse,
            // sinon le tout premier appui lit "Vide" au lieu de la 1re stat.
            if (SlotSections.StatsOverlayActive() && !_statsOverlayOpen) return;

            // Verrou fiche de stats : juste apres l'ouverture, le scroll n'est pas cale
            // donc la ligne courante n'est pas "isVisibleOnScreen" -> le curseur manette
            // virtuel (UIMouse) raccroche un slot d'inventaire derriere l'overlay. Tant
            // qu'on tient la section stats ouverte, on re-impose notre ligne au lieu de
            // suivre ce derapage (le temps que le scroll converge). Les deplacements
            // volontaires (D-pad) passent avant via le test sel == _current ci-dessus.
            if (_current != null && _sectionIndex < _sections.Count
                && _sections[_sectionIndex].Kind == "stats" && SlotSections.StatsOverlayActive()
                && !_sections[_sectionIndex].Slots.Contains(sel))
            {
                ForceSelect(_current);
                return;
            }

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
                string content = section.Kind == "stats"
                    ? InGameTtsCore.BuildStatLine(slot)
                    : InGameTtsCore.BuildElementAnnouncement(slot);
                // L'etoile n'a pas de hover title : libelle de repli.
                if (string.IsNullOrEmpty(content) && SlotSections.IsStatsButton(slot))
                    content = Strings.L("section.stats");
                if (string.IsNullOrEmpty(content)) content = Strings.L("ingame.slot.empty");
                body = string.IsNullOrEmpty(role) ? content : role + ", " + content;

                // Dans la fiche de stats, on prefixe le titre de section au changement
                // (les titres ne sont pas des lignes navigables, sinon on les louperait).
                if (section.Kind == "stats")
                {
                    string title = SlotSections.StatTitleFor(slot);
                    if (!string.IsNullOrEmpty(title) && title != _lastStatTitle)
                        body = title + ". " + body;
                    _lastStatTitle = title;
                }
            }

            string text = announceSectionName ? section.SectionName + ". " + body : body;
            TtsText.Say(text, true);
        }
    }
}
