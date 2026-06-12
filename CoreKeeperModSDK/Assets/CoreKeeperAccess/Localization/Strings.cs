using System.Collections.Generic;
using System.Text;
using I2.Loc;
using Newtonsoft.Json;
using PugMod;
using UnityEngine;

namespace CoreKeeperAccess.Localization
{
    public static class Strings
    {
        private const string ModName = "CoreKeeperAccess";
        private const string FallbackLanguageCode = "en";
        private const string JsonPathPattern = "Conf/Localization/{0}.json";

        private static Dictionary<string, string> _current = new Dictionary<string, string>();
        private static Dictionary<string, string> _fallback = new Dictionary<string, string>();
        private static string _loadedLanguageCode;

        public static void Load()
        {
            var mod = FindSelf();
            if (mod == null)
            {
                Debug.LogWarning("[CoreKeeperAccess] " + Diag.Stamp() + " Strings.Load: mod introuvable via API.ModLoader.LoadedMods");
                return;
            }

            _fallback = ReadJson(mod, FallbackLanguageCode) ?? new Dictionary<string, string>();
            ReloadCurrent(mod);

            LocalizationManager.OnLocalizeEvent -= HandleLanguageChange;
            LocalizationManager.OnLocalizeEvent += HandleLanguageChange;
        }

        public static void Unload()
        {
            LocalizationManager.OnLocalizeEvent -= HandleLanguageChange;
            _current.Clear();
            _fallback.Clear();
            _loadedLanguageCode = null;
        }

        public static string L(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            if (_current.TryGetValue(key, out var translated)) return translated;
            if (_fallback.TryGetValue(key, out var en)) return en;
            return key;
        }

        // Variante optionnelle : vrai seulement si la cle existe (L rend la cle brute
        // sinon, inutilisable pour une table de surcharge facultative comme obj.*).
        public static bool TryL(string key, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(key)) return false;
            if (_current.TryGetValue(key, out value)) return true;
            return _fallback.TryGetValue(key, out value);
        }

        private static void HandleLanguageChange()
        {
            var mod = FindSelf();
            if (mod != null) ReloadCurrent(mod);
        }

        private static LoadedMod FindSelf()
        {
            var loader = API.ModLoader;
            if (loader == null) return null;
            foreach (var m in loader.LoadedMods)
            {
                if (m?.Metadata != null && m.Metadata.name == ModName) return m;
            }
            return null;
        }

        private static void ReloadCurrent(LoadedMod mod)
        {
            var code = LocalizationManager.CurrentLanguageCode;
            if (string.IsNullOrEmpty(code)) code = FallbackLanguageCode;
            if (code == _loadedLanguageCode) return;

            _current = ReadJson(mod, code) ?? _fallback;
            _loadedLanguageCode = code;
        }

        private static Dictionary<string, string> ReadJson(LoadedMod mod, string languageCode)
        {
            var path = string.Format(JsonPathPattern, languageCode);
            byte[] bytes = mod.GetFile(path);
            if (bytes == null || bytes.Length == 0) return null;

            try
            {
                var json = Encoding.UTF8.GetString(bytes);
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            }
            catch (JsonException ex)
            {
                Debug.LogWarning($"[CoreKeeperAccess] {Diag.Stamp()} JSON invalide pour {languageCode}: {ex.Message}");
                return null;
            }
        }
    }
}
