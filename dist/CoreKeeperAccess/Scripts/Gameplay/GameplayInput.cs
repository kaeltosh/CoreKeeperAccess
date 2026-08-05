using System.Collections.Generic;
using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Navigation;
using CoreKeeperAccess.Patches;
using Interaction;
using PugMod;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Pouls gameplay de la touche access : appele chaque frame (apres InfoKey) il publie le
    // centre de l'index d'objets, tisse le reseau de navigation, egrene les .Tick() de tous
    // les sous-modules gameplay (sonar, pas, sonar de proximite, placement, roue de stats,
    // ping) et porte ses deux lectures par-frame maison : la PROSPECTION minerai (combo
    // Triangle + gauche, reponse differee d'une frame par OreScan) et la surveillance de
    // l'INTERACTIBLE a portee (annonce au changement).
    // Les combos d'action sur une UI ouverte (transfert, reparation, recyclage, forge,
    // vente) sont dans StationCommands ; le ping sonar, la locomotion et la lecture de pose
    // dans leurs fichiers dedies (decoupage du 21 juin). Les combos sont routes par
    // ComboDispatcher (cf. ComboBindings) ; ici ne restent que les ticks.
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

        public static void Tick()
        {
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null) { ProximitySonar.Stop(); CollisionRadar.Stop(); _prospectPending = false; return; }

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

            // Les combos (prospection, double-tap carte) sont routes par ComboDispatcher
            // (cf. ComboBindings). Ici ne restent que les ticks.
            ProximityScanner.Tick(player);
            // Apres le scanner : il publie VisibilityScan.CamHalf (fenetre camera), dont le
            // ping joueur se sert pour normaliser sa cadence au bord de l'ecran.
            PlayerPing.Tick(player);
            StepEngine.Tick(player);
            ProximitySonar.Tick(player);
            CollisionRadar.Tick(player);
            PlacementReader.Tick(player);
            StatsWheel.Tick(player);
            HotbarJumpWheel.Tick(player);

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
            WatchCursorToggle();
            WatchLeash(player);
            WatchCattleUi(player);
        }

        // Annonce d'OUVERTURE de la fenetre bétail (CattleUI, Cattle.Interact) : nom, faim,
        // etat de reproduction si dispo - parite avec ce qu'un joueur voyant lit d'un coup
        // d'oeil sur la fenetre. Puis SUIT le texte de faim tant que la fenetre reste ouverte
        // (change progressivement, sans reouverture). Navigation/interaction (slots de
        // nourriture, toggle reproduction) : géré par SlotSections/InventoryNavigator, pas ici.
        private const float CattleUiPollInterval = 0.2f;
        private static Cattle _lastActiveCattle;
        private static string _lastCattleStatus;
        private static CattleUI _cattleUiRef;
        private static float _nextCattleUiPoll;

        private static void WatchCattleUi(PlayerController player)
        {
            if (Time.unscaledTime < _nextCattleUiPoll) return;
            _nextCattleUiPoll = Time.unscaledTime + CattleUiPollInterval;

            Cattle active = player.activeCattle;
            string status = null;
            if (active != null)
            {
                if (_cattleUiRef == null) _cattleUiRef = Object.FindObjectOfType<CattleUI>();
                status = _cattleUiRef != null ? TtsText.ResolvePugText(_cattleUiRef.statusText) : null;
            }

            if (active != _lastActiveCattle)
            {
                _lastActiveCattle = active;
                _lastCattleStatus = status;
                if (active == null) return; // fermeture : silence, comme les autres fenetres du mod

                string text = InGameTtsCore.BuildCattleInfoLabel();
                if (!string.IsNullOrEmpty(text)) TtsText.Say(text, true);
                return;
            }

            if (active == null) return;
            if (!string.IsNullOrEmpty(status) && status != _lastCattleStatus)
            {
                _lastCattleStatus = status;
                TtsText.Say(status, false);
            }
        }

        // Annonce ACCROCHAGE/CASSE de la laisse (betail) : le joueur porte lui-meme
        // LeashingCD.leashedEntity, l'entite qu'il tracte actuellement (Entity.Null si rien).
        // Null -> une entite = laisse accrochee (annonce le nom de la bete) ; entite -> Null =
        // laisse cassee, que ce soit un relachement volontaire (on rejette l'objet Leash) ou
        // une casse automatique (bete trop loin, joueur monte en vehicule/minecart/bateau,
        // mort) - meme libelle dans les deux cas, la nuance ne vaut pas la peine a l'oreille.
        private const float LeashPollInterval = 0.2f;
        private static Entity _lastLeashed = Entity.Null;
        private static float _nextLeashPoll;

        private static void WatchLeash(PlayerController player)
        {
            if (Time.unscaledTime < _nextLeashPoll) return;
            _nextLeashPoll = Time.unscaledTime + LeashPollInterval;

            Entity leashed = Entity.Null;
            try
            {
                if (EntityUtility.HasComponentData<LeashingCD>(player.entity, player.world))
                    leashed = EntityUtility.GetComponentData<LeashingCD>(player.entity, player.world).leashedEntity;
            }
            catch (System.Exception ex) { Diag.Error("A11yLeashDiag", ex); return; }

            if (leashed == _lastLeashed) return;
            bool wasLeashed = _lastLeashed != Entity.Null;
            _lastLeashed = leashed;

            if (leashed != Entity.Null)
            {
                string name = null;
                try
                {
                    if (EntityUtility.HasComponentData<ObjectDataCD>(leashed, player.world))
                        name = InGameTtsCore.ResolveObjectName(
                            EntityUtility.GetComponentData<ObjectDataCD>(leashed, player.world).objectID);
                }
                catch (System.Exception ex) { Diag.Error("A11yLeashDiag", ex); }
                if (string.IsNullOrEmpty(name)) return;
                TtsText.Say(string.Format(Strings.L("leash.attached"), name), true);
            }
            else if (wasLeashed)
            {
                TtsText.Say(Strings.L("leash.broken"), true);
            }
        }

        // Annonce d'INTERACTION A PORTEE : le jeu maintient sur le joueur l'interactible
        // le plus proche actuellement atteignable (InteractorCD.currentClosestInteractable,
        // la donnee qui pilote le prompt visuel des voyants). On annonce au CHANGEMENT
        // ("Statue du boss slime, interaction disponible") -> on sait toujours si A va
        // faire quelque chose et sur quoi. Regle le "il faut etre au bon endroit" des
        // objets multi-cases (statues, Core...). Sortie de portee : silence.
        // Meme entite reconnue -> on surveille EN PLUS son etat a bascule (porte, portail,
        // levier) : un appui Croix qui l'active/desactive change l'etat SANS changer
        // l'identite de l'interactible -> sans ce 2e check, le nouvel etat ne serait jamais
        // annonce. Fonctionne curseur detache OU colle (currentClosestInteractable est la
        // donnee native de portee du joueur, independante du curseur).
        private const float InteractPollInterval = 0.2f;
        private static long _lastInteractable;
        private static ToggleState _lastInteractToggle = ToggleState.None;
        private static float _nextInteractPoll;

        private static void WatchInteractable(PlayerController player)
        {
            if (!InputContext.InGameFree) return;
            if (A11ySettings.MuteInteractInCursor && BuildModeNavigator.CursorDetached) return;
            if (Time.unscaledTime < _nextInteractPoll) return;
            _nextInteractPoll = Time.unscaledTime + InteractPollInterval;

            long key = 0;
            ObjectID id = ObjectID.None;
            ToggleState toggle = ToggleState.None;
            try
            {
                if (!EntityUtility.HasComponentData<InteractorCD>(player.entity, player.world)) return;
                var e = EntityUtility.GetComponentData<InteractorCD>(player.entity, player.world)
                    .currentClosestInteractable;
                if (e != Entity.Null && EntityUtility.HasComponentData<ObjectDataCD>(e, player.world))
                {
                    var od = EntityUtility.GetComponentData<ObjectDataCD>(e, player.world);
                    id = od.objectID;
                    key = EntityKey.Of(e);
                    toggle = ToggleLogic.Compute(e, player.world, od.objectID, od.variation);
                }
            }
            catch (System.Exception ex) { Diag.Error("A11yInteractDiag", ex); return; }

            if (key != _lastInteractable)
            {
                _lastInteractable = key;
                _lastInteractToggle = toggle;
                if (key == 0 || id == ObjectID.None) return;

                string name = InGameTtsCore.ResolveObjectName(id);
                if (string.IsNullOrEmpty(name)) return;
                // Etat a bascule (porte/portail ouvert-ferme, levier actif-inactif) dans la
                // MEME annonce : en approchant, on veut savoir tout de suite si ca vaut le
                // coup d'interagir ("Porte, ouverte, interaction disponible"), pas juste que
                // l'action existe.
                string stateLabel = BuildModeNavigator.ToggleLabel(id, toggle);
                string text = string.IsNullOrEmpty(stateLabel) ? name : name + ", " + stateLabel;
                // interrupt=true (demande utilisateur) : info de POSITION, perimee si elle
                // attend son tour dans la file - on marche, le point chaud c'est MAINTENANT.
                TtsText.Say(text + ", " + Strings.L("interact.available"), true);
                return;
            }

            // Meme interactible : verifier un changement d'etat a bascule (typiquement juste
            // apres un appui Croix qui vient de l'activer/desactiver - l'etat met un instant
            // a revenir du serveur, replique via SwapColliderCD/variation).
            if (key == 0 || toggle == ToggleState.None || toggle == _lastInteractToggle) return;
            _lastInteractToggle = toggle;
            // Curseur DETACHE : on peut viser une case differente de l'interactible natif le
            // plus proche du joueur (deux objets cote a cote) -> WatchCursorToggle, qui suit
            // la case REELLEMENT visee, s'en charge. Evite aussi le doublon quand les deux
            // ciblent le meme objet.
            if (BuildModeNavigator.CursorDetached) return;

            string label = BuildModeNavigator.ToggleLabel(id, toggle);
            if (string.IsNullOrEmpty(label)) return;
            string objName = InGameTtsCore.ResolveObjectName(id);
            TtsText.Say(string.IsNullOrEmpty(objName) ? label : objName + ", " + label, true);
        }

        // Surveillance de la MEME case a bascule (porte/portail/levier) pointee par le
        // curseur DETACHE : annonce le changement d'etat des qu'il arrive. Complementaire a
        // WatchInteractable (qui couvre le curseur COLLE / l'interaction native) : le curseur
        // detache peut viser une case differente de "l'interactible le plus proche du joueur"
        // que le jeu retient nativement (currentClosestInteractable), donc suit sa PROPRE
        // cible (case + objet) plutot que de deriver de cette donnee native.
        private static int2 _toggleTile = new int2(int.MinValue, int.MinValue);
        private static ObjectID _toggleId = ObjectID.None;
        private static ToggleState _toggleState = ToggleState.None;

        private static void WatchCursorToggle()
        {
            if (!InputContext.InGameFree) return;
            if (!BuildModeNavigator.CursorDetached || !TileQuery.ResultValid
                || TileQuery.Toggle == ToggleState.None || TileQuery.ObjectId == ObjectID.None)
            {
                _toggleId = ObjectID.None;
                return;
            }

            bool sameTarget = TileQuery.ObjectId == _toggleId && TileQuery.ResultTile.Equals(_toggleTile);
            if (!sameTarget)
            {
                // Nouvelle case/objet suivi : on memorise l'etat SANS annoncer (Announce(),
                // au survol normal, l'a deja dit).
                _toggleTile = TileQuery.ResultTile;
                _toggleId = TileQuery.ObjectId;
                _toggleState = TileQuery.Toggle;
                return;
            }
            if (TileQuery.Toggle == _toggleState) return;
            _toggleState = TileQuery.Toggle;

            string label = BuildModeNavigator.ToggleLabel(TileQuery.ObjectId, TileQuery.Toggle);
            if (string.IsNullOrEmpty(label)) return;
            string name = InGameTtsCore.ResolveObjectName(TileQuery.ObjectId);
            TtsText.Say(string.IsNullOrEmpty(name) ? label : name + ", " + label, true);
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

        // Consomme la reponse du systeme (frame suivante) : ding positionnel sur le/les
        // resultat(s) (son natif oreHit, pan/distance par le jeu + pitch vertical maison,
        // meme langage que le curseur) + TTS cardinal et distance. Veine ET gisement sont
        // des resultats INDEPENDANTS (OreScan.Found / OreScan.DepositFound) : si les deux
        // sont trouves, un SEUL appel TtsText.Say regroupe les deux phrases -- deux appels
        // successifs en interrupt:true se couperaient l'un l'autre (le second ecraserait
        // le premier avant meme d'etre entendu).
        private static void TickProspect(PlayerController player)
        {
            if (!_prospectPending || !OreScan.ResultValid) return;
            _prospectPending = false;

            if (!OreScan.Found && OreScan.DepositCount == 0)
            {
                TtsText.Say(Strings.L("prospect.none") + ", "
                    + Strings.L("prospect.radius") + " " + _prospectRadius, true);
                return;
            }

            float2 p = new float2(player.WorldPosition.x, player.WorldPosition.z);
            string text = null;

            if (OreScan.Found)
            {
                float2 d = new float2(OreScan.Tile.x, OreScan.Tile.y) - p;
                float pitch = Mathf.Clamp(Mathf.Pow(2f, d.y / 12f), 0.5f, 2f);
                GameplayAudio.PlayTableSpatialNoPitchDev(SfxTableID.oreHit,
                    new Vector3(OreScan.Tile.x, 0f, OreScan.Tile.y), ProspectDingVolume, pitch);
                int dist = Mathf.RoundToInt(math.length(d));
                text = Strings.L("prospect.ore") + ", " + (dist < 1
                    ? Strings.L("prospect.here")
                    : Cardinal(d) + ", " + dist + " " + Strings.L("teleport.tiles"));
            }

            // TOUS les gisements a portee (plafonnes a OreScan.MaxDeposits), du plus proche
            // au plus lointain : ils sont peu nombreux et durables, contrairement aux veines
            // dont chaque case compterait. Demande testeur du 25 juillet 2026 - "il y a un
            // autre gisement juste a cote, et ca ne me dit que le plus proche".
            for (int i = 0; i < OreScan.DepositCount; i++)
            {
                int2 tile = OreScan.DepositTiles[i];
                float2 d = new float2(tile.x, tile.y) - p;
                float pitch = Mathf.Clamp(Mathf.Pow(2f, d.y / 12f), 0.5f, 2f);
                GameplayAudio.PlayTableSpatialNoPitchDev(SfxTableID.oreHit,
                    new Vector3(tile.x, 0f, tile.y), ProspectDingVolume, pitch);
                int dist = Mathf.RoundToInt(math.length(d));
                string depositText = Strings.L("prospect.deposit") + ", " + (dist < 1
                    ? Strings.L("prospect.here")
                    : Cardinal(d) + ", " + dist + " " + Strings.L("teleport.tiles"));
                text = text == null ? depositText : text + ". " + depositText;
            }

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
    }
}
