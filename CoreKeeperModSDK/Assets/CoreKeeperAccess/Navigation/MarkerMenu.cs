using System.Collections.Generic;
using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Localization;
using Unity.Mathematics;

namespace CoreKeeperAccess.Navigation
{
    // Menu contextuel d'un marqueur de carte (POI ou balise) : Croix ouvre une liste d'ACTIONS
    // de guidage (reseau / direct), a laquelle une balise ajoute renommer / supprimer. Passe par
    // ActionMenu (donc le moteur TreeMenu partage). Libere le moteur de keymaps : plus de combo
    // Triangle par action.
    internal static class MarkerMenu
    {
        // Cas POI : guidage seul (titre du marqueur resolu en interne).
        public static void OpenGuide(MapMarkerUIElement m)
        {
            if (m == null || !MapMarkerUtil.MarkerTile(m, out int2 tile)) return;
            Open(MapMarkerUtil.MarkerTitle(m), tile, null);
        }

        // Cas general : guidage (reseau / direct) + actions supplementaires (renommer / supprimer
        // pour une balise). name peut etre vide -> repli "marqueur".
        public static void Open(string name, int2 tile, List<ActionMenu.Item> extra)
        {
            if (string.IsNullOrEmpty(name)) name = Strings.L("map.poi.marker");
            var items = new List<ActionMenu.Item>
            {
                new ActionMenu.Item { Label = Strings.L("ctx.navigate"), Run = () => MapMarkerUtil.StartGuide(tile, name, true) },
                new ActionMenu.Item { Label = Strings.L("ctx.direct"),   Run = () => MapMarkerUtil.StartGuide(tile, name, false) },
            };
            if (extra != null) items.AddRange(extra);
            ActionMenu.Open(Strings.L("ctx.title") + ", " + name, items);
        }
    }
}
