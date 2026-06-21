using System.Collections.Generic;
using System.Reflection;
using CoreKeeperAccess.Localization;
using HarmonyLib;
using Rewired;
using UnityEngine;

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
            { "WorldInfoExportOption", "worldinfo.export" },
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

            // Selecteur d'icone de monde : aucun PugText, juste un sprite -> muet en
            // l'etat. On synthetise un index lisible (les icones n'ont pas de nom).
            if (option is SelectWorldIconOption iconOpt)
            {
                var iconLabel = Strings.L("worldinfo.icon");
                int iconCount = iconOpt.worldInfoTable != null ? iconOpt.worldInfoTable.worldIcons.Count : 0;
                AddPart(iconCount > 0
                    ? iconLabel + " " + (iconOpt.activeIconIndex + 1) + " / " + iconCount
                    : iconLabel);
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

            // Dernier recours pour un bouton-icone sans aucun texte (bandeau "ID de
            // jeu" du menu pause : rafraichir / copier / masquer...) : son libelle vit
            // dans le texte d'info partage PauseMenuIconSelectionInfoText, hors de la
            // hierarchie de l'option -> on le retrouve par correspondance d'option.
            if (parts.Count == 0)
                AddPart(ResolveIconInfoTerm(option));

            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        // Cherche, dans les bandeaux d'icones du menu pause, le terme descriptif (on/off)
        // associe a cette option. Chemin froid (seulement si l'option n'a aucun texte).
        private static string ResolveIconInfoTerm(RadicalMenuOption option)
        {
            var infos = Object.FindObjectsByType<PauseMenuIconSelectionInfoText>(FindObjectsSortMode.None);
            foreach (var info in infos)
            {
                if (info == null || info.options == null) continue;
                foreach (var entry in info.options)
                {
                    if (entry.option != option) continue;
                    bool on = true;
                    try { on = option.IsOn(); } catch { }
                    var term = on ? entry.on : entry.off;
                    var key = term != null ? term.mTerm : null;
                    if (string.IsNullOrEmpty(key)) return null;
                    try
                    {
                        var r = PugText.ProcessText(key, null, true, false);
                        if (!string.IsNullOrEmpty(r)
                            && !r.StartsWith("missing:", System.StringComparison.OrdinalIgnoreCase))
                            return r.Trim();
                    }
                    catch { }
                    return null;
                }
            }
            return null;
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
            => PatchGuard.Run("A11yMenuOnSelected", () => MenuTtsCore.AnnounceOption(__instance, force: false));
    }

    [HarmonyPatch(typeof(RadicalMenuOption), nameof(RadicalMenuOption.OnSkimLeft))]
    internal static class RadicalMenuOptionOnSkimLeftPatch
    {
        [HarmonyPostfix]
        public static void Postfix(RadicalMenuOption __instance)
            => PatchGuard.Run("A11yMenuOnSkimLeft", () => MenuTtsCore.AnnounceOption(__instance, force: true));
    }

    [HarmonyPatch(typeof(RadicalMenuOption), nameof(RadicalMenuOption.OnSkimRight))]
    internal static class RadicalMenuOptionOnSkimRightPatch
    {
        [HarmonyPostfix]
        public static void Postfix(RadicalMenuOption __instance)
            => PatchGuard.Run("A11yMenuOnSkimRight", () => MenuTtsCore.AnnounceOption(__instance, force: true));
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
        public static void Postfix(MenuManager __instance, bool __result) => PatchGuard.Run("A11yTypingCross", () =>
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
        });

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
        public static void Postfix(RadicalMenuOptionTextInput __instance) => PatchGuard.Run("A11yEditOpen", () =>
        {
            if (__instance.readOnly) return;
            var current = __instance.GetInputText();
            if (string.IsNullOrEmpty(current)) current = Strings.L("edit.empty");
            TtsText.Say(Strings.L("edit.open") + ", " + current, false);
        });
    }

    // Annonce la sortie du mode edition : valide (avec le texte retenu) ou annule.
    [HarmonyPatch(typeof(RadicalMenuOptionTextInput), nameof(RadicalMenuOptionTextInput.Deactivate))]
    internal static class TextInputDeactivatePatch
    {
        [HarmonyPostfix]
        public static void Postfix(RadicalMenuOptionTextInput __instance, bool commit) => PatchGuard.Run("A11yEditClose", () =>
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
        });
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
            // Remises a zero d'etat HORS du garde : assignations triviales (ne peuvent pas
            // throw) qui DOIVENT toujours passer. Sinon une exception dans la construction
            // d'annonce laisserait SuppressDuringActivate coince a true (arme par le prefix)
            // -> tous les menus suivants deviendraient muets.
            MenuTtsState.SuppressDuringActivate = false;
            MenuTtsState.LastInstanceId = 0;
            // Toute (re)activation de menu rearme l'en-tete de section "gerer les
            // joueurs" : la premiere liste survolee reannonce sa section.
            ManagePlayersState.LastListInstanceId = 0;

            PatchGuard.Run("A11yMenuActivate", () =>
            {
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
            });
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
            => PatchGuard.Run("A11yCharType", () =>
            {
                var current = TtsText.ResolvePugText(__instance.typeText);
                var help = Strings.L("chartype.help");
                TtsText.Say(string.IsNullOrEmpty(current) ? help : current + ", " + help, true);
                return false; // Croix ne defile plus le carrousel
            }, fallback: true); // exception -> laisser l'original (carrousel defile, comportement vanilla)
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
            => PatchGuard.Run("A11yIntroStart", () => TtsText.Say(Strings.L("cinematic.start"), false));
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
            // out param -> incompatible avec le lambda de PatchGuard : try/catch inline.
            // Exception -> __state=true (= "texte deja demarre") pour que le Postfix s'abstienne.
            try { __state = TextStarted != null && (bool)TextStarted.GetValue(__instance); }
            catch (System.Exception ex) { Diag.Error("A11yIntroText", ex); __state = true; }
        }

        [HarmonyPostfix]
        public static void Postfix(IntroHandler __instance, bool __state) => PatchGuard.Run("A11yIntroText", () =>
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
        });
    }

    // Skip (Croix maintenue 1 s) : confirme que le maintien a marche et coupe la
    // lecture du slide en cours, devenue caduque.
    [HarmonyPatch(typeof(IntroHandler), "SkipIntro")]
    internal static class IntroHandlerSkipPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
            => PatchGuard.Run("A11yIntroSkip", () => TtsText.Say(Strings.L("cinematic.skipped"), true));
    }

    // ----- Ecran "Gerer les joueurs" (multijoueur) -----
    // Cause racine du trou : les boutons par joueur sont des PlayerListEntryButton
    // (heritent de ButtonUIElement), classe SOEUR de RadicalMenuOption sous UIelement.
    // Notre patch standard ne hooke que RadicalMenuOption.OnSelected -> ces boutons
    // passaient a travers, l'ecran etait totalement muet a l'interieur.

    internal static class ManagePlayersState
    {
        // Instance de la liste (ListConnectedPlayers) dont on a annonce la section
        // en dernier. Rearme a 0 a chaque (re)activation de menu.
        public static int LastListInstanceId;
    }

    internal static class ManagePlayersTts
    {
        // L'etat admin vit dans le champ prive `player` (struct PlayerUIEntry).
        private static readonly FieldInfo PlayerField =
            AccessTools.Field(typeof(PlayerListEntry), "player");

        public static void Announce(PlayerListEntryButton button)
        {
            if (button == null) return;
            var entry = button.playerListEntry;
            if (entry == null) return;

            // Nom du joueur : chaine litterale, pas une cle I2 -> on lit le brut
            // (ProcessText risquerait un "missing:" sur un pseudo).
            var name = entry.realCurrentName;
            if (string.IsNullOrEmpty(name) || name == "...")
            {
                var fromText = entry.nameText != null ? entry.nameText.GetText() : null;
                if (!string.IsNullOrEmpty(fromText) && fromText != "...") name = fromText;
            }

            // Options de gestion annoncees PARTOUT ou ces boutons apparaissent : ecran
            // dedie "gerer les joueurs" ET panneau "Joueurs connectes" du menu pause
            // (comportement choisi par l'utilisateur).
            var list = entry.listConnectedPlayers;
            var listType = list != null ? list.menuType : PlayersListType.ACTIVE_PLAYERS;

            var parts = new List<string>();

            // En-tete de section quand on entre dans une nouvelle liste.
            if (list != null)
            {
                int listId = list.GetInstanceID();
                if (listId != ManagePlayersState.LastListInstanceId)
                {
                    ManagePlayersState.LastListInstanceId = listId;
                    // Titre exact affiche a l'ecran (« Joueurs administrateurs »,
                    // « Joueurs exclus »...) plutot qu'un libelle devine.
                    var section = TtsText.ResolvePugText(list.labelText);
                    if (!string.IsNullOrEmpty(section)) parts.Add(section);
                }
            }

            if (!string.IsNullOrEmpty(name)) parts.Add(name);

            // Action : identifiee par reference (l'enum buttonType ne couvre pas les 5
            // boutons), avec le sens contextuel (bannir/debannir, donner/retirer admin).
            var action = ActionLabel(button, entry, listType);
            if (!string.IsNullOrEmpty(action)) parts.Add(action);

            var msg = TtsText.Compose(parts);
            if (!string.IsNullOrEmpty(msg)) TtsText.Say(msg, true);
        }

        private static string ActionLabel(PlayerListEntryButton button, PlayerListEntry entry, PlayersListType listType)
        {
            if (button == entry.banButton)
                return Strings.L(listType == PlayersListType.UNBAN ? "manageplayers.unban" : "manageplayers.ban");
            if (button == entry.inviteButton)
                return Strings.L("manageplayers.invite");
            if (button == entry.showPlayerInfoButton)
                return Strings.L("manageplayers.profile");
            if (button == entry.pvpTeamButton)
                return Strings.L("manageplayers.pvpteam");
            if (button == entry.adminButton)
                return Strings.L(IsAdmin(entry) ? "manageplayers.admin.remove" : "manageplayers.admin.make");
            return null;
        }

        private static bool IsAdmin(PlayerListEntry entry)
        {
            if (PlayerField == null) return false;
            try
            {
                if (PlayerField.GetValue(entry) is ListConnectedPlayers.PlayerUIEntry p)
                    return p.isAdmin;
            }
            catch { }
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerListEntryButton), nameof(PlayerListEntryButton.OnSelected))]
    internal static class PlayerListEntryButtonOnSelectedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PlayerListEntryButton __instance)
            => PatchGuard.Run("A11yManagePlayers", () => ManagePlayersTts.Announce(__instance));
    }

    // ----- Pop-ups de confirmation (PopUpText) -----
    // Les dialogues oui/annuler (bannir, admin, inviter, abandonner les changements...)
    // et les avis transitoires passent tous par Manager.menu.centerPopUpText, jamais
    // lus jusqu'ici. On lit la question + les libelles d'options dans une seule phrase :
    // le menu d'options s'ouvre AVANT que le texte de question soit pose, donc son
    // annonce serait ecrasee par la notre (postfix, en dernier, interrupt).
    [HarmonyPatch(typeof(PopUpText), nameof(PopUpText.StartNewDisplaySequence))]
    internal static class PopUpStartPatch
    {
        private static string _lastText;
        private static float _lastTime;

        [HarmonyPostfix]
        public static void Postfix(PopUpText __instance, List<string> options) => PatchGuard.Run("A11yPopUp", () =>
        {
            var question = TtsText.ResolvePugText(__instance.pugText);
            if (string.IsNullOrEmpty(question)) return;

            // Un appel supprime par la garde de priorite laisse le pugText inchange et
            // peut se repeter chaque frame : on etouffe le meme texte sur une fenetre
            // courte (temps non mis a l'echelle car le jeu peut etre en pause).
            if (question == _lastText && (Time.unscaledTime - _lastTime) < 0.5f) return;
            _lastText = question;
            _lastTime = Time.unscaledTime;

            var parts = new List<string> { question };
            if (options != null)
            {
                foreach (var key in options)
                {
                    var label = ResolveKey(key);
                    if (!string.IsNullOrEmpty(label)) parts.Add(label);
                }
            }

            var msg = TtsText.Compose(parts);
            if (!string.IsNullOrEmpty(msg)) TtsText.Say(msg, true);
        });

        private static string ResolveKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            try
            {
                var r = PugText.ProcessText(key, null, true, false);
                if (string.IsNullOrEmpty(r)) return key;
                if (r.StartsWith("missing:", System.StringComparison.OrdinalIgnoreCase)) return null;
                return r.Trim();
            }
            catch { return key; }
        }
    }
}
