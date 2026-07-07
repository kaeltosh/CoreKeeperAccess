using System.Collections.Generic;
using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Navigation;
using CoreKeeperAccess.Patches;
using Interaction;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Lecture du PLACEMENT pour les objets en main (pose v1, 13 juin). Surveille le
    // PlacementCD du joueur (l'etat que le jeu calcule pour le fantome de pose) :
    //  - rotation (rotationVariationToPlace) change -> annonce le cap ("face nord") ;
    //  - validite (canPlaceObject = ghost bleu/rouge) passe a INVALIDE -> earcon de refus
    //    (PAS de TTS : le ghost oscille en balayant, on ne sature pas la voix).
    // La TAILLE de l'emprise (zone d'outil) est SEULEMENT annoncee ici (poll), jamais a
    // l'EquipSlot (HeldItemAnnouncePatch) : EquippedObjectVisualCD.sizeVariationToPlace
    // n'y est pas encore a jour, lue trop tot ca disait "1x1" avant de se corriger.
    // Le joueur se replace lui-meme : on donne l'info qui manque, pas un assistant.
    internal static class PlacementReader
    {
        private const float Poll = 0.15f;
        private const SfxID InvalidSfx = SfxID.menu_denied; // placeholder (choix utilisateur)
        private const float InvalidVolume = 0.3f;

        private static float _next;
        private static ObjectID _lastObjId = ObjectID.None;
        private static int _lastRot = -1;
        private static int _lastSize = -1;
        private static bool _lastValid = true;
        private static bool _primed;

        public static void Tick(PlayerController player)
        {
            if (player == null) { _primed = false; return; }
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Poll;

            ObjectDataCD held;
            try { held = player.GetHeldObject(); } catch { _primed = false; return; }
            if (held.objectID == ObjectID.None) { _primed = false; return; }

            // Changement d'objet en main : on re-amorce sans annoncer (le nom + la taille
            // partent par HeldItemAnnouncePatch).
            if (held.objectID != _lastObjId) { _primed = false; _lastObjId = held.objectID; }

            PlacementCD pc;
            try
            {
                if (!EntityUtility.HasComponentData<PlacementCD>(player.entity, player.world)) { _primed = false; return; }
                pc = EntityUtility.GetComponentData<PlacementCD>(player.entity, player.world);
            }
            catch { _primed = false; return; }

            // Cran de zone courant pour les outils a zone reglable (le Rotate cycle la
            // ZONE, pas la rotation, donc rotationVariationToPlace ne bouge pas) -> on
            // surveille EquippedObjectVisualCD.sizeVariationToPlace separement.
            int sizeVar = -1;
            try
            {
                if (EntityUtility.HasComponentData<EquippedObjectVisualCD>(player.entity, player.world))
                    sizeVar = EntityUtility.GetComponentData<EquippedObjectVisualCD>(player.entity, player.world).sizeVariationToPlace;
            }
            catch { }

            if (_primed)
            {
                if (pc.rotationVariationToPlace != _lastRot)
                {
                    int2 dir = DirectionBasedOnVariationCD.GetDirectionFromVariation(pc.rotationVariationToPlace, false);
                    TtsText.Say(Strings.L("place.facing") + " " + Cardinal4(dir), true);
                }
                // Zone d'outil cyclee au Rotate -> annonce "zone 3x3" (null = pas un outil
                // a zone reglable, donc muet pour les meubles). En file (interrupt:false) :
                // a la prise en main, l'annonce de durabilite (native, deja en file) peut
                // encore parler quand ce Tick rattrape la taille reelle de l'outil ~150ms
                // plus tard -> ne plus s'ecraser, s'enchainer.
                if (sizeVar != _lastSize && _lastSize >= 0)
                {
                    string zone = InGameTtsCore.ToolZoneLabel(player);
                    if (!string.IsNullOrEmpty(zone)) TtsText.Say(zone, false);
                }
                if (_lastValid && !pc.canPlaceObject)
                    GameplayAudio.PlaySpatial(InvalidSfx, 0f, 1f, InvalidVolume);
            }

            _lastRot = pc.rotationVariationToPlace;
            _lastSize = sizeVar;
            _lastValid = pc.canPlaceObject;
            _primed = true;
        }

        // Cap cardinal (4) d'une direction de variation (x=est, y=nord). Repere CONFIRME
        // par decompil de DirectionBasedOnVariationCD.GetDirectionFromVariation
        // (Pug.ECS.Components) : variation 0=(0,1) nord, 1=(1,0) est, 2=(0,-1) sud,
        // 3=(-1,0) ouest -> Rotate cycle horaire N->E->S->O.
        private static string Cardinal4(int2 d)
        {
            if (d.y > 0) return Strings.L("dir.n");
            if (d.y < 0) return Strings.L("dir.s");
            if (d.x > 0) return Strings.L("dir.e");
            return Strings.L("dir.w");
        }
    }
}
