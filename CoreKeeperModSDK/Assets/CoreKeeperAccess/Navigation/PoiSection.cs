using System.Collections.Generic;
using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using Unity.Mathematics;

namespace CoreKeeperAccess.Navigation
{
    // Section POINTS D'INTERET : tous les marqueurs de carte qui ne sont ni relais/waypoint ni
    // balise joueur (boss scannes, scenes uniques, tombe, pings...). Pas teleportables : Croix
    // ouvre un menu d'ACTIONS de guidage (reseau / direct). Tries du plus proche AU JOUEUR (pas
    // de numerotation stable : c'est la pertinence immediate qui compte).
    internal sealed class PoiSection : MarkerListSection
    {
        private readonly List<MapMarkerUIElement> _pois = new List<MapMarkerUIElement>();

        public override string TitleKey => "map.poi";
        public override string EmptyKey => "map.poi.none";

        public override void Rebuild(List<MapMarkerUIElement> scanned)
        {
            _pois.Clear();
            foreach (var m in scanned)
            {
                if (m.markerType == MapMarkerType.UserPlacedMarker) continue;
                if (m.markerType == MapMarkerType.Portal || m.markerType == MapMarkerType.Waypoint) continue;
                _pois.Add(m);
            }
            float2 pp = MapMarkerUtil.PlayerPos();
            _pois.Sort((a, b) => MapMarkerUtil.DistSq(a, pp).CompareTo(MapMarkerUtil.DistSq(b, pp)));
        }

        public override void Clear() => _pois.Clear();

        protected override int RowCount => _pois.Count;

        protected override void AnnounceRow(int idx, bool interrupt)
        {
            if (idx < 0 || idx >= _pois.Count) return;
            var m = _pois[idx];
            MapMarkerUtil.FocusMarker(m);
            string name = MapMarkerUtil.MarkerTitle(m);
            if (string.IsNullOrEmpty(name)) name = Strings.L("map.poi.marker");
            string head = (idx + 1) + ", " + name;
            string dd = MapMarkerUtil.DirectionAndDistance(m);
            TtsText.Say(string.IsNullOrEmpty(dd) ? head : head + ", " + dd, interrupt);
        }

        protected override void ConfirmRow(int idx)
        {
            if (idx < 0 || idx >= _pois.Count) return;
            MarkerMenu.OpenGuide(_pois[idx]);
        }

        protected override void DetailRow(int idx)
        {
            if (idx < 0 || idx >= _pois.Count) return;
            var m = _pois[idx];
            string label = MapMarkerUtil.MarkerTitle(m);
            if (string.IsNullOrEmpty(label)) label = Strings.L("map.poi.marker");
            string text = (idx + 1) + ", " + label;
            if (MapMarkerUtil.TryMarkerPos(m, out float2 mp))
            {
                text += ", " + Strings.L("teleport.position") + " "
                      + (int)math.round(mp.x) + ", " + (int)math.round(mp.y);
                string biome = MapMarkerUtil.ResolveBiome(mp);
                if (!string.IsNullOrEmpty(biome))
                    text += ", " + Strings.L("teleport.biome") + " " + biome;
            }
            string dd = MapMarkerUtil.DirectionAndDistance(m);
            if (!string.IsNullOrEmpty(dd)) text += ", " + dd;
            TtsText.Say(text, true);
        }
    }
}
