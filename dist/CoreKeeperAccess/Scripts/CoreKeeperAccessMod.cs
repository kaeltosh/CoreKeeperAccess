using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using DavyKager;
using HarmonyLib;
using PugMod;
using Rewired;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Object = UnityEngine.Object;

public class CoreKeeperAccessMod : IMod
{
    // --- Diagnostic d'input (outil temporaire) ---
    // F9 active / coupe le mode. Quand actif, chaque bouton ou axe de la manette
    // pressé est annonce via Tolk ET ecrit dans Player.log (prefixe [A11yInputDiag]),
    // pour confirmer le mapping physique sans avoir a retenir les id a l'oreille.
    // Lit directement Rewired (ReInput), aucun patch Harmony.
    private bool _inputDiag;
    private readonly Dictionary<int, int> _axisState = new Dictionary<int, int>();

    // Mode dev : toggle par fichier-temoin "dev.flag" a la racine du dossier d'install
    // du mod (absent = comportement release, c'est le defaut distribue aux testeurs).
    // Actif : auto-charge monde 1 / perso 1 (indices 0/0) au boot — saute la navigation
    // menu ET l'intro narrative — et coupe les logos studio. fast-build.ps1 pose le
    // fichier d'office (le supprime avec -NoDev).
    private static bool _devMode;
    private bool _autoLoadDone;
    private float _autoLoadStable;

    public void EarlyInit()
    {
        try
        {
            _devMode = System.IO.File.Exists(System.IO.Path.Combine(
                Application.streamingAssetsPath, "Mods", "CoreKeeperAccess", "dev.flag"));
        }
        catch { _devMode = false; }

        if (!_devMode) return;
        // Coupe les logos de demarrage (Unity SplashScreen) si notre code tourne encore
        // pendant leur affichage. Sans effet s'ils sont deja passes (ils sont rendus tres
        // tot par le moteur, possiblement avant le chargement du mod).
        try
        {
            UnityEngine.Rendering.SplashScreen.Stop(
                UnityEngine.Rendering.SplashScreen.StopBehavior.StopImmediate);
        }
        catch { }
    }

    // Version annoncee au boot et a citer dans tout rapport de test :
    // ReleaseTag = la release publiee aux testeurs (ne bouge qu'a la publication),
    // BuildTag = le compteur fin de deploiement (incremente a chaque build).
    private const string ReleaseTag = "alpha 1";
    private const string BuildTag = "build 52";

    public void Init()
    {
        Tolk.Load();
        Strings.Load();
        DiagnosePatches();
        TtsText.Say(Strings.L("mod.loaded") + ", " + ReleaseTag + ", " + BuildTag, false);
    }

    // Verifie au demarrage que chacun de nos patches Harmony s'est bien applique.
    // Une maj de Core Keeper peut renommer/supprimer une methode cible : le patch
    // echoue alors silencieusement et la fonctionnalite associee meurt sans bruit.
    // On loggue un recap (erreur si manquants) pour reperer la casse au boot, sans
    // avoir a re-tester chaque feature en jeu. Auto-maintenu : tout type [HarmonyPatch]
    // de notre assembly est compte, donc un patch ajoute plus tard l'est aussi.
    private static void DiagnosePatches()
    {
        var asm = typeof(CoreKeeperAccessMod).Assembly;
        var expected = asm.GetTypes()
            .Where(t => t.IsDefined(typeof(HarmonyPatch), false))
            .ToList();

        var appliedTypes = new HashSet<Type>();
        foreach (var method in Harmony.GetAllPatchedMethods())
        {
            var info = Harmony.GetPatchInfo(method);
            if (info == null) continue;
            foreach (var patch in info.Prefixes.Concat(info.Postfixes)
                         .Concat(info.Transpilers).Concat(info.Finalizers))
            {
                var declaring = patch.PatchMethod != null ? patch.PatchMethod.DeclaringType : null;
                if (declaring != null) appliedTypes.Add(declaring);
            }
        }

        var missing = expected.Where(t => !appliedTypes.Contains(t)).Select(t => t.Name).ToList();
        if (missing.Count == 0)
            Debug.Log($"[A11yDiag] Patches Harmony OK : {expected.Count}/{expected.Count} appliques.");
        else
            Debug.LogError($"[A11yDiag] Patches Harmony : {missing.Count}/{expected.Count} NON appliques -> {string.Join(", ", missing)}");
    }

