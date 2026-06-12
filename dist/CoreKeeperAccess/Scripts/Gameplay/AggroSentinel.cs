using System.Collections.Generic;
using CoreKeeperAccess.Patches;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Sentinelle d'aggro : rend BRUYANTS les monstres qui nous CHASSENT (et seulement
    // ceux-la). Bequille anti-triche en attendant la gestion de l'eclairage : on ne
    // revele que les mobs qui nous ont DEJA reperes (l'IA du jeu a notre joueur pour
    // cible), donc aucune info que le jeu ne donnerait pas a un voyant attaque.
    // Suivi STABLE (choix utilisateur : "repere une fois, il bipe tant qu'il est a
    // l'ecran") : assure par le flag natif IsInCombatCD du jeu, qui couvre tous les
    // archetypes d'IA et garde 3 s d'hysteresis, cf. AggroSentinelSystem.
    //
    // Signal : bip sinusoidal genere (40 ms, doux), JAMAIS superpose -> file d'attente,
    // un creneau de 100 ms par bip. Cycle de rappel 1 s => jauge : 1 chasseur = 1 bip/s,
    // N chasseurs = N bips egrenes puis silence -> on COMPTE les attaquants a l'oreille.
    // Position au moment ou le bip JOUE (pas a l'empilage) : pan = est/ouest, pitch =
    // +1 demi-ton par ligne nord (langage sonore du mod), volume attenue avec la distance.
    // TTS du nom une seule fois, a la PREMIERE aggro d'un mob.
    //
    // Architecture : pont mod <-> systeme ECS facon LaserCane. Le mod (Tick) publie
    // l'entite joueur, le systeme AggroSentinelSystem filtre les ChaseStateCD qui nous
    // ciblent et publie la liste ; le mod sequence bips et TTS.
    internal static class AggroSentinel
    {
        private const float SlotInterval = 0.1f;   // 100 ms entre deux bips (file, jamais superposes)
        private const float CycleInterval = 1f;    // cadence de rappel de la jauge
        private const float BossInterval = 0.35f;  // cadence dediee BOSS (demande utilisateur :
                                                   // plus frequent qu'un mob, et un bip DIFFERENT)
        private const float MaxAudibleDist = 30f;  // au-dela, volume plancher
        private const float BaseVolume = 0.5f;     // "doux" - a regler a l'oreille

        private static readonly HashSet<int> _known = new HashSet<int>();   // chasseurs deja annonces (TTS)
        private static readonly HashSet<int> _current = new HashSet<int>(); // tampon reutilise
        private static readonly Queue<int> _queue = new Queue<int>();
        private static readonly List<KeyValuePair<int, float>> _sort = new List<KeyValuePair<int, float>>();
        private static float _nextCycle;
        private static float _nextSlot;
        private static float _nextBossBeep;
        private static int _lastVersion;

        public static void Tick()
        {
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null) { Reset(); return; }

            // Menu (pause...) ouvert : monde fige -> on suspend les bips sans perdre
            // l'etat. Inventaire/carte ouverts : on RESTE actif, le jeu continue en
            // temps reel et c'est precisement la qu'on se fait surprendre.
            if (Manager.menu != null && Manager.menu.IsAnyMenuActive()) return;

            // Salve du ping sonar en cours : fenetre sonore reservee, les bips de la
            // sentinelle attendent dans leur file (rien n'est perdu, juste differe).
            if (PingSonar.Silencing) return;

            // Publie le rectangle ecran approximatif en coordonnees MONDE (camera ortho
            // centree joueur) : le systeme s'en sert pour ecarter les mobs hors ecran.
            // Demi-largeur/hauteur en cases + marge d'une case.
            float2 playerPos = new float2(player.WorldPosition.x, player.WorldPosition.z);
            var cam0 = Manager.camera != null ? Manager.camera.gameCamera : null;
            AggroScan.PlayerPos = playerPos;
            AggroScan.CamHalf = cam0 != null
                ? new float2(cam0.orthographicSize * cam0.aspect + 1f, cam0.orthographicSize + 1f)
                : new float2(12f, 8f);
            AggroScan.Active = true;
            if (!AggroScan.ResultValid) return;
            float now = Time.unscaledTime;

            // Nouveaux chasseurs -> TTS du nom, une fois par aggro (le scan a une
            // version : on ne re-derive l'ensemble qu'a la mise a jour du systeme).
            if (AggroScan.Version != _lastVersion)
            {
                _lastVersion = AggroScan.Version;
                _current.Clear();
                string names = null;
                for (int i = 0; i < AggroScan.Count; i++)
                {
                    var c = AggroScan.Chasers[i];
                    _current.Add(c.Key);
                    if (_known.Add(c.Key))
                    {
                        string n = InGameTtsCore.ResolveObjectName(c.Obj);
                        if (!string.IsNullOrEmpty(n))
                            names = names == null ? n : names + ", " + n;
                    }
                }
                _known.RemoveWhere(k => !_current.Contains(k));
                if (names != null) TtsText.Say(names, true);
            }

            // BOSS : canal dedie, cadence rapide (BossInterval), bip GRAVE distinct
            // (PlayBossTone) - exclu de la file standard pour ne pas doubler. Respecte
            // le creneau global _nextSlot (jamais deux bips superposes). S'il y a
            // plusieurs boss (rarissime), le plus proche porte le signal.
            if (now >= _nextBossBeep && now >= _nextSlot && TryGetNearestBoss(playerPos, out float2 bossPos))
            {
                PlayBeep(bossPos, playerPos, true);
                _nextBossBeep = now + BossInterval;
                _nextSlot = now + SlotInterval;
            }

            // Recharge de la file : seulement quand elle est VIDE et que le cycle est
            // echu -> si plus de 10 chasseurs, le cycle glisse, la file ne deborde pas.
            // Tri du plus proche au plus lointain : la menace prioritaire bipe en premier.
            // Les boss n'y entrent pas (canal dedie ci-dessus).
            if (_queue.Count == 0 && now >= _nextCycle && AggroScan.Count > 0)
            {
                _sort.Clear();
                for (int i = 0; i < AggroScan.Count; i++)
                {
                    if (AggroScan.Chasers[i].IsBoss) continue;
                    _sort.Add(new KeyValuePair<int, float>(
                        AggroScan.Chasers[i].Key,
                        math.distancesq(AggroScan.Chasers[i].Pos, playerPos)));
                }
                _sort.Sort((a, b) => a.Value.CompareTo(b.Value));
                foreach (var kv in _sort) _queue.Enqueue(kv.Key);
                _nextCycle = now + CycleInterval;
            }

            // Dispatcher : un bip par creneau de 100 ms, position FRAICHE (le mob a pu
            // bouger depuis l'empilage). Un chasseur disparu (mort, desaggro) est saute.
            if (_queue.Count > 0 && now >= _nextSlot)
            {
                while (_queue.Count > 0)
                {
                    int key = _queue.Dequeue();
                    if (!TryGetChaser(key, out float2 pos)) continue;
                    PlayBeep(pos, playerPos);
                    _nextSlot = now + SlotInterval;
                    break;
                }
            }
        }

        private static bool TryGetChaser(int key, out float2 pos)
        {
            for (int i = 0; i < AggroScan.Count; i++)
                if (AggroScan.Chasers[i].Key == key) { pos = AggroScan.Chasers[i].Pos; return true; }
            pos = default;
            return false;
        }

        private static bool TryGetNearestBoss(float2 playerPos, out float2 pos)
        {
            pos = default;
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < AggroScan.Count; i++)
            {
                if (!AggroScan.Chasers[i].IsBoss) continue;
                float d2 = math.distancesq(AggroScan.Chasers[i].Pos, playerPos);
                if (d2 < best) { best = d2; pos = AggroScan.Chasers[i].Pos; found = true; }
            }
            return found;
        }

        // Bip positionnel : pan gauche-droite normalise sur la demi-largeur ecran +
        // pitch vertical (+1 demi-ton/ligne, borne a une octave) + volume-distance.
        // boss=true -> bip grave dedie (PlayBossTone), meme langage positionnel.
        private static void PlayBeep(float2 worldPos, float2 playerPos, bool boss = false)
        {
            float2 d = worldPos - playerPos;
            var cam = Manager.camera != null ? Manager.camera.gameCamera : null;
            float halfW = cam != null ? cam.orthographicSize * cam.aspect : 0f;
            float pan = halfW > 0.1f ? Mathf.Clamp(d.x / halfW, -1f, 1f) : 0f;
            float pitch = Mathf.Clamp(Mathf.Pow(2f, d.y / 12f), 0.5f, 2f);
            float volume = BaseVolume * Mathf.Clamp(1f - math.length(d) / MaxAudibleDist, 0.15f, 1f);
            if (boss) GameplayAudio.PlayBossTone(pan, pitch, volume);
            else GameplayAudio.PlayTone(pan, pitch, volume);
        }

        private static void Reset()
        {
            AggroScan.Active = false;
            AggroScan.ResultValid = false;
            _known.Clear();
            _queue.Clear();
            _lastVersion = 0;
        }
    }

    // Pont mod <-> AggroSentinelSystem. Le mod arme Active, le systeme publie la liste
    // des chasseurs (version incrementee a chaque scan pour la detection de nouveaute
    // cote mod).
    internal static class AggroScan
    {
        public struct Chaser
        {
            public int Key;        // index d'entite (identite du chasseur)
            public float2 Pos;     // position monde (xz)
            public ObjectID Obj;   // type de creature (TTS du nom)
            public bool IsBoss;    // entite marquee BossCD -> bip dedie, cadence rapide
        }

        public static bool Active;
        public static float2 PlayerPos;  // centre approximatif de l'ecran (camera collee au joueur)
        public static float2 CamHalf;    // demi-largeur/hauteur visibles en cases (+ marge)

        public static bool ResultValid;
        public static int Version;
        public static int Count;
        public static readonly Chaser[] Chasers = new Chaser[32];
    }

    // Factions considerees hostiles - liste d'EXCLUSION partagee avec la canne laser :
    // tout ce qui n'est pas la-dedans compte comme ennemi.
    internal static class HostileFilter
    {
        public static bool IsHostile(FactionID f)
        {
            switch (f)
            {
                case FactionID.None:
                case FactionID.Player:
                case FactionID.Neutral:
                case FactionID.Merchant:
                case FactionID.PlayerMinion:
                case FactionID.Explosion:
                    return false;
                default:
                    return true;
            }
        }

        // Slimes-masses ambiants : faction hostile + EnemyCD mais inertes tant qu'on ne
        // les reveille pas. La liste est celle de ClaimBedSystem (le jeu lui-meme les
        // exclut de son test "ennemis a proximite"). Partage laser / ping sonar.
        public static bool IsDormantSlime(ObjectID id)
            => id == ObjectID.SlimeBlob
            || id == ObjectID.SlipperySlimeBlob
            || id == ObjectID.PoisonSlimeBlob;
    }

    // Enumere les mobs hostiles EN COMBAT visibles a l'ecran, via le flag natif
    // IsInCombatCD.isInCombat : le jeu le maintient lui-meme (IsInCombatSystem) pour
    // TOUS les archetypes d'IA - Chase, mais aussi Charge (champignommes qui foncent
    // tout droit), toutes les attaques, les etats de boss, Stunned, TookDamage... -
    // avec une HYSTERESIS native de 3 s a la sortie de combat -> aucun clignotement,
    // exit le suivi maison de ChaseStateCD (cible posee par fenetres trop breves chez
    // les chargeurs = bips aleatoires). Champ [GhostField] REPLIQUE au monde client ->
    // le systeme tourne en ClientSimulation (et marchera aussi en multi non-hote).
    // Nuance assumee : "en combat" n'est pas "contre NOUS" specifiquement (en solo,
    // c'est equivalent ; le filtre faction hostile reste le garde-fou).
    // Query construite a la main (GetEntityQuery + ToEntityArray) : pas de
    // SystemAPI.Query en hot-compile (source generator absent, cf. fiche ECS).
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class AggroSentinelSystem : SystemBase
    {
        private const float ScanInterval = 0.2f; // 5 Hz : position assez fraiche pour la file de bips

        private EntityQuery _query;
        private float _next;
        private bool _errLogged;

        protected override void OnCreate()
        {
            _query = GetEntityQuery(
                ComponentType.ReadOnly<IsInCombatCD>(),
                ComponentType.ReadOnly<LocalTransform>());
            // Diag de mise au point : confirme dans quel monde le systeme s'enregistre.
            Debug.Log("[A11yAggroDiag] AggroSentinelSystem cree dans " + World.Name);
        }

        protected override void OnUpdate()
        {
            if (!AggroScan.Active) return;
            if (UnityEngine.Time.unscaledTime < _next) return;
            _next = UnityEngine.Time.unscaledTime + ScanInterval;

            try
            {
                int n = 0;
                var ents = _query.ToEntityArray(Allocator.Temp);
                foreach (var e in ents)
                {
                    if (n >= AggroScan.Chasers.Length) break;
                    if (!EntityManager.GetComponentData<IsInCombatCD>(e).isInCombat) continue;

                    // Memes garde-fous que la canne laser : pas de critter, faction hostile.
                    if (EntityUtility.HasComponentData<CritterCD>(e, World)) continue;
                    if (!EntityUtility.HasComponentData<FactionCD>(e, World)
                        || !HostileFilter.IsHostile(EntityUtility.GetComponentData<FactionCD>(e, World).faction)) continue;

                    // Mort : l'etat Death n'est pas replique au client, mais la VIE l'est.
                    // Sans ce filtre, l'hysteresis de 3 s ferait biper le cadavre.
                    if (EntityUtility.HasComponentData<HealthCD>(e, World)
                        && EntityUtility.GetComponentData<HealthCD>(e, World).health <= 0) continue;

                    // "Tant qu'il est a l'ecran" (choix utilisateur) : rectangle camera
                    // approximatif publie par le mod, marge d'une case.
                    var pos = EntityUtility.GetComponentData<LocalTransform>(e, World).Position;
                    float2 p = new float2(pos.x, pos.z);
                    float2 d = p - AggroScan.PlayerPos;
                    if (math.abs(d.x) > AggroScan.CamHalf.x || math.abs(d.y) > AggroScan.CamHalf.y) continue;

                    ObjectID obj = EntityUtility.HasComponentData<ObjectDataCD>(e, World)
                        ? EntityUtility.GetComponentData<ObjectDataCD>(e, World).objectID
                        : ObjectID.None;
                    AggroScan.Chasers[n++] = new AggroScan.Chaser
                    {
                        Key = e.Index,
                        Pos = p,
                        Obj = obj,
                        IsBoss = EntityUtility.HasComponentData<BossCD>(e, World),
                    };
                }
                ents.Dispose();

                AggroScan.Count = n;
                AggroScan.Version++;
                AggroScan.ResultValid = true;
            }
            catch (System.Exception ex)
            {
                // Une seule trace (pas de spam a 5 Hz) : sans ca, un echec de query est
                // invisible et la sentinelle meurt en silence.
                if (!_errLogged) { _errLogged = true; Debug.LogError("[A11yAggroDiag] " + ex); }
            }
        }
    }
}
