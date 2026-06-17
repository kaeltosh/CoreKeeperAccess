using System.Collections.Generic;
using CoreKeeperAccess.Localization;
using HarmonyLib;
using Unity.Mathematics;

namespace CoreKeeperAccess.Patches
{
    internal static class InGameTtsState
    {
        // Dernier element UI in-game annonce (dedup par instance, pas par texte :
        // plusieurs slots vides partagent le meme libelle mais doivent s'annoncer
        // chacun a la navigation).
        public static int LastSelectedInstanceId;
    }

    internal static class InGameTtsCore
    {
        // Construit l'annonce d'un element UI in-game (slot, bouton) a partir de
        // son titre de survol natif + la quantite si c'est une pile d'objets.
        // Volontairement court (titre + quantite) pour une navigation fluide ;
        // description et stats restent disponibles pour un futur "lire le detail".
        public static string BuildElementAnnouncement(UIelement element)
        {
            if (element == null) return null;

            var title = TtsText.ResolveTextAndFormatFields(element.GetHoverTitle());
            if (string.IsNullOrEmpty(title)) return null;

            var parts = new List<string> { title };
            var seen = new HashSet<string> { title };

            int amount = GetAnnounceAmount(element);
            if (amount > 1) parts.Add(amount.ToString());

            void Add(string s)
            {
                if (!string.IsNullOrEmpty(s) && seen.Add(s)) parts.Add(s);
            }

            Add(BuildSkillTalentInfo(element));
            Add(FillLevelLabel(element.GetContainedObject().objectData));
            Add(BuildCraftInfo(element));
            Add(BuildMerchantInfo(element));
            Add(BuildSizeInfo(element));

            // Tooltip : description puis stats, lus directement a la selection.
            // Pour zapper, il suffit de bouger (l'annonce suivante interrompt).
            var desc = element.GetHoverDescription();
            if (desc != null)
                foreach (var d in desc) Add(TtsText.ResolveTextAndFormatFields(d));

            var stats = element.GetHoverStats(false);
            if (stats != null)
                foreach (var s in stats) Add(TtsText.ResolveTextAndFormatFields(s));

            return string.Join(", ", parts);
        }

        // Annonce d'une ligne de la fiche de stats. Contrairement aux slots, une
        // StatTextUIElement n'a pas de hover title : le texte affiche (deja rendu,
        // valeur substituee) est dans son champ .text. On y ajoute la description
        // longue (GetHoverStats -> ConditionEffectDesc), dispo si la ligne est a l'ecran.
        public static string BuildStatLine(UIelement element)
        {
            var st = element as StatTextUIElement;
            if (st == null) return null;

            var parts = new List<string>();
            var seen = new HashSet<string>();
            void Add(string s) { if (!string.IsNullOrEmpty(s) && seen.Add(s)) parts.Add(s); }

            Add(TtsText.ResolvePugText(st.text));
            var hs = st.GetHoverStats(false);
            if (hs != null)
                foreach (var h in hs) Add(TtsText.ResolveTextAndFormatFields(h));

            return parts.Count == 0 ? null : string.Join(", ", parts);
        }

        // Quantite a annoncer : pour une recette = la quantite PRODUITE par craft
        // (ex. torche x3), sinon = la quantite contenue dans l'emplacement.
        private static int GetAnnounceAmount(UIelement element)
        {
            var recipe = element as RecipeSlotUI;
            if (recipe != null)
            {
                var player = Manager.main != null ? Manager.main.player : null;
                var handler = player != null ? player.activeCraftingHandler : null;
                if (handler != null)
                {
                    var info = handler.GetRecipeInfo(recipe.inventorySlotIndex);
                    return info.isValid ? info.amount : 1;
                }
                return 1;
            }
            var od = element.GetContainedObject().objectData;
            // Seau / arrosoir (FullnessCD) UNIQUEMENT : leur amount = niveau de remplissage,
            // pas une quantite empilee -> on ne l'annonce pas comme un nombre d'objets (c'est
            // FillLevelLabel qui dit vide/plein). NB : ne PAS elargir a la durabilite ni a
            // l'xp (l'amount EST l'info utile pour eux, regression vecue le 15 juin).
            if (od.objectID != ObjectID.None && PugDatabase.HasComponent<FullnessCD>(od))
                return 1;
            return od.amount;
        }