    public void Shutdown()
    {
        Strings.Unload();
        Tolk.Unload();
    }

    public void ModObjectLoaded(Object obj)
    {
    }

    public void Update()
    {
        TryAutoLoad();
        TriangleModifier.Tick();
        InfoKey.Tick();
        CoreKeeperAccess.Gameplay.VitalsReadout.Tick(); // apres InfoKey (consomme ses combos)
        CoreKeeperAccess.Gameplay.GameplayInput.Tick(); // idem (prospection minerai)
        CoreKeeperAccess.Navigation.InventoryNavigator.Update();
        TeleportNavigator.Update();
        CoreKeeperAccess.Gameplay.LaserCane.Tick(); // avant le curseur : pose LaserCane.Active
        CoreKeeperAccess.Gameplay.BuildModeNavigator.Tick();
        CoreKeeperAccess.Gameplay.AggroSentinel.Tick();

        if (UnityEngine.Input.GetKeyDown(KeyCode.F9))
        {
            _inputDiag = !_inputDiag;
            _axisState.Clear();
            Announce(_inputDiag ? "Diagnostic manette active" : "Diagnostic manette coupe");
        }

        if (!_inputDiag || !ReInput.isReady)
        {
            return;
        }

        var joy = ReInput.controllers.GetLastActiveController<Joystick>();
        if (joy == null)
        {
            return;
        }

        // Boutons : annonce au moment de l'appui.
        for (int i = 0; i < joy.buttonCount; i++)
        {
            if (joy.GetButtonDown(i))
            {
                var el = joy.ButtonElementIdentifiers[i];
                Announce("Bouton " + el.name + ", id " + el.id);
            }
        }

        // Axes : annonce une seule fois au franchissement d'un seuil.
        for (int i = 0; i < joy.axisCount; i++)
        {
            float v = joy.GetAxis(i);
            int now = v > 0.6f ? 1 : (v < -0.6f ? -1 : 0);
            int prev;
            _axisState.TryGetValue(i, out prev);
            if (now != 0 && now != prev)
            {
                var el = joy.AxisElementIdentifiers[i];
                Announce("Axe " + el.name + ", id " + el.id + ", " + (now > 0 ? "positif" : "negatif"));
            }
            _axisState[i] = now;
        }
    }

    // Mode dev seulement : charge direct monde 1 / perso 1 des que le menu est pret.
    private void TryAutoLoad()
    {
        if (!_devMode || _autoLoadDone) return;
        if (Manager.main == null || Manager.saves == null
            || Manager.load == null || Manager.menu == null) return;
        if (Manager.main.player != null) { _autoLoadDone = true; return; } // deja en jeu
        // Slot 0 (perso 1) et monde 0 (monde 1) doivent exister et etre charges.
        if (!Manager.saves.CharacterExists(0) || !Manager.saves.WorldExists(0)) return;
        // Petite stabilisation pour laisser le menu finir son init avant de charger.
        _autoLoadStable += Time.deltaTime;
        if (_autoLoadStable < 0.5f) return;
        _autoLoadDone = true;
        Debug.Log("[A11yAutoLoad] Chargement auto monde 1 / perso 1");
        SaveSlotPlayOption.StartGameFromActivity(0, 0);
    }

    // Annonce en TTS (interrompt) ET trace dans Player.log pour lecture cote dev.
    private static void Announce(string text)
    {
        Tolk.Output(text, true);
        Debug.Log("[A11yInputDiag] " + text);
    }
}

// Libere le bouton Triangle pour en faire le modificateur a11y du mod (notre "touche
// NVDA") : on retire l'assignation MANETTE de l'action carte (ToggleMap, id 55 - inutile
// pour un joueur aveugle) via l'API Rewired DeleteElementMap. On NE touche ni aux autres
// actions de Triangle (note d'instrument), ni au clavier. En memoire seulement (aucune
// sauvegarde) -> reversible, config Rewired du joueur intacte. Re-applique chaque seconde
// car le jeu peut reconstruire les maps selon le contexte (gameplay / UI). Le mod lit
// ensuite le bouton Triangle PHYSIQUE en direct (independant du mapping d'action).
// NOTE: a deplacer dans son propre fichier au prochain build Unity (futur moteur keymaps).
internal static class TriangleModifier
{
    private const int ToggleMapActionId = 55; // RewiredConsts.Action.ToggleMap

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
                    if (aem.actionId == ToggleMapActionId)
                    {
                        TriangleButtonId = aem.elementIdentifierId; // bouton physique = Triangle
                        toDelete.Add(aem.id);
                    }
                foreach (var id in toDelete) map.DeleteElementMap(id);
            }
        }

        if (TriangleButtonId < 0) TriangleButtonId = FallbackTriangleId;
    }
}

