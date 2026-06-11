using System.Collections.Generic;
using System.Reflection;
using CoreKeeperAccess.Localization;
using HarmonyLib;
using Rewired;

namespace CoreKeeperAccess.Patches
{
    internal static class MenuTtsState
    {
        public static int LastInstanceId;
        public static bool SuppressDuringActivate;
    }

    internal static class MenuTtsCore
    {
        public static void AnnounceOption(RadicalMenuOption option, bool force)
        {
            if (option == null) return;
            if (MenuTtsState.SuppressDuringActivate) return;
            if (!option.IsSelected()) return;

            int id = option.GetInstanceID();
            if (!force && id == MenuTtsState.LastInstanceId) return;

            var announcement = BuildAnnouncement(option);
            if (string.IsNullOrEmpty(announcement)) return;

            MenuTtsState.LastInstanceId = id;
            TtsText.Say(announcement, true);
        }

        public static string BuildAnnouncementPublic(RadicalMenuOption option)
        {
            return option == null ? null : BuildAnnouncement(option);
        }

        private static readonly Dictionary<string, string> IconOnlyOptionKeys = new Dictionary<string, string>
        {
            { "WorldSlotMoreOption",   "menu.option.more" },
            { "WorldSlotDeleteOption", "menu.option.delete" },
            { "SaveSlotDeleteOption",  "menu.option.delete" },
        };

        private static string BuildAnnouncement(RadicalMenuOption option)
        {
            var parts = new List<string>();
            var seen = new HashSet<string>();
            var seenTexts = new HashSet<PugText>();

            void AddPart(string s)
            {
                if (string.IsNullOrEmpty(s)) return;
                s = s.Trim();
                if (s.Length == 0 || !seen.Add(s)) return;
                parts.Add(s);
            }

            var parentSlot = option.GetComponentInParent<WorldSlot>();
            if (parentSlot != null && parentSlot.number != null)
            {
                AddPart(ResolvePugText(parentSlot.number));
                seenTexts.Add(parentSlot.number);
            }

            if (option.labelText != null)
            {
                AddPart(ResolvePugText(option.labelText));
                seenTexts.Add(option.labelText);
            }
            if (option.valueText != null)
            {
                AddPart(ResolvePugText(option.valueText));
                seenTexts.Add(option.valueText);
            }

            foreach (var t in option.GetComponentsInChildren<PugText>(false))
            {
                if (t == null || !seenTexts.Add(t)) continue;
                AddPart(ResolvePugText(t));
            }

            foreach (var t in GetReflectedPugTexts(option, seenTexts))
            {
                AddPart(ResolvePugText(t));
            }

            if (IconOnlyOptionKeys.TryGetValue(option.GetType().Name, out var i18nKey))
            {
                var translated = Strings.L(i18nKey);
                if (!string.IsNullOrEmpty(translated) && translated != i18nKey)
                    AddPart(translated);
            }

            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        private static IEnumerable<PugText> GetReflectedPugTexts(RadicalMenuOption option, HashSet<PugText> alreadySeen)
        {
            var type = option.GetType();
            while (type != null && type != typeof(object))
            {
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (!typeof(PugText).IsAssignableFrom(field.FieldType)) continue;
                    var name = field.Name;
                    if (name.IndexOf("shadow", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    var value = field.GetValue(option) as PugText;
                    if (value == null || !alreadySeen.Add(value)) continue;
                    if (value.gameObject == null || !value.gameObject.activeInHierarchy) continue;
                    yield return value;
                }
                type = type.BaseType;
            }
        }

        public static string FindMenuTitle(RadicalMenu menu)
        {
            if (menu == null) return null;

            var excluded = new HashSet<PugText>();
            foreach (var opt in menu.menuOptions)
            {
                if (opt == null) continue;
                if (opt.labelText != null) excluded.Add(opt.labelText);
                if (opt.valueText != null) excluded.Add(opt.valueText);
                foreach (var t in opt.GetComponentsInChildren<PugText>(true))
                    if (t != null) excluded.Add(t);
            }

            foreach (var t in menu.GetComponentsInChildren<PugText>(false))
            {
                if (t == null || excluded.Contains(t)) continue;
                var resolved = ResolvePugText(t);
                if (!string.IsNullOrEmpty(resolved)) return resolved;
            }
            return null;
        }

        private static string ResolvePugText(PugText text)
        {
            return TtsText.ResolvePugText(text);
        }
    }

    [HarmonyPatch(typeof(RadicalMenuOption), nameof(RadicalMenuOption.OnSelected))]
    internal static class RadicalMenuOptionOnSelectedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(RadicalMenuOption __instance)
        {
            MenuTtsCore.AnnounceOption(__instance, force: false);
        }
    }

