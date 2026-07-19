using System;
using System.Collections.Generic;
using System.Linq;
using CoreKeeperAccess;
using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Navigation;
using CoreKeeperAccess.Patches;
using DavyKager;
using HarmonyLib;
using Pug.RP;
using PugMod;
using UnityEngine;
using Object = UnityEngine.Object;

public class CoreKeeperAccessMod : IMod
{
    // Mode dev : toggle par fichier-temoin "dev.flag" a la racine du dossier d'install
    // du mod (absent = comportement release, c'est le defaut distribue aux testeurs).
    // Actif : auto-charge monde 1 / perso 1 (indices 0/0) au boot — saute la navigation
    // menu ET l'intro narrative — et coupe les logos studio. fast-build.ps1 pose le
    // fichier d'office (le supprime avec -NoDev).
    private static bool _devMode;
    public static bool DevMode => _devMode; // expose pour les diagnostics reserves au dev
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
    private const string ReleaseTag = "1.0.12 beta";
    private const string BuildTag = "build 1";

    public void Init()
    {
        Tolk.Load();
        Strings.Load();
        CoreKeeperAccess.Gameplay.A11ySettings.Load(); // reglages utilisateur (volume maitre des sons du mod)
        // Les fleches "appuie pour changer de barre" (slots 11/12) sont un pur repere visuel
        // sans valeur en TTS -> coupees d'office, tout utilisateur du mod part sans.
        if (Manager.prefs != null) Manager.prefs.ShowHotbarArrows = false;
        DiagnosePatches();
        ComboBindings.RegisterAll(); // table combo x contexte de la touche access
        if (_devMode) TtsText.SelfTest(); // auto-verifs du composeur, cote dev seulement
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
            Diag.Log("A11yDiag", $"Patches Harmony OK : {expected.Count}/{expected.Count} appliques.");
        else
            Debug.LogError($"[A11yDiag] {Diag.Stamp()} Patches Harmony : {missing.Count}/{expected.Count} NON appliques -> {string.Join(", ", missing)}");
    }

    public void Shutdown()
    {
        CoreKeeperAccess.Navigation.BeaconGraph.Flush(); // sauve les dernieres aretes non ecrites (debounce)
        Strings.Unload();
        Tolk.Unload();
    }

    public void ModObjectLoaded(Object obj)
    {
    }

    // Sentinelle de config audio (12 juin) : si la sortie n'est pas en vraie stereo,
    // c'est generalement que l'AUDIO SPATIAL WINDOWS (Sonic/Atmos casque) est actif -
    // il declare le casque en 7.1 et re-melange tous les canaux avec du crossfeed
    // binaural : AUCUN pan ne survit (diagnostique chez l'utilisateur, builds 64-70 ;
    // la fuite d'oreille opposee venait de la, pas du jeu ni du mod). On log la
    // config (support testeurs : chercher driverCaps != Stereo dans Player.log) et
    // on documente "desactiver l'audio spatial Windows" cote utilisateur.
    private bool _audioCfgLogged;

    private void LogAudioConfigOnce()
    {
        if (_audioCfgLogged) return;
        _audioCfgLogged = true;
        var cfg = AudioSettings.GetConfiguration();
        Diag.Log("A11yPanDiag", "speakerMode=" + cfg.speakerMode
            + " driverCaps=" + AudioSettings.driverCapabilities
            + (AudioSettings.driverCapabilities != AudioSpeakerMode.Stereo
                ? " (audio spatial Windows probablement ACTIF : pan degrade)" : ""));
    }

