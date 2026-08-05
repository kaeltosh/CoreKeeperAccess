using System.Collections.Generic;
using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Socle GENERIQUE des annonces de boss (5 aout 2026). Deux roles :
    //
    // 1. GOULOT UNIQUE de toutes les annonces parlees de combat de boss, avec priorites.
    //    Avant, chaque module parlait dans son coin : un boss qui s'enrage en changeant
    //    de phase pile sur un palier de vie declenchait trois annonces simultanees qui
    //    s'ecrasaient (interrupt=true partout). Ici une seule parle par creneau, la plus
    //    urgente d'abord, les autres attendent au lieu de disparaitre.
    //
    // 2. Les evenements COMMUNS a tous les boss, lus sur des composants natifs generiques
    //    publies par BossHealthScanSystem : enrage (EnrageStateCD), changement de phase
    //    et invulnerabilite (PhaseTransitionStateCD), mort. Zero ligne par boss -> couvre
    //    les 15 boss du jeu, y compris ceux jamais rencontres.
    //
    // Les mecaniques PROPRES a un boss (tir acide de la ruche, piliers d'Azeos) restent
    // dans leur fichier et n'appellent que Enqueue().
    internal static class BossAnnounce
    {
        // Plus grand = plus urgent. Seules les priorites >= PrioDanger coupent la parole
        // en cours ; le reste s'enchaine pour ne rien perdre.
        public const int PrioInfo = 1;     // apparition / disparition / reperage
        public const int PrioState = 2;    // enrage, changement de phase
        public const int PrioDanger = 3;   // invulnerabilite, telegraphe d'attaque
        public const int PrioCritical = 4; // mort du boss

        private const float MinGap = 0.6f;        // s entre deux annonces de la file
        private const float MaxAge = 3f;          // s : passe ce delai l'info est perimee
        private const float RepeatCooldown = 2f;  // s : anti-rabachage d'une meme phrase
        private const int MaxQueued = 6;

        private struct Item { public int Prio; public string Text; public float Time; }

        private static readonly List<Item> _queue = new List<Item>();
        private static readonly Dictionary<string, float> _lastSaid = new Dictionary<string, float>();
        private static float _nextSay;

        // Etats de reference pour les fronts (montant/descendant)
        private static bool _everSeen;
        private static bool _wasEnraged;
        private static bool _wasInvulnerable;
        private static bool _wasAppeared;
        private static int _lastPhase;
        private static int _seenTargetToken = -1;
        private static int _seenDeathToken;

        public static void Enqueue(string text, int prio)
        {
            if (string.IsNullOrEmpty(text)) return;

            // Anti-rabachage : un telegraphe d'attaque se declenche a CHAQUE attaque du
            // boss. Repeter la meme phrase toutes les deux secondes noie tout le reste ;
            // au-dela du delai elle repasse, parce que c'est bien une nouvelle attaque.
            if (_lastSaid.TryGetValue(text, out float last)
                && Time.unscaledTime - last < RepeatCooldown) return;

            // Meme phrase deja en attente (deux sources pour un seul evenement reel) :
            // on garde la priorite la plus forte, jamais le doublon.
            for (int i = 0; i < _queue.Count; i++)
            {
                if (_queue[i].Text != text) continue;
                if (prio > _queue[i].Prio)
                    _queue[i] = new Item { Prio = prio, Text = text, Time = _queue[i].Time };
                return;
            }

            if (_queue.Count >= MaxQueued)
            {
                int worst = IndexOfWorst();
                if (_queue[worst].Prio > prio) return; // rien de moins urgent a sacrifier
                _queue.RemoveAt(worst);
            }

            _queue.Add(new Item { Prio = prio, Text = text, Time = Time.unscaledTime });
        }

        // Raccourci des annonces generiques : gabarit i18n a trou + nom du boss resolu
        // par le JEU (donc traduit dans toutes ses langues, sans table maison).
        public static void EnqueueNamed(string key, string bossName, int prio)
        {
            if (string.IsNullOrEmpty(bossName)) return;
            Enqueue(Strings.L(key).Replace("{0}", bossName), prio);
        }

        public static void Tick()
        {
            if (!InputContext.InGameFree)
            {
                _queue.Clear();
                _everSeen = false;
                return;
            }

            TickStates();
            TickQueue();
        }

        // Evenements generiques, valables pour n'importe quelle entite marquee BossCD.
        private static void TickStates()
        {
            // Mort : jeton publie par le scan, consomme une seule fois. Traite avant tout
            // le reste - la cible est deja relachee quand on lit le jeton.
            if (BossScan.DeathToken != _seenDeathToken)
            {
                _seenDeathToken = BossScan.DeathToken;
                EnqueueNamed("boss.defeated",
                    InGameTtsCore.ResolveObjectName(BossScan.DeadObjId), PrioCritical);
            }

            if (!BossScan.Found) { _everSeen = false; return; }

            // Cible changee (boss enchaine, ou bascule d'une entite a l'autre sur un boss
            // multi-entites) : on repart d'une reference propre, sans annoncer.
            if (BossScan.TargetToken != _seenTargetToken)
            {
                _seenTargetToken = BossScan.TargetToken;
                _everSeen = false;
            }

            if (!_everSeen)
            {
                // Premiere lecture : etat pris comme reference, aucune annonce. Meme piege
                // que AzeosBoss.TickBossState - arriver en cours de combat ne doit pas
                // declencher un faux "s'enrage".
                _everSeen = true;
                _wasEnraged = BossScan.Enraged;
                _wasInvulnerable = BossScan.Invulnerable;
                _wasAppeared = BossScan.Appeared;
                _lastPhase = BossScan.Phase;
                return;
            }

            string name = InGameTtsCore.ResolveObjectName(BossScan.ObjId);

            if (BossScan.Enraged && !_wasEnraged) EnqueueNamed("boss.enrage", name, PrioState);
            _wasEnraged = BossScan.Enraged;

            // Apparition / disparition : trois boss reprennent le mecanisme d'Azeos
            // (composant <Boss>HasAppearedCD replique). Azeos garde ses propres phrases
            // ("atterrit" / "disparait"), son composant n'est donc pas lu par le scan.
            if (BossScan.HasAppearedInfo && BossScan.Appeared != _wasAppeared)
            {
                _wasAppeared = BossScan.Appeared;
                EnqueueNamed(_wasAppeared ? "boss.appear" : "boss.disappear", name, PrioInfo);
            }

            if (!BossScan.HasPhase) return;

            if (BossScan.Phase != _lastPhase)
            {
                _lastPhase = BossScan.Phase;
                // Pas de numero de phase a l'annonce : Malugaz numerote A L'ENVERS
                // (ShamanBossSystem, phase 1 = mode melee), un chiffre serait faux.
                EnqueueNamed("boss.phase", name, PrioState);
            }

            if (BossScan.Invulnerable != _wasInvulnerable)
            {
                _wasInvulnerable = BossScan.Invulnerable;
                // Info capitale a l'aveugle : sans elle on tape dans le vide sans
                // comprendre pourquoi la barre ne bouge plus.
                EnqueueNamed(BossScan.Invulnerable ? "boss.invulnerable" : "boss.vulnerable",
                    name, PrioDanger);
            }
        }

        private static void TickQueue()
        {
            if (_queue.Count == 0) return;
            float now = Time.unscaledTime;

            for (int i = _queue.Count - 1; i >= 0; i--)
                if (now - _queue[i].Time > MaxAge) _queue.RemoveAt(i);

            if (_queue.Count == 0) return;

            int best = 0;
            for (int i = 1; i < _queue.Count; i++)
            {
                if (_queue[i].Prio > _queue[best].Prio ||
                    (_queue[i].Prio == _queue[best].Prio && _queue[i].Time < _queue[best].Time))
                    best = i;
            }

            // L'urgence n'attend pas le creneau : retarder de 0,6 s un telegraphe d'attaque
            // ("la ruche tire !") le rendrait inutile. Le reste patiente pour ne pas
            // s'ecraser mutuellement.
            if (now < _nextSay && _queue[best].Prio < PrioDanger) return;

            var item = _queue[best];
            _queue.RemoveAt(best);
            TtsText.Say(item.Text, item.Prio >= PrioDanger);
            _lastSaid[item.Text] = now;
            _nextSay = now + MinGap;
        }

        private static int IndexOfWorst()
        {
            int worst = 0;
            for (int i = 1; i < _queue.Count; i++)
            {
                if (_queue[i].Prio < _queue[worst].Prio ||
                    (_queue[i].Prio == _queue[worst].Prio && _queue[i].Time < _queue[worst].Time))
                    worst = i;
            }
            return worst;
        }
    }
}
