using System.Collections.Generic;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;

namespace CoreKeeperAccess.Controls
{
    // Menu d'AIDE (Triangle + double-tap D-pad haut) : liste navigable de TOUTES les
    // commandes du mod disponibles dans le contexte courant (source : HelpCatalog).
    // Reutilise ActionMenu (deja branche sur les earcons de nav unifies UiSfx) -> on
    // parcourt au D-pad ; Croix EXECUTE une commande ponctuelle, ou RELIT une entree
    // descriptive (geste continu : nav, laser...). Chaque entree : "<geste> : <action>".
    //
    // Extension prevue (cf. pending) : les commandes VANILLA du jeu (mapping Rewired), et
    // plus tard un onglet "apprentissage des sons".
    internal static class HelpMenu
    {
        private static readonly List<HelpItem> _buf = new List<HelpItem>(24);

        public static void Show()
        {
            HelpCatalog.Collect(_buf);
            if (_buf.Count == 0) { TtsText.Say(Strings.L("help.none"), true); return; }

            var items = new List<ActionMenu.Item>(_buf.Count);
            foreach (var h in _buf)
            {
                // Geste vide (entree meta, ex. apprendre la manette) -> libelle seul.
                string label = string.IsNullOrEmpty(h.Gesture) ? h.Label : h.Gesture + " : " + h.Label;
                var run = h.Run; // capture pour la closure (null = entree descriptive)
                items.Add(new ActionMenu.Item { Label = label, Run = run });
            }
            ActionMenu.Open(Strings.L("help.title"), items);
        }
    }
}
