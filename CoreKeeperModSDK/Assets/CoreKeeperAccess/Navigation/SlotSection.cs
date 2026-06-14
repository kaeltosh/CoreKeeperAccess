using System.Collections.Generic;
using System.Linq;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using UnityEngine;

namespace CoreKeeperAccess.Navigation
{
    internal enum NavDir { Up, Down, Left, Right }

    // Une section de navigation = un groupe d'emplacements de meme role (barre
    // rapide, sac, equipement...), ordonnes en ordre de lecture (haut-gauche d'abord).
    internal sealed class SlotSection
    {
        public string NameKey;   // cle i18n du nom de section
        public string Kind;      // hotbar / bag / equipment / crafting / chest / trash / pouch / other
        public bool IsList;      // navigation lineaire (1 axe) au lieu de la grille 2D
        // UIelement (pas SlotUIBase) car une section peut contenir des elements
        // non-slots, ex. les onglets de prereglage d'equipement en bas de l'equipement.
        public readonly List<UIelement> Slots = new List<UIelement>();

        // Grille LOGIQUE en lignes (null sauf pour la section pochette) : une ligne =
        // la barre des emplacements d'equipement de bourse (ligne 0), puis une ligne par
        // bourse (son contenu). Gauche/droite = item dans la ligne, haut/bas = bourse.
        // Quand non nul, la nav l'utilise au lieu de la grille par position.
        public List<List<UIelement>> Rows;

        public string SectionName => Strings.L(NameKey);
    }

    internal static class SlotSections
    {
        private const float Eps = 0.01f;

        private static readonly string[] Order =
        {
            "hotbar", "bag", "pouch", "equipment", "buy", "sell", "crafting", "chest", "trash", "other"
        };

        private static readonly Dictionary<string, string> NameKeys = new Dictionary<string, string>
        {
            { "hotbar", "section.hotbar" }, { "bag", "section.bag" }, { "pouch", "section.pouch" },
            { "equipment", "section.equipment" }, { "buy", "section.buy" }, { "sell", "section.sell" },
            { "crafting", "section.crafting" },
            { "chest", "section.chest" }, { "trash", "section.trash" }, { "other", "section.other" },
            { "skills", "section.skills" }, { "talents", "section.talents" }, { "pettalents", "section.pettalents" },
            { "souls", "section.souls" }, { "stats", "section.stats" },
        };

        // Reconstruit les sections a partir des emplacements actuellement affiches.
        public static List<SlotSection> Build()
        {
            var slots = Object.FindObjectsByType<SlotUIBase>(FindObjectsSortMode.None);
            var byKind = new Dictionary<string, List<SlotUIBase>>();

            foreach (var s in slots)
            {
                if (s == null || s.gameObject == null || !s.gameObject.activeInHierarchy || !s.isShowing) continue;
                var kind = KindOf(s);
                if (!byKind.TryGetValue(kind, out var list))
                {
                    list = new List<SlotUIBase>();
                    byKind[kind] = list;
                }
                list.Add(s);
            }

            var sections = new List<SlotSection>();
            foreach (var kind in Order)
            {
                if (!byKind.TryGetValue(kind, out var list) || list.Count == 0) continue;
                // Equipement = liste verticale (plus simple a apprehender qu'une grille
                // d'icones disparates), le reste garde la grille 2D fidele a l'ecran.
                var section = new SlotSection { Kind = kind, NameKey = NameKeys[kind], IsList = kind == "equipment" };
                // Ordre de lecture : du haut vers le bas, puis de gauche a droite.
                section.Slots.AddRange(list
                    .OrderByDescending(s => s.transform.position.y)
                    .ThenBy(s => s.transform.position.x));
                sections.Add(section);
            }
            BuildPouchRows(sections);
            AppendEquipmentTabs(sections);
            // Fenetres de progression (sous-vues de characterWindow) : compétences,
            // arbre de talents de la competence ouverte, talents de familier. Ce sont
            // des UIelement navigables, captes comme des sections en mode liste.
            AddElementSection<SkillUIElement>(sections, "skills");
            AddElementSection<SkillTalentUIElement>(sections, "talents");
            AddElementSection<PetTalentUIElement>(sections, "pettalents");
            // Onglet ames (debloque seulement si HasUnlockedSouls). Chaque SoulsUIElement
            // expose deja titre/description/effet + OnLeftClicked (toggle on/off), donc le
            // pattern generique suffit. On ecarte les emplacements sans ame (soulID None)
            // qui se liraient "Vide".
            AddElementSection<SoulsUIElement>(sections, "souls", s => s.soulID != SoulID.None);
            // Fiche de stats (overlay de l'etoile) : section a part car ses lignes ne
            // sont pas des slots mais des StatTextUIElement, avec titres de section.
            AddStatsSection(sections);
            return sections;
        }

