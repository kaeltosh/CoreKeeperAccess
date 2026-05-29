using System.Collections.Generic;
using CoreKeeperAccess.Localization;
using HarmonyLib;

namespace CoreKeeperAccess.Patches
{
    internal static class InGameTtsState
    {
        // Dernier element UI in-game annonce (dedup par instance, pas par texte :
        // plusieurs slots vides partagent le meme libelle mais doivent s'annoncer
        // chacun a la navigation).
        public static int LastSelectedInstanceId;
    }

    internal static class InGameTtsCore
    {
        // Construit l'annonce d'un element UI in-game (slot, bouton) a partir de
        // son titre de survol natif + la quantite si c'est une pile d'objets.
        // Volontairement court (titre + quantite) pour une navigation fluide ;
        // description et stats restent disponibles pour un futur "lire le detail".
        public static string BuildElementAnnouncement(UIelement element)
        {
            if (element == null) return null;

            var title = TtsText.ResolveTextAndFormatFields(element.GetHoverTitle());
            if (string.IsNullOrEmpty(title)) return null;

            var parts = new List<string> { title };
            var seen = new HashSet<string> { title };

            int amount = GetAnnounceAmount(element);
            if (amount > 1) parts.Add(amount.ToString());

            void Add(string s)
            {
                if (!string.IsNullOrEmpty(s) && seen.Add(s)) parts.Add(s);
            }

            Add(BuildCraftInfo(element));

            // Tooltip : description puis stats, lus directement a la selection.
            // Pour zapper, il suffit de bouger (l'annonce suivante interrompt).
            var desc = element.GetHoverDescription();
            if (desc != null)
                foreach (var d in desc) Add(TtsText.ResolveTextAndFormatFields(d));

            var stats = element.GetHoverStats(false);
            if (stats != null)
                foreach (var s in stats) Add(TtsText.ResolveTextAndFormatFields(s));

            return string.Join(", ", parts);
        }

        // Quantite a annoncer : pour une recette = la quantite PRODUITE par craft
        // (ex. torche x3), sinon = la quantite contenue dans l'emplacement.
        private static int GetAnnounceAmount(UIelement element)
        {
            var recipe = element as RecipeSlotUI;
            if (recipe != null)
            {
                var player = Manager.main != null ? Manager.main.player : null;
                var handler = player != null ? player.activeCraftingHandler : null;
                if (handler != null)
                {
                    var info = handler.GetRecipeInfo(recipe.inventorySlotIndex);
                    return info.isValid ? info.amount : 1;
                }
                return 1;
            }
            return element.GetContainedObject().objectData.amount;
        }

        // Pour une recette d'artisanat : "fabricable" si on a tout, sinon la liste
        // detaillee de ce qui manque ("manque 3 Bois, 2 Cuivre"). Renvoie null pour
        // tout element qui n'est pas une recette (GetRequiredMaterials = null).
        private static string BuildCraftInfo(UIelement element)
        {
            List<PugDatabase.MaterialInfo> mats;
            try { mats = element.GetRequiredMaterials(false, false); }
            catch { return null; }
            if (mats == null || mats.Count == 0) return null;

            var missing = new List<string>();
            foreach (var m in mats)
            {
                if (m == null || m.amountAvailable >= m.amountNeeded) continue;
                int lack = m.amountNeeded - m.amountAvailable;
                var name = ResolveObjectName(m.objectID);
                missing.Add(string.IsNullOrEmpty(name) ? lack.ToString() : lack + " " + name);
            }

            return missing.Count == 0
                ? Strings.L("craft.craftable")
                : Strings.L("craft.missing") + " " + string.Join(", ", missing);
        }

        // Nom localise d'un objet a partir de son ObjectID (materiaux, resultat de craft).
        public static string ResolveObjectName(ObjectID objectID)
        {
            if (objectID == ObjectID.None) return null;
            var taf = PlayerController.GetObjectName(new ContainedObjectsBuffer
            {
                objectData = new ObjectDataCD { objectID = objectID }
            }, false);
            return TtsText.ResolveTextAndFormatFields(taf);
        }
    }

    // Navigation dans l'inventaire / l'UI in-game. OnUIElementSelected est le point
    // central de selection (souris + clavier + manette). Il aiguille deja les options
    // de menu vers Manager.menu (patch menus du jalon 2) ; on ne traite donc ici que
    // les elements non-menu (slots, boutons in-game) -> zero conflit avec le jalon 2.
    [HarmonyPatch(typeof(UIManager), nameof(UIManager.OnUIElementSelected))]
    internal static class UIManagerOnUIElementSelectedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(UIelement uiElement)
        {
            // Quand notre navigation a11y force la selection, c'est elle qui annonce
            // (avec le contexte de section) : on etouffe l'annonce passive.
            if (Navigation.InventoryNavState.SuppressPassiveAnnounce) return;
            if (uiElement == null || uiElement.isMenuOption) return;

