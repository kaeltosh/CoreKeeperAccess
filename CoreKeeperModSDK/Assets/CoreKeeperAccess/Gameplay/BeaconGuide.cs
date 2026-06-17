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
    // SPATIALISÉ posé sur le point à viser : le pan gauche/droite et l'atténuation par la
    // distance viennent gratuitement de la position (caméra fixe nord-en-haut), la hauteur
    // (pitch) encode le nord/sud. Cadence FIXE : la durée du son ne gouverne PAS la fréquence.
    //
    // DEUX MODES (choisis à la sélection de cible depuis le menu contextuel de la carte) :
    //  - RÉSEAU (routé) : Dijkstra sur le graphe parcouru ; on sonifie le PROCHAIN nœud du
    //    chemin (de torche en torche). Le « next beacon when collected » est automatique : on
    //    recalcule depuis la position courante, le hop avance tout seul. Le dernier tronçon
    //    (cible pas forcément un nœud) se fait en cap direct.
    //  - DIRECT (vol d'oiseau) : on sonifie la case cible elle-même, zéro routage. Pour
    //    atteindre un point sans suivre de route, CRÉER une nouvelle route (marcher droit
    //    tisse le graphe au passage), rejoindre le réseau quand on est perdu, ou viser un
    //    sous-réseau déconnecté (composantes disjointes → routage impossible → direct).
    //
    // RÈGLE DE DESIGN (fiche) : l'earcon ne sonne QUE pendant un itinéraire actif (une cible
    // choisie). Sans cible = SILENCE. Pas de radar permanent de torches.
    //
    // 100 % client, aucune écriture de gameplay. Earcon via GameplayAudio.PlayBeacon (voie
    // dédiée : Stop puis Play à chaque ping, la traîne du clip ne déborde jamais).
    internal static class BeaconGuide
    {
        private const SfxID GuideSfx = SfxID.skillPointChime1; // placeholder (choix utilisateur)
        private const float PingInterval = 0.5f;   // cadence FIXE
        private const float GuideVolume = 0.5f;     // volume de base (× MasterVolume dans PlayBeacon)
        private const float ArriveTiles = 1.5f;     // rayon d'arrivée sur la cible finale
        private const float ReachTiles = 2f;        // rayon pour considérer le réseau « rejoint »

        private static bool _hasTarget;
        private static int2 _target;
        private static bool _routed;
        private static bool _reachedNetwork;        // (routé) a-t-on touché un premier nœud du réseau ?
        private static string _name;
        private static float _nextPing;

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
            _name = name ?? "";
            _hasTarget = true;
            _nextPing = 0f;
            string verb = Strings.L(routed ? "guide.routed" : "guide.direct.to");
            TtsText.Say(verb + " " + _name, true);
        }

        public static void Stop(bool announce)
        {
            if (!_hasTarget) return;
            _hasTarget = false;
            if (announce) TtsText.Say(Strings.L("guide.cancelled"), true);
        }

        public static void Tick(PlayerController player)
        {
            if (!_hasTarget || player == null || !InputContext.InGameFree) return;

            float2 me = new float2(player.WorldPosition.x, player.WorldPosition.z);

            // Arrivée sur la cible FINALE (pas le hop intermédiaire) → annonce et silence.
            float2 toTarget = new float2(_target.x, _target.y) - me;
            if (math.length(toTarget) <= ArriveTiles)
            {
                _hasTarget = false;
                TtsText.Say(Strings.L("guide.arrived") + " " + _name, true);
                return;
            }

            if (Time.unscaledTime < _nextPing) return;

            int2 point = ResolvePoint(me);
            float2 d = new float2(point.x, point.y) - me;
            float dist = math.length(d);

            // Langage positionnel du mod : pan = est/ouest, hauteur = nord/sud, volume = distance.
            float pan = GameplayAudio.PanFromTiles(d.x);
            float pitch = Mathf.Clamp(Mathf.Pow(2f, d.y / 12f), 0.5f, 2f); // nord = aigu, sud = grave
            float volume = GuideVolume * GameplayAudio.DistanceTrim(dist);
            GameplayAudio.PlayBeacon(GuideSfx, pan, pitch, volume);

            _nextPing = Time.unscaledTime + PingInterval; // cadence FIXE
        }

        // Point monde à sonifier : la cible directe, ou le prochain hop du chemin réseau.
        private static int2 ResolvePoint(float2 me)
        {
            if (!_routed) return _target;

            // Nœud d'ENTRÉE = torche la plus proche du joueur. Graphe vide → cap direct.
            if (!BeaconGraph.NearestNode(me, float.MaxValue, out int2 meNode)) return _target;

            // Tant qu'on n'a pas REJOINT le réseau (touché un premier nœud), on guide vers ce
            // point d'entrée, PAS vers un hop lointain du chemin — sinon, parti de loin,
            // l'earcon pointe un nœud profond dans une mauvaise direction (le bug « ça marche
            // que si on est déjà sur le réseau »). Une fois le réseau atteint, le flag empêche
            // de repartir en arrière au milieu d'une longue arête (anti-oscillation).
            if (!_reachedNetwork
                && math.lengthsq(new float2(meNode.x, meNode.y) - me) <= ReachTiles * ReachTiles)
                _reachedNetwork = true;
            if (!_reachedNetwork) return meNode; // rejoindre le réseau d'abord

            // Sur le réseau : route de meNode vers la torche la plus proche de la CIBLE, puis
            // cap direct sur le dernier tronçon (la cible n'est pas forcément un nœud).
            // Composantes disjointes → cap direct sur la cible.
            if (!BeaconGraph.NearestNode(new float2(_target.x, _target.y), float.MaxValue, out int2 tgtNode))
                return _target;

            List<int2> path = BeaconGraph.ShortestPath(meNode, tgtNode);
            if (path.Count >= 2) return path[1]; // prochain nœud vers la cible
            return _target;                      // même nœud / pas de chemin connu : cap direct
        }
    }
}
