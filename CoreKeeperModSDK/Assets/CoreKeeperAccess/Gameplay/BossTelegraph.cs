using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Patches;
using HarmonyLib;

namespace CoreKeeperAccess.Gameplay
{
    // Telegraphes d'action des boss (5 aout 2026), via les ANIMATION EVENTS du jeu.
    //
    // Chaque boss est un MonoBehaviour dont l'animator appelle des methodes nommees AE_*
    // au bon instant de l'animation - le meme hook que le tir acide de la ruche depuis
    // juin. Elles sont remarquablement standardisees d'un boss a l'autre, et surtout
    // CLIENT-SIDE : ca marche aussi pour un joueur distant en multi, la ou lire un etat
    // serveur ne marche qu'en solo/hote.
    //
    // Ne sont branchees ici que les actions qui apportent une info NON couverte ailleurs :
    //  - l'enrage, la transition de phase et la mort ont aussi leur animation event, mais
    //    le socle generique (BossAnnounce) les lit deja sur des composants repliques ->
    //    les patcher ferait doublon ;
    //  - AE_AttackSound (le son de l'attaque elle-meme) n'anticipe rien, le jeu le joue
    //    deja -> aucun interet a l'aveugle.
    //
    // ⚠ Code NON TESTABLE tant que ces boss n'ont pas ete rencontres : un patch qui rate
    // sa cible est signale au boot par le diagnostic de patches, et PatchGuard absorbe
    // toute exception pour ne pas figer la methode du jeu.
    internal static class BossTelegraph
    {
        public static void Say(EntityMonoBehaviour boss, string key, int prio)
        {
            if (!InputContext.InGameFree) return;
            BossAnnounce.EnqueueNamed(key, ResolveName(boss), prio);
        }

        // Nom resolu depuis l'entite du boss lui-meme, pas depuis la cible suivie par le
        // scan : l'animation event dit qui parle, on ne suppose pas que c'est le meme.
        private static string ResolveName(EntityMonoBehaviour boss)
        {
            if (boss == null) return null;
            try
            {
                var od = EntityUtility.GetComponentData<ObjectDataCD>(boss.entity, boss.world);
                return InGameTtsCore.ResolveObjectName(od.objectID);
            }
            catch { return null; }
        }
    }

    // --- Telegraphe d'attaque : meme methode que la ruche, sur deux autres boss --------

    [HarmonyPatch(typeof(ShamanBoss), "AE_AnticipationSound")]
    internal static class ShamanBossAnticipationPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ShamanBoss __instance) =>
            PatchGuard.Run("A11yBossShamanAnticipation",
                () => BossTelegraph.Say(__instance, "boss.telegraph.attack", BossAnnounce.PrioDanger));
    }

    [HarmonyPatch(typeof(SlimeBoss), "AE_AnticipationSound")]
    internal static class SlimeBossAnticipationPatch
    {
        [HarmonyPostfix]
        public static void Postfix(SlimeBoss __instance) =>
            PatchGuard.Run("A11yBossSlimeAnticipation",
                () => BossTelegraph.Say(__instance, "boss.telegraph.attack", BossAnnounce.PrioDanger));
    }

    // --- Scarabee : s'enfouit (il disparait sous terre avant de charger) ---------------

    [HarmonyPatch(typeof(ScarabBoss), "AE_PlayDigSound")]
    internal static class ScarabBossDigPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ScarabBoss __instance) =>
            PatchGuard.Run("A11yBossScarabDig",
                () => BossTelegraph.Say(__instance, "boss.telegraph.dig", BossAnnounce.PrioDanger));
    }

    // --- Robot : sequence d'etats tres lisible, dont une fenetre au sol ----------------

    [HarmonyPatch(typeof(RobotBoss), "AE_StartScan")]
    internal static class RobotBossScanPatch
    {
        [HarmonyPostfix]
        public static void Postfix(RobotBoss __instance) =>
            PatchGuard.Run("A11yBossRobotScan",
                () => BossTelegraph.Say(__instance, "boss.telegraph.scan", BossAnnounce.PrioDanger));
    }

    [HarmonyPatch(typeof(RobotBoss), "AE_Scream")]
    internal static class RobotBossScreamPatch
    {
        [HarmonyPostfix]
        public static void Postfix(RobotBoss __instance) =>
            PatchGuard.Run("A11yBossRobotScream",
                () => BossTelegraph.Say(__instance, "boss.telegraph.scream", BossAnnounce.PrioDanger));
    }

    [HarmonyPatch(typeof(RobotBoss), "AE_FallDownHitGround")]
    internal static class RobotBossFallPatch
    {
        [HarmonyPostfix]
        public static void Postfix(RobotBoss __instance) =>
            PatchGuard.Run("A11yBossRobotFall",
                () => BossTelegraph.Say(__instance, "boss.telegraph.falldown", BossAnnounce.PrioState));
    }

    [HarmonyPatch(typeof(RobotBoss), "AE_Getup")]
    internal static class RobotBossGetupPatch
    {
        [HarmonyPostfix]
        public static void Postfix(RobotBoss __instance) =>
            PatchGuard.Run("A11yBossRobotGetup",
                () => BossTelegraph.Say(__instance, "boss.telegraph.getup", BossAnnounce.PrioState));
    }

    [HarmonyPatch(typeof(RobotBoss), "AE_StartParasiteSequence")]
    internal static class RobotBossParasitePatch
    {
        [HarmonyPostfix]
        public static void Postfix(RobotBoss __instance) =>
            PatchGuard.Run("A11yBossRobotParasite",
                () => BossTelegraph.Say(__instance, "boss.telegraph.parasite", BossAnnounce.PrioDanger));
    }

    // --- Cigale : destruction d'un point faible (progression de la mecanique) ----------

    [HarmonyPatch(typeof(GiantCicadaBoss), "AE_WeakPointExplode")]
    internal static class CicadaBossWeakPointPatch
    {
        [HarmonyPostfix]
        public static void Postfix() =>
            PatchGuard.Run("A11yBossCicadaWeakPoint",
                () => BossAnnounce.Enqueue(
                    Localization.Strings.L("boss.telegraph.weakpoint"), BossAnnounce.PrioState));
    }
}