    public void Update()
    {
        TryAutoLoad();
        TryDevGodMode();
        TryDevInvincible();
        TryDevBeaconGraphDiag();
        TryDevNetworkRecalc();
        TryDevNetworkDump();
        TryDevLightDiag();
        TryDevCheckerStamp();
        PublishLightSources(); // detecteur d'obscurite : liste des sources actives pres du joueur
        LogAudioConfigOnce();
        TriangleModifier.Tick();
        InfoKey.Tick();
        ScannerModifier.Tick(); // second modificateur (R3) : scanner de proximite
        InputContext.Refresh(); // etats d'UI figes pour la frame, avant tout consommateur
        CoreKeeperAccess.Controls.TextEntry.Tick(); // saisie clavier maison (avale le clavier si active)
        CoreKeeperAccess.Settings.SettingsMenu.Tick(); // panneau de reglages a11y (modal, lit la manette en direct)
        CoreKeeperAccess.Controls.ActionMenu.Tick(); // menu contextuel carte (modal, lit la manette en direct)
        CoreKeeperAccess.Controls.SoundGuide.Tick(); // menu d'apprentissage des sons (modal, lit la manette en direct)
        CoreKeeperAccess.Controls.OnboardingHint.Tick(); // popup d'accueil, comment rouvrir l'aide (force 1re fois en jeu, modal)
        CoreKeeperAccess.Controls.PadLearn.Tick(); // mode decouverte manette (relancable via le menu d'aide, modal)
        CoreKeeperAccess.Controls.CommandLearn.Tick(); // mode decouverte des commandes (idem, "input help")
        CoreKeeperAccess.Gameplay.VitalsReadout.Tick(); // apres InfoKey (consomme ses combos)
        CoreKeeperAccess.Gameplay.ConditionAlerts.Tick(); // earcons a l'apparition d'un DoT / stun
        CoreKeeperAccess.Gameplay.GameplayInput.Tick(); // idem (prospection minerai)
        CoreKeeperAccess.Gameplay.PickupAnnouncer.Tick(); // diff inventaire -> annonce des ramassages
        CoreKeeperAccess.Navigation.InventoryNavigator.Update();
        TeleportNavigator.Update();
        CoreKeeperAccess.Gameplay.LaserCane.Tick(); // avant le curseur : pose LaserCane.Active
        CoreKeeperAccess.Gameplay.BuildModeNavigator.Tick();
        CoreKeeperAccess.Gameplay.AggroSentinel.Tick();
        CoreKeeperAccess.Gameplay.CombatSlowMotion.Tick(); // apres la sentinelle : etat de combat frais
        CoreKeeperAccess.Gameplay.SummonBeacon.Tick();     // guide sonore vers le sigil d'invocation du boss
        CoreKeeperAccess.Gameplay.RelayBeacon.Tick();      // drone battement vers le relais non active le plus proche
        CoreKeeperAccess.Gameplay.FireProximity.Tick();    // alerte de proximite des zones de feu
        CoreKeeperAccess.Gameplay.BossAnimAlert.Tick();   // actions du boss de la ruche (tir acide, enrage, oeufs)
        CoreKeeperAccess.Gameplay.AzeosBoss.Tick();       // combat d'Azeos (piliers/rangees/cristaux/etats)
        CoreKeeperAccess.Gameplay.BossHealthAnnounce.Tick(); // annonce vie boss tous les 10% (generique, PROVISOIRE)
        // Apres le tick de tous les modules : les gardes de contexte lisent des etats
        // frais (curseur detache, nav inventaire...) au moment de router les combos.
        ComboDispatcher.Tick();
        PadDiagnostic.Tick(); // diagnostic manette F9
    }

