using System.Collections.Generic;

namespace CoreKeeperAccess.Patches
{
    // Noms enrichis des variantes de customisation perso, accroches a l'ADRESSE GUID de
    // chaque variante (cle STABLE entre patches du jeu, vs l'index positionnel qui peut se
    // decaler). 1re passe APPROXIMATIVE pour les cheveux : descriptions a l'oreille depuis
    // les screenshots, le numero "variante N / M" reste annonce en filet pour lever toute
    // ambiguite (notamment entre les nombreuses coupes courtes/bol). A affiner en 2e passe.
    // Cles en minuscules (compare avec DataBlockAddress.ToString().ToLowerInvariant()).
    internal static class CustomizationNames
    {
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            // --- Corps (PROVISOIRE) : le jeu nomme ses 2 corps "MaleBody"/"FemaleBody" en
            //     interne -> libelles cash en attendant mieux (fonctionnel mais imprecis). ---
            { "a6a9870f-41e8-98b4-9bc7-1cc146f1687d", "Masculin" }, // MaleBody  (var 1)
            { "5e9ee05b-3247-6fb4-5b24-a4e4af303b55", "Féminin" },  // FemaleBody (var 2)

            // --- Cheveux (Hair, 24 variantes) ---
            { "ad057198-5346-7884-eb9b-da8718f93702", "Carré bouclé" },      // var 1
            { "436612ae-9b61-ad34-0890-020ebbab1b08", "Bol lisse" },         // var 2
            { "013694cd-cda7-46f4-19e0-1a69c7f5e508", "Chauve" },           // var 3
            { "04fb9341-6fe3-e3c4-c885-dc1ed42b571c", "Houppette" },         // var 4
            { "6897970a-711a-9a84-59ab-8597b19f8635", "Hérissé" },           // var 5
            { "7f2c02c0-1ce3-2cd4-6b4a-33b4e45f298c", "Ondulé volumineux" }, // var 6
            { "5133051f-794a-6264-bb64-083ea6598ac3", "Rideau lisse" },      // var 7
            { "ac175fc3-111f-0614-69aa-95c5e77c93f4", "Chignon" },           // var 8
            { "56531129-a9d8-a194-4ae0-89da8cc383bf", "Bouclé rond" },       // var 9
            { "9833d741-4e28-7cb4-ca7b-84097ec21e67", "Volume bombé" },      // var 10
            { "91526fa1-c458-6a74-8867-c5de1646ef7f", "Crâne dégarni" },     // var 11
            { "b027db0b-30f4-9464-da19-02d4290b740a", "Épis dressés" },      // var 12
            { "99271261-1ed6-20e4-8900-4c7fa3c6fec1", "Bol à mèche gauche" },// var 13
            { "3c008ea0-0591-c954-0b1d-302962cf9fd6", "Calot" },             // var 14 (incertain)
            { "3cde4d32-417d-31e4-2811-1240e8791bec", "Macarons" },          // var 15
            { "03db6586-7774-9084-2b80-49d43c12e79f", "Long bouclé" },       // var 16
            { "eae7890f-856b-3994-cba9-9b0e02d4f088", "Mi-long ondulé" },    // var 17
            { "cfe8dc97-88df-bc54-fb69-fdf7c63bca24", "Catogan" },           // var 18
            { "c5e4a4ee-0474-4634-58aa-0e89208e9c9d", "Court à frange" },    // var 19
            { "424416a1-ef6d-c9b4-683f-75da5849a2bb", "Couettes hautes" },   // var 20 (incertain)
            { "f08fcb4f-bf43-d9b4-ea7c-cc9dc3f4e00a", "Bol serré" },         // var 21
            { "70521257-e669-7ad4-8b78-839765b36fb1", "Mi-long encadrant" },// var 22
            { "5c936d4c-c4d3-b094-cb26-c90f4306935c", "Court à toupet" },    // var 23
            { "578a7d7c-caa4-2ad4-495c-99f2a2a13433", "Calot lisse" },       // var 24 (incertain)

            // === Couleurs : noms FAITS MAIN, tous DISTINCTS dans leur categorie, calibres
            //     sur les RGB reels de chaque palette (cf. customization_naming/dump). Le but
            //     est qu'aucune variante d'une meme categorie ne porte le meme mot (sinon
            //     impossible de les distinguer a l'oreille). Verification visuelle finale a
            //     faire par une personne voyante. ===

