using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Confirmation audio quand une commande de tome d'invocation "mord" (bouton principal =
    // designer une cible a toutes mes invocations, cf. LaserCane.UpdateTomeFocus). Le champ
    // natif qui dirait directement "cette invocation cible cet ennemi"
    // (MoveToPositionFromCommandStateCD.target) N'EST PAS repliqu au client (aucun GhostField
    // dessus - meme piege deja documente pour ChaseStateCD/StateInfoCD sur la sentinelle
    // d'aggro) -> impossible de lire le succes directement.
    //
    // Verification INDIRECTE a la place : au moment de l'appui, on note la distance la plus
    // proche entre une invocation possedee et la cible visee ; sur la seconde qui suit, si
    // cette distance diminue nettement (une invocation s'est mise en route vers la cible),
    // on considere que ca a mordu -> bip carre de confirmation (GameplayAudio.PlayTomeLock).
    // Si rien de net ne se passe dans la fenetre, SILENCE (jamais de faux positif signale a
    // tort - demande explicite utilisateur). La position des invocations (LocalToWorld), elle,
    // EST repliquee (necessaire au rendu) -> lisible sans risque.
    internal static class MinionCommandFeedback
    {
        private const float WatchWindowSeconds = 1f;
        private const float BiteThresholdTiles = 0.5f; // rapprochement net exige, pas du bruit de mouvement

        private static bool _watching;
        private static float2 _watchTarget;
        private static float _watchStartDist;
        private static float _watchDeadline;

        // Appele par LaserCane.UpdateTomeFocus a chaque tick ou un tome de commande est
        // equipe. commandPressed = bouton principal (Interact) presse CE tick. haveEnemyTarget
        // = la cible visee est un vrai ennemi (pas le repli position joueur - inutile de
        // surveiller une commande qui vise personne).
        public static void Tick(PlayerController player, bool commandPressed, bool haveEnemyTarget, float2 target)
        {
            if (player == null) { Disable(); return; }
            MinionScan.OwnerEntity = player.entity;
            MinionScan.Active = true;

            if (commandPressed && haveEnemyTarget)
            {
                float d = MinDistanceTo(target);
                if (d < float.MaxValue) // au moins une invocation possedee existe
                {
                    _watching = true;
                    _watchTarget = target;
                    _watchStartDist = d;
                    _watchDeadline = Time.unscaledTime + WatchWindowSeconds;
                }
            }

            if (!_watching) return;

            float dist = MinDistanceTo(_watchTarget);
            if (dist < _watchStartDist - BiteThresholdTiles)
            {
                _watching = false;
                GameplayAudio.PlayTomeLock(A11ySettings.NavigationVolume);
            }
            else if (Time.unscaledTime >= _watchDeadline)
            {
                _watching = false; // rien de net observe -> pas de son, pas de faux positif
            }
        }

        public static void Disable()
        {
            _watching = false;
            MinionScan.Active = false;
        }

        private static float MinDistanceTo(float2 target)
        {
            if (!MinionScan.ResultValid || MinionScan.Count == 0) return float.MaxValue;
            float best = float.MaxValue;
            for (int i = 0; i < MinionScan.Count; i++)
            {
                float d = math.length(MinionScan.Positions[i] - target);
                if (d < best) best = d;
            }
            return best;
        }
    }

    // Pont mod <-> MinionWatchSystem, meme facon que LaserScan/TileReader.
    internal static class MinionScan
    {
        public const int MaxMinions = 8; // meme ordre de grandeur que MinionCountUI (achievement > 7)

        public static bool Active;
        public static Entity OwnerEntity;

        public static bool ResultValid;
        public static int Count;
        public static readonly float2[] Positions = new float2[MaxMinions];
    }

    // Publie la position des invocations possedees par le joueur local (MinionCD +
    // OwnerReferenceCD.owner == joueur). Lecture cadence lente (10 Hz) : sert juste a detecter
    // un DEPLACEMENT net sur ~1s, pas un geste instantane.
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class MinionWatchSystem : SystemBase
    {
        private const float ScanInterval = 0.1f;
        private float _next;
        private EntityQuery _minionQuery;

        protected override void OnCreate()
        {
            _minionQuery = GetEntityQuery(
                ComponentType.ReadOnly<MinionCD>(),
                ComponentType.ReadOnly<OwnerReferenceCD>());
        }

        protected override void OnUpdate()
        {
            if (!MinionScan.Active) return;
            if (UnityEngine.Time.unscaledTime < _next) return;
            _next = UnityEngine.Time.unscaledTime + ScanInterval;

            try
            {
                var ents = _minionQuery.ToEntityArray(Allocator.Temp);
                int n = 0;
                for (int i = 0; i < ents.Length && n < MinionScan.MaxMinions; i++)
                {
                    var e = ents[i];
                    if (EntityManager.GetComponentData<OwnerReferenceCD>(e).owner != MinionScan.OwnerEntity)
                        continue;
                    if (!EntityManager.HasComponent<LocalToWorld>(e)) continue;
                    var pos = EntityManager.GetComponentData<LocalToWorld>(e).Position;
                    MinionScan.Positions[n++] = new float2(pos.x, pos.z);
                }
                ents.Dispose();
                MinionScan.Count = n;
                MinionScan.ResultValid = true;
            }
            catch (System.Exception ex) { Diag.Error("A11yMinionWatchDiag", ex); }
        }
    }
}