    // Outil de test CACHE (jamais documente cote testeurs) : toggle du god mode CREATIF
    // natif - god mode INTEGRAL (invincible + passe-muraille + invisible aux ennemis +
    // degats massifs/one-shot), PAS une simple invincibilite. A activer pour explorer /
    // se deplacer vite, a COUPER pour tester le combat reel. Combo volontairement
    // improbable : Triangle (touche access) MAINTENU + F8 clavier -> un testeur ne tombe
    // pas dessus par hasard (F9 est deja le diag manette, F8 etait libre).
    private void TryDevGodMode()
    {
        if (!CoreKeeperAccess.Controls.InfoKey.ModifierHeld) return;
        if (!UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F8)) return;
        var player = Manager.main != null ? Manager.main.player : null;
        if (player == null) return;
        try
        {
            bool on = player.GetLastLocalGodModeState();
            player.SetGodModeCreative(!on);
            TtsText.Say(!on ? "Mode createur active" : "Mode createur desactive", true);
            Diag.Log("A11yDevGodMode", "god mode creatif -> " + (!on));
        }
        catch (System.Exception ex) { Diag.Error("A11yDevGodMode", ex); }
    }

    // Outil de test CACHE : invincibilite PURE (le joueur ne meurt pas, mais le combat
    // reste normal - DISTINCT du god mode creatif F8). Toggle Triangle (touche access)
    // MAINTENU + F7 clavier. Le forcage de vie est fait cote serveur par
    // DevInvincibilitySystem ; ici on ne fait que basculer le flag partage.
    private void TryDevInvincible()
    {
        if (!CoreKeeperAccess.Controls.InfoKey.ModifierHeld) return;
        if (!UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F7)) return;
        CoreKeeperAccess.Gameplay.DevInvincibility.Active = !CoreKeeperAccess.Gameplay.DevInvincibility.Active;
        bool on = CoreKeeperAccess.Gameplay.DevInvincibility.Active;
        TtsText.Say(on ? "Invincible active" : "Invincible desactive", true);
        Diag.Log("A11yDevGodMode", "invincibilite pure -> " + on);
    }

    // Diagnostic dev du reseau de navigation (tranche A) : Triangle (touche access)
    // MAINTENU + F6 -> annonce nombre de noeuds / aretes + noeud courant. Sert a valider
    // le tissage des torches AU TTS, sans audio ni combo manette dedie (le guidage sonore
    // viendra en tranche B). Cache aux testeurs comme F7/F8.
    private void TryDevBeaconGraphDiag()
    {
        if (!CoreKeeperAccess.Controls.InfoKey.ModifierHeld) return;
        if (!UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F6)) return;
        CoreKeeperAccess.Gameplay.BeaconTracker.AnnounceDiag();
    }

    // Recalcul local du reseau (tranche C) : Triangle (touche access) MAINTENU + F5 -> tisse
    // les aretes manquantes par ligne de vue dans un rayon autour du joueur (mise a jour de la
    // base). Au clavier dev pour valider l'algo ; le vrai declencheur manette viendra apres.
    private void TryDevNetworkRecalc()
    {
        if (!CoreKeeperAccess.Controls.InfoKey.ModifierHeld) return;
        if (!UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F5)) return;
        var player = Manager.main != null ? Manager.main.player : null;
        if (player == null) return;
        var wp = player.WorldPosition;
        CoreKeeperAccess.Gameplay.NetworkRecalc.Center = new Unity.Mathematics.int2(
            UnityEngine.Mathf.RoundToInt(wp.x), UnityEngine.Mathf.RoundToInt(wp.z));
        CoreKeeperAccess.Gameplay.NetworkRecalc.Radius = 16f;
        CoreKeeperAccess.Gameplay.NetworkRecalc.ResultValid = false;
        CoreKeeperAccess.Gameplay.NetworkRecalc.Requested = true;
    }

    // Dump ASCII du reseau local (dev) : Triangle (touche access) MAINTENU + F4 -> dessine la
    // zone vue par le mod dans Player.log (prefixe [A11yNetDump]), a croiser avec une capture.
    private void TryDevNetworkDump()
    {
        if (!CoreKeeperAccess.Controls.InfoKey.ModifierHeld) return;
        if (!UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F4)) return;
        var player = Manager.main != null ? Manager.main.player : null;
        if (player == null) return;
        var wp = player.WorldPosition;
        CoreKeeperAccess.Gameplay.NetworkDump.Center = new Unity.Mathematics.int2(
            UnityEngine.Mathf.RoundToInt(wp.x), UnityEngine.Mathf.RoundToInt(wp.z));
        CoreKeeperAccess.Gameplay.NetworkDump.Radius = 12f;
        CoreKeeperAccess.Gameplay.NetworkDump.Requested = true;
    }

    // Diagnostic dev eclairage/plafond (Triangle maintenu + F3, cache comme F4-F9) : dump dans
    // Player.log (a) le biome + la liste ManagedLight proche du joueur (reflexion : allLights
    // est prive cote jeu, meme genre d'acces que GameplayAudio.cs/SlotSection.cs) et (b) demande
    // au systeme ECS le dump ASCII de la grille roofHole/mur (LightDump, Gameplay/TileReader.cs).
    // But : verifier en jeu si allLights est complet (pas coupe par distance) et si le "plafond"
    // (roofHole) colle aux observations (Azeos qui perce le plafond, desert sans plafond).
    private void TryDevLightDiag()
    {
        if (!CoreKeeperAccess.Controls.InfoKey.ModifierHeld) return;
        if (!UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F3)) return;
        var player = Manager.main != null ? Manager.main.player : null;
        if (player == null) return;
        try
        {
            var wp = player.WorldPosition;
            string biome = CoreKeeperAccess.Navigation.MapMarkerUtil.ResolveBiome(
                new Unity.Mathematics.float2(wp.x, wp.z));
            Diag.Log("A11yLightDiag", "joueur " + wp.x + "," + wp.z + " biome=" + (biome ?? "?"));

            DumpManagedLights(wp);

            CoreKeeperAccess.Gameplay.LightDump.Center = new Unity.Mathematics.int2(
                UnityEngine.Mathf.RoundToInt(wp.x), UnityEngine.Mathf.RoundToInt(wp.z));
            CoreKeeperAccess.Gameplay.LightDump.Radius = 12;
            CoreKeeperAccess.Gameplay.LightDump.Requested = true;

            TtsText.Say("Diagnostic eclairage enregistre", true);
        }
        catch (System.Exception ex) { Diag.Error("A11yLightDiag", ex); }
    }

    // Banc de test tuilage/eclairage (Triangle maintenu + F10, cache comme F3-F9) : pose un
    // damier noir/blanc max-contraste (TileType.floor) sur un carre de 24 cases de demi-taille
    // autour du joueur (CheckerStamp, Gameplay/TileReader.cs). Sert de reference visuelle a un
    // testeur voyant pour calibrer a l'oeil l'etendue du halo d'eclairage indirect (aucun
    // chiffre "N cases" extractible du code, cf. core-keeper-ingame-data-access.md). N'ecrit
    // que le sol - le testeur choisit lui-meme une zone sans plafond troue ni lumiere.
    private void TryDevCheckerStamp()
    {
        // Ecrit cote SERVEUR (voir CheckerStampSystem) : sur un vrai serveur partage,
        // rase et damier-ise une zone pour TOUT le monde, pas juste le client local.
        // Calibration eclairage terminee (15 juillet) -> gate _devMode, jamais actif
        // dans l'artefact distribue (dev.flag absent), evite tout risque en multi.
        if (!_devMode) return;
        if (!CoreKeeperAccess.Controls.InfoKey.ModifierHeld) return;
        if (!UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F10)) return;
        var player = Manager.main != null ? Manager.main.player : null;
        if (player == null) return;
        try
        {
            var wp = player.WorldPosition;
            CoreKeeperAccess.Gameplay.CheckerStamp.Center = new Unity.Mathematics.int2(
                UnityEngine.Mathf.RoundToInt(wp.x), UnityEngine.Mathf.RoundToInt(wp.z));
            CoreKeeperAccess.Gameplay.CheckerStamp.HalfSize = 24;
            CoreKeeperAccess.Gameplay.CheckerStamp.Requested = true;
            TtsText.Say("Damier pose", true);
        }
        catch (System.Exception ex) { Diag.Error("A11yCheckerStamp", ex); }
    }

    private static System.Reflection.FieldInfo _allLightsField;
    private static System.Reflection.FieldInfo _lightObjLightField;
    private static System.Reflection.FieldInfo _lightObjTransformField;

    // Detecteur d'obscurite (design fige 16 juillet 2026, cf. core-keeper-darkness-gate.md) :
    // publie chaque frame (throttle ~10 Hz) la liste des sources ponctuelles ACTIVES pres du
    // joueur, dans le pont LightSourceScan (TileReader.cs) - les systemes ECS n'ont pas acces
    // a Manager.camera / la reflexion Unity sur ManagedLight.allLights. Meme reflexion que
    // DumpManagedLights (F3), mais filtree isLightEnabled (pas juste loguee) et bornee en
    // distance (perf : eviter de publier les lumieres d'une base entiere hors ecran).
    private float _nextLightPublish;
    private const float LightPublishInterval = 0.1f; // ~10 Hz, coherent avec les autres scans
    private const float LightPublishRadius = 40f;    // marge large : couvre la portee de tous les consommateurs + le bleed

    private void PublishLightSources()
    {
        if (!CoreKeeperAccess.Gameplay.A11ySettings.DarknessGate)
        {
            CoreKeeperAccess.Gameplay.LightSourceScan.Count = 0;
            return;
        }
        if (Time.unscaledTime < _nextLightPublish) return;
        _nextLightPublish = Time.unscaledTime + LightPublishInterval;

        var player = Manager.main != null ? Manager.main.player : null;
        if (player == null) { CoreKeeperAccess.Gameplay.LightSourceScan.Count = 0; return; }

        if (_allLightsField == null)
            _allLightsField = typeof(ManagedLight).GetField("allLights",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var list = _allLightsField?.GetValue(null) as System.Collections.IList;
        if (list == null) { CoreKeeperAccess.Gameplay.LightSourceScan.Count = 0; return; }

        var wp = player.WorldPosition;
        int count = 0;
        int max = CoreKeeperAccess.Gameplay.LightSourceScan.MaxSources;
        foreach (var boxed in list)
        {
            if (count >= max) break;
            if (_lightObjLightField == null || _lightObjTransformField == null)
            {
                var t = boxed.GetType();
                _lightObjLightField = t.GetField("light", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                _lightObjTransformField = t.GetField("transform", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            }
            var light = _lightObjLightField.GetValue(boxed) as ManagedLight;
            var transform = _lightObjTransformField.GetValue(boxed) as Transform;
            if (light == null || transform == null || light.lightToOptimize == null) continue;
            if (!light.isLightEnabled) continue;
            var p = transform.position + Manager.camera.RenderOrigo; // coordonnees RENDU -> MONDE
            float dx = p.x - wp.x, dz = p.z - wp.z;
            if (dx * dx + dz * dz > LightPublishRadius * LightPublishRadius) continue;
            CoreKeeperAccess.Gameplay.LightSourceScan.Pos[count] = new Unity.Mathematics.float2(p.x, p.z);
            CoreKeeperAccess.Gameplay.LightSourceScan.Range[count] = light.lightToOptimize.range;
            CoreKeeperAccess.Gameplay.LightSourceScan.IsWorldEntity[count] = !light.neverOptimize;
            count++;
        }
        CoreKeeperAccess.Gameplay.LightSourceScan.Count = count;
    }

    // ManagedLight.allLights est prive cote jeu (List<ManagedLight.ManagedLightObject>, struct
    // egalement privee) : lu par reflexion. La struct est privee mais ses CHAMPS ("light"/
    // "transform") sont publics et leurs TYPES (ManagedLight, Transform) le sont aussi -> une
    // fois extraits par reflexion, castables et utilisables normalement (pas besoin de re-
    // reflechir dedans).
    private void DumpManagedLights(Vector3 playerPos)
    {
        if (_allLightsField == null)
            _allLightsField = typeof(ManagedLight).GetField("allLights",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var list = _allLightsField?.GetValue(null) as System.Collections.IList;
        if (list == null)
        {
            Diag.Log("A11yLightDiag", "ManagedLight.allLights introuvable (reflexion)");
            return;
        }

        Diag.Log("A11yLightDiag", "ManagedLight.allLights total=" + list.Count);

        // Hypothese a verifier (16 juillet 2026) : les lumieres portees par une ENTITE
        // (creature/familier, glowLight de ConditionsEffectsHandler) recoivent-elles un
        // traitement DIFFERENT des torches statiques dans le calcul d'eclairage indirect
        // (IndirectLightRenderFeature, PugRP.dll) ? Le masque de calques indirectLightLayers
        // (PugCamera) determine QUELS objets sont captes par la passe indirecte/bloom - si les
        // entites sont sur un calque exclu de ce masque, leur lumiere n'aurait AUCUN bleed
        // indirect (contrairement aux torches), ce qui expliquerait le halo bien plus petit
        // observe sur une creature lumineuse. Log le masque + le calque de CHAQUE source.
        try
        {
            var pugCam = Manager.camera != null && Manager.camera.gameCamera != null
                ? Manager.camera.gameCamera.GetPugCamera() : null;
            if (pugCam != null)
                Diag.Log("A11yLightDiag", "indirectLightLayers=0x" + pugCam.indirectLightLayers.value.ToString("X")
                    + " indirectLightSeparateBlockerPassLayers=0x" + pugCam.indirectLightSeparateBlockerPassLayers.value.ToString("X"));
            else
                Diag.Log("A11yLightDiag", "PugCamera introuvable (GetPugCamera a rendu null)");
        }
        catch (System.Exception ex) { Diag.Error("A11yLightDiag", ex); }

        var entries = new List<LightEntry>();
        foreach (var boxed in list)
        {
            if (_lightObjLightField == null || _lightObjTransformField == null)
            {
                var t = boxed.GetType();
                _lightObjLightField = t.GetField("light", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                _lightObjTransformField = t.GetField("transform", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            }
            var light = _lightObjLightField.GetValue(boxed) as ManagedLight;
            var transform = _lightObjTransformField.GetValue(boxed) as Transform;
            if (light == null || transform == null || light.lightToOptimize == null) continue;
            // transform.position est en coordonnees RENDU (camera-relatives, cf.
            // ManagedLight.UpdateOptimization cote jeu) : il faut reajouter RenderOrigo pour
            // retrouver le vrai monde et comparer a player.WorldPosition.
            var p = transform.position + Manager.camera.RenderOrigo;
            float dx = p.x - playerPos.x;
            float dz = p.z - playerPos.z;
            entries.Add(new LightEntry
            {
                dist = Mathf.Sqrt(dx * dx + dz * dz),
                dx = dx,
                dz = dz,
                range = light.lightToOptimize.range,
                intensity = light.lightToOptimize.intensity,
                enabled = light.isLightEnabled,
                layer = transform.gameObject.layer,
                layerName = LayerMask.LayerToName(transform.gameObject.layer),
                neverOptimize = light.neverOptimize,
                color = light.lightToOptimize.color,
            });
        }
        entries.Sort((a, b) => a.dist.CompareTo(b.dist));
        int shown = 0;
        foreach (var e in entries)
        {
            if (shown >= 40) break;
            Diag.Log("A11yLightDiag", "  dx=" + e.dx.ToString("F1") + " dz=" + e.dz.ToString("F1")
                + " range=" + e.range.ToString("F1") + " intensity=" + e.intensity.ToString("F2")
                + " enabled=" + e.enabled + " layer=" + e.layer + "(" + e.layerName + ")"
                + " neverOptimize=" + e.neverOptimize
                + " color=" + e.color.r.ToString("F2") + "," + e.color.g.ToString("F2") + "," + e.color.b.ToString("F2"));
            shown++;
        }
        if (entries.Count > shown)
            Diag.Log("A11yLightDiag", "  ... + " + (entries.Count - shown) + " autres (tri par distance)");
    }

    private struct LightEntry
    {
        public float dist, dx, dz, range, intensity;
        public bool enabled;
        public int layer;
        public string layerName;
        public bool neverOptimize;
        public Color color;
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
        Diag.Log("A11yAutoLoad", "Chargement auto monde 1 / perso 1");
        SaveSlotPlayOption.StartGameFromActivity(0, 0);
    }

}