        // Reorganise la section pochette en lignes logiques (cf. SlotSection.Rows) :
        // ligne 0 = les emplacements d'equipement de bourse (Pouch1-4, la "barre des
        // bourses"), puis une ligne par bourse = son contenu, groupe par conteneur. Les
        // Slots a plat sont reconstruits dans l'ordre des lignes pour le reste de la nav.
        private static void BuildPouchRows(List<SlotSection> sections)
        {
            var pouch = sections.Find(s => s.Kind == "pouch");
            if (pouch == null || pouch.Slots.Count == 0) return;
            var pinv = Manager.ui != null ? Manager.ui.playerInventoryUI as InventoryUI : null;
            if (pinv == null) return;
            var player = Manager.main != null ? Manager.main.player : null;
            var eh = player != null ? player.equipmentHandler : null;

            // Ligne 0 : les emplacements d'equipement de bourse (Pouch1-4), deja captes
            // dans la section, ordonnes de gauche a droite.
            var equip = new List<UIelement>();
            foreach (var e in pouch.Slots)
            {
                var s = e as SlotUIBase;
                if (s == null) continue;
                var t = s.slotType;
                if (t == ItemSlotsUIType.Pouch1 || t == ItemSlotsUIType.Pouch2
                    || t == ItemSlotsUIType.Pouch3 || t == ItemSlotsUIType.Pouch4)
                    equip.Add(s);
            }

            var rows = new List<List<UIelement>>();
            if (equip.Count > 0)
            {
                equip.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
                rows.Add(equip);
            }
            // Une ligne par bourse : on lit directement les conteneurs dedies du jeu
            // (pouchSlotsContainers, un par bourse equipee), dans leur ordre naturel. On
            // n'EXCLUT PAS les slots inactifs : le jeu n'active le contenu d'une bourse que
            // lorsqu'on la regarde, donc filtrer sur activeInHierarchy ne montrerait que la
            // bourse couramment visitee. Lecture et interaction passent par le handler du
            // slot (pas par son etat UI), donc l'inactivite ne gene pas.
            if (pinv.pouchSlotsContainers != null)
                for (int k = 0; k < pinv.pouchSlotsContainers.Count; k++)
                {
                    // Bourse non equipee : son handler a une taille nulle. Le conteneur UI
                    // peut persister apres deséquipement -> on saute la ligne, sinon une
                    // bourse retiree resterait affichee. (Index k aligne sur le handler k,
                    // comme le fait le jeu dans ItemSlotsBarUI.)
                    int size = (eh != null && eh.pouchInventorySlotsHandlers != null
                        && k < eh.pouchInventorySlotsHandlers.Length)
                        ? eh.pouchInventorySlotsHandlers[k].size : 0;
                    if (size == 0) continue; // bourse non equipee (conteneur UI persistant)
                    var container = pinv.pouchSlotsContainers[k];
                    if (container == null || container.itemSlots == null) continue;
                    var row = new List<UIelement>();
                    foreach (var s in container.itemSlots)
                    {
                        if (s != null) row.Add(s);
                        if (row.Count >= size) break; // borner a la taille reelle : le jeu
                        // pre-instancie le MAX de slots (10), la bourse n'en utilise que size.
                    }
                    if (row.Count > 0) rows.Add(row);
                }
            if (rows.Count == 0) return;

            pouch.Rows = rows;
            pouch.IsList = false;
            pouch.Slots.Clear();
            foreach (var row in rows) pouch.Slots.AddRange(row);
        }

