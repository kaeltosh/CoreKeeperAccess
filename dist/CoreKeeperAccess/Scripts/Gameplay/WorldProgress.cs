using System.Collections.Generic;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;

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
                // Le grand mur n'est mentionne qu'une fois abaisse (sinon ce serait pointer
                // un objectif que le joueur n'a pas encore decouvert).
                if (info.greatWallHasBeenLowered) parts.Add(Strings.L("progress.wall"));
                TtsText.Say(string.Join(", ", parts), true);
            }
            catch { }
        }
    }
}
