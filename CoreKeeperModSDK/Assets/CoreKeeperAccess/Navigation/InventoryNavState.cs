namespace CoreKeeperAccess.Navigation
{
    // Etat partage avec les patches Harmony (neutralisation de l'input natif +
    // etouffement de l'annonce passive quand c'est nous qui pilotons la selection)
    // et entre InventoryNavigator et ActionWheel.
    internal static class InventoryNavState
    {
        public static bool SuppressNativeInput;     // vrai tant que notre nav inventaire tient la main
        public static bool SuppressPassiveAnnounce;  // vrai le temps d'une selection forcee

        // Vrai quand l'emplacement courant est un slot de CONTENU de bourse : ces slots
        // sont sous un masque de defilement, le curseur manette virtuel (UIMouse) ne les
        // "tient" pas (son raycast les manque) -> l'action native Croix taperait sur la
        // barre rapide. On etouffe alors le Croix natif (NativeInputSuppressionPatch) et
        // on route prendre/poser nous-memes sur le bon slot (InventoryNavigator).
        public static bool OnMaskedSlot;

        // Vrai quand l'emplacement courant est une case de talent de familier : cet overlay
        // flottant ne "tient" pas le curseur manette virtuel non plus, qui derive vers les
        // cases voisines (toutes au meme nom tant que le familier n'a pas ses talents) et les
        // slots de bourse en arriere-plan. On etouffe alors l'annonce passive (le postfix de
        // OnUIElementSelected) : nos propres annonces de talents passent par la nav, jamais
        // par ce postfix, donc le couper ici est sans perte. Voir SyncWithGameSelection.
        public static bool OnPetTalent;

        // Action "armee" par la roue : notre patch fera croire au jeu que ce bouton
        // vient d'etre presse (1 fois), pour declencher l'action native. Ttl = filet
        // si le jeu ne lit pas l'input dans la frame.
        public static PlayerInput.InputType? ArmedInput;
        public static int ArmedTtl;
    }
}
