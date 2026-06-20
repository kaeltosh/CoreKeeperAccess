using System;
using System.Collections.Generic;
using CoreKeeperAccess.Controls;
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
        // DescKey (optionnel) : courte explication lue APRES le nom a chaque survol (choix
        // utilisateur : noms courts gardes, descriptif auto en plus). Rappelle le raccourci
        // manette quand il en existe un (ex. snap aussi sur Triangle + L3).
        // Preview (optionnel) : joue le son de l'option au volume regle (Triangle + D-pad haut).
        // Null = pas de son (options de comportement : snap, ralenti, ramassage...).
        internal abstract class Node { public string LabelKey; public string DescKey; public Action Preview; }

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
            public float Step;        // increment (0.05 = 5 %)
            public float Min = 0f;    // borne basse (defaut 0 ; > 0 pour un reglage qui ne doit pas s'annuler)
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

        // --- Helpers de declaration (gardent EnsureBuilt lisible) ---
        // Chaque entree porte sa cle de libelle, sa cle de descriptif (desc) lue en queue, et
        // un apercu sonore optionnel (preview) joue sur Triangle + D-pad haut.
        private static Toggle Tg(string key, string desc, Func<bool> get, Action<bool> set, Action preview = null)
            => new Toggle { LabelKey = key, DescKey = desc, Get = get, Set = set, Preview = preview };

        // Slider de volume : toujours pas de 5 %, 0..200 % (amplification). Cas par defaut.
        private static Slider Vol(string key, string desc, Func<float> get, Action<float> set, Action preview = null)
            => new Slider { LabelKey = key, DescKey = desc, Get = get, Set = set, Step = 0.05f, Max = 2f, Preview = preview };

        private static Category Cat(string key, string desc, params Node[] children)
        {
            var c = new Category { LabelKey = key, DescKey = desc };
            c.Children.AddRange(children);
            return c;
        }

        // --- Construction de l'arbre (categories par domaine) ---
        // Volume general et Normalisation restent a la racine (transverses, acces direct) ;
        // le reste se range par domaine. Ajouter un reglage = 1 ligne dans la bonne categorie.
        private static void EnsureBuilt()
        {
            if (_root != null) return;
            _root = new Category { LabelKey = "settings.title" };

            _root.Children.Add(Vol("settings.mastervolume", "settings.desc.mastervolume",
                () => A11ySettings.MasterVolume, A11ySettings.SetMasterVolume,
                () => GameplayAudio.PlayTone(0f, 1f, 0.6f)));

            _root.Children.Add(Cat("settings.cat.navigation", "settings.desc.cat.navigation",
                Tg("settings.stepbeep", "settings.desc.stepbeep", () => A11ySettings.StepBeep, A11ySettings.SetStepBeep,
                    () => GameplayAudio.PlayTone(0f, 1f, A11ySettings.DirectionTickVolume)),
                Vol("settings.directiontick", "settings.desc.directiontick", () => A11ySettings.DirectionTickVolume, A11ySettings.SetDirectionTickVolume,
                    () => GameplayAudio.PlayTone(0f, 1f, A11ySettings.DirectionTickVolume)),
                Tg("settings.snap", "settings.desc.snap", () => A11ySettings.SnapDirectional, A11ySettings.SetSnapDirectional),
                Vol("settings.navvolume", "settings.desc.navvolume", () => A11ySettings.NavigationVolume, A11ySettings.SetNavigationVolume,
                    () => { GameplayAudio.PlaySpatial(SfxID.inventory_select, 0f, 1f, A11ySettings.NavigationVolume); LaserCane.Preview(); }),
                Vol("settings.guidevolume", "settings.desc.guidevolume", () => A11ySettings.GuideVolume, A11ySettings.SetGuideVolume,
                    BeaconGuide.Preview)));

            _root.Children.Add(Cat("settings.cat.sonar", "settings.desc.cat.sonar",
                Tg("settings.sonar", "settings.desc.sonar", () => A11ySettings.ProximitySonar, A11ySettings.SetProximitySonar,
                    () => StartSonarPreview(2)),
                Vol("settings.sonarvolume", "settings.desc.sonarvolume", () => A11ySettings.SonarVolume, A11ySettings.SetSonarVolume,
                    () => StartSonarPreview(2)),
                Vol("settings.sonarmedium", "settings.desc.sonarmedium", () => A11ySettings.SonarVolMedium, A11ySettings.SetSonarVolMedium,
                    () => StartSonarPreview(0)),
                Vol("settings.sonargrave", "settings.desc.sonargrave", () => A11ySettings.SonarVolGrave, A11ySettings.SetSonarVolGrave,
                    () => StartSonarPreview(1)),
                Tg("settings.objectding", "settings.desc.objectding", () => A11ySettings.ObjectDing, A11ySettings.SetObjectDing,
                    ProximitySonar.PreviewDing)));

            _root.Children.Add(Cat("settings.cat.combat", "settings.desc.cat.combat",
                Tg("settings.slowmo", "settings.desc.slowmo", () => A11ySettings.CombatSlowMo, A11ySettings.SetCombatSlowMo),
                new Slider
                {
                    LabelKey = "settings.slowmospeed", DescKey = "settings.desc.slowmospeed",
                    Get = () => A11ySettings.SlowMoSpeed, Set = A11ySettings.SetSlowMoSpeed,
                    Step = 0.05f, Min = 0.3f, Max = 0.7f,
                },
                Tg("settings.sentinel", "settings.desc.sentinel", () => A11ySettings.SentinelEnabled, A11ySettings.SetSentinelEnabled,
                    AggroSentinel.PreviewMob),
                Vol("settings.sentinelvolume", "settings.desc.sentinelvolume", () => A11ySettings.SentinelVolume, A11ySettings.SetSentinelVolume,
                    AggroSentinel.PreviewMob),
                Vol("settings.sentinelbossvolume", "settings.desc.sentinelbossvolume", () => A11ySettings.SentinelBossVolume, A11ySettings.SetSentinelBossVolume,
                    AggroSentinel.PreviewBoss),
                Tg("settings.fire", "settings.desc.fire", () => A11ySettings.FireEnabled, A11ySettings.SetFireEnabled,
                    FireProximity.Preview),
                Vol("settings.firevolume", "settings.desc.firevolume", () => A11ySettings.FireVolume, A11ySettings.SetFireVolume,
                    FireProximity.Preview)));

            _root.Children.Add(Cat("settings.cat.alerts", "settings.desc.cat.alerts",
                Tg("settings.conditionearcons", "settings.desc.conditionearcons", () => A11ySettings.ConditionEarcons, A11ySettings.SetConditionEarcons,
                    () => GameplayAudio.PlayConditionEarcon(false, A11ySettings.ConditionEarconsVolume)),
                Vol("settings.conditionearconsvolume", "settings.desc.conditionearconsvolume", () => A11ySettings.ConditionEarconsVolume, A11ySettings.SetConditionEarconsVolume,
                    () => GameplayAudio.PlayConditionEarcon(false, A11ySettings.ConditionEarconsVolume)),
                Tg("settings.healthalerts", "settings.desc.healthalerts", () => A11ySettings.HealthAlerts, A11ySettings.SetHealthAlerts,
                    () => GameplayAudio.PlayHealthAlert(A11ySettings.HealthAlertsVolume)),
                Vol("settings.healthalertsvolume", "settings.desc.healthalertsvolume", () => A11ySettings.HealthAlertsVolume, A11ySettings.SetHealthAlertsVolume,
                    () => GameplayAudio.PlayHealthAlert(A11ySettings.HealthAlertsVolume)),
                Vol("settings.healthbeatvolume", "settings.desc.healthbeatvolume", () => A11ySettings.HealthBeatVolume, A11ySettings.SetHealthBeatVolume,
                    () => GameplayAudio.PlayHealthCritical(A11ySettings.HealthBeatVolume)),
                new Slider
                {
                    LabelKey = "settings.healththresholdalert", DescKey = "settings.desc.healththresholdalert",
                    Get = () => A11ySettings.HealthAlertThreshold, Set = A11ySettings.SetHealthAlertThreshold,
                    Step = 0.05f, Min = 0.4f, Max = 0.9f,
                    // Apercu = le son joue au franchissement du seuil d'alerte (les bips d'avertissement).
                    Preview = () => GameplayAudio.PlayHealthWarn(A11ySettings.HealthAlertsVolume),
                },
                new Slider
                {
                    LabelKey = "settings.healththresholdcrit", DescKey = "settings.desc.healththresholdcrit",
                    Get = () => A11ySettings.HealthCritThreshold, Set = A11ySettings.SetHealthCritThreshold,
                    Step = 0.05f, Min = 0.1f, Max = 0.35f,
                    // Apercu = le son joue a l'entree en zone critique (la sirene).
                    Preview = () => GameplayAudio.PlayHealthAlert(A11ySettings.HealthAlertsVolume),
                }));

            _root.Children.Add(Cat("settings.cat.pickup", "settings.desc.cat.pickup",
                Tg("settings.pickup", "settings.desc.pickup", () => A11ySettings.PickupAnnounce, A11ySettings.SetPickupAnnounce),
                Tg("settings.pickupblocks", "settings.desc.pickupblocks", () => A11ySettings.PickupFilterBlocks, A11ySettings.SetPickupFilterBlocks),
                Tg("settings.pickuptotal", "settings.desc.pickuptotal", () => A11ySettings.PickupTotal, A11ySettings.SetPickupTotal)));

            _root.Children.Add(Tg("settings.normalize", "settings.desc.normalize",
                () => A11ySettings.NormalizeAudio, A11ySettings.SetNormalizeAudio));
        }

        // --- Ouverture / fermeture ---
        public static void Open()
        {
            if (Active) return;
            EnsureBuilt();
            Active = true;
            _stack.Clear();
            _stack.Add(new Level { Cat = _root, Idx = 0 });
            ClickValidate();   // ouverture = entree dans le panneau
            TtsText.Say(Strings.L("settings.title") + ", " + Describe(Cur), true);
        }

        public static void Close()
        {
            if (!Active) return;
            Active = false;
            _stack.Clear();
            StopSonarPreview();   // coupe une nappe d'apercu encore en cours
            ClickEntry();   // fermeture
            TtsText.Say(Strings.L("settings.closed"), true);
        }

        // --- Boucle (tickee tot dans l'Update, comme TextEntry) ---
        public static void Tick()
        {
            if (!Active) return;
            // Coupe l'apercu sonar (nappe continue) au bout de sa fenetre, meme si aucun bouton.
            if (_sonarStopAt > 0f && Time.unscaledTime >= _sonarStopAt) StopSonarPreview();
            if (!ReInput.isReady) return;
            var joy = ReInput.controllers.GetLastActiveController<Joystick>();
            if (joy == null) return;

            if (Down(joy, IdDown)) Move(+1);
            // Triangle (physique) MAINTENU + haut = apercu du son de l'option ; sinon nav.
            else if (Down(joy, IdUp)) { if (Held(joy, TriangleModifier.TriangleButtonId)) PreviewCurrent(); else Move(-1); }
            else if (Down(joy, IdRight)) OnRight();
            else if (Down(joy, IdLeft)) OnLeft();
            else if (Down(joy, IdCross)) OnCross();
            else if (Down(joy, IdCircle) || Down(joy, IdBack)) Back();
        }

        // --- Apercu sonore (Triangle + D-pad haut) ---
        // Joue le son de l'option survolee au volume regle. Null (option de comportement :
        // snap, ralenti, ramassage...) = rien, choix utilisateur "sans son = sans son".
        private static void PreviewCurrent() => Cur.Preview?.Invoke();

        // Apercu du sonar : nappe CONTINUE -> on l'arme pour une courte fenetre puis on coupe
        // (StopSonarPreview), au Tick ou a la fermeture. which : 0 medium, 1 grave, 2 les deux.
        private const float SonarPreviewDur = 0.9f;
        private static float _sonarStopAt;
        private static void StartSonarPreview(int which)
        {
            ProximitySonar.StartPreview(which);
            _sonarStopAt = Time.unscaledTime + SonarPreviewDur;
        }
        private static void StopSonarPreview()
        {
            if (_sonarStopAt <= 0f) return;
            ProximitySonar.StopPreview();
            _sonarStopAt = 0f;
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
            if (wrapped) ClickCycle(); else ClickEntry(); // cyclage debut/fin sinon survol d'entree
            TtsText.Say(Describe(items[ni]), true);
        }

        private static void OnRight()
        {
            var n = Cur;
            if (n is Slider s) { s.Set(Mathf.Clamp(s.Get() + s.Step, s.Min, s.Max)); ClickEntry(); SayValue(s); }
            else if (n is Toggle t) { if (!t.Get()) t.Set(true); ClickValidate(); SayValue(t); }
            else if (n is Category c) Enter(c);
        }

        private static void OnLeft()
        {
            var n = Cur;
            if (n is Slider s) { s.Set(Mathf.Clamp(s.Get() - s.Step, s.Min, s.Max)); ClickEntry(); SayValue(s); }
            else if (n is Toggle t) { if (t.Get()) t.Set(false); ClickValidate(); SayValue(t); }
            else Back(); // categorie focalisee : gauche = remonter
        }

        private static void OnCross()
        {
            var n = Cur;
            if (n is Category c) Enter(c);
            else if (n is Toggle t) { t.Set(!t.Get()); ClickValidate(); SayValue(t); }
            else SayValue(n); // slider : re-annonce la valeur
        }

        private static void Enter(Category c)
        {
            if (c.Children.Count == 0) { ClickCycle(); TtsText.Say(Describe(c), true); return; }
            _stack.Add(new Level { Cat = c, Idx = 0 });
            ClickValidate();   // entrer un sous-menu = validation
            TtsText.Say(Strings.L(c.LabelKey) + ", " + Describe(Cur), true);
        }

        private static void Back()
        {
            if (_stack.Count <= 1) { Close(); return; }
            _stack.RemoveAt(_stack.Count - 1);
            ClickEntry();   // remonter = navigation entre entrees
            TtsText.Say(Strings.L(Current.LabelKey) + ", " + Describe(Cur), true);
        }

        // --- TTS ---
        private static string Describe(Node n)
        {
            string s = n is Category
                ? Strings.L(n.LabelKey) + ", " + Strings.L("settings.submenu")
                : Strings.L(n.LabelKey) + ", " + ValueText(n);
            // Descriptif auto en queue (point separateur -> pause naturelle du TTS). Au
            // REGLAGE d'une valeur on passe par SayValue (valeur seule), donc pas repete.
            if (!string.IsNullOrEmpty(n.DescKey)) s += ". " + Strings.L(n.DescKey);
            return s;
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

        // --- Earcons du panneau (sons natifs du jeu, choix utilisateur) ---
        // Trois sons DISTINCTS, un par type d'action (pitchDev = 0 : pas de random pitch) :
        //  - naviguer entre les entrees (haut/bas, remonter, fermer) + cran de slider = FIXME_menu_select (roue) ;
        //  - CYCLAGE : revenir au debut / aller a la fin de la liste = switch_click ;
        //  - valider / entrer un sous-menu / bascule / ouvrir = inventory_select.
        // Joues via GameplayAudio.PlayUi : NORMALISES (3 clics natifs tres inegaux) ET pilotes
        // par le volume general (le master gouverne tout ; le TTS NVDA porte la nav si maitre bas).
        private static void ClickEntry() => GameplayAudio.PlayUi(SfxID.FIXME_menu_select);
        private static void ClickCycle() => GameplayAudio.PlayUi(SfxID.switch_click_1_01);
        private static void ClickValidate() => GameplayAudio.PlayUi(SfxID.inventory_select);

        // --- Lecture bouton physique par id d'element (template Rewired Gamepad) ---
        private const int IdUp = 16, IdRight = 17, IdDown = 18, IdLeft = 19;
        private const int IdCross = 6, IdCircle = 7, IdBack = 12;

        private static bool Down(Joystick joy, int id)
        {
            for (int i = 0; i < joy.buttonCount; i++)
                if (joy.ButtonElementIdentifiers[i].id == id) return joy.GetButtonDown(i);
            return false;
        }

        // Etat MAINTENU (pas le front) d'un bouton physique : pour le modificateur Triangle
        // de l'apercu. id < 0 (Triangle pas encore capte) -> aucun match -> false.
        private static bool Held(Joystick joy, int id)
        {
            if (id < 0) return false;
            for (int i = 0; i < joy.buttonCount; i++)
                if (joy.ButtonElementIdentifiers[i].id == id) return joy.GetButton(i);
            return false;
        }
    }
}
