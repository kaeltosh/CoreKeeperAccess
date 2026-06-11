using System.Collections.Generic;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Navigation;
using CoreKeeperAccess.Patches;
using Interaction;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Commandes gameplay de la touche access (combos Triangle + D-pad), consommees par
    // contexte (cf. InfoKey / dispatcher). Premieres commandes : combos d'INVENTAIRE.
    //  - Triangle + bas    = transferer l'objet selectionne vers l'autre inventaire
    //                        (reloge depuis la roue d'actions, secteur libere).
    //  - Triangle + droite = reparer l'objet selectionne (station de reparation ouverte).
    //  - Triangle + gauche = tout recycler (contenu des slots de la station).
    // Les combos station sont CONTEXTUELS : sans station ouverte ils sont muets (pas
    // d'annonce d'erreur), comme s'ils n'existaient pas. Et Triangle + haut (details)
    // s'enrichit, station ouverte, du COUT DE REPARATION et du GAIN DE RECYCLAGE
    // estime de l'objet selectionne (BuildStationDetail, appele par AnnounceDetail).
    // La reparation/le recyclage n'utilisent PAS les boutons/modes souris de la
    // SalvageAndRepairUI : on appelle directement les methodes publiques du jeu
    // (CraftingHandler.RepairOrReinforce / SalvageAndRepairUI.Salvage), qui passent
    // par la file d'actions serveur officielle comme l'artisanat.
    // (Le moteur de keymaps complet - multi-appui, couches - viendra s'ajouter ici.)
    internal static class GameplayInput
    {
        // CONFIRME en jeu (build 28) : la stat VisibleOreDistance ne porte que le BONUS
        // (talent minage "Night Vision", +2/point) ; la distance de BASE des paillettes
        // est codee en dur dans le shader, illisible. On approxime la base a 10 cases
        // (= portee de l'ambiance minerai) et on AJOUTE le bonus, comme le shader.
        private const int BaseProspectRadius = 10;
        private const int MaxProspectRadius = 30; // plafond perf
        private const float ProspectDingVolume = 0.8f;

        private static bool _prospectPending;
        private static int _prospectRadius;

        private static bool StationOpen
            => Manager.ui != null && Manager.ui.isSalvageAndRepairUIShowing;

        // Combos de GAMEPLAY (hors nav inventaire, hors menus) consommes ici :
        // Triangle + gauche = prospection minerai. Appele chaque frame apres InfoKey.
        public static void Tick()
        {
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null) { _prospectPending = false; return; }

            // Centre de l'index d'objets (TileReaderSystem le reconstruit autour).
            ObjectIndex.Center = new float2(player.WorldPosition.x, player.WorldPosition.z);

            bool uiBusy = InventoryNavState.SuppressNativeInput
                          || (Manager.menu != null && Manager.menu.IsAnyMenuActive());
            if (!uiBusy && InfoKey.ComboLeft) RequestProspect(player);

            TickProspect(player);
            WatchInteractable(player);
        }

        // Annonce d'INTERACTION A PORTEE : le jeu maintient sur le joueur l'interactible
        // le plus proche actuellement atteignable (InteractorCD.currentClosestInteractable,
        // la donnee qui pilote le prompt visuel des voyants). On annonce au CHANGEMENT
        // ("Statue du boss slime, interaction disponible") -> on sait toujours si A va
        // faire quelque chose et sur quoi. Regle le "il faut etre au bon endroit" des
        // objets multi-cases (statues, Core...). Sortie de portee : silence.
        private const float InteractPollInterval = 0.2f;
        private static int _lastInteractable;
        private static float _nextInteractPoll;

        private static void WatchInteractable(PlayerController player)
        {
            if (Time.unscaledTime < _nextInteractPoll) return;
            _nextInteractPoll = Time.unscaledTime + InteractPollInterval;

            int key = 0;
            ObjectID id = ObjectID.None;
            try
            {
                if (!EntityUtility.HasComponentData<InteractorCD>(player.entity, player.world)) return;
                var e = EntityUtility.GetComponentData<InteractorCD>(player.entity, player.world)
                    .currentClosestInteractable;
                if (e != Entity.Null && EntityUtility.HasComponentData<ObjectDataCD>(e, player.world))
                {
                    id = EntityUtility.GetComponentData<ObjectDataCD>(e, player.world).objectID;
                    key = e.Index;
                }
            }
            catch { return; }

            if (key == _lastInteractable) return;
            _lastInteractable = key;
            if (key == 0 || id == ObjectID.None) return;

            string name = InGameTtsCore.ResolveObjectName(id);
            if (string.IsNullOrEmpty(name)) return;
            // interrupt=true (demande utilisateur) : info de POSITION, perimee si elle
            // attend son tour dans la file - on marche, le point chaud c'est MAINTENANT.
            TtsText.Say(name + ", " + Strings.L("interact.available"), true);
        }

        // Pose la demande de scan : rayon = stat VisibleOreDistance du perso, la MEME
        // que le shader des paillettes (equite stricte : les talents de minage et
        // objets qui l'augmentent portent aussi notre prospection).
        private static void RequestProspect(PlayerController player)
        {
            int bonus = 0;
            try
            {
                bonus = EntityUtility.GetConditionEffectValue(
                    ConditionEffect.VisibleOreDistance, player.entity, player.world);
            }
            catch { }
            int radius = Mathf.Clamp(BaseProspectRadius + bonus, 1, MaxProspectRadius);

            OreScan.Center = new int2(
                Mathf.RoundToInt(player.WorldPosition.x),
                Mathf.RoundToInt(player.WorldPosition.z));
            OreScan.Radius = radius;
            OreScan.ResultValid = false;
            OreScan.Requested = true;
            _prospectPending = true;
            _prospectRadius = radius;
        }

        // Consomme la reponse du systeme (frame suivante) : ding positionnel sur le
        // filon (son natif oreHit, pan/distance par le jeu + pitch vertical maison,
        // meme langage que le curseur) + TTS cardinal et distance.
        private static void TickProspect(PlayerController player)
        {
            if (!_prospectPending || !OreScan.ResultValid) return;
            _prospectPending = false;

            if (!OreScan.Found)
            {
                TtsText.Say(Strings.L("prospect.none") + ", "
                    + Strings.L("prospect.radius") + " " + _prospectRadius, true);
                return;
            }

            float2 p = new float2(player.WorldPosition.x, player.WorldPosition.z);
            float2 d = new float2(OreScan.Tile.x, OreScan.Tile.y) - p;
            float pitch = Mathf.Clamp(Mathf.Pow(2f, d.y / 12f), 0.5f, 2f);
            GameplayAudio.PlayTableSpatialNoPitchDev(SfxTableID.oreHit,
                new Vector3(OreScan.Tile.x, 0f, OreScan.Tile.y), ProspectDingVolume, pitch);

            int dist = Mathf.RoundToInt(math.length(d));
            string text = Strings.L("prospect.ore") + ", " + (dist < 1
                ? Strings.L("prospect.here")
                : Cardinal(d) + ", " + dist + " " + Strings.L("teleport.tiles"));
            TtsText.Say(text, true);
        }

        private static readonly string[] DirKeys =
            { "dir.n", "dir.ne", "dir.e", "dir.se", "dir.s", "dir.sw", "dir.w", "dir.nw" };

        // Secteur cardinal (8) d'un vecteur monde x=est, y=nord (memes cles i18n que
        // la teleportation).
        private static string Cardinal(float2 d)
        {
            float ang = math.degrees(math.atan2(d.x, d.y));
            if (ang < 0f) ang += 360f;
            return Strings.L(DirKeys[((int)math.round(ang / 45f)) % 8]);
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

        public static void RepairSelected()
        {
            if (!StationOpen) return; // contextuel : muet sans station
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null || player.activeCraftingHandler == null) return;

            var slot = Manager.ui.currentSelectedUIElement as InventorySlotUI;
            var handler = slot != null ? slot.GetInventoryHandler() : null;
            if (handler == null
                || !player.activeCraftingHandler.CanBeRepaired(slot.inventorySlotIndex, handler, false))
            {
                TtsText.Say(Strings.L("repair.none"), true);
                return;
            }

            player.activeCraftingHandler.RepairOrReinforce(player, slot.inventorySlotIndex, handler, false);
            TtsText.Say(Strings.L("repair.done"), true);
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

        // Volet "station" du combo details (Triangle + haut) : cout de reparation de
        // l'objet selectionne (meme source que l'infobulle native du mode reparation)
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

            // Cout de reparation, seulement si l'objet est effectivement reparable.
            if (player.activeCraftingHandler.CanBeRepaired(slot.inventorySlotIndex, handler, false))
            {
                List<PugDatabase.MaterialInfo> mats = null;
                try { mats = slot.GetRequiredMaterials(true, false); }
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
                        parts.Add(Strings.L("repair.cost") + " " + string.Join(", ", items));
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
    }
}
