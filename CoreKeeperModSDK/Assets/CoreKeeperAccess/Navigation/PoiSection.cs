using System.Collections.Generic;
using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using Unity.Mathematics;

namespace CoreKeeperAccess.Navigation
{
    // Section POINTS D'INTERET : tous les marqueurs sauf balises joueur et position du joueur.
    // Inclut Portal/Waypoint (anciens relais) pour permettre le reperage hors-relais.
    // Navigation a deux niveaux : groupes par nom → items dans le groupe.
    // Singletons : Croix = guide direct. Multi : Croix/droite = entre, gauche = remonte.
    internal sealed class PoiSection : MapSection
    {
        private struct PoiGroup
        {
            public string Name;
            public List<MapMarkerUIElement> Items;
        }

        private const int DpadRight = 17, DpadLeft = 19;

        private readonly List<PoiGroup> _groups = new List<PoiGroup>();
        private int _groupIdx;
        private int _itemIdx;
        private bool _inGroup;

        public override string TitleKey => "map.poi";
        public override string EmptyKey => "map.poi.none";
        public override bool IsEmpty => _groups.Count == 0;

        public override void Rebuild(List<MapMarkerUIElement> scanned)
        {
            _groups.Clear();
            _inGroup = false;
            _groupIdx = 0;
            _itemIdx = 0;

            float2 pp = MapMarkerUtil.PlayerPos();
            var byName = new Dictionary<string, List<MapMarkerUIElement>>();

            var clientWorld = Manager.ecs != null ? Manager.ecs.ClientWorld : null;

            foreach (var m in scanned)
            {
                if (m.markerType == MapMarkerType.UserPlacedMarker) continue;

                // Parité carte native : Portal/Waypoint/TitanShrine cachés si Hidden==true
                if (m.markerType == MapMarkerType.Portal
                    || m.markerType == MapMarkerType.Waypoint
                    || m.markerType == MapMarkerType.TitanShrine)
                {
                    if (clientWorld != null
                        && EntityUtility.HasComponentData<MapMarkerActivatedCD>(m.mapMarkerEntity, clientWorld)
                        && EntityUtility.GetComponentData<MapMarkerActivatedCD>(m.mapMarkerEntity, clientWorld).Hidden)
                        continue;
                }

                string name = MapMarkerUtil.MarkerTitle(m);
                if (string.IsNullOrEmpty(name)) name = Strings.L("map.poi.marker");

                if (!byName.ContainsKey(name))
                    byName[name] = new List<MapMarkerUIElement>();
                byName[name].Add(m);
            }

            foreach (var kv in byName)
            {
                kv.Value.Sort((a, b) =>
                    MapMarkerUtil.DistSq(a, pp).CompareTo(MapMarkerUtil.DistSq(b, pp)));
                _groups.Add(new PoiGroup { Name = kv.Key, Items = kv.Value });
            }

            _groups.Sort((a, b) =>
                MapMarkerUtil.DistSq(a.Items[0], pp).CompareTo(MapMarkerUtil.DistSq(b.Items[0], pp)));
        }

        public override void Clear()
        {
            _groups.Clear();
            _inGroup = false;
        }

        public override void Enter()
        {
            _inGroup = false;
            _groupIdx = 0;
            _itemIdx = 0;
            if (_groups.Count > 0) AnnounceGroup(0, interrupt: false);
        }

        public override void Move(int step)
        {
            if (_inGroup)
            {
                var items = _groups[_groupIdx].Items;
                if (items.Count == 0) return;
                int raw = _itemIdx + step;
                bool wrapped = raw < 0 || raw >= items.Count;
                _itemIdx = (raw + items.Count) % items.Count;
                if (wrapped) UiSfx.Cycle(); else UiSfx.Entry();
                AnnounceItem(items[_itemIdx], _itemIdx, interrupt: true);
            }
            else
            {
                if (_groups.Count == 0) return;
                int raw = _groupIdx + step;
                bool wrapped = raw < 0 || raw >= _groups.Count;
                _groupIdx = (raw + _groups.Count) % _groups.Count;
                if (wrapped) UiSfx.Cycle(); else UiSfx.Entry();
                AnnounceGroup(_groupIdx, interrupt: true);
            }
        }

        public override void Confirm()
        {
            if (_groups.Count == 0) return;
            if (!_inGroup)
            {
                var g = _groups[_groupIdx];
                if (g.Items.Count == 1)
                {
                    MarkerMenu.OpenGuide(g.Items[0]);
                }
                else
                {
                    _inGroup = true;
                    _itemIdx = 0;
                    UiSfx.Entry();
                    AnnounceItem(g.Items[0], 0, interrupt: false);
                }
            }
            else
            {
                var g = _groups[_groupIdx];
                if (_itemIdx >= 0 && _itemIdx < g.Items.Count)
                    MarkerMenu.OpenGuide(g.Items[_itemIdx]);
            }
        }

        public override void Detail()
        {
            if (_inGroup)
            {
                var g = _groups[_groupIdx];
                if (_itemIdx >= 0 && _itemIdx < g.Items.Count)
                    DetailItem(g.Items[_itemIdx], _itemIdx);
            }
            else
            {
                if (_groupIdx < _groups.Count) AnnounceGroup(_groupIdx, interrupt: true);
            }
        }

        public override bool HandleDpadLR(int id)
        {
            if (id == DpadRight && !_inGroup && _groups.Count > 0
                && _groups[_groupIdx].Items.Count > 1)
            {
                _inGroup = true;
                _itemIdx = 0;
                UiSfx.Entry();
                AnnounceItem(_groups[_groupIdx].Items[0], 0, interrupt: true);
                return true;
            }
            if (id == DpadLeft && _inGroup)
            {
                _inGroup = false;
                UiSfx.Cycle();
                AnnounceGroup(_groupIdx, interrupt: true);
                return true;
            }
            return false;
        }

        private void AnnounceGroup(int idx, bool interrupt)
        {
            var g = _groups[idx];
            string count = g.Items.Count == 1
                ? Strings.L("map.poi.group.one")
                : string.Format(Strings.L("map.poi.group.many"), g.Items.Count);
            string dd = MapMarkerUtil.DirectionAndDistance(g.Items[0]);
            string text = g.Name + ", " + count;
            if (!string.IsNullOrEmpty(dd)) text += ", " + dd;
            TtsText.Say(text, interrupt);
        }

        private void AnnounceItem(MapMarkerUIElement m, int idx, bool interrupt)
        {
            MapMarkerUtil.FocusMarker(m);
            string name = MapMarkerUtil.MarkerTitle(m);
            if (string.IsNullOrEmpty(name)) name = Strings.L("map.poi.marker");
            string head = (idx + 1) + ", " + name;
            string dd = MapMarkerUtil.DirectionAndDistance(m);
            TtsText.Say(string.IsNullOrEmpty(dd) ? head : head + ", " + dd, interrupt);
        }

        private void DetailItem(MapMarkerUIElement m, int idx)
        {
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
