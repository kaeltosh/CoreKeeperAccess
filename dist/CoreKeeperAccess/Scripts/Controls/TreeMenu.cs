using System;
using System.Collections.Generic;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using Rewired;
using UnityEngine;

namespace CoreKeeperAccess.Controls
{
    // Moteur de menu en ARBRE, entierement TTS, INSTANCIABLE : chaque menu maison du mod
    // (panneau de reglages, menu contextuel, et a venir lecteur de carte / codex) est une
    // INSTANCE de cette classe avec son propre arbre. Avant, la mecanique vivait en statique
    // dans SettingsMenu et n'etait pas partageable ; ActionMenu en redupliquait une copie.
    //
    // MODAL : tant qu'une instance est ouverte (Active), l'input jeu est gele
    // (NativeInputSuppressionPatch teste les Active via InputContext.ModalA11yOpen) et on lit
    // les boutons PHYSIQUES en direct (Rewired). Une seule instance ouverte a la fois en
    // pratique (les combos d'ouverture sont gardes par contexte).
    //
    // Controles (D-pad, menu ouvert, Triangle relache) :
    //  - Haut/Bas : naviguer le niveau courant. CYCLAGE (boucle) + son de BUTEE au
    //    franchissement de la couture (debut<->fin) pour reperer les bords a l'oreille.
    //  - Droite : feuille reglable = +pas / activer ; categorie = entrer. Feuille Action = rien.
    //  - Gauche : feuille reglable = -pas / desactiver ; categorie focalisee = remonter.
    //  - Croix : entrer une categorie / basculer une bascule / executer une Action.
    //  - Rond ou Back : remonter d'un niveau ; a la racine = fermer.
    //  - Triangle (physique) MAINTENU + Haut : apercu sonore de l'option (si elle en a un).
    // Earcons : grammaire de nav PARTAGEE (UiSfx) commune a tous les menus maison.
    internal sealed class TreeMenu
    {
        // --- Modele d'arbre ---
        // Le libelle se resout SOIT par cle i18n (LabelKey, resolue paresseusement -> suit la
        // langue) SOIT par texte deja resolu (LabelText, prioritaire -> menus a contenu
        // dynamique comme le menu contextuel : noms de balises, de POI...).
        // DescKey (optionnel) : courte explication lue APRES le nom a chaque survol.
        // Preview (optionnel) : joue le son de l'option (Triangle + Haut). Null = pas de son.
        internal abstract class Node { public string LabelKey; public string LabelText; public string DescKey; public Action Preview; }

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
            // Raw : annonce la valeur ENTIERE + unite au lieu d'un pourcentage (ex. une
            // portee 2..6, pas un ratio de volume). Defaut false = comportement historique (%).
            public bool Raw;
            // Cle i18n de l'unite pour un slider Raw (ex. "teleport.tiles" = "cases"). Null =
            // retro-compatible avec l'ancien comportement fige sur "cases".
            public string RawUnitKey;
        }

        // Feuille ACTION : execute Run a la Croix. Run null = entree INFORMATIVE (relit le
        // libelle et RESTE ouverte, ex. rappel d'un geste dans le menu d'aide).
        internal sealed class Command : Node { public Action Run; }

        // --- Configuration d'instance ---
        // ClosedKey : cle i18n annoncee a la fermeture (null = fermeture silencieuse).
        // OnTick : appele en tete de chaque Tick (ex. timer d'apercu du sonar cote panneau).
        // OnClose : appele a la fermeture, avant l'earcon (ex. couper un apercu en cours).
        // AutoPreview : si vrai, le son de l'option survolee (Preview) joue AUTOMATIQUEMENT a
        // chaque changement de selection, en plus du Triangle + Haut manuel. Pour le menu
        // d'apprentissage des sons (SoundGuide), ou ecouter le son EST le but.
        private readonly string _closedKey;
        private readonly Action _onTick;
        private readonly Action _onClose;
        private readonly bool _autoPreview;
        // SilentNav : coupe les earcons de navigation du menu (clic/butee/validation). Pour le
        // menu d'apprentissage des sons, ou ces clics se telescoperaient avec le son ecoute (le
        // TTS suffit a porter la navigation).
        private readonly bool _silentNav;

        public TreeMenu(string closedKey = null, Action onTick = null, Action onClose = null, bool autoPreview = false, bool silentNav = false)
        {
            _closedKey = closedKey;
            _onTick = onTick;
            _onClose = onClose;
            _autoPreview = autoPreview;
            _silentNav = silentNav;
        }

        // Earcons de navigation, etouffes en mode SilentNav.
        private void NavEntry() { if (!_silentNav) UiSfx.Entry(); }
        private void NavCycle() { if (!_silentNav) UiSfx.Cycle(); }
        private void NavValidate() { if (!_silentNav) UiSfx.Validate(); }

