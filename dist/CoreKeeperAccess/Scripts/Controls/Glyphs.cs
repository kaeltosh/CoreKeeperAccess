using CoreKeeperAccess.Gameplay;
using CoreKeeperAccess.Localization;

namespace CoreKeeperAccess.Controls
{
    // Boutons LOGIQUES de la manette (independants de la nomenclature). Une seule source de
    // verite pour TOUS les libelles de boutons du menu d'aide (combos, gestes, commandes
    // vanilla) : Glyphs.Name() les traduit selon le reglage A11ySettings.XboxButtons
    // (PlayStation par defaut). Bascule PS/Xbox coherente d'un coup.
    internal enum Btn
    {
        FaceDown, FaceRight, FaceLeft, FaceUp,   // Croix/Rond/Carre/Triangle = A/B/X/Y
        L1, R1, L2, R2, L3, R3,
        Back, Start,
        Dpad, Up, Down, Left, Right,
        StickLeft, StickRight,
    }

    internal static class Glyphs
    {
        // Libelle d'un bouton logique selon la nomenclature reglee. Les boutons "face",
        // bumpers, gachettes, D-pad et Back/Start changent de nom ; directions/sticks/clics
        // sont identiques (memes mots en PS et Xbox).
        public static string Name(Btn b)
        {
            string s = A11ySettings.XboxButtons ? "xbox" : "ps";
            switch (b)
            {
                case Btn.FaceDown: return Strings.L("btn." + s + ".facedown");
                case Btn.FaceRight: return Strings.L("btn." + s + ".faceright");
                case Btn.FaceLeft: return Strings.L("btn." + s + ".faceleft");
                case Btn.FaceUp: return Strings.L("btn." + s + ".faceup");
                case Btn.L1: return Strings.L("btn." + s + ".l1");
                case Btn.R1: return Strings.L("btn." + s + ".r1");
                case Btn.L2: return Strings.L("btn." + s + ".l2");
                case Btn.R2: return Strings.L("btn." + s + ".r2");
                case Btn.Back: return Strings.L("btn." + s + ".back");
                case Btn.Start: return Strings.L("btn." + s + ".start");
                case Btn.Dpad: return Strings.L("btn." + s + ".dpad");
                case Btn.L3: return Strings.L("btn.l3");
                case Btn.R3: return Strings.L("btn.r3");
                // Directions du D-pad : on prefixe le nom du D-pad (regle PS/Xbox) -> "Croix
                // directionnelle haut" / "D-pad haut". Leve l'ambiguite avec le stick dans les
                // combos ("Triangle plus croix directionnelle haut") et l'apprentissage.
                case Btn.Up: return Strings.L("btn." + s + ".dpad") + " " + Strings.L("btn.up");
                case Btn.Down: return Strings.L("btn." + s + ".dpad") + " " + Strings.L("btn.down");
                case Btn.Left: return Strings.L("btn." + s + ".dpad") + " " + Strings.L("btn.left");
                case Btn.Right: return Strings.L("btn." + s + ".dpad") + " " + Strings.L("btn.right");
                case Btn.StickLeft: return Strings.L("btn.stickleft");
                case Btn.StickRight: return Strings.L("btn.stickright");
                default: return "";
            }
        }

        // Combo "touche access + X" : "Triangle plus haut", "Y plus L1"...
        public static string Combo(Btn modifier, Btn other)
            => Name(modifier) + " " + Strings.L("btn.plus") + " " + Name(other);

        // Paire "A et B" (ex. "L1 et R1").
        public static string Pair(Btn a, Btn b)
            => Name(a) + " " + Strings.L("btn.and") + " " + Name(b);

        // Bouton logique correspondant a un nom d'element Rewired (templates Xbox ET Sony) :
        // traduit les bindings VANILLA lus en direct. null si inconnu (sera ignore).
        public static Btn? FromRewired(string el)
        {
            if (string.IsNullOrEmpty(el)) return null;
            switch (el.Trim().ToLowerInvariant())
            {
                case "a": case "cross": return Btn.FaceDown;
                case "b": case "circle": return Btn.FaceRight;
                case "x": case "square": return Btn.FaceLeft;
                case "y": case "triangle": return Btn.FaceUp;
                case "left bumper": case "left shoulder": case "l1": return Btn.L1;
                case "right bumper": case "right shoulder": case "r1": return Btn.R1;
                case "left trigger": case "l2": return Btn.L2;
                case "right trigger": case "r2": return Btn.R2;
                case "left stick button": case "left stick": case "l3": return Btn.L3;
                case "right stick button": case "right stick": case "r3": return Btn.R3;
                case "back": case "view": case "select": case "share": case "create": return Btn.Back;
                case "start": case "menu": case "options": return Btn.Start;
                case "d-pad up": case "dpad up": case "up": return Btn.Up;
                case "d-pad down": case "dpad down": case "down": return Btn.Down;
                case "d-pad left": case "dpad left": case "left": return Btn.Left;
                case "d-pad right": case "dpad right": case "right": return Btn.Right;
                default: return null;
            }
        }
    }
}
