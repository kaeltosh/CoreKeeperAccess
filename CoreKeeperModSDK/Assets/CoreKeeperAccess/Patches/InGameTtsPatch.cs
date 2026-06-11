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

        // Annonce d'une ligne de la fiche de stats. Contrairement aux slots, une
        // StatTextUIElement n'a pas de hover title : le texte affiche (deja rendu,
        // valeur substituee) est dans son champ .text. On y ajoute la description
        // longue (GetHoverStats -> ConditionEffectDesc), dispo si la ligne est a l'ecran.
        public static string BuildStatLine(UIelement element)
        {
            var st = element as StatTextUIElement;
            if (st == null) return null;

            var parts = new List<string>();
            var seen = new HashSet<string>();
            void Add(string s) { if (!string.IsNullOrEmpty(s) && seen.Add(s)) parts.Add(s); }

            Add(TtsText.ResolvePugText(st.text));
            var hs = st.GetHoverStats(false);
            if (hs != null)
                foreach (var h in hs) Add(TtsText.ResolveTextAndFormatFields(h));

            return parts.Count == 0 ? null : string.Join(", ", parts);
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
        // FALLBACK : certains objets n'ont pas de terme localise (cable ancien des
        // ruines, statues de boss, generateur...) -> ils etaient DETECTES mais MUETS
        // (bip sans nom, info vide). Plutot que le silence, on lit le nom d'enum
        // decoupe pour le TTS ("IndestructibleAncientWire" -> "Indestructible Ancient
        // Wire"). Pas localise, mais identifiable - des libelles i18n cibles pourront
        // s'ajouter au cas par cas.
        public static string ResolveObjectName(ObjectID objectID)
        {
            if (objectID == ObjectID.None) return null;
            var taf = PlayerController.GetObjectName(new ContainedObjectsBuffer
            {
                objectData = new ObjectDataCD { objectID = objectID }
            }, false);
            string name = TtsText.ResolveTextAndFormatFields(taf);
            return string.IsNullOrEmpty(name) ? SplitEnumName(objectID.ToString()) : name;
        }

        // "LarvaHiveBossStatue" -> "Larva Hive Boss Statue" (espaces aux frontieres de
        // majuscules, en preservant les sigles).
        private static string SplitEnumName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            var sb = new System.Text.StringBuilder(raw.Length + 8);
            for (int i = 0; i < raw.Length; i++)
            {
                if (i > 0 && char.IsUpper(raw[i])
                    && (char.IsLower(raw[i - 1])
                        || (i + 1 < raw.Length && char.IsLower(raw[i + 1]))))
                    sb.Append(' ');
                sb.Append(raw[i]);
            }
            return sb.ToString();
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
            // BlockingUIElement = bloqueur invisible (pose par les overlays, ex. la
            // fiche de stats) ; il n'a jamais de titre lisible et le curseur manette
            // tend a deraper dessus -> on l'ignore pour ne pas lire un "Vide" parasite.
            if (uiElement == null || uiElement.isMenuOption || uiElement is BlockingUIElement) return;

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

    // NOTE : l'annonce du resultat de craft "en main" est desormais geree de facon
    // unifiee par InventoryNavigator.WatchHandChange (qui surveille la main et couvre
    // AUSSI les prises d'objet, pas seulement le craft). Le postfix sur CraftItem a donc
    // ete retire pour eviter le doublon.

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
