using System.Collections.Generic;
using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Navigation;
using CoreKeeperAccess.Patches;
using Unity.Mathematics;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Guidage à l'oreille vers un nœud du réseau (cf. fiche beacon-navigation, tranche B).
    // Modèle du repère de centre d'arène : un earcon SPATIALISÉ posé sur la position monde
    // du point à atteindre — le pan gauche/droite et l'atténuation par la distance viennent
    // gratuitement de la position (caméra fixe nord-en-haut). On NE code PAS de nord/sud :
    // on triangule en marchant. La hauteur (pitch) et la cadence encodent la PROXIMITÉ
    // (précision croissante en approchant), pas une direction.
    //
    //  - Cible sélectionnée → routage Dijkstra sur le graphe parcouru ; on sonifie le
    //    PROCHAIN nœud du chemin depuis le nœud le plus proche du joueur (le « next beacon
    //    when collected » est automatique : on recalcule depuis la position courante, donc
    //    le hop avance tout seul quand on atteint le précédent).
    //  - Pas de cible → on sonifie la TORCHE LA PLUS PROCHE (repère de proximité par défaut).
    //
    // 100 % client, aucune écriture de gameplay. Earcon = ping répété (SfxID natif), via
    // GameplayAudio.PlaySpatial — même chaîne que le sonar / la sentinelle.
    internal static class BeaconGuide
    {
        // Son du beacon (placeholder — l'utilisateur tranche flûte vs carillon à l'oreille).
        // Alternative : SfxID.fluteToneFs. NB : ces deux sons ont de la traîne (~2-4 s) ;
        // si le ping rapproché les fait se chevaucher en nappe, tronquer le clip en RAM.
        private const SfxID GuideSfx = SfxID.skillPointChime1;

        private const float PingInterval = 0.5f;   // cadence FIXE : le son joue toutes les 0,5 s, point
        private const float GuideVolume = 0.5f;     // volume de base (× MasterVolume dans PlayBeacon)

        private static bool _hasTarget;
        private static int2 _target;
        private static float _nextPing;

        public static bool Active { get; private set; }

        // Sélection d'une cible explicite (à câbler depuis TeleportNavigator / la liste de
        // balises au prochain tour). Sans cible, le guidage suit la torche la plus proche.
        public static void SetTarget(int2 pos) { _target = pos; _hasTarget = true; }
        public static void ClearTarget() { _hasTarget = false; }

        public static void Toggle()
        {
            Active = !Active;
            _nextPing = 0f;
            TtsText.Say(Active ? "Navigation activee" : "Navigation desactivee", true);
        }

        public static void Tick(PlayerController player)
        {
            if (!Active || player == null || !InputContext.InGameFree) return;

            float2 me = new float2(player.WorldPosition.x, player.WorldPosition.z);

            // Point à sonifier : prochain hop vers la cible, sinon torche la plus proche.
            if (!ResolveTarget(me, out int2 point)) return; // graphe vide : rien à guider

            if (Time.unscaledTime < _nextPing) return;

            float2 d = new float2(point.x, point.y) - me;
            float dist = math.length(d);

            // Langage positionnel du mod (curseur / laser / repère de centre) :
            //   pan = est/ouest (horizontal), hauteur = nord/sud (vertical), volume = distance.
            float pan = GameplayAudio.PanFromTiles(d.x);
            float pitch = Mathf.Clamp(Mathf.Pow(2f, d.y / 12f), 0.5f, 2f); // nord = aigu, sud = grave
            float volume = GuideVolume * GameplayAudio.DistanceTrim(dist);
            GameplayAudio.PlayBeacon(GuideSfx, pan, pitch, volume);

            _nextPing = Time.unscaledTime + PingInterval; // cadence FIXE, independante de la position
        }

        // Détermine le point monde vers lequel pinger. false si le graphe est vide.
        private static bool ResolveTarget(float2 me, out int2 point)
        {
            point = default;
            // Nœud le plus proche du joueur (rayon non borné : on cherche dans tout le graphe).
            if (!BeaconGraph.NearestNode(me, float.MaxValue, out int2 nearest)) return false;

            if (!_hasTarget) { point = nearest; return true; } // pas de cible : la plus proche

            // Cible sélectionnée : routage depuis le nœud le plus proche. Le prochain hop
            // après ce nœud est le point à viser ; si on EST déjà sur la cible (ou pas de
            // chemin connu), on pointe la cible directement (fallback optimiste à vol d'oiseau).
            List<int2> path = BeaconGraph.ShortestPath(nearest, _target);
            point = path.Count >= 2 ? path[1] : _target;
            return true;
        }
    }
}
