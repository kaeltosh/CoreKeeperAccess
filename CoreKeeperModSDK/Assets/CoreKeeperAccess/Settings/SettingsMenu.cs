using System;
using System.Collections.Generic;
using CoreKeeperAccess.Gameplay;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using Rewired;
using UnityEngine;

namespace CoreKeeperAccess.Settings
{
    // Panneau de reglages MAISON, entierement TTS (aucun asset Unity, aucune greffe sur le
    // menu options natif - verifie non injectable). Ouvre via Triangle + Back. MODAL : tant
    // qu'il est ouvert, l'input jeu est gele (NativeInputSuppressionPatch, comme TextEntry),
    // on lit les boutons PHYSIQUES en direct (Rewired) et on pilote une liste navigable.
    //
    // Structure en ARBRE (categories imbricables, facon menu contextuel Windows) : la v1
    // tient en une liste plate a la racine, mais le moteur descend/remonte deja par pile -
    // ajouter une categorie plus tard = pure declaration dans Build(). Les reglages vivent
    // dans A11ySettings (persistance immediate a chaque changement).
    //
    // Controles (D-pad, panneau ouvert, Triangle relache) :
    //  - Haut/Bas : naviguer le niveau courant. CYCLAGE (boucle) + son de BUTEE au
    //    franchissement de la couture (debut<->fin) pour reperer les bords a l'oreille.
    //  - Droite : feuille = +pas / activer ; categorie = entrer.
    //  - Gauche : feuille = -pas / desactiver ; categorie = remonter d'un niveau.
    //  - Croix : entrer une categorie / basculer une bascule.
    //  - Rond ou Back : remonter d'un niveau ; a la racine = fermer.
    // Les earcons (Tone) sont des PLACEHOLDERS (tons generes) : l'utilisateur choisira les
    // sons definitifs (son domaine).
    internal static class SettingsMenu
    {
        // --- Modele d'arbre ---
        internal abstract class Node { public string LabelKey; }

        internal sealed class Category : Node { public readonly List<Node> Children = new List<Node>(); }

        internal sealed class Toggle : Node
        {
            public Func<bool> Get;
            public Action<bool> Set;
        }

        internal sealed class Slider : Node
        {
            public Func<float> Get;
            public Action<float> Set;
            public float Step;        // increment (0.1 = 10 %)
            public float Max = 1f;    // borne haute : 1 = volume normal ; > 1 = amplification (ex. 2 = +6 dB)
        }


        // --- Etat ---
        public static bool Active { get; private set; }

        private static Category _root;

        // Pile de navigation : un niveau ouvert = sa categorie + l'index focalise (la
        // position est memorisee quand on entre/ressort, comme l'explorateur Windows).
        private struct Level { public Category Cat; public int Idx; }
        private static readonly List<Level> _stack = new List<Level>();

        private static Category Current => _stack[_stack.Count - 1].Cat;

        private static int Index
        {
            get => _stack[_stack.Count - 1].Idx;
            set { var lv = _stack[_stack.Count - 1]; lv.Idx = value; _stack[_stack.Count - 1] = lv; }
        }

        private static Node Cur => Current.Children[Index];

        // --- Construction de l'arbre (v1 : racine plate) ---
        private static void EnsureBuilt()
        {
            if (_root != null) return;
            _root = new Category { LabelKey = "settings.title" };
            _root.Children.Add(new Slider
            {
                LabelKey = "settings.mastervolume",
                Get = () => A11ySettings.MasterVolume, Set = A11ySettings.SetMasterVolume, Step = 0.05f, Max = 2f,
            });
            _root.Children.Add(new Slider
            {
                LabelKey = "settings.navvolume",
                Get = () => A11ySettings.NavigationVolume, Set = A11ySettings.SetNavigationVolume, Step = 0.1f, Max = 2f,
            });
            _root.Children.Add(new Slider
            {
                LabelKey = "settings.guidevolume",
                Get = () => A11ySettings.GuideVolume, Set = A11ySettings.SetGuideVolume, Step = 0.05f, Max = 2f,
            });
            _root.Children.Add(new Toggle
            {
                LabelKey = "settings.stepbeep",
                Get = () => A11ySettings.StepBeep, Set = A11ySettings.SetStepBeep,
            });
            _root.Children.Add(new Slider
            {
                LabelKey = "settings.directiontick",
                Get = () => A11ySettings.DirectionTickVolume, Set = A11ySettings.SetDirectionTickVolume, Step = 0.05f, Max = 2f,
            });
            _root.Children.Add(new Toggle
            {
                LabelKey = "settings.snap",
                Get = () => A11ySettings.SnapDirectional, Set = A11ySettings.SetSnapDirectional,
            });
            _root.Children.Add(new Toggle
            {
                LabelKey = "settings.slowmo",
                Get = () => A11ySettings.CombatSlowMo, Set = A11ySettings.SetCombatSlowMo,
            });
            _root.Children.Add(new Toggle
            {
                LabelKey = "settings.normalize",
                Get = () => A11ySettings.NormalizeAudio, Set = A11ySettings.SetNormalizeAudio,
            });
            _root.Children.Add(new Toggle
            {
                LabelKey = "settings.sonar",
                Get = () => A11ySettings.ProximitySonar, Set = A11ySettings.SetProximitySonar,
            });
            _root.Children.Add(new Slider
            {
                LabelKey = "settings.sonarvolume",
                Get = () => A11ySettings.SonarVolume, Set = A11ySettings.SetSonarVolume, Step = 0.05f, Max = 2f,
            });
            _root.Children.Add(new Slider
            {
                LabelKey = "settings.sonarmedium",
                Get = () => A11ySettings.SonarVolMedium, Set = A11ySettings.SetSonarVolMedium, Step = 0.05f, Max = 2f,
            });
            _root.Children.Add(new Slider
            {
                LabelKey = "settings.sonargrave",
                Get = () => A11ySettings.SonarVolGrave, Set = A11ySettings.SetSonarVolGrave, Step = 0.05f, Max = 2f,
            });
            _root.Children.Add(new Toggle
            {
                LabelKey = "settings.objectding",
                Get = () => A11ySettings.ObjectDing, Set = A11ySettings.SetObjectDing,
            });
        }