        // Niveau de remplissage d'un objet a jauge de type FULLNESS (seau, arrosoir) :
        // "vide" / "plein" / "N/M" si la jauge a plusieurs crans. Renvoie null pour tout
        // objet sans FullnessCD (durabilite et xp ne sont pas concernes ici). Centralise
        // pour l'inventaire ET l'objet en main.
        public static string FillLevelLabel(ObjectDataCD od)
        {
            if (od.objectID == ObjectID.None) return null;
            if (!PugDatabase.HasComponent<FullnessCD>(od)) return null;
            int max = PugDatabase.GetComponent<FullnessCD>(od).maxFullness;
            int cur = od.amount;
            if (cur <= 0) return Strings.L("fill.empty");
            if (max > 1 && cur < max) return cur + "/" + max;
            return Strings.L("fill.full");
        }

        // Pour une recette d'artisanat : "fabricable" si on a tout, sinon la liste
        // detaillee de ce qui manque ("manque 3 Bois, 2 Cuivre"). Renvoie null pour
        // tout element qui n'est pas une recette (GetRequiredMaterials = null).
        private static string BuildCraftInfo(UIelement element)
        {
            List<PugDatabase.MaterialInfo> mats;
            try { mats = element.GetRequiredMaterials(false, false); }
            catch { return null; }
            if (mats == null || mats.Count == 0) return null;

            var missing = new List<string>();
            foreach (var m in mats)
            {
                if (m == null || m.amountAvailable >= m.amountNeeded) continue;
                int lack = m.amountNeeded - m.amountAvailable;
                var name = ResolveObjectName(m.objectID);
                missing.Add(string.IsNullOrEmpty(name) ? lack.ToString() : lack + " " + name);
            }

            return missing.Count == 0
                ? Strings.L("craft.craftable")
                : Strings.L("craft.missing") + " " + string.Join(", ", missing);
        }

