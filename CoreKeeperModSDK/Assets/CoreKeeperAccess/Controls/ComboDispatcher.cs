using System;
using System.Collections.Generic;

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
        }

        private struct Entry
        {
            public Func<bool> When;
            public Action Run;
        }

        private static readonly List<Entry>[] _table = BuildTable();

        private static List<Entry>[] BuildTable()
        {
            var t = new List<Entry>[8];
            for (int i = 0; i < t.Length; i++) t[i] = new List<Entry>(4);
            return t;
        }

        public static void Register(Combo combo, Func<bool> when, Action run)
        {
            _table[(int)combo].Add(new Entry { When = when, Run = run });
        }

        // A appeler en FIN d'Update du mod, apres le tick de tous les modules : les
        // gardes lisent alors des etats frais (curseur detache, nav inventaire...).
        public static void Tick()
        {
            if (InfoKey.DetailRequested) Fire(Combo.Detail);
            if (InfoKey.ComboRight) Fire(Combo.Right);
            if (InfoKey.ComboDown) Fire(Combo.Down);
            if (InfoKey.ComboLeft) Fire(Combo.Left);
            if (InfoKey.ComboLB) Fire(Combo.BumperL);
            if (InfoKey.ComboR1) Fire(Combo.BumperR);
            if (InfoKey.ComboL3) Fire(Combo.LeftStick);
            if (InfoKey.DoubleTapped) Fire(Combo.DoubleTap);
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
