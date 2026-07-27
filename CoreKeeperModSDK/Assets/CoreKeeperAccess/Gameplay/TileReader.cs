using System.Collections.Generic;
using CoreKeeperAccess.Navigation;
using Pug.Automation;
using Pug.Properties;
using PugTilemap;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;

namespace CoreKeeperAccess.Gameplay
{
    // Etat de croissance d'une plante posee sur la case (agriculture). None = pas une
    // plante. Lu depuis l'ObjectIndex (qui balaye deja toutes les entites) : GrowingCD =
    // plante en croissance, + tag HasFinishedGrowingCD a maturite = recoltable.
    public enum PlantState : byte { None = 0, Growing = 1, Ready = 2 }

    // Etat d'alimentation electrique d'un objet d'automation (cable, machine).
    // None = pas d'etat de tension a annoncer (cable/conducteur pur, ou pas electrique).
    // Off/On = consommateur dont le jeu afficherait l'icone manque/assez de courant.
    // Source = generateur (sourceEnergy>0) : il PRODUIT, jamais "hors tension".
    public enum PowerState : byte { None = 0, Off = 1, On = 2, Source = 3 }

    // Etat d'une porte/portail/levier a bascule. None = pas un objet a bascule connu.
    // Off = ferme (porte/portail) ou desactive (levier) ; On = ouvert / active.
    public enum ToggleState : byte { None = 0, Off = 1, On = 2 }

    // Contenu remarquable d'une case, decouple de tout systeme : se passe en parametre.
    // Rempli par TileScan.Read et consomme par la sonification partagee
    // (BuildModeNavigator.SonifyTile), utilisee par le curseur ET la canne laser.
    public struct TileInfo
    {
        public TileType Ground;        // type de sol de la case
        public int GroundTileset;
        public bool HasWall;           // tuile bloquante (mur) presente
        public TileType WallType;
        public int WallTileset;
        public bool HasOre;            // couche minerai (ore / ancientCrystal), independante du mur
        public bool IsImmune;          // couche immune (Grande Muraille...) : mur INVULNERABLE
        public ObjectID ObjectId;      // objet/construction pose sur la case (ou None)
        public bool ObjectInteractable; // l'entite porte InteractableObjectReferenceCD (vrai interactible)
        public PlantState Plant;       // si une plante est posee la : etat de croissance
        public bool Conveyor;          // l'objet est un convoyeur (MoverCD)
        public int2 ConveyorDir;       // sens de transport (signe de stop - start), si convoyeur
        public PowerState Power;       // alimentation electrique (cable / machine), None si pas electrique
        public int Connections;        // cotes connectes au reseau electrique (ElectricityDirectionMask brut), 0 = aucun
        public bool HasStorage;        // l'objet est un stockage d'automation (StorageCD)
        public int StorageCount;       // nombre d'objets dedans (0 = vide), si HasStorage
        public PowerState WirePower;   // tension d'un cable present sur la case (sous un objet non electrique), None sinon
        public ObjectID WireObjectId;  // identite du cable dont WirePower rapporte la tension (None si aucun)
        public ToggleState Toggle;     // etat ouvert/ferme (porte, portail) ou active/desactive (levier), None sinon
        public bool Lit;               // detecteur d'obscurite : case eclairee (roofHole ou source ponctuelle), cf. LightIndex
        public bool RoofHole;          // plafond troue (couche roofHole) : case a l'air libre / ciel ouvert
    }

    // Pont mod <-> systeme ECS. Le TileAccessor ne se construit que depuis un
    // SystemState : seul notre systeme ECS peut lire une case. Le curseur (cote mod)
    // depose la case a lire dans TileQuery, le systeme la lit chaque frame et publie
    // le resultat ici. (Valide en jeu : un systeme du mod s'enregistre en hot-compile,
    // pas de build Unity necessaire.)
    internal static class TileQuery
    {
        public static bool Active;        // le curseur veut une lecture
        public static int2 Tile;          // case demandee (coordonnee monde)

        public static bool ResultValid;   // un resultat a ete publie
        public static int2 ResultTile;    // case effectivement lue
        public static TileType Ground;
        public static int GroundTileset;
        public static bool HasWall;
        public static TileType WallType;
        public static int WallTileset;
        public static bool HasOre;
        public static bool IsImmune;
        public static ObjectID ObjectId;
        public static bool ObjectInteractable;
        public static PlantState Plant;
        public static bool Conveyor;
        public static int2 ConveyorDir;
        public static PowerState Power;
        public static int Connections;
        public static bool HasStorage;
        public static int StorageCount;
        public static PowerState WirePower;
        public static ObjectID WireObjectId;
        public static ToggleState Toggle;
        public static bool Lit;
        public static bool RoofHole;

        // Vue figee de la case courante, pour la passer a la sonification partagee.
        public static TileInfo Snapshot() => new TileInfo
        {
            Ground = Ground,
            GroundTileset = GroundTileset,
            HasWall = HasWall,
            WallType = WallType,
            WallTileset = WallTileset,
            HasOre = HasOre,
            IsImmune = IsImmune,
            ObjectId = ObjectId,
            ObjectInteractable = ObjectInteractable,
            Plant = Plant,
            Conveyor = Conveyor,
            ConveyorDir = ConveyorDir,
            Power = Power,
            Connections = Connections,
            HasStorage = HasStorage,
            StorageCount = StorageCount,
            WirePower = WirePower,
            WireObjectId = WireObjectId,
            Toggle = Toggle,
            Lit = Lit,
            RoofHole = RoofHole,
        };
    }

    // Pont prospection minerai (commande Triangle + gauche). Le mod pose une demande
    // (centre = case du joueur, rayon = stat VisibleOreDistance du perso - la MEME
    // valeur que le shader des paillettes cote voyant, donc equite stricte et talents
    // de minage respectes) ; le systeme balaye la zone et publie la tuile de minerai
    // la plus proche (couche ore / ancientCrystal, a travers les murs, comme les
    // paillettes du jeu qui s'affichent meme filon enfoui).
    internal static class OreScan
    {
        public static bool Requested;   // demande posee par le mod (consommee par le systeme)
        public static int2 Center;      // case du joueur
        public static int Radius;       // rayon en cases

        public static bool ResultValid; // reponse publiee (consommee par le mod)
        public static bool Found;
        public static int2 Tile;        // veine de minerai la plus proche (couche tuile)

        // Gisements a foreuse (objets poses, ObjectIndex.Entry.Resource) - resultat SEPARE
        // de la veine ci-dessus : les deux peuvent etre trouves en meme temps, l'un ne doit
        // pas ecraser l'annonce de l'autre. Depuis le 27 juillet 2026 on en publie
        // PLUSIEURS, du plus proche au plus lointain (demande testeur : deux gisements cote
        // a cote, un seul etait annonce). Dedoublonnes par ENTITE dans ScanOre - un gisement
        // occupe 2x2 cases dans l'index, sans ca il sortirait quatre fois.
        public const int MaxDeposits = 4;
        public static int DepositCount;
        public static readonly int2[] DepositTiles = new int2[MaxDeposits];
    }

    // Pont recalcul local du reseau de navigation (tranche C, "mise a jour du reseau"). Le
    // mod pose une demande (centre = case joueur, rayon de revision) ; le systeme la traite
    // avec son TileAccessor (NetworkWeaver tisse les aretes manquantes par LIGNE DE VUE
    // franchissable) et publie le nombre d'aretes ajoutees. Le mod l'annonce. AJOUT seulement.
    internal static class NetworkRecalc
    {
        public static bool Requested;   // demande posee par le mod (consommee par le systeme)
        public static int2 Center;      // case du joueur
        public static float Radius;     // rayon de revision en cases

        public static bool ResultValid; // reponse publiee (consommee par le mod)
        public static int AddedEdges;   // aretes ajoutees (lignes de vue degagees)
        public static int RemovedEdges; // aretes coupees (lignes de vue obstruees)
        public static int LostNodes;    // noeuds fantomes elagues (balise disparue, feuille)
    }

    // Pont DUMP ASCII du reseau local (dev). Le mod pose une demande (centre, rayon) ; le
    // systeme dessine dans Player.log une grille "vue par le mod" : # = mur LU, . = sol,
    // = passage (pont/porte sur tuile bloquante), lettre = noeud, @ = joueur, + la liste des
    // aretes intra-fenetre (par lettres). A comparer a une capture carte : valide a la fois la
    // coherence interne (aucune arete a travers un #) ET la lecture des murs (# lus vs reels).
    internal static class NetworkDump
    {
        public static bool Requested;
        public static int2 Center;
        public static float Radius;
    }

    // Pont DUMP eclairage/plafond (dev, Triangle+F3). Le mod pose une demande (centre, rayon) ;
    // le systeme dessine dans Player.log une grille "vue par le mod" : # = mur LU, R = tuile
    // roofHole (trou perce dans le plafond implicite), . = sol ferme (pas de trou), @ = joueur.
    // Sert a verifier en jeu la mecanique observee (Azeos qui perce le plafond de ses attaques,
    // biome desert eclaire sans plafond a percer). Cf. [[core-keeper-ingame-data-access]].
    internal static class LightDump
    {
        public static bool Requested;
        public static int2 Center;
        public static int Radius;
    }

    // Pont sources lumineuses ponctuelles (torche/lanterne/glow d'entite...), publie par le
    // mod cote MonoBehaviour (CoreKeeperAccessMod.PublishLightSources, reflexion sur
    // ManagedLight.allLights - un systeme ECS ne peut pas lire cette liste Unity). Positions
    // DEJA en coordonnees MONDE (+ Manager.camera.RenderOrigo), filtrees isLightEnabled,
    // bornees en distance au joueur (perf). Consomme par LightIndex.IsLit. Cf.
    // core-keeper-darkness-gate.md (design fige 16 juillet 2026).
    internal static class LightSourceScan
    {
        public const int MaxSources = 64;
        public static int Count;
        public static readonly float2[] Pos = new float2[MaxSources];
        public static readonly float[] Range = new float[MaxSources];
        // true = entite du monde (torche posee, neverOptimize=false cote jeu) ; false = glow
        // attache au joueur (lanterne equipee, glow de condition, neverOptimize=true). Distingue
        // le bleed a appliquer, cf. calibration 17 juillet (core-keeper-darkness-gate.md).
        public static readonly bool[] IsWorldEntity = new bool[MaxSources];
    }

