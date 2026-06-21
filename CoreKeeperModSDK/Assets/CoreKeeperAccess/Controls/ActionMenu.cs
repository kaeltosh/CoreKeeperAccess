using System;
using System.Collections.Generic;

namespace CoreKeeperAccess.Controls
{
    // Menu contextuel MAISON, entierement TTS (aucun asset Unity). Ouvert sur un element (de
    // la carte via Croix, ou le menu d'aide) : il presente une liste d'ACTIONS adaptee, ce qui
    // libere le moteur de keymaps (plus besoin d'un combo Triangle par action).
    //
    // Depuis le 21 juin 2026, ce n'est plus qu'un CLIENT du moteur generique TreeMenu
    // (Controls/TreeMenu.cs), partage avec le panneau de reglages : ActionMenu se contente de
    // traduire ses Item en feuilles Action (Command) et de deleguer toute la mecanique
    // (navigation, earcons, modal, lecture manette en direct). Une liste plate de feuilles
    // Action, sans sous-menus ni reglages.
    //
    // Controles (D-pad, menu ouvert, Triangle relache) :
    //  - Haut/Bas : naviguer. CYCLAGE + son de BUTEE au franchissement de la couture.
    //  - Croix : executer l'action focalisee (puis fermeture). Item informatif (Run null) =
    //    relit le libelle sans fermer.
    //  - Rond ou Back : fermer sans rien faire.
    // (Nomme ActionMenu et non ContextMenu pour ne pas heurter l'attribut UnityEngine.ContextMenu.)
    internal static class ActionMenu
    {
        internal struct Item
        {
            public string Label;   // libelle TTS (deja resolu)
            public Action Run;      // action a executer ; null = entree informative (relit, reste ouvert)
        }

        // Pas d'annonce de fermeture, pas de hook : un menu contextuel se ferme en silence
        // (l'action lancee, ou l'earcon de cyclage, suffisent au repere).
        private static readonly TreeMenu _menu = new TreeMenu();

        public static bool Active => _menu.Active;

        // Ouvre le menu sur une liste d'items non vide. Le titre et les libelles sont DEJA
        // resolus (LabelText) : le menu contextuel a un contenu dynamique (nom de balise, de
        // POI...) qui ne passe pas par une cle i18n fixe.
        public static void Open(string title, List<Item> items)
        {
            if (items == null || items.Count == 0) return;
            var root = new TreeMenu.Category { LabelText = title ?? "" };
            foreach (var it in items)
                root.Children.Add(new TreeMenu.Command { LabelText = it.Label, Run = it.Run });
            _menu.Open(root);
        }

        public static void Close() => _menu.Close();

        // A ticker tot dans l'Update, AVANT les consommateurs de carte / D-pad.
        public static void Tick() => _menu.Tick();
    }
}
