using System.Collections.Generic;
using System.Reflection;
using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using Unity.Mathematics;

namespace CoreKeeperAccess.Navigation
{
    // Section DESTINATIONS : les relais/waypoints ou l'on peut se teleporter. Croix = clic natif
    // sur le marqueur (OnLeftClicked) = teleportation. Numerotation STABLE (tri par distance au
    // centre 0,0 -> le Core = relais 1, puis de plus en plus loin), independante de la position
    // du joueur. CanTeleport (prive) via reflection filtre les destinations atteignables.
    internal sealed class DestinationsSection : MarkerListSection
    {
        private readonly List<MapMarkerUIElement> _dests = new List<MapMarkerUIElement>();      // navigables
        private readonly List<MapMarkerUIElement> _allPortals = new List<MapMarkerUIElement>();  // tous, pour la numerotation
        private MapMarkerUIElement _coreMarker;                                                  // le plus proche de l'origine

        private static MethodInfo _canTeleport;
        private static bool _reflResolved;

        public override string TitleKey => "map.destinations";
        public override string EmptyKey => "teleport.none";

        public override void Rebuild(List<MapMarkerUIElement> scanned)
        {
            _dests.Clear();
            _allPortals.Clear();
            _coreMarker = null;
            ResolveReflection();

            foreach (var m in scanned)
            {
                if (m.markerType != MapMarkerType.Portal && m.markerType != MapMarkerType.Waypoint) continue;
                _allPortals.Add(m);
                bool ok;
                try { ok = _canTeleport != null && (bool)_canTeleport.Invoke(m, null); }
                catch { ok = false; }
                if (ok) _dests.Add(m);
            }

            _allPortals.Sort(CompareByCenterDistance);
            _dests.Sort(CompareByCenterDistance);

            float best = float.MaxValue;
            foreach (var m in _allPortals)
                if (MapMarkerUtil.TryMarkerPos(m, out float2 p) && math.lengthsq(p) < best)
                {
                    best = math.lengthsq(p);
                    _coreMarker = m;
                }
            if (best > 100f) _coreMarker = null; // aucun relais assez proche de l'origine
        }

        public override void Clear() { _dests.Clear(); _allPortals.Clear(); _coreMarker = null; }

        protected override int RowCount => _dests.Count;

        protected override void AnnounceRow(int idx, bool interrupt)
        {
            if (idx < 0 || idx >= _dests.Count) return;
            var m = _dests[idx];
            MapMarkerUtil.FocusMarker(m);
            string name = (m == _coreMarker) ? Strings.L("teleport.core") : MapMarkerUtil.MarkerTitle(m);
            if (string.IsNullOrEmpty(name)) name = Strings.L("teleport.portal");
            string head = StableNumber(m) + ", " + name;
            string dd = MapMarkerUtil.DirectionAndDistance(m);
            TtsText.Say(string.IsNullOrEmpty(dd) ? head : head + ", " + dd, interrupt);
        }

        // Croix = teleportation directe : on appelle OnLeftClicked (public) nous-memes, le
        // routage natif de Croix ne le declenche pas sur la carte. Il verifie CanTeleport, fait
        // QueueInputAction(Teleport) et ferme la carte.
        protected override void ConfirmRow(int idx)
        {
            if (idx < 0 || idx >= _dests.Count) return;
            UiSfx.Validate();
            _dests[idx].OnLeftClicked(false, false);
        }

        protected override void DetailRow(int idx)
        {
            if (idx < 0 || idx >= _dests.Count) return;
            var m = _dests[idx];
            string label = (m == _coreMarker) ? Strings.L("teleport.core")
                : (m.markerType == MapMarkerType.Waypoint
                    ? Strings.L("teleport.waypoint") : Strings.L("teleport.portal"));
            string text = StableNumber(m) + ", " + label;
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

        // Vrai s'il existe au moins une destination atteignable (pour choisir la categorie de
        // depart : destinations si on vient d'un relais, sinon points d'interet).
        public bool HasReachable => _dests.Count > 0;

        // Numero stable d'un relais = sa place dans _allPortals trie par distance au centre.
        private int StableNumber(MapMarkerUIElement m) => _allPortals.IndexOf(m) + 1;

        private static int CompareByCenterDistance(MapMarkerUIElement a, MapMarkerUIElement b)
        {
            MapMarkerUtil.TryMarkerPos(a, out float2 pa);
            MapMarkerUtil.TryMarkerPos(b, out float2 pb);
            return math.lengthsq(pa).CompareTo(math.lengthsq(pb));
        }

        private static void ResolveReflection()
        {
            if (_reflResolved) return;
            _reflResolved = true;
            _canTeleport = typeof(MapMarkerUIElement)
                .GetMethod("CanTeleport", BindingFlags.NonPublic | BindingFlags.Instance);
        }
    }
}