    // Detecteur d'obscurite (design fige 16 juillet 2026, cf. core-keeper-darkness-gate.md) :
    // une case est "eclairee" si une source ponctuelle active la couvre a <= range+3 (bleed
    // calibre a l'oeil sur PLUSIEURS points reels, torches/lanterne, cf.
    // core-keeper-ingame-data-access.md) OU si un roofHole existe a <= 5 cases (meme fiche).
    // Parite avec un joueur voyant : les outils de nav (curseur/canne/scanner/sonar/collision)
    // ne doivent rien reveler sur une case que ce filtre juge sombre. Appelable depuis TOUT
    // systeme qui a deja un TileAccessor.
    //
    // ⚠ 16 juillet 2026 : une tentative de rendre ce bleed PROPORTIONNEL au range (suite a
    // une creature lumineuse range=2 semblant sur-eclairee) a ete REVERTEE - la correction
    // etait basee sur UN SEUL point de donnee mal mesure (pas de comptage precis, juste une
    // impression), et touchait du meme coup la constante torches qui, elle, EST calibree
    // sur plusieurs points reels (piege signale par l'utilisateur : "t'as bousille nos calculs
    // sur un coup de tete"). Rien ne prouve non plus que les lumieres de creature suivent le
    // MEME mecanisme que les torches (verifie : IndirectLightRenderFeature est un effet GPU
    // global en espace ecran, aucune distinction de code par type d'entite - mais ca ne
    // prouve pas l'absence d'une autre difference, ex. parametres du Light lui-meme). Le flat
    // +3 reste la seule valeur avec de vraies donnees derriere -> gardee telle quelle tant
    // qu'une VRAIE calibration (Triangle+F3, grille "Lit" comparee case par case au damier,
    // cf. DumpLight) n'a pas ete faite sur des lumieres de creature/plante.
    internal static class LightIndex
    {
        private const int RoofHoleRadius = 5;
        // Bleed calibre au voyant le 17 juillet 2026 (plusieurs points reels, cf.
        // core-keeper-darkness-gate.md) : les torches posees (entites du monde) portent
        // reellement 1 case de plus que leur range brut. Les glows attaches au joueur (lanterne
        // equipee, glow de condition, nourriture) n'ont PAS de marge fiable qui tienne dans les
        // deux sens (lanterne orange : range-1 reel ; glow bleu : range+1 reel) -> aucun
        // modificateur, valeur brute prise telle quelle plutot qu'une moyenne inventee.
        private const float TorchBleed = 1f;
        private const float GlowBleed = 0f;

        public static bool IsLit(ref TileAccessor ta, int2 tile)
        {
            if (!A11ySettings.DarknessGate) return true; // coupe-circuit : feature desactivee -> toujours "eclaire"

            // Sources ponctuelles d'abord (liste courte en general - moins cher que le
            // balayage roofHole dans le cas courant "pres d'une torche").
            float2 ft = new float2(tile.x, tile.y);
            for (int i = 0; i < LightSourceScan.Count; i++)
            {
                float bleed = LightSourceScan.IsWorldEntity[i] ? TorchBleed : GlowBleed;
                if (math.distance(LightSourceScan.Pos[i], ft) <= LightSourceScan.Range[i] + bleed)
                    return true;
            }

            int r2 = RoofHoleRadius * RoofHoleRadius;
            for (int dy = -RoofHoleRadius; dy <= RoofHoleRadius; dy++)
            {
                for (int dx = -RoofHoleRadius; dx <= RoofHoleRadius; dx++)
                {
                    if (dx * dx + dy * dy > r2) continue;
                    if (ta.HasType(new int2(tile.x + dx, tile.y + dy), TileType.roofHole)) return true;
                }
            }
            return false;
        }
    }

    // CheckerStamp (pont banc de test damier, Triangle+F10) deplace dans Gameplay/DevTools.cs,
    // avec le systeme SERVEUR qui le consomme (cf. leçon CheckerStampSystem/DevInvincibilitySystem).

    // Pont du scanner de proximite (R3 tenu). PUBLICATION CONTINUE (pas de demande/reponse
    // consommee comme PingScan) : le mod publie chaque frame le rectangle camera courant
    // (meme convention que la sentinelle d'aggro, AggroScan.CamHal) et le systeme rafraichit
    // Targets/Count a son propre rythme (VisInterval) - le mod lit "la derniere photo connue",
    // acceptable pour une liste qui se reconstruit ~4 Hz. Creatures (ennemis / passifs / PNJ
    // marchands) uniquement : les objets poses (coffres, plantes, ressources) sont lus cote
    // mod directement dans ObjectIndex, pas besoin d'ECS pour eux.
    internal static class VisibilityScan
    {
        public struct Creature
        {
            public long Key;   // identite d'entite (EntityKey.Of), suit une cible qui bouge
            public float2 Pos;
            public ObjectID Obj;
            public ProximityScanner.Category Cat;
        }

        public const int MaxTargets = 32;

        public static bool Active;      // le mod veut le scan (scanner active + jeu normal)
        public static float2 Center;    // position joueur
        public static float2 CamHalf;   // demi-largeur/hauteur ecran en cases (+ marge), cf. AggroScan

        public static int Count;
        public static readonly Creature[] Targets = new Creature[MaxTargets];
    }

    // Pont du sonar de proximite. Le mod pose une demande (case du joueur) ; le systeme lit
    // les 8 directions (cardinales + diagonales) jusqu'a 2 cases et publie, par direction, la
    // texture du 1er obstacle (0=libre, 1=mur, 2=trou/eau) et sa distance (1 ou 2). Ordre des
    // directions = horaire depuis le nord, identique a ProximitySonar (Dx/Dy).
    internal static class SonarScan
    {
        public static bool Requested;
        public static int2 Center;
        // Joueur en bateau : l'eau cesse d'etre un obstacle (c'est la surface sur laquelle
        // on avance). Pose par le mod avec la demande, cf. PlayerRide.
        public static bool OnWater;
        public static bool ResultValid;
        public static readonly int[] Tex = new int[4];
        public static readonly int[] Dist = new int[4];
        // Couche v2 : objet pose detecte dans la direction (<= 2 cases) + sa distance (1 ou 2).
        public static readonly bool[] Obj = new bool[4];
        public static readonly int[] ObjDist = new int[4];
    }

    // Pont du detecteur de collision directionnel (etage 3 navigation, stick gauche). Le mod
    // pose une demande (case du joueur, direction NORMALISEE de l'intention de marche, portee
    // en cases) ; le systeme avance le long de la direction (DDA, meme technique que la canne
    // laser) et publie la distance du premier infranchissable (mur/pit/eau), ou Found=false si
    // la portee est franche.
    internal static class CollisionScan
    {
        public static bool Requested;
        public static int2 Center;
        public static float2 Direction;
        public static float MaxRange;
        // Joueur en bateau : l'eau n'alerte plus (sinon la nappe reste au maximum en
        // permanence en navigation - retour testeur 27 juillet 2026). Cf. PlayerRide.
        public static bool OnWater;

        public static bool ResultValid;
        public static bool Found;
        public static float Dist;
    }

    // Pont détecteur de sol dangereux (sol vaseux acide...). Scanné en continu à ~10 Hz
    // par TileReaderSystem : carré 5×5 via TileAccessor + PugDatabase.TryGetTileItemInfo
    // pour identifier le tileset sans hardcoder sa valeur numérique. FireProximity lit
    // Found/Tile pour inclure les tuiles de sol dans l'alerte positionnelle.
    internal static class HazardGroundScan
    {
        public static bool Found;
        public static int2 Tile;   // case la plus proche dans le rayon 2
    }

    // Index case -> objet pose, reconstruit periodiquement depuis les ENTITES
    // (position + emprise prefab lue dans PugDatabase). Capte les objets SANS
    // collider physique - etabli en fer, generateur, Core, torches... - que les
    // sondes physiques de TileScan.ObjectAt ratent (confirme par [A11yTileDiag] :
    // obj=None sur leurs cases). Rempli par TileReaderSystem (~4 Hz, rayon borne
    // autour du joueur), consulte en dernier recours par ObjectAt.
    internal static class ObjectIndex
    {
        public struct Entry
        {
            public ObjectID Id;
            public bool Interactable;
            public PlantState Plant; // si l'entite est une plante : son etat de croissance
            public bool Conveyor;    // automation : convoyeur (MoverCD)
            public int2 ConveyorDir; // sens de transport
            public PowerState Power; // automation : alimentation electrique
            public int Connections;  // automation : cotes connectes (ElectricityDirectionMask brut)
            public bool HasStorage;  // automation : stockage (StorageCD)
            public int StorageCount; // nombre d'objets dans le stockage
            public bool Infra;       // cable / conducteur electrique pur : cede la case a toute machine
            public bool Resource;    // gisement minable (PugAutomationCD.type Mineable) : priorite haute
            public ToggleState Toggle; // porte/portail/levier a bascule : etat ouvert/ferme ou active/desactive
            public Entity Ent;       // entite source (pour le diagnostic automation dev)
        }

        // Cable present sur la case, INDEPENDANT de l'objet gagnant de l'index : un cable
        // cede la case a la machine posee dessus, mais on garde sa tension ET son identite
        // ici pour pouvoir l'annoncer separement ("cable electrique, sous tension") meme
        // quand une machine masque la case dans Map.
        public struct WireEntry
        {
            public PowerState Power;
            public ObjectID Id;
        }

        public static float2 Center; // position joueur, publiee par le mod (GameplayInput)
        public static readonly Dictionary<long, Entry> Map = new Dictionary<long, Entry>();
        public static readonly Dictionary<long, WireEntry> WireMap = new Dictionary<long, WireEntry>();

        public static long Key(int2 t) => ((long)t.x << 32) ^ (uint)t.y;

        public static bool TryGet(int2 t, out Entry e) => Map.TryGetValue(Key(t), out e);
    }

    // Pont du repere de centre : position de la zone d'invocation (SummonArea = centre
    // de l'arene de boss) la plus proche, captee par TileReaderSystem au fil de son scan
    // d'objets (aucun scan dedie). Found=false = aucune SummonArea a portee (cas normal
    // hors arene) -> le drone du repere se tait.
    internal static class SummonScan
    {
        public static bool Found;
        public static float2 Pos;
    }

