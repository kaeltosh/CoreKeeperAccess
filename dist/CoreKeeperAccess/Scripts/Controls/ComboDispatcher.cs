using System;
using System.Collections.Generic;
using CoreKeeperAccess.Localization;

namespace CoreKeeperAccess.Controls
{
    // Dispatcher declaratif des combos de la touche access (moteur de keymaps v1).
    // Les fournisseurs enregistrent (combo, garde de contexte, action) au boot ;
    // chaque frame, pour chaque combo declenche par InfoKey, la PREMIERE entree
    // dont la garde passe gagne (priorite = ordre d'enregistrement). Remplace le
    // polling des drapeaux InfoKey disperse dans les consommateurs.
    //
    // Chemin chaud sans allocation : la table (delegues compris) est construite
    // une fois a l'enregistrement, le tick ne fait que des lectures.
    internal static class ComboDispatcher
    {
        internal enum Combo
        {
            Detail,    // Triangle + haut
            Right,     // Triangle + droite
            Down,      // Triangle + bas
            Left,      // Triangle + gauche
            BumperL,   // Triangle + L1
            BumperR,   // Triangle + R1 (pivoter / changer taille de zone)
            DoubleTap, // double-tap bref de Triangle
            LeftStick, // Triangle + L3 (toggle direction assistee)
            Back,      // Triangle + Back (ouvrir le panneau de reglages)
        }

        private struct Entry
        {
            public Func<bool> When;
            public Action Run;
            public string LabelKey;   // cle i18n du libelle (pour le menu d'aide)
        }

        private static readonly List<Entry>[] _table = BuildTable();

        private static List<Entry>[] BuildTable()
        {
            var t = new List<Entry>[9];
            for (int i = 0; i < t.Length; i++) t[i] = new List<Entry>(4);
            return t;
        }

        public static void Register(Combo combo, Func<bool> when, Action run, string labelKey)
        {
            _table[(int)combo].Add(new Entry { When = when, Run = run, LabelKey = labelKey });
        }

        // AJOUTE (n'efface pas) a la liste les commandes ACTIVES ici et maintenant : pour
        // chaque combo, la PREMIERE entree dont la garde passe (meme priorite que Fire). Le
        // geste est COMPOSE via Glyphs -> il suit le reglage PS/Xbox comme tout le menu d'aide.
        public static void CollectActive(List<HelpItem> outList)
        {
            for (int c = 0; c < _table.Length; c++)
            {
                var entries = _table[c];
                for (int i = 0; i < entries.Count; i++)
                {
                    if (!entries[i].When()) continue;
                    outList.Add(new HelpItem
                    {
                        Gesture = ComboGesture((Combo)c),
                        Label = Strings.L(entries[i].LabelKey),
                        Run = entries[i].Run,
                    });
                    break; // un seul libelle par combo
                }
            }
        }

        // Libelle du combo "touche access + X", compose via Glyphs (touche access = bouton
        // FaceUp = Triangle en PS / Y en Xbox).
        private static string ComboGesture(Combo c)
        {
            switch (c)
            {
                case Combo.Detail: return Glyphs.Combo(Btn.FaceUp, Btn.Up);
                case Combo.Right: return Glyphs.Combo(Btn.FaceUp, Btn.Right);
                case Combo.Down: return Glyphs.Combo(Btn.FaceUp, Btn.Down);
                case Combo.Left: return Glyphs.Combo(Btn.FaceUp, Btn.Left);
                case Combo.BumperL: return Glyphs.Combo(Btn.FaceUp, Btn.L1);
                case Combo.BumperR: return Glyphs.Combo(Btn.FaceUp, Btn.R1);
                case Combo.LeftStick: return Glyphs.Combo(Btn.FaceUp, Btn.L3);
                case Combo.Back: return Glyphs.Combo(Btn.FaceUp, Btn.Back);
                case Combo.DoubleTap: return Strings.L("combo.doubletapprefix") + " " + Glyphs.Name(Btn.FaceUp);
                default: return "";
            }
        }

        // A appeler en FIN d'Update du mod, apres le tick de tous les modules : les
        // gardes lisent alors des etats frais (curseur detache, nav inventaire...).
        public static void Tick()
        {
            // Un modal a11y est ouvert (reglages, menu contextuel/aide, saisie, mode decouverte) :
            // il a pris la main sur la manette, aucun combo touche access ne doit partir.
            if (InputContext.ModalA11yOpen) return;
            if (InfoKey.DetailRequested) Fire(Combo.Detail);
            if (InfoKey.ComboRight) Fire(Combo.Right);
            if (InfoKey.ComboDown) Fire(Combo.Down);
            if (InfoKey.ComboLeft) Fire(Combo.Left);
            if (InfoKey.ComboLB) Fire(Combo.BumperL);
            if (InfoKey.ComboR1) Fire(Combo.BumperR);
            if (InfoKey.ComboL3) Fire(Combo.LeftStick);
            if (InfoKey.ComboBack) Fire(Combo.Back);
            if (InfoKey.DoubleTapped) Fire(Combo.DoubleTap);
            // Menu d'aide : double-tap du D-pad haut sous Triangle. Special (global,
            // s'auto-exclut de l'enumeration) -> appel direct, pas un binding de contexte.
            if (InfoKey.RecallRequested) HelpMenu.Show();
        }

        private static void Fire(Combo combo)
        {
            var entries = _table[(int)combo];
            for (int i = 0; i < entries.Count; i++)
            {
                if (!entries[i].When()) continue;
                entries[i].Run();
                return;
            }
            // Aucun contexte preneur : le combo ne dit rien, il n'existe pas
            // (regle de design gravee, cf. fiche touche access).
        }
    }
}
