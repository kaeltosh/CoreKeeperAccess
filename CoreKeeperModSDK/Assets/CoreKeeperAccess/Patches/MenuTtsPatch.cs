using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using CoreKeeperAccess.Localization;
using DavyKager;
using HarmonyLib;
using PugMod;
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
            Tolk.Output(announcement, true);
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

        private static readonly Regex UnsubstitutedPlaceholder = new Regex(@"\{\d+\}", RegexOptions.Compiled);

        private static string ResolvePugText(PugText text)
        {
            if (text == null) return null;
            var raw = text.GetText();
            if (string.IsNullOrEmpty(raw)) return null;

            string result;
            try { result = text.ProcessText(raw); }
            catch { result = API.Localization?.GetLocalizedTerm(raw); }

            if (string.IsNullOrEmpty(result)) result = raw;
            if (UnsubstitutedPlaceholder.IsMatch(result)) return null;
            if (result.StartsWith("missing:", System.StringComparison.OrdinalIgnoreCase)) return null;
            return result;
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
            Tolk.Output(announcement, true);
        }
    }
}