// Dispatcher de la "touche access" du mod. Triangle (libere par TriangleModifier) sert de
// MODIFICATEUR : tant qu'il est tenu, le D-pad ne navigue plus mais declenche des commandes
// a11y. Premier combo : Triangle + D-pad haut = "plus de details" sur l'element focalise,
// pris en charge par le contexte actif (ici le curseur de tuile). Tick() doit tourner AVANT
// les consommateurs du D-pad (ils consultent ModifierHeld / DetailRequested).
internal static class InfoKey
{
    private const int DpadUp = 16, DpadRight = 17, DpadDown = 18, DpadLeft = 19;
    private const int BumperLeft = 10; // LB / L1 (id physique template Rewired Gamepad)

    public static bool ModifierHeld;    // Triangle physiquement tenu
    public static bool DetailRequested; // combo Triangle + haut declenche cette frame
    public static bool ComboRight;      // Triangle + droite (reparer, selon contexte)
    public static bool ComboDown;       // Triangle + bas (transferer)
    public static bool ComboLeft;       // Triangle + gauche (tout recycler)
    public static bool ComboLB;         // Triangle + L1 (ping sonar)
    public static bool DoubleTapped;    // double-tap bref de Triangle (ouvrir la carte)

    // Double-tap : deux TAPS courts (< TapMaxDuration, sans combo D-pad pendant la
    // tenue) espaces de moins de DoubleTapWindow. Un appui long ou un combo n'est
    // jamais un tap -> aucun conflit avec le role de modificateur.
    private const float TapMaxDuration = 0.30f;
    private const float DoubleTapWindow = 0.40f;
    private static bool _wasHeld;
    private static float _holdStart;
    private static bool _comboDuringHold;
    private static float _lastTap = -10f;

    public static void Tick()
    {
        ModifierHeld = false;
        DetailRequested = false;
        ComboRight = ComboDown = ComboLeft = ComboLB = false;
        DoubleTapped = false;
        if (!ReInput.isReady) return;
        int tri = TriangleModifier.TriangleButtonId;
        if (tri < 0) return; // id Triangle pas encore capte
        var joy = ReInput.controllers.GetLastActiveController<Joystick>();
        if (joy == null) return;

        ModifierHeld = GetButtonById(joy, tri);
        if (ModifierHeld)
        {
            if (GetButtonDownById(joy, DpadUp)) DetailRequested = true;
            else if (GetButtonDownById(joy, DpadRight)) ComboRight = true;
            else if (GetButtonDownById(joy, DpadDown)) ComboDown = true;
            else if (GetButtonDownById(joy, DpadLeft)) ComboLeft = true;
            else if (GetButtonDownById(joy, BumperLeft)) ComboLB = true;
        }

        // Suivi tap / double-tap (fronts montant et descendant de Triangle).
        if (ModifierHeld && !_wasHeld) { _holdStart = Time.unscaledTime; _comboDuringHold = false; }
        if (ModifierHeld && (DetailRequested || ComboRight || ComboDown || ComboLeft || ComboLB))
            _comboDuringHold = true;
        if (!ModifierHeld && _wasHeld)
        {
            bool tap = Time.unscaledTime - _holdStart <= TapMaxDuration && !_comboDuringHold;
            if (tap)
            {
                if (Time.unscaledTime - _lastTap <= DoubleTapWindow)
                {
                    DoubleTapped = true;
                    _lastTap = -10f;
                }
                else
                {
                    _lastTap = Time.unscaledTime;
                }
            }
        }
        _wasHeld = ModifierHeld;
    }

    private static bool GetButtonById(Joystick joy, int id)
    {
        for (int i = 0; i < joy.buttonCount; i++)
            if (joy.ButtonElementIdentifiers[i].id == id) return joy.GetButton(i);
        return false;
    }

    private static bool GetButtonDownById(Joystick joy, int id)
    {
        for (int i = 0; i < joy.buttonCount; i++)
            if (joy.ButtonElementIdentifiers[i].id == id) return joy.GetButtonDown(i);
        return false;
    }
}

