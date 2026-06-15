using CoreKeeperAccess.Controls;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
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
    // Activable/desactivable par l'utilisateur via le panneau (A11ySettings.CombatSlowMo).
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
            // Ralenti desactive par l'utilisateur (panneau) : on se comporte comme hors combat
            // (vitesse normale restauree ci-dessous si on l'avait applique).
            bool inCombat = A11ySettings.CombatSlowMo && AggroScan.ResultValid && AggroScan.Count > 0;

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
}
