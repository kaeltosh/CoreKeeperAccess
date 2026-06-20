using Unity.Entities;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Alertes SONORES automatiques a l'apparition d'un etat subi (earcons, pas du TTS : un
    // signal immediat et non intrusif). Complement de la roue de stats, qui lit les conditions
    // a la DEMANDE (VitalsReadout.AnnounceConditions). On ne sonne qu'au FRONT MONTANT
    // (apparition), une SEULE fois - jamais de rappel periodique tant que ca dure (choix
    // utilisateur).
    //
    // Deux earcons retenus apres tri du code du jeu (les autres candidats ne touchent JAMAIS
    // le joueur : le charme est garde !receiverIsPlayer, la suffocation n'est referencee par
    // aucun systeme gameplay) :
    //  - DoT  : feu / acide / radiation -> UN earcon generique. Le VIDE en est exclu (sa
    //    boucle native est deja audible) mais reste chiffre a la demande dans la roue.
    //  - Stun : earcon distinct. Critique : aucun bip de nav ne signale qu'on est fige, et le
    //    jeu COUPE tout l'input (ClearInputOnStunnedSystem lit l'effet a l'index 58).
    //
    // Modele de degats CONFIRME par decompil (DealDamageFromConditionSystem) : un DoT n'est PAS
    // une regen negative (canal separe) mais des COUPS periodiques sur le canal de degats. Les
    // montants vivent dans les buffers RESUMES, lisibles cote client (les memes que la roue) :
    //   feu   = effect[16] x (1 + conditions[336]/100), tick 2 s   (l'huile amplifie)
    //   acide = effect[14],                              tick 1 s
    //   radio = conditions[207],                         tick 2 s
    //   vide  = conditions[249] % x PV max,              tick 1 s
    // Les index sont en dur (le jeu lui-meme les code ainsi) ; une sonde diag les valide en jeu.
    internal static class ConditionAlerts
    {
        // Index dans SummarizedConditionEffectsBuffer (indexe par ConditionEffect)
        private const int BurnDamageEffect = 16;
        private const int AcidDamageEffect = 14;
        private const int StunEffect = 58;       // ce que ClearInputOnStunnedSystem lit pour couper l'input
        // Index dans SummarizedConditionsBuffer (indexe par ConditionID)
        private const int OilCondition = 336;        // pourcentage d'huile (bonus de degat de brulure)
        private const int RadioDamageCondition = 207; // montant radio (donc > 0 = radiation active)
        private const int VoidPercentCondition = 249;  // pourcentage de PV max perdu par tick

        private static bool _wasDot, _wasStun;

        public static void Tick()
        {
            if (!A11ySettings.ConditionEarcons) { _wasDot = _wasStun = false; return; }
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null) { _wasDot = _wasStun = false; return; }

            bool dot = false, stun = false;
            try
            {
                var em = player.world.EntityManager;
                if (em.HasBuffer<SummarizedConditionsBuffer>(player.entity)
                    && em.HasBuffer<SummarizedConditionEffectsBuffer>(player.entity))
                {
                    var cond = em.GetBuffer<SummarizedConditionsBuffer>(player.entity, true);
                    var eff = em.GetBuffer<SummarizedConditionEffectsBuffer>(player.entity, true);

                    bool feu = ActiveCond(cond, (int)ConditionID.Burning);
                    bool acid = ActiveCond(cond, (int)ConditionID.AcidDamage);
                    bool radio = Val(cond, RadioDamageCondition) > 0;
                    dot = feu || acid || radio;
                    stun = Val(eff, StunEffect) > 0;
                }
            }
            catch { _wasDot = _wasStun = false; return; }

            // Front montant uniquement : une alerte a l'apparition, pas de rappel.
            if (dot && !_wasDot) { GameplayAudio.PlayConditionEarcon(false); Diag.Log("A11yCondAlert", "DoT apparu"); }
            if (stun && !_wasStun) { GameplayAudio.PlayConditionEarcon(true); Diag.Log("A11yCondAlert", "Stun apparu"); }
            _wasDot = dot;
            _wasStun = stun;
        }

        private static bool ActiveCond(DynamicBuffer<SummarizedConditionsBuffer> buf, int idx)
            => idx >= 0 && idx < buf.Length && buf[idx].value != 0;

        private static int Val(DynamicBuffer<SummarizedConditionsBuffer> buf, int idx)
            => (idx >= 0 && idx < buf.Length) ? buf[idx].value : 0;

        private static int Val(DynamicBuffer<SummarizedConditionEffectsBuffer> buf, int idx)
            => (idx >= 0 && idx < buf.Length) ? buf[idx].value : 0;

        // Degat-par-seconde EXACT de chaque DoT actif (0 si inactif), pour la lecture A LA
        // DEMANDE dans la roue conditions. Reproduit le calcul du jeu (cf. en-tete), arrondi
        // a l'entier le plus proche.
        public static void ReadDps(PlayerController player, out int feu, out int acid, out int radio, out int vide)
        {
            feu = acid = radio = vide = 0;
            if (player == null) return;
            try
            {
                var em = player.world.EntityManager;
                if (!em.HasBuffer<SummarizedConditionsBuffer>(player.entity)
                    || !em.HasBuffer<SummarizedConditionEffectsBuffer>(player.entity)) return;
                var cond = em.GetBuffer<SummarizedConditionsBuffer>(player.entity, true);
                var eff = em.GetBuffer<SummarizedConditionEffectsBuffer>(player.entity, true);

                if (ActiveCond(cond, (int)ConditionID.Burning))
                {
                    float oil = Val(cond, OilCondition) / 100f;
                    feu = Mathf.RoundToInt(Val(eff, BurnDamageEffect) * (1f + oil) / 2f); // tick 2 s
                }
                if (ActiveCond(cond, (int)ConditionID.AcidDamage))
                    acid = Val(eff, AcidDamageEffect); // tick 1 s
                int r = Val(cond, RadioDamageCondition);
                if (r > 0) radio = Mathf.RoundToInt(r / 2f); // tick 2 s
                int vp = Val(cond, VoidPercentCondition);
                if (vp > 0) vide = Mathf.RoundToInt(vp / 100f * player.GetMaxHealth()); // tick 1 s
            }
            catch { }
        }
    }
}