    [HarmonyPatch(typeof(RadicalMenuOption), nameof(RadicalMenuOption.OnSkimLeft))]
    internal static class RadicalMenuOptionOnSkimLeftPatch
    {
        [HarmonyPostfix]
        public static void Postfix(RadicalMenuOption __instance)
        {
            MenuTtsCore.AnnounceOption(__instance, force: true);
        }
    }

    [HarmonyPatch(typeof(RadicalMenuOption), nameof(RadicalMenuOption.OnSkimRight))]
    internal static class RadicalMenuOptionOnSkimRightPatch
    {
        [HarmonyPostfix]
        public static void Postfix(RadicalMenuOption __instance)
        {
            MenuTtsCore.AnnounceOption(__instance, force: true);
        }
    }

    // En mode edition de texte (nom de monde/perso), le jeu n'accepte que Entree
    // clavier pour valider : le bouton de validation manette n'est pas gere du tout
    // (HandleTypingInput avale tout l'input menu). On ajoute Croix = Entree. Lecture
    // du bouton PHYSIQUE (id 6, pattern TeleportNavigator) et pas de l'action Rewired
    // menu-interact : celle-ci peut etre bindee sur Espace clavier, qui doit rester
    // un caractere tapable.
    [HarmonyPatch(typeof(MenuManager), "HandleTypingInput")]
    internal static class TypingCrossValidatePatch
    {
        private const int CrossButton = 6;

        // Clavier virtuel manette (overlay Steam...) affiche -> ne pas interferer.
        private static readonly FieldInfo ShowingControllerInput =
            AccessTools.Field(typeof(MenuManager), "isShowingControllerTextInput");

        [HarmonyPostfix]
        public static void Postfix(MenuManager __instance, bool __result)
        {
            if (!__result) return; // pas en mode edition
            var field = Manager.input != null ? Manager.input.activeInputField : null;
            if (field == null) return;
            if (ShowingControllerInput != null && (bool)ShowingControllerInput.GetValue(__instance)) return;
            if (!CrossPressedThisFrame()) return;

            field.Deactivate(true);
            // Meme garde-fou que TrySetInputText : evite que l'appui soit retraite
            // par le menu juste apres la fermeture du champ.
            Manager.input.StartMenuActivationInputCooldown();
        }

        private static bool CrossPressedThisFrame()
        {
            if (!ReInput.isReady) return false;
            var joy = ReInput.controllers.GetLastActiveController<Joystick>();
            if (joy == null) return false;
            for (int i = 0; i < joy.buttonCount; i++)
                if (joy.ButtonElementIdentifiers[i].id == CrossButton)
                    return joy.GetButtonDown(i);
            return false;
        }
    }

    // Annonce l'entree en mode edition + le contenu courant du champ. interrupt:false :
    // a l'ouverture du menu creation de perso le champ est auto-active pendant que le
    // titre du menu s'annonce, on se met en file plutot que d'ecraser.
    [HarmonyPatch(typeof(RadicalMenuOptionTextInput), nameof(RadicalMenuOptionTextInput.OnActivated))]
    internal static class TextInputOnActivatedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(RadicalMenuOptionTextInput __instance)
        {
            if (__instance.readOnly) return;
            var current = __instance.GetInputText();
            if (string.IsNullOrEmpty(current)) current = Strings.L("edit.empty");
            TtsText.Say(Strings.L("edit.open") + ", " + current, false);
        }
    }

    // Annonce la sortie du mode edition : valide (avec le texte retenu) ou annule.
    [HarmonyPatch(typeof(RadicalMenuOptionTextInput), nameof(RadicalMenuOptionTextInput.Deactivate))]
    internal static class TextInputDeactivatePatch
    {
        [HarmonyPostfix]
        public static void Postfix(RadicalMenuOptionTextInput __instance, bool commit)
        {
            if (__instance.readOnly) return;
            if (!commit)
            {
                TtsText.Say(Strings.L("edit.cancelled"), true);
                return;
            }
            var text = __instance.GetInputText();
            if (string.IsNullOrEmpty(text)) text = Strings.L("edit.empty");
            TtsText.Say(Strings.L("edit.confirmed") + ", " + text, true);
        }
    }

