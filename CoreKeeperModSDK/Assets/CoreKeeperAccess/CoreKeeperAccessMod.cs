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
    private const string ReleaseTag = "alpha 1";
    private const string BuildTag = "build 90";

    public void Init()
    {
        Tolk.Load();
        Strings.Load();
        CoreKeeperAccess.Gameplay.A11ySettings.Load(); // reglages utilisateur (volume maitre des sons du mod)
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
        LogAudioConfigOnce();
        TriangleModifier.Tick();
        InfoKey.Tick();
        InputContext.Refresh(); // etats d'UI figes pour la frame, avant tout consommateur
        CoreKeeperAccess.Controls.TextEntry.Tick(); // saisie clavier maison (avale le clavier si active)
        CoreKeeperAccess.Settings.SettingsMenu.Tick(); // panneau de reglages a11y (modal, lit la manette en direct)
        CoreKeeperAccess.Controls.ActionMenu.Tick(); // menu contextuel carte (modal, lit la manette en direct)
        CoreKeeperAccess.Gameplay.VitalsReadout.Tick(); // apres InfoKey (consomme ses combos)
        CoreKeeperAccess.Gameplay.GameplayInput.Tick(); // idem (prospection minerai)
        CoreKeeperAccess.Navigation.InventoryNavigator.Update();
        TeleportNavigator.Update();
        CoreKeeperAccess.Gameplay.LaserCane.Tick(); // avant le curseur : pose LaserCane.Active
        CoreKeeperAccess.Gameplay.BuildModeNavigator.Tick();
        CoreKeeperAccess.Gameplay.AggroSentinel.Tick();
        CoreKeeperAccess.Gameplay.CombatSlowMotion.Tick(); // apres la sentinelle : etat de combat frais
        CoreKeeperAccess.Gameplay.CenterBeacon.Tick();     // repere de centre d'arene (drone vers la SummonArea)
        CoreKeeperAccess.Gameplay.FireProximity.Tick();    // alerte de proximite des zones de feu
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
