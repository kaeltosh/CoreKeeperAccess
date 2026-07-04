using System.Collections.Generic;
using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Navigation;
using CoreKeeperAccess.Patches;
using Interaction;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Pouls gameplay de la touche access : appele chaque frame (apres InfoKey) il publie le
    // centre de l'index d'objets, tisse le reseau de navigation, egrene les .Tick() de tous
    // les sous-modules gameplay (sonar, pas, sonar de proximite, placement, roue de stats,
    // ping) et porte ses deux lectures par-frame maison : la PROSPECTION minerai (combo
    // Triangle + gauche, reponse differee d'une frame par OreScan) et la surveillance de
    // l'INTERACTIBLE a portee (annonce au changement).
    // Les combos d'action sur une UI ouverte (transfert, reparation, recyclage, forge,
    // vente) sont dans StationCommands ; le ping sonar, la locomotion et la lecture de pose
    // dans leurs fichiers dedies (decoupage du 21 juin). Les combos sont routes par
    // ComboDispatcher (cf. ComboBindings) ; ici ne restent que les ticks.
    internal static class GameplayInput
    {
        // CONFIRME en jeu (build 28) : la stat VisibleOreDistance ne porte que le BONUS
        // (talent minage "Night Vision", +2/point) ; la distance de BASE des paillettes
        // est codee en dur dans le shader, illisible. On approxime la base a 10 cases
        // (= portee de l'ambiance minerai) et on AJOUTE le bonus, comme le shader.
        private const int BaseProspectRadius = 10;
        private const int MaxProspectRadius = 30; // plafond perf
        private const float ProspectDingVolume = 0.8f;

        private static bool _prospectPending;
        private static int _prospectRadius;

        public static void Tick()
        {
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null) { ProximitySonar.Stop(); CollisionRadar.Stop(); _prospectPending = false; return; }

            // Centre de l'index d'objets (TileReaderSystem le reconstruit autour).
            ObjectIndex.Center = new float2(player.WorldPosition.x, player.WorldPosition.z);

            // Tisse le reseau de navigation (torches -> noeuds, trajets -> aretes). Apres la
            // publication du centre : l'index est alimente pour la meme position de joueur.
            BeaconTracker.Tick(player);
            BeaconGuide.Tick(player); // guidage a l'oreille vers un noeud (si le mode est actif)

            // Recalcul local du reseau (tranche C) : reponse du systeme -> on annonce le
            // nombre d'aretes ajoutees par le tissage en ligne de vue.
            if (NetworkRecalc.ResultValid)
            {
                NetworkRecalc.ResultValid = false;
                string msg = Strings.L("netrecalc.done") + ", "
                    + NetworkRecalc.AddedEdges + " " + Strings.L("netrecalc.links");
                if (NetworkRecalc.RemovedEdges > 0)
                    msg += ", " + NetworkRecalc.RemovedEdges + " " + Strings.L("netrecalc.removed");
                if (NetworkRecalc.LostNodes > 0)
                    msg += ", " + NetworkRecalc.LostNodes + " " + Strings.L("netrecalc.lost");
                TtsText.Say(msg, true);
            }

            // Les combos (prospection, ping sonar, double-tap carte) sont routes par
            // ComboDispatcher (cf. ComboBindings). Ici ne restent que les ticks.
            PingSonar.Tick(player);
            StepEngine.Tick(player);
            ProximitySonar.Tick(player);
            CollisionRadar.Tick(player);
            PlacementReader.Tick(player);
            StatsWheel.Tick(player);
            HotbarJumpWheel.Tick(player);

            // Etalement de l'emprise : declenche par un DEPLACEMENT delibere du curseur
            // (BuildModeNavigator pose FootprintDueAt), annonce apres un petit delai - le
            // temps que le ghost rattrape le curseur (sinon ca oscille), et UNIQUEMENT sur
            // geste -> n'interrompt plus les autres TTS en continu.
            if (BuildModeNavigator.FootprintDueAt > 0f && Time.unscaledTime >= BuildModeNavigator.FootprintDueAt)
            {
                BuildModeNavigator.FootprintDueAt = -1f;
                string fp = InGameTtsCore.FootprintFromCursor(BuildModeNavigator.CursorTile);
                if (!string.IsNullOrEmpty(fp)) TtsText.Say(fp, true);
            }

            TickProspect(player);
            WatchInteractable(player);
        }

        // Annonce d'INTERACTION A PORTEE : le jeu maintient sur le joueur l'interactible
        // le plus proche actuellement atteignable (InteractorCD.currentClosestInteractable,
        // la donnee qui pilote le prompt visuel des voyants). On annonce au CHANGEMENT
        // ("Statue du boss slime, interaction disponible") -> on sait toujours si A va
        // faire quelque chose et sur quoi. Regle le "il faut etre au bon endroit" des
        // objets multi-cases (statues, Core...). Sortie de portee : silence.
        private const float InteractPollInterval = 0.2f;
        private static long _lastInteractable;
        private static float _nextInteractPoll;

        private static void WatchInteractable(PlayerController player)
        {
            if (A11ySettings.MuteInteractInCursor && BuildModeNavigator.CursorDetached) return;
            if (Time.unscaledTime < _nextInteractPoll) return;
            _nextInteractPoll = Time.unscaledTime + InteractPollInterval;

            long key = 0;
            ObjectID id = ObjectID.None;
            try
            {
                if (!EntityUtility.HasComponentData<InteractorCD>(player.entity, player.world)) return;
                var e = EntityUtility.GetComponentData<InteractorCD>(player.entity, player.world)
                    .currentClosestInteractable;
                if (e != Entity.Null && EntityUtility.HasComponentData<ObjectDataCD>(e, player.world))
                {
                    id = EntityUtility.GetComponentData<ObjectDataCD>(e, player.world).objectID;
                    key = EntityKey.Of(e);
                }
            }
            catch (System.Exception ex) { Diag.Error("A11yInteractDiag", ex); return; }

            if (key == _lastInteractable) return;
            _lastInteractable = key;
            if (key == 0 || id == ObjectID.None) return;

            string name = InGameTtsCore.ResolveObjectName(id);
            if (string.IsNullOrEmpty(name)) return;
            // interrupt=true (demande utilisateur) : info de POSITION, perimee si elle
            // attend son tour dans la file - on marche, le point chaud c'est MAINTENANT.
            TtsText.Say(name + ", " + Strings.L("interact.available"), true);
        }

        // Pose la demande de scan : rayon = stat VisibleOreDistance du perso, la MEME
        // que le shader des paillettes (equite stricte : les talents de minage et
        // objets qui l'augmentent portent aussi notre prospection).
        internal static void RequestProspect(PlayerController player)
        {
            int bonus = 0;
            try
            {
                bonus = EntityUtility.GetConditionEffectValue(
                    ConditionEffect.VisibleOreDistance, player.entity, player.world);
            }
            catch { }
            int radius = Mathf.Clamp(BaseProspectRadius + bonus, 1, MaxProspectRadius);

            OreScan.Center = new int2(
                Mathf.RoundToInt(player.WorldPosition.x),
                Mathf.RoundToInt(player.WorldPosition.z));
            OreScan.Radius = radius;
            OreScan.ResultValid = false;
            OreScan.Requested = true;
            _prospectPending = true;
            _prospectRadius = radius;
        }

        // Consomme la reponse du systeme (frame suivante) : ding positionnel sur le
        // filon (son natif oreHit, pan/distance par le jeu + pitch vertical maison,
        // meme langage que le curseur) + TTS cardinal et distance.
        private static void TickProspect(PlayerController player)
        {
            if (!_prospectPending || !OreScan.ResultValid) return;
            _prospectPending = false;

            if (!OreScan.Found)
            {
                TtsText.Say(Strings.L("prospect.none") + ", "
                    + Strings.L("prospect.radius") + " " + _prospectRadius, true);
                return;
            }

            float2 p = new float2(player.WorldPosition.x, player.WorldPosition.z);
            float2 d = new float2(OreScan.Tile.x, OreScan.Tile.y) - p;
            float pitch = Mathf.Clamp(Mathf.Pow(2f, d.y / 12f), 0.5f, 2f);
            GameplayAudio.PlayTableSpatialNoPitchDev(SfxTableID.oreHit,
                new Vector3(OreScan.Tile.x, 0f, OreScan.Tile.y), ProspectDingVolume, pitch);

            int dist = Mathf.RoundToInt(math.length(d));
            string text = Strings.L("prospect.ore") + ", " + (dist < 1
                ? Strings.L("prospect.here")
                : Cardinal(d) + ", " + dist + " " + Strings.L("teleport.tiles"));
            TtsText.Say(text, true);
        }

        private static readonly string[] DirKeys =
            { "dir.n", "dir.ne", "dir.e", "dir.se", "dir.s", "dir.sw", "dir.w", "dir.nw" };

        // Secteur cardinal (8) d'un vecteur monde x=est, y=nord (memes cles i18n que
        // la teleportation).
        private static string Cardinal(float2 d)
        {
            float ang = math.degrees(math.atan2(d.x, d.y));
            if (ang < 0f) ang += 360f;
            return Strings.L(DirKeys[((int)math.round(ang / 45f)) % 8]);
        }
    }
}
