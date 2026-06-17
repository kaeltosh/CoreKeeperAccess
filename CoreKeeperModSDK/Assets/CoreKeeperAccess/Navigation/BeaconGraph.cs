using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;

namespace CoreKeeperAccess.Navigation
{
    // Type d'un noeud du reseau de navigation. A PLAT (pas de hierarchie - cf. fiche
    // beacon-navigation) : seul le libelle TTS differera. Torch = balise lumineuse captee
    // par l'index ; Door = passage franchi (a venir) ; Manual = balise posee a la main
    // (BeaconStore, a relier plus tard). Tranche A : Torch uniquement.
    internal enum NodeType : byte { Torch = 1, Door = 2, Manual = 3 }

    // Le GRAPHE de navigation : noeuds (= POSITIONS, jamais l'entite) + aretes (= passages
    // physiquement parcourus). Magasin + persistance, persiste PAR MONDE. Embryon pose par
    // BeaconStore (les NOMS) ; ici les noeuds et la connectivite. Le moteur de tissage vit
    // dans BeaconTracker (Gameplay), ce fichier ne fait que stocker / charger / sauver.
    //
    // INVARIANTS (fiche beacon-navigation, a ne jamais violer) :
    //  - un noeud = une case, l'entite peut disparaitre, la position survit ;
    //  - une arete = une traversee continue prouvee par le joueur (qui a pose la balise est
    //    sans objet) ;
    //  - on n'ENLEVE jamais sur une absence (tranche A : on ne fait qu'ajouter / renforcer).
    //
    // SERIALISATION MAISON (pas JsonUtility, qui jette nos types hot-compile - cf. leçon
    // MEMORY et l'en-tete de BeaconStore). Fichier
    // <persistentDataPath>/CoreKeeperAccess/graph/<worldId>.txt :
    //   ligne 1 : "v1"
    //   noeud   : "n|x|y|t"            (t = code NodeType)
    //   arete   : "e|x1|y1|x2|y2|w"    (w = poids, point decimal force - culture invariante)
    internal static class BeaconGraph
    {
        internal struct Node { public int x; public int y; public NodeType type; }
        internal struct Edge { public int ax, ay, bx, by; public float w; }

        private const string Header = "v1";
        private const float SaveDebounce = 3f; // ne pas marteler le disque en explorant

        private static int _worldId = int.MinValue; // sentinelle : monde charge en memoire
        private static readonly Dictionary<long, Node> _nodes = new Dictionary<long, Node>();
        private static readonly Dictionary<string, Edge> _edges = new Dictionary<string, Edge>();
        private static bool _dirty;
        private static float _saveDueAt;

        private static long Key(int2 p) => ((long)p.x << 32) ^ (uint)p.y;

        // Cle d'arete : paire de noeuds ORDONNEE (l'arete est non orientee), pour qu'un
        // A->B et un B->A pointent la meme entree. Cle string (pas de ValueTuple : on evite
        // toute dependance fragile en compilation a chaud du ModLoader).
        private static string EdgeKey(int2 a, int2 b)
        {
            long ka = Key(a), kb = Key(b);
            return ka <= kb ? ka + ":" + kb : kb + ":" + ka;
        }

        public static int NodeCount { get { EnsureLoaded(); return _nodes.Count; } }
        public static int EdgeCount { get { EnsureLoaded(); return _edges.Count; } }

        // Recharge si le monde courant a change (changement de save sans relancer le jeu).
        private static void EnsureLoaded()
        {
            int id = CurrentWorldId();
            if (id == _worldId) return;
            _worldId = id;
            _nodes.Clear();
            _edges.Clear();
            _dirty = false;
            try
            {
                string path = FilePath(id);
                if (File.Exists(path))
                    foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
                        ParseLine(raw);
            }
            catch (Exception ex) { Diag.Error("A11yBeaconGraph", ex); }
            Diag.Log("A11yBeaconGraph", "load world=" + id
                + " nodes=" + _nodes.Count + " edges=" + _edges.Count);
        }

