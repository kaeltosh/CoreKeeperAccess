using CoreKeeperAccess.Navigation;
using HarmonyLib;

namespace CoreKeeperAccess.Patches
{
    // Nettoyage de NOS fichiers de persistance a la suppression EFFECTIVE d'un monde.
    //
    // Le jeu n'appelle SaveManager.RemoveWorld(id) qu'apres confirmation du joueur (le bouton
    // supprimer ouvre un pop-up, et seul un IsConfirm declenche DeleteSaveSlot -> RemoveWorld) :
    // c'est donc la suppression reelle, jamais la simple tentative. On se branche en PREFIX
    // pour lire le guid du monde TANT QU'IL EST ENCORE LA (RemoveWorld l'efface des sa 2e
    // instruction via _ClearWorldInfo). On efface nos 3 fichiers (graphe / balises / journal)
    // par guid ET par slot legacy, puis on vide les caches en memoire (pas de reecriture
    // tardive). Avec la cle par guid, un orphelin ne causait plus de bug ; ce patch evite juste
    // l'accumulation de fichiers morts et, cote dev, une re-migration du legacy.
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.RemoveWorld))]
    internal static class WorldDeleteCleanupPatch
    {
        [HarmonyPrefix]
        public static void Prefix(int id) => PatchGuard.Run("A11yWorldDelete", () =>
        {
            WorldKey.PurgeWorldFiles(id);
            BeaconGraph.ForgetCurrent();
            BeaconStore.ForgetCurrent();
            DialogueLog.ForgetCurrent();
        });
    }
}
