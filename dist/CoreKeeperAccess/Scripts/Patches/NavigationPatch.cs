using CoreKeeperAccess.Gameplay;
using CoreKeeperAccess.Localization;
using HarmonyLib;
using UnityEngine;

namespace CoreKeeperAccess.Patches
{
    // Hooks Harmony lies a la navigation in-game (accueillera d'autres hooks de
    // deplacement). Premier hook : rendre les pas HONNETES.
    //
    // Probleme : AE_FootStep est un Animation Event, declenche par l'animation de
    // course independamment du deplacement reel. Donc quand on bute contre un mur
    // (le perso "court sur place"), les pas continuent et rien ne signale le blocage.
    //
    // Regle absolue : le son de pas = preuve de mouvement. Silence UNIQUEMENT quand le
    // perso est immobile (bute contre un mur). En glissement le long d'un mur, il bouge
    // donc il garde des pas, juste plus espaces (cadence allegee = ralenti percu).
    //
    // L'espacement est DETERMINISTE et borne : on ne saute jamais deux pas d'affilee
    // (au pire cadence /2), pour qu'un "ralenti" ne devienne jamais un blanc suspect.
    [HarmonyPatch(typeof(PlayerController), nameof(PlayerController.AE_FootStep))]
    internal static class FootstepHonestyPatch
    {
        private static float _skipAccu;   // accumule slide01 ; un pas saute quand >= 1
        private static bool _lastSkipped; // garde-fou : jamais deux sauts consecutifs

        [HarmonyPrefix]
        public static bool Prefix(PlayerController __instance)
        {
            try
            {
                // Joueur local uniquement (en multi, AE_FootStep existe sur tous les persos).
                if (Manager.main == null || __instance != Manager.main.player) return true;

                var state = MovementReader.Evaluate(__instance, out float slide);

                if (state == MovementReader.MoveState.Blocked)
                {
                    _skipAccu = 0f; _lastSkipped = false;
                    return false; // immobile : silence
                }

                if (state == MovementReader.MoveState.Sliding)
                {
                    // Plus on glisse fort, plus l'accumulateur grimpe vite -> on saute
                    // plus souvent, mais jamais deux pas de suite (cadence /2 au pire).
                    _skipAccu += slide;
                    if (_skipAccu >= 1f && !_lastSkipped)
                    {
                        _skipAccu -= 1f;
                        _lastSkipped = true;
                        return false;
                    }
                    _lastSkipped = false;
                    return true;
                }

                // Free / NoIntent : pas natif intact.
                _skipAccu = 0f; _lastSkipped = false;
                return true;
            }
            catch
            {
                return true; // un Animation Event ne doit jamais throw : on laisse le pas natif
            }
        }
    }

    // Annonce l'objet en main quand on change de slot de barre rapide en jeu (EquipSlot
    // est le point exact du changement). En jeu normal seulement : inventaire ouvert,
    // la navigation par sections annonce deja les slots.
    [HarmonyPatch(typeof(PlayerController), nameof(PlayerController.EquipSlot))]
    internal static class HeldItemAnnouncePatch
    {
        private static int _lastSlot = -1;

        [HarmonyPostfix]
        public static void Postfix(PlayerController __instance, int slotIndex, bool __result)
        {
            try
            {
                if (!__result || Manager.main == null || __instance != Manager.main.player) return;
                if (slotIndex == _lastSlot) return; // pas un vrai changement
                _lastSlot = slotIndex;

                if (Manager.ui != null && Manager.ui.isAnyInventoryShowing) return; // jeu normal seulement

                var held = __instance.GetHeldObject();
                string text;
                if (held.objectID == ObjectID.None)
                {
                    text = Strings.L("ingame.slot.empty");
                }
                else
                {
                    string name = InGameTtsCore.ResolveObjectName(held.objectID);
                    if (string.IsNullOrEmpty(name)) return;
                    // Seau / arrosoir : annoncer vide/plein plutot que l'amount (= niveau,
                    // pas une quantite empilee).
                    string fill = InGameTtsCore.FillLevelLabel(held);
                    if (fill != null) text = $"{name}, {fill}";
                    else text = held.amount > 1 ? $"{name}, {held.amount}" : name;
                    // Outil a zone reglable (houe/arrosoir/pelle/semoir) : ajouter la zone
                    // d'effet courante ("zone 3x3"). null pour tout autre objet.
                    string zone = InGameTtsCore.ToolZoneLabel(__instance);
                    if (!string.IsNullOrEmpty(zone)) text += ", " + zone;
                }
                TtsText.Say(text, true);
            }
            catch { }
        }
    }
}
