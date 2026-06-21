using CoreKeeperAccess.Gameplay;

namespace CoreKeeperAccess.Controls
{
    // Earcons de NAVIGATION partages par tous les menus MAISON du mod (panneau de reglages,
    // menu contextuel ActionMenu, roues de commandes, lecteur de carte + journal). But : une
    // SEULE grammaire sonore, pour que tout ce qui se navigue au D-pad/stick sonne "comme un
    // seul menu" a l'oreille. Trois earcons distincts, tous joues via GameplayAudio.PlayUi donc
    // NORMALISES (les 3 clics natifs ont des niveaux tres inegaux) ET pilotes par le volume
    // general (master). PlayUi joue sans random pitch : un cran sonne toujours pareil.
    //
    // Sons de reference (choix utilisateur, 20 juin 2026) - timbres reaffectes, sans lien avec
    // leur fonction d'origine en jeu :
    //  - Entry    : naviguer entre entrees, cran de slider, remonter d'un niveau, fermer.
    //  - Cycle    : BUTEE de cyclage (revenir au debut / aller a la fin d'une liste).
    //  - Validate : ouvrir un menu, valider, entrer un sous-menu / une section, basculer.
    internal static class UiSfx
    {
        public static void Entry() => GameplayAudio.PlayUi(SfxID.FIXME_menu_select);
        public static void Cycle() => GameplayAudio.PlayUi(SfxID.switch_click_1_01);
        public static void Validate() => GameplayAudio.PlayUi(SfxID.inventory_select);
    }
}
