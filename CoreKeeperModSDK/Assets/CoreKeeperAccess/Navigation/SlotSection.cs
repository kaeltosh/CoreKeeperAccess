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

        public string SectionName => Strings.L(NameKey);
    }

    internal static class SlotSections
    {
        private const float Eps = 0.01f;

        private static readonly string[] Order =
        {
            "hotbar", "bag", "pouch", "equipment", "crafting", "chest", "trash", "other"
        };

        private static readonly Dictionary<string, string> NameKeys = new Dictionary<string, string>
        {
            { "hotbar", "section.hotbar" }, { "bag", "section.bag" }, { "pouch", "section.pouch" },
            { "equipment", "section.equipment" }, { "crafting", "section.crafting" },
            { "chest", "section.chest" }, { "trash", "section.trash" }, { "other", "section.other" },
            { "skills", "section.skills" }, { "talents", "section.talents" }, { "pettalents", "section.pettalents" },
            { "stats", "section.stats" },
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
            AppendEquipmentTabs(sections);
            // Fenetres de progression (sous-vues de characterWindow) : compétences,
            // arbre de talents de la competence ouverte, talents de familier. Ce sont
            // des UIelement navigables, captes comme des sections en mode liste.
            AddElementSection<SkillUIElement>(sections, "skills");
            AddElementSection<SkillTalentUIElement>(sections, "talents");
            AddElementSection<PetTalentUIElement>(sections, "pettalents");
            // Fiche de stats (overlay de l'etoile) : section a part car ses lignes ne
            // sont pas des slots mais des StatTextUIElement, avec titres de section.
            AddStatsSection(sections);
            return sections;
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
        private static void AddElementSection<T>(List<SlotSection> sections, string kind) where T : UIelement
        {
            var found = Object.FindObjectsByType<T>(FindObjectsSortMode.None);
            var actives = new List<UIelement>();
            foreach (var e in found)
            {
                if (e != null && e.gameObject != null && e.gameObject.activeInHierarchy && e.isShowing)
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
            if (section.Kind == "crafting" || section.Kind == "trash"
                || section.Kind == "skills" || section.Kind == "talents" || section.Kind == "pettalents"
                || section.Kind == "stats")
                return null;
            return (indexInSection + 1).ToString();
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
