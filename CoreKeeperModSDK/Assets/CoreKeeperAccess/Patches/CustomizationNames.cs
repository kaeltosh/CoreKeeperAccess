using System.Collections.Generic;
using CoreKeeperAccess.Localization;

namespace CoreKeeperAccess.Patches
{
    // Noms enrichis des variantes de customisation perso, accroches a l'ADRESSE GUID de
    // chaque variante (cle STABLE entre patches du jeu, vs l'index positionnel qui peut se
    // decaler). La table mappe GUID -> CLE i18n ; le libelle reel vit dans les JSON de
    // localisation (Conf/Localization/<lang>.json, cles custom.*) et est resolu par
    // Strings.L -> traduisible comme le reste du mod, sans recompiler. Le commentaire en
    // regard rappelle le libelle francais et le numero de variante (filet "variante N / M"
    // toujours annonce en complement). Cles GUID en minuscules (compare avec
    // DataBlockAddress.ToString().ToLowerInvariant()).
    internal static class CustomizationNames
    {
        private static readonly Dictionary<string, string> Keys = new Dictionary<string, string>
        {
            // --- Corps : nomme par la CARRURE, pas le sexe (aucun attribut genre visible ;
            //     decision utilisateur 22 juin). Le jeu les nomme "MaleBody"/"FemaleBody". ---
            { "a6a9870f-41e8-98b4-9bc7-1cc146f1687d", "custom.body.1" }, // Robuste (MaleBody, var 1, large)
            { "5e9ee05b-3247-6fb4-5b24-a4e4af303b55", "custom.body.2" }, // Svelte  (FemaleBody, var 2, fine)

            // --- Cheveux (Hair, 24 variantes) ---
            { "ad057198-5346-7884-eb9b-da8718f93702", "custom.hair.1" },  // Carré bouclé
            { "436612ae-9b61-ad34-0890-020ebbab1b08", "custom.hair.2" },  // Bol lisse
            { "013694cd-cda7-46f4-19e0-1a69c7f5e508", "custom.hair.3" },  // Chauve
            { "04fb9341-6fe3-e3c4-c885-dc1ed42b571c", "custom.hair.4" },  // Houppette
            { "6897970a-711a-9a84-59ab-8597b19f8635", "custom.hair.5" },  // Hérissé
            { "7f2c02c0-1ce3-2cd4-6b4a-33b4e45f298c", "custom.hair.6" },  // Ondulé volumineux
            { "5133051f-794a-6264-bb64-083ea6598ac3", "custom.hair.7" },  // Rideau lisse
            { "ac175fc3-111f-0614-69aa-95c5e77c93f4", "custom.hair.8" },  // Chignon
            { "56531129-a9d8-a194-4ae0-89da8cc383bf", "custom.hair.9" },  // Bouclé rond
            { "9833d741-4e28-7cb4-ca7b-84097ec21e67", "custom.hair.10" }, // Volume bombé
            { "91526fa1-c458-6a74-8867-c5de1646ef7f", "custom.hair.11" }, // Crâne dégarni
            { "b027db0b-30f4-9464-da19-02d4290b740a", "custom.hair.12" }, // Épis dressés
            { "99271261-1ed6-20e4-8900-4c7fa3c6fec1", "custom.hair.13" }, // Bol à mèche gauche
            { "3c008ea0-0591-c954-0b1d-302962cf9fd6", "custom.hair.14" }, // Calot (incertain)
            { "3cde4d32-417d-31e4-2811-1240e8791bec", "custom.hair.15" }, // Macarons
            { "03db6586-7774-9084-2b80-49d43c12e79f", "custom.hair.16" }, // Long bouclé
            { "eae7890f-856b-3994-cba9-9b0e02d4f088", "custom.hair.17" }, // Mi-long ondulé
            { "cfe8dc97-88df-bc54-fb69-fdf7c63bca24", "custom.hair.18" }, // Catogan
            { "c5e4a4ee-0474-4634-58aa-0e89208e9c9d", "custom.hair.19" }, // Court à frange
            { "424416a1-ef6d-c9b4-683f-75da5849a2bb", "custom.hair.20" }, // Couettes hautes (incertain)
            { "f08fcb4f-bf43-d9b4-ea7c-cc9dc3f4e00a", "custom.hair.21" }, // Bol serré
            { "70521257-e669-7ad4-8b78-839765b36fb1", "custom.hair.22" }, // Mi-long encadrant
            { "5c936d4c-c4d3-b094-cb26-c90f4306935c", "custom.hair.23" }, // Court à toupet
            { "578a7d7c-caa4-2ad4-495c-99f2a2a13433", "custom.hair.24" }, // Calot lisse (incertain)

            // --- Peau (SkinColor, 4) ---
            { "90cec489-adc0-7244-9a6d-e2dc969b5e05", "custom.skin.1" }, // claire
            { "15c8529a-ad69-7854-a91e-4335bf340b0d", "custom.skin.2" }, // mate
            { "25529cf7-7dd2-1644-f8e4-6cea61dd9e8c", "custom.skin.3" }, // foncée
            { "cc76bde3-dfec-f734-da14-e254e54d3383", "custom.skin.4" }, // très claire

            // --- Couleur des cheveux (HairColor, 19) ---
            { "ba4e4c2f-2ee5-13f4-ea28-bb4a2ea43822", "custom.haircolor.1" },  // châtain
            { "159e9efb-7b22-98e4-7944-55ed67d8f293", "custom.haircolor.2" },  // blond
            { "f53470cd-79aa-a514-e804-4c94cc076741", "custom.haircolor.3" },  // bleu glacier
            { "605f638d-0724-2c94-a84c-c82253c05760", "custom.haircolor.4" },  // noir
            { "564bf265-cbe1-8564-aa0c-34706cc17a5b", "custom.haircolor.5" },  // roux
            { "cd9949e0-7169-0784-5b10-8739343e536f", "custom.haircolor.6" },  // vieux rose
            { "4f3a99cc-bccc-ec94-ca74-c2ba32168d9e", "custom.haircolor.7" },  // violet
            { "613e1c7c-05d5-4ac4-69a4-4e575384ee38", "custom.haircolor.8" },  // bleu
            { "21830409-baab-c5e4-0a43-faddb43683b8", "custom.haircolor.9" },  // vert
            { "ec27703f-a5d5-cfa4-5b13-a81d22e098e2", "custom.haircolor.10" }, // blanc
            { "551dcea2-0e9a-1644-e999-5b15636be83d", "custom.haircolor.11" }, // cuivré
            { "6a42dc17-7f39-b784-7930-2ba85996d0c6", "custom.haircolor.12" }, // magenta
            { "51af5578-d268-2304-c99c-efa881e11e0b", "custom.haircolor.13" }, // bleu ciel
            { "d55c85fc-f1e9-a9a4-58e5-9035b15a2af1", "custom.haircolor.14" }, // vert pomme
            { "5571f386-2137-b904-6820-35640e1cfd67", "custom.haircolor.15" }, // châtain clair
            { "066b3d95-5d2d-e7c4-1ae5-11a74aff2773", "custom.haircolor.16" }, // gris
            { "ee431743-eebe-d824-78c1-aa08bb33cba0", "custom.haircolor.17" }, // rouge
            { "49adccf9-e645-1894-caec-669abb06ccda", "custom.haircolor.18" }, // turquoise
            { "53b70d70-3bca-5364-7af3-ba8f479aac41", "custom.haircolor.19" }, // fuchsia

            // --- Yeux (EyeColor, 14) ---
            { "2f1e0497-bb80-3a94-7815-c9be99fa700c", "custom.eyes.1" },  // bleu
            { "71332930-e150-ead4-f8a2-6adb25283f76", "custom.eyes.2" },  // marron
            { "3cb73e82-c05a-42d4-8b25-e5a82639cdfc", "custom.eyes.3" },  // violet
            { "f2ac8d4f-abf8-6894-69cb-65a4e48f6c3a", "custom.eyes.4" },  // vert
            { "ce5bdf89-8a1f-be94-bbf7-36ee7ece6ba0", "custom.eyes.5" },  // rouge
            { "fd13f699-57fa-5144-59b9-a59dd9506fb0", "custom.eyes.6" },  // vieux rose
            { "5998eb5b-fcc4-2f84-aac1-4d7fe1b23bf3", "custom.eyes.7" },  // bleu ciel
            { "c651cc8b-c4c0-8954-687f-1fdf34a6c229", "custom.eyes.8" },  // bleu lavande
            { "274864a4-dc61-8bc4-cbb2-98afe623384b", "custom.eyes.9" },  // gris foncé
            { "eff75f7a-00bf-25b4-3b7b-35e8f03cd8d3", "custom.eyes.10" }, // taupe
            { "050730dd-abd7-b5f4-0bc1-136a4adde2df", "custom.eyes.11" }, // ambre
            { "8d609da2-774a-7ce4-3ba1-e1f648018812", "custom.eyes.12" }, // sarcelle
            { "589c4d02-88f1-e0f4-e894-bc7fd0ae82c2", "custom.eyes.13" }, // brun foncé
            { "267211eb-75f6-f004-ab1a-4c9b61b6e970", "custom.eyes.14" }, // rose

            // --- Chemise (ShirtColor, 19 — GUID identiques entre les 2 corps) ---
            { "6328f73a-c74b-21a4-590d-e72af12f9b54", "custom.shirt.1" },  // vert
            { "9617bf53-b57f-9a04-88aa-9affd7ac650d", "custom.shirt.2" },  // bleu acier
            { "950076f4-12a1-5e54-298d-57d0b34cce39", "custom.shirt.3" },  // anthracite
            { "00953b1d-e7b9-a604-89d6-b2c2b16aa2f7", "custom.shirt.4" },  // prune
            { "f654be8f-74ad-8c64-a91a-a412d57da898", "custom.shirt.5" },  // bleu roi
            { "57d009e1-68fa-2774-a86c-91ea9d1ff9d9", "custom.shirt.6" },  // orange
            { "fb06f085-1a36-1734-3b3c-4670ec25351a", "custom.shirt.7" },  // rouge
            { "0884444b-7848-9794-f963-14bfbe31dd52", "custom.shirt.8" },  // rose
            { "d18d1bc0-4192-55a4-68c9-358dc4a4e340", "custom.shirt.9" },  // mauve
            { "7c520c53-1004-02c4-98f1-c13488ac7f17", "custom.shirt.10" }, // moutarde
            { "a8956032-8deb-7654-ca79-09e98d9e6c9f", "custom.shirt.11" }, // cyan
            { "06ded8f0-3326-f8b4-2a6c-b54171815619", "custom.shirt.12" }, // kaki
            { "5bd49601-c1c4-fb54-f932-1abe9cad45ff", "custom.shirt.13" }, // lavande
            { "237869e0-1fd5-8d24-282e-a2ae7335bd0d", "custom.shirt.14" }, // magenta
            { "74ff894c-81bb-f274-f81c-b51bf1a363f9", "custom.shirt.15" }, // terracotta
            { "87aa507b-d6ca-b4e4-da70-bebe2018c083", "custom.shirt.16" }, // turquoise
            { "0597bbfe-3b2a-1544-b8a4-71f2b3985655", "custom.shirt.17" }, // violet
            { "b3216471-26f2-15f4-28ad-6f15f35f67f9", "custom.shirt.18" }, // vert pomme
            { "44c49903-99e7-c8e4-1b49-4d5619458bfb", "custom.shirt.19" }, // bleu électrique

            // --- Pantalon (PantsColor, 18 — GUID identiques entre les 2 corps) ---
            { "1e81ea3f-cb6e-a0e4-1bec-798be4d2f64e", "custom.pants.1" },  // bleu acier
            { "6d284a99-cac3-5ce4-190d-ff84d27ce163", "custom.pants.2" },  // anthracite
            { "fb7d67b6-5d85-98a4-68a3-e996cbcba826", "custom.pants.3" },  // prune
            { "efff68be-7e3b-e4d4-8939-ee80f209e75c", "custom.pants.4" },  // bleu roi
            { "b83c1a69-d09f-51a4-28fa-9c1ef18c4d5f", "custom.pants.5" },  // orange
            { "6c882fdb-aa64-d6c4-d806-48f6e2f6ab51", "custom.pants.6" },  // rouge
            { "03a52c73-7533-3bc4-4aaf-fc3fceb3b2e1", "custom.pants.7" },  // rose
            { "e88e07fa-5742-0184-68c2-aaff03c592e4", "custom.pants.8" },  // vert
            { "1510f21f-2535-dd74-aa79-85b81dc9c6e1", "custom.pants.9" },  // mauve
            { "db283088-2a66-c4d4-8b0e-af6a56638a7d", "custom.pants.10" }, // moutarde
            { "9ae2322d-fd02-6fb4-ea38-7fedf7d6e047", "custom.pants.11" }, // cyan
            { "a718e677-f178-4214-0b67-3fb1d30fee86", "custom.pants.12" }, // kaki
            { "f1f2e6d9-1174-ba74-a947-31559e4aba17", "custom.pants.13" }, // lavande
            { "bb2ab6ee-1978-1364-eaa5-79833a4e5aa4", "custom.pants.14" }, // magenta
            { "272c4683-58fa-c754-990e-a543a69ba131", "custom.pants.15" }, // terracotta
            { "8886a47d-4388-1514-eae2-f2254350cb0c", "custom.pants.16" }, // turquoise
            { "62df78ab-ce21-6c74-4a8a-13ec84bee5fa", "custom.pants.17" }, // violet
            { "60f9072e-fa93-b0c4-8aab-abe1f8b5fdbe", "custom.pants.18" }, // vert pomme
        };

        // Nom enrichi d'une variante par son adresse GUID, resolu dans la langue courante,
        // ou null si non repertoriee (l'appelant retombe alors sur le simple "variante N / M").
        public static string Lookup(string guidLower)
        {
            if (string.IsNullOrEmpty(guidLower)) return null;
            return Keys.TryGetValue(guidLower, out var key) ? Strings.L(key) : null;
        }
    }
}
