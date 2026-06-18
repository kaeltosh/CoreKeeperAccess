using System.Collections.Generic;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using Unity.Collections;
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
                // Cœur eveille mais pas encore active : les 3 statues de boss sont chargees
                // (etat qu'un voyant percoit - le Cœur s'illumine a fond), il attend qu'on lui
                // parle. C'est aussi ce que dit l'indice natif TalkToTheCore. On ne le signale
                // PAS tant que les 3 ne sont pas la (pointer "il faut 3 statues" = spoiler).
                if (!info.coreIsActivated && ActivatedStatues() >= 3)
                    parts.Add(Strings.L("progress.talktocore"));
                // Le grand mur n'est mentionne qu'une fois abaisse (sinon ce serait pointer
                // un objectif que le joueur n'a pas encore decouvert).
                if (info.greatWallHasBeenLowered) parts.Add(Strings.L("progress.wall"));
                TtsText.Say(string.Join(", ", parts), true);
            }
            catch { }
        }

        // Nombre de statues de boss "chargees" (doneLoadingUp, GhostField repliqué au client),
        // lues via une requete ECS directe - meme source que TheCore.UpdateStatueLoadState.
        // Manager.ecs.GetClientEntityQuery est une API managee (PAS un systeme ECS SystemBase),
        // donc fast-build suffit, pas de build Unity.
        private static int ActivatedStatues()
        {
            try
            {
                var q = Manager.ecs.GetClientEntityQuery(new ComponentType[] { typeof(BossStatueCD) });
                using (var arr = q.ToComponentDataArray<BossStatueCD>(Allocator.Temp))
                {
                    int done = 0;
                    for (int i = 0; i < arr.Length; i++)
                        if (arr[i].doneLoadingUp) done++;
                    return done;
                }
            }
            catch { return 0; }
        }
    }
}
