using System.Collections.Generic;
using UnityEngine;

namespace CoreKeeperAccess.Patches
{
    // Generation EN DIRECT d'un nom de couleur francais a partir de la palette RGB reelle
    // d'une variante (champ List<Color> du ReplacementColorDataBlock, lu a la volee). Pas de
    // table figee -> survit aux patches du jeu. 1re passe : teinte dominante via HSV.
    //
    // - Peau : rampe de tons chair -> on nomme la CLARTE (claire/mate/foncee), pas une teinte.
    // - Cheveux/yeux/chemise/pantalon : on prend la teinte la plus VIVE de la palette (les
    //   palettes contiennent souvent une couleur d'ombre fixe + des reflets) et on la nomme.
    internal static class ColorNamer
    {
        public static string Name(CharacterCustomizationMenu.CustomizableBodyPartType type, List<Color> palette)
        {
            if (palette == null || palette.Count == 0) return null;
            if (type == CharacterCustomizationMenu.CustomizableBodyPartType.SkinColor)
                return SkinTone(palette);
            return HueName(MostVivid(palette));
        }

        // Ton de peau par luminance moyenne de la rampe (4 buckets distincts cales sur les
        // 4 teintes de peau du jeu).
        private static string SkinTone(List<Color> palette)
        {
            float sum = 0f;
            foreach (var c in palette) sum += Lum(c);
            float l = sum / palette.Count;
            if (l > 0.68f) return "très claire";
            if (l > 0.52f) return "claire";
            if (l > 0.38f) return "mate";
            if (l > 0.24f) return "foncée";
            return "très foncée";
        }

        // Couleur la plus "vive" (saturation x valeur max) : ecarte les tons d'ombre ternes et
        // capte la teinte identitaire de la palette.
        private static Color MostVivid(List<Color> palette)
        {
            Color best = palette[0];
            float bestScore = -1f;
            foreach (var c in palette)
            {
                Color.RGBToHSV(c, out _, out float s, out float v);
                float score = s * v;
                if (score > bestScore) { bestScore = score; best = c; }
            }
            return best;
        }

        private static float Lum(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

        private static string HueName(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);

            // Quasi sans saturation -> niveaux de gris.
            if (s < 0.15f)
            {
                if (v < 0.12f) return "noir";
                if (v < 0.30f) return "gris foncé";
                if (v < 0.65f) return "gris";
                if (v < 0.88f) return "gris clair";
                return "blanc";
            }

            float deg = h * 360f;
            string hue;
            if (deg < 15f || deg >= 345f) hue = "rouge";
            else if (deg < 45f) hue = (v < 0.60f) ? "marron" : "orange";
            else if (deg < 70f) hue = "jaune";
            else if (deg < 90f) hue = "vert clair";
            else if (deg < 160f) hue = "vert";
            else if (deg < 200f) hue = "turquoise";
            else if (deg < 255f) hue = "bleu";
            else if (deg < 290f) hue = "violet";
            else hue = "rose";

            // Nuance de clarte (sauf marron, deja "sombre" par definition).
            if (hue != "marron")
            {
                if (v < 0.30f) hue += " foncé";
                else if (v > 0.85f && s < 0.55f) hue += " pâle";
            }
            return hue;
        }
    }
}
