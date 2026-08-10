using System.Collections.Generic;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using Unity.Entities;

namespace CoreKeeperAccess.Gameplay
{
    // Lecture de l'avancement du monde (roue de stats, secteur sud). Lit le singleton
    // WorldInfoCD - repliqué au client par GhostField, donc accessible cote mod. On
    // n'annonce QUE l'etat brut (ce qu'un voyant percoit : statues du Core, grand mur),
    // jamais la marche a suivre -> cf. regle no-spoilers. Pour l'Acte 1, les trois flags
    // utiles et repliques : coreIsActivated, bossesKilled (compteur), greatWallHasBeenLowered.
    internal static class WorldProgress
    {
        internal static void Announce(PlayerController player)
        {
            try
            {
                var info = player.querySystem.GetSingleton<WorldInfoCD>();
                var parts = new List<string>
                {
                    Strings.L("progress.core") + " "
                        + Strings.L(info.coreIsActivated ? "progress.core.on" : "progress.core.off"),
                    Strings.L("progress.bosses") + " " + info.bossesKilled,
                };
                // Les 3 cristaux sont poses mais le joueur n'a pas encore parle au Cœur : c'est
                // l'etape qui manque, et rien a l'ecran ne la reclame clairement (retour testeur
                // 10 aout 2026 : joueur COINCE, la Grande Muraille reste sourde). Deux donnees
                // DISTINCTES cote jeu, la nuance est tout le probleme :
                //  - WorldInfoCD.coreIsActivated = les 3 cristaux sont dans les statues, POINT
                //    (UpdateWorldInfoSystem) - rien a voir avec "avoir parle au Cœur" ;
                //  - SoulsInfoCD.hasUnlockedSouls = le dialogue du Cœur a bien eu lieu (pose par
                //    PlayerController.UnlockSouls des la 1re replique), donnee du PERSONNAGE.
                // PlayerController.UpdateGreatWall exige LES DEUX pour laisser toucher le mur ->
                // c'est exactement ce couple qu'on annonce. (L'ancienne garde "pas active + 3
                // statues" etait impossible : 3 statues => coreIsActivated, clause jamais dite.)
                if (info.coreIsActivated && !HasUnlockedSouls(player))
                    parts.Add(Strings.L("progress.talktocore"));
                // Le grand mur n'est mentionne qu'une fois abaisse (sinon ce serait pointer
                // un objectif que le joueur n'a pas encore decouvert).
                if (info.greatWallHasBeenLowered) parts.Add(Strings.L("progress.wall"));
                TtsText.Say(string.Join(", ", parts), true);
            }
            catch { }
        }

        // Le joueur a-t-il debloque les ames aupres du Cœur ? SoulsInfoCD.hasUnlockedSouls est
        // porte par l'entite JOUEUR et marque [GhostField] (serialiseur ghost genere cote jeu)
        // -> repliqué au client, donc lisible en invite multijoueur comme en solo. C'est la
        // seule donnee qui distingue "cristaux poses" de "j'ai parle au Cœur" ; partagee avec
        // la garde du journal de dialogues (CoreDialogueArchive).
        internal static bool HasUnlockedSouls(PlayerController player)
        {
            try
            {
                if (player == null) return false;
                return EntityUtility.GetComponentData<SoulsInfoCD>(player.entity, player.world).hasUnlockedSouls;
            }
            catch { return false; }
        }
    }
}
