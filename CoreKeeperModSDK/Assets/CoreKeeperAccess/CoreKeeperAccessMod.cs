using System.Collections.Generic;
using CoreKeeperAccess.Localization;
using DavyKager;
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
        Tolk.Output(Strings.L("mod.loaded"), false);
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
