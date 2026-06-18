using System.Collections.Generic;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Annonce le nom des objets RAMASSES, a chaque ramassage (le jeu ne nomme l'objet
    // qu'a la PREMIERE decouverte d'un type via ChatWindow.NewItem, puis c'est un compteur
    // visuel muet - confirme dans PlayerController.DetectUndiscoveredObjectsInInventory).
    //
    // Le ramassage au sol est un systeme ECS server-authoritative (PickUpItemSystem) non
    // hookable proprement en hot-compile. On copie donc le pattern du jeu lui-meme : un DIFF
    // cote client de l'inventaire du joueur. Chaque tick on compare un instantane par slot
    // (objectID + quantite) au contenu courant ; toute HAUSSE = un gain a annoncer.
    //
    // Garde-fou anti-faux-positif : on n'annonce QUE quand aucun inventaire n'est ouvert
    // (comme le jeu). Inventaire ferme, les seules hausses de quantite viennent de vrais
    // ramassages (drops au sol, recolte, butin, auto-ramassage des bourses) : deplacer,
    // crafter ou prendre dans un coffre se fait inventaire OUVERT -> exclu d'office. On
    // continue toutefois a tenir l'instantane a jour inventaire ouvert, pour ne pas dumper
    // les manips manuelles comme des ramassages a la fermeture.
    //
    // Anti-spam : les gains sont accumules sur une courte fenetre (debounce a la traine) puis
    // annonces en une fois ("Terre fois 4, Cuivre fois 2"), en file d'attente NVDA
    // (interrupt:false) comme les autres notifs passives.
    internal static class PickupAnnouncer
    {
        private struct Slot { public ObjectID Id; public int Amount; }

        // Instantane du contenu, une entree par handler suivi (sac + bourses). Le handler
        // (instance de classe) sert de cle stable entre les frames.
        private static readonly Dictionary<InventoryHandler, Slot[]> _snap =
            new Dictionary<InventoryHandler, Slot[]>();

        // Gains en attente d'annonce (ordre d'apparition preserve pour une lecture naturelle).
        private static readonly List<ObjectID> _order = new List<ObjectID>();
        private static readonly Dictionary<ObjectID, int> _counts = new Dictionary<ObjectID, int>();
        private static float _flushAt;       // debounce : annonce quand plus rien depuis Window
        private static float _firstPendingAt; // garde-fou : flush force passe MaxAge

        private const float Window = 0.5f;   // silence apres le dernier ramassage avant d'annoncer
        private const float MaxAge = 2f;     // ramassage continu : on ne retient pas indefiniment

        // Reutilisable entre handlers : evite d'allouer un tableau temporaire par tick.
        private static readonly List<InventoryHandler> _seen = new List<InventoryHandler>();

        public static void Tick()
        {
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null)
            {
                // Retour menu / changement de monde : on rebaseline a la prochaine entree.
                if (_snap.Count > 0) _snap.Clear();
                ClearPending();
                return;
            }

            bool announce = A11ySettings.PickupAnnounce
                && Manager.ui != null && !Manager.ui.isAnyInventoryShowing
                && Manager.sceneHandler != null && Manager.sceneHandler.isSceneHandlerReady
                && !Manager.sceneHandler.cutsceneIsPlaying;

            _seen.Clear();
            ScanHandler(player.playerInventoryHandler, announce);
            var eh = player.equipmentHandler;
            if (eh != null && eh.pouchInventorySlotsHandlers != null)
                foreach (var h in eh.pouchInventorySlotsHandlers)
                    ScanHandler(h, announce);

            // Purge des handlers disparus (bourse retiree) pour ne pas fuir.
            if (_snap.Count > _seen.Count) PruneStale();

            CheckFull(player.playerInventoryHandler, announce);
            FlushIfDue();
        }

        // Alerte "inventaire plein" : aucune notif native n'existe (verifie dans
        // ChatWindow.MessageTextType), donc on la deduit du scan. "Plein" = plus aucun
        // emplacement LIBRE dans le sac/barre (un objet d'un type NOUVEAU ne rentrerait plus,
        // meme si une pile existante pourrait encore grossir). Annoncee une seule fois au
        // front montant ; re-armee des qu'une case se libere.
        private static bool _wasFull;

        private static void CheckFull(InventoryHandler bag, bool announce)
        {
            if (bag == null || !_snap.TryGetValue(bag, out var snap)) return;
            bool full = true;
            for (int i = 0; i < snap.Length; i++)
                if (snap[i].Id == ObjectID.None) { full = false; break; }

            // On met l'etat a jour meme inventaire ouvert (remplir soi-meme ne doit pas
            // declencher une alerte differee a la fermeture) ; on ne PARLE qu'au front
            // montant et seulement quand l'annonce est active.
            if (full && !_wasFull && announce) TtsText.Say(Strings.L("pickup.full"), false);
            _wasFull = full;
        }

        // Diff d'un handler : detecte les hausses de quantite slot par slot. Met TOUJOURS
        // l'instantane a jour ; n'empile un gain que si announce == true.
        private static void ScanHandler(InventoryHandler handler, bool announce)
        {
            if (handler == null) return;
            _seen.Add(handler);

            int size;
            try { size = handler.size; } catch { return; }
            if (size <= 0) return;

            Slot[] snap;
            bool fresh = false;
            if (!_snap.TryGetValue(handler, out snap) || snap.Length != size)
            {
                snap = new Slot[size];
                _snap[handler] = snap;
                fresh = true; // premier scan de ce handler : on baseline sans annoncer
            }

            for (int i = 0; i < size; i++)
            {
                ObjectDataCD od;
                try { od = handler.GetContainedObjectData(i).objectData; }
                catch { continue; }

                var prev = snap[i];
                snap[i].Id = od.objectID;
                snap[i].Amount = od.amount;

                if (fresh || !announce || od.objectID == ObjectID.None) continue;

                ObjectInfo info = TryInfo(od.objectID, od.variation);
                bool stackable = info == null || info.isStackable;

                int gained = 0;
                if (od.objectID != prev.Id)
                    gained = stackable ? od.amount : 1; // nouvel objet dans le slot
                else if (od.amount > prev.Amount && stackable)
                    gained = od.amount - prev.Amount; // meme pile empilable qui grossit
                // (meme objet non empilable, amount qui change = durabilite -> ignore)

                if (gained <= 0) continue;
                if (A11ySettings.PickupFilterBlocks && IsBlock(info)) continue; // blocs filtres au gre du joueur
                AddPending(od.objectID, gained);
            }
        }

        private static ObjectInfo TryInfo(ObjectID id, int variation)
        {
            try { return PugDatabase.GetObjectInfo(id, variation); }
            catch { return null; }
        }

        // Un "bloc" = objet portant le tag ObjectCategoryTag.TileBlock (terre, pierre, murs
        // posables...) : c'est la categorie a part du jeu pour les tuiles posables, la source
        // principale du bruit en minant.
        private static bool IsBlock(ObjectInfo info)
        {
            return info != null && info.tags != null && info.tags.Contains(ObjectCategoryTag.TileBlock);
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

            var parts = new List<string>(_order.Count);
            foreach (var id in _order)
            {
                string name = InGameTtsCore.ResolveObjectName(id);
                if (string.IsNullOrEmpty(name)) continue;
                int n = _counts[id];
                parts.Add(n > 1 ? name + " " + Strings.L("pickup.times") + " " + n : name);
            }
            ClearPending();
            if (parts.Count > 0) TtsText.Say(string.Join(", ", parts), false);
        }

        private static void ClearPending()
        {
            _order.Clear();
            _counts.Clear();
        }

        private static void PruneStale()
        {
            var dead = new List<InventoryHandler>();
            foreach (var kv in _snap)
                if (!_seen.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (var h in dead) _snap.Remove(h);
        }
    }
}
