using System.Collections.Generic;
using CoreKeeperAccess.Localization;
using DavyKager;
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

            int amount = element.GetContainedObject().objectData.amount;
            return amount > 1 ? title + ", " + amount : title;
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
            Tolk.Output(announcement, true);
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
            Tolk.Output(announcement, false);
        }
    }
}