            // --- Peau (SkinColor, 4) ---
            { "90cec489-adc0-7244-9a6d-e2dc969b5e05", "claire" },        // var 1
            { "15c8529a-ad69-7854-a91e-4335bf340b0d", "mate" },          // var 2
            { "25529cf7-7dd2-1644-f8e4-6cea61dd9e8c", "foncée" },        // var 3
            { "cc76bde3-dfec-f734-da14-e254e54d3383", "très claire" },   // var 4

            // --- Couleur des cheveux (HairColor, 19) ---
            { "ba4e4c2f-2ee5-13f4-ea28-bb4a2ea43822", "châtain" },       // var 1
            { "159e9efb-7b22-98e4-7944-55ed67d8f293", "blond" },         // var 2
            { "f53470cd-79aa-a514-e804-4c94cc076741", "bleu glacier" },  // var 3
            { "605f638d-0724-2c94-a84c-c82253c05760", "noir" },          // var 4
            { "564bf265-cbe1-8564-aa0c-34706cc17a5b", "roux" },          // var 5
            { "cd9949e0-7169-0784-5b10-8739343e536f", "vieux rose" },    // var 6
            { "4f3a99cc-bccc-ec94-ca74-c2ba32168d9e", "violet" },        // var 7
            { "613e1c7c-05d5-4ac4-69a4-4e575384ee38", "bleu" },          // var 8
            { "21830409-baab-c5e4-0a43-faddb43683b8", "vert" },          // var 9
            { "ec27703f-a5d5-cfa4-5b13-a81d22e098e2", "blanc" },         // var 10
            { "551dcea2-0e9a-1644-e999-5b15636be83d", "cuivré" },        // var 11
            { "6a42dc17-7f39-b784-7930-2ba85996d0c6", "magenta" },       // var 12
            { "51af5578-d268-2304-c99c-efa881e11e0b", "bleu ciel" },     // var 13
            { "d55c85fc-f1e9-a9a4-58e5-9035b15a2af1", "vert pomme" },    // var 14
            { "5571f386-2137-b904-6820-35640e1cfd67", "châtain clair" }, // var 15
            { "066b3d95-5d2d-e7c4-1ae5-11a74aff2773", "gris" },          // var 16
            { "ee431743-eebe-d824-78c1-aa08bb33cba0", "rouge" },         // var 17
            { "49adccf9-e645-1894-caec-669abb06ccda", "turquoise" },     // var 18
            { "53b70d70-3bca-5364-7af3-ba8f479aac41", "fuchsia" },       // var 19

            // --- Yeux (EyeColor, 14) ---
            { "2f1e0497-bb80-3a94-7815-c9be99fa700c", "bleu" },          // var 1
            { "71332930-e150-ead4-f8a2-6adb25283f76", "marron" },        // var 2
            { "3cb73e82-c05a-42d4-8b25-e5a82639cdfc", "violet" },        // var 3
            { "f2ac8d4f-abf8-6894-69cb-65a4e48f6c3a", "vert" },          // var 4
            { "ce5bdf89-8a1f-be94-bbf7-36ee7ece6ba0", "rouge" },         // var 5
            { "fd13f699-57fa-5144-59b9-a59dd9506fb0", "vieux rose" },    // var 6
            { "5998eb5b-fcc4-2f84-aac1-4d7fe1b23bf3", "bleu ciel" },     // var 7
            { "c651cc8b-c4c0-8954-687f-1fdf34a6c229", "bleu lavande" },  // var 8
            { "274864a4-dc61-8bc4-cbb2-98afe623384b", "gris foncé" },    // var 9
            { "eff75f7a-00bf-25b4-3b7b-35e8f03cd8d3", "taupe" },         // var 10
            { "050730dd-abd7-b5f4-0bc1-136a4adde2df", "ambre" },         // var 11
            { "8d609da2-774a-7ce4-3ba1-e1f648018812", "sarcelle" },      // var 12
            { "589c4d02-88f1-e0f4-e894-bc7fd0ae82c2", "brun foncé" },    // var 13
            { "267211eb-75f6-f004-ab1a-4c9b61b6e970", "rose" },          // var 14

