using HarmonyLib;
using Unity.Mathematics;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Decide, a chaque pas, si le perso bouge vraiment et comment.
    //
    // On mesure le DEPLACEMENT REEL depuis le pas precedent (delta de WorldPosition)
    // plutot que la velocite instantanee EffectiveVelocityCD : cette derniere s'est
    // revelee BRUITEE au moment d'un pas (oscille entre 0 et la vraie vitesse selon le
    // timing tick de simulation / animation), ce qui coupait des pas pendant un vrai
    // mouvement (mouvement silencieux a l'entree d'un glissement). Le delta de position
    // entre deux pas, lui, est stable.
    //
    //  - deplacement ~0 alors qu'on pousse        -> BLOQUE (immobile contre un mur)
    //  - deplacement non nul mais DEVIE du cap     -> GLISSEMENT (slide01 = ampleur, via l'angle)
    //  - deplacement aligne avec le cap            -> LIBRE
    internal static class MovementReader
    {
        internal enum MoveState { NoIntent, Free, Sliding, Blocked }

        private const float IntentDeadzone = 0.1f;  // intention traitee (0..1) : en dessous on ne pousse pas
        private const float MoveEpsilon = 0.05f;    // deplacement mini entre deux pas pour "bouge" (sinon immobile)
        private const float SlideAngleMin = 30f;    // degres d'ecart cap/reel a partir desquels c'est du glissement
        private const bool Diag = false;            // log de calibration (coupe : footstep calibre)

        private static readonly AccessTools.FieldRef<PlayerController, Vector3> TargetVel =
            AccessTools.FieldRefAccess<PlayerController, Vector3>("targetMovementVelocity");

        private static Vector3 _lastPos;
        private static bool _hasLast;

        // slide01 (0..1) = ampleur du glissement, pour doser l'espacement des pas.
        public static MoveState Evaluate(PlayerController player, out float slide01)
        {
            slide01 = 0f;
            if (player == null) return MoveState.NoIntent;

            // Deplacement reel depuis le pas precedent (toujours mis a jour).
            Vector3 pos = player.WorldPosition;
            float2 disp = new float2(pos.x - _lastPos.x, pos.z - _lastPos.z);
            bool hadLast = _hasLast;
            _lastPos = pos;
            _hasLast = true;
            if (!hadLast) return MoveState.Free; // premier pas de reference : on joue

            Vector3 intent = TargetVel(player);
            float2 intentXZ = new float2(intent.x, intent.z);
            float intentMag = math.length(intentXZ);
            if (intentMag < IntentDeadzone) return MoveState.NoIntent; // on ne pousse pas : laisser le pas natif

            float dist = math.length(disp);
            MoveState state;
            float angle = 0f;
            if (dist < MoveEpsilon)
            {
                state = MoveState.Blocked;
            }
            else
            {
                float2 moveDir = disp / dist;
                float2 intentDir = intentXZ / intentMag;
                float cos = math.clamp(math.dot(moveDir, intentDir), -1f, 1f);
                angle = math.degrees(math.acos(cos));
                if (angle > SlideAngleMin)
                {
                    slide01 = math.clamp((angle - SlideAngleMin) / (90f - SlideAngleMin), 0f, 1f);
                    state = MoveState.Sliding;
                }
                else
                {
                    state = MoveState.Free;
                }
            }

            if (Diag && state != MoveState.Free)
                Debug.Log($"[A11yNavDiag] {state} dist={dist:0.00} angle={angle:0}deg slide={slide01:0.00}");
            return state;
        }
    }
}
