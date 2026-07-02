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
    // Moteur de pas PARTAGE : une seule horloge de locomotion. Detecte le franchissement
    // de case (RoundToInt sur la position monde) et dispatche aux COUCHES de feedback
    // activees independamment dans A11ySettings. Aujourd'hui une couche (le bip de pas) ;
    // le sonar d'interstices viendra se brancher ici (etage 2). Appele chaque frame depuis
    // GameplayInput.Tick - il tourne en PERMANENCE (meme couches eteintes) pour garder la
    // case courante fraiche : rallumer une couche en pleine marche ne rejoue pas un delta
    // perime.
    internal static class StepEngine
    {
        private static int _cx, _cz;
        private static bool _hasCell;

        public static void Tick(PlayerController player)
        {
            if (player == null) { _hasCell = false; return; }
            Vector3 pos = player.WorldPosition;
            int cx = Mathf.RoundToInt(pos.x), cz = Mathf.RoundToInt(pos.z);
            if (!_hasCell) { _cx = cx; _cz = cz; _hasCell = true; return; }
            if (cx == _cx && cz == _cz) return;

            int dx = cx - _cx, dz = cz - _cz;
            _cx = cx; _cz = cz;

            // Couches togglables, allumees separement (cf. A11ySettings).
            if (A11ySettings.StepBeep) StepBeep.OnStep(dx, dz);
            // (Etage 2 : le sonar d'interstices se branchera ici.)
        }
    }

    // Couche 0 : bip de pas (la "boussole de locomotion"). Un petit bip par case franchie
    // encode la direction du deplacement - pan est/ouest, pitch nord/sud (langage du
    // curseur) -> confirme le cap ET compte les cases a l'oreille. DECOUPLE du snap
    // construction (DirectionAssist) le 16 juin : c'est un feedback PERMANENT (actif par
    // defaut, regle au panneau), la ou le snap est un outil PONCTUEL. Diagonale en marche
    // libre : arrondie au cardinal dominant (comme avant, ou le snap forcait le cardinal).
    internal static class StepBeep
    {
        private const float NorthPitch = 1.5f;   // nord = plus aigu
        private const float SouthPitch = 0.67f;  // sud = plus grave (est/ouest = neutre 1.0)

        public static void OnStep(int dx, int dz)
        {
            float pan, pitch;
            if (Mathf.Abs(dx) >= Mathf.Abs(dz)) { pan = dx >= 0 ? 1f : -1f; pitch = 1f; }   // est / ouest
            else { pan = 0f; pitch = dz > 0 ? NorthPitch : SouthPitch; }                    // nord / sud
            GameplayAudio.PlayTone(pan, pitch, A11ySettings.DirectionTickVolume);
        }
    }

    // Snap directionnel (toggle Triangle + L3). Tant qu'il est actif, le deplacement au
    // stick gauche est SNAPPE au cardinal dominant (la composante perpendiculaire est
    // annulee, cf. DirectionSnapSystem) -> lignes droites franches sans deviation, pour
    // poser des murs / labourer / semer en rangs. On ne touche NI a la pose NI a la
    // vitesse : le joueur tient LT et marche, le jeu pose ; on rectifie juste le cap.
    // DECOUPLE du bip de pas (StepBeep) le 16 juin : deux toggles independants.
    internal static class DirectionAssist
    {
        // Etat persiste dans A11ySettings : Triangle+L3 ET le panneau de reglages pilotent
        // la meme source de verite (le snap est donc reactive tel quel au relancement).
        public static bool Active
        {
            get => A11ySettings.SnapDirectional;
            set => A11ySettings.SnapDirectional = value;
        }

        // Bascule le snap + annonce l'etat.
        public static void Toggle()
        {
            Active = !Active;
            TtsText.Say(Strings.L(Active ? "direction.assist.on" : "direction.assist.off"), true);
        }
    }

    // Snappe la marche au cardinal quand DirectionAssist est actif. Tourne APRES
    // SendClientInputSystem (qui vient d'ecrire movementDirection depuis le stick) :
    // on lit cette direction, on annule la composante perpendiculaire (la dominante
    // garde sa magnitude = vitesse analogique preservee), on reecrit. Cede au
    // point-and-click (MoveCommand) pour ne pas se marcher dessus. Meme pont ECS que
    // PlayerMoveToSystem (cf. BuildModeNavigator).
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(RunSimulationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(SendClientInputSystem))]
    public partial class DirectionSnapSystem : SystemBase
    {
        private const float SnapDeadzone = 0.1f;
        private EntityQuery _query;

        protected override void OnCreate()
        {
            _query = GetEntityQuery(
                ComponentType.ReadWrite<ClientInputData>(),
                ComponentType.ReadOnly<GhostOwnerIsLocal>());
        }

        protected override void OnUpdate()
        {
            if (!DirectionAssist.Active || MoveCommand.Active) return;
            var entities = _query.ToEntityArray(Allocator.Temp);
            try
            {
                if (entities.Length == 0) return;
                Entity e = entities[0];
                ClientInputData data = EntityManager.GetComponentData<ClientInputData>(e);
                ClientInput ci = UnsafeUtility.As<ClientInputData, ClientInput>(ref data);

                float2 m = ci.movementDirection;
                if (math.length(m) > SnapDeadzone)
                {
                    if (math.abs(m.x) >= math.abs(m.y)) m.y = 0f;   // est-ouest domine
                    else m.x = 0f;                                  // nord-sud domine
                    ci.movementDirection = m;
                    data = UnsafeUtility.As<ClientInput, ClientInputData>(ref ci);
                    EntityManager.SetComponentData(e, data);
                }
            }
            finally { entities.Dispose(); }
        }
    }

    // Gel de la marche pendant la touche access (Triangle tenu). Le stick gauche pilote
    // alors la roue de stats (cf. StatsWheel) au lieu de deplacer le perso : on annule
    // movementDirection tant que Triangle est tenu. Meme pont ECS que DirectionSnapSystem
    // (apres SendClientInputSystem, qui vient d'ecrire le mouvement depuis le stick).
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(RunSimulationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(SendClientInputSystem))]
    public partial class AccessKeyMovementLockSystem : SystemBase
    {
        private EntityQuery _query;

        protected override void OnCreate()
        {
            _query = GetEntityQuery(
                ComponentType.ReadWrite<ClientInputData>(),
                ComponentType.ReadOnly<GhostOwnerIsLocal>());
        }

        protected override void OnUpdate()
        {
            // Aussi gele en mode R1 de la roue de saut barre rapide (stick gauche vole a
            // la roue, cf. HotbarJumpWheel) : meme geste que la touche access elle-meme.
            if (!CoreKeeperAccess.Controls.InfoKey.ModifierHeld
                && !CoreKeeperAccess.Controls.InfoKey.HotbarWheelRight) return;
            var entities = _query.ToEntityArray(Allocator.Temp);
            try
            {
                if (entities.Length == 0) return;
                Entity e = entities[0];
                ClientInputData data = EntityManager.GetComponentData<ClientInputData>(e);
                ClientInput ci = UnsafeUtility.As<ClientInputData, ClientInput>(ref data);
                if (math.lengthsq(ci.movementDirection) > 0f)
                {
                    ci.movementDirection = float2.zero;
                    data = UnsafeUtility.As<ClientInput, ClientInputData>(ref ci);
                    EntityManager.SetComponentData(e, data);
                }
            }
            finally { entities.Dispose(); }
        }
    }
}
