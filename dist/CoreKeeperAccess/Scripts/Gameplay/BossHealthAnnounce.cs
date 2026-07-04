using Unity.Collections;
using Unity.Entities;

namespace CoreKeeperAccess.Gameplay
{
    // Pont mod <-> BossHealthScanSystem.
    internal static class BossHealthScan
    {
        public static bool Found;
        public static int Health;
        public static int MaxHealth;
    }

    // Detection GENERIQUE d'un boss en vie (marqueur BossCD, deja utilise par AggroSentinel
    // pour discriminer boss/mob) + son HealthCD (repliqu, GhostField confirme - cf. usage
    // existant sur BirdBossStone dans AzeosScan.cs). Systeme dedie, separe des autres scans
    // (meme choix que AzeosScanSystem/BossEggScanSystem) - pas specifique a un boss donne.
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class BossHealthScanSystem : SystemBase
    {
        private const float ScanInterval = 0.2f;
        private EntityQuery _query;
        private float _next;

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
            var ents = _query.ToEntityArray(Allocator.Temp);
            bool found = false; int health = 0, maxHealth = 0;
            foreach (var e in ents)
            {
                var hp = EntityManager.GetComponentData<HealthCD>(e);
                // Cadavre ecarte : vie a zero mais entite encore presente ~2s (destroyTimer
                // natif, cf. memoire Azeos) le temps de l'anim de destruction.
                if (hp.health <= 0) continue;
                found = true;
                health = hp.health;
                maxHealth = hp.maxHealth;
                break; // un seul boss suivi a la fois (limite assumee, pas de multi-boss)
            }
            ents.Dispose();

            BossHealthScan.Found = found;
            BossHealthScan.Health = health;
            BossHealthScan.MaxHealth = maxHealth;
        }
    }

    // Annonce parlee de la vie du boss tous les 10% (front DESCENDANT, one-shot par palier
    // franchi). Meme principe que PlayAzeosShapeCallout : WAV TTS pre-rendu par palier,
    // cf. GameplayAudio.PlayBossHealthCallout. Generique (BossCD), pas specifique a Azeos.
    internal static class BossHealthAnnounce
    {
        private static bool _everSeen;
        private static int _lastBucket = 100;

        public static void Tick()
        {
            if (!A11ySettings.BossHealthCallouts || !BossHealthScan.Found || BossHealthScan.MaxHealth <= 0)
            {
                _everSeen = false;
                return;
            }

            int percent = (int)(100L * BossHealthScan.Health / BossHealthScan.MaxHealth);
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