// Navigateur de destinations de teleportation (anciens relais). Interagir avec un relais
// pose player.currentPortal et ouvre la carte = l'interface de voyage rapide. On la rend
// accessible : liste des relais/waypoints teleportables (D-pad haut/bas), annonce nom +
// direction + distance, A native = clic sur le marqueur = teleportation (le jeu ferme la
// carte). Les bumpers restent libres pour de futures categories de marqueurs.
// NOTE: a reloger dans son propre fichier au prochain build Unity.
internal static class TeleportNavigator
{
    private const int DpadUp = 16, DpadDown = 18, CrossButton = 6, Lb = 10, Rb = 11;

    private static bool _active;
    private static readonly List<MapMarkerUIElement> _dests = new List<MapMarkerUIElement>();      // navigables (CanTeleport)
    private static readonly List<MapMarkerUIElement> _allPortals = new List<MapMarkerUIElement>();  // tous, pour la numerotation stable
    // Points d'INTERET : tous les autres marqueurs actifs de la carte (boss scannes,
    // pings...) - pas teleportables, mais nommables et localisables (cap + distance).
    // Categorie separee aux bumpers (reserves pour ca depuis le debut).
    private static readonly List<MapMarkerUIElement> _pois = new List<MapMarkerUIElement>();
    private static int _category; // 0 = destinations (teleport), 1 = points d'interet
    private static MapMarkerUIElement _coreMarker;                                                  // le plus proche de l'origine
    private static int _index;

    private static List<MapMarkerUIElement> CurrentList => _category == 0 ? _dests : _pois;

    // CanTeleport est prive -> reflection. (mapMarkerEntity, lui, est un champ PUBLIC :
    // acces direct, pas de reflection.)
    private static MethodInfo _canTeleport;
    private static bool _reflResolved;

