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
        private static readonly Regex RichTextTag = new Regex(@"<[^<>]+>", RegexOptions.Compiled);

        // Sortie TTS centralisee : annonce via Tolk ET trace dans Player.log
        // (prefixe [A11yTTS], timecode pour correler avec les erreurs [A11y*Diag])
        // pour diagnostiquer tout ce qui est reellement lu.
        public static void Say(string text, bool interrupt)
        {
            if (string.IsNullOrEmpty(text)) return;
            // Garde-fou de balisage : aucun tag rich-text (<color>, <sprite>, <missing>...)
            // ne doit atteindre la synthese, NVDA les epellerait. On strippe ET on trace
            // (deduplique par tag) pour remonter au site fautif au lieu de masquer.
            if (text.IndexOf('<') >= 0 && RichTextTag.IsMatch(text))
            {
                var tag = RichTextTag.Match(text).Value;
                Diag.Warn("A11yTTS", tag, "balisage strippe avant TTS, tag '" + tag + "' dans : " + text);
                text = RichTextTag.Replace(text, "").Trim();
                if (text.Length == 0) return;
            }
            Tolk.Output(text, interrupt);
            Debug.Log("[A11yTTS] " + Diag.Stamp() + " " + text);
        }

        // Assemble un message TTS depuis des morceaux : saute les vides, trim,
        // deduplique les repetitions consecutives, joint par ", ". Null si rien.
        // Socle commun de composition (les nouveaux codes l'utilisent d'office,
        // les anciens migrent au passage).
        public static string Compose(System.Collections.Generic.IReadOnlyList<string> parts)
        {
            if (parts == null) return null;
            string result = null;
            string prev = null;
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                if (string.IsNullOrEmpty(p)) continue;
                var t = p.Trim();
                if (t.Length == 0 || t == prev) continue;
                prev = t;
                result = result == null ? t : result + ", " + t;
            }
            return result;
        }

        public static string Compose(params string[] parts) =>
            Compose((System.Collections.Generic.IReadOnlyList<string>)parts);

        // Auto-verifications du composeur, executees au boot en mode dev seulement :
        // le contrat est simple mais sert de socle reutilisable, on le verrouille.
        public static void SelfTest()
        {
            AssertEq(Compose("a", null, "", "b"), "a, b");
            AssertEq(Compose("a", "a", "b"), "a, b");
            AssertEq(Compose(null, ""), null);
            AssertEq(Compose(" a ", "b "), "a, b");
            AssertEq(Compose("a", "b", "a"), "a, b, a"); // seuls les CONSECUTIFS dedupliquent
        }

        private static void AssertEq(string got, string want)
        {
            if (got != want)
                Debug.LogError("[A11yComposeDiag] " + Diag.Stamp()
                    + " SelfTest : attendu '" + want + "', obtenu '" + got + "'");
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
                main = Compose(main, add);
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