        // Reflection vers les membres prives de SkillTalentUIElement (onglet talents),
        // resolus une fois. Si une maj du jeu les renomme, ils valent null -> le helper
        // ci-dessous retombe sur null sans casser l'annonce (try/catch).
        private static readonly System.Reflection.MethodInfo _talentPointsMethod =
            typeof(SkillTalentUIElement).GetMethod("GetCurrentPoints",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        private static readonly System.Reflection.PropertyInfo _talentAllowedProp =
            typeof(SkillTalentUIElement).GetProperty("allowedToPlacePoints",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        private static readonly System.Reflection.FieldInfo _talentTreeField =
            typeof(SkillTalentUIElement).GetField("activeSkillTree",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Etat d'une competence ou d'un talent (onglet talents de la fenetre perso),
        // suffixe au titre dans l'annonce. Aucune des methodes de survol natives ne
        // l'expose, d'ou ce calcul. Null pour tout autre element.
        //  - Competence (SkillUIElement) : points de talent EN ATTENTE sur cette
        //    competence ("N a depenser"), pour reperer ou investir sans ouvrir l'arbre.
        //  - Talent (SkillTalentUIElement), etat exclusif : "au maximum" (5/5) ;
        //    "verrouille, requiert X" (prerequis non rempli, X = parent(s) a completer) ;
        //    "disponible" (eligible ET des points en banque) ; rien sinon (le titre dit
        //    deja les points poses).
        private static string BuildSkillTalentInfo(UIelement element)
        {
            var saves = Manager.saves;
            if (saves == null) return null;

            var skill = element as SkillUIElement;
            if (skill != null)
            {
                int avail = saves.GetAvailableTalentPoints(skill.skillID);
                return avail > 0 ? avail + " " + Strings.L("skill.spendable") : null;
            }

            var talent = element as SkillTalentUIElement;
            if (talent == null) return null;
            try
            {
                int points = (int)_talentPointsMethod.Invoke(talent, null);
                if (points >= 5) return Strings.L("talent.maxed");

                bool allowed = (bool)_talentAllowedProp.GetValue(talent);
                if (!allowed)
                    return Strings.L("talent.locked") + ", " + Strings.L("talent.prereq")
                        + " " + TalentPrereqNames(talent);

                var tree = (SkillID)_talentTreeField.GetValue(talent);
                if (saves.GetAvailableTalentPoints(tree) > 0) return Strings.L("talent.available");
            }
            catch { }
            return null;
        }

        // Nom(s) du ou des talents prerequis d'un talent verrouille : les talents qui le
        // designent comme enfant (leftSkillTalent / rightSkillTalent) et ne sont pas
        // encore completes a 5 points (completer un seul suffit a debloquer). Joints par
        // "ou". Repli generique si on n'en identifie aucun.
        private static string TalentPrereqNames(SkillTalentUIElement talent)
        {
            var all = UnityEngine.Object.FindObjectsByType<SkillTalentUIElement>(
                UnityEngine.FindObjectsSortMode.None);
            var names = new List<string>();
            foreach (var t in all)
            {
                if (t == null || (t.leftSkillTalent != talent && t.rightSkillTalent != talent)) continue;
                try { if ((int)_talentPointsMethod.Invoke(t, null) >= 5) continue; } // parent deja complete
                catch { }
                var title = t.GetHoverTitle();
                string name = title.formatFields != null && title.formatFields.Length > 0
                    ? title.formatFields[0] : null;
                if (!string.IsNullOrEmpty(name) && !names.Contains(name)) names.Add(name);
            }
            return names.Count > 0
                ? string.Join(" " + Strings.L("talent.or") + " ", names)
                : Strings.L("talent.prereqGeneric");
        }

        // Prix marchand. Pour un emplacement d'achat (BuySlot) : cout d'achat + verdict
        // "abordable" / "trop cher" selon les pieces possedees. Pour un emplacement de
        // vente (PlayerSellSlot rempli) : valeur de revente. Et, quand un marchand est
        // ouvert, pour un emplacement du joueur (sac/barre/pochette) rempli : la valeur de
        // revente de l'objet, pour juger quoi vendre sans le deplacer. GetCoinValue()
        // gere les deux sens (buy deduit du slotType). Null hors de ces cas.
        private static string BuildMerchantInfo(UIelement element)
        {
            var slot = element as InventorySlotUI;
            if (slot == null) return null;
            bool buy = slot.slotType == ItemSlotsUIType.BuySlot;
            bool sell = slot.slotType == ItemSlotsUIType.PlayerSellSlot;
            bool playerSlot = !buy && !sell && Manager.ui != null && Manager.ui.isSellUIShowing
                && IsPlayerInventorySlot(slot.slotType);
            if (!buy && !sell && !playerSlot) return null;
            if (slot.GetContainedObject().objectData.objectID == ObjectID.None) return null;

            int value = slot.GetCoinValue();
            // Objet du sac non vendable (valeur nulle) : on ne dit rien, ca n'apporte rien.
            if (playerSlot && value <= 0) return null;

            string priced = (buy ? Strings.L("merchant.price") : Strings.L("merchant.value"))
                + " " + value + " " + Strings.L("merchant.coins");
            if (!buy) return priced;

            var player = Manager.main != null ? Manager.main.player : null;
            int coins = player != null ? player.playerInventoryHandler.GetExistingAmountOfObject(ObjectID.AncientCoin) : 0;
            return priced + ", " + Strings.L(value <= coins ? "merchant.affordable" : "merchant.tooExpensive");
        }

        // Emplacement appartenant a l'inventaire du joueur (sac, barre rapide, pochettes).
        private static bool IsPlayerInventorySlot(ItemSlotsUIType t)
        {
            return t == ItemSlotsUIType.PlayerInventorySlot
                || t == ItemSlotsUIType.PouchInventorySlot
                || t == ItemSlotsUIType.Pouch1 || t == ItemSlotsUIType.Pouch2
                || t == ItemSlotsUIType.Pouch3 || t == ItemSlotsUIType.Pouch4;
        }

        // Detail marchand (touche access) : solde de pieces du joueur + total de revente
        // des emplacements de vente actuellement remplis. Null si aucune fenetre marchand
        // ouverte. Sert a entendre "ce que je gagne si je vends tout" avant de valider.
        public static string BuildMerchantDetail()
        {
            var ui = Manager.ui;
            if (ui == null || (!ui.isBuyUIShowing && !ui.isSellUIShowing)) return null;
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null) return null;

            int coins = player.playerInventoryHandler.GetExistingAmountOfObject(ObjectID.AncientCoin);
            var parts = new List<string>
            {
                Strings.L("merchant.balance") + " " + coins + " " + Strings.L("merchant.coins")
            };
            if (ui.isSellUIShowing && player.sellSlotsHandler != null)
            {
                int total = player.sellSlotsHandler.sellSlotsInventoryHandler.GetCoinValueAll(player, false);
                parts.Add(Strings.L("merchant.sellTotal") + " " + total + " " + Strings.L("merchant.coins"));
            }
            return string.Join(", ", parts);
        }

        // Taille brute "2x2" pour le tooltip inventaire (hors contexte de pose : pas de
        // direction, qui n'aurait pas de sens sans curseur). Null si 1x1 ou vide.
        private static string BuildSizeInfo(UIelement element)
        {
            try
            {
                var od = element.GetContainedObject().objectData;
                return FootprintSize(od.objectID, od.variation);
            }
            catch { return null; }
        }

        public static string FootprintSize(ObjectID objectID, int variation)
        {
            int2 sz, co;
            if (!TryFootprint(objectID, variation, out sz, out co)) return null;
            return sz.x + "x" + sz.y;
        }

        // Debordements de l'emprise PAR RAPPORT au point vise (= la case du curseur),
        // en cases : "s'etend 2 vers le haut, 1 a droite, 1 a gauche". C'est l'info
        // actionnable AU CURSEUR (ou la structure tombe si on pose ici). Repere x=est
        // y=nord, ancre = vise - cornerOffset (confirme PlaceObjectSlot) :
        //   haut(nord)=sz.y-1-co.y, bas(sud)=co.y, droite(est)=sz.x-1-co.x, gauche(ouest)=co.x.
        // ROTATION prise en compte : l'emprise tourne AUTOUR du point vise (RotateTransform
        // = RotateY autour du cornerOffset, decompile) -> les 4 debordements pivotent en
        // bloc. variation 0=nord..3=ouest = sens horaire -> decalage cyclique de rot crans.
        // Etalement de l'emprise RELATIF A LA CASE DU CURSEUR. Le ghost SUIT le curseur
        // (le mod vise la case du curseur), donc on RECALCULE l'ancre nous-memes a partir
        // du curseur - synchrone, sans la latence d'une frame qui faisait osciller la
        // lecture de bestPositionToPlaceAt. Le jeu CENTRE l'objet sur la visee :
        // ancre = curseur - taille/2 (division entiere). VALIDE sur le log (lit 2x1,
        // curseur x=9 -> ancre 8, comme le jeu). Etalement = comment le ghost deborde
        // autour de ta case visee. cursor en coord tuile (x=est, y=nord).
        // Deploiement du ghost RELATIF a la case du curseur (sonde). Le lit se pose dans
        // la direction de VISEE (pas au curseur) -> on lit sa position REELLE
        // (PlacementCD.bestPositionToPlaceAt), on la COMPARE au curseur, et on decrit ou
        // les cases du ghost tombent par rapport a la sonde. Curseur SUR le ghost ->
        // etalement de chaque cote ; curseur a cote -> "de X a Y" dans la direction du ghost.
        public static string FootprintFromCursor(int2 cursor)
        {
            try
            {
                var player = Manager.main != null ? Manager.main.player : null;
                if (player == null) return null;
                var held = player.GetHeldObject();
                int2 szBase, co;
                if (!TryFootprint(held.objectID, held.variation, out szBase, out co)) return null;
                if (!EntityUtility.HasComponentData<PlacementCD>(player.entity, player.world)) return null;
                var pc = EntityUtility.GetComponentData<PlacementCD>(player.entity, player.world);

                int2 sz = DirectionCD.GetPrefabTileSize(szBase,
                    DirectionBasedOnVariationCD.GetDirectionFromVariation(pc.rotationVariationToPlace, false));
                var bp = pc.bestPositionToPlaceAt;               // position REELLE de l'hologramme (verifie diag)
                int xmin = bp.x - co.x, zmin = bp.z - co.y;
                int xmax = xmin + sz.x - 1, zmax = zmin + sz.y - 1;

                // REGLE STRICTE (demande utilisateur) : curseur PAS sur l'hologramme -> rien.
                if (cursor.x < xmin || cursor.x > xmax || cursor.y < zmin || cursor.y > zmax) return null;

                // Curseur sur l'hologramme : de combien il deborde de la case du curseur.
                int left = cursor.x - xmin, right = xmax - cursor.x;
                int down = cursor.y - zmin, up = zmax - cursor.y;   // z+ = nord
                var parts = new List<string>();
                if (up > 0) parts.Add(up + " " + Strings.L("place.up"));
                if (down > 0) parts.Add(down + " " + Strings.L("place.down"));
                if (right > 0) parts.Add(right + " " + Strings.L("place.right"));
                if (left > 0) parts.Add(left + " " + Strings.L("place.left"));
                if (parts.Count == 0) return null; // hologramme reduit a la case du curseur
                return Strings.L("place.extends") + " " + string.Join(", ", parts);
            }
            catch { return null; }
        }

        // Lit prefabTileSize + prefabCornerOffset d'un objet ; false si 1x1, vide ou
        // base de donnees indispo (rien a annoncer).
        private static bool TryFootprint(ObjectID objectID, int variation, out int2 size, out int2 corner)
        {
            size = default; corner = default;
            try
            {
                if (objectID == ObjectID.None) return false;
                var player = Manager.main != null ? Manager.main.player : null;
                if (player == null) return false;
                var bank = player.querySystem.GetSingleton<PugDatabase.DatabaseBankCD>();
                ref var info = ref PugDatabase.GetEntityObjectInfo(objectID, bank.databaseBankBlob, variation);
                if (info.prefabTileSize.x <= 1 && info.prefabTileSize.y <= 1) return false;
                // Outils a zone d'effet reglable (houe, arrosoir, pelle, semoir...) : leur
                // prefabTileSize est la zone d'effet MAXIMALE, PAS l'emprise d'un meuble
                // -> pas d'annonce de forme (la zone COURANTE est dite par ToolZoneLabel).
                // Discriminant FIABLE = ObjectType : ResizableTileSizeCD est aussi porte par
                // des meubles (le lit, PlaceablePrefab), donc faux positifs si on s'y fie.
                if (IsZoneTool(info.objectType)) return false;
                size = info.prefabTileSize; corner = info.prefabCornerOffset;
                return true;
            }
            catch { return false; }
        }

        // Taille de la zone d'effet COURANTE d'un outil a zone reglable (houe, arrosoir,
        // pelle, semoir...) tenu en main : "3x3". null si l'objet en main n'est pas un
        // tel outil. Ces outils portent ResizableTileSizeCD ; leur prefabTileSize est la
        // zone MAX et EquippedObjectVisualCD.sizeVariationToPlace (replique cote client)
        // le cran courant cycle par le Rotate -> EquipmentSlot.GetTileSizeFromVariation
        // combine les deux (zone carree NxN). Annonce a la selection ET au Rotate.
        public static string ToolZoneLabel(PlayerController player)
        {
            try
            {
                if (player == null) return null;
                var held = player.GetHeldObject();
                if (held.objectID == ObjectID.None) return null;
                var bank = player.querySystem.GetSingleton<PugDatabase.DatabaseBankCD>();
                ref var info = ref PugDatabase.GetEntityObjectInfo(held.objectID, bank.databaseBankBlob, held.variation);
                if (!IsZoneTool(info.objectType)) return null;
                if (info.prefabTileSize.x <= 1) return null;
                if (!EntityUtility.HasComponentData<EquippedObjectVisualCD>(player.entity, player.world)) return null;
                var vis = EntityUtility.GetComponentData<EquippedObjectVisualCD>(player.entity, player.world);
                int2 sz = EquipmentSlot.GetTileSizeFromVariation(vis.sizeVariationToPlace, info.prefabTileSize);
                return Strings.L("place.zone") + " " + sz.x + "x" + sz.y;
            }
            catch { return null; }
        }

        // Outil a zone d'effet reglable au Rotate (houe, arrosoir, pelle, semoir, pinceau,
        // toiture, seau). Leur prefabTileSize = zone MAX, a distinguer de l'emprise d'un
        // meuble (ObjectType.PlaceablePrefab). ObjectType issu de la DB (EntityObjectInfo).
        private static bool IsZoneTool(ObjectType t)
        {
            return t == ObjectType.Hoe || t == ObjectType.Shovel || t == ObjectType.WaterCan
                || t == ObjectType.Seeder || t == ObjectType.PaintTool || t == ObjectType.RoofingTool
                || t == ObjectType.Bucket;
        }

        // Nom localise d'un objet a partir de son ObjectID (materiaux, resultat de craft).
        // FALLBACK : certains objets n'ont pas de terme localise (cable ancien des
        // ruines, statues de boss, generateur...) -> ils etaient DETECTES mais MUETS
        // (bip sans nom, info vide). Plutot que le silence, on lit le nom d'enum
        // decoupe pour le TTS ("IndestructibleAncientWire" -> "Indestructible Ancient
        // Wire"). Pas localise, mais identifiable - des libelles i18n cibles pourront
        // s'ajouter au cas par cas.
        public static string ResolveObjectName(ObjectID objectID)
        {
            if (objectID == ObjectID.None) return null;
            // Surcharge i18n du mod EN PRIORITE. Certains objets ne sont PAS traduits par
            // le jeu dans la langue courante (ex. les plantes en croissance *Plant comme
            // HeartBerryPlant -> le jeu renvoie l'ANGLAIS en repli, donc l'ancienne logique
            // "surcharge seulement si nom vide" ne s'activait jamais). Notre table
            // obj.<NomEnum> prime donc pour les cles qu'on declare ; tout objet NON declare
            // (TryL renvoie false) passe par le jeu normalement -> zero regression.
            if (Strings.TryL("obj." + objectID, out string custom)) return custom;
            // Plante en croissance "<Fruit>Plant" non traduite par le jeu -> nom derive du
            // FRUIT (lui traduit). Couvre TOUTES les cultures sans table manuelle.
            string plant = ResolvePlantName(objectID);
            if (plant != null) return plant;
            var taf = PlayerController.GetObjectName(new ContainedObjectsBuffer
            {
                objectData = new ObjectDataCD { objectID = objectID }
            }, false);
            string name = TtsText.ResolveTextAndFormatFields(taf);
            if (!string.IsNullOrEmpty(name)) return name;
            return SplitEnumName(objectID.ToString());
        }

        // Nom d'une plante en croissance dont l'ObjectID finit par "Plant" (HeartBerryPlant,
        // LunacornPlant...). Le jeu ne traduit PAS ces ObjectID (repli anglais) ; on construit
        // "<prefixe> <fruit>" a partir du FRUIT correspondant (nom d'enum sans "Plant"), lui
        // traduit par le jeu. Ne s'active QUE si la langue fournit le prefixe obj.plant_of (le
        // francais) -> l'anglais garde le nom natif du jeu. Repli propre (null) si le fruit
        // n'est pas un ObjectID connu (ex. SnarePlant) -> on retombe sur le nom du jeu.
        private static string ResolvePlantName(ObjectID objectID)
        {
            if (!Strings.TryL("obj.plant_of", out string prefix)) return null;
            string s = objectID.ToString();
            if (s.Length <= 5 || !s.EndsWith("Plant")) return null;
            string fruit = s.Substring(0, s.Length - 5);
            if (!System.Enum.TryParse(fruit, out ObjectID fruitId) || fruitId == ObjectID.None) return null;
            string fruitName = ResolveObjectName(fruitId);
            if (string.IsNullOrEmpty(fruitName)) return null;
            return prefix + " " + char.ToLower(fruitName[0]) + fruitName.Substring(1);
        }

        // "LarvaHiveBossStatue" -> "Larva Hive Boss Statue" (espaces aux frontieres de
        // majuscules, en preservant les sigles).
        private static string SplitEnumName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            var sb = new System.Text.StringBuilder(raw.Length + 8);
            for (int i = 0; i < raw.Length; i++)
            {
                if (i > 0 && char.IsUpper(raw[i])
                    && (char.IsLower(raw[i - 1])
                        || (i + 1 < raw.Length && char.IsLower(raw[i + 1]))))
                    sb.Append(' ');
                sb.Append(raw[i]);
            }
            return sb.ToString();
        }
    }

