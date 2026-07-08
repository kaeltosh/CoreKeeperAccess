using System.Collections.Generic;
using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Patches;
using PugTilemap;
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
        private const float BaseVolume = 0.5f;     // "doux" - a regler a l'oreille
        private const float BossVolumeScale = 0.6f; // le bip boss (scie, riche en harmoniques)
                                                    // sort plus fort qu'un mob a volume egal -> attenue

        private static readonly HashSet<long> _known = new HashSet<long>();   // chasseurs deja annonces (TTS)
        private static readonly HashSet<long> _current = new HashSet<long>(); // tampon reutilise
        private static readonly Queue<long> _queue = new Queue<long>();
        private static readonly List<KeyValuePair<long, float>> _sort = new List<KeyValuePair<long, float>>();
        private static readonly List<string> _names = new List<string>();     // tampon de composition TTS
        private static float _nextCycle;
        private static float _nextSlot;
        private static float _nextBossBeep;
        private static int _lastVersion;

        public static void Tick()
        {
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null) { Reset(); return; }

            // Coupe-circuit utilisateur (panneau) : on coupe le scan ECS et les bips.
            if (!A11ySettings.SentinelEnabled) { Reset(); return; }

            // Menu (pause...) ouvert : monde fige -> on suspend les bips sans perdre
            // l'etat. Inventaire/carte ouverts : on RESTE actif, le jeu continue en
            // temps reel et c'est precisement la qu'on se fait surprendre.
            if (InputContext.MenuOpen) return;

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
                _names.Clear();
                for (int i = 0; i < AggroScan.Count; i++)
                {
                    var c = AggroScan.Chasers[i];
                    _current.Add(c.Key);
                    if (_known.Add(c.Key))
                        _names.Add(InGameTtsCore.ResolveObjectName(c.Obj));
                }
                _known.RemoveWhere(k => !_current.Contains(k));
                TtsText.Say(TtsText.Compose(_names), true);
            }

            // BOSS : canal dedie, cadence rapide (BossInterval), timbre distinct
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
                    _sort.Add(new KeyValuePair<long, float>(
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
                    long key = _queue.Dequeue();
                    if (!TryGetChaser(key, out float2 pos)) continue;
                    PlayBeep(pos, playerPos);
                    _nextSlot = now + SlotInterval;
                    break;
                }
            }
        }

        private static bool TryGetChaser(long key, out float2 pos)
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

        // Bip positionnel : pan gauche-droite au bareme commun en cases (PanFromTiles)
        // + pitch vertical (+1 demi-ton/ligne, borne a une octave) + volume-distance.
        // boss=true -> timbre boss dedie (PlayBossTone), meme langage positionnel.
        private static void PlayBeep(float2 worldPos, float2 playerPos, bool boss = false)
        {
            float2 d = worldPos - playerPos;
            float pan = GameplayAudio.PanFromTiles(d.x);
            float pitch = Mathf.Clamp(Mathf.Pow(2f, d.y / 12f), 0.5f, 2f);
            float trim = GameplayAudio.DistanceTrim(math.length(d));
            // Volumes SEPARES (reglables a part) : monstres ordinaires vs boss. BossVolumeScale
            // reste la compensation de timbre de base (la scie sort plus fort), multipliee par
            // le volume boss de l'utilisateur.
            if (boss) GameplayAudio.PlayBossTone(pan, pitch, BaseVolume * BossVolumeScale * A11ySettings.SentinelBossVolume * trim);
            else GameplayAudio.PlayTone(pan, pitch, BaseVolume * A11ySettings.SentinelVolume * trim);
        }

        // Apercus sonores (panneau de reglages) : bip de monstre ordinaire / de boss au centre,
        // chacun au volume actuellement regle -> regler a l'oreille sans ennemi sous la main.
        public static void PreviewMob() => GameplayAudio.PlayTone(0f, 1f, BaseVolume * A11ySettings.SentinelVolume);
        public static void PreviewBoss() => GameplayAudio.PlayBossTone(0f, 1f, BaseVolume * BossVolumeScale * A11ySettings.SentinelBossVolume);

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
            public long Key;       // cle d'entite index+version (identite du chasseur)
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
                // Decor destructible (tables, pots, caisses...) : faction native reservee
                // aux objets qui encaissent des coups sans jamais attaquer personne. Sans
                // cette exclusion, un meuble tape (IsInCombatCD s'allume pour TOUTE entite
                // qui prend des degats, pas seulement les monstres) sonnait comme un
                // ennemi a la sentinelle, et sa case ressortait "Ennemi" au scanner.
                case FactionID.CanOnlyBeAttackedByPlayerOrExplosion:
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
        private const float SnarePlantRangeTiles = 8f; // portee d'alerte de la plante immobilisante

        private EntityQuery _query;
        private EntityQuery _eggQuery;
        private float _next;

        protected override void OnCreate()
        {
            _query = GetEntityQuery(
                ComponentType.ReadOnly<IsInCombatCD>(),
                ComponentType.ReadOnly<LocalTransform>());
            // LarvaHiveEgg : actifs (health>0) mais n'attaquent pas -> IsInCombatCD=false.
            // On les injecte directement dans AggroScan comme des chasers normaux.
            _eggQuery = GetEntityQuery(
                ComponentType.ReadOnly<ObjectDataCD>(),
                ComponentType.ReadOnly<HealthCD>(),
                ComponentType.ReadOnly<LocalTransform>());
            Diag.Log("A11yAggroDiag", "AggroSentinelSystem cree dans " + World.Name);
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

                    // Memes garde-fous que la canne laser : pas de critter, une VRAIE creature
                    // (EnemyCD), faction hostile. Decor destructible (tables...) peut porter un
                    // FactionCD herite (ex. Caveling, confirme diag 8 juillet) sans etre EnemyCD :
                    // IsInCombatCD s'allume pour tout ce qui encaisse des degats, EnemyCD est le
                    // seul signal fiable "c'est un monstre".
                    if (EntityUtility.HasComponentData<CritterCD>(e, World)) continue;
                    if (!EntityUtility.HasComponentData<EnemyCD>(e, World)) continue;
                    if (!EntityUtility.HasComponentData<FactionCD>(e, World)
                        || !HostileFilter.IsHostile(EntityUtility.GetComponentData<FactionCD>(e, World).faction)) continue;

                    // Mort : l'etat Death n'est pas replique au client, mais la VIE l'est.
                    // Sans ce filtre, l'hysteresis de 3 s ferait biper le cadavre.
                    if (EntityUtility.HasComponentData<HealthCD>(e, World)
                        && EntityUtility.GetComponentData<HealthCD>(e, World).health <= 0) continue;

                    // "Tant qu'il est a l'ecran" (choix utilisateur) : rectangle camera
                    // approximatif publie par le mod, marge d'une case. EXCEPTION boss
                    // (12 juin) : un boss en combat reste audible HORS camera - les
                    // teleports de Malugaz sortent du cadre et c'est precisement la
                    // qu'il faut le suivre ; le volume-distance dit deja "loin".
                    bool isBoss = EntityUtility.HasComponentData<BossCD>(e, World);
                    var pos = EntityUtility.GetComponentData<LocalTransform>(e, World).Position;
                    float2 p = new float2(pos.x, pos.z);
                    float2 d = p - AggroScan.PlayerPos;
                    if (!isBoss && (math.abs(d.x) > AggroScan.CamHalf.x || math.abs(d.y) > AggroScan.CamHalf.y)) continue;

                    ObjectID obj = EntityUtility.HasComponentData<ObjectDataCD>(e, World)
                        ? EntityUtility.GetComponentData<ObjectDataCD>(e, World).objectID
                        : ObjectID.None;
                    AggroScan.Chasers[n++] = new AggroScan.Chaser
                    {
                        Key = EntityKey.Of(e),
                        Pos = p,
                        Obj = obj,
                        IsBoss = isBoss,
                    };
                }
                ents.Dispose();

                // Oeufs actifs de la Hive Mother + plante immobilisante (SnarePlant) : ni
                // l'un ni l'autre n'allume IsInCombatCD (l'oeuf n'attaque pas ; la plante
                // attaque via l'etat StateID.MeleeAttackContinuous, absent de la liste
                // native d'IsInCombatSystem - StateInfoCD n'est de toute facon pas replique
                // au client, donc pas moyen de lire son etat directement). Injectes ici
                // comme chasers normaux, chacun avec sa propre regle de portee.
                TileAccessor ta = default;
                bool taReady = false;
                var eggs = _eggQuery.ToEntityArray(Allocator.Temp);
                foreach (var egg in eggs)
                {
                    if (n >= AggroScan.Chasers.Length) break;
                    var od = EntityManager.GetComponentData<ObjectDataCD>(egg);
                    if (od.objectID != ObjectID.LarvaHiveEgg && od.objectID != ObjectID.SnarePlant) continue;
                    var hp = EntityManager.GetComponentData<HealthCD>(egg);
                    if (hp.health <= 0) continue;
                    var pos = EntityManager.GetComponentData<LocalTransform>(egg).Position;
                    float2 ep = new float2(pos.x, pos.z);
                    float2 ed = ep - AggroScan.PlayerPos;

                    if (od.objectID == ObjectID.LarvaHiveEgg)
                    {
                        // Zone 2x plus large que la camera : oeufs hors ecran restent audibles.
                        if (math.abs(ed.x) > AggroScan.CamHalf.x * 2f || math.abs(ed.y) > AggroScan.CamHalf.y * 2f) continue;
                    }
                    else // SnarePlant
                    {
                        // Immobile : le rectangle camera entier biperait a travers un mur
                        // pour un danger jamais atteignable. Rayon court + ligne de vue.
                        if (math.lengthsq(ed) > SnarePlantRangeTiles * SnarePlantRangeTiles) continue;
                        if (!taReady) { ta = new TileAccessor(ref CheckedStateRef, true); taReady = true; }
                        int2 playerTile = new int2((int)math.round(AggroScan.PlayerPos.x), (int)math.round(AggroScan.PlayerPos.y));
                        int2 plantTile = new int2((int)math.round(ep.x), (int)math.round(ep.y));
                        if (!HasLineOfSight(ref ta, playerTile, plantTile)) continue;
                    }

                    AggroScan.Chasers[n++] = new AggroScan.Chaser
                    {
                        Key    = EntityKey.Of(egg),
                        Pos    = ep,
                        Obj    = od.objectID,
                        IsBoss = false,
                    };
                }
                eggs.Dispose();

                AggroScan.Count = n;
                AggroScan.Version++;
                AggroScan.ResultValid = true;
            }
            catch (System.Exception ex)
            {
                // Sans trace, un echec de query est invisible et la sentinelle meurt
                // en silence. Diag.Error deduplique (pas de spam a 5 Hz) et laisse
                // passer une erreur DIFFERENTE au meme site.
                Diag.Error("A11yAggroDiag", ex);
            }
        }

        // Trajet a->b degage de mur ? Meme logique que l'occlusion de la canne laser :
        // un mur bloque (impact), un gouffre/de l'eau reste transparent a la vue (juste
        // infranchissable a pied). Traversee de grille case par case (Amanatides-Woo).
        private static bool HasLineOfSight(ref TileAccessor ta, int2 a, int2 b)
        {
            int x = a.x, y = a.y;
            int dx = b.x - a.x, dy = b.y - a.y;
            int stepX = dx > 0 ? 1 : (dx < 0 ? -1 : 0);
            int stepY = dy > 0 ? 1 : (dy < 0 ? -1 : 0);
            float adx = math.abs(dx), ady = math.abs(dy);
            float tDeltaX = adx > 0 ? 1f / adx : float.MaxValue;
            float tDeltaY = ady > 0 ? 1f / ady : float.MaxValue;
            float tMaxX = adx > 0 ? 0.5f / adx : float.MaxValue;
            float tMaxY = ady > 0 ? 0.5f / ady : float.MaxValue;

            int guard = (int)(adx + ady) + 2; // garde-fou anti-boucle
            while ((x != b.x || y != b.y) && guard-- > 0)
            {
                if (tMaxX < tMaxY) { tMaxX += tDeltaX; x += stepX; }
                else { tMaxY += tDeltaY; y += stepY; }
                if (x == b.x && y == b.y) break; // case d'arrivee : jamais testee comme mur
                if (ta.TryGetBlockingTile(new int2(x, y), out TileCD blocking, true))
                {
                    bool seeThrough = blocking.tileType == TileType.pit || blocking.tileType == TileType.water;
                    if (!seeThrough) return false;
                }
            }
            return true;
        }
    }
}
