namespace CoreKeeperAccess.Gameplay
{
    // Etat de "monture" du joueur (bateau pour l'instant). Lu sur PlayerStateCD, dont tous
    // les champs d'etat sont [GhostField] -> replique au client, utilisable tel quel en
    // multi non-hote. PlayerStateEnum est un masque de bits : HasAnyState est le test du
    // jeu lui-meme (cf. ControllableLocalToWorldSystem.IsBoatRiding cote decompil).
    // Types qualifies a la main (namespace PlayerState du jeu) : un using l'exposerait
    // sous un nom qui se confond avec les etats eux-memes.
    //
    // Sert aux couches de perception qui traitent l'eau comme un obstacle : en bateau,
    // l'eau est au contraire la surface franchissable (retour testeur 27 juillet 2026 :
    // "le rayon de proximite considere qu'on est toujours contre un obstacle" en
    // navigation). Cf. core-keeper-proximity-sonar.
    internal static class PlayerRide
    {
        public static bool OnBoat(PlayerController player)
        {
            if (player == null) return false;
            try
            {
                if (!EntityUtility.HasComponentData<PlayerState.PlayerStateCD>(player.entity, player.world))
                    return false;
                return EntityUtility.GetComponentData<PlayerState.PlayerStateCD>(player.entity, player.world)
                    .HasAnyState(PlayerState.PlayerStateEnum.BoatRiding);
            }
            catch { return false; }
        }
    }
}