    // Navigation dans l'inventaire / l'UI in-game. OnUIElementSelected est le point
    // central de selection (souris + clavier + manette). Il aiguille deja les options
    // de menu vers Manager.menu (patch menus du jalon 2) ; on ne traite donc ici que
    // les elements non-menu (slots, boutons in-game) -> zero conflit avec le jalon 2.
    [HarmonyPatch(typeof(UIManager), nameof(UIManager.OnUIElementSelected))]
    internal static class UIManagerOnUIElementSelectedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(UIelement uiElement)
        {
            // Quand notre navigation a11y force la selection, c'est elle qui annonce
            // (avec le contexte de section) : on etouffe l'annonce passive.
            if (Navigation.InventoryNavState.SuppressPassiveAnnounce) return;
            // BlockingUIElement = bloqueur invisible (pose par les overlays, ex. la
            // fiche de stats) ; il n'a jamais de titre lisible et le curseur manette
            // tend a deraper dessus -> on l'ignore pour ne pas lire un "Vide" parasite.
            if (uiElement == null || uiElement.isMenuOption || uiElement is BlockingUIElement) return;
            // Boutons par joueur de l'ecran "gerer les joueurs" : annonces par notre
            // patch dedie (PlayerListEntryButton.OnSelected). Sans ce skip, ce patch
            // in-game ne sait pas les lire et ecrase l'annonce utile par un "Vide".
            if (uiElement is PlayerListEntryButton) return;

