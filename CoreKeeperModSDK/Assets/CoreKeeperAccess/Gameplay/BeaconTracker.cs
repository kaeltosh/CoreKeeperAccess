using CoreKeeperAccess.Navigation;
using Unity.Mathematics;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Moteur de tissage du reseau de navigation (cf. fiche beacon-navigation). Observe le
    // joueur frame par frame et alimente BeaconGraph SANS rien demander a personne :
    //  - capte les TORCHES vues par l'ObjectIndex -> noeuds (la position EST l'identite) ;
    //  - relie deux noeuds par une arete des qu'on passe de l'un a l'autre EN MARCHANT
    //    (preuve de franchissabilite = le trajet parcouru), poids = distance reellement
    //    parcourue (integree frame par frame) ;
    //  - un saut de position (teleport, changement de zone) COUPE la continuite : pas
    //    d'arete fantome a vol d'oiseau (le noeud courant repart "a froid").
    //
    // 100 % client, aucune ecriture de gameplay : on ne fait que LIRE la position et l'index.
    // Tranche A : tissage + persistance + diagnostic. Pas encore de guidage audio (tranche B)
    // ni de portes / recalcul en base (tranche C).
    internal static class BeaconTracker
    {
        private const float ScanInterval = 0.25f; // ~4 Hz, cale sur le rafraichissement de l'index
        private const float BrushTiles = 1.5f;     // "frole" un noeud si on passe a <= 1.5 case
        private const float JumpTiles = 6f;        // delta d'une frame au-dela = teleport -> coupe

        private static bool _hasPrev;
        private static float2 _prevPos;
        private static bool _hasCurrent;
        private static int2 _currentNode;
        private static float _accumDist; // distance parcourue depuis le dernier noeud frole
        private static float _nextScan;

        public static bool HasCurrent => _hasCurrent;
        public static int2 CurrentNode => _currentNode;

        public static void Tick(PlayerController player)
        {
            if (player == null) { _hasPrev = false; _hasCurrent = false; _accumDist = 0f; return; }

            Vector3 wp = player.WorldPosition;
            float2 pos = new float2(wp.x, wp.z);

            if (!_hasPrev) { _hasPrev = true; _prevPos = pos; return; }

            // Continuite : un delta enorme entre deux frames = saut (teleport / chargement).
            // On rompt le fil (pas d'arete) ; sinon on integre la distance reellement marchee.
            float delta = math.length(pos - _prevPos);
            if (delta > JumpTiles) { _hasCurrent = false; _accumDist = 0f; }
            else _accumDist += delta;
            _prevPos = pos;

            // Capte les torches proches dans l'index (rempli par TileReaderSystem, lu ici en
            // main thread comme le fait PingSonar). La position EST l'identite du noeud.
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + ScanInterval;
                foreach (var kv in ObjectIndex.Map)
                {
                    // Torches ET portes deviennent des noeuds (a plat, seul le libelle differe).
                    if (!BeaconObjects.IsNode(kv.Value.Id, out NodeType nt)) continue;
                    BeaconGraph.EnsureNode(new int2((int)(kv.Key >> 32), (int)(uint)kv.Key), nt);
                }
            }

            // Frolement : le noeud le plus proche du joueur a <= BrushTiles devient le noeud
            // courant. S'il change, on tisse l'arete depuis le precedent (trajet parcouru).
            if (BeaconGraph.NearestNode(pos, BrushTiles, out int2 near))
            {
                if (!_hasCurrent)
                {
                    _currentNode = near;
                    _hasCurrent = true;
                    _accumDist = 0f;
                }
                else if (!near.Equals(_currentNode))
                {
                    // Poids = max(distance marchee, distance a vol d'oiseau) : jamais sous la
                    // distance reelle, meme si le joueur a fait quelques allers-retours.
                    float crow = math.length(new float2(near.x, near.y) - new float2(_currentNode.x, _currentNode.y));
                    BeaconGraph.AddOrReinforceEdge(_currentNode, near, math.max(_accumDist, crow),
                        BeaconGraph.TypeOf(_currentNode), BeaconGraph.TypeOf(near));
                    _currentNode = near;
                    _accumDist = 0f;
                }
            }

            BeaconGraph.FlushIfDue();
        }

        // (La reconnaissance des noeuds torches/portes est centralisee dans BeaconObjects.)

        // Diagnostic dev (Triangle + F6) : etat du graphe au lecteur d'ecran, pour valider
        // le tissage sans audio. Nom du noeud courant via BeaconStore si nomme, sinon type
        // (porte / torche) + coords.
        public static void AnnounceDiag()
        {
            int nodes = BeaconGraph.NodeCount;
            int edges = BeaconGraph.EdgeCount;
            string cur;
            if (_hasCurrent)
            {
                string name = BeaconStore.GetName(_currentNode);
                string kind = BeaconGraph.TypeOf(_currentNode) == NodeType.Door ? "porte" : "torche";
                cur = !string.IsNullOrEmpty(name)
                    ? name
                    : kind + " case " + _currentNode.x + ", " + _currentNode.y;
            }
            else cur = "aucun noeud courant";

            string msg = nodes + " noeuds, " + edges + " aretes. Courant : " + cur;
            Patches.TtsText.Say(msg, true);
            Diag.Log("A11yBeaconGraph", "diag -> " + msg);
        }
    }
}
