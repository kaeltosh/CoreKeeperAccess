namespace CoreKeeperAccess.Navigation
{
    // Etat partage avec les patches Harmony (neutralisation de l'input natif +
    // etouffement de l'annonce passive quand c'est nous qui pilotons la selection)
    // et entre InventoryNavigator et ActionWheel.
    internal static class InventoryNavState
    {
        public static bool SuppressNativeInput;     // vrai tant que notre nav inventaire tient la main
        public static bool SuppressPassiveAnnounce;  // vrai le temps d'une selection forcee

        // Action "armee" par la roue : notre patch fera croire au jeu que ce bouton
        // vient d'etre presse (1 fois), pour declencher l'action native. Ttl = filet
        // si le jeu ne lit pas l'input dans la frame.
        public static PlayerInput.InputType? ArmedInput;
        public static int ArmedTtl;
    }
}