            int id = uiElement.GetInstanceID();
            if (id == InGameTtsState.LastSelectedInstanceId) return;

            var announcement = InGameTtsCore.BuildElementAnnouncement(uiElement);
            if (string.IsNullOrEmpty(announcement))
            {
                // Pas de titre lisible : essentiellement un slot d'inventaire vide.
                announcement = Strings.L("ingame.slot.empty");
                if (string.IsNullOrEmpty(announcement)) return;
            }

            InGameTtsState.LastSelectedInstanceId = id;
            TtsText.Say(announcement, true);
        }
    }

    // Notifications de jeu (objet ramasse, item peche, point de talent, durabilite,
    // ame, level de familier...) + messages de chat recus. Tout passe par
    // ChatWindow.AddPugText avec un PugText deja rendu -> on relit le texte affiche.
    [HarmonyPatch(typeof(ChatWindow), "AddPugText")]
    internal static class ChatWindowAddPugTextPatch
    {
        private static readonly HashSet<ChatWindow.MessageTextType> AnnouncedTypes = new HashSet<ChatWindow.MessageTextType>
        {
            ChatWindow.MessageTextType.Received,
            ChatWindow.MessageTextType.NewItem,
            ChatWindow.MessageTextType.CaughtItem,
            ChatWindow.MessageTextType.NewTalentPointAvailable,
            ChatWindow.MessageTextType.DurabilityLost,
            ChatWindow.MessageTextType.AdditionalItemGained,
            ChatWindow.MessageTextType.GainedItem,
            ChatWindow.MessageTextType.ReceivedItems,
            ChatWindow.MessageTextType.PetLeveledUp,
            ChatWindow.MessageTextType.GainedSoul,
        };

