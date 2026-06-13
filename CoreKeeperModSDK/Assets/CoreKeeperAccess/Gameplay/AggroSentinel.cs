using System.Collections.Generic;
using CoreKeeperAccess.Controls;
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

            // Menu (pause...) ouvert : monde fige -> on suspend les bips sans perdre
            // l'etat. Inventaire/carte ouverts : on RESTE actif, le jeu continue en
            // temps reel et c'est precisement la qu'on se fait surprendre.
            if (InputContext.MenuOpen) return;

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
            float volume = BaseVolume * GameplayAudio.DistanceTrim(math.length(d));
            if (boss) GameplayAudio.PlayBossTone(pan, pitch, volume * BossVolumeScale);
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

    // Ralenti de combat (PROTO, 13 juin). Quand le joueur est EN COMBAT, on ralentit le
    // temps du jeu via Time.timeScale -> le combat devient tenable a l'oreille. Tout
    // ralentit ENSEMBLE (le boss ET le joueur) ; le gain vient de ce que le temps de
    // REACTION humain, lui, est fixe en temps reel : ralenti, il pese bien moins lourd
    // dans la fenetre de jeu, donc on a le temps d'entendre un signal et d'agir. SOLO
    // uniquement (on ne ralentit pas un serveur partage). PROTO : se declenche sur TOUT
    // combat ; a restreindre aux BOSS ensuite (AggroScan.Chasers[i].IsBoss). "En combat"
    // = exactement ce que publie la sentinelle (hostiles vivants IsInCombatCD a l'ecran
    // + boss hors-champ, filtres deja faits) -> AggroScan.Count > 0.
    //
    // A loger dans son propre fichier au prochain build Unity (ici pour rester fast-build).
    internal static class CombatSlowMotion
    {
        private const float SlowScale = 0.5f; // mi-vitesse ; a regler a l'oreille en jeu

        private static bool _applied;

        public static void Tick()
        {
            var player = Manager.main != null ? Manager.main.player : null;

            // Un menu de PAUSE fige le jeu en posant lui-meme timeScale=0 : on ne lutte
            // JAMAIS contre ca (sinon on casse la pause). MenuOpen = monde fige (pas
            // l'inventaire, qui lui continue en temps reel).
            bool worldRunning = player != null && !InputContext.MenuOpen;
            bool inCombat = AggroScan.ResultValid && AggroScan.Count > 0;

            if (worldRunning && inCombat)
            {
                if (!_applied) { Time.timeScale = SlowScale; _applied = true; }
                return;
            }

            // On ne ralentit plus : restaurer la vitesse normale - SAUF si une pause a la
            // main sur timeScale (monde fige), auquel cas on lache sans toucher (le jeu
            // gere ; au depause, on reposera le ralenti si le combat dure encore).
            if (_applied)
            {
                if (!InputContext.MenuOpen) Time.timeScale = 1f;
                _applied = false;
            }
        }
    }

    // Repere de centre d'arene (placeholder, 13 juin). Quand une zone d'invocation
    // (SummonArea = centre de l'arene de boss) est a portee, un drone sinusoidal doux et
    // CONTINU indique sa direction : pan est-ouest + pitch nord-sud (langage positionnel
    // du mod), par rapport au joueur. Sert AVANT le combat (poser le crane pile au centre,
    // fini le tatonnement) ET pendant (se situer dans le disque). Volume tres faible
    // (ambiance, jamais couvrant). Portee calee sur l'arene (~16 cases de diametre ->
    // rayon ~8, + marge). Detection = CenterScan, publie par TileReaderSystem au fil de
    // son scan d'objets. Son = sinus genere placeholder (l'utilisateur choisira).
    // A loger dans son propre fichier au prochain build Unity (avec CombatSlowMotion).
    internal static class CenterBeacon
    {
        private const float Range = 10f;     // portee max (rayon arene ~8 + marge)
        private const float Volume = 0.12f;  // tres faible (placeholder, a regler a l'oreille)

        public static void Tick()
        {
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null || !InputContext.InGameFree || !CenterScan.Found)
            {
                GameplayAudio.SetCenterDrone(false, 0f, 1f, 0f);
                return;
            }

            float2 me = new float2(player.WorldPosition.x, player.WorldPosition.z);
            float2 d = CenterScan.Pos - me;
            float dist = math.length(d);
            if (dist > Range)
            {
                GameplayAudio.SetCenterDrone(false, 0f, 1f, 0f);
                return;
            }

            float pan = GameplayAudio.PanFromTiles(d.x);
            float pitch = Mathf.Clamp(Mathf.Pow(2f, d.y / 12f), 0.5f, 2f);
            GameplayAudio.SetCenterDrone(true, pan, pitch, Volume);
        }
    }

    // Detecteur de proximite des ZONES DE FEU (premier jet, 13 juin). Le feu au sol
    // (zones AoE de Malugaz, pieges de flammes...) n'est PAS une tuile : ce sont des
    // ENTITES (ObjectID FireAoeDamage / FireTrap / OilFireTrap) -> on les lit dans
    // l'index case->objet existant (il capte les entites sans collider, deja reconstruit
    // ~4 Hz). Alerte positionnelle (pan est-ouest + pitch nord-sud) quand une zone est a
    // <= AlertRange cases ; rappel a la cadence tant qu'elle reste proche. Le joueur
    // ESQUIVE lui-meme (perception, pas assist). Son = SfxID.dg2 (choix utilisateur, son
    // natif court du jeu). A loger dans son propre fichier au prochain build Unity.
    // PREMIER JET : la zone la plus proche seulement (agglomeration
    // en clusters + alerte de contact dediee a ajouter apres validation de la detection).
    internal static class FireProximity
    {
        private const float AlertRange = 2f;     // cases : seuil d'alerte de proximite
        private const float ScanInterval = 0.1f; // ~10 Hz : frequence d'iteration de l'index
        private const float BeepInterval = 0.15f; // rappel rapide (crepitement dense, zone proche)
        private const SfxID FireSfx = SfxID.dg2;   // son de proximite feu (choix utilisateur)
        private const float Volume = 1.2f;         // pousse : dg2 est attenue par la normalisation

        private static float _nextScan;
        private static float _nextBeep;

        public static void Tick()
        {
            var p = Manager.main != null ? Manager.main.player : null;
            if (p == null || !InputContext.InGameFree) return;
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + ScanInterval;

            int2 me = new int2(
                (int)math.round(p.WorldPosition.x),
                (int)math.round(p.WorldPosition.z));

            // Zone de feu la plus proche dans l'index (cle = case encodee, cf. ObjectIndex.Key).
            bool found = false;
            float best = float.MaxValue;
            int2 bestTile = default;
            foreach (var kv in ObjectIndex.Map)
            {
                if (!IsFire(kv.Value.Id)) continue;
                int2 t = new int2((int)(kv.Key >> 32), (int)(kv.Key & 0xFFFFFFFF));
                float d2 = math.distancesq(new float2(t.x, t.y), new float2(me.x, me.y));
                if (d2 < best) { best = d2; bestTile = t; found = true; }
            }
            if (!found) return;

            float dist = math.sqrt(best);
            if (dist > AlertRange) return;
            if (Time.unscaledTime < _nextBeep) return;
            _nextBeep = Time.unscaledTime + BeepInterval;

            int dx = bestTile.x - me.x, dy = bestTile.y - me.y;
            // Portee courte (<= AlertRange) -> on EXAGERE pan et pitch, sinon la direction
            // est imperceptible (le bareme commun 1/12 par case ne bouge presque pas sur
            // 2 cases) : plein pan a la portee max, ~1 octave de pitch a la portee max.
            float pan = Mathf.Clamp((float)dx / AlertRange, -1f, 1f);
            float pitch = Mathf.Clamp(Mathf.Pow(2f, dy / 2f), 0.5f, 2f);
            GameplayAudio.PlaySpatial(FireSfx, pan, pitch, Volume * GameplayAudio.DistanceTrim(dist));
        }

        private static bool IsFire(ObjectID id)
            => id == ObjectID.FireAoeDamage
            || id == ObjectID.FireTrap
            || id == ObjectID.OilFireTrap;
    }

    // Flag de l'invincibilite PURE (outil de test, toggle Triangle+F7). Partage entre le
    // mod (qui le bascule) et le systeme serveur (qui force la vie) - meme process en solo.
    // DISTINCT du god mode creatif (F8) : ici le combat reste NORMAL (boss vivant, ennemis
    // actifs, murs solides), le joueur ne meurt juste pas.
    internal static class DevInvincibility
    {
        public static bool Active;
    }

    // Force la vie du joueur au max tant que l'invincibilite de test est active. SERVEUR
    // (autoritaire) : en solo le ServerWorld tourne dans notre process, donc l'ecriture
    // tient (cote client elle serait corrigee au prochain snapshot). Cible les entites
    // HealthCD + PlayerGhost (= les joueurs). Pas de SystemAPI.Query en hot-compile ->
    // GetEntityQuery + EntityManager (cf. fiche acces donnees). health/maxHealth confirmes
    // sur HealthCD ; on rejoue le meme "health = maxHealth" que le jeu pour soigner.
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class DevInvincibilitySystem : SystemBase
    {
        private EntityQuery _query;

        protected override void OnCreate()
        {
            _query = GetEntityQuery(
                ComponentType.ReadWrite<HealthCD>(),
                ComponentType.ReadOnly<PlayerGhost>());
            Diag.Log("A11yDevGodMode", "DevInvincibilitySystem cree dans " + World.Name);
        }

        protected override void OnUpdate()
        {
            if (!DevInvincibility.Active) return;
            try
            {
                var ents = _query.ToEntityArray(Allocator.Temp);
                foreach (var e in ents)
                {
                    var h = EntityManager.GetComponentData<HealthCD>(e);
                    if (h.health < h.maxHealth)
                    {
                        h.health = h.maxHealth;
                        EntityManager.SetComponentData(e, h);
                    }
                }
                ents.Dispose();
            }
            catch (System.Exception ex) { Diag.Error("A11yDevGodMode", ex); }
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

        protected override void OnCreate()
        {
            _query = GetEntityQuery(
                ComponentType.ReadOnly<IsInCombatCD>(),
                ComponentType.ReadOnly<LocalTransform>());
            // Diag de mise au point : confirme dans quel monde le systeme s'enregistre.
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

                    // Memes garde-fous que la canne laser : pas de critter, faction hostile.
                    if (EntityUtility.HasComponentData<CritterCD>(e, World)) continue;
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
    }
}