    // Pont diagnostic AUTOMATION (mode dev seulement). Pose par le combo details
    // (Triangle+haut de BuildModeNavigator) quand le curseur est sur une machine
    // industrielle : le systeme dumpe dans Player.log TOUS les composants de l'entite
    // + les valeurs des composants automation connus. Sert a finaliser l'a11y industrie
    // sur du concret (le contenu d'automation n'est atteignable en jeu qu'avec l'ecarlate
    // - impossible a tester autrement). Cf. methode gravee : log = seule verite.
    internal static class AutomationDiag
    {
        public static bool Requested;
        public static int2 Tile;
    }

    // Lecture d'une case (sol / mur / minerai / objet pose), partagee par les systemes
    // ECS du mod (curseur de tuile, canne laser). Doit etre appelee depuis un systeme
    // (le TileAccessor vient de son SystemState) ; le CollisionWorld et le World sont
    // passes pour la requete spatiale d'objet.
    internal static class TileScan
    {
        // Filtre large : requiredObjectFilter ne couvre que quelques couches (etabli,
        // coffre) et rate le Core. On ratisse toutes les couches et on filtre ensuite
        // par "a un ObjectDataCD nomme" -> capte tout objet/construction pose, Core inclus.
        private static readonly CollisionFilter AnyObjectFilter = new CollisionFilter
        {
            BelongsTo = uint.MaxValue,
            CollidesWith = uint.MaxValue,
        };

        public static TileInfo Read(ref TileAccessor ta, int2 t, World world)
        {
            var info = new TileInfo();
            var groundTile = ta.GetTop(t);
            info.Ground = groundTile.tileType;
            info.GroundTileset = groundTile.tileset;
            bool hasWall = ta.TryGetBlockingTile(t, out TileCD wall, true);
            info.HasWall = hasWall;
            info.WallType = hasWall ? wall.tileType : default;
            info.WallTileset = hasWall ? wall.tileset : 0;
            info.HasOre = ta.HasType(t, TileType.ore) || ta.HasType(t, TileType.ancientCrystal);
            info.IsImmune = ta.HasType(t, TileType.immune);
            // L'index d'objets est l'AUTORITE : il gere la priorite (cable cede a la machine)
            // et capte les objets sans collider. Quand il a une entree, le NOM ET les attributs
            // (orientation, tension, stock...) viennent de la MEME entree -> coherence. Avant :
            // le nom venait de la sonde physique (qui voit le cable, il a un collider) tandis
            // que l'orientation/tension venaient de l'index (la machine gagnante) -> annonce
            // batarde "Cable electrique, vers Sud" sur une case foreuse+cable.
            if (ObjectIndex.TryGet(t, out var pe))
            {
                info.ObjectId = pe.Id;
                info.ObjectInteractable = pe.Interactable;
                info.Plant = pe.Plant;
                info.Conveyor = pe.Conveyor;
                info.ConveyorDir = pe.ConveyorDir;
                info.Power = pe.Power;
                info.Connections = pe.Connections;
                info.HasStorage = pe.HasStorage;
                info.StorageCount = pe.StorageCount;
                info.Toggle = pe.Toggle;
            }
            else
            {
                // Fallback : sonde physique (objet pose pas encore reindexe ~0,25 s, cas limite).
                info.ObjectId = ObjectAt(t, world, out bool interactable);
                info.ObjectInteractable = interactable;
            }
            // Tension + identite d'un cable present sur la case, qu'il soit l'objet principal
            // ou masque sous une machine -> permet de l'annoncer meme sous une structure non
            // electrique (cf. AnnounceCursorDetails, clause cable dediee).
            if (ObjectIndex.WireMap.TryGetValue(ObjectIndex.Key(t), out var wp))
            {
                info.WirePower = wp.Power;
                info.WireObjectId = wp.Id;
            }
            info.Lit = LightIndex.IsLit(ref ta, t);
            info.RoofHole = ta.HasType(t, TileType.roofHole);
            return info;
        }

        // Objet/construction pose sur la case : requete spatiale (les objets sont des
        // entites, pas des tuiles). None si rien. interactable = l'entite trouvee porte
        // InteractableObjectReferenceCD (vrai interactible vs deco passive).
        // DEUX sondes : au sol (objets simples), puis a MI-HAUTEUR si rien - les
        // grosses structures (Core, generateur, etabli en fer...) ont leur collider
        // centre a y+0.5 (confirme par PlacementHandler : le jeu teste l'occupation
        // d'une case avec un box cast a tuile + 0.5 en Y) et la sonde au sol passe
        // litteralement SOUS elles.
        public static ObjectID ObjectAt(int2 t, World world, out bool interactable)
        {
            var cw = PhysicsManager.GetCollisionWorld();
            ObjectID id = Probe(cw, new float3(t.x, 0f, t.y), 0.4f, world, out interactable);
            if (id == ObjectID.None)
                id = Probe(cw, new float3(t.x, 0.5f, t.y), 0.45f, world, out interactable);
            // Index d'entites (objets sans collider physique - etabli en fer,
            // generateur, Core, torches...). Il complete les sondes ET les CORRIGE :
            // une machine posee SUR le cable ancien (qui, lui, a un collider) etait
            // masquee par lui -> un INTERACTIBLE de l'index prime sur un
            // non-interactible rendu par la sonde.
            if (ObjectIndex.TryGet(t, out var e)
                && (id == ObjectID.None || (e.Interactable && !interactable)))
            {
                interactable = e.Interactable;
                id = e.Id;
            }
            return id;
        }

        private static ObjectID Probe(CollisionWorld cw, float3 pos, float radius,
            World world, out bool interactable)
        {
            interactable = false;
            var hits = new NativeList<DistanceHit>(8, Allocator.Temp);
            ObjectID id = ObjectID.None;
            if (cw.OverlapSphere(pos, radius, ref hits, AnyObjectFilter, QueryInteraction.Default))
            {
                foreach (var h in hits)
                {
                    // Les creatures et joueurs portent aussi un ObjectDataCD : on ne
                    // veut que les objets POSES (la sonde a mi-hauteur attraperait un
                    // slime de passage et l'annoncerait comme un meuble). Idem pour les
                    // projectiles en vol (fleches du joueur, mortiers...).
                    bool isCattleEntity = EntityUtility.HasComponentData<ObjectDataCD>(h.Entity, world)
                        && ProximityScanner.CattleIds.Contains(
                            EntityUtility.GetComponentData<ObjectDataCD>(h.Entity, world).objectID);
                    if ((!isCattleEntity && (EntityUtility.HasComponentData<EnemyCD>(h.Entity, world)
                            || EntityUtility.HasComponentData<CritterCD>(h.Entity, world)))
                        || EntityUtility.HasComponentData<PlayerGhost>(h.Entity, world)
                        || EntityUtility.HasComponentData<ProjectileCD>(h.Entity, world)
                        || EntityUtility.HasComponentData<MortarProjectileCD>(h.Entity, world)) continue;
                    if (EntityUtility.HasComponentData<ObjectDataCD>(h.Entity, world))
                    {
                        var od = EntityUtility.GetComponentData<ObjectDataCD>(h.Entity, world);
                        if (od.objectID != ObjectID.None)
                        {
                            id = od.objectID;
                            interactable = EntityUtility.HasComponentData<InteractableObjectReferenceCD>(h.Entity, world);
                            break;
                        }
                    }
                }
            }
            hits.Dispose();
            return id;
        }
    }

    // Calcul de l'etat ouvert/ferme (porte, portail) ou active/desactive (levier) d'une
    // entite. Partage entre l'index d'objets (TileReaderSystem, autour du joueur) et la
    // surveillance de l'interactible a portee (GameplayInput.WatchInteractable, qui lit
    // directement l'entite currentClosestInteractable) - meme regle, deux points d'entree.
    internal static class ToggleLogic
    {
        // Portes/portails en BOIS (et variantes pierre/ecarlate/corail/galaxite/bois lumineux) :
        // pas de composant d'etat dedie, l'etat ouvert/ferme est lu via la parite de la
        // variation (cf. Compute). N'inclut PAS les portes electriques (SwapColliderCD
        // couvre celles-la) ni PuzzleDoor (verrou a cle, mecanique differente).
        private static readonly HashSet<ObjectID> WoodGateDoorIds = new HashSet<ObjectID>
        {
            ObjectID.WoodFenceGate, ObjectID.StoneFenceGate, ObjectID.ScarletFenceGate,
            ObjectID.CoralFenceGate, ObjectID.GalaxiteFenceGate, ObjectID.GleamWoodFenceGate,
            ObjectID.WoodDoor, ObjectID.StoneDoor, ObjectID.ScarletDoor,
            ObjectID.GleamWoodDoor, ObjectID.CoralDoor, ObjectID.GalaxiteDoor,
        };

