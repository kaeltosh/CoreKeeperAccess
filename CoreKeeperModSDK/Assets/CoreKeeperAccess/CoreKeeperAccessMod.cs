using System;
using System.Collections.Generic;
using System.Linq;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using DavyKager;
using HarmonyLib;
using PugMod;
using Rewired;
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

    // PROVISOIRE (dev) : auto-charge monde 1 / perso 1 (indices 0/0) au boot, une seule
    // fois par lancement, pour aller direct en jeu et gagner du temps de test. Saute la
    // navigation menu ET l'intro narrative (save existante -> LoadMainScene direct).
    // Flag actif jusqu'a nouvel ordre : passer DevAutoLoad a false pour revenir au menu.
    private const bool DevAutoLoad = true;
    private bool _autoLoadDone;
    private float _autoLoadStable;

    public void EarlyInit()
    {
        // Tentative de couper les logos de demarrage (Unity SplashScreen) si notre code
        // tourne encore pendant leur affichage. Sans effet s'ils sont deja passes (ils
        // sont rendus tres tot par le moteur, possiblement avant le chargement du mod).
        try
        {
            UnityEngine.Rendering.SplashScreen.Stop(
                UnityEngine.Rendering.SplashScreen.StopBehavior.StopImmediate);
        }
        catch { }
    }

    // PROVISOIRE (diagnostic) : numero de version annonce au boot, a incrementer a
    // chaque build, pour confirmer a l'oreille quelle version tourne reellement. A
    // retirer une fois l'ambiguite "build pas a jour ?" levee.
    private const string BuildTag = "build 11";

    public void Init()
    {
        Tolk.Load();
        Strings.Load();
        DiagnosePatches();
        TtsText.Say(Strings.L("mod.loaded") + ", " + BuildTag, false);
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
        CoreKeeperAccess.Navigation.InventoryNavigator.Update();
        CoreKeeperAccess.Gameplay.BuildModeNavigator.Tick();

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

    // PROVISOIRE (dev) : charge direct monde 1 / perso 1 des que le menu est pret.
    private void TryAutoLoad()
    {
        if (!DevAutoLoad || _autoLoadDone) return;
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
