using System;
using System.Text.RegularExpressions;
using PugMod;

namespace CoreKeeperAccess.Patches
{
    // Resolution partagee de texte localise pour le TTS (menus + in-game).
    internal static class TtsText
    {
        private static readonly Regex UnsubstitutedPlaceholder = new Regex(@"\{\d+\}", RegexOptions.Compiled);

        // Resout un PugText deja rendu en texte affiche, ou null si vide / non resolu.
        public static string ResolvePugText(PugText text)
        {
            if (text == null) return null;
            var raw = text.GetText();
            if (string.IsNullOrEmpty(raw)) return null;

            string result;
            try { result = text.ProcessText(raw); }
            catch { result = API.Localization?.GetLocalizedTerm(raw); }

            return Clean(result, raw);
        }

        // Resout un TextAndFormatFields (terme I2 + champs de substitution {0}, {1}...)
        // en texte affiche, additionalText inclus. Null si vide / non resolu.
        public static string ResolveTextAndFormatFields(TextAndFormatFields taf)
        {
            if (taf == null || string.IsNullOrEmpty(taf.text)) return null;

            string result;
            try
            {
                result = PugText.ProcessText(taf.text, taf.formatFields, !taf.dontLocalize, !taf.dontLocalizeFormatFields);
            }
            catch
            {
                result = taf.dontLocalize ? taf.text : API.Localization?.GetLocalizedTerm(taf.text);
            }

            var main = Clean(result, taf.text);

            if (!string.IsNullOrEmpty(taf.additionalText))
            {
                string add;
                try { add = PugText.ProcessText(taf.additionalText, null, !taf.dontLocalize, false); }
                catch { add = taf.additionalText; }
                add = Clean(add, taf.additionalText);
                if (!string.IsNullOrEmpty(add))
                    main = string.IsNullOrEmpty(main) ? add : main + ", " + add;
            }

            return main;
        }

        // Filtre les resultats inexploitables (placeholder non substitue, terme I2 manquant).
        private static string Clean(string result, string raw)
        {
            if (string.IsNullOrEmpty(result)) result = raw;
            if (string.IsNullOrEmpty(result)) return null;
            if (UnsubstitutedPlaceholder.IsMatch(result)) return null;
            if (result.StartsWith("missing:", StringComparison.OrdinalIgnoreCase)) return null;
            var trimmed = result.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }
    }
}