        // --- Ouverture / fermeture ---
        public static void Open()
        {
            if (Active) return;
            EnsureBuilt();
            Active = true;
            _stack.Clear();
            _stack.Add(new Level { Cat = _root, Idx = 0 });
            Tone(1.2f);
            TtsText.Say(Strings.L("settings.title") + ", " + Describe(Cur), true);
        }

        public static void Close()
        {
            if (!Active) return;
            Active = false;
            _stack.Clear();
            Tone(0.7f);
            TtsText.Say(Strings.L("settings.closed"), true);
        }

        // --- Boucle (tickee tot dans l'Update, comme TextEntry) ---
        public static void Tick()
        {
            if (!Active) return;
            if (!ReInput.isReady) return;
            var joy = ReInput.controllers.GetLastActiveController<Joystick>();
            if (joy == null) return;

            if (Down(joy, IdDown)) Move(+1);
            else if (Down(joy, IdUp)) Move(-1);
            else if (Down(joy, IdRight)) OnRight();
            else if (Down(joy, IdLeft)) OnLeft();
            else if (Down(joy, IdCross)) OnCross();
            else if (Down(joy, IdCircle) || Down(joy, IdBack)) Back();
        }

        // Navigation haut/bas avec CYCLAGE + son de butee au franchissement de la couture.
        private static void Move(int delta)
        {
            var items = Current.Children;
            if (items.Count == 0) return;
            int n = items.Count;
            int ni = Index + delta;
            bool wrapped = false;
            if (ni < 0) { ni = n - 1; wrapped = true; }
            else if (ni >= n) { ni = 0; wrapped = true; }
            Index = ni;
            Tone(wrapped ? 0.6f : 1f); // butee grave au bord, clic neutre sinon
            TtsText.Say(Describe(items[ni]), true);
        }

        private static void OnRight()
        {
            var n = Cur;
            if (n is Slider s) { s.Set(Mathf.Clamp(s.Get() + s.Step, 0f, s.Max)); Tone(1.1f); SayValue(s); }
            else if (n is Toggle t) { if (!t.Get()) t.Set(true); Tone(1.5f); SayValue(t); }
            else if (n is Category c) Enter(c);
        }

        private static void OnLeft()
        {
            var n = Cur;
            if (n is Slider s) { s.Set(Mathf.Clamp(s.Get() - s.Step, 0f, s.Max)); Tone(1.1f); SayValue(s); }
            else if (n is Toggle t) { if (t.Get()) t.Set(false); Tone(1.5f); SayValue(t); }
            else Back(); // categorie focalisee : gauche = remonter
        }

        private static void OnCross()
        {
            var n = Cur;
            if (n is Category c) Enter(c);
            else if (n is Toggle t) { t.Set(!t.Get()); Tone(1.5f); SayValue(t); }
            else SayValue(n); // slider : re-annonce la valeur
        }

        private static void Enter(Category c)
        {
            if (c.Children.Count == 0) { Tone(0.6f); TtsText.Say(Describe(c), true); return; }
            _stack.Add(new Level { Cat = c, Idx = 0 });
            Tone(1.3f);
            TtsText.Say(Strings.L(c.LabelKey) + ", " + Describe(Cur), true);
        }

        private static void Back()
        {
            if (_stack.Count <= 1) { Close(); return; }
            _stack.RemoveAt(_stack.Count - 1);
            Tone(0.8f);
            TtsText.Say(Strings.L(Current.LabelKey) + ", " + Describe(Cur), true);
        }

        // --- TTS ---
        private static string Describe(Node n)
        {
            if (n is Category) return Strings.L(n.LabelKey) + ", " + Strings.L("settings.submenu");
            return Strings.L(n.LabelKey) + ", " + ValueText(n);
        }

        // Au reglage d'une valeur on ne reannonce QUE la valeur (le libelle vient d'etre lu
        // a la navigation) -> retour rapide, moins verbeux.
        private static void SayValue(Node n) => TtsText.Say(ValueText(n), true);

        private static string ValueText(Node n)
        {
            if (n is Toggle t) return Strings.L(t.Get() ? "settings.on" : "settings.off");
            if (n is Slider s) return Pct(s.Get()) + " " + Strings.L("settings.percent");
            return "";
        }

        // Pas de Clamp01 : un slider d'amplification peut afficher au-dela de 100 % (ex. 150 %).
        private static string Pct(float v) => Mathf.RoundToInt(Mathf.Max(0f, v) * 100f).ToString();

        // --- Earcons (placeholders : tons generes, l'utilisateur choisira) ---
        private const float EarVol = 0.3f;
        private static void Tone(float pitch) => GameplayAudio.PlayTone(0f, pitch, EarVol);

        // --- Lecture bouton physique par id d'element (template Rewired Gamepad) ---
        private const int IdUp = 16, IdRight = 17, IdDown = 18, IdLeft = 19;
        private const int IdCross = 6, IdCircle = 7, IdBack = 12;

        private static bool Down(Joystick joy, int id)
        {
            for (int i = 0; i < joy.buttonCount; i++)
                if (joy.ButtonElementIdentifiers[i].id == id) return joy.GetButtonDown(i);
            return false;
        }
    }
}
