using CoreKeeperAccess.Controls;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Roue de SAUT direct dans la barre rapide ACTIVE (10 slots = 10 secteurs ; a ce
    // decoupage il n'y a plus de reperes cardinaux/diagonaux propres, decision utilisateur
    // assumee). Deux gestes MIROIR au choix, geres par InfoKey (HotbarWheelRight/Left) :
    //   - R1 tenu (Triangle relache) : stick GAUCHE pilote, marche gelee (meme pont ECS que
    //     la roue de stats, AccessKeyMovementLockSystem).
    //   - L1 tenu (Triangle relache) : stick DROIT pilote, marche libre, mais la canne
    //     laser (aussi stick droit) se tait le temps de la tenue (LaserCane.Tick).
    // LECTURE DIRECTE au survol (runOnHover) : pointer un secteur EQUIPE tout de suite le
    // slot correspondant de la barre COURANTE (hotbarStartIndex + secteur) ; l'annonce est
    // gratuite (HeldItemAnnouncePatch sur EquipSlot). Clic de cran sonore a chaque secteur
    // (EquipSlot direct n'a pas de son natif, contrairement au swap de barre qui a le sien).
    // Togglable dans le panneau de reglages (A11ySettings.HotbarWheelEnabled) : desactive,
    // R1/L1 restent le pas-a-pas natif (objet precedent/suivant).
    internal static class HotbarJumpWheel
    {
        private static readonly CommandWheel Wheel = Build();

        private static CommandWheel Build()
        {
            var w = new CommandWheel(0, 0, -1, runOnHover: true, tickSound: true, sectorCount: 10);
            for (int i = 0; i < 10; i++)
            {
                int slot = i; // capture locale (boucle)
                w.AddAtSector(i, "", () => EquipCurrentBarSlot(slot));
            }
            return w;
        }

        private static void EquipCurrentBarSlot(int slot)
        {
            var p = Player();
            if (p == null) return;
            p.EquipSlot(p.hotbarStartIndex + slot);
        }

        public static void Tick(PlayerController player)
        {
            if (player == null || !InputContext.InGameFree
                || (!InfoKey.HotbarWheelRight && !InfoKey.HotbarWheelLeft))
            {
                Wheel.Tick(Vector2.zero, false);
                return;
            }

            var input = player.inputModule;
            if (input == null) { Wheel.Tick(Vector2.zero, false); return; }

            // R1 -> stick gauche (mapping monde x=est/y=nord, comme la roue de stats) ;
            // L1 -> stick droit (comme la canne laser qu'il remplace le temps de la tenue).
            bool rightStick = InfoKey.HotbarWheelLeft;
            Vector2 aim = input.GetRawAxisInput(false, true, rightStick);
            Wheel.Tick(aim, false);
        }

        private static PlayerController Player() =>
            Manager.main != null ? Manager.main.player : null;
    }
}