        // --- Etat ---
        public bool Active { get; private set; }

        private Category _root;

        // Saute l'input de la frame d'OUVERTURE : le bouton qui vient de lancer ce menu (ex.
        // Croix sur "Apprendre les sons" dans le menu d'aide) est encore enfonce cette frame et
        // serait relu ici -> on entrerait direct dans la 1re categorie. Meme parade que PadLearn.
        private bool _justOpened;

        // Pile de navigation : un niveau ouvert = sa categorie + l'index focalise (la position
        // est memorisee quand on entre/ressort, comme l'explorateur Windows).
        private struct Level { public Category Cat; public int Idx; }
        private readonly List<Level> _stack = new List<Level>();

        private Category Current => _stack[_stack.Count - 1].Cat;

        private int Index
        {
            get => _stack[_stack.Count - 1].Idx;
            set { var lv = _stack[_stack.Count - 1]; lv.Idx = value; _stack[_stack.Count - 1] = lv; }
        }

        private Node Cur => Current.Children[Index];

        // --- Ouverture / fermeture ---
        // root = arbre a presenter (sa racine porte le titre, via LabelKey ou LabelText). Le
        // client construit/recycle son arbre avant d'appeler Open (statique pour le panneau,
        // reconstruit a chaque ouverture pour un menu contextuel).
        public void Open(Category root)
        {
            if (Active || root == null) return;
            _root = root;
            Active = true;
            _stack.Clear();
            _stack.Add(new Level { Cat = _root, Idx = 0 });
            _justOpened = true; // ignore le Croix de lancement encore enfonce cette frame
            NavValidate();   // ouverture = entree dans le menu
            TtsText.Say(ResolveLabel(_root) + ", " + Describe(Cur), true);
            AutoPreview();
        }

        public void Close()
        {
            if (!Active) return;
            Active = false;
            _stack.Clear();
            _root = null;
            _onClose?.Invoke();
            NavEntry();   // fermeture
            if (!string.IsNullOrEmpty(_closedKey)) TtsText.Say(Strings.L(_closedKey), true);
        }

        // --- Boucle (tickee tot dans l'Update, comme TextEntry) ---
        public void Tick()
        {
            if (!Active) return;
            _onTick?.Invoke();
            if (!ReInput.isReady) return;
            var joy = ReInput.controllers.GetLastActiveController<Joystick>();
            if (joy == null) return;

            // Frame d'ouverture : on ne lit pas les boutons (le Croix de lancement est encore
            // enfonce et nous ferait entrer dans la 1re entree). Une frame suffit : des la
            // suivante, GetButtonDown du Croix maintenu est faux.
            if (_justOpened) { _justOpened = false; return; }

            if (Down(joy, IdDown)) Move(+1);
            // Triangle (physique) MAINTENU + haut = apercu du son de l'option ; sinon nav.
            else if (Down(joy, IdUp)) { if (Held(joy, TriangleModifier.TriangleButtonId)) PreviewCurrent(); else Move(-1); }
            else if (Down(joy, IdRight)) OnRight();
            else if (Down(joy, IdLeft)) OnLeft();
            else if (Down(joy, IdCross)) OnCross();
            else if (Down(joy, IdCircle) || Down(joy, IdBack)) Back();
        }

        // Apercu sonore (Triangle + Haut) : joue le son de l'option survolee. Null = rien.
        private void PreviewCurrent() => Cur.Preview?.Invoke();

        // Apercu AUTOMATIQUE au changement de selection (mode autoPreview seulement). Joue le
        // son de l'entree qu'on vient de survoler, apres son libelle TTS.
        private void AutoPreview()
        {
            if (!_autoPreview || Current.Children.Count == 0) return;
            Cur.Preview?.Invoke();
        }

        // Navigation haut/bas avec CYCLAGE + son de butee au franchissement de la couture.
        private void Move(int delta)
        {
            var items = Current.Children;
            if (items.Count == 0) return;
            int n = items.Count;
            int ni = Index + delta;
            bool wrapped = false;
            if (ni < 0) { ni = n - 1; wrapped = true; }
            else if (ni >= n) { ni = 0; wrapped = true; }
            Index = ni;
            if (wrapped) NavCycle(); else NavEntry(); // cyclage debut/fin sinon survol d'entree
            TtsText.Say(Describe(items[ni]), true);
            AutoPreview();
        }

        private void OnRight()
        {
            var n = Cur;
            if (n is Slider s) { s.Set(Mathf.Clamp(s.Get() + s.Step, s.Min, s.Max)); NavEntry(); SayValue(s); }
            else if (n is Toggle t) { if (!t.Get()) t.Set(true); NavValidate(); SayValue(t); }
            else if (n is Category c) Enter(c);
            // Command (feuille Action) : droite neutre, elle ne se "regle" pas.
        }