        [HarmonyPostfix]
        public static void Postfix(ChatWindow.MessageTextType type, PugText text)
        {
            if (!AnnouncedTypes.Contains(type)) return;

            var announcement = TtsText.ResolvePugText(text);
            if (string.IsNullOrEmpty(announcement)) return;

            // File d'attente NVDA (interrupt = false) : les notifs s'enchainent sans
            // se couper entre elles ni ecraser une annonce de navigation en cours.
            TtsText.Say(announcement, false);
        }
    }

    // Messages flottants contextuels (systeme Emote) : "trop dur, il me faut un
    // drill", "cet objet a besoin d'energie", messages du Core et des forges, lit
    // occupe, tutoriels... OnOccupied choisit le terme selon l'EmoteType puis rend le
    // PugText DANS la methode -> un postfix relit le texte rendu (deja localise).
    // Filtres : emotes ICONE (emoteTypeInput = __illegal__ -> le PugText contient
    // encore le texte d'une emote precedente du pool, ne rien lire) et ponctuation
    // pure ("!", "?") sans valeur en TTS. interrupt=false : evenement passif, file
    // d'attente NVDA (regle commune avec les notifications).
    [HarmonyPatch(typeof(Emote), "OnOccupied")]
    internal static class EmoteOnOccupiedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Emote.EmoteType ___emoteTypeInput, PugText ___text)
        {
            if (___emoteTypeInput == Emote.EmoteType.__illegal__) return;
            if (___emoteTypeInput == Emote.EmoteType.ExclamationMark
                || ___emoteTypeInput == Emote.EmoteType.QuestionMark) return;

            var announcement = TtsText.ResolvePugText(___text);
            if (string.IsNullOrEmpty(announcement)) return;
            TtsText.Say(announcement, false);
        }
    }

    // NOTE : l'annonce du resultat de craft "en main" est desormais geree de facon
    // unifiee par InventoryNavigator.WatchHandChange (qui surveille la main et couvre
    // AUSSI les prises d'objet, pas seulement le craft). Le postfix sur CraftItem a donc
    // ete retire pour eviter le doublon.

    // Bascule de jeu d'equipement (onglets I/II/III ou boutons EQUIP_PRESET_1/2/3).
    // On annonce le prereglage actif ; le contenu des slots se relit ensuite via la
    // navigation / WatchSlotChange.
    [HarmonyPatch(typeof(PlayerController), nameof(PlayerController.SetActiveEquipmentPreset))]
    internal static class PlayerControllerSetActiveEquipmentPresetPatch
    {
        [HarmonyPostfix]
        public static void Postfix(int presetIndex)
        {
            TtsText.Say(Strings.L("equip.preset") + " " + (presetIndex + 1), true);
        }
    }

    // Dialogue scenarise du Core (TheCore). A chaque interaction, le Core choisit une
    // replique (indices vers les boss, deblocage des ames, revelation puis deblocage du
    // boss final, indices du grand mur, phrase finale, outro...) et la REND dans son
    // PugText coreSpeechText, avec un effet de syllabes sonores facon charabia
    // (PugTextEffectEnunciateSyllables) -> AUCUN texte n'atteint le lecteur d'ecran.
    // On capte la reference de chaque coreSpeechText vivant (OnOccupied/OnFree) puis on
    // relit le texte rendu au postfix de PugText.Render.
    // Pourquoi hooker Render et pas OnUse : certaines repliques sont rendues en DIFFERE
    // par des coroutines (ScanArea, LookAround, EmpowerPlayer) hors du frame de OnUse ->
    // un postfix sur OnUse les raterait. Render les capte toutes, immediates comme
    // differees, et part AVANT le charabia (le texte est connu des le rendu).
    internal static class CoreSpeechState
    {
        // coreSpeechText actuellement vivants (un seul Core en pratique ; HashSet par
        // surete vis-a-vis du pooling d'entites). Les CONTOURS (coreSpeechTextOutlines)
        // n'y sont PAS : ils rendent le meme terme mais ne declenchent aucune lecture,
        // donc zero doublon.
        public static readonly HashSet<PugText> SpeechTexts = new HashSet<PugText>();
    }

    [HarmonyPatch(typeof(TheCore), nameof(TheCore.OnOccupied))]
    internal static class TheCoreOnOccupiedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(TheCore __instance)
        {
            if (__instance.coreSpeechText != null)
                CoreSpeechState.SpeechTexts.Add(__instance.coreSpeechText);
        }
    }

    [HarmonyPatch(typeof(TheCore), nameof(TheCore.OnFree))]
    internal static class TheCoreOnFreePatch
    {
        [HarmonyPostfix]
        public static void Postfix(TheCore __instance)
        {
            if (__instance.coreSpeechText != null)
                CoreSpeechState.SpeechTexts.Remove(__instance.coreSpeechText);
        }
    }

    // Render(string text, bool rewindEffectAnims, bool force, bool activate) : la
    // surcharge que TheCore appelle (toujours avec rewindEffectAnims=true, ce qui force
    // un vrai rendu a chaque replique). Le check Count==0 en tete rend le cout nul tant
    // qu'aucun Core n'est charge (cas general) ; sinon un Contains sur un HashSet de 1.
    [HarmonyPatch(typeof(PugText), nameof(PugText.Render),
        new[] { typeof(string), typeof(bool), typeof(bool), typeof(bool) })]
    internal static class PugTextRenderCoreSpeechPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PugText __instance)
        {
            if (CoreSpeechState.SpeechTexts.Count == 0) return;
            if (!CoreSpeechState.SpeechTexts.Contains(__instance)) return;

            var announcement = TtsText.ResolvePugText(__instance);
            if (string.IsNullOrEmpty(announcement)) return;

            // Marqueurs de pause/emphase de l'effet syllabes ('*' = starDelay,
            // '`' = backtickDelay) : structurels, pas du texte -> retires pour que NVDA
            // ne les epelle pas, puis espaces compactes.
            if (announcement.IndexOf('*') >= 0 || announcement.IndexOf('`') >= 0)
            {
                announcement = announcement.Replace("*", "").Replace("`", "");
                announcement = System.Text.RegularExpressions.Regex.Replace(announcement, @"\s+", " ").Trim();
                if (announcement.Length == 0) return;
            }

            // interrupt=true : une nouvelle replique remplace la precedente (le joueur
            // avance dans le dialogue a son rythme, pas de file qui s'accumule).
            TtsText.Say(announcement, true);
        }
    }
}
