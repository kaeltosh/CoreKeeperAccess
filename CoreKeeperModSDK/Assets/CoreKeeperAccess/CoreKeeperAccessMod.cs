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

    public void EarlyInit()
    {
    }

    public void Init()
    {
        Tolk.Load();
        Strings.Load();
        DiagnosePatches();
        TtsText.Say(Strings.L("mod.loaded"), false);
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

    // Annonce en TTS (interrompt) ET trace dans Player.log pour lecture cote dev.
    private static void Announce(string text)
    {
        Tolk.Output(text, true);
        Debug.Log("[A11yInputDiag] " + text);
    }
}