        // Electrique (porte/portail elec) -> SwapColliderCD.swap (bool replique reseau,
        // source fiable - c'est le champ que le jeu lui-meme utilise pour animer l'ouverture).
        // Bois (porte/portail classique) + levier -> pas de composant d'etat dedie, encode
        // dans la VARIATION de l'ObjectDataCD ; parite confirmee par decompil (Gate : case
        // impaire = "Open" explicite dans le code jeu, Lever : 1 = allume) -> IMPAIR = ouvert/
        // actif, PAIR = ferme/inactif. Mapping pas teste en jeu, a valider a l'oreille.
        public static ToggleState Compute(Entity e, World world, ObjectID objectId, int variation)
        {
            if (EntityUtility.HasComponentData<SwapColliderCD>(e, world))
            {
                return EntityUtility.GetComponentData<SwapColliderCD>(e, world).swap
                    ? ToggleState.On : ToggleState.Off;
            }
            if (objectId == ObjectID.Lever || WoodGateDoorIds.Contains(objectId))
                return (variation % 2 != 0) ? ToggleState.On : ToggleState.Off;
            return ToggleState.None;
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class TileReaderSystem : SystemBase
    {
        private const float IndexInterval = 0.25f; // ~4 Hz, assez frais pour un curseur humain
        private const float IndexRadius = 24f;     // cases autour du joueur (couvre l'ecran)

        private EntityQuery _objQuery;
        private EntityQuery _dbQuery;
        private EntityQuery _visQuery;
        private float _nextIndex;
        private float _nextHazardScan;
        private float _nextVis;
        private const float VisInterval = 0.25f; // ~4 Hz, meme cadence que l'index d'objets

        protected override void OnCreate()
        {
            // ObjectDataCD SEUL : exiger un composant de transform dans la query
            // excluait mysterieusement certaines entites (le generateur electrique
            // matchait Query(ObjectDataCD) et HasComponent<LocalToWorld> rendait true,
            // mais Query(ObjectDataCD, LocalToWorld) ne le voyait pas). On requete
            // large et on lit la position composant par composant dans la boucle.
            _objQuery = GetEntityQuery(ComponentType.ReadOnly<ObjectDataCD>());
            _dbQuery = GetEntityQuery(ComponentType.ReadOnly<PugDatabase.DatabaseBankCD>());
            // Creatures pour le scanner de proximite : tout ce qui porte EnemyCD, CritterCD OU
            // FactionCD (les deux premiers sont exclusifs entre eux ; FactionCD elargit aux PNJ
            // marchands, qui ne portent ni l'un ni l'autre).
            _visQuery = GetEntityQuery(new EntityQueryDesc
            {
                Any = new[]
                {
                    ComponentType.ReadOnly<EnemyCD>(),
                    ComponentType.ReadOnly<CritterCD>(),
                    ComponentType.ReadOnly<FactionCD>(),
                },
            });
        }

        protected override void OnUpdate()
        {
            RebuildObjectIndex();
            // Scanner de proximite : creatures visibles a l'ecran, rafraichi en continu tant
            // que le mod le demande (pas de request/consume : le scanner lit la derniere photo).
            if (VisibilityScan.Active && UnityEngine.Time.unscaledTime >= _nextVis)
            {
                _nextVis = UnityEngine.Time.unscaledTime + VisInterval;
                try
                {
                    var taVis = new TileAccessor(ref CheckedStateRef, true);
                    ScanVisibility(ref taVis);
                }
                catch (System.Exception ex)
                {
                    VisibilityScan.Count = 0;
                    Diag.Error("A11yScannerDiag", ex);
                }
            }
            // Prospection minerai : independante du curseur (TileQuery peut etre inactif).
            if (OreScan.Requested)
            {
                OreScan.Requested = false;
                try
                {
                    var taOre = new TileAccessor(ref CheckedStateRef, true);
                    ScanOre(ref taOre);
                }
                catch (System.Exception ex)
                {
                    OreScan.Found = false;
                    OreScan.DepositCount = 0;
                    OreScan.ResultValid = true;
                    Diag.Error("A11yOreDiag", ex);
                }
            }

            // Sonar de proximite : balaye les 8 directions a la demande (independant du curseur).
            if (SonarScan.Requested)
            {
                SonarScan.Requested = false;
                try
                {
                    var taSonar = new TileAccessor(ref CheckedStateRef, true);
                    ScanSonar(ref taSonar);
                }
                catch (System.Exception ex)
                {
                    SonarScan.ResultValid = true;
                    Diag.Error("A11ySonarDiag", ex);
                }
            }

            // Detecteur de collision directionnel : DDA a la demande (independant du curseur).
            if (CollisionScan.Requested)
            {
                CollisionScan.Requested = false;
                try
                {
                    var taCol = new TileAccessor(ref CheckedStateRef, true);
                    ScanCollision(ref taCol);
                }
                catch (System.Exception ex)
                {
                    CollisionScan.Found = false;
                    CollisionScan.ResultValid = true;
                    Diag.Error("A11yCollisionDiag", ex);
                }
            }

            // Sol dangereux : scan continu ~20 Hz du carré 5×5 autour du joueur.
            if (UnityEngine.Time.unscaledTime >= _nextHazardScan)
            {
                _nextHazardScan = UnityEngine.Time.unscaledTime + 0.05f;
                try
                {
                    var taH = new TileAccessor(ref CheckedStateRef, true);
                    ScanHazardGround(ref taH);
                }
                catch (System.Exception ex)
                {
                    HazardGroundScan.Found = false;
                    Diag.Error("A11yHazardScan", ex);
                }
            }

            // Recalcul local du reseau de navigation (tranche C) : tisse les aretes manquantes
            // par ligne de vue dans le rayon de revision. Independant du curseur.
            if (NetworkRecalc.Requested)
            {
                NetworkRecalc.Requested = false;
                try
                {
                    var taNet = new TileAccessor(ref CheckedStateRef, true);
                    CoreKeeperAccess.Navigation.NetworkWeaver.Weave(
                        ref taNet, NetworkRecalc.Center, NetworkRecalc.Radius,
                        out int added, out int removed, out int lost);
                    NetworkRecalc.AddedEdges = added;
                    NetworkRecalc.RemovedEdges = removed;
                    NetworkRecalc.LostNodes = lost;
                    NetworkRecalc.ResultValid = true;
                }
                catch (System.Exception ex)
                {
                    NetworkRecalc.AddedEdges = 0;
                    NetworkRecalc.RemovedEdges = 0;
                    NetworkRecalc.LostNodes = 0;
                    NetworkRecalc.ResultValid = true;
                    Diag.Error("A11yNetRecalc", ex);
                }
            }

            // Dump ASCII du reseau local (dev) : dessine la zone vue par le mod dans le log.
            if (NetworkDump.Requested)
            {
                NetworkDump.Requested = false;
                try { DumpNetwork(); }
                catch (System.Exception ex) { Diag.Error("A11yNetDump", ex); }
            }

            // Dump ASCII plafond/roofHole (dev) : verification terrain de la mecanique eclairage.
            if (LightDump.Requested)
            {
                LightDump.Requested = false;
                try { DumpLight(); }
                catch (System.Exception ex) { Diag.Error("A11yLightDiag", ex); }
            }

            // Tampon damier (dev) : la demande est consommee cote SERVEUR (CheckerStampSystem,
            // Gameplay/DevTools.cs) - une ecriture ICI (client) serait ecrasee au prochain
            // snapshot NetCode, meme piege que DevInvincibilitySystem. Rien a faire cote client.

            // Diagnostic automation a la demande (dev) : independant du curseur actif.
            if (AutomationDiag.Requested)
            {
                AutomationDiag.Requested = false;
                try { DumpAutomation(); }
                catch (System.Exception ex) { Diag.Error("A11yAutoDiag", ex); }
            }

            if (!TileQuery.Active) return;
            try
            {
                var ta = new TileAccessor(ref CheckedStateRef, true);
                int2 t = TileQuery.Tile;
                var info = TileScan.Read(ref ta, t, World);
                TileQuery.Ground = info.Ground;
                TileQuery.GroundTileset = info.GroundTileset;
                TileQuery.HasWall = info.HasWall;
                TileQuery.WallType = info.WallType;
                TileQuery.WallTileset = info.WallTileset;
                TileQuery.HasOre = info.HasOre;
                TileQuery.IsImmune = info.IsImmune;
                TileQuery.ObjectId = info.ObjectId;
                TileQuery.ObjectInteractable = info.ObjectInteractable;
                TileQuery.Plant = info.Plant;
                TileQuery.Conveyor = info.Conveyor;
                TileQuery.ConveyorDir = info.ConveyorDir;
                TileQuery.Power = info.Power;
                TileQuery.Connections = info.Connections;
                TileQuery.HasStorage = info.HasStorage;
                TileQuery.StorageCount = info.StorageCount;
                TileQuery.WirePower = info.WirePower;
                TileQuery.WireObjectId = info.WireObjectId;
                TileQuery.Toggle = info.Toggle;
                TileQuery.Lit = info.Lit;
                TileQuery.RoofHole = info.RoofHole;
                TileQuery.ResultTile = t;
                TileQuery.ResultValid = true;
            }
            catch (System.Exception ex) { Diag.Error("A11yTileDiag", ex); }
        }

        // Reconstruit l'index case -> objet depuis les entites proches du joueur :
        // position + emprise prefab (prefabTileSize / prefabCornerOffset de la base,
        // memes champs que le placement du jeu). Throttle ~4 Hz, rayon borne. On
        // ecarte creatures et joueurs (eux aussi portent un ObjectDataCD). NB : la
        // rotation des objets n'est pas appliquee (footprint xy brut) - a affiner si
        // un objet long pivote remonte decale.
        private void RebuildObjectIndex()
        {
            if (UnityEngine.Time.unscaledTime < _nextIndex) return;
            _nextIndex = UnityEngine.Time.unscaledTime + IndexInterval;

            try
            {
                ObjectIndex.Map.Clear();
                ObjectIndex.WireMap.Clear();
                if (_dbQuery.IsEmptyIgnoreFilter) return;
                var bank = _dbQuery.GetSingleton<PugDatabase.DatabaseBankCD>();
                float2 center = ObjectIndex.Center;
                float r2 = IndexRadius * IndexRadius;

                // Repere de centre : on capte au passage la SummonArea ET tout
                // BossSpawnLocationCD (sigil d'invocation = centre de l'arene de boss)
                // les plus proches, sans scan dedie (ce balayage d'objets tourne deja
                // a ~4 Hz). Les Titans (Azeos...) utilisent BossSpawnLocationCD, une
                // entite normale repliquee client - contrairement a SummonArea (sol de
                // salle, lu via ServerWorld plus bas), donc capturee ICI directement.
                bool caFound = false; float caBest = float.MaxValue; float2 caPos = default;
                var ents = _objQuery.ToEntityArray(Allocator.Temp);
                foreach (var e in ents)
                {
                    // Position : LocalToWorld d'abord (toute entite rendue), sinon
                    // LocalTransform, sinon l'entite n'est pas localisable -> on passe.
                    float3 pos;
                    if (EntityManager.HasComponent<LocalToWorld>(e))
                        pos = EntityManager.GetComponentData<LocalToWorld>(e).Position;
                    else if (EntityManager.HasComponent<LocalTransform>(e))
                        pos = EntityManager.GetComponentData<LocalTransform>(e).Position;
                    else continue;
                    float2 p = new float2(pos.x, pos.z);
                    if (math.lengthsq(p - center) > r2) continue;

                    if (EntityUtility.HasComponentData<EnemyCD>(e, World)
                        || EntityUtility.HasComponentData<CritterCD>(e, World)
                        || EntityUtility.HasComponentData<PlayerGhost>(e, World)) continue;
                    // Projectiles en vol (fleches du joueur, mortiers...) : des entites
                    // ObjectDataCD ephemeres, pas des objets POSES -> hors index (sinon
                    // chaque fleche tiree bipait au laser et au curseur).
                    if (EntityUtility.HasComponentData<ProjectileCD>(e, World)
                        || EntityUtility.HasComponentData<MortarProjectileCD>(e, World)) continue;

                    var od = EntityManager.GetComponentData<ObjectDataCD>(e);
                    if (od.objectID == ObjectID.None) continue;

                    if (od.objectID == ObjectID.SummonArea)
                        continue; // sol de la salle : position via ServerWorld uniquement

                    // Titans (Azeos...) : BossSpawnLocationCD remplace SummonArea. Meme
                    // traitement que la rune classique - centre de drone + annonce "Rune
                    // d'invocation" injectee plus bas, PAS le nom brut (SplitEnumName sur
                    // "BirdBossSpawnLocation" etc. - invisible pour un voyant, marqueur
                    // technique de spawn uniquement).
                    if (EntityUtility.HasComponentData<BossSpawnLocationCD>(e, World))
                    {
                        var bsl = EntityUtility.GetComponentData<BossSpawnLocationCD>(e, World);
                        if (bsl.bossID != ObjectID.None)
                        {
                            float bd2 = math.lengthsq(p - center);
                            if (bd2 < caBest) { caBest = bd2; caPos = p; caFound = true; }
                        }
                        continue;
                    }

                    int2 size;
                    int2 corner;
                    try
                    {
                        // ref obligatoire (analyseur Unity EA0001) : la donnee vit en blob storage.
                        ref var info = ref PugDatabase.GetEntityObjectInfo(od.objectID, bank.databaseBankBlob, od.variation);
                        size = math.max(info.prefabTileSize, new int2(1, 1));
                        corner = info.prefabCornerOffset;
                    }
                    catch { size = new int2(1, 1); corner = int2.zero; }

                    // Plante (agriculture) : GrowingCD = en croissance ; le tag
                    // HasFinishedGrowingCD apparait a maturite (PlantsGrowingSystem) =
                    // recoltable. Lu une fois ici, porte par l'entree d'index.
                    // Maturite = la MEME condition que la recolte du jeu
                    // (HoeSlot.EntityIsPlantReadyForHarvest) : GrowingCD.HasFinishedGrowing
                    // (currentStage >= nb de stades, lu dans ObjectPropertiesCD). PAS le tag
                    // HasFinishedGrowingCD : il n'est pas pose sur les plantes qui repoussent
                    // (ex. baie en coeur -> annoncee "a soif" a tort une fois mure).
                    PlantState plant = PlantState.None;
                    if (EntityUtility.HasComponentData<GrowingCD>(e, World))
                    {
                        bool ready = false;
                        if (EntityUtility.HasComponentData<ObjectPropertiesCD>(e, World))
                        {
                            try
                            {
                                ready = EntityUtility.GetComponentData<GrowingCD>(e, World)
                                    .HasFinishedGrowing(EntityUtility.GetComponentData<ObjectPropertiesCD>(e, World));
                            }
                            catch { ready = false; }
                        }
                        plant = ready ? PlantState.Ready : PlantState.Growing;
                    }

                    // Marqueur d'automation : PugAutomationCD est present sur TOUTE machine
                    // (convoyeur, bras, foreuse, gisement minable, stockage) et ABSENT des
                    // cables (ElectricalWire) -> c'est le discriminant machine vs cable.
                    bool hasAuto = EntityUtility.HasComponentData<PugAutomationCD>(e, World);
                    // Gisement a foreuse : AutomationType.Mineable ne suffit PAS comme
                    // discriminant (retour testeur 21 juillet 2026 : "l'ancien relais est
                    // considere comme un gisement" - l'infrastructure des ruines du Core porte
                    // le meme flag). RequiresDrillCD (tag ECS) est la vraie signature du
                    // gisement qu'on ne peut miner qu'a la foreuse, ce que la prospection
                    // Triangle+gauche cherche a annoncer.
                    bool isMineable = hasAuto
                        && (EntityUtility.GetComponentData<PugAutomationCD>(e, World).type
                            & AutomationType.Mineable) != 0
                        && EntityUtility.HasComponentData<RequiresDrillCD>(e, World);

                    // Orientation VISIBLE de l'objet, lue sur la donnee REELLE (jamais devinee
                    // depuis la variation : un gisement var=0 n'a PAS d'orientation, et le
                    // mapping variation->sens differe selon l'objet). DirectionCD (machines a
                    // direction libre, ex. fonderie auto) puis le CHAMP direction de
                    // DirectionBasedOnVariationCD (convoyeurs, bras, foreuses). Le sens REEL de
                    // transport (MoverCD.stop-start) vit sur des entites-movers separees
                    // inatteignables d'ici -> on lit l'orientation de pose, ce que voit un voyant.
                    int2 dir = int2.zero; bool hasDir = false;
                    if (EntityUtility.HasComponentData<DirectionCD>(e, World))
                    {
                        float3 dv = EntityUtility.GetComponentData<DirectionCD>(e, World).direction;
                        dir = new int2(
                            (int)math.round(math.clamp(dv.x, -1f, 1f)),
                            (int)math.round(math.clamp(dv.z, -1f, 1f)));
                        hasDir = true;
                    }
                    else if (EntityUtility.HasComponentData<DirectionBasedOnVariationCD>(e, World))
                    {
                        dir = EntityUtility.GetComponentData<DirectionBasedOnVariationCD>(e, World).direction;
                        hasDir = true;
                    }
                    // "vers X" pour les machines orientees ; le gisement (dir nulle) n'en a pas.
                    bool conveyor = hasAuto && hasDir && !math.all(dir == int2.zero);
                    int2 convDir = conveyor ? dir : default;

                    // Electricite : logique d'AFFICHAGE du jeu + tension du cable pour l'a11y.
                    // SOURCE (sourceEnergy>0, generateur) -> PRODUIT, jamais "hors tension".
                    // Consommateur affichant l'icone manque-de-courant (ShouldDisplayed) ->
                    // sous/hors tension. Cable/conducteur pur (electrique sans PugAutomationCD)
                    // -> on EXPOSE quand meme sa tension : le jeu n'affiche pas d'icone dessus,
                    // mais pour nous "courant present ici" signale le cable (et sa presence sous
                    // une structure non electrique, via WireMap).
                    PowerState power = PowerState.None; int sourceEnergy = 0; PowerState wirePower = PowerState.None;
                    bool hasElec = EntityUtility.HasComponentData<ElectricityCD>(e, World);
                    if (hasElec)
                    {
                        var el = EntityUtility.GetComponentData<ElectricityCD>(e, World);
                        sourceEnergy = el.sourceEnergy;
                        if (el.sourceEnergy > 0) power = PowerState.Source;
                        else if (el.ShouldDisplayedRequireElectricity())
                            power = el.hasEnoughElectricityToPowerStuff ? PowerState.On : PowerState.Off;
                        else if (!hasAuto) // cable / conducteur pur
                        {
                            wirePower = el.hasEnoughElectricityToPowerStuff ? PowerState.On : PowerState.Off;
                            power = wirePower; // l'entree cable elle-meme annonce sa tension
                        }
                    }
                    bool hasConn = EntityUtility.HasComponentData<ElectricityConnectionCD>(e, World);
                    int conns = hasConn
                        ? (int)EntityUtility.GetComponentData<ElectricityConnectionCD>(e, World).direction
                        : 0;

                    // Cable / conducteur pur : electrique, SANS fonction propre ni interaction,
                    // non source. Il doit CEDER la case a toute machine posee dessus (sinon il
                    // masquait foreuses/convoyeurs/bras dans l'index - "le cable annonce tout").
                    // Cable / conducteur pur : electrique SANS PugAutomationCD (les cables
                    // ElectricalWire n'en portent pas, toutes les machines si) -> il CEDE la
                    // case a toute machine posee dessus (sinon il masquait le bras/convoyeur/
                    // foreuse cable par-dessous).
                    bool interactable0 = EntityUtility.HasComponentData<InteractableObjectReferenceCD>(e, World);
                    bool infra = (hasElec || hasConn) && !hasAuto && !interactable0 && sourceEnergy == 0;

                    // Stockage d'automation : remplissage. L'inventaire vit sur une entite
                    // separee (StorageCD.inventoryEntity) -> on compte ses slots occupes.
                    bool hasStorage = false; int storageCount = 0;
                    if (EntityUtility.HasComponentData<StorageCD>(e, World))
                    {
                        hasStorage = true;
                        Entity inv = EntityUtility.GetComponentData<StorageCD>(e, World).inventoryEntity;
                        if (inv != Entity.Null && EntityManager.HasBuffer<ContainedObjectsBuffer>(inv))
                        {
                            var buf = EntityManager.GetBuffer<ContainedObjectsBuffer>(inv, true);
                            for (int i = 0; i < buf.Length; i++)
                                if (buf[i].objectID != ObjectID.None) storageCount++;
                        }
                    }

                    // Etat ouvert/ferme (porte, portail) ou active/desactive (levier) : deux
                    // familles distinctes cote jeu. Electrique (porte/portail elec) -> lu via
                    // SwapColliderCD.swap (bool replique reseau, source fiable - c'est le champ
                    // que le jeu lui-meme utilise pour animer l'ouverture). Bois (porte/portail
                    // classique) + levier -> pas de composant d'etat dedie, encode dans la
                    // VARIATION de l'ObjectDataCD ; parite confirmee par decompil (Gate : case
                    // impaire = "Open" explicite dans le code jeu, Lever : 1 = allume) -> IMPAIR
                    // = ouvert/actif, PAIR = ferme/inactif. Mapping pas teste en jeu, a valider
                    // a l'oreille (poser une porte/un portail, l'ouvrir/fermer, ecouter).
                    ToggleState toggle = ToggleLogic.Compute(e, World, od.objectID, od.variation);
                    // Le levier porte un ElectricityCD avec sourceEnergy fixe (capacite nominale,
                    // constante en base) : sans ce garde-fou, "genere du courant" s'annoncait
                    // MEME desactive (le blocage reel passe par blocksElectricityWhenVariationIsZero,
                    // pas par une remise a zero de sourceEnergy) -> contradiction avec le Toggle
                    // qui, lui, reflete l'etat REEL. Objet a bascule connu -> le Toggle prime,
                    // on tait toute annonce electrique concurrente (sous tension / genere...).
                    if (toggle != ToggleState.None)
                    {
                        power = PowerState.None;
                        wirePower = PowerState.None;
                    }

                    var entry = new ObjectIndex.Entry
                    {
                        Id = od.objectID,
                        Interactable = interactable0,
                        Plant = plant,
                        Conveyor = conveyor,
                        ConveyorDir = convDir,
                        Power = power,
                        Connections = conns,
                        HasStorage = hasStorage,
                        StorageCount = storageCount,
                        Infra = infra,
                        Resource = isMineable,
                        Toggle = toggle,
                        Ent = e,
                    };
                    // Priorite de la case : interactible (3) > gisement/ressource (2) >
                    // machine (1) > cable (0). Le gisement minable prime pour rester reperable
                    // meme entoure de foreuses. A priorite egale, le dernier balaye gagne.
                    int newPrio = Prio(in entry);
                    // Emprise : on ne lit pas la rotation de l'objet -> pour un prefab
                    // RECTANGULAIRE on marque l'UNION des deux orientations (xy et yx,
                    // la regle du jeu echange les axes selon la direction). Sur-couvrir
                    // d'une case adjacente est sans gravite pour une annonce ; rater la
                    // moitie d'une machine pivotee ne l'etait pas (vecu : scie/etabli
                    // en fer muets une case sur deux).
                    int2 a = new int2((int)math.round(pos.x), (int)math.round(pos.z)) + corner;
                    // Tension + identite du cable deposees sur sa case, hors logique de
                    // priorite : meme si une machine prend la case dans Map, on garde
                    // "courant ici" ET "c'est un cable" dans WireMap.
                    if (wirePower != PowerState.None)
                        ObjectIndex.WireMap[ObjectIndex.Key(a)] =
                            new ObjectIndex.WireEntry { Power = wirePower, Id = od.objectID };
                    // Emprise EXACTE : maintenant qu'on lit l'orientation, on couvre la taille
                    // tournee reelle (axes echanges si l'objet regarde est/ouest, regle du jeu)
                    // au lieu de l'union des deux orientations - cette union faisait DEBORDER
                    // les foreuses sur les cases du gisement voisin. Sans direction connue et
                    // emprise non carree -> repli sur l'union (securite, meubles non orientes).
                    if (hasDir)
                    {
                        int2 tsize = (dir.x != 0) ? new int2(size.y, size.x) : size;
                        for (int dx = 0; dx < tsize.x; dx++)
                            for (int dy = 0; dy < tsize.y; dy++)
                                Place(new int2(a.x + dx, a.y + dy), in entry, newPrio);
                    }
                    else
                    {
                        int2 span = math.max(size, size.yx);
                        for (int dx = 0; dx < span.x; dx++)
                            for (int dy = 0; dy < span.y; dy++)
                            {
                                if (!((dx < size.x && dy < size.y) || (dx < size.y && dy < size.x))) continue;
                                Place(new int2(a.x + dx, a.y + dy), in entry, newPrio);
                            }
                    }
                }
                ents.Dispose();

                // Fallback : la SummonAreaCD n'est pas replicuee au ClientWorld ->
                // en solo, les deux mondes tournent dans le meme process ; on lit le
                // ServerWorld directement pour trouver la rune de la Hive Mother.
                if (!caFound)
                {
                    try
                    {
                        var sw = Manager.ecs.ServerWorld;
                        if (sw != null)
                        {
                            var sem = sw.EntityManager;
                            var sq = sem.CreateEntityQuery(
                                ComponentType.ReadOnly<SummonAreaCD>(),
                                ComponentType.ReadOnly<LocalTransform>());
                            var se = sq.ToEntityArray(Allocator.Temp);
                            foreach (var e in se)
                            {
                                var sa = sem.GetComponentData<SummonAreaCD>(e);
                                if (sa.bossToSummon == ObjectID.None) continue;
                                var p3 = sem.GetComponentData<LocalTransform>(e).Position;
                                float2 sp = new float2(p3.x, p3.z);
                                float sd2 = math.lengthsq(sp - center);
                                if (sd2 < caBest) { caBest = sd2; caPos = sp; caFound = true; }
                            }
                            se.Dispose();
                            sq.Dispose();
                        }
                    }
                    catch (System.Exception ex) { Diag.Error("A11yCenterSWDiag", ex); }
                }

                SummonScan.Found = caFound;
                SummonScan.Pos = caPos;

                // Injecte la rune dans ObjectIndex : le curseur de tuile peut alors
                // l'annoncer comme n'importe quel interactible (prio max = 3).
                if (caFound)
                {
                    int2 rc = new int2((int)math.round(caPos.x), (int)math.round(caPos.y));
                    Place(rc, new ObjectIndex.Entry { Id = ObjectID.SummonArea, Interactable = true }, 3);
                }
            }
            catch (System.Exception ex) { Diag.Error("A11yIndexDiag", ex); }
        }

        // Priorite d'occupation d'une case : interactible > gisement/ressource > machine > cable.
        private static int Prio(in ObjectIndex.Entry e)
            => e.Interactable ? 3 : e.Resource ? 2 : e.Infra ? 0 : 1;

        // Pose une entree sur une case en respectant la priorite (ne se fait pas ecraser par
        // moins prioritaire). A priorite egale, le dernier balaye gagne (comportement historique).
        private static void Place(int2 cell, in ObjectIndex.Entry entry, int newPrio)
        {
            long k = ObjectIndex.Key(cell);
            if (ObjectIndex.Map.TryGetValue(k, out var old) && Prio(in old) > newPrio) return;
            ObjectIndex.Map[k] = entry;
        }

        // Balaye le disque (rayon en cases) autour du centre et retient la tuile de
        // minerai la plus proche. Couche ore/ancientCrystal lue par TileAccessor.HasType,
        // independante des murs (le filon enfoui est detecte, comme ses paillettes) ET
        // de l'eclairage (pas de check TileQuery.Lit ici, comportement voulu).
        private static void ScanOre(ref TileAccessor ta)
        {
            int r = OreScan.Radius;
            int2 c = OreScan.Center;
            int r2 = r * r;
            bool found = false;
            int best = int.MaxValue;
            int2 bestTile = default;

            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    int d2 = dx * dx + dy * dy;
                    if (d2 > r2 || d2 >= best) continue;
                    int2 t = new int2(c.x + dx, c.y + dy);
                    if ((ta.HasType(t, TileType.ore) || ta.HasType(t, TileType.ancientCrystal))
                        && !ta.HasType(t, TileType.immune)) // filon SCELLE (Grande Muraille) : iminable, ne pas y guider
                    {
                        found = true;
                        best = d2;
                        bestTile = t;
                    }
                }
            }

            // Gisements a foreuse (IronOreBoulder etc.) : objets poses, pas une couche de
            // tuile -> lus depuis ObjectIndex (flag Resource = PugAutomationCD.type Mineable),
            // deja rempli par TileReaderSystem. Resultat SEPARE de la veine ci-dessus (best/
            // bestTile) : les deux annonces doivent pouvoir coexister, l'une n'ecrase pas
            // l'autre. Meme regle que les veines : ni mur ni eclairage ne filtrent (demande
            // explicite utilisateur, "je m'en fous qu'il soit eclaire ou non"). Borne reelle
            // = IndexRadius (24) de l'index, pas le rayon de prospection si celui-ci le depasse.
            // Liste triee des N plus proches, une entree par ENTITE (un gisement occupe
            // 2x2 cases dans l'index : sans dedoublonnage il sortirait quatre fois, et
            // saturerait a lui seul la liste). Insertion directe, N vaut 4 -> pas de tri.
            int depositCount = 0;
            var depTiles = new int2[OreScan.MaxDeposits];
            var depDist = new int[OreScan.MaxDeposits];
            var depEnt = new Entity[OreScan.MaxDeposits];
            foreach (var kv in ObjectIndex.Map)
            {
                if (!kv.Value.Resource) continue;
                int2 t = new int2((int)(kv.Key >> 32), (int)(uint)kv.Key);
                int2 dd = t - c;
                int d2 = dd.x * dd.x + dd.y * dd.y;
                if (d2 > r2) continue;

                // Meme gisement deja retenu : on ne garde que sa case la plus proche.
                Entity ent = kv.Value.Ent;
                int existing = -1;
                for (int i = 0; i < depositCount; i++)
                    if (depEnt[i] == ent) { existing = i; break; }
                if (existing >= 0)
                {
                    if (d2 >= depDist[existing]) continue;
                    // Retire l'ancienne position, la reinsertion ci-dessous la reclasse.
                    for (int i = existing; i < depositCount - 1; i++)
                    {
                        depTiles[i] = depTiles[i + 1];
                        depDist[i] = depDist[i + 1];
                        depEnt[i] = depEnt[i + 1];
                    }
                    depositCount--;
                }
                else if (depositCount == OreScan.MaxDeposits && d2 >= depDist[depositCount - 1])
                    continue; // liste pleine et candidat plus loin que le dernier retenu

                int pos = depositCount;
                while (pos > 0 && depDist[pos - 1] > d2) pos--;
                if (pos >= OreScan.MaxDeposits) continue;
                for (int i = math.min(depositCount, OreScan.MaxDeposits - 1); i > pos; i--)
                {
                    depTiles[i] = depTiles[i - 1];
                    depDist[i] = depDist[i - 1];
                    depEnt[i] = depEnt[i - 1];
                }
                depTiles[pos] = t;
                depDist[pos] = d2;
                depEnt[pos] = ent;
                if (depositCount < OreScan.MaxDeposits) depositCount++;
            }

            OreScan.Found = found;
            OreScan.Tile = bestTile;
            OreScan.DepositCount = depositCount;
            for (int i = 0; i < depositCount; i++) OreScan.DepositTiles[i] = depTiles[i];
            OreScan.ResultValid = true;
        }

