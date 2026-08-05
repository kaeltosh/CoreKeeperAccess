using Unity.Collections;
using Unity.Entities;

namespace CoreKeeperAccess.Gameplay
{
    // Pont mod <-> BossHealthScanSystem : etat complet du boss actuellement suivi.
    // Consomme par BossAnnounce (evenements generiques) et BossHealthAnnounce (vie).
    internal static class BossScan
    {
        public static bool Found;
        public static Entity Target;
        public static ObjectID ObjId;
        public static int Health;
        public static int MaxHealth;
        public static bool Enraged;
        public static bool HasPhase;      // le boss expose PhaseTransitionStateCD
        public static int Phase;          // currentSyncedPhase (numero NON annonce, cf. BossAnnounce)
        public static bool Invulnerable;
        public static bool HasAppearedInfo; // le boss expose un <Boss>HasAppearedCD
        public static bool Appeared;
        public static int TargetToken;    // incremente a chaque nouvelle cible verrouillee
        public static int DeathToken;     // incremente quand la cible suivie tombe a zero
        public static ObjectID DeadObjId; // le boss du dernier DeathToken
    }

    // Detection GENERIQUE d'un boss en vie (marqueur BossCD, deja utilise par AggroSentinel
    // pour discriminer boss/mob) + son HealthCD, son EnrageStateCD et son
    // PhaseTransitionStateCD - tous confirmes repliques au client (serializer ghost genere
    // cote jeu). Systeme dedie, separe des autres scans (meme choix que AzeosScanSystem /
    // BossEggScanSystem) - pas specifique a un boss donne.
    //
    // NE PAS RENOMMER cette classe : le fichier .g.cs qui l'enregistre n'est produit que
    // par un build Unity complet, jamais par fast-build.
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class BossHealthScanSystem : SystemBase
    {
        private const float ScanInterval = 0.2f;
        private EntityQuery _query;
        private float _next;
        private bool _multiLogged;

        protected override void OnCreate()
        {
            _query = GetEntityQuery(
                ComponentType.ReadOnly<BossCD>(),
                ComponentType.ReadOnly<HealthCD>());
        }

        protected override void OnUpdate()
        {
            float now = UnityEngine.Time.unscaledTime;
            if (now < _next) return;
            _next = now + ScanInterval;

            try { Scan(); }
            catch (System.Exception ex) { Diag.Error("A11yBossHealthScan", ex); }
        }

        private void Scan()
        {
            Entity target = BossScan.Target;

            if (!IsAliveBoss(target))
            {
                // Mort CONSTATEE = vie a zero sous les yeux (le cadavre reste ~2 s avant
                // suppression, destroyTimer natif). Une disparition seche (chunk decharge,
                // joueur qui s'eloigne, deconnexion) ne compte PAS comme une victoire : on
                // relache la cible en silence.
                if (target != Entity.Null && EntityManager.Exists(target)
                    && EntityManager.HasComponent<HealthCD>(target)
                    && EntityManager.GetComponentData<HealthCD>(target).health <= 0)
                {
                    BossScan.DeadObjId = BossScan.ObjId;
                    BossScan.DeathToken++;
                }

                SetTarget(PickTarget());
                target = BossScan.Target;
            }
            else if (!InCombat(target))
            {
                // Le verrou ne doit pas rester colle a un boss au repos (Glurch attend dans
                // sa salle des la generation du monde) pendant qu'on en combat un autre :
                // on ne le lache que pour un boss REELLEMENT engage.
                var challenger = PickTarget();
                if (challenger != Entity.Null && challenger != target && InCombat(challenger))
                {
                    SetTarget(challenger);
                    target = challenger;
                }
            }

            if (target == Entity.Null)
            {
                BossScan.Found = false;
                _multiLogged = false;
                return;
            }

            var hp = EntityManager.GetComponentData<HealthCD>(target);
            BossScan.Found = true;
            BossScan.Health = hp.health;
            BossScan.MaxHealth = hp.maxHealth;
            BossScan.ObjId = EntityManager.HasComponent<ObjectDataCD>(target)
                ? EntityManager.GetComponentData<ObjectDataCD>(target).objectID
                : ObjectID.None;

            BossScan.Enraged = EntityManager.HasComponent<EnrageStateCD>(target)
                && EntityManager.GetComponentData<EnrageStateCD>(target).isEnraged;

            if (EntityManager.HasComponent<PhaseTransitionStateCD>(target))
            {
                var ph = EntityManager.GetComponentData<PhaseTransitionStateCD>(target);
                BossScan.HasPhase = true;
                BossScan.Phase = ph.currentSyncedPhase;
                BossScan.Invulnerable = ph.isInvulnerable;
            }
            else
            {
                BossScan.HasPhase = false;
                BossScan.Invulnerable = false;
            }

            ReadHasAppeared(target);
        }

        // Apparition / disparition. Trois boss reprennent le mecanisme d'Azeos, chacun avec
        // SON composant (pas de composant commun cote jeu) : on les teste a la suite.
        // BirdBossHasAppearedCD est volontairement absent - Azeos a ses propres annonces
        // ("atterrit" / "disparait") dans AzeosBoss, les lire ici ferait un doublon.
        private void ReadHasAppeared(Entity target)
        {
            if (EntityManager.HasComponent<OctopusBossHasAppearedCD>(target))
            {
                BossScan.HasAppearedInfo = true;
                BossScan.Appeared = EntityManager.GetComponentData<OctopusBossHasAppearedCD>(target).Value;
            }
            else if (EntityManager.HasComponent<ScarabBossHasAppearedCD>(target))
            {
                BossScan.HasAppearedInfo = true;
                BossScan.Appeared = EntityManager.GetComponentData<ScarabBossHasAppearedCD>(target).Value;
            }
            else if (EntityManager.HasComponent<GiantCicadaBossHasAppearedCD>(target))
            {
                BossScan.HasAppearedInfo = true;
                BossScan.Appeared = EntityManager.GetComponentData<GiantCicadaBossHasAppearedCD>(target).Value;
            }
            else
            {
                BossScan.HasAppearedInfo = false;
                BossScan.Appeared = false;
            }
        }

        private void SetTarget(Entity e)
        {
            if (e != BossScan.Target) BossScan.TargetToken++;
            BossScan.Target = e;
        }

        private bool IsAliveBoss(Entity e)
        {
            return e != Entity.Null
                && EntityManager.Exists(e)
                && EntityManager.HasComponent<HealthCD>(e)
                && EntityManager.GetComponentData<HealthCD>(e).health > 0;
        }

        // Drapeau natif du jeu (IsInCombatSystem), replique - meme source que la sentinelle
        // d'aggro pour decider qu'une creature est engagee.
        private bool InCombat(Entity e)
        {
            return EntityManager.HasComponent<IsInCombatCD>(e)
                && EntityManager.GetComponentData<IsInCombatCD>(e).isInCombat;
        }

        // Choix de la barre a suivre, VERROUILLEE ensuite (cf. Scan).
        //
        // Les boss MULTI-ENTITES (mur a segments, hydre a plusieurs tetes, robot a jambes
        // detachables, coeur a orbes) presentent plusieurs entites marquees boss en meme
        // temps : "la premiere trouvee" faisait sauter le suivi de l'une a l'autre entre
        // deux scans. Criteres, du plus fort au plus faible :
        //   1. le boss est ENGAGE (IsInCombatCD) - ecarte un boss qui dort ailleurs ;
        //   2. le boss designe lui-meme son entite principale (WallBossCD.isMainEntity) ;
        //   3. la plus grosse reserve de vie - les parties detachables (jambes, orbes,
        //      points faibles) en ont toujours moins que le corps.
        private Entity PickTarget()
        {
            var ents = _query.ToEntityArray(Allocator.Temp);
            Entity best = Entity.Null;
            bool bestCombat = false, bestMain = false;
            int bestMax = -1;
            int alive = 0;

            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                var hp = EntityManager.GetComponentData<HealthCD>(e);
                if (hp.health <= 0) continue;
                alive++;

                bool combat = InCombat(e);
                bool main = EntityManager.HasComponent<WallBossCD>(e)
                    && EntityManager.GetComponentData<WallBossCD>(e).isMainEntity;

                if (best != Entity.Null)
                {
                    if (combat != bestCombat) { if (!combat) continue; }
                    else if (main != bestMain) { if (!main) continue; }
                    else if (hp.maxHealth <= bestMax) continue;
                }

                best = e; bestCombat = combat; bestMain = main; bestMax = hp.maxHealth;
            }

            if (alive > 1) LogMulti(ents);
            ents.Dispose();
            return best;
        }