        // Titre de section associe a chaque ligne de stat navigable (pour prefixer
        // l'annonce au changement de section : "Defense. Armure plus 12"). Rempli
        // par AddStatsSection, lu par l'annonce dans InventoryNavigator.
        private static readonly Dictionary<UIelement, string> StatTitles = new Dictionary<UIelement, string>();

        // Construit la section "stats" si l'overlay de la fiche est ouvert. Les lignes
        // (StatTextUIElement a conditionEffect != None) sont navigables ; les titres de
        // section (conditionEffect == None, plus le niveau d'objet total) servent de
        // jalons et alimentent StatTitles, sans etre navigables eux-memes.
        // L'overlay de la fiche de stats est-il ouvert ? (bouton etoile bascule juste
        // l'activeSelf de statsWindow, par-dessus l'onglet courant.)
        public static bool StatsOverlayActive()
        {
            var cw = Manager.ui != null ? Manager.ui.characterWindow : null;
            var sw = cw != null ? cw.statsWindow : null;
            return sw != null && sw.gameObject != null && sw.gameObject.activeInHierarchy;
        }

        private static void AddStatsSection(List<SlotSection> sections)
        {
            if (!StatsOverlayActive()) return;
            var texts = Manager.ui.characterWindow.statsWindow.statsTexts;
            if (texts == null) return;

            var actives = new List<StatTextUIElement>();
            foreach (var st in texts)
                if (st != null && st.gameObject != null && st.gameObject.activeInHierarchy)
                    actives.Add(st);
            if (actives.Count == 0) return;
            // Ordre visuel haut->bas (les lignes sont empilees vers le bas a l'ecran).
            actives.Sort((a, b) => b.transform.position.y.CompareTo(a.transform.position.y));

            StatTitles.Clear();
            var lines = new List<UIelement>();
            string currentTitle = null;
            foreach (var st in actives)
            {
                if (st.conditionEffect == ConditionEffect.None)
                {
                    var t = TtsText.ResolvePugText(st.text);
                    if (!string.IsNullOrEmpty(t)) currentTitle = t;
                    continue;
                }
                lines.Add(st);
                StatTitles[st] = currentTitle;
            }
            if (lines.Count == 0) return;

            var section = new SlotSection { Kind = "stats", NameKey = NameKeys["stats"], IsList = true };
            section.Slots.AddRange(lines);
            sections.Add(section);
        }

        // Titre de section d'une ligne de stat, ou null. Voir StatTitles.
        public static string StatTitleFor(UIelement e)
            => e != null && StatTitles.TryGetValue(e, out var t) ? t : null;

        // L'element est-il le bouton etoile qui ouvre/ferme la fiche de stats ?
        public static bool IsStatsButton(UIelement e)
        {
            var cw = Manager.ui != null ? Manager.ui.characterWindow : null;
            return cw != null && e != null && cw.statsButton == e;
        }

        // Cree une section (mode liste) a partir de tous les UIelement d'un type donne
        // actuellement affiches. Sert pour les compétences / talents.
        private static void AddElementSection<T>(List<SlotSection> sections, string kind,
            System.Func<T, bool> keep = null) where T : UIelement
        {
            var found = Object.FindObjectsByType<T>(FindObjectsSortMode.None);
            var actives = new List<UIelement>();
            foreach (var e in found)
            {
                if (e != null && e.gameObject != null && e.gameObject.activeInHierarchy && e.isShowing
                    && (keep == null || keep(e)))
                    actives.Add(e);
            }
            if (actives.Count == 0) return;
            actives.Sort(ReadingOrder);
            var section = new SlotSection { Kind = kind, NameKey = NameKeys[kind], IsList = true };
            section.Slots.AddRange(actives);
            sections.Add(section);
        }

