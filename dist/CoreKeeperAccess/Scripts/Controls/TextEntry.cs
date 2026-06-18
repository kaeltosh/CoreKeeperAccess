using System;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using UnityEngine;

namespace CoreKeeperAccess.Controls
{
    // Editeur de texte clavier MAISON (aucun champ UI du jeu, aucun asset Unity). Meme
    // mecanique que le jeu (MenuManager.HandleTypingInput) : Input.inputString pour les
    // caracteres, GetKeyDown pour Retour arriere / Entree / Echap. Sert a (re)nommer une
    // balise sur la carte, ou aucun activeInputField natif n'existe. NVDA fait l'echo des
    // frappes physiques lui-meme : on n'annonce que les bornes (ouverture, validation,
    // annulation).
    internal static class TextEntry
    {
        private static bool _active;
        private static string _text = "";
        private static Action<string> _onCommit;
        private static int _maxLen;

        public static bool Active => _active;

        public static void Begin(string promptKey, string initial, int maxLen, Action<string> onCommit)
        {
            _active = true;
            _text = initial ?? "";
            _onCommit = onCommit;
            _maxLen = maxLen;
            string body = string.IsNullOrEmpty(_text) ? Strings.L("edit.empty") : _text;
            TtsText.Say(Strings.L(promptKey) + ", " + body, true);
        }

        private static void Cancel()
        {
            _active = false; _onCommit = null;
            TtsText.Say(Strings.L("edit.cancelled"), true);
        }

        private static void Commit()
        {
            _active = false;
            var cb = _onCommit; _onCommit = null;
            string t = (_text ?? "").Trim();
            TtsText.Say(Strings.L("edit.confirmed") + ", "
                + (string.IsNullOrEmpty(t) ? Strings.L("edit.empty") : t), true);
            cb?.Invoke(t);
        }

        // A ticker tot dans l'Update du mod, AVANT les consommateurs de carte / D-pad.
        public static void Tick()
        {
            if (!_active) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Cancel(); return; }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) { Commit(); return; }
            if (Input.GetKeyDown(KeyCode.Backspace))
            {
                if (_text.Length > 0) _text = _text.Substring(0, _text.Length - 1);
                return;
            }
            string s = Input.inputString;
            if (string.IsNullOrEmpty(s)) return;
            foreach (char c in s)
            {
                if (c == '\b' || c == '\n' || c == '\r') continue; // geres ci-dessus
                if (_text.Length >= _maxLen) break;
                _text += c;
            }
        }
    }
}
