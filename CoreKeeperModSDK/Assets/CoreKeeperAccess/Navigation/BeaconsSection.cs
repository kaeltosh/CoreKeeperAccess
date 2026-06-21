using System;
using System.Collections.Generic;
using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Gameplay;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using Unity.Mathematics;
using UnityEngine;

namespace CoreKeeperAccess.Navigation
{
    // Section MES BALISES : les marqueurs poses par le joueur (UserPlacedMarker). On les pose (a
    // sa position), renomme et supprime. Premier jalon du reseau de navigation par points (un
    // noeud = une POSITION, d'ou la persistance des noms par case dans BeaconStore).
    //
    // Lignes VIRTUELLES (qui ne sont pas des marqueurs) : en TETE 0 = "Nouvelle balise ici"
    // (toujours), 1 = "Rejoindre le reseau le plus proche" (si le graphe a des noeuds) ; en QUEUE
    // "Recalculer le reseau" (si le graphe a des noeuds, action de maintenance reletguee en fin).
    // Les balises reelles s'intercalent, decalees du nombre de lignes de tete.
    internal sealed class BeaconsSection : MarkerListSection
    {
        private readonly List<MapMarkerUIElement> _beacons = new List<MapMarkerUIElement>();

        private const int VrowNew = 0, VrowJoin = 1, VrowRecalc = 2;

        // Rafraichissement differe apres une pose : le marqueur n'existe en UIElement qu'au
        // prochain passage de MapUI (~1 s) -> on re-scanne et on selectionne la balise fraiche
        // un court instant plus tard. Silencieux (la pose a deja ete annoncee).
        private bool _hasPendingSelect;
        private int2 _pendingSelectTile;
        private float _pendingSelectTime;

        public override string TitleKey => "map.beacons";
        public override string EmptyKey => "map.poi.none"; // jamais vide en pratique (ligne "Nouvelle balise")

        public override void Rebuild(List<MapMarkerUIElement> scanned)
        {
            _beacons.Clear();
            foreach (var m in scanned)
                if (m.markerType == MapMarkerType.UserPlacedMarker)
                    _beacons.Add(m);
            float2 pp = MapMarkerUtil.PlayerPos();
            _beacons.Sort((a, b) => MapMarkerUtil.DistSq(a, pp).CompareTo(MapMarkerUtil.DistSq(b, pp)));
        }

        public override void Clear() { _beacons.Clear(); _hasPendingSelect = false; }

        // Selection differee de la balise fraichement posee (le temps que MapUI cree son
        // UIElement). Re-scanne juste cette section et focalise la balise par sa case.
        public override void Tick()
        {
            if (!_hasPendingSelect || Time.time < _pendingSelectTime) return;
            _hasPendingSelect = false;
            Rebuild(MapMarkerUtil.ScanMarkers());
            int mi = _beacons.FindIndex(b => MapMarkerUtil.MarkerTile(b, out int2 t) && t.Equals(_pendingSelectTile));
            if (mi >= 0) { Index = BeaconHeadCount() + mi; AnnounceRow(Index, interrupt: false); }
        }

        // ===== Lignes virtuelles =====
        private static bool HasNetwork => BeaconGraph.NodeCount > 0;
        private static int BeaconHeadCount() => 1 + (HasNetwork ? 1 : 0); // creer (+ rejoindre)
        private static int BeaconTailCount() => HasNetwork ? 1 : 0;        // recalculer

        protected override int RowCount => BeaconHeadCount() + _beacons.Count + BeaconTailCount();

        private static bool IsVirtualRow(int idx, out int kind)
        {
            kind = -1;
            int head = BeaconHeadCount();
            if (idx >= 0 && idx < head) { kind = idx; return true; }                          // tete
            return false;
        }

        // Vrai si idx est la ligne de queue "Recalculer".
        private bool IsRecalcRow(int idx) => BeaconTailCount() > 0 && idx == BeaconHeadCount() + _beacons.Count;