        // 4 directions cardinales (x=est, y=nord), identiques a ProximitySonar.
        private static readonly int[] SonarDx = { 0, 1, 0, -1 };
        private static readonly int[] SonarDy = { 1, 0, -1, 0 };

        // Scan du sonar de proximite (v1, couche TUILE) : pour chaque direction, retient le
        // PREMIER obstacle a <= 2 cases. Tuile bloquante pit/eau -> type 2 (clapotis) ; tout
        // autre mur bloquant -> type 1 (mat) ; rien -> libre (silence). Les objets poses et
        // les portes fermees (sonde de collision physique) viendront en v2.
        private static void ScanSonar(ref TileAccessor ta)
        {
            int2 c = SonarScan.Center;
            for (int d = 0; d < 4; d++)
            {
                int tex = 0, dist = 0;              // mur (couche tuile)
                bool obj = false; int objDist = 0;  // objet pose (index d'objets)
                for (int step = 1; step <= 2; step++)
                {
                    int2 t = new int2(c.x + SonarDx[d] * step, c.y + SonarDy[d] * step);
                    // Detecteur d'obscurite : case sombre -> on ne sait pas ce qu'il y a LA,
                    // on ne dit rien pour ELLE, mais on continue de sonder plus loin dans la
                    // meme direction (par-case, pas un arret complet comme la canne).
                    if (!LightIndex.IsLit(ref ta, t)) continue;
                    if (ta.TryGetBlockingTile(t, out TileCD wall, true))
                    {
                        // En bateau, l'eau est la route : ni clapotis ni arret de la sonde
                        // (on continue de chercher un vrai obstacle plus loin).
                        if (SonarScan.OnWater && wall.tileType == TileType.water) continue;
                        tex = (wall.tileType == TileType.pit || wall.tileType == TileType.water) ? 2 : 1;
                        dist = step;
                        break;   // un mur stoppe la perception au-dela
                    }
                    // Objet pose (torche, champignon, etabli...) capte par l'index d'objets,
                    // tant qu'aucun mur ne le precede. On retient le plus proche.
                    if (!obj && ObjectIndex.TryGet(t, out _)) { obj = true; objDist = step; }
                }
                SonarScan.Tex[d] = tex;
                SonarScan.Dist[d] = dist;
                SonarScan.Obj[d] = obj;
                SonarScan.ObjDist[d] = objDist;
            }
            SonarScan.ResultValid = true;
        }

