using System.Collections.Generic;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Annonce le nom des objets RAMASSES, a chaque ramassage (le jeu ne nomme l'objet qu'a la
    // PREMIERE decouverte d'un type via ChatWindow.NewItem, puis c'est un compteur visuel muet -
    // confirme dans PlayerController.DetectUndiscoveredObjectsInInventory).
    //
    // Le ramassage au sol est un systeme ECS server-authoritative (PickUpItemSystem) non
    // hookable proprement en hot-compile. On le deduit donc d'un DIFF cote client de l'inventaire
    // du joueur. CRUCIAL : on diff les TOTAUX PAR OBJET, pas les slots. Le jeu reorganise sans
    // cesse les slots (poser via le switch rapide echange deux slots, tri auto, etc.) -> un diff
    // slot par slot voit de faux "gains" de piles entieres a chaque echange. Avec les totaux :
    // changer de slot garde le total identique (rien), consommer = total qui baisse (ignore),
    // ramasser = total qui monte (annonce). Empilable -> somme des quantites ; non empilable ->
    // nombre d'exemplaires (l'amount serait la durabilite, pas une quantite).
    //
    // Garde-fou : on n'annonce QUE inventaire ferme (gameplay). On tient quand meme les totaux a
    // jour inventaire ouvert, pour ne pas dumper les manips manuelles a la fermeture.
    //
    // Anti-spam : gains accumules sur une courte fenetre (debounce a la traine) puis annonces en
    // une fois ("Terre fois 4, Cuivre fois 2"), en file d'attente NVDA (interrupt:false).
    internal static class PickupAnnouncer
    {
        // Totaux par objet : precedent (reference) et courant (recalcule chaque tick).
        private static readonly Dictionary<ObjectID, int> _prev = new Dictionary<ObjectID, int>();
        private static readonly Dictionary<ObjectID, int> _cur = new Dictionary<ObjectID, int>();
        private static bool _hasBaseline;
        private static bool _bagHasEmpty; // un slot libre dans le SAC principal (pour "plein")

        // Cache d'empilabilite par objet (evite un GetObjectInfo par slot par tick).
        private static readonly Dictionary<ObjectID, bool> _stackCache = new Dictionary<ObjectID, bool>();

        // Gains en attente d'annonce (ordre d'apparition preserve pour une lecture naturelle).
        private static readonly List<ObjectID> _order = new List<ObjectID>();
        private static readonly Dictionary<ObjectID, int> _counts = new Dictionary<ObjectID, int>();
        private static float _flushAt;        // debounce : annonce quand plus rien depuis Window
        private static float _firstPendingAt;  // garde-fou : flush force passe MaxAge
        private const float Window = 0.5f;     // silence apres le dernier ramassage avant d'annoncer
        private const float MaxAge = 2f;       // ramassage continu : on ne retient pas indefiniment

        public static void Tick()
        {
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null)
            {
                // Retour menu / changement de monde : on rebaseline a la prochaine entree.
                if (_hasBaseline) { _prev.Clear(); _hasBaseline = false; }
                ClearPending();
                return;
            }

            bool announce = A11ySettings.PickupAnnounce
                && Manager.ui != null && !Manager.ui.isAnyInventoryShowing
                && Manager.sceneHandler != null && Manager.sceneHandler.isSceneHandlerReady
                && !Manager.sceneHandler.cutsceneIsPlaying;

            ComputeTotals(player); // remplit _cur + _bagHasEmpty

            // Diff des totaux : toute HAUSSE = un gain (= un ramassage). Seulement si l'on a deja
            // une reference (sinon le 1er scan annoncerait tout l'inventaire) et que l'annonce
            // est active.
            if (announce && _hasBaseline)
                foreach (var kv in _cur)
                {
                    int prev = _prev.TryGetValue(kv.Key, out int p) ? p : 0;
                    int gained = kv.Value - prev;
                    if (gained <= 0) continue;
                    if (A11ySettings.PickupFilterBlocks && IsBlock(kv.Key)) continue; // blocs filtres au gre du joueur
                    AddPending(kv.Key, gained);
                }

            // Bascule la reference (TOUJOURS, meme inventaire ouvert).
            _prev.Clear();
            foreach (var kv in _cur) _prev[kv.Key] = kv.Value;
            _hasBaseline = true;

            CheckBlockedPickup(player, announce);
            FlushIfDue();
        }

        // Recalcule les totaux par objet sur les inventaires du joueur (sac + bourses) et note si
        // le SAC principal a au moins un slot vide (pour l'alerte "plein").
        private static void ComputeTotals(PlayerController player)
        {
            _cur.Clear();
            _bagHasEmpty = false;
            AddHandlerTotals(player.playerInventoryHandler, true);
            var eh = player.equipmentHandler;
            if (eh != null && eh.pouchInventorySlotsHandlers != null)
                foreach (var h in eh.pouchInventorySlotsHandlers)
                    AddHandlerTotals(h, false);
        }

        private static void AddHandlerTotals(InventoryHandler handler, bool isMainBag)
        {
            if (handler == null) return;
            int size;
            try { size = handler.size; } catch { return; }
            for (int i = 0; i < size; i++)
            {
                ObjectDataCD od;
                try { od = handler.GetContainedObjectData(i).objectData; }
                catch { continue; }
                if (od.objectID == ObjectID.None) { if (isMainBag) _bagHasEmpty = true; continue; }
                int add = IsStackable(od.objectID, od.variation) ? od.amount : 1;
                _cur.TryGetValue(od.objectID, out int c);
                _cur[od.objectID] = c + add;
            }
        }

        private static bool IsStackable(ObjectID id, int variation)
        {
            if (_stackCache.TryGetValue(id, out bool s)) return s;
            bool stackable = true;
            try { var info = PugDatabase.GetObjectInfo(id, variation); stackable = info == null || info.isStackable; }
            catch { }
            _stackCache[id] = stackable;
            return stackable;
        }

        // Un "bloc" = objet portant le tag ObjectCategoryTag.TileBlock (terre, pierre, murs
        // posables...) : la categorie a part du jeu pour les tuiles posables, la source du bruit
        // en minant. (variation 0 : le tag ne depend pas de la variation.)
        private static bool IsBlock(ObjectID id)
        {
            try
            {
                var info = PugDatabase.GetObjectInfo(id, 0);
                return info != null && info.tags != null && info.tags.Contains(ObjectCategoryTag.TileBlock);
            }
            catch { return false; }
        }

        private static void AddPending(ObjectID id, int qty)
        {
            if (_order.Count == 0) _firstPendingAt = Time.unscaledTime; // debut d'une salve
            if (_counts.TryGetValue(id, out int c)) _counts[id] = c + qty;
            else { _counts[id] = qty; _order.Add(id); }
            _flushAt = Time.unscaledTime + Window;
        }

        private static void FlushIfDue()
        {
            if (_order.Count == 0) return;
            float now = Time.unscaledTime;
            if (now < _flushAt && now - _firstPendingAt < MaxAge) return;

            bool withTotal = A11ySettings.PickupTotal;
            var parts = new List<string>(_order.Count);
            foreach (var id in _order)
            {
                string name = InGameTtsCore.ResolveObjectName(id);
                if (string.IsNullOrEmpty(name)) continue;
                int n = _counts[id];
                string label = n > 1 ? name + " " + Strings.L("pickup.times") + " " + n : name;
                if (withTotal)
                {
                    int total = _cur.TryGetValue(id, out int t) ? t : n;
                    // Inutile de redire le total quand on ne possede que ce qu'on vient de prendre.
                    if (total > n) label += ", " + total + " " + Strings.L("pickup.total");
                }
                parts.Add(label);
            }
            ClearPending();
            if (parts.Count > 0) TtsText.Say(string.Join(", ", parts), false);
        }

        private static void ClearPending()
        {
            _order.Clear();
            _counts.Clear();
        }

        // Alerte de ramassage BLOQUE ("Fer, inventaire plein"). Aucune notif native n'existe
        // (verifie dans ChatWindow.MessageTextType), et "plein" depend de l'OBJET : le plafond de
        // pile du jeu est 9999, donc il y a de la place s'il reste un slot vide OU une pile du
        // meme objet. On regarde les objets LACHES au sol (PickUpItemCD - present UNIQUEMENT sur
        // le loot lache, PAS sur les meubles poses) dans le rayon de ramassage du joueur et on
        // teste leur place contre les totaux deja calcules -> alerte seulement quand un objet a
        // cote ne rentre VRAIMENT pas. Throttle ~3 fois/s. Requetes ECS managees, pas un systeme.
        private static ObjectID _lastBlocked = ObjectID.None;
        private static float _nextScan;

        private static void CheckBlockedPickup(PlayerController player, bool announce)
        {
            if (!announce) { _lastBlocked = ObjectID.None; return; }
            float now = Time.unscaledTime;
            if (now < _nextScan) return;
            _nextScan = now + 0.3f;

            ObjectID blocked = ObjectID.None;
            try
            {
                // Rayon de ramassage du joueur (inclut le bonus d'aimant). Repli si absent.
                float pd = 1.6f;
                try { pd = EntityUtility.GetComponentData<AutoPickUpItemCD>(player.entity, player.world).pickupDistance; }
                catch { }
                float pd2 = pd * pd;

                var q = Manager.ecs.GetClientEntityQuery(new ComponentType[]
                {
                    ComponentType.ReadOnly<PickUpItemCD>(),
                    ComponentType.ReadOnly<LocalTransform>(),
                    ComponentType.ReadOnly<ContainedObjectsBuffer>(),
                    ComponentType.Exclude<EntityDestroyedCD>(),
                });
                using (var states = q.ToComponentDataArray<PickUpItemCD>(Allocator.Temp))
                using (var trans = q.ToComponentDataArray<LocalTransform>(Allocator.Temp))
                using (var ents = q.ToEntityArray(Allocator.Temp))
                {
                    var pp = player.WorldPosition;
                    for (int i = 0; i < ents.Length; i++)
                    {
                        var st = states[i].state;
                        if (st == PickUpItemState.IsBeingPickedUp || st == PickUpItemState.HasBeenPickedUp)
                            continue; // deja en cours de ramassage : non bloque
                        var p = trans[i].Position;
                        float dx = p.x - pp.x, dz = p.z - pp.z;
                        if (dx * dx + dz * dz > pd2) continue; // hors du rayon de ramassage
                        var buf = EntityUtility.GetBuffer<ContainedObjectsBuffer>(ents[i], player.world);
                        if (buf.Length == 0) continue;
                        var od = buf[0].objectData;
                        if (od.objectID == ObjectID.None || HasRoom(od)) continue;
                        blocked = od.objectID;
                        break;
                    }
                }
            }
            catch (System.Exception ex) { Diag.Error("A11yPickupDiag", ex); return; }

            // Annonce une fois par objet bloque ; re-armee quand plus rien ne bloque a cote
            // (blocked == None) ou quand l'objet bloque change de type.
            if (blocked != ObjectID.None && blocked != _lastBlocked)
                TtsText.Say(InGameTtsCore.ResolveObjectName(blocked) + ", " + Strings.L("pickup.full"), false);
            _lastBlocked = blocked;
        }

        // Y a-t-il de la place pour cet objet ? Un slot vide dans le sac principal, ou - si
        // empilable - on en possede deja (on peut empiler dessus, le plafond 9999 n'est jamais
        // atteint en pratique). Les slots VIDES de bourse ne comptent pas (filtres par categorie,
        // on ne sait pas s'ils accepteraient l'objet). Reutilise les totaux deja calcules.
        private static bool HasRoom(ObjectDataCD od)
        {
            if (_bagHasEmpty) return true;
            return IsStackable(od.objectID, od.variation)
                && _cur.TryGetValue(od.objectID, out int c) && c > 0;
        }
    }
}
