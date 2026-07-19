using System;
using System.Collections.Generic;
using CoreKeeperAccess.Localization;

namespace CoreKeeperAccess.Controls
{
    // Une entree du menu d'aide : libelle de geste + libelle d'action, tous deux DEJA resolus,
    // + action optionnelle. Run null = entree DESCRIPTIVE (rappel d'un geste continu, non
    // lancable depuis un menu). Run non null = commande EXECUTABLE (Croix la lance).
    internal struct HelpItem
    {
        public string Gesture;
        public string Label;
        public Action Run;
    }

    // Catalogue COMPLET des commandes du mod par contexte. Source du menu d'aide, en trois
    // couches fusionnees et filtrees par contexte :
    //  1. gestes/modes DESCRIPTIFS declares ici (libelles de boutons via Glyphs -> bascule
    //     PS/Xbox automatique) ;
    //  2. combos EXECUTABLES du registre ComboDispatcher (Triangle + ...) ;
    //  3. commandes VANILLA du jeu, lues sur le vrai mapping Rewired (VanillaControls).
    internal static class HelpCatalog
    {
        private struct Entry { public Func<bool> When; public Func<string> Gesture; public string LabelKey; }

        private static readonly List<Entry> _entries = Build();

        private static void Add(List<Entry> e, Func<bool> when, Func<string> gesture, string label)
            => e.Add(new Entry { When = when, Gesture = gesture, LabelKey = label });

        private static List<Entry> Build()
        {
            var e = new List<Entry>();

            // --- Jeu libre (exploration / curseur de tuile) ---
            Add(e, () => InputContext.InGameFree, () => Glyphs.Name(Btn.StickRight), "help.laser");
            Add(e, () => InputContext.InGameFree, () => Glyphs.Name(Btn.Dpad), "help.tilecursor");
            Add(e, () => InputContext.InGameFree, () => Glyphs.Name(Btn.FaceDown), "help.mine");
            Add(e, () => InputContext.InGameFree, () => Glyphs.Name(Btn.L2), "help.place");
            Add(e, () => InputContext.InGameFree, () => Glyphs.Combo(Btn.FaceUp, Btn.StickLeft), "help.statswheel");

            // --- Inventaire / fenetre perso ---
            Add(e, () => InputContext.InventoryNavActive, () => Glyphs.Name(Btn.Dpad), "help.invnav");
            Add(e, () => InputContext.InventoryNavActive, () => Glyphs.Pair(Btn.L1, Btn.R1), "help.invsection");
            Add(e, () => InputContext.InventoryNavActive, () => Glyphs.Name(Btn.FaceDown), "help.invtake");
            Add(e, () => InputContext.InventoryNavActive, () => Glyphs.Name(Btn.R2), "help.invquickmove");
            Add(e, () => InputContext.InventoryNavActive, () => Glyphs.Name(Btn.L2), "help.invdrop");
            Add(e, () => InputContext.InventoryNavActive, () => Glyphs.Combo(Btn.FaceUp, Btn.L2), "help.invtrash");
            Add(e, () => InputContext.InventoryNavActive, () => Glyphs.Name(Btn.StickLeft), "help.actionwheel");

            // --- Carte (le menu central) ---
            Add(e, () => InputContext.MapOpen, () => Glyphs.Name(Btn.Dpad), "help.mapnav");
            Add(e, () => InputContext.MapOpen, () => Glyphs.Pair(Btn.L1, Btn.R1), "help.mapcategory");
            Add(e, () => InputContext.MapOpen, () => Glyphs.Name(Btn.FaceDown), "help.mapaction");
            Add(e, () => InputContext.MapOpen, () => Glyphs.Name(Btn.Dpad), "help.journalopen");

            // --- Menu natif du jeu (pause, options...) ---
            Add(e, () => InputContext.MenuOpen, () => Glyphs.Name(Btn.Dpad), "help.menunav");
            Add(e, () => InputContext.MenuOpen, () => Glyphs.Name(Btn.FaceDown), "help.menuvalidate");

            return e;
        }

        // Remplit la liste des commandes pertinentes ICI ET MAINTENANT : gestes du mod, puis
        // combos executables, puis commandes vanilla du jeu.
        public static void Collect(List<HelpItem> outList)
        {
            outList.Clear();
            // Toujours en TETE : (re)lancer le mode decouverte de la manette (executable).
            outList.Add(new HelpItem { Gesture = "", Label = Strings.L("help.learn"), Run = PadLearn.Start });
            // Juste apres : ouvrir le menu d'apprentissage des sons du mod.
            outList.Add(new HelpItem { Gesture = "", Label = Strings.L("sound.learn"), Run = SoundGuide.Start });
            // Puis : mode commandes (appuyer -> annonce ce que ça fait, sans l'exécuter).
            outList.Add(new HelpItem { Gesture = "", Label = Strings.L("cmdlearn.entry"), Run = CommandLearn.Start });
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].When())
                    outList.Add(new HelpItem { Gesture = _entries[i].Gesture(), Label = Strings.L(_entries[i].LabelKey), Run = null });

            ComboDispatcher.CollectActive(outList);
            VanillaControls.Collect(outList);
        }
    }
}
