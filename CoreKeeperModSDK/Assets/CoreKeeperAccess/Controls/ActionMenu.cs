using System;
using System.Collections.Generic;
using CoreKeeperAccess.Gameplay;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using Rewired;

namespace CoreKeeperAccess.Controls
{
    // Menu contextuel MAISON, entierement TTS (aucun asset Unity). Ouvert sur un element
    // de la carte (Croix) : il presente une liste d'ACTIONS adaptee a cet element, ce qui
    // libere le moteur de keymaps (plus besoin d'un combo Triangle par action). MODAL : tant
    // qu'il est ouvert, l'input jeu est gele (NativeInputSuppressionPatch teste ActionMenu.
    // Active, comme TextEntry / SettingsMenu) et on lit les boutons PHYSIQUES en direct
    // (Rewired). Generique : le contenu (titre + items) est fourni a l'ouverture.
    // (Nomme ActionMenu et non ContextMenu pour ne pas heurter l'attribut UnityEngine.ContextMenu.)
    //
    // Controles (D-pad, menu ouvert, Triangle relache) :
    //  - Haut/Bas : naviguer. CYCLAGE + son de BUTEE au franchissement de la couture.
    //  - Croix : executer l'action focalisee (puis fermeture).
    //  - Rond ou Back : fermer sans rien faire.
    internal static class ActionMenu
    {
        internal struct Item
        {
            public string Label;   // libelle TTS (deja resolu)
            public Action Run;      // action a executer
        }

        private static bool _active;
        private static string _title;
        private static readonly List<Item> _items = new List<Item>();
        private static int _index;

        public static bool Active => _active;

        // Ouvre le menu sur une liste d'items non vide. Annonce titre + premier item.
        public static void Open(string title, List<Item> items)
        {
            if (items == null || items.Count == 0) return;
            _active = true;
            _title = title ?? "";
            _items.Clear();
            _items.AddRange(items);
            _index = 0;
            Tone(1.2f);
            TtsText.Say(_title + ", " + _items[0].Label, true);
        }

        public static void Close()
        {
            if (!_active) return;
            _active = false;
            _items.Clear();
            Tone(0.7f);
        }

        // A ticker tot dans l'Update, AVANT les consommateurs de carte / D-pad.
        public static void Tick()
        {
            if (!_active) return;
            if (!ReInput.isReady) return;
            var joy = ReInput.controllers.GetLastActiveController<Joystick>();
            if (joy == null) return;

            if (Down(joy, IdDown)) Move(+1);
            else if (Down(joy, IdUp)) Move(-1);
            else if (Down(joy, IdCross)) Activate();
            else if (Down(joy, IdCircle) || Down(joy, IdBack)) Close();
        }

        private static void Move(int delta)
        {
            int n = _items.Count;
            if (n == 0) return;
            int ni = _index + delta;
            bool wrapped = false;
            if (ni < 0) { ni = n - 1; wrapped = true; }
            else if (ni >= n) { ni = 0; wrapped = true; }
            _index = ni;
            Tone(wrapped ? 0.6f : 1f); // butee grave au bord, clic neutre sinon
            TtsText.Say(_items[ni].Label, true);
        }

        private static void Activate()
        {
            if (_index < 0 || _index >= _items.Count) return;
            var run = _items[_index].Run;
            // On ferme AVANT d'executer : l'action peut rearmer un input (fermer la carte)
            // qui ne doit plus etre gele par ActionMenu.Active.
            _active = false;
            _items.Clear();
            run?.Invoke();
        }

        // --- Lecture bouton physique par id d'element (meme template que SettingsMenu) ---
        private const int IdUp = 16, IdDown = 18, IdCross = 6, IdCircle = 7, IdBack = 12;

        private static bool Down(Joystick joy, int id)
        {
            for (int i = 0; i < joy.buttonCount; i++)
                if (joy.ButtonElementIdentifiers[i].id == id) return joy.GetButtonDown(i);
            return false;
        }

        private static void Tone(float pitch) => GameplayAudio.PlayTone(0f, pitch, 0.3f);
    }
}
