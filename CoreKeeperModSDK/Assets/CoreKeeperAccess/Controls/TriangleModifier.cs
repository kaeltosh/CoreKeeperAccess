using System.Collections.Generic;
using Rewired;
using UnityEngine;

namespace CoreKeeperAccess.Controls
{
    // Libere le bouton Triangle pour en faire le modificateur a11y du mod (notre "touche
    // NVDA") : on retire l'assignation MANETTE de l'action carte (ToggleMap, id 55 - inutile
    // pour un joueur aveugle) via l'API Rewired DeleteElementMap. On NE touche ni aux autres
    // actions de Triangle (note d'instrument), ni au clavier. En memoire seulement (aucune
    // sauvegarde) -> reversible, config Rewired du joueur intacte. Re-applique chaque seconde
    // car le jeu peut reconstruire les maps selon le contexte (gameplay / UI). Le mod lit
    // ensuite le bouton Triangle PHYSIQUE en direct (independant du mapping d'action).
    internal static class TriangleModifier
    {
        private const int ToggleMapActionId = 55;  // RewiredConsts.Action.ToggleMap
        private const int RotateActionId = 207;    // RewiredConsts.Action.Rotate (sur Croix en placement)

        // Id physique du bouton Triangle, capte automatiquement depuis le binding de la carte
        // avant qu'on le supprime -> juste pour la manette reelle du joueur (PS/Xbox/...), pas
        // de valeur en dur. -1 tant que pas encore capte.
        public static int TriangleButtonId = -1;

        private static float _nextCheck;

        // Id physique de SECOURS : 9 = Y/Triangle du template Rewired Gamepad standard,
        // confirme en jeu par le diagnostic F9 (DualSense). Necessaire car le jeu PEUT
        // sauvegarder sa config controles APRES notre suppression en memoire (vecu le
        // 11 juin 2026 : plus aucun binding de l'action 55 dans CoreKeeper_Controls.json)
        // -> au boot suivant il n'y a plus rien a capter et la touche access mourait.
        private const int FallbackTriangleId = 9;

        public static void Tick()
        {
            if (!ReInput.isReady) return;
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 1f; // cout negligeable : balayage une fois/seconde

            var toDelete = new List<int>();
            foreach (var player in ReInput.players.AllPlayers)
            {
                foreach (var map in player.controllers.maps.GetAllMaps())
                {
                    if (map == null || map.controllerType != ControllerType.Joystick) continue;
                    toDelete.Clear();
                    foreach (var aem in map.AllMaps)
                    {
                        if (aem.actionId == ToggleMapActionId)
                        {
                            TriangleButtonId = aem.elementIdentifierId; // bouton physique = Triangle
                            toDelete.Add(aem.id);
                        }
                        // Rotate (207) : retiree de la manette pour que Croix ne pivote plus
                        // un objet par accident. On la rejoue via Triangle + R1 (armement).
                        else if (aem.actionId == RotateActionId) toDelete.Add(aem.id);
                    }
                    foreach (var id in toDelete) map.DeleteElementMap(id);
                }
            }

            if (TriangleButtonId < 0) TriangleButtonId = FallbackTriangleId;
        }
    }
}