        private void OnLeft()
        {
            var n = Cur;
            if (n is Slider s) { s.Set(Mathf.Clamp(s.Get() - s.Step, s.Min, s.Max)); NavEntry(); SayValue(s); }
            else if (n is Toggle t) { if (t.Get()) t.Set(false); NavValidate(); SayValue(t); }
            else if (n is Category) Back(); // categorie focalisee : gauche = remonter
            // Command : gauche neutre.
        }

        private void OnCross()
        {
            var n = Cur;
            if (n is Category c) Enter(c);
            else if (n is Toggle t) { t.Set(!t.Get()); NavValidate(); SayValue(t); }
            else if (n is Command cmd) Activate(cmd);
            else SayValue(n); // slider : re-annonce la valeur
        }

        private void Activate(Command cmd)
        {
            // Run null = entree informative : relit + reste ouverte (pas de fermeture).
            if (cmd.Run == null) { NavEntry(); TtsText.Say(ResolveLabel(cmd), true); return; }
            // On ferme AVANT d'executer : l'action peut rearmer un input (fermer la carte) qui
            // ne doit plus etre gele par Active.
            Active = false;
            _stack.Clear();
            _root = null;
            NavValidate();   // execution d'une action = validation
            cmd.Run.Invoke();
        }

        private void Enter(Category c)
        {
            if (c.Children.Count == 0) { NavCycle(); TtsText.Say(Describe(c), true); return; }
            _stack.Add(new Level { Cat = c, Idx = 0 });
            NavValidate();   // entrer un sous-menu = validation
            TtsText.Say(ResolveLabel(c) + ", " + Describe(Cur), true);
            AutoPreview();
        }

        private void Back()
        {
            if (_stack.Count <= 1) { Close(); return; }
            _stack.RemoveAt(_stack.Count - 1);
            NavEntry();   // remonter = navigation entre entrees
            TtsText.Say(ResolveLabel(Current) + ", " + Describe(Cur), true);
            AutoPreview();
        }

        // --- TTS ---
        private static string ResolveLabel(Node n)
            => !string.IsNullOrEmpty(n.LabelText) ? n.LabelText : Strings.L(n.LabelKey);

        private static string Describe(Node n)
        {
            string label = ResolveLabel(n);
            string s;
            if (n is Category) s = label + ", " + Strings.L("settings.submenu");
            else if (n is Command) s = label;                  // feuille Action : pas de valeur
            else s = label + ", " + ValueText(n);              // Toggle / Slider
            // Descriptif auto en queue (point separateur -> pause naturelle du TTS). Au reglage
            // d'une valeur on passe par SayValue (valeur seule), donc pas repete.
            if (!string.IsNullOrEmpty(n.DescKey)) s += ". " + Strings.L(n.DescKey);
            return s;
        }

        // Au reglage d'une valeur on ne reannonce QUE la valeur (le libelle vient d'etre lu a
        // la navigation) -> retour rapide, moins verbeux.
        private static void SayValue(Node n) => TtsText.Say(ValueText(n), true);

        private static string ValueText(Node n)
        {
            if (n is Toggle t) return Strings.L(t.Get() ? "settings.on" : "settings.off");
            if (n is Slider s) return s.Raw
                ? Mathf.RoundToInt(s.Get()) + " " + Strings.L(s.RawUnitKey ?? "teleport.tiles")
                : Pct(s.Get()) + " " + Strings.L("settings.percent");
            return "";
        }

        // Pas de Clamp01 : un slider d'amplification peut afficher au-dela de 100 % (ex. 150 %).
        private static string Pct(float v) => Mathf.RoundToInt(Mathf.Max(0f, v) * 100f).ToString();

        // --- Lecture bouton physique par id d'element (template Rewired Gamepad) ---
        private const int IdUp = 16, IdRight = 17, IdDown = 18, IdLeft = 19;
        private const int IdCross = 6, IdCircle = 7, IdBack = 12;

        private static bool Down(Joystick joy, int id)
        {
            for (int i = 0; i < joy.buttonCount; i++)
                if (joy.ButtonElementIdentifiers[i].id == id) return joy.GetButtonDown(i);
            return false;
        }

        // Etat MAINTENU (pas le front) d'un bouton physique : pour le modificateur Triangle de
        // l'apercu. id < 0 (Triangle pas encore capte) -> aucun match -> false.
        private static bool Held(Joystick joy, int id)
        {
            if (id < 0) return false;
            for (int i = 0; i < joy.buttonCount; i++)
                if (joy.ButtonElementIdentifiers[i].id == id) return joy.GetButton(i);
            return false;
        }
    }
}