    public static void Update()
    {
        // Carte ouverte = nav active, AVEC ou SANS relais (le double-tap Triangle
        // ouvre la carte hors relais : la categorie destinations est alors vide,
        // mais les points d'interet restent consultables).
        var player = Manager.main != null ? Manager.main.player : null;
        bool open = player != null && Manager.ui != null && Manager.ui.isShowingMap;

        if (open && !_active) Enter();
        else if (!open && _active) { _active = false; _dests.Clear(); _pois.Clear(); }
        if (!_active) return;

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
        string cat = Strings.L(_category == 0 ? "map.destinations" : "map.poi");
        var list = CurrentList;
        if (list.Count == 0)
        {
            TtsText.Say(cat + ", " + Strings.L(_category == 0 ? "teleport.none" : "map.poi.none"), true);
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
        _coreMarker = null;

        var all = Object.FindObjectsByType<MapMarkerUIElement>(FindObjectsSortMode.None);
        foreach (var m in all)
        {
            if (m == null || !m.isActiveAndEnabled) continue;
            if (m.markerType != MapMarkerType.Portal && m.markerType != MapMarkerType.Waypoint)
            {
                // Tout autre marqueur actif = point d'interet (boss scanne, ping...).
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

        float best = float.MaxValue;
        foreach (var m in _allPortals)
            if (TryMarkerPos(m, out float2 p) && math.lengthsq(p) < best)
            {
                best = math.lengthsq(p);
                _coreMarker = m;
            }
        if (best > 100f) _coreMarker = null; // aucun relais assez proche de l'origine
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
        var joy = ReInput.isReady ? ReInput.controllers.GetLastActiveController<Joystick>() : null;
        if (joy == null) return;
        // Touche access (Triangle) tenue : la croix directionnelle est reservee aux
        // commandes -> Triangle + haut = details de la destination.
        if (InfoKey.ModifierHeld)
        {
            if (InfoKey.DetailRequested) AnnounceDetail();
            return;
        }
        for (int i = 0; i < joy.buttonCount; i++)
        {
            if (!joy.GetButtonDown(i)) continue;
            int id = joy.ButtonElementIdentifiers[i].id;
            if (id == DpadUp) { Move(-1); return; }
            if (id == DpadDown) { Move(+1); return; }
            if (id == CrossButton) { Confirm(); return; }
            if (id == Lb || id == Rb) { SwitchCategory(); return; }
        }
    }

    // Bumpers = basculer destinations <-> points d'interet (2 categories : un toggle).
    private static void SwitchCategory()
    {
        _category = 1 - _category;
        _index = 0;
        AnnounceCategory();
    }

    private static void Move(int step)
    {
        var list = CurrentList;
        if (list.Count == 0) return;
        _index = (_index + step + list.Count) % list.Count;
        Select(_index);
    }

    // A = clic direct sur la destination selectionnee = teleportation. On appelle
    // OnLeftClicked (public) nous-memes : le routage natif de A ne le declenche pas sur la
    // carte. OnLeftClicked verifie CanTeleport, fait QueueInputAction(Teleport) et ferme la
    // carte. Un point d'interet n'est PAS teleportable : refus parlant.
    private static void Confirm()
    {
        if (_category != 0)
        {
            TtsText.Say(Strings.L("map.poi.notteleport"), true);
            return;
        }
        if (_index < 0 || _index >= _dests.Count) return;
        _dests[_index].OnLeftClicked(false, false);
    }

    private static void Select(int idx, bool interruptTts = true)
    {
        var list = CurrentList;
        if (idx < 0 || idx >= list.Count) return;
        var m = list[idx];
        // Selection forcee + recalage du curseur manette virtuel (meme piege UIMouse que
        // l'inventaire) pour que A native clique bien CE marqueur -> teleportation.
        CoreKeeperAccess.Navigation.InventoryNavState.SuppressPassiveAnnounce = true;
        try
        {
            Manager.ui.OnUIElementSelected(m);
            if (Manager.ui.mouse != null)
                Manager.ui.mouse.PlaceMousePositionOnSelectedUIElementWhenControlledByJoystick();
        }
        finally { CoreKeeperAccess.Navigation.InventoryNavState.SuppressPassiveAnnounce = false; }
        Announce(m, interruptTts);
    }

    private static void Announce(MapMarkerUIElement m, bool interrupt = true)
    {
        string name = (m == _coreMarker)
            ? Strings.L("teleport.core")
            : TtsText.ResolveTextAndFormatFields(m.GetHoverTitle());
        if (string.IsNullOrEmpty(name))
            name = Strings.L(_category == 0 ? "teleport.portal" : "map.poi.marker");
        // Numero en tete : STABLE pour les relais (tri par distance au centre),
        // simple ordre de proximite pour les points d'interet.
        int number = _category == 0 ? StableNumber(m) : _index + 1;
        string head = number + ", " + name;
        string dd = DirectionAndDistance(m);
        TtsText.Say(string.IsNullOrEmpty(dd) ? head : head + ", " + dd, interrupt);
    }

    // Touche access (Triangle + haut) : details de la destination selectionnee -> type
    // (portail / point de passage) + coordonnees exactes + cap + distance.
    private static void AnnounceDetail()
    {
        var list = CurrentList;
        if (_index < 0 || _index >= list.Count) return;
        var m = list[_index];
        string label;
        if (_category == 0)
        {
            label = (m == _coreMarker) ? Strings.L("teleport.core")
                : (m.markerType == MapMarkerType.Waypoint
                    ? Strings.L("teleport.waypoint") : Strings.L("teleport.portal"));
        }
        else
        {
            label = TtsText.ResolveTextAndFormatFields(m.GetHoverTitle());
            if (string.IsNullOrEmpty(label)) label = Strings.L("map.poi.marker");
        }
        int number = _category == 0 ? StableNumber(m) : _index + 1;
        string text = number + ", " + label;
        if (TryMarkerPos(m, out float2 mp))
        {
            text += ", " + Strings.L("teleport.position") + " "
                  + (int)math.round(mp.x) + " " + (int)math.round(mp.y);
            string biome = ResolveBiome(mp);
            if (!string.IsNullOrEmpty(biome))
                text += ", " + Strings.L("teleport.biome") + " " + biome;
        }
        string dd = DirectionAndDistance(m);
        if (!string.IsNullOrEmpty(dd)) text += ", " + dd;
        TtsText.Say(text, true);
    }

    // Biome a une position monde, via la generation du monde (marche meme zone non
    // chargee). Le singleton vit sur une entite SYSTEME -> query IncludeSystems. BiomeLookup
    // construit depuis BiomeSamplesCD (mondes a samples) ou BiomeRangesCD (procedural), ce
    // dernier etant IDisposable -> on libere. Tout sous try/catch : pas de biome si echec.
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
        catch { return null; }
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