        // Detecteur de collision directionnel (stick gauche) : avance case par case (DDA, meme
        // pas d'echantillonnage 0.34 que la canne laser) dans Direction, jusqu'a MaxRange.
        // S'arrete au premier INFRANCHISSABLE (mur ou pit/eau - TryGetBlockingTile(...,true)
        // couvre les deux, comme le sonar de proximite) : distance CONTINUE (pas arrondie a la
        // case) pour un calcul de volume fin cote mod.
        private const float CollisionStep = 0.34f;

        private static void ScanCollision(ref TileAccessor ta)
        {
            int2 c = CollisionScan.Center;
            float2 dir = CollisionScan.Direction;
            float maxRange = CollisionScan.MaxRange;
            float2 origin = new float2(c.x, c.y);

            int2 last = c;
            bool found = false;
            float dist = 0f;

            for (float dd = CollisionStep; dd <= maxRange + 0.001f; dd += CollisionStep)
            {
                int2 t = new int2(
                    (int)math.round(origin.x + dir.x * dd),
                    (int)math.round(origin.y + dir.y * dd));
                if (t.Equals(last)) continue;
                last = t;

                // Detecteur d'obscurite : par-case, pas un arret comme la canne (portee trop
                // courte pour le probleme du "voir a travers l'ombre" - la case suivante peut
                // rester sondee normalement).
                if (!LightIndex.IsLit(ref ta, t)) continue;

                if (ta.TryGetBlockingTile(t, out TileCD block, true))
                {
                    // En bateau, l'eau n'est plus un infranchissable : sans ca la nappe
                    // d'alerte hurlait en permanence en pleine mer (retour testeur).
                    if (CollisionScan.OnWater && block.tileType == TileType.water) continue;
                    found = true;
                    dist = dd;
                    break;
                }
            }

            CollisionScan.Found = found;
            CollisionScan.Dist = dist;
            CollisionScan.ResultValid = true;
        }