    [HarmonyPatch(typeof(RadicalMenu), nameof(RadicalMenu.Activate))]
    internal static class RadicalMenuActivatePatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            MenuTtsState.SuppressDuringActivate = true;
        }

        [HarmonyPostfix]
        public static void Postfix(RadicalMenu __instance)
        {
            MenuTtsState.SuppressDuringActivate = false;
            MenuTtsState.LastInstanceId = 0;

            var title = MenuTtsCore.FindMenuTitle(__instance);
            var option = __instance.GetSelectedMenuOption();
            var optionText = MenuTtsCore.BuildAnnouncementPublic(option);

            string announcement;
            if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(optionText))
                announcement = title + ". " + optionText;
            else if (!string.IsNullOrEmpty(title))
                announcement = title;
            else
                announcement = optionText;

            if (string.IsNullOrEmpty(announcement)) return;

            if (option != null) MenuTtsState.LastInstanceId = option.GetInstanceID();
            TtsText.Say(announcement, true);
        }
    }

    // Choix du mode de personnage (Normal/Casual/Hardcore) : l'ecran est UN selecteur
    // carrousel (gauche/droite) + un bouton Termine — pas trois options. Le jeu cable
    // Croix sur le selecteur pour AVANCER le carrousel (OnActivated appelle OnSkimRight) :
    // le joueur croit valider et change le mode sans le savoir. On neutralise ce
    // defilement et on annonce a la place le mode courant + la marche a suivre.
    [HarmonyPatch(typeof(CharacterTypeOption_Selection), nameof(CharacterTypeOption_Selection.OnActivated))]
    internal static class CharacterTypeActivatePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(CharacterTypeOption_Selection __instance)
        {
            var current = TtsText.ResolvePugText(__instance.typeText);
            var help = Strings.L("chartype.help");
            TtsText.Say(string.IsNullOrEmpty(current) ? help : current + ", " + help, true);
            return false; // Croix ne defile plus le carrousel
        }
    }

    // TTS de la cinematique d'intro de nouveau perso (et de l'outro de fin, meme
    // classe) : diaporama IntroHandler, chaque slide porte une cle I2 affichee
    // syllabe par syllabe. On annonce l'entree (sinon rien n'indique qu'une
    // cinematique tourne ni comment la passer), puis le texte de chaque slide.
    [HarmonyPatch(typeof(IntroHandler), "Start")]
    internal static class IntroHandlerStartPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            TtsText.Say(Strings.L("cinematic.start"), false);
        }
    }

    // StartText est appele chaque frame de fondu mais ne rend le texte qu'une fois
    // (garde privee textStarted) : on capture la garde avant/apres pour n'annoncer
    // que le vrai demarrage du texte d'un slide.
    [HarmonyPatch(typeof(IntroHandler), "StartText")]
    internal static class IntroHandlerStartTextPatch
    {
        private static readonly FieldInfo TextStarted =
            AccessTools.Field(typeof(IntroHandler), "textStarted");
        private static readonly FieldInfo SlideIndex =
            AccessTools.Field(typeof(IntroHandler), "currentSlideIndex");

        [HarmonyPrefix]
        public static void Prefix(IntroHandler __instance, out bool __state)
        {
            __state = TextStarted != null && (bool)TextStarted.GetValue(__instance);
        }

        [HarmonyPostfix]
        public static void Postfix(IntroHandler __instance, bool __state)
        {
            if (__state) return; // texte deja demarre avant cet appel
            if (TextStarted == null || SlideIndex == null) return;
            if (!(bool)TextStarted.GetValue(__instance)) return;

            int idx = (int)SlideIndex.GetValue(__instance);
            var slides = __instance.slidesList;
            if (slides == null || idx < 0 || idx >= slides.Count) return;
            var key = slides[idx].text;
            if (string.IsNullOrEmpty(key)) return;

            string resolved;
            try { resolved = PugText.ProcessText(key, null, true, false); }
            catch { resolved = null; }
            if (string.IsNullOrEmpty(resolved)
                || resolved.StartsWith("missing:", System.StringComparison.OrdinalIgnoreCase)) return;
            TtsText.Say(resolved, true);
        }
    }

    // Skip (Croix maintenue 1 s) : confirme que le maintien a marche et coupe la
    // lecture du slide en cours, devenue caduque.
    [HarmonyPatch(typeof(IntroHandler), "SkipIntro")]
    internal static class IntroHandlerSkipPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            TtsText.Say(Strings.L("cinematic.skipped"), true);
        }
    }
}
