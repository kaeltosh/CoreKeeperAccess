using System.Collections.Generic;
using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Navigation;
using CoreKeeperAccess.Patches;
using Unity.Entities;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Lecture des etats vitaux a la demande + alertes a seuil (touche access) :
    //  - AnnounceHealth : vie + barriere magique si active (roue, secteur nord).
    //  - AnnounceHunger : faim (roue, secteur nord-ouest).
    //  - AnnounceMana : mana + serviteurs, toujours annonces (roue, secteur nord-est).
    //  - AnnouncePosition : coordonnees monde + biome courant. Cable sur Triangle + D-pad
    //    haut (localisation) quand le curseur est attache, cf. ComboBindings.
    //  - Alertes automatiques : vie sous 30 % -> "Vie faible", faim sous 20 ->
    //    "Faim critique". Une annonce au franchissement du seuil, rearmee quand on
    //    repasse au-dessus (pas de spam), muette a la mort (vie 0).
    internal static class VitalsReadout
    {
        private const float LowHealthRatio = 0.30f;
        private const float LowHungerValue = 20f;
        private const float AlertPollInterval = 0.5f;

        private static bool _lowHealthAnnounced;
        private static bool _lowHungerAnnounced;
        private static float _nextPoll;

        public static void Tick()
        {
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null)
            {
                _lowHealthAnnounced = _lowHungerAnnounced = false;
                return;
            }

            // Les combos Triangle + bas (vitals) / + droite (position) sont routes par
            // ComboDispatcher (cf. ComboBindings). Ici ne restent que les alertes.

            // Alertes a seuil : poll espace, actif meme inventaire ouvert (le jeu
            // continue en temps reel).
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + AlertPollInterval;
            CheckAlerts(player);
        }

        // Vie (roue, secteur nord) : points de vie + barriere magique RANGEE ICI (un
        // bouclier de PV, sa place logique). Barriere annoncee TOUJOURS, meme a zero -
        // choix utilisateur : la repere systematiquement avec la vie.
        internal static void AnnounceHealth(PlayerController player)
        {
            string s = Strings.L("vitals.health") + " " + player.currentHealth + " "
                     + Strings.L("vitals.outof") + " " + player.GetMaxHealth();
            int barrier = 0, barrierMax = 0;
            try
            {
                if (EntityUtility.HasComponentData<MagicBarrierCD>(player.entity, player.world))
                {
                    var b = EntityUtility.GetComponentData<MagicBarrierCD>(player.entity, player.world);
                    barrier = Mathf.RoundToInt((float)b.barrierHealth);
                    barrierMax = Mathf.RoundToInt((float)b.barrierMaxHealth);
                }
            }
            catch { }
            s += ", " + Strings.L("vitals.barrier") + " " + barrier;
            if (barrierMax > 0) s += " " + Strings.L("vitals.outof") + " " + barrierMax;

            // Regen de vie en queue : rien si nul, sinon "+X.Y/s" (effet HealOverTime).
            int regen = 0;
            try { regen = EntityUtility.GetConditionEffectValue(ConditionEffect.HealOverTime, player.entity, player.world); }
            catch { }
            s += RegenSuffix(regen);

            TtsText.Say(s, true);
        }

        // Suffixe de regeneration : "" si nul, sinon ", +X.Y/s" (PAS de mot "regeneration",
        // le contexte vie/mana suffit). La valeur brute de l'effet est en DIXIEMES par
        // seconde (42 = 4.2/s) -> partie entiere + decimale, point decimal en dur.
        private static string RegenSuffix(int raw)
        {
            if (raw <= 0) return "";
            return ", +" + (raw / 10) + "." + (raw % 10) + "/s";
        }

        // Faim (roue, secteur nord-ouest) : jauge 0-100, toujours presente.
        internal static void AnnounceHunger(PlayerController player)
        {
            float hunger = ReadHunger(player);
            int h = hunger >= 0f ? Mathf.RoundToInt(hunger) : 0;
            TtsText.Say(Strings.L("vitals.hunger") + " " + h + " "
                + Strings.L("vitals.outof") + " 100", true);
        }

        // Mana + serviteurs (roue, secteur nord-est) : le cote invocation/magie. Annonces
        // TOUJOURS, meme a zero (build sans magie) - choix utilisateur : reperer la position
        // sur la roue. Serviteurs = minions actifs (MinionCountTrackerCD.count) sur capacite
        // (ConditionEffect.MaxMinions, comme la prospection lit VisibleOreDistance).
        internal static void AnnounceMana(PlayerController player)
        {
            int manaPct = 0;
            try
            {
                if (EntityUtility.HasComponentData<ManaCD>(player.entity, player.world))
                    manaPct = Mathf.RoundToInt(
                        EntityUtility.GetComponentData<ManaCD>(player.entity, player.world).Normalized * 100f);
            }
            catch { }
            string s = Strings.L("vitals.mana") + " " + manaPct + " " + Strings.L("vitals.percent");

            int count = 0, max = 0;
            try
            {
                if (EntityUtility.HasComponentData<MinionCountTrackerCD>(player.entity, player.world))
                    count = EntityUtility.GetComponentData<MinionCountTrackerCD>(player.entity, player.world).count;
            }
            catch { }
            try { max = EntityUtility.GetConditionEffectValue(ConditionEffect.MaxMinions, player.entity, player.world); }
            catch { }
            s += ", " + count + " " + Strings.L("vitals.minions");
            if (max > 0) s += " " + Strings.L("vitals.outof") + " " + max;

            // Regen de mana en queue : rien si nul, sinon "+X.Y/s" (effet ManaRegen). La base
            // de jeu (+100 interne) n'est pas comptee -> 0 = pas de BONUS de regen.
            int regen = 0;
            try { regen = EntityUtility.GetConditionEffectValue(ConditionEffect.ManaRegen, player.entity, player.world); }
            catch { }
            s += RegenSuffix(regen);

            TtsText.Say(s, true);
        }

        internal static void AnnouncePosition(PlayerController player)
        {
            var w = player.WorldPosition;
            string s = Strings.L("vitals.position") + " "
                + Mathf.RoundToInt(w.x) + ", " + Mathf.RoundToInt(w.z);
            // Biome courant en queue (demande utilisateur : "ou je suis" en un seul geste).
            string biome = TeleportNavigator.CurrentBiomeName(player);
            if (!string.IsNullOrEmpty(biome))
                s += ", " + Strings.L("teleport.biome") + " " + biome;
            TtsText.Say(s, true);
        }

        // Liste BLANCHE des etats notables a annoncer (debuffs de survie / handicaps qui
        // expliquent une perte de vie ou une gene). On NE liste PAS l'agregat complet du
        // SummarizedConditionsBuffer (il melange des dizaines de bonus d'equipement, illisible)
        // : on sonde seulement ces ConditionID a leur index. Tableaux paralleles (id <-> cle
        // i18n) pour rester simple en hot-compile.
        private static readonly ConditionID[] DebuffIds =
        {
            ConditionID.Poisoned, ConditionID.Burning, ConditionID.SlowedBySlime,
            ConditionID.StarvingMovementSpeedDecrease, ConditionID.Suffocating,
            ConditionID.AcidDamage, ConditionID.Snared, ConditionID.Stunned, ConditionID.Charmed,
        };
        private static readonly string[] DebuffKeys =
        {
            "cond.poisoned", "cond.burning", "cond.slimeslow",
            "cond.starving", "cond.suffocating",
            "cond.acid", "cond.snared", "cond.stunned", "cond.charmed",
        };

        // Etats actifs (roue de stats, secteur est). Lit le SummarizedConditionsBuffer du
        // joueur (maintenu cote client par SummarizeConditionsSystem, indexe par ConditionID,
        // .value != 0 = condition active) et n'en retient que les debuffs de la liste blanche.
        internal static void AnnounceConditions(PlayerController player)
        {
            var actives = new List<string>();
            try
            {
                var em = player.world.EntityManager;
                if (em.HasBuffer<SummarizedConditionsBuffer>(player.entity))
                {
                    DynamicBuffer<SummarizedConditionsBuffer> buf =
                        em.GetBuffer<SummarizedConditionsBuffer>(player.entity, true);
                    for (int i = 0; i < DebuffIds.Length; i++)
                    {
                        int idx = (int)DebuffIds[i];
                        if (idx >= 0 && idx < buf.Length && buf[idx].value != 0)
                            actives.Add(Strings.L(DebuffKeys[i]));
                    }
                }
            }
            catch { }

            TtsText.Say(actives.Count == 0
                ? Strings.L("stats.conditions.none")
                : Strings.L("stats.conditions") + " : " + string.Join(", ", actives), true);
        }

        private static void CheckAlerts(PlayerController player)
        {
            int max = player.GetMaxHealth();
            if (max > 0)
            {
                float ratio = (float)player.currentHealth / max;
                if (ratio <= LowHealthRatio && ratio > 0f)
                {
                    if (!_lowHealthAnnounced)
                    {
                        _lowHealthAnnounced = true;
                        TtsText.Say(Strings.L("vitals.lowhealth") + ", " + player.currentHealth
                            + " " + Strings.L("vitals.outof") + " " + max, true);
                    }
                }
                else
                {
                    _lowHealthAnnounced = false;
                }
            }

            float hunger = ReadHunger(player);
            if (hunger >= 0f)
            {
                if (hunger <= LowHungerValue)
                {
                    if (!_lowHungerAnnounced)
                    {
                        _lowHungerAnnounced = true;
                        TtsText.Say(Strings.L("vitals.lowhunger") + ", " + Mathf.RoundToInt(hunger), true);
                    }
                }
                else
                {
                    _lowHungerAnnounced = false;
                }
            }
        }

        // Faim 0-100 (struct HungerCD exposee par le PlayerController) ; -1 si
        // indisponible (ecran de chargement, composant pas encore pret).
        private static float ReadHunger(PlayerController player)
        {
            try { return player.hungerComponent.hunger; }
            catch { return -1f; }
        }
    }
}