        private static void ParseLine(string line)
        {
            if (string.IsNullOrEmpty(line) || line == Header) return;
            string[] f = line.Split('|');
            if (f[0] == "n" && f.Length >= 4)
            {
                if (!int.TryParse(f[1], out int x) || !int.TryParse(f[2], out int y)) return;
                byte.TryParse(f[3], out byte t);
                _nodes[Key(new int2(x, y))] = new Node { x = x, y = y, type = (NodeType)t };
            }
            else if (f[0] == "e" && f.Length >= 6)
            {
                if (!int.TryParse(f[1], out int ax) || !int.TryParse(f[2], out int ay)) return;
                if (!int.TryParse(f[3], out int bx) || !int.TryParse(f[4], out int by)) return;
                float.TryParse(f[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float w);
                _edges[EdgeKey(new int2(ax, ay), new int2(bx, by))] =
                    new Edge { ax = ax, ay = ay, bx = bx, by = by, w = w };
            }
        }

        private static int CurrentWorldId()
        {
            try { return Manager.saves != null ? Manager.saves.GetWorldId() : 0; }
            catch { return 0; }
        }

        private static string FilePath(int id)
        {
            string dir = Path.Combine(Application.persistentDataPath, "CoreKeeperAccess", "graph");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, id + ".txt");
        }

        // Cree le noeud s'il n'existe pas a cette case ; sinon ne touche a rien (la
        // position EST l'identite - re-detecter une torche deja connue est un non-evenement).
        public static void EnsureNode(int2 pos, NodeType type)
        {
            EnsureLoaded();
            long k = Key(pos);
            if (_nodes.ContainsKey(k)) return;
            _nodes[k] = new Node { x = pos.x, y = pos.y, type = type };
            MarkDirty();
        }

        // Ajoute l'arete a<->b, ou conserve le POIDS LE PLUS COURT si elle existe deja
        // (Dijkstra a venir : on garde le meilleur trajet connu). Cree les deux noeuds au
        // besoin (une arete sans ses extremites n'a pas de sens).
        public static void AddOrReinforceEdge(int2 a, int2 b, float w, NodeType ta, NodeType tb)
        {
            if (a.Equals(b)) return;
            EnsureNode(a, ta);
            EnsureNode(b, tb);
            var key = EdgeKey(a, b);
            if (_edges.TryGetValue(key, out var e))
            {
                if (w < e.w) { e.w = w; _edges[key] = e; MarkDirty(); }
                return;
            }
            _edges[key] = new Edge { ax = a.x, ay = a.y, bx = b.x, by = b.y, w = w };
            MarkDirty();
        }

        // Noeud le plus proche d'une position monde (x,z), dans un rayon en cases. Le graphe
        // est petit (quelques dizaines de noeuds) -> balayage lineaire trivial.
        public static bool NearestNode(float2 worldXZ, float maxTiles, out int2 pos)
        {
            EnsureLoaded();
            float best = maxTiles * maxTiles;
            bool found = false;
            pos = default;
            foreach (var n in _nodes.Values)
            {
                float d2 = math.lengthsq(new float2(n.x, n.y) - worldXZ);
                if (d2 <= best) { best = d2; pos = new int2(n.x, n.y); found = true; }
            }
            return found;
        }

        public static NodeType TypeOf(int2 pos)
        {
            EnsureLoaded();
            return _nodes.TryGetValue(Key(pos), out var n) ? n.type : NodeType.Torch;
        }

        // Nœuds dont la position tombe dans un rayon (cases) autour d'un centre monde (x,z).
        // Sert au recalcul local de connectivité (tranche C) : on ne tisse qu'au cœur chargé.
        public static void NodesInRadius(float2 center, float radius, List<int2> outList)
        {
            EnsureLoaded();
            outList.Clear();
            float r2 = radius * radius;
            foreach (var n in _nodes.Values)
                if (math.lengthsq(new float2(n.x, n.y) - center) <= r2)
                    outList.Add(new int2(n.x, n.y));
        }

        // Vrai si une arête existe déjà entre ces deux nœuds (le recalcul n'ajoute que les
        // liaisons manquantes — il ne touche jamais aux arêtes existantes).
        public static bool HasEdge(int2 a, int2 b)
        {
            EnsureLoaded();
            return _edges.ContainsKey(EdgeKey(a, b));
        }

