using System.Collections.Generic;
using System.Linq;
using CoreKeeperAccess.Localization;
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
            return sections;
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
            if (section.Kind == "crafting" || section.Kind == "trash")
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
