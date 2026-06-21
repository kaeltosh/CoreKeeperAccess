using System.Collections.Generic;
using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Gameplay;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoreKeeperAccess.Navigation
{
    // Helpers PARTAGES par les sections du menu carte (scan des marqueurs, positions, cap +
    // distance, biome, titre, focus UIMouse, lancement d'un guidage, fermeture). Extraits de
    // TeleportNavigator lors de la refonte en sections declaratives (21 juin) : chaque section
    // les reutilise sans dupliquer la mecanique.
    internal static class MapMarkerUtil
    {
        // Scan UNIQUE de tous les marqueurs vivants. FindObjectsInactive.Include car le jeu
        // DESACTIVE (Container.SetActive(false)) les marqueurs hors cadre minimap au lieu de les
        // detruire (cf. MapUI.MoveMarkersWithinBounds) -> sans Include on ratait tombe et relais
        // lointains. Filtre "entite vivante" + dedup par entite : elimine les reliquats du pool
        // de recyclage de MapUI et le marqueur joueur (non-entite). L'orchestrateur le fait une
        // fois par ouverture/bascule et passe la liste aux sections, qui filtrent leur part.
        public static List<MapMarkerUIElement> ScanMarkers()
        {
            var result = new List<MapMarkerUIElement>();
            var all = Object.FindObjectsByType<MapMarkerUIElement>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var seen = new HashSet<Entity>();
            foreach (var m in all)
            {
                if (m == null) continue;
                if (m.mapMarkerEntity == Entity.Null) continue;
                if (!TryMarkerPos(m, out _)) continue;
                if (!seen.Add(m.mapMarkerEntity)) continue;
                result.Add(m);
            }
            return result;
        }

        public static float2 PlayerPos()
        {
            var p = Manager.main != null ? Manager.main.player : null;
            if (p == null) return float2.zero;
            var w = p.WorldPosition;
            return new float2(w.x, w.z);
        }

        // Position monde (x,z) d'un marqueur via son entite (champ public mapMarkerEntity).
        public static bool TryMarkerPos(MapMarkerUIElement m, out float2 pos)
        {
            pos = float2.zero;
            try
            {
                var world = Manager.ecs != null ? Manager.ecs.ClientWorld : null;
                var ent = m.mapMarkerEntity;
                if (world == null || !EntityUtility.HasComponentData<LocalTransform>(ent, world)) return false;
                var t = EntityUtility.GetComponentData<LocalTransform>(ent, world);
                pos = new float2(t.Position.x, t.Position.z);
                return true;
            }
            catch { return false; }
        }

        // Case (int2) d'un marqueur = sa position monde arrondie. Cle d'identite des balises
        // (un noeud = une POSITION), alignee sur la pose serveur (coords entieres).
        public static bool MarkerTile(MapMarkerUIElement m, out int2 tile)
        {
            if (TryMarkerPos(m, out float2 p))
            {
                tile = new int2((int)math.round(p.x), (int)math.round(p.y));
                return true;
            }
            tile = default;
            return false;
        }

        public static float DistSq(MapMarkerUIElement m, float2 from)
            => TryMarkerPos(m, out float2 mp) ? math.distancesq(mp, from) : float.MaxValue;

        // "Nord-ouest 315 degres, 56 cases" : cardinal (accessible) + cap precis + distance.
        public static string DirectionAndDistance(MapMarkerUIElement m)
        {
            if (!TryMarkerPos(m, out float2 mp)) return null;
            float2 d = mp - PlayerPos();
            int cases = (int)math.round(math.length(d));
            string tiles = cases + " " + Strings.L("teleport.tiles");
            int hdg = Heading(d);
            if (hdg < 0) return tiles;
            return Cardinal(hdg) + " " + hdg + " " + Strings.L("teleport.degrees") + ", " + tiles;
        }

        // Cap en degres : 0 = nord (+z), 90 = est (+x), sens horaire. -1 si trop proche.
        private static int Heading(float2 d)
        {
            if (math.lengthsq(d) < 0.25f) return -1;
            float ang = math.degrees(math.atan2(d.x, d.y));
            if (ang < 0f) ang += 360f;
            return ((int)math.round(ang)) % 360;
        }

        private static readonly string[] DirKeys =
            { "dir.n", "dir.ne", "dir.e", "dir.se", "dir.s", "dir.sw", "dir.w", "dir.nw" };

        private static string Cardinal(int hdg)
        {
            int sector = ((int)math.round(hdg / 45f)) % 8;
            return Strings.L(DirKeys[sector]);
        }

        // Titre d'un marqueur, avec rattrapage des libelles que le JEU laisse en anglais meme en
        // francais (trad I2 manquante) : on substitue notre cle i18n. Insensible a la casse ; si
        // une maj du jeu traduit le terme, le texte ne matche plus et le natif reprend la main.
        public static string MarkerTitle(MapMarkerUIElement m)
        {
            string t = TtsText.ResolveTextAndFormatFields(m.GetHoverTitle());
            if (string.Equals(t, "Larva Boss", System.StringComparison.OrdinalIgnoreCase))
                return Strings.L("poi.ghorm");
            return t;
        }

        // Selection forcee + recalage du curseur manette virtuel (piege UIMouse, comme
        // l'inventaire) : sans ca, Croix native ne clique pas le bon marqueur -> teleportation
        // ratee. SuppressPassiveAnnounce evite que la selection forcee parle d'elle-meme.
        public static void FocusMarker(MapMarkerUIElement m)
        {
            if (m == null || Manager.ui == null) return;
            InventoryNavState.SuppressPassiveAnnounce = true;
            try
            {
                Manager.ui.OnUIElementSelected(m);
                if (Manager.ui.mouse != null)
                    Manager.ui.mouse.PlaceMousePositionOnSelectedUIElementWhenControlledByJoystick();
            }
            finally { InventoryNavState.SuppressPassiveAnnounce = false; }
        }

        // Lance un guidage (reseau ou direct) vers la case puis ferme la carte pour partir.
        public static void StartGuide(int2 tile, string name, bool routed)
        {
            if (routed) BeaconGuide.StartRouted(tile, name);
            else BeaconGuide.StartDirect(tile, name);
            CloseMap();
        }

        // Ferme la carte par l'API native directe (celle du bouton de fermeture du jeu) ->
        // deterministe. L'earcon de guidage prend alors le relais en jeu (carte fermee).
        public static void CloseMap()
        {
            if (Manager.ui != null) Manager.ui.HideMap();
        }

        // ===== Biome (via la generation du monde : marche meme zone non chargee) =====

        public static string ResolveBiome(float2 pos)
        {
            var world = Manager.ecs != null ? Manager.ecs.ClientWorld : null;
            if (world == null) return null;
            try
            {
                var em = world.EntityManager;
                var sb = new EntityQueryBuilder(Allocator.Temp)
                    .WithAll<BiomeSamplesCD>().WithOptions(EntityQueryOptions.IncludeSystems);
                var sq = sb.Build(em);
                sb.Dispose();

                BiomeLookup lookup;
                bool dispose = false;
                if (sq.HasSingleton<BiomeSamplesCD>())
                {
                    lookup = new BiomeLookup(sq.GetSingleton<BiomeSamplesCD>());
                }
                else
                {
                    var rb = new EntityQueryBuilder(Allocator.Temp)
                        .WithAll<BiomeRangesCD>().WithOptions(EntityQueryOptions.IncludeSystems);
                    var rq = rb.Build(em);
                    rb.Dispose();
                    if (!rq.HasSingleton<BiomeRangesCD>()) return null;
                    lookup = new BiomeLookup(rq.GetSingleton<BiomeRangesCD>().Value, Allocator.Temp);
                    dispose = true;
                }

                Biome b = lookup.GetBiome(new int2((int)math.round(pos.x), (int)math.round(pos.y)));
                if (dispose) lookup.Dispose();
                return BiomeName(b);
            }
            catch (System.Exception ex) { Diag.Error("A11yBiomeDiag", ex); return null; }
        }

        private static string BiomeName(Biome b)
        {
            switch (b)
            {
                case Biome.Slime: return Strings.L("biome.slime");
                case Biome.Larva: return Strings.L("biome.larva");
                case Biome.Stone: return Strings.L("biome.stone");
                case Biome.Nature: return Strings.L("biome.nature");
                case Biome.Sea: return Strings.L("biome.sea");
                case Biome.Desert: return Strings.L("biome.desert");
                case Biome.Crystal: return Strings.L("biome.crystal");
                case Biome.Passage: return Strings.L("biome.passage");
                case Biome.Excavation: return Strings.L("biome.excavation");
                default: return null; // None / biomes obsoletes
            }
        }
    }
}
