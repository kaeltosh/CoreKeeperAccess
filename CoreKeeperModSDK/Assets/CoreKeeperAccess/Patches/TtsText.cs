using System;
using System.Text.RegularExpressions;
using DavyKager;
using PugMod;
using UnityEngine;

namespace CoreKeeperAccess.Patches
{
    // Resolution partagee de texte localise pour le TTS (menus + in-game).
    internal static class TtsText
    {
        private static readonly Regex UnsubstitutedPlaceholder = new Regex(@"\{\d+\}", RegexOptions.Compiled);

        // Sortie TTS centralisee : annonce via Tolk ET trace dans Player.log
        // (prefixe [A11yTTS]) pour diagnostiquer tout ce qui est reellement lu.
        public static void Say(string text, bool interrupt)
        {
            if (string.IsNullOrEmpty(text)) return;
            Tolk.Output(text, interrupt);
            Debug.Log("[A11yTTS] " + text);
        }

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

            var main = Clean(ProcessTaf(taf, !taf.dontLocalizeFormatFields), taf.text);
            // Les champs numeriques des stats (degats, nourriture...) ne sont pas des
            // termes I2 : si les localiser a produit des <missing>, on refait la
            // substitution en gardant les champs bruts.
            if (main != null && main.Contains("<missing>"))
            {
                var alt = Clean(ProcessTaf(taf, false), taf.text);
                if (!string.IsNullOrEmpty(alt) && !alt.Contains("<missing>")) main = alt;
            }

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

        // Substitution localisee, avec controle de la localisation des champs de format.
        private static string ProcessTaf(TextAndFormatFields taf, bool localizeFormatFields)
        {
            try { return PugText.ProcessText(taf.text, taf.formatFields, !taf.dontLocalize, localizeFormatFields); }
            catch { return taf.dontLocalize ? taf.text : API.Localization?.GetLocalizedTerm(taf.text); }
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
