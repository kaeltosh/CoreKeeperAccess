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
            { "013694cd-cda7-46f4-19e0-1a69c7f5e508", "Court plaqué" },      // var 3
            { "04fb9341-6fe3-e3c4-c885-dc1ed42b571c", "Houppette" },         // var 4
            { "6897970a-711a-9a84-59ab-8597b19f8635", "Hérissé" },           // var 5
            { "7f2c02c0-1ce3-2cd4-6b4a-33b4e45f298c", "Ondulé volumineux" }, // var 6
            { "5133051f-794a-6264-bb64-083ea6598ac3", "Rideau lisse" },      // var 7
            { "ac175fc3-111f-0614-69aa-95c5e77c93f4", "Bol net" },           // var 8
            { "56531129-a9d8-a194-4ae0-89da8cc383bf", "Bouclé rond" },       // var 9
            { "9833d741-4e28-7cb4-ca7b-84097ec21e67", "Volume bombé" },      // var 10
            { "91526fa1-c458-6a74-8867-c5de1646ef7f", "Couettes basses" },   // var 11
            { "b027db0b-30f4-9464-da19-02d4290b740a", "Épis dressés" },      // var 12
            { "99271261-1ed6-20e4-8900-4c7fa3c6fec1", "Bol arrondi" },       // var 13
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