        // Plus court chemin de `from` à `to` SUR LE GRAPHE PARCOURU (Dijkstra). Retourne la
        // suite de positions de `from` (inclus) à `to` (inclus), ou liste VIDE si pas de
        // chemin connu (composantes disjointes — règle d'égalité : on ne route que sur du
        // parcouru/vérifié, jamais à travers de l'inconnu, cf. fiche no-assist). Graphe
        // minuscule (quelques dizaines de nœuds) → Dijkstra O(V²) sans tas, amplement suffisant.
        public static List<int2> ShortestPath(int2 from, int2 to)
        {
            EnsureLoaded();
            long kf = Key(from), kt = Key(to);
            var path = new List<int2>();
            if (!_nodes.ContainsKey(kf) || !_nodes.ContainsKey(kt)) return path;
            if (kf == kt) { path.Add(from); return path; }

            // Adjacence à la volée depuis les arêtes (non orientées).
            var adj = new Dictionary<long, List<KeyValuePair<long, float>>>();
            void Link(int2 a, int2 b, float w)
            {
                long ka = Key(a);
                if (!adj.TryGetValue(ka, out var l)) { l = new List<KeyValuePair<long, float>>(); adj[ka] = l; }
                l.Add(new KeyValuePair<long, float>(Key(b), w));
            }
            foreach (var e in _edges.Values)
            {
                var a = new int2(e.ax, e.ay); var b = new int2(e.bx, e.by);
                Link(a, b, e.w); Link(b, a, e.w);
            }

            var dist = new Dictionary<long, float> { [kf] = 0f };
            var prev = new Dictionary<long, long>();
            var visited = new HashSet<long>();
            while (true)
            {
                // Nœud non visité de plus petite distance (balayage linéaire).
                long u = 0; float best = float.MaxValue; bool any = false;
                foreach (var kv in dist)
                    if (!visited.Contains(kv.Key) && kv.Value < best) { best = kv.Value; u = kv.Key; any = true; }
                if (!any || u == kt) break;
                visited.Add(u);
                if (!adj.TryGetValue(u, out var nbrs)) continue;
                foreach (var n in nbrs)
                {
                    float nd = best + n.Value;
                    if (!dist.TryGetValue(n.Key, out float old) || nd < old)
                    {
                        dist[n.Key] = nd;
                        prev[n.Key] = u;
                    }
                }
            }

            if (!dist.ContainsKey(kt)) return path; // injoignable
            // Reconstruction kt -> kf via prev, puis on remet dans l'ordre from -> to.
            var rev = new List<long>();
            long cur = kt;
            rev.Add(cur);
            while (cur != kf && prev.TryGetValue(cur, out long p)) { cur = p; rev.Add(cur); }
            if (cur != kf) return new List<int2>(); // garde-fou (chaîne rompue)
            for (int i = rev.Count - 1; i >= 0; i--)
                path.Add(new int2((int)(rev[i] >> 32), (int)(uint)rev[i]));
            return path;
        }

        private static void MarkDirty()
        {
            if (!_dirty) _saveDueAt = Time.unscaledTime + SaveDebounce;
            _dirty = true;
        }

        // Appele par le tracker chaque frame : sauve si une modif attend depuis assez
        // longtemps (debounce). Evite un fichier reecrit a chaque case en exploration.
        public static void FlushIfDue()
        {
            if (_dirty && Time.unscaledTime >= _saveDueAt) Save();
        }

        public static void Flush()
        {
            if (_dirty) Save();
        }

        private static void Save()
        {
            _dirty = false;
            try
            {
                var sb = new StringBuilder();
                sb.Append(Header).Append('\n');
                foreach (var n in _nodes.Values)
                    sb.Append("n|").Append(n.x).Append('|').Append(n.y)
                      .Append('|').Append((byte)n.type).Append('\n');
                foreach (var e in _edges.Values)
                    sb.Append("e|").Append(e.ax).Append('|').Append(e.ay)
                      .Append('|').Append(e.bx).Append('|').Append(e.by)
                      .Append('|').Append(e.w.ToString("0.##", CultureInfo.InvariantCulture)).Append('\n');
                File.WriteAllText(FilePath(_worldId), sb.ToString(), new UTF8Encoding(false));
                Diag.Log("A11yBeaconGraph", "save world=" + _worldId
                    + " nodes=" + _nodes.Count + " edges=" + _edges.Count);
            }
            catch (Exception ex) { Diag.Error("A11yBeaconGraph", ex); }
        }
    }
}
