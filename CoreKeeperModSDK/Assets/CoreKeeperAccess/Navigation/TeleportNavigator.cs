using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Gameplay;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using Rewired;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoreKeeperAccess.Navigation
{
    // Navigateur de destinations de teleportation (anciens relais). Interagir avec un relais
    // pose player.currentPortal et ouvre la carte = l'interface de voyage rapide. On la rend
    // accessible : liste des relais/waypoints teleportables (D-pad haut/bas), annonce nom +
    // direction + distance, A native = clic sur le marqueur = teleportation (le jeu ferme la
    // carte). Bumpers = categories (destinations / points d'interet).
    internal static class TeleportNavigator
    {
        private const int DpadUp = 16, DpadDown = 18, DpadRight = 17, DpadLeft = 19, CrossButton = 6, Lb = 10, Rb = 11;

        private static bool _active;
        private static readonly List<MapMarkerUIElement> _dests = new List<MapMarkerUIElement>();      // navigables (CanTeleport)
        private static readonly List<MapMarkerUIElement> _allPortals = new List<MapMarkerUIElement>();  // tous, pour la numerotation stable
        // Points d'INTERET : marqueurs de la carte (boss scannes, scenes uniques, tombe,
        // pings...) - pas teleportables, mais nommables et localisables (cap + distance).
        private static readonly List<MapMarkerUIElement> _pois = new List<MapMarkerUIElement>();
        // MES BALISES : les marqueurs poses par le joueur (UserPlacedMarker). Onglet a part :
        // on les pose (a sa position), supprime et renomme. PREMIER JALON du reseau de
        // navigation par points (cf. fiche beacon-navigation) : un noeud = une POSITION,
        // d'ou la persistance des noms par case dans BeaconStore.
        private static readonly List<MapMarkerUIElement> _beacons = new List<MapMarkerUIElement>();
        // JOURNAL : dialogues archives (Coeur + tutoriels), navigues comme un MENU CONTEXTUEL en
        // cascade : niveau 0 = liste des conversations (titres), on en OUVRE une (droite/Croix)
        // pour passer au niveau 1 = ses repliques, et on REVIENT au niveau 0 (gauche). Un seul
        // niveau s'entend a la fois. C'est du TEXTE, pas des marqueurs -> chemin a part.
        private static readonly List<DialogueGroup> _groups = new List<DialogueGroup>();
        private static int _jLevel;  // 0 = liste des conversations, 1 = repliques d'une conversation ouverte
        private static int _jGroup;  // conversation courante (niveau 0)
        private static int _jItem;   // replique courante (niveau 1)
        private static int _category; // 0 = destinations, 1 = points d'interet, 2 = mes balises, 3 = journal
        private const int CategoryCount = 4;
        private static bool _coreReconstructAttempted; // reconstruction du dialogue d'activation deja reussie
        private static MapMarkerUIElement _coreMarker;                                                  // le plus proche de l'origine
        private static int _index;

        // Rafraichissement differe apres une pose : le marqueur n'existe en UIElement
        // qu'au prochain passage de MapUI (timer ~1 s) -> on re-scanne et on selectionne
        // la balise fraiche un court instant plus tard.
        private static bool _hasPendingSelect;
        private static int2 _pendingSelectTile;
        private static float _pendingSelectTime;

        private static List<MapMarkerUIElement> CurrentList =>
            _category == 0 ? _dests : (_category == 1 ? _pois : _beacons);

        // ===== Lignes VIRTUELLES de l'onglet "Mes balises" =====
        // En tete de l'onglet balises, des entrees qui ne sont PAS des marqueurs :
        //   0 = "Nouvelle balise ici" (toujours presente, meme liste vide) -> Croix cree ;
        //   1 = "Rejoindre le reseau le plus proche" (seulement si le graphe a des noeuds)
        //       -> Croix lance un guidage DIRECT vers la torche la plus proche.
        // Les balises reelles suivent, decalees de ce nombre.
        // Lignes virtuelles de l'onglet "Mes balises". En TETE : 0 = "Nouvelle balise ici"
        // (toujours), 1 = "Rejoindre le reseau le plus proche" (si le graphe a des noeuds).
        // En QUEUE : "Recalculer le reseau" (si le graphe a des noeuds) = action de maintenance,
        // donc reletguee en fin de liste.
        private const int VrowNew = 0, VrowJoin = 1, VrowRecalc = 2;

        private static bool HasNetwork => BeaconGraph.NodeCount > 0;
        private static int BeaconHeadCount() => 1 + (HasNetwork ? 1 : 0); // tete (creer / rejoindre)
        private static int BeaconTailCount() => HasNetwork ? 1 : 0;        // queue (recalculer)

        // Nombre de lignes navigables de la categorie courante (lignes virtuelles comprises
        // pour l'onglet balises).
        private static int RowCount()
            => _category == 2 ? BeaconHeadCount() + _beacons.Count + BeaconTailCount() : CurrentList.Count;

        // Vrai si l'index (onglet balises) tombe sur une ligne virtuelle ; sort son kind.
        private static bool IsVirtualRow(int idx, out int kind)
        {
            kind = -1;
            if (_category != 2) return false;
            int head = BeaconHeadCount();
            if (idx >= 0 && idx < head) { kind = idx; return true; }                          // tete
            if (BeaconTailCount() > 0 && idx == head + _beacons.Count) { kind = VrowRecalc; return true; } // queue
            return false;
        }

        // Marqueur correspondant a l'index courant (onglet balises : decale des lignes
        // virtuelles de tete). null si l'index ne pointe pas un marqueur.
        private static MapMarkerUIElement MarkerAt(int idx)
        {
            if (_category != 2)
            {
                var l = CurrentList;
                return (idx >= 0 && idx < l.Count) ? l[idx] : null;
            }
            int mi = idx - BeaconHeadCount();
            return (mi >= 0 && mi < _beacons.Count) ? _beacons[mi] : null;
        }

        // CanTeleport est prive -> reflection. (mapMarkerEntity, lui, est un champ PUBLIC :
        // acces direct, pas de reflection.)
        private static MethodInfo _canTeleport;
        private static bool _reflResolved;

        public static void Update()
        {
            // Carte ouverte = nav active, AVEC ou SANS relais (le double-tap Triangle
            // ouvre la carte hors relais : la categorie destinations est alors vide,
            // mais les points d'interet restent consultables).
            bool open = InputContext.MapOpen;

            if (open && !_active) Enter();
            else if (!open && _active) { _active = false; _dests.Clear(); _pois.Clear(); _beacons.Clear(); _groups.Clear(); _hasPendingSelect = false; ActionMenu.Close(); }
            if (!_active) return;

            // Selection differee d'une balise fraichement posee (le temps que MapUI cree
            // son UIElement). Silencieux : la pose a deja ete annoncee.
            if (_hasPendingSelect && Time.time >= _pendingSelectTime)
            {
                _hasPendingSelect = false;
                BuildDestinations();
                if (_category == 2)
                {
                    int idx = _beacons.FindIndex(b => MarkerTile(b, out int2 t) && t.Equals(_pendingSelectTile));
                    if (idx >= 0) { _index = BeaconHeadCount() + idx; Select(_index, interruptTts: false); }
                }
            }

            HandleInput();
        }

        private static void Enter()
        {
            _active = true;
            ResolveReflection();
            BuildDestinations();
            _index = 0;
            // Categorie de depart : destinations si on vient d'un relais (teleport possible),
            // sinon points d'interet (consultation pure).
            var player = Manager.main != null ? Manager.main.player : null;
            _category = (player != null && player.currentPortal != null && _dests.Count > 0) ? 0 : 1;
            AnnounceCategory();
        }

        // Annonce le nom de la categorie courante puis son premier element (ou son vide).
        private static void AnnounceCategory()
        {
            // Journal : menu contextuel en cascade. On entre au niveau 0 (liste des conversations).
            if (_category == 3)
            {
                string j = Strings.L("map.journal");
                _jLevel = 0; _jGroup = 0; _jItem = 0;
                if (_groups.Count == 0) { TtsText.Say(j + ", " + Strings.L("map.journal.none"), true); return; }
                TtsText.Say(j, true);
                AnnounceGroup(interrupt: false);
                return;
            }
            string cat = Strings.L(_category == 0 ? "map.destinations"
                : _category == 1 ? "map.poi" : "map.beacons");
            // L'onglet balises a toujours au moins la ligne "Nouvelle balise ici" -> RowCount.
            if (RowCount() == 0)
            {
                string none = Strings.L(_category == 0 ? "teleport.none" : "map.poi.none");
                TtsText.Say(cat + ", " + none, true);
                return;
            }
            TtsText.Say(cat, true);
            _index = 0;
            Select(0, interruptTts: false);
        }

        private static void ResolveReflection()
        {
            if (_reflResolved) return;
            _reflResolved = true;
            _canTeleport = typeof(MapMarkerUIElement)
                .GetMethod("CanTeleport", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        // Tous les marqueurs OU on peut se teleporter (CanTeleport gere relais actif, marqueur
        // active, et exclut celui ou on se tient), tries du plus proche au plus lointain.
        // _allPortals = TOUS les portails/waypoints, tries par DISTANCE AU CENTRE (0,0) ->
        // numerotation STABLE et intuitive : le Core (centre du monde) = relais 1, puis de plus
        // en plus loin. Le relais courant y compte meme s'il n'est pas une destination, donc les
        // numeros ne decalent pas selon ou on se tient. _dests = ceux ou l'on peut aller
        // (CanTeleport, qui exclut le relais courant). _coreMarker = le plus proche de l'origine
        // -> annonce "Relais du Core".
        private static void BuildDestinations()
        {
            _dests.Clear();
            _allPortals.Clear();
            _pois.Clear();
            _beacons.Clear();
            _coreMarker = null;

            // FindObjectsInactive.Include : le jeu DESACTIVE (Container.SetActive(false)) les
            // marqueurs hors du cadre de la minimap (Portal/Waypoint/UserPlacedMarker/PlayerGrave)
            // au lieu de les detruire (cf. MapUI.MoveMarkersWithinBounds). Sans Include on les
            // ratait -> "il faut se rapprocher" pour capter la tombe et les relais lointains.
            // On capte tout, on filtre ensuite.
            var all = Object.FindObjectsByType<MapMarkerUIElement>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var seen = new HashSet<Entity>();
            foreach (var m in all)
            {
                if (m == null) continue;
                // Filtre "entite vivante" (remplace l'ancien !isActiveAndEnabled, qui jetait
                // justement les marqueurs caches qu'on veut) : on ne garde que ceux dont l'entite
                // existe encore et porte une position. Elimine les reliquats du pool de recyclage
                // de MapUI (Container desactive, mapMarkerEntity morte ou nulle) que Include capte
                // aussi. Ecarte au passage le marqueur du joueur (non-entite) -> plus de POI "You".
                if (m.mapMarkerEntity == Entity.Null) continue;
                if (!TryMarkerPos(m, out _)) continue;
                if (!seen.Add(m.mapMarkerEntity)) continue; // marqueur actif + reliquat de pool = meme entite
                if (m.markerType == MapMarkerType.UserPlacedMarker)
                {
                    // Balise posee par le joueur : onglet a part (pose / suppression / renommage).
                    _beacons.Add(m);
                    continue;
                }
                if (m.markerType != MapMarkerType.Portal && m.markerType != MapMarkerType.Waypoint)
                {
                    // Tout autre marqueur = point d'interet (boss scanne, tombe, ping...).
                    _pois.Add(m);
                    continue;
                }
                _allPortals.Add(m);
                bool ok;
                try { ok = _canTeleport != null && (bool)_canTeleport.Invoke(m, null); }
                catch { ok = false; }
                if (ok) _dests.Add(m);
            }

            _allPortals.Sort(CompareByCenterDistance);
            _dests.Sort(CompareByCenterDistance);
            // POI tries du plus proche AU JOUEUR (pas au centre : pas de numerotation
            // stable a garantir, c'est la pertinence immediate qui compte).
            float2 pp = PlayerPos();
            _pois.Sort((a, b) => DistSq(a, pp).CompareTo(DistSq(b, pp)));
            _beacons.Sort((a, b) => DistSq(a, pp).CompareTo(DistSq(b, pp)));

            float best = float.MaxValue;
            foreach (var m in _allPortals)
                if (TryMarkerPos(m, out float2 p) && math.lengthsq(p) < best)
                {
                    best = math.lengthsq(p);
                    _coreMarker = m;
                }
            if (best > 100f) _coreMarker = null; // aucun relais assez proche de l'origine

            // Reconstruction unique du dialogue d'activation du Core (one-shot deja joue) tant
            // qu'on ne l'a pas reussie : ne se fait que si une instance TheCore est chargee
            // (Core a portee) -> on retente aux ouvertures suivantes jusqu'a y arriver.
            if (!_coreReconstructAttempted && Patches.CoreDialogueArchive.ReconstructActivation())
                _coreReconstructAttempted = true;

            // Journal : recharge les conversations archivees du monde courant (groupes triees).
            _groups.Clear();
            _groups.AddRange(DialogueLog.BuildGroups());
        }

        // Tri par distance a l'origine (0,0) : le Core (centre du monde) = relais 1, puis de
        // plus en plus loin. Stable (la distance au centre ne depend pas du joueur) et intuitif.
        private static int CompareByCenterDistance(MapMarkerUIElement a, MapMarkerUIElement b)
        {
            TryMarkerPos(a, out float2 pa);
            TryMarkerPos(b, out float2 pb);
            return math.lengthsq(pa).CompareTo(math.lengthsq(pb));
        }

        // Numero stable d'un relais (sa place dans _allPortals trie par coords).
        private static int StableNumber(MapMarkerUIElement m) => _allPortals.IndexOf(m) + 1;

        private static void HandleInput()
        {
            // Saisie d'un nom OU menu contextuel ouvert : ils ont la main, on gele la nav.
            if (TextEntry.Active || ActionMenu.Active) return;
            var joy = ReInput.isReady ? ReInput.controllers.GetLastActiveController<Joystick>() : null;
            if (joy == null) return;
            // Touche access (Triangle) tenue : la croix directionnelle est reservee aux
            // commandes (routees par ComboDispatcher, cf. ComboBindings).
            if (InfoKey.ModifierHeld) return;
            for (int i = 0; i < joy.buttonCount; i++)
            {
                if (!joy.GetButtonDown(i)) continue;
                int id = joy.ButtonElementIdentifiers[i].id;
                if (id == DpadUp) { Move(-1); return; }
                if (id == DpadDown) { Move(+1); return; }
                // Journal (menu cascade) : droite ouvre la conversation, gauche revient. Capte
                // seulement sur cet onglet -> les autres categories gardent leur D-pad natif.
                if (_category == 3 && id == DpadRight) { JournalOpen(); return; }
                if (_category == 3 && id == DpadLeft) { JournalBack(); return; }
                if (id == CrossButton) { Confirm(); return; }
                if (id == Lb) { SwitchCategory(-1); return; }
                if (id == Rb) { SwitchCategory(+1); return; }
            }
        }

        // Bumpers = faire defiler les 3 categories (destinations / points d'interet /
        // mes balises). LB = precedente, RB = suivante. On re-scanne a chaque bascule :
        // les balises et POI ont pu changer depuis l'ouverture (pose, scan de boss...).
        private static void SwitchCategory(int dir)
        {
            BuildDestinations();
            _category = (_category + dir + CategoryCount) % CategoryCount;
            _index = 0;
            AnnounceCategory();
        }

        private static void Move(int step)
        {
            if (_category == 3) { JournalMove(step); return; }
            int n = RowCount();
            if (n == 0) return;
            _index = (_index + step + n) % n;
            Select(_index);
        }

        // ===== Journal : menu contextuel en cascade =====

        // D-pad haut/bas : parcourt le niveau courant (conversations au niveau 0, repliques de la
        // conversation ouverte au niveau 1).
        private static void JournalMove(int step)
        {
            if (_groups.Count == 0) return;
            if (_jLevel == 0)
            {
                _jGroup = (_jGroup + step + _groups.Count) % _groups.Count;
                AnnounceGroup(interrupt: true);
            }
            else
            {
                var g = _groups[_jGroup];
                if (g.lines.Count == 0) return;
                _jItem = (_jItem + step + g.lines.Count) % g.lines.Count;
                AnnounceItem(interrupt: true);
            }
        }

        // D-pad droite / Croix : OUVRE la conversation focalisee (niveau 0 -> 1). Au niveau 1,
        // Croix relit la replique courante.
        private static void JournalOpen()
        {
            if (_groups.Count == 0) return;
            if (_jLevel == 0)
            {
                if (_groups[_jGroup].lines.Count == 0) return;
                _jLevel = 1; _jItem = 0;
                AnnounceItem(interrupt: true);
            }
            else AnnounceItem(interrupt: true); // relire
        }

        // D-pad gauche : REVIENT a la liste des conversations (niveau 1 -> 0). Butee au niveau 0.
        private static void JournalBack()
        {
            if (_jLevel == 1) { _jLevel = 0; AnnounceGroup(interrupt: true); }
        }

        // Niveau 0 : titre de la conversation + nombre de repliques.
        private static void AnnounceGroup(bool interrupt)
        {
            if (_jGroup < 0 || _jGroup >= _groups.Count) return;
            var g = _groups[_jGroup];
            string pos = (_jGroup + 1) + " " + Strings.L("journal.of") + " " + _groups.Count;
            TtsText.Say(pos + ", " + g.label + ", " + g.lines.Count + " " + Strings.L("journal.replies"), interrupt);
        }

        // Niveau 1 : position "n sur m" dans la conversation + texte de la replique.
        private static void AnnounceItem(bool interrupt)
        {
            if (_jGroup < 0 || _jGroup >= _groups.Count) return;
            var g = _groups[_jGroup];
            if (_jItem < 0 || _jItem >= g.lines.Count) return;
            string pos = (_jItem + 1) + " " + Strings.L("journal.of") + " " + g.lines.Count;
            TtsText.Say(pos + ", " + g.lines[_jItem], interrupt);
        }

        // A = clic direct sur la destination selectionnee = teleportation. On appelle
        // OnLeftClicked (public) nous-memes : le routage natif de A ne le declenche pas sur la
        // carte. OnLeftClicked verifie CanTeleport, fait QueueInputAction(Teleport) et ferme la
        // carte. Un point d'interet n'est PAS teleportable : refus parlant.
        // Croix : selon l'onglet. Destinations = teleporter. Mes balises = poser une balise
        // a la position du joueur. Points d'interet = refus parlant.
        private static void Confirm()
        {
            if (_category == 3) { JournalOpen(); return; } // Croix = ouvrir la conversation / relire
            // Onglet balises : lignes virtuelles (creer / rejoindre le reseau) en tete,
            // sinon menu d'actions sur la balise.
            if (_category == 2)
            {
                if (IsVirtualRow(_index, out int kind))
                {
                    if (kind == VrowNew) PlaceBeaconAtPlayer();
                    else if (kind == VrowJoin) JoinNearestNetwork();
                    else RecalcNetwork();
                    return;
                }
                var bm = MarkerAt(_index);
                if (bm != null) OpenMarkerMenu(bm);
                return;
            }
            // Points d'interet : menu d'actions (guidage). Pas de teleportation.
            if (_category == 1)
            {
                if (_index >= 0 && _index < _pois.Count) OpenMarkerMenu(_pois[_index]);
                return;
            }
            // Destinations / relais : Croix = teleportation directe (inchange).
            if (_index < 0 || _index >= _dests.Count) return;
            _dests[_index].OnLeftClicked(false, false);
        }

        // ===== Menu contextuel d'un marqueur (POI / balise) =====
        // Croix sur un POI ou une balise ouvre une liste d'ACTIONS (guidage reseau / direct,
        // + renommer / supprimer pour une balise). Libere le moteur de keymaps : plus de
        // combo Triangle par action.
        private static void OpenMarkerMenu(MapMarkerUIElement m)
        {
            if (m == null || !MarkerTile(m, out int2 tile)) return;
            string name = _category == 2 ? BeaconName(m) : MarkerTitle(m);
            if (string.IsNullOrEmpty(name)) name = Strings.L("map.poi.marker");

            var items = new List<ActionMenu.Item>
            {
                new ActionMenu.Item { Label = Strings.L("ctx.navigate"), Run = () => StartGuide(tile, name, true) },
                new ActionMenu.Item { Label = Strings.L("ctx.direct"),   Run = () => StartGuide(tile, name, false) },
            };
            if (_category == 2)
            {
                var marker = m; // capture pour les closures (rename/delete operent sur CE marqueur)
                items.Add(new ActionMenu.Item { Label = Strings.L("ctx.rename"), Run = () => RenameBeacon(marker) });
                items.Add(new ActionMenu.Item { Label = Strings.L("ctx.delete"), Run = () => DeleteBeacon(marker) });
            }
            ActionMenu.Open(Strings.L("ctx.title") + ", " + name, items);
        }

        // Lance un guidage (reseau ou direct) vers la case, puis ferme la carte pour partir.
        private static void StartGuide(int2 tile, string name, bool routed)
        {
            if (routed) BeaconGuide.StartRouted(tile, name);
            else BeaconGuide.StartDirect(tile, name);
            CloseMap();
        }

        // "Rejoindre le reseau le plus proche" : guidage DIRECT vers la torche-noeud la plus
        // proche du joueur (pour rallier ses balises quand on est perdu hors reseau).
        private static void JoinNearestNetwork()
        {
            if (!BeaconGraph.NearestNode(PlayerPos(), float.MaxValue, out int2 node))
            {
                TtsText.Say(Strings.L("guide.nonetwork"), true);
                return;
            }
            BeaconGuide.StartDirect(node, Strings.L("guide.network"));
            CloseMap();
        }

        // "Recalculer le reseau" : pose une demande de recalcul local (tranche C) autour de la
        // position du joueur. Le systeme tisse les aretes manquantes par ligne de vue ; l'annonce
        // ("Reseau mis a jour, K liaisons") part de GameplayInput quand la reponse arrive. On
        // RESTE sur la carte (action de maintenance, pas de deplacement).
        private static void RecalcNetwork()
        {
            float2 pp = PlayerPos();
            NetworkRecalc.Center = new int2((int)math.round(pp.x), (int)math.round(pp.y));
            NetworkRecalc.Radius = 16f;
            NetworkRecalc.ResultValid = false;
            NetworkRecalc.Requested = true;
        }

        // Ferme la carte par l'API native directe (Manager.ui.HideMap(), celle qu'appelle le
        // bouton de fermeture du jeu) -> deterministe, contrairement a l'armement de TOGGLE_MAP
        // qui dependait du polling du jeu. L'earcon de guidage prend alors le relais en jeu
        // (il ne sonne qu'InGameFree, donc carte fermee).
        private static void CloseMap()
        {
            if (Manager.ui != null) Manager.ui.HideMap();
        }

        private static void Select(int idx, bool interruptTts = true)
        {
            // Ligne virtuelle (onglet balises) : pas de marqueur a focaliser, juste l'annonce.
            if (IsVirtualRow(idx, out int kind))
            {
                TtsText.Say(VirtualLabel(kind), interruptTts);
                return;
            }
            var m = MarkerAt(idx);
            if (m == null) return;
            // Selection forcee + recalage du curseur manette virtuel (meme piege UIMouse que
            // l'inventaire) pour que A native clique bien CE marqueur -> teleportation.
            InventoryNavState.SuppressPassiveAnnounce = true;
            try
            {
                Manager.ui.OnUIElementSelected(m);
                if (Manager.ui.mouse != null)
                    Manager.ui.mouse.PlaceMousePositionOnSelectedUIElementWhenControlledByJoystick();
            }
            finally { InventoryNavState.SuppressPassiveAnnounce = false; }
            Announce(m, interruptTts);
        }

        // Libelle TTS d'une ligne virtuelle de l'onglet balises.
        private static string VirtualLabel(int kind)
            => kind == VrowNew ? Strings.L("beacon.new")
             : kind == VrowJoin ? Strings.L("guide.joinnetwork")
             : Strings.L("netrecalc.menu");

        // Titre d'un marqueur, avec rattrapage des libelles que le JEU laisse en anglais
        // meme en francais (trad I2 manquante, un voyant FR voit le meme anglais) : on
        // substitue notre cle i18n. Comparaison insensible a la casse ; si une maj du jeu
        // traduit enfin le terme, le texte ne matche plus et le natif reprend la main.
        private static string MarkerTitle(MapMarkerUIElement m)
        {
            string t = TtsText.ResolveTextAndFormatFields(m.GetHoverTitle());
            if (string.Equals(t, "Larva Boss", System.StringComparison.OrdinalIgnoreCase))
                return Strings.L("poi.ghorm");
            return t;
        }

        private static void Announce(MapMarkerUIElement m, bool interrupt = true)
        {
            string name;
            if (_category == 2)
            {
                name = BeaconName(m);
            }
            else
            {
                name = (m == _coreMarker) ? Strings.L("teleport.core") : MarkerTitle(m);
                if (string.IsNullOrEmpty(name))
                    name = Strings.L(_category == 0 ? "teleport.portal" : "map.poi.marker");
            }
            // Numero en tete : STABLE pour les relais (tri par distance au centre),
            // simple ordre de proximite pour les points d'interet et les balises (l'onglet
            // balises retranche les lignes virtuelles de tete pour numeroter a partir de 1).
            int number = _category == 0 ? StableNumber(m)
                : _category == 2 ? (_index - BeaconHeadCount() + 1)
                : _index + 1;
            string head = number + ", " + name;
            string dd = DirectionAndDistance(m);
            TtsText.Say(string.IsNullOrEmpty(dd) ? head : head + ", " + dd, interrupt);
        }

        // Touche access (Triangle + haut) : details de la destination selectionnee -> type
        // (portail / point de passage) + coordonnees exactes + cap + distance.
        internal static void AnnounceDetail()
        {
            if (_category == 3) { if (_jLevel == 1) AnnounceItem(true); else AnnounceGroup(true); return; } // journal : relire le focus
            if (IsVirtualRow(_index, out int kind)) { TtsText.Say(VirtualLabel(kind), true); return; }
            var m = MarkerAt(_index);
            if (m == null) return;
            string label;
            if (_category == 0)
            {
                label = (m == _coreMarker) ? Strings.L("teleport.core")
                    : (m.markerType == MapMarkerType.Waypoint
                        ? Strings.L("teleport.waypoint") : Strings.L("teleport.portal"));
            }
            else if (_category == 2)
            {
                label = BeaconName(m);
            }
            else
            {
                label = MarkerTitle(m);
                if (string.IsNullOrEmpty(label)) label = Strings.L("map.poi.marker");
            }
            int number = _category == 0 ? StableNumber(m)
                : _category == 2 ? (_index - BeaconHeadCount() + 1)
                : _index + 1;
            string text = number + ", " + label;
            if (TryMarkerPos(m, out float2 mp))
            {
                text += ", " + Strings.L("teleport.position") + " "
                      + (int)math.round(mp.x) + ", " + (int)math.round(mp.y);
                string biome = ResolveBiome(mp);
                if (!string.IsNullOrEmpty(biome))
                    text += ", " + Strings.L("teleport.biome") + " " + biome;
            }
            string dd = DirectionAndDistance(m);
            if (!string.IsNullOrEmpty(dd)) text += ", " + dd;
            TtsText.Say(text, true);
        }

        // ===== Mes balises (marqueurs joueur) — premier jalon du reseau de navigation =====

        // Nom affiche d'une balise : le nom custom (BeaconStore, par position) ou un repli
        // pour une balise posee hors mod (couleur native, sans nom dans notre magasin).
        private static string BeaconName(MapMarkerUIElement m)
        {
            if (MarkerTile(m, out int2 tile))
            {
                string n = BeaconStore.GetName(tile);
                if (!string.IsNullOrEmpty(n)) return n;
            }
            return Strings.L("map.poi.marker");
        }

        // Case (int2) d'un marqueur = sa position monde (x,z) arrondie. Cle d'identite des
        // balises (un noeud = une POSITION), alignee sur la pose serveur (coords entieres).
        private static bool MarkerTile(MapMarkerUIElement m, out int2 tile)
        {
            if (TryMarkerPos(m, out float2 p))
            {
                tile = new int2((int)math.round(p.x), (int)math.round(p.y));
                return true;
            }
            tile = default;
            return false;
        }

        // Croix sur l'onglet "Mes balises" : pose une balise a la position du joueur (pas
        // besoin de viser sur la carte). On reproduit la pose native : CreateMapUI au serveur
        // (fait foi) + prespawn local pour le visuel immediat, couleur fixe Marker1. Le NOM
        // auto ("Balise <biome> <n>") est stocke par case ; la balise rejoint la liste au
        // prochain passage de MapUI -> selection differee.
        private static void PlaceBeaconAtPlayer()
        {
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null) return;
            if (player.guestMode) { TtsText.Say(Strings.L("beacon.guest"), true); return; }

            var wp = player.WorldPosition;
            int2 tile = new int2((int)math.round(wp.x), (int)math.round(wp.z));
            float3 world = new float3(tile.x, wp.y, tile.y); // tile.y = coord Z monde
            try { player.playerCommandSystem.CreateMapUI(world, (int)UserMapMarkerType.Marker1); }
            catch (Exception ex) { Diag.Error("A11yBeacon", ex); return; }
            try
            {
                player.entityPrespawnSystem.CreatePrespawnEntity(
                    new ObjectDataCD { objectID = ObjectID.MapMarker, variation = 0 },
                    EntityMonoBehaviour.ToRenderFromWorld(world), default(float3));
            }
            catch { } // visuel local optionnel : la balise serveur arrive de toute facon

            string biome = ResolveBiome(new float2(tile.x, tile.y));
            int n = BeaconStore.NextAutoNumber();
            string name = Strings.L("beacon.auto")
                + (string.IsNullOrEmpty(biome) ? "" : " " + biome) + " " + n;
            BeaconStore.SetName(tile, name);
            TtsText.Say(Strings.L("beacon.placed") + ", " + name, true);

            _pendingSelectTile = tile;
            _pendingSelectTime = Time.time + 1.2f;
            _hasPendingSelect = true;
        }

        // Action "Supprimer" du menu contextuel : supprime la balise donnee (RemoveMarker
        // serveur) et oublie son nom. Opere sur le marqueur capture a l'ouverture du menu
        // (pas sur _index, qui inclut desormais les lignes virtuelles).
        private static void DeleteBeacon(MapMarkerUIElement m)
        {
            if (m == null || !MarkerTile(m, out int2 tile)) return;
            if (!TryMarkerPos(m, out float2 pos)) return;
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null || player.guestMode) return;
            string name = BeaconName(m);
            try
            {
                player.QueueInputAction(new UIInputActionData
                {
                    action = UIInputAction.RemoveMarker,
                    entity = m.mapMarkerEntity,
                    position = pos
                });
            }
            catch (Exception ex) { Diag.Error("A11yBeacon", ex); return; }
            BeaconStore.Remove(tile);
            TtsText.Say(Strings.L("beacon.deleted") + ", " + name, true);
            BuildDestinations();
            // Re-cadre la selection sur une ligne valide (les lignes virtuelles restent).
            int n = RowCount();
            if (_index >= n) _index = n - 1;
            if (_index < 0) _index = 0;
            Select(_index, interruptTts: false);
        }

        // Action "Renommer" du menu contextuel : ouvre l'editeur clavier maison (carte
        // ouverte = gameplay gele, saisie sure) sur la balise donnee.
        private static void RenameBeacon(MapMarkerUIElement m)
        {
            if (m == null || !MarkerTile(m, out int2 tile)) return;
            string current = BeaconStore.GetName(tile) ?? "";
            TextEntry.Begin("beacon.rename", current, 40, name =>
            {
                if (string.IsNullOrEmpty(name)) return; // nom vide -> on garde l'ancien
                BeaconStore.SetName(tile, name);
                TtsText.Say(Strings.L("beacon.renamed") + ", " + name, true);
            });
        }

        // Biome a une position monde, via la generation du monde (marche meme zone non
        // chargee). Le singleton vit sur une entite SYSTEME -> query IncludeSystems. BiomeLookup
        // construit depuis BiomeSamplesCD (mondes a samples) ou BiomeRangesCD (procedural), ce
        // dernier etant IDisposable -> on libere. Tout sous try/catch : pas de biome si echec.
        // Biome courant a la position du joueur (suffixe de la localisation, D-pad haut) :
        // nom localise via la meme table que la carte, ou null (ecran de chargement).
        internal static string CurrentBiomeName(PlayerController player)
        {
            var w = player.WorldPosition;
            return ResolveBiome(new float2(w.x, w.z));
        }

        private static string ResolveBiome(float2 pos)
        {
            var world = Manager.ecs != null ? Manager.ecs.ClientWorld : null;
            if (world == null) return null;
            try
            {
                var em = world.EntityManager;
                var sb = new EntityQueryBuilder(Allocator.Temp)
                    .WithAll<BiomeSamplesCD>().WithOptions(EntityQueryOptions.IncludeSystems);
                var sq = sb.Build(em);
                sb.Dispose();

                BiomeLookup lookup;
                bool dispose = false;
                if (sq.HasSingleton<BiomeSamplesCD>())
                {
                    lookup = new BiomeLookup(sq.GetSingleton<BiomeSamplesCD>());
                }
                else
                {
                    var rb = new EntityQueryBuilder(Allocator.Temp)
                        .WithAll<BiomeRangesCD>().WithOptions(EntityQueryOptions.IncludeSystems);
                    var rq = rb.Build(em);
                    rb.Dispose();
                    if (!rq.HasSingleton<BiomeRangesCD>()) return null;
                    lookup = new BiomeLookup(rq.GetSingleton<BiomeRangesCD>().Value, Allocator.Temp);
                    dispose = true;
                }

                Biome b = lookup.GetBiome(new int2((int)math.round(pos.x), (int)math.round(pos.y)));
                if (dispose) lookup.Dispose();
                return BiomeName(b);
            }
            catch (System.Exception ex) { Diag.Error("A11yBiomeDiag", ex); return null; }
        }

        private static string BiomeName(Biome b)
        {
            switch (b)
            {
                case Biome.Slime: return Strings.L("biome.slime");
                case Biome.Larva: return Strings.L("biome.larva");
                case Biome.Stone: return Strings.L("biome.stone");
                case Biome.Nature: return Strings.L("biome.nature");
                case Biome.Sea: return Strings.L("biome.sea");
                case Biome.Desert: return Strings.L("biome.desert");
                case Biome.Crystal: return Strings.L("biome.crystal");
                case Biome.Passage: return Strings.L("biome.passage");
                case Biome.Excavation: return Strings.L("biome.excavation");
                default: return null; // None / biomes obsoletes
            }
        }

        private static float2 PlayerPos()
        {
            var p = Manager.main != null ? Manager.main.player : null;
            if (p == null) return float2.zero;
            var w = p.WorldPosition;
            return new float2(w.x, w.z);
        }

        private static bool TryMarkerPos(MapMarkerUIElement m, out float2 pos)
        {
            pos = float2.zero;
            try
            {
                var world = Manager.ecs != null ? Manager.ecs.ClientWorld : null;
                var ent = m.mapMarkerEntity; // champ public
                if (world == null || !EntityUtility.HasComponentData<LocalTransform>(ent, world)) return false;
                var t = EntityUtility.GetComponentData<LocalTransform>(ent, world);
                pos = new float2(t.Position.x, t.Position.z);
                return true;
            }
            catch { return false; }
        }

        private static float DistSq(MapMarkerUIElement m, float2 from)
            => TryMarkerPos(m, out float2 mp) ? math.distancesq(mp, from) : float.MaxValue;

        private static string DirectionAndDistance(MapMarkerUIElement m)
        {
            if (!TryMarkerPos(m, out float2 mp)) return null;
            float2 d = mp - PlayerPos();
            int cases = (int)math.round(math.length(d));
            string tiles = cases + " " + Strings.L("teleport.tiles");
            int hdg = Heading(d);
            if (hdg < 0) return tiles;
            // Cardinal d'abord (accessible), puis le cap precis en degres. Plusieurs modes
            // d'annonce configurables viendront en options plus tard.
            return Cardinal(hdg) + " " + hdg + " " + Strings.L("teleport.degrees") + ", " + tiles;
        }

        // Cap en degres : 0 = nord (+z), 90 = est (+x), sens horaire. -1 si trop proche.
        private static int Heading(float2 d)
        {
            if (math.lengthsq(d) < 0.25f) return -1;
            float ang = math.degrees(math.atan2(d.x, d.y));
            if (ang < 0f) ang += 360f;
            return ((int)math.round(ang)) % 360;
        }

        private static readonly string[] DirKeys =
            { "dir.n", "dir.ne", "dir.e", "dir.se", "dir.s", "dir.sw", "dir.w", "dir.nw" };

        // Secteur cardinal (8) correspondant au cap.
        private static string Cardinal(int hdg)
        {
            int sector = ((int)math.round(hdg / 45f)) % 8;
            return Strings.L(DirKeys[sector]);
        }
    }

}