            int id = uiElement.GetInstanceID();
            if (id == InGameTtsState.LastSelectedInstanceId) return;

            var announcement = InGameTtsCore.BuildElementAnnouncement(uiElement);
            if (string.IsNullOrEmpty(announcement))
            {
                // Pas de titre lisible : essentiellement un slot d'inventaire vide.
                announcement = Strings.L("ingame.slot.empty");
                if (string.IsNullOrEmpty(announcement)) return;
            }

            InGameTtsState.LastSelectedInstanceId = id;
            TtsText.Say(announcement, true);
        }
    }

    // Notifications de jeu (objet ramasse, item peche, point de talent, durabilite,
    // ame, level de familier...) + messages de chat recus. Tout passe par
    // ChatWindow.AddPugText avec un PugText deja rendu -> on relit le texte affiche.
    [HarmonyPatch(typeof(ChatWindow), "AddPugText")]
    internal static class ChatWindowAddPugTextPatch
    {
        private static readonly HashSet<ChatWindow.MessageTextType> AnnouncedTypes = new HashSet<ChatWindow.MessageTextType>
        {
            ChatWindow.MessageTextType.Received,
            ChatWindow.MessageTextType.NewItem,
            ChatWindow.MessageTextType.CaughtItem,
            ChatWindow.MessageTextType.NewTalentPointAvailable,
            ChatWindow.MessageTextType.DurabilityLost,
            ChatWindow.MessageTextType.AdditionalItemGained,
            ChatWindow.MessageTextType.GainedItem,
            ChatWindow.MessageTextType.ReceivedItems,
            ChatWindow.MessageTextType.PetLeveledUp,
            ChatWindow.MessageTextType.GainedSoul,
        };

        [HarmonyPostfix]
        public static void Postfix(ChatWindow.MessageTextType type, PugText text)
        {
            if (!AnnouncedTypes.Contains(type)) return;

            var announcement = TtsText.ResolvePugText(text);
            if (string.IsNullOrEmpty(announcement)) return;

            // File d'attente NVDA (interrupt = false) : les notifs s'enchainent sans
            // se couper entre elles ni ecraser une annonce de navigation en cours.
            TtsText.Say(announcement, false);
        }
    }

    // Resultat d'une fabrication : CraftItem n'est appele que si les materiaux sont
    // la, donc un postfix suffit pour annoncer le succes. L'objet fabrique atterrit
    // dans la main (curseur) -> on le rappelle a l'utilisateur.
    [HarmonyPatch(typeof(InventoryHandler), nameof(InventoryHandler.CraftItem))]
    internal static class InventoryHandlerCraftItemPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            // On lit le contenu reel de la main apres le craft : ca donne le total
            // tenu (cumule si on enchaine les crafts), pas juste ce qui vient d'etre fait.
            var player = Manager.main != null ? Manager.main.player : null;
            var mouse = player != null ? player.mouseInventoryHandler : null;
            if (mouse == null) return;

            var held = mouse.GetObjectData(0);
            var name = InGameTtsCore.ResolveObjectName(held.objectID);
            if (string.IsNullOrEmpty(name)) return;

            string qty = held.amount > 1 ? held.amount + " " : "";
            // interrupt = true : un craft = une annonce qui ECRASE la precedente.
            // En rafale on n'entend que le dernier etat, pas chaque occurrence empilee.
            TtsText.Say(Strings.L("craft.crafted") + ", " + qty + name + " " + Strings.L("craft.inhand"), true);
        }
    }

    // Bascule de jeu d'equipement (onglets I/II/III ou boutons EQUIP_PRESET_1/2/3).
    // On annonce le prereglage actif ; le contenu des slots se relit ensuite via la
    // navigation / WatchSlotChange.
    [HarmonyPatch(typeof(PlayerController), nameof(PlayerController.SetActiveEquipmentPreset))]
    internal static class PlayerControllerSetActiveEquipmentPresetPatch
    {
        [HarmonyPostfix]
        public static void Postfix(int presetIndex)
        {
            TtsText.Say(Strings.L("equip.preset") + " " + (presetIndex + 1), true);
        }
    }
}