        private MapMarkerUIElement MarkerAt(int idx)
        {
            int mi = idx - BeaconHeadCount();
            return (mi >= 0 && mi < _beacons.Count) ? _beacons[mi] : null;
        }

        private static string VirtualLabel(int kind)
            => kind == VrowNew ? Strings.L("beacon.new")
             : kind == VrowJoin ? Strings.L("guide.joinnetwork")
             : Strings.L("netrecalc.menu");

        // ===== Lignes (override de MarkerListSection) =====

        protected override void AnnounceRow(int idx, bool interrupt)
        {
            if (IsVirtualRow(idx, out int kind)) { TtsText.Say(VirtualLabel(kind), interrupt); return; }
            if (IsRecalcRow(idx)) { TtsText.Say(VirtualLabel(VrowRecalc), interrupt); return; }
            var m = MarkerAt(idx);
            if (m == null) return;
            MapMarkerUtil.FocusMarker(m);
            string name = BeaconName(m);
            string head = (idx - BeaconHeadCount() + 1) + ", " + name;
            string dd = MapMarkerUtil.DirectionAndDistance(m);
            TtsText.Say(string.IsNullOrEmpty(dd) ? head : head + ", " + dd, interrupt);
        }

        protected override void ConfirmRow(int idx)
        {
            if (IsVirtualRow(idx, out int kind))
            {
                UiSfx.Validate();   // action directe (creer / rejoindre)
                if (kind == VrowNew) PlaceBeaconAtPlayer();
                else JoinNearestNetwork();
                return;
            }
            if (IsRecalcRow(idx)) { UiSfx.Validate(); RecalcNetwork(); return; }
            var bm = MarkerAt(idx);
            if (bm == null || !MapMarkerUtil.MarkerTile(bm, out int2 tile)) return;
            var marker = bm; // capture pour les closures (rename/delete operent sur CE marqueur)
            var extra = new List<ActionMenu.Item>
            {
                new ActionMenu.Item { Label = Strings.L("ctx.rename"), Run = () => RenameBeacon(marker) },
                new ActionMenu.Item { Label = Strings.L("ctx.delete"), Run = () => DeleteBeacon(marker) },
            };
            MarkerMenu.Open(BeaconName(bm), tile, extra); // ActionMenu joue son propre earcon
        }

        protected override void DetailRow(int idx)
        {
            if (IsVirtualRow(idx, out int kind)) { TtsText.Say(VirtualLabel(kind), true); return; }
            if (IsRecalcRow(idx)) { TtsText.Say(VirtualLabel(VrowRecalc), true); return; }
            var m = MarkerAt(idx);
            if (m == null) return;
            string text = (idx - BeaconHeadCount() + 1) + ", " + BeaconName(m);
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

        // ===== Nom d'une balise =====
        private static string BeaconName(MapMarkerUIElement m)
        {
            if (MapMarkerUtil.MarkerTile(m, out int2 tile))
            {
                string n = BeaconStore.GetName(tile);
                if (!string.IsNullOrEmpty(n)) return n;
            }
            return Strings.L("map.poi.marker");
        }

        // ===== Actions =====

        // Pose une balise a la position du joueur (pas besoin de viser). On reproduit la pose
        // native : CreateMapUI au serveur (fait foi) + prespawn local pour le visuel immediat,
        // couleur fixe Marker1. Nom auto "Balise <biome> <n>" stocke par case ; la balise
        // rejoint la liste au prochain passage de MapUI -> selection differee.
        private void PlaceBeaconAtPlayer()
        {
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null) return;
            if (player.guestMode) { TtsText.Say(Strings.L("beacon.guest"), true); return; }

            var wp = player.WorldPosition;
            int2 tile = new int2((int)math.round(wp.x), (int)math.round(wp.z));
            float3 world = new float3(tile.x, wp.y, tile.y); // tile.y = coord Z monde
            try { player.playerCommandSystem.CreateMapUI(world, (int)UserMapMarkerType.Marker1); }
            catch (Exception ex) { Diag.Error("A11yBeacon", ex); return; }
            try
            {
                player.entityPrespawnSystem.CreatePrespawnEntity(
                    new ObjectDataCD { objectID = ObjectID.MapMarker, variation = 0 },
                    EntityMonoBehaviour.ToRenderFromWorld(world), default(float3));
            }
            catch { } // visuel local optionnel : la balise serveur arrive de toute facon

            string biome = MapMarkerUtil.ResolveBiome(new float2(tile.x, tile.y));
            int n = BeaconStore.NextAutoNumber();
            string name = Strings.L("beacon.auto")
                + (string.IsNullOrEmpty(biome) ? "" : " " + biome) + " " + n;
            BeaconStore.SetName(tile, name);
            TtsText.Say(Strings.L("beacon.placed") + ", " + name, true);

            _pendingSelectTile = tile;
            _pendingSelectTime = Time.time + 1.2f;
            _hasPendingSelect = true;
        }

