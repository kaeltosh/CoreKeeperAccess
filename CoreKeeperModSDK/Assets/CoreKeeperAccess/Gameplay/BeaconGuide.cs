using System.Collections.Generic;
using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Navigation;
using CoreKeeperAccess.Patches;
using Unity.Mathematics;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Guidage à l'oreille vers un point (cf. fiche beacon-navigation, tranche B). Un earcon
    // répété qui encode la direction d'un POINT À VISER, en zones FRANCHES :
    //  - est/ouest → pan CONTINU plein (0 à 100 %), suit la direction de la carotte.
    //  - nord/sud  → 3 pitch : aigu / médium / grave, médium = même ligne (seuil serré).
    //  - cadence   → en SUIVI DE ROUTE : ÉCART LATÉRAL à la ligne (sur le fil = rapide 0,3 s,
    //    on s'écarte = lent 1 s) ; en DIRECT / rejoindre : distance au point à viser.
    //  - volume    → FIXE, réglé au panneau (« Volume du guidage »), découplé de la distance.
    // La durée du clip ne gouverne PAS la fréquence (voie dédiée Stop/Play dans PlayBeacon).
    //
    // DEUX MODES (choisis à la sélection de cible depuis le menu contextuel de la carte) :
    //  - DIRECT (vol d'oiseau) : le point à viser EST la cible, en continu. Zéro routage.
    //  - RÉSEAU (routé) : SUIVI DE ROUTE CONTINUE (« poursuite à carotte », refonte 18 juin).
    //    Le chemin Dijkstra (suite de torches) est calculé UNE FOIS au moment où on rejoint le
    //    réseau et conservé comme une POLYLIGNE. Chaque frame on PROJETTE la position du joueur
    //    sur cette polyligne et on vise un point un peu EN AVANT sur la ligne (la « carotte »).
    //    Le point visé GLISSE le long de la route au lieu de SAUTER d'un nœud à l'autre → plus
    //    de bascule « à mi-distance », donc plus de demi-tour aux coudes : la carotte ne tourne
    //    qu'en ARRIVANT au coude. Coût : un seul Dijkstra au départ (puis recalcul ponctuel si
    //    on dévie franchement), et par frame juste une projection sur segments (quasi gratuit) —
    //    plus léger que l'ancien recalcul de chemin à chaque frame. L'audio change alors de rôle :
    //    le pan/pitch RAMÈNENT sur la route (direction de la carotte), la cadence dit la
    //    PROXIMITÉ AU FIL (écart latéral) pour rester dessus.
    //
    // RÈGLE DE DESIGN (fiche) : l'earcon ne sonne QUE pendant un itinéraire actif (une cible
    // choisie). Sans cible = SILENCE. Pas de radar permanent de torches.
    //
    // 100 % client, aucune écriture de gameplay. Earcon via GameplayAudio.PlayBeacon (voie
    // dédiée : Stop puis Play à chaque ping, la traîne du clip ne déborde jamais).
    internal static class BeaconGuide
    {
        private const SfxID GuideSfx = SfxID.skillPointChime1; // placeholder (choix utilisateur)
        private const float ArriveTiles = 1.5f;     // rayon d'arrivée sur la cible finale
        private const float ReachTiles = 2f;        // rayon pour considérer le réseau « rejoint »

        // --- Suivi de route ---
        private const float LookAheadTiles = 3f;    // la carotte est visée ce nombre de cases EN AVANT sur la route
        private const float OffRouteTiles = 4f;     // écart latéral au-delà duquel on recalcule la route

        // --- Mapping discret (choix utilisateur) : tout est quantifié en zones franches pour la
        // lisibilité, au lieu d'un continuum. Grille de 9 zones (3 pitch × 3 pans). ---
        private const float BaseVolume = 0.5f;       // volume de base (× GuideVolume réglage × MasterVolume)
        private const float AlignTiles = 1f;         // seuil SERRÉ d'alignement (médium / pan centré)
        private const float PitchHigh = 1.1554f;     // nord (+2,5 demi-tons)
        private const float PitchMid = 1f;           // pile sur la même ligne (cible droit est/ouest)
        private const float PitchLow = 0.8655f;      // sud (−2,5 demi-tons)
        // Distance → CADENCE (la distance ne pilote plus le volume) : proche = rapide, loin =
        // lent, linéaire sur 0..DistRangeTiles, plafonné au-delà.
        private const float CadenceNear = 0.5f;      // sur le fil / collé à la cible → rapide
        private const float CadenceFar = 1f;         // au plus loin → lent
        private const float DistRangeTiles = 14f;    // portée de la cadence en mode DIRECT / rejoindre
        // En SUIVI DE ROUTE, la cadence n'indique plus la distance à la cible mais l'ÉCART
        // LATÉRAL à la ligne (proximité au fil d'Ariane) : sur la route = rapide, on s'écarte = lent.
        private const float LateralRangeTiles = 4f;  // écart à la ligne pour atteindre la cadence lente

        private static bool _hasTarget;
        private static int2 _target;
        private static bool _routed;
        private static bool _reachedNetwork;        // (routé) a-t-on touché un premier nœud du réseau ?
        private static string _name;
        private static float _lastPing;             // horodatage du dernier ping (cadence ré-évaluée chaque frame)

        // Polyligne du chemin réseau (positions monde, torche par torche, finie sur la cible).
        // Construite quand on rejoint le réseau, conservée tant qu'on ne dévie pas trop.
        private static readonly List<float2> _route = new List<float2>();
        private static float _progress;            // abscisse curviligne déjà parcourue (monotone, ne recule jamais)

        // Un itinéraire est en cours. C'est la seule condition qui fait sonner l'earcon.
        public static bool Active => _hasTarget;

        // Démarre un itinéraire RÉSEAU (suit les torches) vers la case cible.
        public static void StartRouted(int2 target, string name) => Start(target, name, true);

        // Démarre un itinéraire DIRECT (vol d'oiseau) vers la case cible.
        public static void StartDirect(int2 target, string name) => Start(target, name, false);

        private static void Start(int2 target, string name, bool routed)
        {
            _target = target;
            _routed = routed;
            _reachedNetwork = false;
            _route.Clear();
            _progress = 0f;
            _name = name ?? "";
            _hasTarget = true;
            _lastPing = -999f;  // force un premier ping immédiat
            string verb = Strings.L(routed ? "guide.routed" : "guide.direct.to");
            TtsText.Say(verb + " " + _name, true);
        }

        public static void Stop(bool announce)
        {
            if (!_hasTarget) return;
            _hasTarget = false;
            _route.Clear();
            if (announce) TtsText.Say(Strings.L("guide.cancelled"), true);
        }

        public static void Tick(PlayerController player)
        {
            if (!_hasTarget || player == null || !InputContext.InGameFree) return;

            float2 me = new float2(player.WorldPosition.x, player.WorldPosition.z);
            float2 targetF = new float2(_target.x, _target.y);

            // Arrivée sur la cible FINALE → annonce et silence (vol d'oiseau, vaut pour les deux modes).
            if (math.distance(me, targetF) <= ArriveTiles)
            {
                _hasTarget = false;
                _route.Clear();
                TtsText.Say(Strings.L("guide.arrived") + " " + _name, true);
                return;
            }

            // Point à viser (DIRECTION) + niveau de cadence 0..1 (0 = rapide, 1 = lent). En suivi
            // de route la cadence = écart latéral à la ligne ; sinon = distance au point à viser.
            float2 aim;
            float cadence01;
            if (_routed) ResolveRouted(me, targetF, out aim, out cadence01);
            else { aim = targetF; cadence01 = Mathf.Clamp01(math.distance(me, targetF) / DistRangeTiles); }

            // Cadence ré-évaluée À CHAQUE FRAME avec l'état COURANT (pas figée au dernier ping) :
            // l'intervalle suit en temps réel. 0 = rapide (0,3 s), 1 = lent (1 s).
            float interval = Mathf.Lerp(CadenceNear, CadenceFar, cadence01);
            if (Time.unscaledTime - _lastPing < interval) return;

            // Mapping vers le point visé.
            float2 d = aim - me;
            float len = math.length(d);
            // Est/ouest → pan CONTINU plein (0 à 100 %) : composante horizontale normalisée
            // (sinus de l'angle au nord) → suit finement la direction de la carotte.
            float pan = len > 1e-3f ? math.clamp(d.x / len, -1f, 1f) : 0f;
            // Nord/sud → 3 pitch (crans) : aigu = nord, grave = sud, médium = même ligne (seuil serré).
            float pitch = d.y > AlignTiles ? PitchHigh : (d.y < -AlignTiles ? PitchLow : PitchMid);
            float volume = BaseVolume * A11ySettings.GuideVolume; // FIXE (réglage), découplé de la distance
            GameplayAudio.PlayBeacon(GuideSfx, pan, pitch, volume);

            _lastPing = Time.unscaledTime;
        }

        // Résout le point à viser en mode RÉSEAU : d'abord rejoindre le réseau, puis SUIVRE LA
        // ROUTE par poursuite à carotte. Sort aussi le niveau de cadence 0..1 (0 = rapide).
        private static void ResolveRouted(float2 me, float2 targetF, out float2 aim, out float cadence01)
        {
            // Phase 1 : rejoindre le réseau. On guide vers la torche la plus proche tant qu'on
            // n'en a pas touché une (sinon, parti de loin, on pointerait un nœud profond dans
            // une mauvaise direction). Cadence = distance au point d'entrée. Graphe vide → cible.
            if (!_reachedNetwork)
            {
                if (!BeaconGraph.NearestNode(me, float.MaxValue, out int2 meNode))
                {
                    aim = targetF; cadence01 = Mathf.Clamp01(math.distance(me, targetF) / DistRangeTiles); return;
                }
                float2 meNodeF = new float2(meNode.x, meNode.y);
                if (math.lengthsq(meNodeF - me) <= ReachTiles * ReachTiles)
                {
                    _reachedNetwork = true;
                    BuildRoute(me);           // réseau rejoint → on fige la polyligne du chemin
                }
                else
                {
                    aim = meNodeF; cadence01 = Mathf.Clamp01(math.distance(me, meNodeF) / DistRangeTiles); return;
                }
            }

            // Phase 2 : suivi de route. Projeter le joueur sur la polyligne ; si l'écart latéral
            // est trop grand (on a quitté le couloir), recalculer la route depuis ici.
            if (_route.Count == 0) BuildRoute(me);
            Project(me, out float s, out float lateral);
            if (lateral > OffRouteTiles)
            {
                BuildRoute(me);
                Project(me, out s, out lateral);
            }
            _progress = math.max(_progress, s); // l'avancement ne recule jamais

            // Carotte = point sur la route, LookAhead cases plus loin que l'avancement courant.
            aim = PointAlong(_progress + LookAheadTiles);
            // Cadence = ÉCART LATÉRAL à la ligne (proximité au fil) : sur la route = rapide.
            cadence01 = Mathf.Clamp01(lateral / LateralRangeTiles);
        }

        // Construit la polyligne du chemin : Dijkstra de la torche la plus proche du joueur vers
        // celle la plus proche de la cible, terminée par la cible finale (dernier tronçon hors
        // réseau). Composantes disjointes / graphe vide → route réduite à la cible (cap direct).
        private static void BuildRoute(float2 me)
        {
            _route.Clear();
            _progress = 0f;
            float2 targetF = new float2(_target.x, _target.y);

            if (BeaconGraph.NearestNode(me, float.MaxValue, out int2 meNode)
                && BeaconGraph.NearestNode(targetF, float.MaxValue, out int2 tgtNode))
            {
                List<int2> path = BeaconGraph.ShortestPath(meNode, tgtNode);
                foreach (int2 p in path)
                {
                    float2 f = new float2(p.x, p.y);
                    if (_route.Count == 0 || math.distance(_route[_route.Count - 1], f) > 0.01f)
                        _route.Add(f);
                }
            }
            // Toujours finir sur la cible elle-même (la cible n'est pas forcément un nœud).
            if (_route.Count == 0 || math.distance(_route[_route.Count - 1], targetF) > 0.01f)
                _route.Add(targetF);
        }

        // Projette 'me' sur la polyligne : abscisse curviligne du point le plus proche (s) et
        // écart latéral (lateral). Un seul point = distance à ce point.
        private static void Project(float2 me, out float s, out float lateral)
        {
            if (_route.Count == 1) { s = 0f; lateral = math.distance(me, _route[0]); return; }

            s = 0f; lateral = float.MaxValue; float acc = 0f;
            for (int i = 0; i < _route.Count - 1; i++)
            {
                float2 a = _route[i], b = _route[i + 1], ab = b - a;
                float len = math.length(ab);
                if (len < 1e-4f) continue;
                float u = math.clamp(math.dot(me - a, ab) / (len * len), 0f, 1f);
                float2 proj = a + ab * u;
                float dlat = math.distance(me, proj);
                if (dlat < lateral) { lateral = dlat; s = acc + u * len; }
                acc += len;
            }
        }

        // Point de la polyligne à l'abscisse curviligne demandée (clampée à la fin = cible).
        private static float2 PointAlong(float sTarget)
        {
            if (_route.Count == 1) return _route[0];

            float acc = 0f;
            for (int i = 0; i < _route.Count - 1; i++)
            {
                float2 a = _route[i], b = _route[i + 1];
                float len = math.distance(a, b);
                if (acc + len >= sTarget)
                {
                    float u = len < 1e-4f ? 0f : (sTarget - acc) / len;
                    return a + (b - a) * u;
                }
                acc += len;
            }
            return _route[_route.Count - 1]; // au-delà de la fin → cible finale
        }

        private static float RouteLength()
        {
            float L = 0f;
            for (int i = 0; i < _route.Count - 1; i++) L += math.distance(_route[i], _route[i + 1]);
            return L;
        }
    }
}
