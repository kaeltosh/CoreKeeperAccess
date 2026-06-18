using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using PugTilemap;
using Rewired;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Curseur d'inspection de tuile (etape A). Actif en jeu (inventaire ferme) :
    //  - colle au joueur par defaut ;
    //  - le D-pad le detache et le deplace case par case, borne a la zone visible ;
    //  - bouger au stick gauche le recolle au joueur (marcher = recentrer).
    // A chaque deplacement, on demande la lecture de la case au TileReaderSystem (ECS)
    // et on annonce le remarquable : mur, ou sol notable (tout sauf le sol de base).
    internal static class BuildModeNavigator
    {
        private const int DpadUp = 16, DpadRight = 17, DpadDown = 18, DpadLeft = 19;
        private const int LeftStickX = 0, LeftStickY = 1;
        private const int CrossButton = 6; // A / Croix (bouton sud)
        private const float StickMove = 0.25f; // deadzone stick gauche du jeu

        private static int2 _cursor;
        private static bool _detached;
        private static bool _pending;

        // Vrai quand on est en jeu et qu'on prend le D-pad au jeu (lu par le patch de
        // suppression d'input natif pour neutraliser tri/empiler/swap-hotbar du D-pad).
        internal static bool StealsDpad;

        // Vrai quand le curseur est DETACHE : on vole alors Croix (INTERACT_WITH_OBJECT)
        // pour que l'action passe par la case visee, pas par l'objet adjacent natif.
        internal static bool StealsCross;

        // Curseur detache, pour la garde du combo "details de la case" (ComboBindings).
        // La case sous le perso n'a pas de lecture fraiche -> detache seulement.
        internal static bool CursorDetached => _detached;

        public static void Tick()
        {
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null || Manager.ui == null) { Reset(); return; }

            // Jeu normal seulement : si une fenetre (inventaire, fiche perso) OU la carte
            // (mode voyage rapide, gere par TeleportNavigator) prend le D-pad, on se retire.
            if (!InputContext.InGameFree) { Reset(); return; }

            StealsDpad = true; // en jeu : on vole le D-pad au jeu pour le curseur

            var joy = ReInput.isReady ? ReInput.controllers.GetLastActiveController<Joystick>() : null;
            int2 playerTile = ToTile(player.WorldPosition);

            // Bouger au stick gauche -> recoller au joueur.
            if (joy != null)
            {
                float ax = AxisById(joy, LeftStickX), ay = AxisById(joy, LeftStickY);
                if (ax * ax + ay * ay > StickMove * StickMove) _detached = false;
            }

            // Filet : si le curseur detache n'est plus dans le champ (le perso s'est
            // eloigne sous lui, la camera l'a suivi), on le recolle. Sinon toute cible
            // D-pad tomberait hors viewport et le D-pad semblerait gele.
            if (_detached && !InViewport(_cursor)) _detached = false;

            if (!_detached)
            {
                _cursor = playerTile;
                TileQuery.Active = false;
            }

            // D-pad -> deplacer le curseur d'une case (un cran par appui), borne a l'ecran.
            // Sauf si Triangle (touche access) est tenu : le D-pad est alors reserve aux
            // commandes info (InfoKey), il ne deplace plus le curseur. Sauf aussi si la canne
            // laser est active (stick droit pousse) : le laser a alors priorite.
            if (joy != null && !InfoKey.ModifierHeld && !LaserCane.Active && DpadDir(joy, out int2 dir))
            {
                int2 target = _cursor + dir;
                if (InViewport(target))
                {
                    _detached = true;
                    _cursor = target;
                    TileQuery.Tile = _cursor;
                    TileQuery.Active = true;
                    TileQuery.ResultValid = false;
                    _pending = true;
                }
            }

            // Croix : action contextuelle sur la case du curseur (detache).
            bool croixDown = joy != null && ButtonDownById(joy, CrossButton);
            if (_detached)
            {
                int cheb = Mathf.Max(Mathf.Abs(_cursor.x - playerTile.x), Mathf.Abs(_cursor.y - playerTile.y));
                if (cheb <= 1)
                {
                    // Case adjacente. UN APPUI = UN COUP (pas de maintien : le minage
                    // intensif reste sur la gachette native ; Croix = action ponctuelle
                    // ciblee). On oriente le perso vers la case EN CONTINU (pre-orientation
                    // -> le coup part droit), et sur l'APPUI seulement on arme le bouton
                    // natif contextuel pour une passe (relache par PlayerMoveToSystem).
                    //  - mur/bloc bloquant -> miner      (INTERACT, prioritaire)
                    //  - objet sur le sol  -> interagir  (INTERACT_WITH_OBJECT)
                    //  - case vide         -> s'y deplacer (comme une case lointaine)
                    GameplayAction.AimActive = true;
                    GameplayAction.AimDir = AimToward(_cursor, playerTile);
                    // Garde de fraicheur (fix audit) : TOUT le routage exige une lecture de
                    // tuile republiee POUR LA CASE COURANTE. Avant, seule la branche
                    // "deplacer" l'exigeait : miner/interagir pouvaient router sur la
                    // lecture de la case PRECEDENTE (vieille d'une frame) -> mauvaise
                    // action sur appui rapide apres un mouvement du curseur. Lecture pas
                    // fraiche = l'appui ne fait rien, comme pour le deplacement.
                    if (croixDown && TileQuery.ResultValid && TileQuery.ResultTile.Equals(_cursor))
                    {
                        if (TileQuery.HasWall)
                        {
                            // Un mur/bloc bloquant prime -> miner, MEME si une entite "objet"
                            // est aussi detectee dessus (un bloc de terre est minable, pas
                            // interactif). Sinon le routage basculait de "miner" a "interagir"
                            // des que la case frappee se renommait "bloc de terre".
                            GameplayAction.Held = PlayerInput.InputType.INTERACT;
                            GameplayAction.Pressed = PlayerInput.InputType.INTERACT;
                        }
                        else if (TileQuery.ObjectId != ObjectID.None && TileQuery.ObjectInteractable)
                        {
                            // VRAI interactible sur case non bloquante (coffre, machine) ->
                            // interagir. Les objets PASSIFS (cable ancien, deco, statues...)
                            // ne comptent pas : depuis l'index d'objets ils occupent plein de
                            // cases marchables, et router "interagir" dessus interdisait de
                            // s'y deplacer au Croix.
                            GameplayAction.Held = PlayerInput.InputType.INTERACT_WITH_OBJECT;
                            GameplayAction.Pressed = PlayerInput.InputType.INTERACT_WITH_OBJECT;
                        }
                        else
                        {
                            // Case CONFIRMEE vide -> s'y deplacer.
                            MoveCommand.Target = new float2(_cursor.x, _cursor.y);
                            MoveCommand.Active = true;
                        }
                    }
                }
                else
                {
                    // Case lointaine : pas d'action ni d'orientation, Croix = s'y rendre.
                    GameplayAction.Disarm();
                    if (croixDown)
                    {
                        MoveCommand.Target = new float2(_cursor.x, _cursor.y);
                        MoveCommand.Active = true;
                    }
                }
            }
            else
            {
                GameplayAction.Disarm();
            }

            // Annonce quand le resultat publie correspond a la case du curseur.
            if (_pending && TileQuery.ResultValid && TileQuery.ResultTile.Equals(_cursor))
            {
                Announce();
                _pending = false;
            }

            // Triangle + haut (details de la case) est route par ComboDispatcher
            // (cf. ComboBindings), garde par CursorDetached.

            StealsCross = _detached; // vol de Croix actif uniquement curseur detache
        }

        // Case du curseur de tuile, exposee pour l'annonce d'emprise differee (PlacementReader
        // au poll, le temps que le ghost rattrape le curseur -> pas la latence du ghost).
        internal static int2 CursorTile => _cursor;
        internal static float FootprintDueAt = -1f; // echeance d'annonce d'emprise (apres deplacement curseur)

        private static void Announce()
        {
            var p = Manager.main != null ? Manager.main.player : null;
            int2 pt = p != null ? ToTile(p.WorldPosition) : _cursor;
            int dx = _cursor.x - pt.x, dy = _cursor.y - pt.y;
            // Curseur deplace deliberement (detache) -> programmer l'annonce d'emprise un
            // peu plus tard (le ghost a alors rattrape le curseur). Repousse a chaque
            // deplacement -> une seule annonce quand on s'arrete, pas pendant le balayage.
            if (CursorDetached) FootprintDueAt = UnityEngine.Time.unscaledTime + 0.12f;

            // Repere central : curseur sur la case du personnage. Sans coordonnees, c'est
            // le point d'ancrage pour se retrouver. On l'annonce et on s'arrete la (le sol
            // sous le perso n'est pas une info utile ici). Cas propre au curseur (la canne
            // laser ne pointe jamais le perso lui-meme).
            if (dx == 0 && dy == 0)
            {
                PlayMoveTick(dx, dy);
                TtsText.Say(Strings.L("cursor.player"), true);
                return;
            }

            var info = TileQuery.Snapshot();
            SonifyTile(_cursor, in info, dx, dy, true);
        }

        // Sonification PARTAGEE d'une case (curseur de tuile ET canne laser). Joue le son
        // qui porte le contenu de la case (mur=materiau, trou/eau=son dedie, objet, sol=tick),
        // tous spatialises (pan gauche-droite + pitch vertical) par rapport au joueur via
        // dx/dy. Le TTS du libelle n'est dit que si speak=true : le curseur (deplacement
        // delibere au D-pad) le veut ; la canne laser (balayage continu) ne veut QUE le son,
        // pas un TTS hache. La case (coords MONDE) sert a positionner les sons natifs.
        public static void SonifyTile(int2 tile, in TileInfo info, int dx, int dy, bool speak)
        {
            // Case infranchissable (mur/bloc) : on joue le SON DU MATERIAU que le jeu
            // attribue lui-meme a cette tuile (sfxTableDestroyId du TileEffectCD : c'est
            // le seul son vraiment decline par materiau). Spatialise sur la case (pan +
            // distance nativement), PLUS un pitch vertical maison (+1 demi-ton par ligne)
            // car la spatialisation native ne distingue pas haut/bas. Le son porte
            // "bloque" + materiau + axe vertical -> PAS de TTS "mur", ce serait redondant.
            // Il remplace le tick de deplacement : a l'oreille, bloque != libre.
            if (info.HasWall)
            {
                // Un trou (pit) creuse a la pelle, ou de l'eau, remonte comme tuile BLOQUANTE
                // mais n'est NI un mur minable NI une case franchissable. On les sort de la
                // categorie "mur" : sinon PlayWallMaterialSfx jouait le son de roche et on
                // croyait taper un mur impossible a miner/franchir (piege vecu en jeu). Son
                // DEDIE + TTS, spatialise comme le reste (pan + pitch vertical).
                if (info.WallType == TileType.pit)
                {
                    PlaySpecialSurface(dx, dy, SfxID.ui_plop_1_01);
                    if (speak) TtsText.Say(Strings.L("cursor.pit"), true);
                    return;
                }
                if (info.WallType == TileType.water)
                {
                    PlaySpecialSurface(dx, dy, SfxID.fish_splash_1_02);
                    if (speak) TtsText.Say(Strings.L("cursor.water"), true);
                    return;
                }
                // Mur SCELLE (couche immune : Grande Muraille...) : invulnerable, donc
                // ni minable ni prospectable. Son du materiau conserve (info vraie),
                // mais TTS explicite - l'equivalent du visuel distinctif qu'un voyant
                // remarque immediatement. Le ding minerai est coupe dans
                // PlayWallMaterialSfx (un filon scelle est un piege, pas une ressource).
                PlayWallMaterialSfx(tile, in info, dy);
                if (speak && info.IsImmune) TtsText.Say(Strings.L("cursor.immune"), true);
                return;
            }

            // Case franchissable. Priorite : objet/construction pose > sol notable.
            string text = null;
            if (info.ObjectId != ObjectID.None)
            {
                // Vrai interactible (coffre, machine...) -> son du materiau si present +
                // marqueur interactible, qui REMPLACENT le tick de deplacement (comme le
                // materiau pour un mur). A l'oreille : case vide = tick, mur = materiau,
                // interactible = (materiau +) marqueur. La deco passive (pas de
                // InteractableObjectReferenceCD) ne bipe pas : juste le tick, comme une
                // case nue. Le TTS du nom est conserve dans les deux cas.
                if (info.ObjectInteractable)
                    PlayObjectSfx(tile, in info, dx, dy);
                else
                    PlayMoveTick(dx, dy);
                if (speak) text = AppendIndustry(AppendPlant(InGameTtsCore.ResolveObjectName(info.ObjectId), in info), in info, false);
            }
            else
            {
                // Sol : tick de position. Sol notable annonce (le sol de base reste muet).
                PlayMoveTick(dx, dy);
                if (speak && info.Ground != TileType.ground)
                    text = GroundLabel(info.Ground);
            }

            if (speak && !string.IsNullOrEmpty(text)) TtsText.Say(text, true);
        }

        // Tick SPATIALISE (son maison pan/pitch) a chaque deplacement sur une case
        // franchissable : confirme la position du curseur par rapport au joueur. Pan
        // gauche-droite au bareme commun en cases (GameplayAudio.PanFromTiles) ;
        // pitch +1 demi-ton par ligne d'ecart vertical (au-dessus = plus aigu).
        // Volume du tick de deplacement (case vide / deco), a regler a l'oreille.
        private const float MoveTickVolume = 0.3f;

        private static void PlayMoveTick(int dx, int dy)
        {
            float pan = GameplayAudio.PanFromTiles(dx);
            float pitch = Mathf.Pow(2f, dy / 12f); // 1 demi-ton par ligne
            GameplayAudio.PlaySpatial(SfxID.inventory_select, pan, pitch,
                MoveTickVolume * A11ySettings.NavigationVolume * GameplayAudio.DistanceTrim(Dist(dx, dy)));
        }

        // Distance joueur->case en cases, pour le trim de volume commun.
        private static float Dist(int dx, int dy) => Mathf.Sqrt(dx * dx + dy * dy);

        // Volume des surfaces speciales (trou / eau) au curseur (a regler a l'oreille).
        private const float SpecialSurfaceVolume = 0.25f;

        // Surface speciale au curseur (trou / eau) : un son DEDIE (pas le son de roche d'un
        // mur), spatialise gauche-droite + pitch vertical (haut = aigu), comme le tick.
        private static void PlaySpecialSurface(int dx, int dy, SfxID id)
        {
            float pan = GameplayAudio.PanFromTiles(dx);
            float pitch = Mathf.Pow(2f, dy / 12f); // 1 demi-ton par ligne
            GameplayAudio.PlaySpatial(id, pan, pitch,
                SpecialSurfaceVolume * A11ySettings.NavigationVolume * GameplayAudio.DistanceTrim(Dist(dx, dy)));
        }

        // Volume du son de materiau au survol d'un mur (a regler a l'oreille).
        private const float WallSfxVolume = 0.5f;

        // Volume du marqueur sonore "interactible" (a regler a l'oreille).
        private const float ObjectMarkerVolume = 0.1f;

        // Objet interactible au survol. Le marqueur "interactible" (charge_bar_ui_1) est un
        // SUPPLEMENT d'identite ("on peut agir ici") : hauteur CONSTANTE, faible volume, juste
        // spatialise gauche-droite. Il se greffe TOUJOURS sur un son INFORMATIF qui, lui, porte
        // la position : le son du materiau de l'objet s'il en a un (spatialise + pitch vertical),
        // sinon le tick de deplacement standard. Beaucoup d'objets (coffre/etabli en bois...)
        // n'ont pas de materiau -> le tick garantit qu'il y a toujours un son porteur sous le
        // marqueur (qui sinon paraitrait etre le son principal et tromperait).
        private static void PlayObjectSfx(int2 tile, in TileInfo info, int dx, int dy)
        {
            // Son informatif (porte la position).
            int matSfx = ResolveObjectSfx(info.ObjectId);
            if (matSfx != 0)
            {
                int2 r = EntityMonoBehaviour.ToRenderFromWorld(tile);
                var pos = new Vector3(r.x, 0f, r.y);
                float pitchV = Mathf.Pow(2f, dy / 12f); // 1 demi-ton par ligne
                GameplayAudio.PlayTableSpatialNoPitchDev(matSfx, pos, WallSfxVolume * A11ySettings.NavigationVolume, pitchV);
            }
            else
            {
                PlayMoveTick(dx, dy);
            }

            // Marqueur interactible en supplement : hauteur fixe (1f), juste pan gauche-droite.
            float pan = GameplayAudio.PanFromTiles(dx);
            GameplayAudio.PlaySpatial(SfxID.charge_bar_ui_1, pan, 1f,
                ObjectMarkerVolume * A11ySettings.NavigationVolume * GameplayAudio.DistanceTrim(Dist(dx, dy)));
        }

        // Son du materiau que le jeu attribue a l'objet pose : ObjectDataCD (objectID +
        // variation) -> TileEffectCD -> sfxTableDestroyId, meme chemin qu'EffectsManager.
        // PAS de fallback (consigne : "materiau si present") -> 0 si l'objet n'en a pas.
        // Variation 0 par defaut : on ne dispose pas de la variation cote curseur, et le
        // son de destruction est rarement decline par variation.
        private static int ResolveObjectSfx(ObjectID objectId)
        {
            try
            {
                var od = new ObjectDataCD { objectID = objectId, variation = 0 };
                if (PugDatabase.HasComponent<TileEffectCD>(od))
                {
                    int id = PugDatabase.GetComponent<TileEffectCD>(od).sfxTableDestroyId;
                    if (id != 0) return id;
                }
            }
            catch { }
            return 0;
        }

        // Joue le son du materiau du mur pointe, spatialise NATIVEMENT a la position de la
        // case (pan + distance par rapport au perso, gracieusete du jeu) + un pitch vertical
        // maison (+1 demi-ton par ligne d'ecart, haut = aigu) car le natif ne distingue pas
        // haut/bas. On passe par PlayTableSpatialNoPitchDev pour neutraliser le random pitch
        // de la table (sinon il brouillerait l'info verticale). Coords RENDER (comme InViewport).
        private static void PlayWallMaterialSfx(int2 tile, in TileInfo info, int dy)
        {
            int sfx = ResolveWallSfx(in info);
            int2 r = EntityMonoBehaviour.ToRenderFromWorld(tile);
            var pos = new Vector3(r.x, 0f, r.y);
            float pitch = Mathf.Pow(2f, dy / 12f); // 1 demi-ton par ligne
            GameplayAudio.PlayTableSpatialNoPitchDev(sfx, pos, WallSfxVolume * A11ySettings.NavigationVolume, pitch);

            // Minerai (couche ore / ancientCrystal, detectee independamment du mur bloquant :
            // un filon peut etre superpose a un mur de terre sans etre LA tuile bloquante)
            // : le jeu superpose un "oreHit" par-dessus le son du materiau -> on fait pareil
            // pour reperer les minerais a l'oreille (sinon un mur nu et un mur a minerai
            // sonnent pareil). Pitch constant (signal "minerai" stable et reconnaissable) ;
            // le son de materiau, lui, porte deja l'axe vertical.
            if (info.HasOre && !info.IsImmune)
                GameplayAudio.PlayTableSpatialNoPitchDev(SfxTableID.oreHit, pos, WallSfxVolume * A11ySettings.NavigationVolume, 1f);
        }

        // Le son que le jeu attribue a la tuile : ObjectInfo de la tuile (type + tileset)
        // -> son composant TileEffectCD -> sfxTableDestroyId (le seul son vraiment decline
        // par materiau ; le son de coup, lui, est generique pour la plupart des murs).
        // Pour un minerai (ressource contenue), c'est le mur porteur qui porte le son
        // (comme EffectsManager fait). Fallback defaultTileDestroy.
        private static int ResolveWallSfx(in TileInfo info)
        {
            try
            {
                TileType wt = info.WallType;
                ObjectInfo tileInfo = wt.IsContainedResource()
                    ? PugDatabase.TryGetTileItemInfo(TileType.wall, info.WallTileset)
                    : PugDatabase.TryGetTileItemInfo(wt, info.WallTileset);
                if (tileInfo != null)
                {
                    var od = new ObjectDataCD { objectID = tileInfo.objectID, variation = tileInfo.variation };
                    if (PugDatabase.HasComponent<TileEffectCD>(od))
                    {
                        int id = PugDatabase.GetComponent<TileEffectCD>(od).sfxTableDestroyId;
                        if (id != 0) return id;
                    }
                }
            }
            catch { }
            return SfxTableID.defaultTileDestroy;
        }

        // "Plus de details" sur la case sous le curseur (commande Triangle + haut). Donne ce
        // que le survol normal NE dit PAS a la voix : surtout le MATERIAU du mur (le survol
        // ne joue qu'un son). Trou/eau -> leur libelle ; objet -> son nom ; sinon le sol.
        internal static void AnnounceCursorDetails()
        {
            string text;
            if (TileQuery.HasWall)
            {
                if (TileQuery.WallType == TileType.pit) text = Strings.L("cursor.pit");
                else if (TileQuery.WallType == TileType.water) text = Strings.L("cursor.water");
                else text = ResolveWallName();
                // Mur scelle : l'immunite d'abord (l'info qui change tout), puis le materiau.
                if (TileQuery.IsImmune)
                    text = string.IsNullOrEmpty(text)
                        ? Strings.L("cursor.immune")
                        : Strings.L("cursor.immune") + ", " + text;
            }
            else if (TileQuery.ObjectId != ObjectID.None)
            {
                var snap = TileQuery.Snapshot();
                text = AppendIndustry(AppendPlant(InGameTtsCore.ResolveObjectName(TileQuery.ObjectId), in snap), in snap, true);
            }
            else
                text = GroundLabel(TileQuery.Ground);

            // Coordonnees monde de la case pointee, en queue de l'annonce (demande
            // utilisateur : repere absolu pour noter/retrouver un endroit).
            string pos = Strings.L("vitals.position") + " " + _cursor.x + ", " + _cursor.y;
            text = string.IsNullOrEmpty(text) ? pos : text + ", " + pos;

            TtsText.Say(text, true);

            // Dev : sur une machine industrielle, dumper ses composants reels dans le log
            // (le contenu d'automation n'est atteignable qu'avec l'ecarlate -> seul moyen
            // de finaliser l'a11y industrie sur du concret). Silencieux pour les testeurs.
            if (CoreKeeperAccessMod.DevMode
                && (TileQuery.Conveyor || TileQuery.Power != PowerState.None || TileQuery.HasStorage))
            {
                AutomationDiag.Tile = _cursor;
                AutomationDiag.Requested = true;
            }
        }

        // Nom du materiau du mur pointe (ObjectInfo de la tuile -> nom localise), pour la
        // commande de details. Meme resolution que ResolveWallSfx, mais on rend le NOM.
        private static string ResolveWallName()
        {
            try
            {
                TileType wt = TileQuery.WallType;
                ObjectInfo info = wt.IsContainedResource()
                    ? PugDatabase.TryGetTileItemInfo(TileType.wall, TileQuery.WallTileset)
                    : PugDatabase.TryGetTileItemInfo(wt, TileQuery.WallTileset);
                if (info != null) return InGameTtsCore.ResolveObjectName(info.objectID);
            }
            catch { }
            return null;
        }

        // Libelle d'un sol notable. Sols agricoles traduits (labour/arrosage = info clef
        // pour cultiver a l'aveugle) ; tout autre sol notable garde son nom brut (dette
        // i18n mineure existante, rarement declenchee).
        private static string GroundLabel(TileType g)
        {
            if (g == TileType.dugUpGround) return Strings.L("cursor.tilled");
            if (g == TileType.wateredGround) return Strings.L("cursor.watered");
            return g.ToString();
        }

        // Ajoute l'etat d'une plante au libelle de l'objet survole : "en croissance" /
        // "prete a recolter". Une plante en croissance sur sol NON arrose "a soif" (elle
        // ne pousse pas tant que le sol n'est pas arrose, cf. PlantsGrowingSystem).
        // Etat d'une plante, en UN seul mot, exclusif (pas de numero de stade) :
        //  - mure              -> "prete a recolter"
        //  - en croissance NON arrosee -> "a soif" (elle ne pousse pas tant qu'on n'arrose pas)
        //  - en croissance arrosee     -> "en croissance"
        private static string AppendPlant(string name, in TileInfo info)
        {
            if (info.Plant == PlantState.None) return name;
            string st;
            if (info.Plant == PlantState.Ready)
                st = Strings.L("cursor.plant_ready");
            else if (info.Ground != TileType.wateredGround)
                st = Strings.L("cursor.plant_thirsty");
            else
                st = Strings.L("cursor.plant_growing");
            return string.IsNullOrEmpty(name) ? st : name + ", " + st;
        }

        // Ajoute l'etat d'automation au libelle de l'objet survole : sens d'un convoyeur
        // (« vers Nord ») et alimentation electrique (« sous tension » / « hors tension »).
        // Les connexions du reseau (vers quels cotes un cable propage) ne sont ajoutees que
        // dans les details (Triangle+haut, includeConnections=true) pour ne pas saturer le
        // survol case par case.
        private static string AppendIndustry(string name, in TileInfo info, bool includeConnections)
        {
            if (info.Conveyor)
            {
                string dir = CardinalLabel(info.ConveyorDir);
                if (!string.IsNullOrEmpty(dir))
                    name = Join(name, Strings.L("cursor.toward") + " " + dir);
            }
            if (info.Power != PowerState.None)
            {
                name = Join(name, Strings.L(info.Power == PowerState.On ? "cursor.powered" : "cursor.unpowered"));
                if (includeConnections && info.Connections != 0)
                {
                    string c = ConnectionLabel(info.Connections);
                    if (!string.IsNullOrEmpty(c)) name = Join(name, Strings.L("cursor.connected") + " " + c);
                }
            }
            // Stockage : vide, ou nombre d'objets dedans (surveiller un stock sans l'ouvrir).
            if (info.HasStorage)
                name = Join(name, info.StorageCount == 0
                    ? Strings.L("cursor.storage_empty")
                    : info.StorageCount + " " + Strings.L("cursor.items"));
            return name;
        }

        private static string Join(string a, string b)
            => string.IsNullOrEmpty(a) ? b : a + ", " + b;

        // Sens cardinal d'un vecteur case (convoyeur). Convoyeurs cardinaux purs : une
        // seule composante non nulle ; priorite a l'axe nord-sud si jamais les deux.
        private static string CardinalLabel(int2 d)
        {
            if (d.y > 0) return Strings.L("dir.n");
            if (d.y < 0) return Strings.L("dir.s");
            if (d.x > 0) return Strings.L("dir.e");
            if (d.x < 0) return Strings.L("dir.w");
            return null;
        }

        // Liste des cotes connectes au reseau electrique (ElectricityDirectionMask brut :
        // East=1, North=2, South=4, West=8), ordonnee N, E, S, O.
        private static string ConnectionLabel(int mask)
        {
            var parts = new System.Collections.Generic.List<string>(4);
            if ((mask & 2) != 0) parts.Add(Strings.L("dir.n"));
            if ((mask & 1) != 0) parts.Add(Strings.L("dir.e"));
            if ((mask & 4) != 0) parts.Add(Strings.L("dir.s"));
            if ((mask & 8) != 0) parts.Add(Strings.L("dir.w"));
            return string.Join(", ", parts);
        }

        private static void Reset()
        {
            _detached = false;
            _pending = false;
            StealsDpad = false;
            StealsCross = false;
            TileQuery.Active = false;
            GameplayAction.Disarm();
        }

        private static int2 ToTile(Vector3 wp)
            => new int2((int)math.round(wp.x), (int)math.round(wp.z));

        // Direction de visee (stick droit virtuel) du joueur vers la case ciblee.
        // Sous le joueur (ecart nul) -> sud par defaut, faute de direction.
        private static Vector2 AimToward(int2 cursor, int2 playerTile)
        {
            Vector2 d = new Vector2(cursor.x - playerTile.x, cursor.y - playerTile.y);
            if (d.sqrMagnitude < 0.01f) return new Vector2(0f, -1f);
            return d.normalized;
        }

        // Demi-largeur visible en cases (le "range" pour normaliser le pan -1..+1).
        private static float HalfWidthTiles()
        {
            var cam = Manager.camera != null ? Manager.camera.gameCamera : null;
            return cam != null ? cam.orthographicSize * cam.aspect : 0f;
        }

        private static bool InViewport(int2 tile)
        {
            if (Manager.camera == null) return true;
            // IsPointInViewport attend un point dans l'espace RENDER de la gameCamera
            // (qui vit autour de RenderOrigo, recale quand le joueur s'eloigne). Lui
            // passer des coords MONDE rendait le test faux des qu'on quittait l'origine
            // -> toute cible D-pad jugee hors ecran, D-pad "gele" hors zone de depart.
            int2 r = EntityMonoBehaviour.ToRenderFromWorld(tile);
            return Manager.camera.IsPointInViewport(new Vector3(r.x, 0f, r.y), 0f);
        }

        private static bool DpadDir(Joystick joy, out int2 dir)
        {
            if (ButtonDownById(joy, DpadUp)) { dir = new int2(0, 1); return true; }
            if (ButtonDownById(joy, DpadDown)) { dir = new int2(0, -1); return true; }
            if (ButtonDownById(joy, DpadLeft)) { dir = new int2(-1, 0); return true; }
            if (ButtonDownById(joy, DpadRight)) { dir = new int2(1, 0); return true; }
            dir = default;
            return false;
        }

        private static float AxisById(Joystick joy, int id)
        {
            for (int i = 0; i < joy.axisCount; i++)
                if (joy.AxisElementIdentifiers[i].id == id) return joy.GetAxis(i);
            return 0f;
        }

        private static bool ButtonDownById(Joystick joy, int id)
        {
            for (int i = 0; i < joy.buttonCount; i++)
                if (joy.ButtonElementIdentifiers[i].id == id) return joy.GetButtonDown(i);
            return false;
        }
    }

    // Pont mod -> ECS pour le deplacement : la case cible, pilotee par le systeme.
    internal static class MoveCommand
    {
        public static bool Active;
        public static float2 Target; // case cible (coordonnee monde x,z)
    }

    // Action contextuelle ponctuelle (pose / mine / interagir). On ne touche PAS au
    // ClientInput (le buttonSetMask serait deja fige) : on arme un bouton natif et on
    // injecte une visee. Lu par les patches Harmony sur PlayerInput (WasButtonPressedDown
    // ThisFrame / IsButtonCurrentlyDown / GetInputAxisValue), donc SendClientInputSystem
    // fabrique lui-meme le button state + le masque. Cycle de vie : l'AIM est pilote en
    // continu par BuildModeNavigator.Tick (pre-orientation) ; le BOUTON (Held+Pressed) est
    // arme une frame sur l'appui de Croix puis relache par PlayerMoveToSystem apres la
    // passe -> un appui = un coup (pas de maintien).
    internal static class GameplayAction
    {
        public static PlayerInput.InputType? Held;     // bouton maintenu (IsButtonCurrentlyDown)
        public static PlayerInput.InputType? Pressed;  // bouton "presse cette frame" (WasButtonPressedDownThisFrame)
        public static bool AimActive;                  // forcer la visee (stick droit virtuel)
        public static Vector2 AimDir;                  // direction de visee injectee

        public static void Disarm()
        {
            Held = null;
            Pressed = null;
            AimActive = false;
            AimDir = default;
        }
    }

    // Pilote la marche du joueur local en ligne droite vers MoveCommand.Target en
    // poussant sa direction de mouvement (comme l'input stick). Tourne APRES
    // SendClientInputSystem (qui reecrit movementDirection depuis le stick chaque frame),
    // ce qui permet de detecter quand le joueur reprend la main au stick -> on annule.
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(RunSimulationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(SendClientInputSystem))]
    public partial class PlayerMoveToSystem : SystemBase
    {
        private const float ArriveDist = 0.5f;
        private const float StickDeadzone = 0.3f;
        private EntityQuery _query;

        // API runtime classique (pas de SystemAPI.Query : son source generator ne tourne
        // pas dans la compilation a chaud du ModLoader).
        protected override void OnCreate()
        {
            _query = GetEntityQuery(
                ComponentType.ReadWrite<ClientInputData>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<GhostOwnerIsLocal>());
        }

        protected override void OnUpdate()
        {
            bool buttonArmed = GameplayAction.Held.HasValue || GameplayAction.Pressed.HasValue;
            if (!MoveCommand.Active && !buttonArmed) return;

            if (MoveCommand.Active)
            {
                var entities = _query.ToEntityArray(Allocator.Temp);
                try
                {
                    if (entities.Length > 0)
                    {
                        Entity e = entities[0];
                        float2 pos = EntityManager.GetComponentData<LocalTransform>(e).Position.xz;
                        ClientInputData data = EntityManager.GetComponentData<ClientInputData>(e);
                        ClientInput ci = UnsafeUtility.As<ClientInputData, ClientInput>(ref data);

                        float2 delta = MoveCommand.Target - pos;
                        float dist = math.length(delta);
                        if (dist < ArriveDist) MoveCommand.Active = false;                       // arrive
                        else if (math.length(ci.movementDirection) > StickDeadzone) MoveCommand.Active = false; // reprise main
                        else
                        {
                            ci.movementDirection = delta / dist;
                            data = UnsafeUtility.As<ClientInput, ClientInputData>(ref ci);
                            EntityManager.SetComponentData(e, data);
                        }
                    }
                }
                finally { entities.Dispose(); }
            }

            // Un appui = un coup : on tourne APRES SendClientInputSystem, qui vient de lire
            // le bouton arme (button state + masque poses nativement). On relache le bouton
            // pour que l'action ne dure qu'une passe. L'aim (pre-orientation) reste pilote
            // par BuildModeNavigator.Tick, on n'y touche pas ici.
            if (buttonArmed) { GameplayAction.Held = null; GameplayAction.Pressed = null; }
        }
    }
}
