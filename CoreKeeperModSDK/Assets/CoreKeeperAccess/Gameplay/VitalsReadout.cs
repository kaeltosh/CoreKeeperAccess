using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Navigation;
using CoreKeeperAccess.Patches;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Lecture des etats vitaux a la demande + alertes a seuil (touche access) :
    //  - Triangle + D-pad bas (hors nav inventaire, ou "bas" = transferer) :
    //    vie, faim, + mana si entame et barriere magique si active.
    //  - Triangle + D-pad droite : position (coordonnees monde arrondies).
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

        internal static void AnnounceVitals(PlayerController player)
        {
            string s = Strings.L("vitals.health") + " " + player.currentHealth + " "
                     + Strings.L("vitals.outof") + " " + player.GetMaxHealth();

            float hunger = ReadHunger(player);
            if (hunger >= 0f)
                s += ", " + Strings.L("vitals.hunger") + " " + Mathf.RoundToInt(hunger)
                   + " " + Strings.L("vitals.outof") + " 100";

            // Mana : seulement si entame (un build sans magie n'a pas a l'entendre).
            try
            {
                if (EntityUtility.HasComponentData<ManaCD>(player.entity, player.world))
                {
                    float n = EntityUtility.GetComponentData<ManaCD>(player.entity, player.world).Normalized;
                    if (n < 0.995f)
                        s += ", " + Strings.L("vitals.mana") + " " + Mathf.RoundToInt(n * 100f)
                           + " " + Strings.L("vitals.percent");
                }
            }
            catch { }

            // Barriere magique : seulement si active.
            try
            {
                if (EntityUtility.HasComponentData<MagicBarrierCD>(player.entity, player.world))
                {
                    var b = EntityUtility.GetComponentData<MagicBarrierCD>(player.entity, player.world);
                    if (b.barrierMaxHealth > 0 && b.barrierHealth > 0)
                        s += ", " + Strings.L("vitals.barrier") + " "
                           + Mathf.RoundToInt((float)b.barrierHealth) + " "
                           + Strings.L("vitals.outof") + " "
                           + Mathf.RoundToInt((float)b.barrierMaxHealth);
                }
            }
            catch { }

            TtsText.Say(s, true);
        }

        internal static void AnnouncePosition(PlayerController player)
        {
            var w = player.WorldPosition;
            TtsText.Say(Strings.L("vitals.position") + " "
                + Mathf.RoundToInt(w.x) + ", " + Mathf.RoundToInt(w.z), true);
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
