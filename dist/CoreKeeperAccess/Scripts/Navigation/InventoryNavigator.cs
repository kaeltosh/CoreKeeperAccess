using System.Collections.Generic;
using CoreKeeperAccess.Controls;
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
        private const int Cross = 6; // A / Croix (prendre-poser), id physique confirme

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

        // Derniere ligne (= bourse) annoncee dans la section pochette, pour ne prefixer le
        // repere de bourse qu'au changement de ligne. Remis a -1 a chaque changement de section.
        private static int _lastPouchRow = -1;

        // Etat precedent de l'overlay de la fiche de stats, pour detecter son
        // ouverture (front montant) et sauter dessus automatiquement.
        private static bool _statsOverlayOpen;

        // Signature de la structure des bourses (nb de cases actives par bourse), pour
        // reconstruire des qu'elle change : ouverture progressive du panneau (les bourses
        // s'activent sur quelques frames) ou deséquipement (une bourse disparait). -1 = a
        // recalculer (entree/sortie de nav).
        private static int _pouchSig = -1;

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

            // Panneau des bourses maintenu deploye : sinon ses 4 emplacements d'equipement
            // (Pouch1-4) restent inactifs et inatteignables -> impossible de deséquiper.
            EnsurePouchPanelOpen();

            // Si les emplacements n'etaient pas prets a l'ouverture, on retente.
            if (_sections.Count == 0)
            {
                Rebuild();
                if (_sections.Count == 0) return;
                SelectSection(0, announceSectionName: true);
                return;
            }

            RefreshIfPouchChanged();
            HandleStatsOverlay();
            SyncWithGameSelection();
            // Slot de contenu de bourse sous masque : on l'expose au patch d'input AVANT
            // de lire les boutons, pour qu'il etouffe le Croix natif cette frame.
            InventoryNavState.OnMaskedSlot = IsMaskedSlot(_current);
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
        internal static void AnnounceDetail()
        {
            if (_current == null) return;
            string info = BuildRecipeComponents(_current);
            string station = Gameplay.GameplayInput.BuildStationDetail(_current);
            if (!string.IsNullOrEmpty(station))
                info = string.IsNullOrEmpty(info) ? station : info + ". " + station;
            string forge = Gameplay.GameplayInput.BuildForgeDetail();
            if (!string.IsNullOrEmpty(forge))
                info = string.IsNullOrEmpty(info) ? forge : info + ". " + forge;
            string merchant = InGameTtsCore.BuildMerchantDetail();
            if (!string.IsNullOrEmpty(merchant))
                info = string.IsNullOrEmpty(info) ? merchant : info + ". " + merchant;
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
            _pouchSig = -1;
            EnsurePouchPanelOpen(); // emplacements de bourse actifs avant le 1er scan
            Rebuild();
            if (_sections.Count > 0)
                SelectSection(0, announceSectionName: true);
        }

        private static void Exit()
        {
            _active = false;
            InventoryNavState.SuppressNativeInput = false;
            InventoryNavState.OnMaskedSlot = false;
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
            _pouchSig = -1;
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
            // Les combos eux-memes sont routes par ComboDispatcher (cf. ComboBindings).
            if (InfoKey.ModifierHeld) return;

            // Boutons presses durant cette frame (front montant).
            int pressed = -1;
            bool crossPressed = false;
            for (int i = 0; i < joy.buttonCount; i++)
            {
                if (!joy.GetButtonDown(i)) continue;
                int id = joy.ButtonElementIdentifiers[i].id;
                if (id == DpadUp || id == DpadDown || id == DpadLeft || id == DpadRight || id == Lb || id == Rb)
                {
                    pressed = id;
                    break;
                }
                if (id == Cross) crossPressed = true;
            }

            // Croix sur un slot de bourse masque : l'action native taperait a cote (cf.
            // OnMaskedSlot), on route prendre/poser directement sur notre slot.
            if (crossPressed && InventoryNavState.OnMaskedSlot)
            {
                PickPlaceMaskedSlot();
                return;
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

        // Deploie le panneau des bourses (PouchesWindow) s'il est replie. Replie, ses 4
        // emplacements d'equipement (Pouch1-4) sont inactifs -> ni captes par le scan, ni
        // manipulables, donc pas de deséquipement possible. On agit sur root directement
        // (ce que fait ShowPouches du jeu) pour eviter le son de TogglePouchWindow.
        private static void EnsurePouchPanelOpen()
        {
            var eq = Manager.ui != null ? Manager.ui.equipmentInventoryUI : null;
            var pw = eq != null ? eq.pouchesWindow : null;
            if (pw != null && pw.root != null && !pw.root.activeSelf)
                pw.root.SetActive(true);
        }

        // Reconstruit la nav si la structure des bourses a change (cf. _pouchSig) :
        // ouverture progressive du panneau (les bourses s'activent sur quelques frames) ou
        // deséquipement (une bourse disparait). Silencieux tant que l'emplacement courant
        // survit (on garde la selection) ; sinon on recale sur le 1er slot de la section.
        private static void RefreshIfPouchChanged()
        {
            int sig = ComputePouchSig();
            if (sig == _pouchSig) return;
            _pouchSig = sig;
            if (_sections.Count == 0) return; // le rebuild initial s'en charge

            string kind = _sectionIndex < _sections.Count ? _sections[_sectionIndex].Kind : null;
            var keep = _current;
            Rebuild();
            if (kind != null)
            {
                int idx = _sections.FindIndex(s => s.Kind == kind);
                if (idx >= 0) _sectionIndex = idx;
            }
            if (_sectionIndex >= _sections.Count) _sectionIndex = 0;
            if (_sections.Count == 0) { _current = null; return; }

            var section = _sections[_sectionIndex];
            if (keep != null && section.Slots.Contains(keep))
            {
                _current = keep; // selection intacte : aucune annonce
            }
            else
            {
                _current = section.Slots.Count > 0 ? section.Slots[0] : null;
                if (_current != null)
                {
                    ForceSelect(_current);
                    Announce(section, _current, announceSectionName: false);
                }
            }
        }

        // Empreinte de la structure des bourses : nombre de cases actives par bourse.
        // Change a l'ouverture (peuplement progressif) et au deséquipement (bourse en moins).
        private static int ComputePouchSig()
        {
            var player = Manager.main != null ? Manager.main.player : null;
            var eh = player != null ? player.equipmentHandler : null;
            if (eh == null || eh.pouchInventorySlotsHandlers == null) return 0;
            // Source de verite des bourses equipees : la taille de chaque handler de bourse
            // (0 = non equipee). Ne bouge qu'a l'equipement / deséquipement, jamais a la
            // navigation -> pas de rebuild ni de re-annonce parasite. (Le conteneur UI peut
            // persister apres deséquipement, d'ou le besoin de cette source-ci.)
            int sig = 17;
            foreach (var h in eh.pouchInventorySlotsHandlers)
                sig = sig * 31 + (h != null ? h.size : 0);
            return sig;
        }

        // Slots sous masque de defilement (panneau des bourses) : le curseur manette
        // virtuel ne les tient pas. Concerne le CONTENU (PouchInventorySlot) et les 4
        // emplacements d'equipement (Pouch1-4). Voir OnMaskedSlot.
        private static bool IsMaskedSlot(UIelement e)
        {
            var s = e as InventorySlotUI;
            if (s == null) return false;
            var t = s.slotType;
            return t == ItemSlotsUIType.PouchInventorySlot
                || t == ItemSlotsUIType.Pouch1 || t == ItemSlotsUIType.Pouch2
                || t == ItemSlotsUIType.Pouch3 || t == ItemSlotsUIType.Pouch4;
        }

        // Prendre/poser sur le slot de bourse courant en appelant directement la methode
        // du jeu qui agit sur le slot PASSE (DoMove), au lieu de l'action native qui passe
        // par le raycast du curseur (lequel manque le slot masque). Le contenu de la main
        // est ensuite reannonce par WatchHandChange / WatchSlotChange.
        private static void PickPlaceMaskedSlot()
        {
            var slot = _current as InventorySlotUI;
            var mouse = Manager.ui != null ? Manager.ui.mouse : null;
            if (slot == null || mouse == null) return;
            mouse.OnInventorySlotLeftClicked(slot, false, true, -1, false);
        }

        private static void ChangeSection(int delta)
        {
            // On memorise la section courante par IDENTITE (son Kind) avant de
            // reconstruire : le Rebuild peut faire apparaitre/disparaitre des sections
            // (typiquement les bourses, dont la fenetre s'ouvre/se ferme), et un index
            // numerique brut se decalerait alors -> dans un sens on saute une section,
            // dans l'autre on tombe dessus par le wrap. On se recale sur le Kind, puis
            // on applique le pas depuis cette position stable.
            string currentKind = _sectionIndex < _sections.Count ? _sections[_sectionIndex].Kind : null;
            Rebuild(); // l'ouverture d'un coffre/atelier peut avoir ajoute des sections
            if (_sections.Count == 0) return;
            int baseIdx = currentKind != null ? _sections.FindIndex(s => s.Kind == currentKind) : -1;
            if (baseIdx < 0) baseIdx = Mathf.Clamp(_sectionIndex, 0, _sections.Count - 1);
            int next = ((baseIdx + delta) % _sections.Count + _sections.Count) % _sections.Count;
            SelectSection(next, announceSectionName: true);
        }

        private static void Move(NavDir dir)
        {
            if (_sectionIndex >= _sections.Count) return;
            var section = _sections[_sectionIndex];
            var target = section.Rows != null
                ? RowNeighbour(section, _current, dir)
                : section.IsList
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

        // Navigation en grille LOGIQUE par lignes (section pochette) : gauche/droite =
        // item dans la ligne (= la bourse), haut/bas = ligne (= bourse, ligne 0 = la barre
        // des emplacements d'equipement). Bornee : null au bord -> Move reannonce sur place.
        // En changeant de ligne, on garde la colonne, clampee a la taille de la cible.
        private static UIelement RowNeighbour(SlotSection section, UIelement current, NavDir dir)
        {
            var rows = section.Rows;
            if (rows == null || rows.Count == 0) return null;

            int r = -1, c = -1;
            for (int i = 0; i < rows.Count && r < 0; i++)
            {
                int j = rows[i].IndexOf(current);
                if (j >= 0) { r = i; c = j; }
            }
            if (r < 0) return rows[0].Count > 0 ? rows[0][0] : null;

            if (dir == NavDir.Left || dir == NavDir.Right)
            {
                c += dir == NavDir.Left ? -1 : 1;
                var row = rows[r];
                return (c >= 0 && c < row.Count) ? row[c] : null;
            }
            // Haut / bas : on change de ligne (de bourse).
            r += dir == NavDir.Up ? -1 : 1;
            if (r < 0 || r >= rows.Count) return null;
            var target = rows[r];
            if (target.Count == 0) return null;
            c = Mathf.Clamp(c, 0, target.Count - 1);
            return target[c];
        }

        private static void SelectSection(int index, bool announceSectionName)
        {
            _lastStatTitle = null; // tout changement de section reamorce le prefixe de titre
            _lastPouchRow = -1;    // idem pour le repere de bourse (pochette en lignes)
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

            // Sur un slot de bourse (sous masque), le curseur manette virtuel raccroche en
            // permanence un slot visible : on ne le suit jamais, on reste sur notre
            // selection logique (sinon _current devie et l'annonce se repete en boucle).
            if (IsMaskedSlot(_current))
            {
                ForceSelect(_current);
                return;
            }

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

            // Raccrochage parasite du curseur manette virtuel : quand on saute sur une
            // section dont le slot ne "tient" pas le focus (bourse = slot sous masque de
            // defilement), UIMouse re-selectionne tout seul, des la frame suivante, un slot
            // visible de la barre rapide. Ce sel appartient a une section DEJA connue
            // (trouvee sans rebuild) et DIFFERENTE de la courante : c'est un recul parasite,
            // pas une nouvelle vue (qui, elle, amene un element inconnu). Si on le suivait,
            // la section juste apres les bourses (equipement) deviendrait inatteignable au
            // bumper droit (on retombe en boucle sur la barre rapide). On reimpose donc
            // notre slot. Une vraie nouvelle vue (sel inconnu) tombe dans la branche
            // rebuild ci-dessous et reste captee normalement.
            if (_current != null && _sectionIndex < _sections.Count)
            {
                int known = FindSectionIndex(sel);
                if (known >= 0 && known != _sectionIndex)
                {
                    ForceSelect(_current);
                    return;
                }
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

                // Pochette en lignes : on prefixe la bourse (ou la barre d'equipement) au
                // changement de ligne, pour savoir sur quelle bourse on arrive apres un
                // haut/bas. Inchange en gauche/droite (meme ligne).
                if (section.Kind == "pouch" && section.Rows != null)
                {
                    int row = SlotSections.RowOf(section, slot);
                    if (row != _lastPouchRow)
                    {
                        string label = SlotSections.RowLabel(section, row);
                        if (!string.IsNullOrEmpty(label)) body = label + ". " + body;
                    }
                    _lastPouchRow = row;
                }
            }

            string text = announceSectionName ? section.SectionName + ". " + body : body;
            TtsText.Say(text, true);
        }
    }
}
