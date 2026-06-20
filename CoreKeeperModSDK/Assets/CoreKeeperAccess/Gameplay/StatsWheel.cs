using CoreKeeperAccess.Controls;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Roue de STATS au stick GAUCHE, active tant que Triangle (la touche access) est tenu
    // en jeu libre. LECTURE DIRECTE au survol : pousser le stick vers un secteur annonce
    // la stat tout de suite (pas de bouton de validation - runOnHover). Le stick gauche
    // sert sinon au deplacement, gele pendant la tenue par AccessKeyMovementLockSystem ->
    // pousser le stick ne fait plus marcher mais navigue la roue (comme une touche Maj).
    // (Stick gauche et non droit : Triangle et le stick droit sont sous le meme pouce,
    // physiquement inconciliables ; le D-pad/stick gauche sont sous l'autre pouce.)
    //
    // Positions FIXES (AddAtSector) pour des reflexes stables des le depart :
    //   Nord (0)  = vie (+ barriere)     Nord-est (1) = mana + serviteurs
    //   Est (2)   = etats / conditions   Sud (4) = progression du monde
    //   Ouest (6) = prospection minerai  Nord-ouest (7) = faim
    //   SE (3), SO (5) libres. Biome -> fin du D-pad haut (localisation) ; pieces retirees
    //   (info de commerce, pas de terrain) ; pas de cycle jour-nuit dans le jeu.
    internal static class StatsWheel
    {
        private const float Deadzone = 0.5f; // = deadzone secteur de CommandWheel

        // axes 0/0 ignores : on alimente la roue par la surcharge Vector2 (stick lu via
        // l'API du jeu). confirmId -1 + runOnHover : roue de lecture, aucune validation.
        private static readonly CommandWheel Wheel = Build();

        private static CommandWheel Build()
        {
            var w = new CommandWheel(0, 0, -1, runOnHover: true, tickSound: false);
            w.AddAtSector(0, "stats.health", () =>
            {
                var p = Player();
                if (p != null) VitalsReadout.AnnounceHealth(p);
            });
            w.AddAtSector(1, "stats.magic", () =>
            {
                var p = Player();
                if (p != null) VitalsReadout.AnnounceMana(p);
            });
            w.AddAtSector(7, "stats.hunger", () =>
            {
                var p = Player();
                if (p != null) VitalsReadout.AnnounceHunger(p);
            });
            w.AddAtSector(2, "stats.conditions", () =>
            {
                var p = Player();
                if (p != null) VitalsReadout.AnnounceConditions(p);
            });
            w.AddAtSector(4, "stats.progress", () =>
            {
                var p = Player();
                if (p != null) WorldProgress.Announce(p);
            });
            w.AddAtSector(6, "stats.prospect", () =>
            {
                var p = Player();
                if (p != null) GameplayInput.RequestProspect(p);
            });
            return w;
        }

        public static void Tick(PlayerController player)
        {
            // Active uniquement Triangle tenu, en jeu libre. Sinon repos : stick neutre ->
            // CommandWheel remet son secteur a -1 (la prochaine poussee re-annonce).
            if (player == null || !InfoKey.ModifierHeld || !InputContext.InGameFree)
            {
                Wheel.Tick(Vector2.zero, false);
                return;
            }

            var input = player.inputModule;
            if (input == null) { Wheel.Tick(Vector2.zero, false); return; }

            // Stick GAUCHE BRUT (getRightJoystick=false) : x=est, y=nord (mapping monde,
            // comme le deplacement) -> pousser le stick vers le haut = secteur nord, etc.
            Vector2 aim = input.GetRawAxisInput(false, true, false);

            // Engagement pendant la tenue : signale a InfoKey que ce relachement n'est pas
            // un tap (sinon le double-tap "ouvrir la carte" se declencherait par megarde).
            if (aim.sqrMagnitude >= Deadzone * Deadzone) InfoKey.StickEngagedDuringHold = true;

            Wheel.Tick(aim, false);
        }

        private static PlayerController Player() =>
            Manager.main != null ? Manager.main.player : null;
    }
}