            // --- Chemise (ShirtColor, 19 — GUID identiques entre les 2 corps) ---
            { "6328f73a-c74b-21a4-590d-e72af12f9b54", "vert" },          // var 1
            { "9617bf53-b57f-9a04-88aa-9affd7ac650d", "bleu acier" },    // var 2
            { "950076f4-12a1-5e54-298d-57d0b34cce39", "anthracite" },    // var 3
            { "00953b1d-e7b9-a604-89d6-b2c2b16aa2f7", "prune" },         // var 4
            { "f654be8f-74ad-8c64-a91a-a412d57da898", "bleu roi" },      // var 5
            { "57d009e1-68fa-2774-a86c-91ea9d1ff9d9", "orange" },        // var 6
            { "fb06f085-1a36-1734-3b3c-4670ec25351a", "rouge" },         // var 7
            { "0884444b-7848-9794-f963-14bfbe31dd52", "rose" },          // var 8
            { "d18d1bc0-4192-55a4-68c9-358dc4a4e340", "mauve" },         // var 9
            { "7c520c53-1004-02c4-98f1-c13488ac7f17", "moutarde" },      // var 10
            { "a8956032-8deb-7654-ca79-09e98d9e6c9f", "cyan" },          // var 11
            { "06ded8f0-3326-f8b4-2a6c-b54171815619", "kaki" },          // var 12
            { "5bd49601-c1c4-fb54-f932-1abe9cad45ff", "lavande" },       // var 13
            { "237869e0-1fd5-8d24-282e-a2ae7335bd0d", "magenta" },       // var 14
            { "74ff894c-81bb-f274-f81c-b51bf1a363f9", "terracotta" },    // var 15
            { "87aa507b-d6ca-b4e4-da70-bebe2018c083", "turquoise" },     // var 16
            { "0597bbfe-3b2a-1544-b8a4-71f2b3985655", "violet" },        // var 17
            { "b3216471-26f2-15f4-28ad-6f15f35f67f9", "vert pomme" },    // var 18
            { "44c49903-99e7-c8e4-1b49-4d5619458bfb", "bleu électrique" },// var 19

            // --- Pantalon (PantsColor, 18 — GUID identiques entre les 2 corps) ---
            { "1e81ea3f-cb6e-a0e4-1bec-798be4d2f64e", "bleu acier" },    // var 1
            { "6d284a99-cac3-5ce4-190d-ff84d27ce163", "anthracite" },    // var 2
            { "fb7d67b6-5d85-98a4-68a3-e996cbcba826", "prune" },         // var 3
            { "efff68be-7e3b-e4d4-8939-ee80f209e75c", "bleu roi" },      // var 4
            { "b83c1a69-d09f-51a4-28fa-9c1ef18c4d5f", "orange" },        // var 5
            { "6c882fdb-aa64-d6c4-d806-48f6e2f6ab51", "rouge" },         // var 6
            { "03a52c73-7533-3bc4-4aaf-fc3fceb3b2e1", "rose" },          // var 7
            { "e88e07fa-5742-0184-68c2-aaff03c592e4", "vert" },          // var 8
            { "1510f21f-2535-dd74-aa79-85b81dc9c6e1", "mauve" },         // var 9
            { "db283088-2a66-c4d4-8b0e-af6a56638a7d", "moutarde" },      // var 10
            { "9ae2322d-fd02-6fb4-ea38-7fedf7d6e047", "cyan" },          // var 11
            { "a718e677-f178-4214-0b67-3fb1d30fee86", "kaki" },          // var 12
            { "f1f2e6d9-1174-ba74-a947-31559e4aba17", "lavande" },       // var 13
            { "bb2ab6ee-1978-1364-eaa5-79833a4e5aa4", "magenta" },       // var 14
            { "272c4683-58fa-c754-990e-a543a69ba131", "terracotta" },    // var 15
            { "8886a47d-4388-1514-eae2-f2254350cb0c", "turquoise" },     // var 16
            { "62df78ab-ce21-6c74-4a8a-13ec84bee5fa", "violet" },        // var 17
            { "60f9072e-fa93-b0c4-8aab-abe1f8b5fdbe", "vert pomme" },    // var 18
        };

        // Nom enrichi d'une variante par son adresse GUID, ou null si non repertoriee
        // (l'appelant retombe alors sur le simple "variante N / M").
        public static string Lookup(string guidLower)
        {
            if (string.IsNullOrEmpty(guidLower)) return null;
            return Names.TryGetValue(guidLower, out var name) ? name : null;
        }
    }
}