        // Supprime la balise donnee (RemoveMarker serveur) et oublie son nom. Opere sur le
        // marqueur capture a l'ouverture du menu. Re-cadre ensuite la selection sur une ligne
        // valide (les lignes virtuelles restent).
        private void DeleteBeacon(MapMarkerUIElement m)
        {
            if (m == null || !MapMarkerUtil.MarkerTile(m, out int2 tile)) return;
            if (!MapMarkerUtil.TryMarkerPos(m, out float2 pos)) return;
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null || player.guestMode) return;
            string name = BeaconName(m);
            try
            {
                player.QueueInputAction(new UIInputActionData
                {
                    action = UIInputAction.RemoveMarker,
                    entity = m.mapMarkerEntity,
                    position = pos
                });
            }
            catch (Exception ex) { Diag.Error("A11yBeacon", ex); return; }
            BeaconStore.Remove(tile);
            TtsText.Say(Strings.L("beacon.deleted") + ", " + name, true);
            Rebuild(MapMarkerUtil.ScanMarkers());
            int n = RowCount;
            if (Index >= n) Index = n - 1;
            if (Index < 0) Index = 0;
            AnnounceRow(Index, interrupt: false);
        }

        // Ouvre l'editeur clavier maison (carte ouverte = gameplay gele, saisie sure).
        private void RenameBeacon(MapMarkerUIElement m)
        {
            if (m == null || !MapMarkerUtil.MarkerTile(m, out int2 tile)) return;
            string current = BeaconStore.GetName(tile) ?? "";
            TextEntry.Begin("beacon.rename", current, 40, name =>
            {
                if (string.IsNullOrEmpty(name)) return; // nom vide -> on garde l'ancien
                BeaconStore.SetName(tile, name);
                TtsText.Say(Strings.L("beacon.renamed") + ", " + name, true);
            });
        }

        // "Rejoindre le reseau le plus proche" : guidage DIRECT vers la torche-noeud la plus
        // proche du joueur (pour rallier ses balises quand on est perdu hors reseau).
        private void JoinNearestNetwork()
        {
            if (!BeaconGraph.NearestNode(MapMarkerUtil.PlayerPos(), float.MaxValue, out int2 node))
            {
                TtsText.Say(Strings.L("guide.nonetwork"), true);
                return;
            }
            BeaconGuide.StartDirect(node, Strings.L("guide.network"));
            MapMarkerUtil.CloseMap();
        }

        // "Recalculer le reseau" : pose une demande de recalcul local autour du joueur. Le
        // systeme tisse les aretes manquantes par ligne de vue ; l'annonce part de GameplayInput
        // quand la reponse arrive. On RESTE sur la carte (maintenance, pas de deplacement).
        private void RecalcNetwork()
        {
            float2 pp = MapMarkerUtil.PlayerPos();
            NetworkRecalc.Center = new int2((int)math.round(pp.x), (int)math.round(pp.y));
            NetworkRecalc.Radius = 16f;
            NetworkRecalc.ResultValid = false;
            NetworkRecalc.Requested = true;
        }
    }
}