        // Scanne le carré 5×5 autour du joueur pour détecter les sols dangereux
        // (TileType.groundSlime à tileset acide). PugDatabase.TryGetTileItemInfo résout
        // le tileset sans en hardcoder la valeur numérique, même chemin que le curseur.
        private static void ScanHazardGround(ref TileAccessor ta)
        {
            int2 center = new int2(
                (int)math.round(ObjectIndex.Center.x),
                (int)math.round(ObjectIndex.Center.y));
            const int R = 2;
            bool found = false;
            float best = float.MaxValue;
            int2 bestTile = default;

            for (int dy = -R; dy <= R; dy++)
            {
                for (int dx = -R; dx <= R; dx++)
                {
                    int2 t = new int2(center.x + dx, center.y + dy);
                    var top = ta.GetTop(t);
                    if (top.tileType != TileType.groundSlime) continue;
                    ObjectInfo info;
                    try { info = PugDatabase.TryGetTileItemInfo(top.tileType, top.tileset); }
                    catch { continue; }
                    if (info == null || info.objectID != ObjectID.GroundAcidSlime) continue;
                    float d2 = dx * dx + dy * dy;
                    if (d2 < best) { best = d2; bestTile = t; found = true; }
                }
            }

            HazardGroundScan.Found = found;
            HazardGroundScan.Tile = bestTile;
        }

        // Balaye les creatures dans la fenetre camera et publie position + categorie, pour le
        // scanner de proximite. Memes regles de classification que l'ancien ping sonar (retire) :
        // CritterCD = paisible ; EnemyCD a faction hostile = ennemi, sauf slime dormant (paisible) ;
        // EnemyCD a faction neutre (chevres, betail) = paisible. Faction Merchand (PNJ, ni EnemyCD
        // ni CritterCD) = categorie PNJ marchands, testee AVANT le reste. Cadavres ecartes
        // (HealthCD a 0 : l'entite persiste quelques secondes apres la mort).
        private void ScanVisibility(ref TileAccessor ta)
        {
            int count = 0;
            float2 center = VisibilityScan.Center;
            float2 half = VisibilityScan.CamHalf;

            var ents = _visQuery.ToEntityArray(Allocator.Temp);
            foreach (var e in ents)
            {
                if (count >= VisibilityScan.MaxTargets) break;

                float3 pos;
                if (EntityManager.HasComponent<LocalToWorld>(e))
                    pos = EntityManager.GetComponentData<LocalToWorld>(e).Position;
                else if (EntityManager.HasComponent<LocalTransform>(e))
                    pos = EntityManager.GetComponentData<LocalTransform>(e).Position;
                else continue;
                float2 p = new float2(pos.x, pos.z);
                float2 d = p - center;
                if (math.abs(d.x) > half.x || math.abs(d.y) > half.y) continue;

                // Detecteur d'obscurite : exclut SEULEMENT la cible dont la case a elle est
                // sombre (pas tout le scan) - une autre cible visible plus loin, dans une
                // flaque de lumiere, reste captee normalement. Cf. core-keeper-darkness-gate.md.
                int2 pTile = new int2((int)math.round(p.x), (int)math.round(p.y));
                if (!LightIndex.IsLit(ref ta, pTile)) continue;

                if (EntityManager.HasComponent<HealthCD>(e)
                    && EntityManager.GetComponentData<HealthCD>(e).health <= 0) continue;

                // Le joueur LOCAL lui-meme (GhostOwnerIsLocal = ce client possede cette entite -
                // ne matche PAS un autre joueur en multi, dont l'entite appartient a une autre
                // connexion) et son familier/serviteur (FactionID.PlayerMinion) n'ont rien a
                // faire dans "creatures" - mais un AUTRE joueur en multi doit rester visible.
                if (EntityManager.HasComponent<GhostOwnerIsLocal>(e)) continue;

                bool critter = EntityManager.HasComponent<CritterCD>(e);
                bool enemyCd = EntityManager.HasComponent<EnemyCD>(e);
                bool hasFaction = EntityManager.HasComponent<FactionCD>(e);
                FactionID faction = hasFaction ? EntityManager.GetComponentData<FactionCD>(e).faction : FactionID.None;
                if (faction == FactionID.PlayerMinion) continue;
                ObjectID oid = EntityManager.HasComponent<ObjectDataCD>(e)
                    ? EntityManager.GetComponentData<ObjectDataCD>(e).objectID
                    : ObjectID.None;

                // Decor destructible (tables...) peut porter un FactionCD herite (ex. Caveling)
                // sans etre une creature : exiger EnemyCD/CritterCD, sauf PNJ marchand qui est
                // volontairement FactionCD seul (ni EnemyCD ni CritterCD, cf. requete ci-dessus).
                ProximityScanner.Category cat;
                if (ProximityScanner.CattleIds.Contains(oid))
                    cat = ProximityScanner.Category.Cattle;
                else if (!critter && !enemyCd && hasFaction && faction == FactionID.Merchant)
                    cat = ProximityScanner.Category.Merchant;
                else if (!critter && enemyCd && HostileFilter.IsHostile(faction) && !HostileFilter.IsDormantSlime(oid))
                    cat = ProximityScanner.Category.Enemy;
                else if (critter || enemyCd)
                    cat = ProximityScanner.Category.Passive;
                else
                    continue; // ni critter ni EnemyCD ni PNJ marchand : pas une creature

                VisibilityScan.Targets[count++] = new VisibilityScan.Creature
                {
                    Key = EntityKey.Of(e),
                    Pos = p,
                    Obj = oid,
                    Cat = cat,
                };
            }
            ents.Dispose();

            VisibilityScan.Count = count;
        }

        // Dump ASCII du reseau local (dev) : grille "vue par le mod" dans Player.log, Nord en
        // haut. # = mur LU (TryGetBlockingTile), . = sol, = = passage (pont/porte sur tuile
        // bloquante), lettre = noeud du reseau, @ = joueur. Puis la liste des aretes dont les
        // DEUX extremites sont dans la fenetre (par lettres). A croiser avec une capture carte.
        private const string DumpAlphabet =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