        // Ordre de lecture : du haut vers le bas, puis de gauche a droite.
        private static int ReadingOrder(UIelement a, UIelement b)
        {
            int c = b.transform.position.y.CompareTo(a.transform.position.y);
            return c != 0 ? c : a.transform.position.x.CompareTo(b.transform.position.x);
        }

        // Onglets de la fenetre perso ajoutes a la fin de la section equipement : en mode
        // liste, on les atteint en descendant au D-pad. D'abord les prereglages (I/II/III),
        // puis les onglets de vue (perso, stats, ames). Ce sont des CharacterWindowTab
        // (pas des slots), d'ou la liste d'UIelement generiques.
        private static void AppendEquipmentTabs(List<SlotSection> sections)
        {
            var equip = sections.Find(s => s.Kind == "equipment");
            if (equip == null) return;
            var cw = Manager.ui != null ? Manager.ui.characterWindow : null;
            if (cw == null) return;
            AppendTabs(equip, cw.presetTabs);
            AppendTabs(equip, cw.windowTabs);
            // Bouton etoile = ouvre/ferme l'overlay de la fiche de stats (A dessus).
            var star = cw.statsButton;
            if (star != null && star.gameObject != null && star.gameObject.activeInHierarchy
                && star.isShowing && !equip.Slots.Contains(star))
                equip.Slots.Add(star);
        }

        private static void AppendTabs(SlotSection section, List<CharacterWindowTab> tabs)
        {
            if (tabs == null) return;
            foreach (var tab in tabs)
            {
                if (tab != null && tab.gameObject != null && tab.gameObject.activeInHierarchy
                    && tab.isShowing && !section.Slots.Contains(tab))
                    section.Slots.Add(tab);
            }
        }

        private static string KindOf(SlotUIBase s)
        {
            switch (s.slotType)
            {
                case ItemSlotsUIType.PlayerInventorySlot:
                    return s.inventorySlotIndex < 10 ? "hotbar" : "bag";
                case ItemSlotsUIType.PouchInventorySlot:
                case ItemSlotsUIType.Pouch1:
                case ItemSlotsUIType.Pouch2:
                case ItemSlotsUIType.Pouch3:
                case ItemSlotsUIType.Pouch4:
                    return "pouch";
                case ItemSlotsUIType.HelmSlot:
                case ItemSlotsUIType.BreastSlot:
                case ItemSlotsUIType.PantsSlot:
                case ItemSlotsUIType.NecklaceSlot:
                case ItemSlotsUIType.RingSlot1:
                case ItemSlotsUIType.RingSlot2:
                case ItemSlotsUIType.OffhandSlot:
                case ItemSlotsUIType.BagSlot:
                case ItemSlotsUIType.PetSlot:
                case ItemSlotsUIType.LanternSlot:
                case ItemSlotsUIType.HelmVanitySlot:
                case ItemSlotsUIType.BreastVanitySlot:
                case ItemSlotsUIType.PantsVanitySlot:
                    return "equipment";
                case ItemSlotsUIType.BuySlot:
                    return "buy";
                case ItemSlotsUIType.PlayerSellSlot:
                    return "sell";
                case ItemSlotsUIType.RecipeSlot:
                case ItemSlotsUIType.MaterialSlot:
                case ItemSlotsUIType.OutputSlot:
                case ItemSlotsUIType.OutputCategorySlot:
                    return "crafting";
                case ItemSlotsUIType.ChestSlot:
                    return "chest";
                case ItemSlotsUIType.TrashCanSlot:
                    return "trash";
                default:
                    return "other";
            }
        }

        // Libelle du role d'un emplacement, prefixe au contenu lors de l'annonce.
        // Equipement -> nom du role ; grilles -> numero 1-based ; artisanat -> rien
        // (le nom de la recette tient lieu de libelle).
        public static string RoleLabel(SlotSection section, UIelement element, int indexInSection)
        {
            if (section.Kind == "equipment")
            {
                // Slot d'equipement -> nom du role ; onglet de preset -> rien (son
                // titre suffit a l'annonce).
                var slot = element as SlotUIBase;
                return slot != null ? Strings.L(EquipKey(slot.slotType)) : null;
            }
            // Pochette en lignes : numero de colonne (position dans la bourse / la barre).
            if (section.Kind == "pouch" && section.Rows != null)
            {
                int col = ColOf(section, element);
                return col >= 0 ? (col + 1).ToString() : null;
            }
            if (section.Kind == "crafting" || section.Kind == "trash"
                || section.Kind == "skills" || section.Kind == "talents" || section.Kind == "pettalents"
                || section.Kind == "souls" || section.Kind == "stats")
                return null;
            return (indexInSection + 1).ToString();
        }

