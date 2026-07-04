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
    // Commandes de combo de la touche access agissant sur une UI d'atelier/marchand
    // OUVERTE (extraites de GameplayInput le 21 juin pour rendre le decoupage cohesif).
    //  - Triangle + bas    = bascule mode reparation/renforcement (station de
    //                        reparation/recyclage ouverte). Le transfert d'objet est
    //                        retire de ce combo (deja couvert par la gachette RT, cf.
    //                        InventoryNavigator.HandleInput), donc libre pour ce role.
    //  - Triangle + droite = agit selon le mode courant : repare OU renforce l'objet
    //                        selectionne (station de reparation ouverte).
    //  - Triangle + gauche = tout recycler (contenu des slots de la station) OU tout
    //                        vendre (marchand ouvert), selon le contexte.
    // Les combos station/forge sont CONTEXTUELS : sans la bonne UI ouverte ils sont muets
    // (pas d'annonce d'erreur), comme s'ils n'existaient pas. Et Triangle + haut (details)
    // s'enrichit, station/forge ouverte, du COUT DE REPARATION/RENFORCEMENT et du GAIN DE
    // RECYCLAGE estime (BuildStationDetail) ou du cout d'amelioration (BuildForgeDetail),
    // appeles par AnnounceDetail (InventoryNavigator).
    // La reparation/le recyclage/la forge n'utilisent PAS les boutons/modes souris des UI :
    // on appelle directement les methodes publiques du jeu (CraftingHandler.RepairOrReinforce
    // / SalvageAndRepairUI.Salvage / UpgradeForgeUI.Upgrade), qui passent par la file
    // d'actions serveur officielle comme l'artisanat.
    internal static class StationCommands
    {
        private static bool StationOpen => InputContext.StationOpen;
        private static bool ForgeOpen => InputContext.ForgeOpen;

        // Mode courant du combo Triangle+droite dans la station de reparation/recyclage.
        // Remis a Repair a chaque (re)ouverture de la station (cf. InputContext.Refresh)
        // pour ne jamais surprendre avec un renforcement laisse arme d'une session precedente.
        private static bool _reinforceMode;

        public static void ResetRepairMode() => _reinforceMode = false;

        public static void ToggleRepairMode()
        {
            if (!StationOpen) return; // contextuel : muet sans station
            _reinforceMode = !_reinforceMode;
            TtsText.Say(Strings.L(_reinforceMode ? "repair.mode.reinforce" : "repair.mode.repair"), true);
        }

        // Transfert = meme appel direct que faisait la roue (le geste natif est
        // maintien + A, pas simulable par armement d'input).
        public static void TransferSelected()
        {
            var slot = Manager.ui != null ? Manager.ui.currentSelectedUIElement as InventorySlotUI : null;
            if (slot == null) return;
            slot.TryToSendItemToOtherInventoryOrEquip();
            TtsText.Say(Strings.L("wheel.transfer") + ", " + Strings.L("wheel.done"), true);
        }

        // Applique le mode courant (repare par defaut, renforce si bascule via
        // Triangle+bas). Renforcer booste la durabilite max au-dela du plafond normal
        // (jusqu'a x2, cf. InventoryUtility.CanBeRepaired) au lieu de juste reparer.
        public static void RepairSelected()
        {
            if (!StationOpen) return; // contextuel : muet sans station
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null || player.activeCraftingHandler == null) return;

            var slot = Manager.ui.currentSelectedUIElement as InventorySlotUI;
            var handler = slot != null ? slot.GetInventoryHandler() : null;
            bool reinforce = _reinforceMode;
            if (handler == null
                || !player.activeCraftingHandler.CanBeRepaired(slot.inventorySlotIndex, handler, reinforce))
            {
                TtsText.Say(Strings.L(reinforce ? "reinforce.none" : "repair.none"), true);
                return;
            }

            player.activeCraftingHandler.RepairOrReinforce(player, slot.inventorySlotIndex, handler, reinforce);
            TtsText.Say(Strings.L(reinforce ? "reinforce.done" : "repair.done"), true);
        }

        // Recycle le contenu des slots de la station (l'equivalent du gros bouton
        // gris). Salvage() porte deja ses gardes et ses sons natifs ; on pre-teste
        // CanSalvageAnyItem uniquement pour pouvoir annoncer un refus parlant.
        public static void SalvageStation()
        {
            if (!StationOpen) return; // contextuel : muet sans station
            var player = Manager.main != null ? Manager.main.player : null;
            var station = FindStation();
            if (player == null || player.activeCraftingHandler == null || station == null) return;

            if (!player.activeCraftingHandler.inventoryHandler.CanSalvageAnyItem())
            {
                TtsText.Say(Strings.L("salvage.none"), true);
                return;
            }

            station.Salvage();
            TtsText.Say(Strings.L("salvage.done"), true);
        }

        // Bascule la "categorie" de recettes affichee sur un etabli/station classique qui
        // en embarque plusieurs (ex. enclume fer = ses propres recettes + celles de
        // l'enclume cuivre qu'elle remplace, plus besoin de la garder). Le jeu decoupe le
        // buffer complet de recettes en "fenetres" (une par batiment inclus,
        // IncludedCraftingBuildingsBuffer) et n'en montre qu'une a la fois ; les fleches
        // natives (CraftingCategoryNavigationUI, haut/bas souris) ne sont jamais atteintes
        // par notre nav -> on appelle directement le canal officiel
        // Manager.ui.ChangeCraftingCategoryWindowInfo. Muet si une seule fenetre (rien a
        // basculer, comme les autres combos contextuels).
        public static void SwitchCraftingCategory(bool forward)
        {
            if (!InputContext.CraftingUIOpen) return;
            var windows = Manager.ui.GetCraftingCategoryWindowInfos();
            if (windows == null || windows.Count <= 1) return;

            Manager.ui.ChangeCraftingCategoryWindowInfo(forward);
            string name = CurrentCraftingCategoryName();
            TtsText.Say(!string.IsNullOrEmpty(name) ? name : Strings.L("craft.category.switched"), true);
        }

        // Nom du batiment "inclus" dont on affiche actuellement les recettes. Le
        // CraftingCategoryWindowInfo natif ne garde que l'icone (pas l'ObjectID) -> on
        // relit nous-memes IncludedCraftingBuildingsBuffer (meme donnee que celle qui a
        // construit les fenetres, CraftingBuilding.OnOccupied), aligne par index de fenetre.
        private static string CurrentCraftingCategoryName()
        {
            try
            {
                var building = UIManager.GetCraftingBuilding();
                if (building == null) return null;
                var windows = Manager.ui.GetCraftingCategoryWindowInfos();
                var current = Manager.ui.GetCraftingCategoryWindowInfo();
                if (windows == null || current == null) return null;
                int idx = windows.IndexOf(current);
                if (idx < 0) return null;

                var buf = EntityUtility.GetBuffer<IncludedCraftingBuildingsBuffer>(building.entity, building.world);
                if (idx >= buf.Length) return null;
                return InGameTtsCore.ResolveObjectName(buf[idx].objectID);
            }
            catch { return null; }
        }

        // Forge d'amelioration (1 slot) : on depose un objet, ce combo l'ameliore d'un
        // niveau. Comme la reparation, le gros bouton visuel ne fait que basculer un mode
        // souris -> on appelle directement le canal officiel UpgradeForgeUI.Upgrade(). Refus
        // parlant si slot vide / niveau max / materiaux insuffisants (le bouton expose son
        // eligibilite via ShouldBeActive() et le cout via GetRequiredMaterials()).
        public static void UpgradeForgeAction()
        {
            if (!ForgeOpen) return; // contextuel : muet sans forge
            var player = Manager.main != null ? Manager.main.player : null;
            var forge = FindForge();
            if (player == null || forge == null || forge.button == null) return;

            var data = forge.GetInventoryHandler().GetObjectData(0);
            if (data.objectID == ObjectID.None)
            {
                TtsText.Say(Strings.L("forge.empty"), true);
                return;
            }
            // GetRequiredMaterials renvoie null si l'objet est au niveau max (ou non
            // ameliorable) ; sinon ShouldBeActive distingue "pas assez de materiaux".
            List<PugDatabase.MaterialInfo> mats = null;
            try { mats = forge.button.GetRequiredMaterials(false, false); }
            catch { }
            if (mats == null)
            {
                TtsText.Say(Strings.L("forge.maxed"), true);
                return;
            }
            if (!forge.button.ShouldBeActive())
            {
                TtsText.Say(Strings.L("forge.noMaterials"), true);
                return;
            }

            forge.Upgrade();
            TtsText.Say(Strings.L("forge.done"), true);
        }

        // Volet "forge" du combo details (Triangle + haut) : cout d'amelioration de l'objet
        // depose (materiaux requis pour passer au niveau suivant). Null hors forge / slot
        // vide / niveau max. La forge n'a qu'un slot, le cout ne depend pas de l'element
        // focalise -> on lit directement le bouton (meme source que l'infobulle native).
        public static string BuildForgeDetail()
        {
            if (!ForgeOpen) return null;
            var forge = FindForge();
            if (forge == null || forge.button == null) return null;

            List<PugDatabase.MaterialInfo> mats = null;
            try { mats = forge.button.GetRequiredMaterials(false, false); }
            catch { }
            if (mats == null || mats.Count == 0) return null;

            var items = new List<string>();
            foreach (var m in mats)
            {
                if (m == null) continue;
                string nom = InGameTtsCore.ResolveObjectName(m.objectID);
                if (!string.IsNullOrEmpty(nom)) items.Add(m.amountNeeded + " " + nom);
            }
            if (items.Count == 0) return null;
            return Strings.L("forge.cost") + " " + string.Join(", ", items);
        }

        // "Tout vendre" (Triangle + gauche quand un marchand est ouvert) : encaisse tout
        // ce qui est depose dans les emplacements de vente via le canal serveur officiel
        // SellAll (memes gardes et sons natifs que le bouton Vendre du jeu). Annonce le
        // total encaisse, ou un refus parlant si les emplacements de vente sont vides.
        public static void SellAllToMerchant()
        {
            var ui = Manager.ui;
            if (ui == null || !ui.isSellUIShowing) return; // contextuel : muet hors vente
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null || player.sellSlotsHandler == null) return;

            var handler = player.sellSlotsHandler.sellSlotsInventoryHandler;
            int total = handler.GetCoinValueAll(player, false);
            if (total <= 0)
            {
                TtsText.Say(Strings.L("merchant.sellNothing"), true);
                return;
            }
            handler.SellAll(player, player.RenderPosition);
            TtsText.Say(Strings.L("merchant.sold") + " " + total + " " + Strings.L("merchant.coins"), true);
        }

        // Volet "station" du combo details (Triangle + haut) : cout de reparation OU
        // renforcement (selon le mode courant, meme source que l'infobulle native)
        // + gain de recyclage estime. Null si pas de station ouverte / pas d'objet.
        public static string BuildStationDetail(UIelement element)
        {
            if (!StationOpen) return null;
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null || player.activeCraftingHandler == null) return null;

            var slot = element as InventorySlotUI;
            var handler = slot != null ? slot.GetInventoryHandler() : null;
            if (handler == null) return null;
            var data = handler.GetObjectData(slot.inventorySlotIndex);
            if (data.objectID == ObjectID.None) return null;

            var parts = new List<string>();

            // Cout de reparation/renforcement, seulement si l'objet est eligible au mode
            // courant. GetRequiredMaterials(isRepairing, isReinforcing) : le jeu appelle
            // (true,false) pour reparer, (false,true) pour renforcer (UIMouse.cs).
            bool reinforce = _reinforceMode;
            if (player.activeCraftingHandler.CanBeRepaired(slot.inventorySlotIndex, handler, reinforce))
            {
                List<PugDatabase.MaterialInfo> mats = null;
                try { mats = slot.GetRequiredMaterials(!reinforce, reinforce); }
                catch { }
                if (mats != null && mats.Count > 0)
                {
                    var items = new List<string>();
                    foreach (var m in mats)
                    {
                        if (m == null) continue;
                        string nom = InGameTtsCore.ResolveObjectName(m.objectID);
                        if (!string.IsNullOrEmpty(nom)) items.Add(m.amountNeeded + " " + nom);
                    }
                    if (items.Count > 0)
                        parts.Add(Strings.L(reinforce ? "reinforce.cost" : "repair.cost") + " " + string.Join(", ", items));
                }
            }

            // Gain de recyclage. Formules reprises du jeu (InventoryUtility,
            // GetScrapPartsValue + TrySalvageObject) - a resynchroniser si une maj
            // les change : pieces = max(1, arrondi(niveau x 2 x repairCostMultiplier
            // x 2)) ; materiaux rendus = 30 a 49 % des ingredients de craft selon la
            // durabilite restante. Silence si l'objet n'a ni durabilite ni niveau
            // (consommables, empilables : pas recyclables en pratique).
            int scrap = SalvageScrapValue(player, data, out int matPercent);
            if (scrap > 0)
            {
                string s = Strings.L("salvage.gain") + " " + scrap + " "
                         + InGameTtsCore.ResolveObjectName(ObjectID.ScrapPart);
                if (matPercent > 0)
                    s += ", " + Strings.L("salvage.materials").Replace("{0}", matPercent.ToString());
                parts.Add(s);
            }

            return parts.Count > 0 ? string.Join(". ", parts) : null;
        }

        private static int SalvageScrapValue(PlayerController player, ObjectDataCD data, out int matPercent)
        {
            matPercent = 0;
            try
            {
                var bank = player.querySystem.GetSingleton<PugDatabase.DatabaseBankCD>();
                Entity prefab = PugDatabase.GetPrimaryPrefabEntity(data.objectID, bank.databaseBankBlob, data.variation);
                var world = Manager.ecs.ClientWorld;
                if (!EntityUtility.HasComponentData<DurabilityCD>(prefab, world)
                    || !EntityUtility.HasComponentData<LevelCD>(prefab, world)) return 0;
                var dur = EntityUtility.GetComponentData<DurabilityCD>(prefab, world);
                var lvl = EntityUtility.GetComponentData<LevelCD>(prefab, world);

                int scrap = (int)Mathf.Max(1f, Mathf.Round(lvl.level * 2 * dur.repairCostMultiplier * 2f));
                // ObjectDataCD.amount = durabilite COURANTE pour un objet a durabilite.
                float frac = dur.maxDurability > 0
                    ? Mathf.Min((float)data.amount / dur.maxDurability, 1f) : 1f;
                matPercent = Mathf.RoundToInt(Mathf.Lerp(30f, 49f, frac));
                return scrap;
            }
            catch { return 0; }
        }

        // La fenetre de la station actuellement ouverte (pour appeler son Salvage()
        // natif), sinon null. Recherche a l'appui du combo seulement.
        private static SalvageAndRepairUI FindStation()
        {
            var stations = Object.FindObjectsByType<SalvageAndRepairUI>(FindObjectsSortMode.None);
            foreach (var s in stations)
                if (s != null && s.isShowing) return s;
            return null;
        }

        // La forge d'amelioration ouverte, sinon null. Recherche a l'appui du combo.
        private static UpgradeForgeUI FindForge()
        {
            var forges = Object.FindObjectsByType<UpgradeForgeUI>(FindObjectsSortMode.None);
            foreach (var f in forges)
                if (f != null && f.isShowing) return f;
            return null;
        }
    }
}