        private void DumpNetwork()
        {
            int r = (int)NetworkDump.Radius;
            int2 c = NetworkDump.Center;
            var ta = new TileAccessor(ref CheckedStateRef, true);

            // Noeuds de la fenetre -> une lettre chacun (au-dela de 52 : '*').
            var nodes = new List<int2>();
            BeaconGraph.NodesInRadius(new float2(c.x, c.y), r, nodes);
            var label = new Dictionary<long, char>();
            for (int i = 0; i < nodes.Count; i++)
                label[ObjectIndex.Key(nodes[i])] = i < DumpAlphabet.Length ? DumpAlphabet[i] : '*';

            Diag.Log("A11yNetDump", "=== zone " + c.x + "," + c.y + " rayon " + r
                + " : " + nodes.Count + " noeuds (Nord en haut) ===");

            var sb = new System.Text.StringBuilder();
            for (int y = c.y + r; y >= c.y - r; y--)
            {
                sb.Clear();
                for (int x = c.x - r; x <= c.x + r; x++)
                {
                    int2 t = new int2(x, y);
                    char ch;
                    if (x == c.x && y == c.y) ch = '@';
                    else if (label.TryGetValue(ObjectIndex.Key(t), out char lc)) ch = lc;
                    else if (!ta.TryGetBlockingTile(t, out _, true)) ch = '.';
                    else if (ObjectIndex.TryGet(t, out var e) && BeaconObjects.IsPassable(e.Id)) ch = '=';
                    else ch = '#';
                    sb.Append(ch);
                }
                Diag.Log("A11yNetDump", sb.ToString());
            }

            // Aretes intra-fenetre (les deux extremites dans le rayon) par lettres.
            var edges = new List<BeaconGraph.Edge>();
            BeaconGraph.EdgesInRadius(new float2(c.x, c.y), r, edges);
            var es = new System.Text.StringBuilder();
            foreach (var e in edges)
            {
                char la = label.TryGetValue(ObjectIndex.Key(new int2(e.ax, e.ay)), out char a) ? a : '?';
                char lb = label.TryGetValue(ObjectIndex.Key(new int2(e.bx, e.by)), out char b) ? b : '?';
                if (es.Length > 0) es.Append(' ');
                es.Append(la).Append('-').Append(lb);
            }
            Diag.Log("A11yNetDump", "aretes (" + edges.Count + ") : " + es);
        }

        // Dump ASCII plafond/roofHole (dev) : meme principe que DumpNetwork mais pour la
        // mecanique eclairage. # = mur LU, R = roofHole (trou perce), . = sol ferme, @ = joueur.
        private void DumpLight()
        {
            int r = LightDump.Radius;
            int2 c = LightDump.Center;
            var ta = new TileAccessor(ref CheckedStateRef, true);

            Diag.Log("A11yLightDiag", "=== plafond zone " + c.x + "," + c.y + " rayon " + r
                + " (Nord en haut, @ joueur, # mur, R roofHole perce, . sol ferme) ===");

            int roofHoleCount = 0, floorCount = 0;
            var sb = new System.Text.StringBuilder();
            for (int y = c.y + r; y >= c.y - r; y--)
            {
                sb.Clear();
                for (int x = c.x - r; x <= c.x + r; x++)
                {
                    int2 t = new int2(x, y);
                    char ch;
                    if (x == c.x && y == c.y) ch = '@';
                    else if (ta.TryGetBlockingTile(t, out _, true)) ch = '#';
                    else if (ta.HasType(t, TileType.roofHole)) { ch = 'R'; roofHoleCount++; floorCount++; }
                    else { ch = '.'; floorCount++; }
                    sb.Append(ch);
                }
                Diag.Log("A11yLightDiag", sb.ToString());
            }
            Diag.Log("A11yLightDiag", "roofHole=" + roofHoleCount + " / sol=" + floorCount);

            // Grille "Lit" CALCULEE par le detecteur d'obscurite (LightIndex.IsLit) - a
            // comparer DIRECTEMENT a un screenshot de la meme zone, sans repasser par les
            // dx/dz bruts des sources. L = notre algo juge la case eclairee, . = sombre.
            Diag.Log("A11yLightDiag", "=== Lit calcule (L=eclaire, .=sombre, @ joueur) ===");
            int litCount = 0, darkCount = 0;
            for (int y = c.y + r; y >= c.y - r; y--)
            {
                sb.Clear();
                for (int x = c.x - r; x <= c.x + r; x++)
                {
                    int2 t = new int2(x, y);
                    char ch;
                    if (x == c.x && y == c.y) ch = '@';
                    else if (LightIndex.IsLit(ref ta, t)) { ch = 'L'; litCount++; }
                    else { ch = '.'; darkCount++; }
                    sb.Append(ch);
                }
                Diag.Log("A11yLightDiag", sb.ToString());
            }
            Diag.Log("A11yLightDiag", "lit=" + litCount + " / sombre=" + darkCount);
        }

        // Dump dev : liste TOUS les composants de l'entite sous le curseur + les valeurs
        // des composants automation connus. But : capturer la verite terrain d'une machine
        // (atteignable seulement avec l'ecarlate) pour finaliser l'a11y industrie sans
        // deviner. Sortie dans Player.log, prefixe [A11yAutoDiag].
        private void DumpAutomation()
        {
            int2 tile = AutomationDiag.Tile;
            // Etat RESOLU de la case (ce que le joueur entend) : HasWall/WallType prime sur
            // ObjectId dans SonifyTile/AnnounceCursorDetails - le voir ici permet de savoir SI
            // la case est encore classee bloquante (pit/eau) au moment du dump, avant meme de
            // regarder les entites brutes ci-dessous.
            Diag.Log("A11yAutoDiag", "case " + tile.x + "," + tile.y
                + " HasWall=" + TileQuery.HasWall + " WallType=" + TileQuery.WallType
                + " ObjectId=" + TileQuery.ObjectId + " Interactable=" + TileQuery.ObjectInteractable
                + " IsImmune=" + TileQuery.IsImmune);
            // On dumpe TOUTES les entites-objets dont la case-centre tombe sur la case visee
            // (pas seulement la gagnante de l'index) : c'est la seule facon de voir le cable
            // ET la machine posee dessus, le sens reel, l'orientation, le type d'automation.
            var ents = _objQuery.ToEntityArray(Allocator.Temp);
            int found = 0;
            foreach (var e in ents)
            {
                float3 pos;
                if (EntityManager.HasComponent<LocalToWorld>(e))
                    pos = EntityManager.GetComponentData<LocalToWorld>(e).Position;
                else if (EntityManager.HasComponent<LocalTransform>(e))
                    pos = EntityManager.GetComponentData<LocalTransform>(e).Position;
                else continue;
                int2 cell = new int2((int)math.round(pos.x), (int)math.round(pos.z));
                if (math.abs(cell.x - tile.x) > 1 || math.abs(cell.y - tile.y) > 1) continue;
                if (EntityUtility.HasComponentData<EnemyCD>(e, World)
                    || EntityUtility.HasComponentData<CritterCD>(e, World)
                    || EntityUtility.HasComponentData<PlayerGhost>(e, World)) continue;
                found++;
                DumpEntity(e, cell);
            }
            ents.Dispose();
            if (found == 0)
                Diag.Log("A11yAutoDiag", "case " + tile.x + "," + tile.y + " : aucune entite-objet a portee");
        }

        // Dump complet d'une entite-objet : composants + valeurs automation connues.
        private void DumpEntity(Entity ent, int2 cell)
        {
            ObjectID oid = EntityManager.HasComponent<ObjectDataCD>(ent)
                ? EntityManager.GetComponentData<ObjectDataCD>(ent).objectID : ObjectID.None;
            int variation = EntityManager.HasComponent<ObjectDataCD>(ent)
                ? EntityManager.GetComponentData<ObjectDataCD>(ent).variation : 0;

            var types = EntityManager.GetComponentTypes(ent, Allocator.Temp);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < types.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                var mt = types[i].GetManagedType();
                sb.Append(mt != null ? mt.Name : types[i].ToString());
            }
            types.Dispose();
            Diag.Log("A11yAutoDiag", oid + " @ " + cell.x + "," + cell.y + " var=" + variation
                + " smallEntities=" + EntityManager.HasBuffer<SmallEntityRefBuffer>(ent)
                + " : " + sb);

            if (EntityManager.HasComponent<PugAutomationCD>(ent))
            {
                var a = EntityManager.GetComponentData<PugAutomationCD>(ent);
                Diag.Log("A11yAutoDiag", "  PugAutomationCD type=" + a.type + " isActive=" + a.isActive);
            }
            if (EntityManager.HasComponent<DirectionCD>(ent))
                Diag.Log("A11yAutoDiag", "  DirectionCD dir="
                    + EntityManager.GetComponentData<DirectionCD>(ent).direction);
            if (EntityManager.HasComponent<DirectionBasedOnVariationCD>(ent))
                Diag.Log("A11yAutoDiag", "  DirectionBasedOnVariationCD dir="
                    + EntityManager.GetComponentData<DirectionBasedOnVariationCD>(ent).direction);
            if (EntityManager.HasComponent<MoverCD>(ent))
            {
                var m = EntityManager.GetComponentData<MoverCD>(ent);
                Diag.Log("A11yAutoDiag", "  MoverCD start=" + m.start + " stop=" + m.stop
                    + " moveTime=" + m.moveTime);
            }
            if (EntityManager.HasComponent<MinerCD>(ent))
            {
                var mi = EntityManager.GetComponentData<MinerCD>(ent);
                Diag.Log("A11yAutoDiag", "  MinerCD position=" + mi.position + " damage=" + mi.damage);
            }
            if (EntityManager.HasComponent<ElectricityCD>(ent))
            {
                var el = EntityManager.GetComponentData<ElectricityCD>(ent);
                Diag.Log("A11yAutoDiag", "  ElectricityCD amount=" + el.electricityAmount
                    + " src=" + el.sourceEnergy + " circuitType=" + el.circuitType
                    + " connMode=" + el.circuitConnectionMode
                    + " shouldDisplay=" + el.ShouldDisplayedRequireElectricity()
                    + " blocks=" + el.blocksElectricity);
            }
            if (EntityManager.HasComponent<ElectricityConnectionCD>(ent))
                Diag.Log("A11yAutoDiag", "  ElectricityConnectionCD dir="
                    + EntityManager.GetComponentData<ElectricityConnectionCD>(ent).direction);
            if (EntityManager.HasComponent<StorageCD>(ent))
                Diag.Log("A11yAutoDiag", "  StorageCD inv="
                    + EntityManager.GetComponentData<StorageCD>(ent).inventoryEntity.Index);
        }
    }
}