        // Indice de ligne (= bourse) de l'element dans une section a Rows, ou -1.
        public static int RowOf(SlotSection section, UIelement element)
        {
            if (section.Rows == null || element == null) return -1;
            for (int i = 0; i < section.Rows.Count; i++)
                if (section.Rows[i].Contains(element)) return i;
            return -1;
        }

        // Indice de colonne (position dans la ligne) de l'element, ou -1.
        private static int ColOf(SlotSection section, UIelement element)
        {
            if (section.Rows == null || element == null) return -1;
            foreach (var row in section.Rows)
            {
                int c = row.IndexOf(element);
                if (c >= 0) return c;
            }
            return -1;
        }

        // Libelle d'une ligne de la pochette : ligne 0 = la barre des emplacements
        // d'equipement, lignes suivantes = "Bourse N". Annonce au changement de ligne.
        public static string RowLabel(SlotSection section, int rowIndex)
        {
            if (section.Rows == null || rowIndex < 0 || rowIndex >= section.Rows.Count) return null;
            if (rowIndex == 0) return Strings.L("pouch.equip");
            return Strings.L("pouch.bag") + " " + rowIndex;
        }

        private static string EquipKey(ItemSlotsUIType t)
        {
            switch (t)
            {
                case ItemSlotsUIType.HelmSlot: return "slot.helm";
                case ItemSlotsUIType.BreastSlot: return "slot.breast";
                case ItemSlotsUIType.PantsSlot: return "slot.pants";
                case ItemSlotsUIType.NecklaceSlot: return "slot.necklace";
                case ItemSlotsUIType.RingSlot1: return "slot.ring1";
                case ItemSlotsUIType.RingSlot2: return "slot.ring2";
                case ItemSlotsUIType.OffhandSlot: return "slot.offhand";
                case ItemSlotsUIType.BagSlot: return "slot.backpack";
                case ItemSlotsUIType.PetSlot: return "slot.pet";
                case ItemSlotsUIType.LanternSlot: return "slot.lantern";
                case ItemSlotsUIType.HelmVanitySlot: return "slot.helmVanity";
                case ItemSlotsUIType.BreastVanitySlot: return "slot.breastVanity";
                case ItemSlotsUIType.PantsVanitySlot: return "slot.pantsVanity";
                default: return "section.equipment";
            }
        }

        // Emplacement le plus proche dans la direction donnee, ou null si on est au bord
        // (navigation bornee a la section : c'est ce qui "verrouille" la section).
        public static UIelement BestNeighbour(SlotSection section, UIelement current, NavDir dir)
        {
            if (current == null) return null;
            var p = current.transform.position;
            UIelement best = null;
            float bestScore = float.MaxValue;

            foreach (var s in section.Slots)
            {
                if (s == null || s == current) continue;
                var q = s.transform.position;
                float dx = q.x - p.x, dy = q.y - p.y;
                bool ok;
                float along, across;
                switch (dir)
                {
                    case NavDir.Right: ok = dx > Eps; along = dx; across = Mathf.Abs(dy); break;
                    case NavDir.Left: ok = dx < -Eps; along = -dx; across = Mathf.Abs(dy); break;
                    case NavDir.Up: ok = dy > Eps; along = dy; across = Mathf.Abs(dx); break;
                    default: ok = dy < -Eps; along = -dy; across = Mathf.Abs(dx); break;
                }
                if (!ok) continue;
                // On privilegie fortement l'alignement (meme rangee / colonne).
                float score = across * 10f + along;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = s;
                }
            }
            return best;
        }
    }
}