        // Instrumentation posee a l'avance : le jour ou l'un des boss multi-entites est
        // rencontre pour de vrai, le log dit exactement quelles entites portent BossCD et
        // avec quelle vie -> le reglage se fait sur donnees, pas sur supposition.
        private void LogMulti(NativeArray<Entity> ents)
        {
            if (_multiLogged) return;
            _multiLogged = true;

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < ents.Length; i++)
            {
                var hp = EntityManager.GetComponentData<HealthCD>(ents[i]);
                if (hp.health <= 0) continue;
                var id = EntityManager.HasComponent<ObjectDataCD>(ents[i])
                    ? EntityManager.GetComponentData<ObjectDataCD>(ents[i]).objectID
                    : ObjectID.None;
                if (sb.Length > 0) sb.Append(" | ");
                sb.Append(id).Append(' ').Append(hp.health).Append('/').Append(hp.maxHealth);
            }
            Diag.Log("A11yBossMulti", "entites boss vivantes : " + sb);
        }
    }

    // Annonce parlee de la vie du boss tous les 10% (one-shot par palier franchi). Canal
    // AUDIO DEDIE (WAV de voix pre-rendu, volume propre, hors file NVDA), pas TtsText -
    // choix confirme le 5 aout 2026 : c'est la seule annonce assez frequente pour meriter
    // son propre canal. Multilingue depuis, cf. GameplayAudio.PlayBossHealthCallout.
    internal static class BossHealthAnnounce
    {
        private static bool _everSeen;
        private static int _lastBucket = 100;
        private static int _seenTargetToken = -1;

        public static void Tick()
        {
            if (!A11ySettings.BossHealthCallouts || !BossScan.Found || BossScan.MaxHealth <= 0)
            {
                _everSeen = false;
                return;
            }

            // Cible changee : on repart d'un palier de reference propre plutot que de
            // comparer la vie d'un boss avec celle du precedent.
            if (BossScan.TargetToken != _seenTargetToken)
            {
                _seenTargetToken = BossScan.TargetToken;
                _everSeen = false;
            }

            int percent = (int)(100L * BossScan.Health / BossScan.MaxHealth);
            if (percent > 100) percent = 100;
            int bucket = (percent / 10) * 10;

            if (!_everSeen)
            {
                // Pas d'annonce a la toute premiere lecture (evite un faux palier en
                // arrivant en cours de combat, meme piege que AzeosBoss.TickBossState).
                _everSeen = true;
                _lastBucket = bucket;
                return;
            }

            if (bucket != _lastBucket)
            {
                // Symetrique (choix utilisateur, 3 juillet) : un soin qui fait remonter la
                // vie DOIT s'entendre, meme prix la spam si ca oscille pile sur un seuil -
                // masquer une info de soin est pire que le bruit.
                _lastBucket = bucket;
                if (bucket > 0) GameplayAudio.PlayBossHealthCallout(bucket);
            }
        }
    }
}
