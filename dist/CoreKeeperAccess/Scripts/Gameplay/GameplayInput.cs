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

        private static bool StationOpen => InputContext.StationOpen;
        private static bool ForgeOpen => InputContext.ForgeOpen;

        // Combos de GAMEPLAY (hors nav inventaire, hors menus) consommes ici :
        // Triangle + gauche = prospection minerai. Appele chaque frame apres InfoKey.
        public static void Tick()
        {
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null) { _prospectPending = false; return; }

            // Centre de l'index d'objets (TileReaderSystem le reconstruit autour).
            ObjectIndex.Center = new float2(player.WorldPosition.x, player.WorldPosition.z);

            // Tisse le reseau de navigation (torches -> noeuds, trajets -> aretes). Apres la
            // publication du centre : l'index est alimente pour la meme position de joueur.
            BeaconTracker.Tick(player);
            BeaconGuide.Tick(player); // guidage a l'oreille vers un noeud (si le mode est actif)

            // Recalcul local du reseau (tranche C) : reponse du systeme -> on annonce le
            // nombre d'aretes ajoutees par le tissage en ligne de vue.
            if (NetworkRecalc.ResultValid)
            {
                NetworkRecalc.ResultValid = false;
                string msg = Strings.L("netrecalc.done") + ", "
                    + NetworkRecalc.AddedEdges + " " + Strings.L("netrecalc.links");
                if (NetworkRecalc.RemovedEdges > 0)
                    msg += ", " + NetworkRecalc.RemovedEdges + " " + Strings.L("netrecalc.removed");
                if (NetworkRecalc.LostNodes > 0)
                    msg += ", " + NetworkRecalc.LostNodes + " " + Strings.L("netrecalc.lost");
                TtsText.Say(msg, true);
            }

            // Les combos (prospection, ping sonar, double-tap carte) sont routes par
            // ComboDispatcher (cf. ComboBindings). Ici ne restent que les ticks.
            PingSonar.Tick(player);
            StepEngine.Tick(player);
            ProximitySonar.Tick(player);
            PlacementReader.Tick(player);
            StatsWheel.Tick(player);

            // Etalement de l'emprise : declenche par un DEPLACEMENT delibere du curseur
            // (BuildModeNavigator pose FootprintDueAt), annonce apres un petit delai - le
            // temps que le ghost rattrape le curseur (sinon ca oscille), et UNIQUEMENT sur
            // geste -> n'interrompt plus les autres TTS en continu.
            if (BuildModeNavigator.FootprintDueAt > 0f && Time.unscaledTime >= BuildModeNavigator.FootprintDueAt)
            {
                BuildModeNavigator.FootprintDueAt = -1f;
                string fp = InGameTtsCore.FootprintFromCursor(BuildModeNavigator.CursorTile);
                if (!string.IsNullOrEmpty(fp)) TtsText.Say(fp, true);
            }

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
        private static long _lastInteractable;
        private static float _nextInteractPoll;

        private static void WatchInteractable(PlayerController player)
        {
            if (Time.unscaledTime < _nextInteractPoll) return;
            _nextInteractPoll = Time.unscaledTime + InteractPollInterval;

            long key = 0;
            ObjectID id = ObjectID.None;
            try
            {
                if (!EntityUtility.HasComponentData<InteractorCD>(player.entity, player.world)) return;
                var e = EntityUtility.GetComponentData<InteractorCD>(player.entity, player.world)
                    .currentClosestInteractable;
                if (e != Entity.Null && EntityUtility.HasComponentData<ObjectDataCD>(e, player.world))
                {
                    id = EntityUtility.GetComponentData<ObjectDataCD>(e, player.world).objectID;
                    key = EntityKey.Of(e);
                }
            }
            catch (System.Exception ex) { Diag.Error("A11yInteractDiag", ex); return; }

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
        internal static void RequestProspect(PlayerController player)
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

        // La forge d'amelioration ouverte, sinon null. Recherche a l'appui du combo.
        private static UpgradeForgeUI FindForge()
        {
            var forges = Object.FindObjectsByType<UpgradeForgeUI>(FindObjectsSortMode.None);
            foreach (var f in forges)
                if (f != null && f.isShowing) return f;
            return null;
        }
    }

    // Ping sonar (Triangle + L1) : la "photo sonore" de l'environnement - le coup d'oeil
    // circulaire du voyant. Un appui = une salve de bips spatialises, un par cible
    // notable autour du joueur, egrenes du PLUS PROCHE au PLUS LOIN (l'ordre temporel
    // encode la distance). Trois timbres, langage du laser reutilise : hostile, creature
    // paisible, trouvaille (zone de fouille). Pas de TTS dans la salve (le timbre donne
    // la categorie) ; "Rien autour" si vide. Pendant la salve, le laser et la sentinelle
    // se taisent (fenetre sonore reservee, via Silencing).
    // Creatures via PingScan (systeme ECS) ; trouvailles lues directement dans
    // ObjectIndex (rempli par le meme systeme, main thread -> lecture sure).
    // (A reloger dans un fichier dedie au prochain build Unity.)
    internal static class PingSonar
    {
        private const float Radius = 12f;          // rayon en cases (= portee du laser)
        private const float SlotInterval = 0.12f;  // espacement des bips de la salve
        private const int MaxBeeps = 12;           // plafond (au-dela : le plus proche d'abord)
        private const float ResultTimeout = 1f;    // garde-fou : ne jamais rester gele

        // Timbres : memes placeholders que le laser (l'utilisateur choisira les vrais).
        private const SfxID HostileSfx = SfxID.proximity_sensor_set;
        private const SfxID CreatureSfx = SfxID.inventory_doot;
        private const SfxID FindSfx = SfxID.inventory_ding;
        private const float HostileVolume = 0.5f;
        private const float PassiveVolume = 0.35f;

        private struct Beep
        {
            public float2 Pos;
            public SfxID Sfx;
            public float Volume;
            public float DistSq; // tri proche -> loin
        }

        private static readonly List<Beep> _salvo = new List<Beep>();
        private static bool _pending;     // demande posee, en attente du scan systeme
        private static float _requestedAt;
        private static int _next;         // prochain bip de la salve
        private static float _nextTime;

        // Fenetre sonore reservee : laser et sentinelle consultent ce flag.
        public static bool Silencing => _pending || _next < _salvo.Count;

        public static void Trigger(PlayerController player)
        {
            if (Silencing) return; // salve en cours : on ne rearme pas par-dessus
            PingScan.Center = new float2(player.WorldPosition.x, player.WorldPosition.z);
            PingScan.Radius = Radius;
            PingScan.ResultValid = false;
            PingScan.Requested = true;
            _pending = true;
            _requestedAt = Time.unscaledTime;
            _salvo.Clear();
            _next = 0;
        }

        public static void Tick(PlayerController player)
        {
            if (player == null) { _pending = false; _salvo.Clear(); _next = 0; return; }

            if (_pending)
            {
                if (PingScan.ResultValid) BuildSalvo(player);
                else if (Time.unscaledTime - _requestedAt > ResultTimeout) _pending = false;
                else return;
            }

            // Egrene la salve : un bip par creneau, pan/pitch recalcules a la position
            // COURANTE du joueur (s'il marche pendant la salve, l'image reste juste).
            if (_next < _salvo.Count && Time.unscaledTime >= _nextTime)
            {
                var b = _salvo[_next++];
                float2 d = b.Pos - new float2(player.WorldPosition.x, player.WorldPosition.z);
                float pan = GameplayAudio.PanFromTiles(d.x);
                float pitch = Mathf.Clamp(Mathf.Pow(2f, d.y / 12f), 0.5f, 2f);
                GameplayAudio.PlaySpatial(b.Sfx, pan, pitch,
                    b.Volume * GameplayAudio.DistanceTrim(math.length(d)));
                _nextTime = Time.unscaledTime + SlotInterval;
            }
        }

        // Fusionne creatures (PingScan) + trouvailles (ObjectIndex), trie du plus
        // proche au plus loin, tronque au plafond.
        private static void BuildSalvo(PlayerController player)
        {
            _pending = false;
            _salvo.Clear();
            _next = 0;
            float2 center = new float2(player.WorldPosition.x, player.WorldPosition.z);

            for (int i = 0; i < PingScan.Count; i++)
            {
                var t = PingScan.Targets[i];
                _salvo.Add(new Beep
                {
                    Pos = t.Pos,
                    Sfx = t.Hostile ? HostileSfx : CreatureSfx,
                    Volume = t.Hostile ? HostileVolume : PassiveVolume,
                    DistSq = math.lengthsq(t.Pos - center),
                });
            }

            // Trouvailles : objets de l'index dans le rayon. Un spot multi-cases est
            // marque sur plusieurs cases -> dedup grossiere (une trouvaille deja
            // retenue a moins de 2 cases absorbe la suivante).
            float r2 = Radius * Radius;
            foreach (var kv in ObjectIndex.Map)
            {
                if (!IsFind(kv.Value.Id)) continue;
                float2 p = new float2((int)(kv.Key >> 32), (int)(uint)kv.Key);
                float distSq = math.lengthsq(p - center);
                if (distSq > r2) continue;
                bool dup = false;
                for (int i = 0; i < _salvo.Count; i++)
                {
                    if (_salvo[i].Sfx == FindSfx && math.lengthsq(_salvo[i].Pos - p) <= 4f)
                    { dup = true; break; }
                }
                if (dup) continue;
                _salvo.Add(new Beep { Pos = p, Sfx = FindSfx, Volume = PassiveVolume, DistSq = distSq });
            }

            if (_salvo.Count == 0)
            {
                TtsText.Say(Strings.L("ping.none"), true);
                return;
            }

            _salvo.Sort((a, b) => a.DistSq.CompareTo(b.DistSq));
            if (_salvo.Count > MaxBeeps) _salvo.RemoveRange(MaxBeeps, _salvo.Count - MaxBeeps);
            _nextTime = Time.unscaledTime; // premier bip immediat
        }

        // Trouvailles reconnues : les zones de fouille (toutes variantes de biome).
        // Liste a etendre au fil des decouvertes du meme genre.
        private static bool IsFind(ObjectID id)
            => id == ObjectID.DiggingSpot
            || id == ObjectID.DiggingSpotNature
            || id == ObjectID.DiggingSpotSea
            || id == ObjectID.DiggingSpotDesert
            || id == ObjectID.DiggingSpotLava
            || id == ObjectID.DiggingSpotExcavation;
    }

    // Moteur de pas PARTAGE : une seule horloge de locomotion. Detecte le franchissement
    // de case (RoundToInt sur la position monde) et dispatche aux COUCHES de feedback
    // activees independamment dans A11ySettings. Aujourd'hui une couche (le bip de pas) ;
    // le sonar d'interstices viendra se brancher ici (etage 2). Appele chaque frame depuis
    // GameplayInput.Tick - il tourne en PERMANENCE (meme couches eteintes) pour garder la
    // case courante fraiche : rallumer une couche en pleine marche ne rejoue pas un delta
    // perime.
    internal static class StepEngine
    {
        private static int _cx, _cz;
        private static bool _hasCell;

        public static void Tick(PlayerController player)
        {
            if (player == null) { _hasCell = false; return; }
            Vector3 pos = player.WorldPosition;
            int cx = Mathf.RoundToInt(pos.x), cz = Mathf.RoundToInt(pos.z);
            if (!_hasCell) { _cx = cx; _cz = cz; _hasCell = true; return; }
            if (cx == _cx && cz == _cz) return;

            int dx = cx - _cx, dz = cz - _cz;
            _cx = cx; _cz = cz;

            // Couches togglables, allumees separement (cf. A11ySettings).
            if (A11ySettings.StepBeep) StepBeep.OnStep(dx, dz);
            // (Etage 2 : le sonar d'interstices se branchera ici.)
        }
    }

    // Couche 0 : bip de pas (la "boussole de locomotion"). Un petit bip par case franchie
    // encode la direction du deplacement - pan est/ouest, pitch nord/sud (langage du
    // curseur) -> confirme le cap ET compte les cases a l'oreille. DECOUPLE du snap
    // construction (DirectionAssist) le 16 juin : c'est un feedback PERMANENT (actif par
    // defaut, regle au panneau), la ou le snap est un outil PONCTUEL. Diagonale en marche
    // libre : arrondie au cardinal dominant (comme avant, ou le snap forcait le cardinal).
    internal static class StepBeep
    {
        private const float NorthPitch = 1.5f;   // nord = plus aigu
        private const float SouthPitch = 0.67f;  // sud = plus grave (est/ouest = neutre 1.0)

        public static void OnStep(int dx, int dz)
        {
            float pan, pitch;
            if (Mathf.Abs(dx) >= Mathf.Abs(dz)) { pan = dx >= 0 ? 1f : -1f; pitch = 1f; }   // est / ouest
            else { pan = 0f; pitch = dz > 0 ? NorthPitch : SouthPitch; }                    // nord / sud
            GameplayAudio.PlayTone(pan, pitch, A11ySettings.DirectionTickVolume);
        }
    }

    // Snap directionnel (toggle Triangle + L3). Tant qu'il est actif, le deplacement au
    // stick gauche est SNAPPE au cardinal dominant (la composante perpendiculaire est
    // annulee, cf. DirectionSnapSystem) -> lignes droites franches sans deviation, pour
    // poser des murs / labourer / semer en rangs. On ne touche NI a la pose NI a la
    // vitesse : le joueur tient LT et marche, le jeu pose ; on rectifie juste le cap.
    // DECOUPLE du bip de pas (StepBeep) le 16 juin : deux toggles independants.
    internal static class DirectionAssist
    {
        // Etat persiste dans A11ySettings : Triangle+L3 ET le panneau de reglages pilotent
        // la meme source de verite (le snap est donc reactive tel quel au relancement).
        public static bool Active
        {
            get => A11ySettings.SnapDirectional;
            set => A11ySettings.SnapDirectional = value;
        }

        // Bascule le snap + annonce l'etat.
        public static void Toggle()
        {
            Active = !Active;
            TtsText.Say(Strings.L(Active ? "direction.assist.on" : "direction.assist.off"), true);
        }
    }

    // Snappe la marche au cardinal quand DirectionAssist est actif. Tourne APRES
    // SendClientInputSystem (qui vient d'ecrire movementDirection depuis le stick) :
    // on lit cette direction, on annule la composante perpendiculaire (la dominante
    // garde sa magnitude = vitesse analogique preservee), on reecrit. Cede au
    // point-and-click (MoveCommand) pour ne pas se marcher dessus. Meme pont ECS que
    // PlayerMoveToSystem (cf. BuildModeNavigator).
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(RunSimulationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(SendClientInputSystem))]
    public partial class DirectionSnapSystem : SystemBase
    {
        private const float SnapDeadzone = 0.1f;
        private EntityQuery _query;

        protected override void OnCreate()
        {
            _query = GetEntityQuery(
                ComponentType.ReadWrite<ClientInputData>(),
                ComponentType.ReadOnly<GhostOwnerIsLocal>());
        }

        protected override void OnUpdate()
        {
            if (!DirectionAssist.Active || MoveCommand.Active) return;
            var entities = _query.ToEntityArray(Allocator.Temp);
            try
            {
                if (entities.Length == 0) return;
                Entity e = entities[0];
                ClientInputData data = EntityManager.GetComponentData<ClientInputData>(e);
                ClientInput ci = UnsafeUtility.As<ClientInputData, ClientInput>(ref data);

                float2 m = ci.movementDirection;
                if (math.length(m) > SnapDeadzone)
                {
                    if (math.abs(m.x) >= math.abs(m.y)) m.y = 0f;   // est-ouest domine
                    else m.x = 0f;                                  // nord-sud domine
                    ci.movementDirection = m;
                    data = UnsafeUtility.As<ClientInput, ClientInputData>(ref ci);
                    EntityManager.SetComponentData(e, data);
                }
            }
            finally { entities.Dispose(); }
        }
    }

    // Gel de la marche pendant la touche access (Triangle tenu). Le stick gauche pilote
    // alors la roue de stats (cf. StatsWheel) au lieu de deplacer le perso : on annule
    // movementDirection tant que Triangle est tenu. Meme pont ECS que DirectionSnapSystem
    // (apres SendClientInputSystem, qui vient d'ecrire le mouvement depuis le stick).
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(RunSimulationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(SendClientInputSystem))]
    public partial class AccessKeyMovementLockSystem : SystemBase
    {
        private EntityQuery _query;

        protected override void OnCreate()
        {
            _query = GetEntityQuery(
                ComponentType.ReadWrite<ClientInputData>(),
                ComponentType.ReadOnly<GhostOwnerIsLocal>());
        }

        protected override void OnUpdate()
        {
            if (!CoreKeeperAccess.Controls.InfoKey.ModifierHeld) return;
            var entities = _query.ToEntityArray(Allocator.Temp);
            try
            {
                if (entities.Length == 0) return;
                Entity e = entities[0];
                ClientInputData data = EntityManager.GetComponentData<ClientInputData>(e);
                ClientInput ci = UnsafeUtility.As<ClientInputData, ClientInput>(ref data);
                if (math.lengthsq(ci.movementDirection) > 0f)
                {
                    ci.movementDirection = float2.zero;
                    data = UnsafeUtility.As<ClientInput, ClientInputData>(ref ci);
                    EntityManager.SetComponentData(e, data);
                }
            }
            finally { entities.Dispose(); }
        }
    }

    // Lecture du PLACEMENT pour les objets en main (pose v1, 13 juin). Surveille le
    // PlacementCD du joueur (l'etat que le jeu calcule pour le fantome de pose) :
    //  - rotation (rotationVariationToPlace) change -> annonce le cap ("face nord") ;
    //  - validite (canPlaceObject = ghost bleu/rouge) passe a INVALIDE -> earcon de refus
    //    (PAS de TTS : le ghost oscille en balayant, on ne sature pas la voix).
    // La TAILLE de l'emprise est annoncee a la selection hotbar (HeldItemAnnouncePatch).
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
                // a zone reglable, donc muet pour les meubles).
                if (sizeVar != _lastSize && _lastSize >= 0)
                {
                    string zone = InGameTtsCore.ToolZoneLabel(player);
                    if (!string.IsNullOrEmpty(zone)) TtsText.Say(zone, true);
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
