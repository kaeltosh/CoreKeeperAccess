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

        // Mode LIGNE : le bouton de pose (LT/SECOND_INTERACT) maintenu avec le curseur
        // detache sur une case adjacente verrouille l'offset relatif curseur/joueur. Le
        // curseur suit alors le perso pendant qu'il marche et la pose native, maintenue,
        // tombe case par case sur la case suivie (via la visee injectee) -> ligne continue.
        private static bool _lineMode;
        private static int2 _lineOffset;

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

            // Mode LIGNE : tant que le bouton de pose (LT) est maintenu avec le curseur
            // detache sur une case ADJACENTE et un objet a poser/appliquer en main, on
            // verrouille l'offset relatif. Sinon (LT relache) on quitte le mode.
            bool placeHeld = _detached && SecondInteractHeld(player) && HasPlacement(player);
            if (_lineMode && !placeHeld) _lineMode = false;
            else if (placeHeld && !_lineMode)
            {
                int2 off = _cursor - playerTile;
                if (Mathf.Max(Mathf.Abs(off.x), Mathf.Abs(off.y)) == 1) // une des 8 cases adjacentes
                {
                    _lineMode = true;
                    _lineOffset = off;
                }
            }

            // Bouger au stick gauche -> recoller au joueur (SAUF en mode ligne, ou le curseur
            // est asservi a l'offset verrouille et doit suivre le perso pendant qu'il marche).
            if (joy != null && !_lineMode)
            {
                float ax = AxisById(joy, LeftStickX), ay = AxisById(joy, LeftStickY);
                if (ax * ax + ay * ay > StickMove * StickMove) _detached = false;
            }

            // Filet : si le curseur detache n'est plus dans le champ (le perso s'est
            // eloigne sous lui, la camera l'a suivi), on le recolle. Sinon toute cible
            // D-pad tomberait hors viewport et le D-pad semblerait gele.
            if (_detached && !InViewport(_cursor)) { _detached = false; _lineMode = false; }

            if (!_detached)
            {
                _cursor = playerTile;
                TileQuery.Active = false;
            }

            // Mode ligne : le curseur translate avec le perso a offset constant. A chaque
            // nouvelle case franchie, on relance la lecture de tuile (tick sonore = compteur
            // de cases) ; le D-pad est inhibe (curseur asservi a l'offset).
            if (_lineMode)
            {
                int2 followed = playerTile + _lineOffset;
                if (!followed.Equals(_cursor))
                {
                    _cursor = followed;
                    TileQuery.Tile = _cursor;
                    TileQuery.Active = true;
                    TileQuery.ResultValid = false;
                    _pending = true;
                }
            }
            // D-pad -> deplacer le curseur d'une case (un cran par appui), borne a l'ecran.
            // Sauf si Triangle (touche access) OU R3 (scanner de proximite) est tenu : le D-pad
            // est alors reserve aux commandes de ce modificateur, il ne deplace plus le curseur.
            // Sauf aussi si la canne laser est active (stick droit pousse) : le laser a alors priorite.
            else if (joy != null && !InfoKey.ModifierHeld && !ScannerModifier.Held && !LaserCane.Active && DpadDir(joy, out int2 dir))
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

            // Annonce quand le resultat publie correspond a la case du curseur. En mode ligne :
            // tick SONORE seul (compteur de cases a l'oreille), pas de TTS hache a chaque case.
            if (_pending && TileQuery.ResultValid && TileQuery.ResultTile.Equals(_cursor))
            {
                if (_lineMode) AnnounceLineTick(playerTile);
                else Announce();
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

            // AUTRE JOUEUR (multi) sur la case pointee : il prime sur le contenu de la case
            // (retour testeur 29 juillet 2026, "on ne se voit pas" - un voyant voit d'abord le
            // personnage, pas la dalle sous ses pieds). Positions lues dans le scan de
            // visibilite deja publie en continu par TileReaderSystem (~4 Hz), aucune requete
            // supplementaire. Clic sec = timbre "joueur" partage avec le scanner et la canne.
            string other = OtherPlayerOn(_cursor);
            if (other != null)
            {
                GameplayAudio.PlaySpatial(ProximityScanner.PlayerSfx,
                    GameplayAudio.PanFromTiles(dx), Mathf.Pow(2f, dy / 12f),
                    MoveTickVolume * A11ySettings.NavigationVolume * GameplayAudio.DistanceTrim(Dist(dx, dy)));
                TtsText.Say(other, true);
                return;
            }

            var info = TileQuery.Snapshot();
            SonifyTile(_cursor, in info, dx, dy, true);
        }

        // Tick SONORE seul a chaque case franchie en mode ligne (compteur de cases a
        // l'oreille). Meme sonification spatialisee que le curseur (mur/objet/sol porte la
        // position : pan + pitch vertical) mais SANS TTS : tracer une ligne ne doit pas
        // hacher la voix a chaque case.
        private static void AnnounceLineTick(int2 playerTile)
        {
            int dx = _cursor.x - playerTile.x, dy = _cursor.y - playerTile.y;
            var info = TileQuery.Snapshot();
            SonifyTile(_cursor, in info, dx, dy, false);

            // Dev : verifier que la case REELLEMENT posee par le jeu (bestPositionToPlaceAt)
            // colle a la case suivie -> calibration de la geometrie sans deviner (cf. fiche).
            if (CoreKeeperAccessMod.DevMode)
            {
                try
                {
                    var p = Manager.main.player;
                    if (EntityUtility.HasComponentData<PlacementCD>(p.entity, p.world))
                    {
                        var bp = EntityUtility.GetComponentData<PlacementCD>(p.entity, p.world).bestPositionToPlaceAt;
                        Diag.Log("A11yLinePlace", $"cursor={_cursor.x},{_cursor.y} best={bp.x},{bp.z} off={_lineOffset.x},{_lineOffset.y}");
                    }
                }
                catch { }
            }
        }

        // Sonification PARTAGEE d'une case (curseur de tuile ET canne laser). Joue le son
        // qui porte le contenu de la case (mur=materiau, trou/eau=son dedie, objet, sol=tick),
        // tous spatialises (pan gauche-droite + pitch vertical) par rapport au joueur via
        // dx/dy. Le TTS du libelle n'est dit que si speak=true : le curseur (deplacement
        // delibere au D-pad) le veut ; la canne laser (balayage continu) ne veut QUE le son,
        // pas un TTS hache. La case (coords MONDE) sert a positionner les sons natifs.
        public static void SonifyTile(int2 tile, in TileInfo info, int dx, int dy, bool speak)
        {
            // Detecteur d'obscurite (design fige 16 juillet 2026, parite avec un joueur
            // voyant) : case non eclairee -> UN SEUL earcon dedie ("le son du noir"), rien
            // d'autre (pas de TTS, pas de son de materiau/objet/sol) - on ne revele RIEN sur
            // une case qu'un voyant ne verrait pas non plus. Remplace toute la sonification
            // normale, curseur ET canne (les deux passent par ici). Cf.
            // core-keeper-darkness-gate.md.
            if (!info.Lit)
            {
                PlayDarkness(dx, dy);
                return;
            }

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
                if (info.WallType == TileType.pit || info.WallType == TileType.water)
                {
                    bool isWater = info.WallType == TileType.water;
                    PlaySpecialSurface(dx, dy, isWater ? SfxID.fish_splash_1_02 : SfxID.ui_plop_1_01);
                    if (speak)
                    {
                        // Un objet POSE sur l'eau (bateau...) ou au-dessus d'un trou ne doit
                        // pas etre masque par la surface : sans ca, le bateau amarre etait
                        // introuvable au curseur detache (retour testeur 24 juillet 2026, "on
                        // ne peut le detecter qu'au message d'interaction"). Le son de surface
                        // est conserve (il porte deja "eau/trou"), le TTS nomme l'objet puis
                        // la surface, comme un voyant voit le bateau ET l'eau autour.
                        string surface = Strings.L(isWater ? "cursor.water" : "cursor.pit");
                        string objName = info.ObjectId != ObjectID.None && !IsSilentDecor(info.ObjectId)
                            ? AppendObjectPaint(InGameTtsCore.ResolveObjectName(info.ObjectId), info.Paint)
                            : null;
                        TtsText.Say(string.IsNullOrEmpty(objName) ? surface : Join(objName, surface), true);
                    }
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
            // Exception : un cable SEUL sur la case (aucune machine gagnante, ObjectId est le
            // cable lui-meme) cache sous une dalle/sol notable est invisible a l'oeil pendant
            // un simple survol - on le traite comme une case vide avec sol notable, MAIS sa
            // tension fuite sur l'annonce du sol (meme convention que AppendIndustry pour un
            // objet non electrique pose sur un cable : "on devine sa presence par l'indication
            // de courant, sans dire cable" - reserve aux details Triangle+Haut de le NOMMER).
            bool hiddenByGround = info.WireObjectId != ObjectID.None
                && info.WireObjectId == info.ObjectId
                && info.Ground != TileType.ground;
            string text = null;
            if (info.ObjectId != ObjectID.None && !hiddenByGround)
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
                if (speak)
                {
                    // SummonArea non-interactible = sol physique de la salle de boss -> muet.
                    // SummonArea interactible = case injectee synthetiquement (vraie rune) -> annonce.
                    string objName = (info.ObjectId == ObjectID.SummonArea && !info.ObjectInteractable) || IsSilentDecor(info.ObjectId)
                        ? null
                        : AppendObjectPaint(InGameTtsCore.ResolveObjectName(info.ObjectId), info.Paint);
                    text = AppendToggle(AppendIndustry(AppendPlant(objName, in info), in info, false), in info);
                }
            }
            else
            {
                // Sol : tick de position. Sol notable annonce (le sol de base reste muet).
                PlayMoveTick(dx, dy);
                if (speak && info.Ground != TileType.ground)
                {
                    text = GroundLabel(info.Ground, info.GroundTileset);
                    if (hiddenByGround)
                        text = Join(text, Strings.L(info.WirePower == PowerState.On ? "cursor.powered" : "cursor.unpowered"));
                }
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

        // Un AUTRE joueur (multi) occupe-t-il cette case ? Rend son pseudo (ou le libelle de
        // categorie si le pseudo n'est pas encore replique), null sinon. Source = le scan de
        // visibilite publie en continu par TileReaderSystem, qui exclut deja le joueur local
        // et le familier ; sa cadence (~4 Hz) suffit pour un survol au curseur.
        internal static string OtherPlayerOn(int2 tile)
        {
            for (int i = 0; i < VisibilityScan.Count; i++)
            {
                var t = VisibilityScan.Targets[i];
                if (t.Cat != ProximityScanner.Category.Player) continue;
                if ((int)Mathf.Round(t.Pos.x) != tile.x || (int)Mathf.Round(t.Pos.y) != tile.y) continue;
                return string.IsNullOrEmpty(t.Name) ? Strings.L("scanner.cat.player") : t.Name;
            }
            return null;
        }

        // Volume des surfaces speciales (trou / eau) au curseur (a regler a l'oreille).
        private const float SpecialSurfaceVolume = 0.25f;

        // Surface speciale au curseur (trou / eau) : un son DEDIE (pas le son de roche d'un
        // mur), spatialise gauche-droite + pitch vertical (haut = aigu), comme le tick.
        // Rivage vu du bateau : la terre ferme, la ou la navigation s'arrete. Meme langage
        // spatial que les autres surfaces (pan + pitch vertical + trim distance). Timbre
        // PLACEHOLDER (pas de sable choisi a l'oreille) - a auditionner, cf.
        // core-keeper-sound-audition.md.
        internal static void PlayShore(int dx, int dy)
            => PlaySpecialSurface(dx, dy, SfxID.Footstep_Sand);

        public static void PreviewShore() => PlayShore(0, 0);

        private static void PlaySpecialSurface(int dx, int dy, SfxID id)
        {
            float pan = GameplayAudio.PanFromTiles(dx);
            float pitch = Mathf.Pow(2f, dy / 12f); // 1 demi-ton par ligne
            GameplayAudio.PlaySpatial(id, pan, pitch,
                SpecialSurfaceVolume * A11ySettings.NavigationVolume * GameplayAudio.DistanceTrim(Dist(dx, dy)));
        }

        // Volume du "son du noir" (detecteur d'obscurite) - a regler a l'oreille, timbre
        // choisi par audition (cf. core-keeper-sound-audition.md).
        private const float DarknessVolume = 0.5f;

        // Case sombre : meme grammaire spatiale que le reste (pan gauche-droite + pitch
        // vertical par rapport au joueur), UN SEUL earcon, aucun TTS.
        private static void PlayDarkness(int dx, int dy)
        {
            float pan = GameplayAudio.PanFromTiles(dx);
            float pitch = Mathf.Pow(2f, dy / 12f); // 1 demi-ton par ligne
            GameplayAudio.PlayDarknessEarcon(pan, pitch,
                DarknessVolume * A11ySettings.NavigationVolume * GameplayAudio.DistanceTrim(Dist(dx, dy)));
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
        // ne joue qu'un son). Empile TOUTES les couches presentes sur la case (demande
        // testeur 21 juillet 2026 : plafond/cable/dalle ne doivent plus se masquer entre eux)
        // au lieu du choix exclusif d'origine (mur OU objet OU sol). Ordre : plafond, mur,
        // cable, objet pose, sol/dalle, position.
        internal static void AnnounceCursorDetails()
        {
            // Detecteur d'obscurite (cf. core-keeper-darkness-gate.md) : la sonification
            // normale du curseur coupe deja tout sur une case sombre (PlayDarkness), mais ce
            // combo de details la contournait - trou signale par testeur le 18 juillet 2026.
            // Meme principe : rien de la case, juste le signal dedie.
            if (!TileQuery.Lit)
            {
                TtsText.Say(Strings.L("cursor.dark"), true);
                return;
            }

            var snap = TileQuery.Snapshot();
            string text = null;

            // Autre joueur present : en TETE de l'empilage (c'est ce qu'un voyant remarque
            // d'abord sur la case), les couches de la case suivent normalement.
            string otherPlayer = OtherPlayerOn(_cursor);
            if (otherPlayer != null) text = Stack(text, otherPlayer);

            // Plafond : toujours annonce (intact ou troue), c'est une couche a part entiere.
            text = Stack(text, Strings.L(TileQuery.RoofHole ? "cursor.roofhole" : "cursor.roofed"));

            // Mur.
            if (TileQuery.HasWall)
            {
                string wallText;
                if (TileQuery.WallType == TileType.pit) wallText = Strings.L("cursor.pit");
                else if (TileQuery.WallType == TileType.water) wallText = Strings.L("cursor.water");
                else wallText = ResolveWallName();
                // Mur scelle : l'immunite d'abord (l'info qui change tout), puis le materiau.
                if (TileQuery.IsImmune)
                    wallText = string.IsNullOrEmpty(wallText)
                        ? Strings.L("cursor.immune")
                        : Strings.L("cursor.immune") + ", " + wallText;
                // Eau / trou : une surface, pas un bloc plein - ce qui est POSE dessus (bateau
                // amarre, structure au-dessus d'un trou) reste visible et doit etre annonce,
                // en tete puisqu'il prime a l'oeil sur la surface. Un vrai mur, lui, occupe
                // toute la case et masque tout (comportement inchange).
                if (TileQuery.WallType == TileType.pit || TileQuery.WallType == TileType.water)
                {
                    string overName = TileQuery.ObjectId != ObjectID.None && !IsSilentDecor(TileQuery.ObjectId)
                        ? InGameTtsCore.ResolveObjectName(TileQuery.ObjectId)
                        : null;
                    if (!string.IsNullOrEmpty(overName)) text = Stack(text, overName);
                }
                text = Stack(text, wallText);
            }

            // Cable / objet pose / sol : un mur occupe la case entiere, rien de tout ca n'est
            // visible dessous (meme exclusion que le comportement d'origine).
            if (!TileQuery.HasWall)
            {
                // Sol calcule EN AMONT. "Notable" (dalle a peindre/pont/rail...) sert a
                // detecter si un cable SEUL sur la case (aucune machine gagnante cote
                // ObjectIndex) est en fait cache visuellement sous une dalle posee dessus.
                // Le LIBELLE, lui, couvre aussi le sol de BASE depuis le 27 juillet 2026
                // (retour testeurs : "quand on fait triangle plus haut on ne voit plus les
                // sols standards, autant tout voir dans ce mode, le sol est utile notamment
                // pour les cultures") - regression de la reecriture en empilage du 21 juillet.
                // Le survol normal, lui, reste muet sur le sol de base (inchange).
                // rawFallback:false sur le sol de base -> si la resolution echoue on se tait
                // plutot que d'annoncer le nom d'enum brut "Ground" sur chaque case nue.
                bool groundNotable = TileQuery.Ground != TileType.ground;
                string groundText = GroundLabel(TileQuery.Ground, TileQuery.GroundTileset, groundNotable);

                // Cable : la case a-t-elle un cable, et est-il MASQUE - par un autre objet
                // gagnant (foreuse/machine posee dessus), OU par une dalle/sol notable posee
                // par-dessus (le cable est alors le SEUL "objet" cote ObjectIndex, mais
                // visuellement invisible sous la dalle - pont, dalle de pierre, dalle a
                // peindre... - meme si rien ne l'occupe cote entites) ? Si le cable EST
                // l'objet affiche ET que rien ne le recouvre, sa tension sort deja via ce
                // chemin (AppendIndustry) - pas de doublon ici.
                bool wireOnCase = TileQuery.WireObjectId != ObjectID.None;
                bool hiddenByGround = wireOnCase && TileQuery.WireObjectId == TileQuery.ObjectId && groundNotable;
                bool wireMasked = wireOnCase && (TileQuery.WireObjectId != TileQuery.ObjectId || hiddenByGround);
                if (wireMasked)
                {
                    // Coupe la fuite de tension vers le nom de l'objet masquant (AppendIndustry,
                    // branche Power/WirePower) : la clause dediee ci-dessous porte deja cette
                    // info, eviter de la dire deux fois sous deux formulations differentes.
                    snap.WirePower = PowerState.None;
                    if (hiddenByGround) snap.Power = PowerState.None;
                }

                // Objet pose : tu si le cable lui-meme est la seule entree ET qu'une dalle le
                // recouvre (rien d'autre a dire ici, la dalle parle pour la case ci-dessous).
                if (TileQuery.ObjectId != ObjectID.None && !hiddenByGround)
                {
                    string objName = IsSilentDecor(TileQuery.ObjectId)
                        ? null
                        : AppendObjectPaint(InGameTtsCore.ResolveObjectName(TileQuery.ObjectId), snap.Paint);
                    string objText = AppendToggle(AppendIndustry(AppendPlant(objName, in snap), in snap, true), in snap);
                    text = Stack(text, objText);
                }

                // Sol / dalle (couche de revetement, ex. dalle a peindre/pont/rail) : annonce
                // toujours quand NOTABLE, meme si un objet occupe la case (ne doit plus etre
                // masque) - sol de base reste muet (evite le "Ground" brut sur chaque case nue).
                // AVANT le cable : ce qui est VISIBLE (objet, dalle) prime dans l'ordre,
                // l'info cachee (cable masque) vient en dernier, jamais en tete.
                text = Stack(text, groundText);

                // Decor de sol pose en ENTITE (carrelage caverneux, grand carreau de pierre) :
                // depuis le 10 aout 2026 il cede la case a ce qu'on pose dessus (il masquait
                // lampes et portes electriques), mais l'INSPECTION deliberee le nomme quand
                // meme, a sa place logique : avec le sol, avant l'info cachee du cable. Si
                // c'est lui l'objet de la case (rien dessus), il est deja sorti plus haut.
                if (TileQuery.FloorObjectId != ObjectID.None && TileQuery.FloorObjectId != TileQuery.ObjectId)
                    text = Stack(text, InGameTtsCore.ResolveObjectName(TileQuery.FloorObjectId));

                if (wireMasked)
                {
                    string wireName = InGameTtsCore.ResolveObjectName(TileQuery.WireObjectId);
                    if (string.IsNullOrEmpty(wireName)) wireName = Strings.L("cursor.wire");
                    string tension = Strings.L(TileQuery.WirePower == PowerState.On ? "cursor.powered" : "cursor.unpowered");
                    text = Stack(text, wireName + ", " + tension);
                }
            }

            // Coordonnees monde de la case pointee, en queue de l'annonce (demande
            // utilisateur : repere absolu pour noter/retrouver un endroit).
            string pos = Strings.L("vitals.position") + " " + _cursor.x + ", " + _cursor.y;
            text = string.IsNullOrEmpty(text) ? pos : text + ", " + pos;

            TtsText.Say(text, true);

            // Dev : dumper dans le log l'etat resolu de la case (HasWall/WallType/ObjectId...)
            // ET toutes les entites-objets brutes alentour, meme si la case est classee
            // bloquante (pit/eau) - sert a diagnostiquer les cas ou un decor naturel se
            // dispute une case avec un objet pose par le joueur (ex. pont sur trou/lac).
            // Silencieux pour les testeurs.
            if (CoreKeeperAccessMod.DevMode)
            {
                // Sol de base : trace la resolution du nom (27 juillet 2026). Si le TTS reste
                // muet sur "sol de terre"/"sol de pierre", ce log dit si TryGetTileItemInfo
                // rend un ObjectID exploitable pour TileType.ground selon le tileset, ou s'il
                // faut passer par une table i18n maison indexee sur le tileset.
                Diag.Log("A11yGroundDiag", "ground=" + TileQuery.Ground
                    + " tileset=" + TileQuery.GroundTileset
                    + " label=" + (GroundLabel(TileQuery.Ground, TileQuery.GroundTileset, false) ?? "<null>"));
                AutomationDiag.Tile = _cursor;
                AutomationDiag.Requested = true;
            }
        }

        // Nom du materiau du mur pointe (ObjectInfo de la tuile -> nom localise), pour la
        // commande de details. Meme resolution que ResolveWallSfx, mais on rend le NOM.
        // Couleur de peinture ajoutee en suffixe si le tileset est une teinte "base building".
        private static string ResolveWallName()
        {
            try
            {
                TileType wt = TileQuery.WallType;
                ObjectInfo info = wt.IsContainedResource()
                    ? PugDatabase.TryGetTileItemInfo(TileType.wall, TileQuery.WallTileset)
                    : PugDatabase.TryGetTileItemInfo(wt, TileQuery.WallTileset);
                if (info != null)
                    return AppendPaintColor(InGameTtsCore.ResolveObjectName(info.objectID), TileQuery.WallTileset);
            }
            catch { }
            return null;
        }

        // Couleur de peinture d'un mur/sol "base building" : les 14 teintes du pinceau ont
        // chacune leur propre valeur de Tileset (PaintToolSlot.PaintIndexToTileset, confirme
        // par decompil), independante de l'ObjectID (le nom de base ne change pas, seul le
        // sprite/tileset varie). Suffixe court, style adjectif ("Mur jaune"), pas de "peint"/
        // "a peindre" (consigne utilisateur, 8 juillet 2026).
        private static string AppendPaintColor(string name, int tileset)
        {
            string color = PaintColorLabel(tileset);
            if (string.IsNullOrEmpty(color)) return name;
            return string.IsNullOrEmpty(name) ? color : name + " " + color;
        }

        // Couleur de peinture d'un OBJET pose (meuble : table, tabouret, coffre...). Mecanisme
        // different des murs/sols ci-dessus : le pinceau ecrit ici PaintableObjectCD.color sur
        // l'entite (enum PaintableColor), l'ObjectID et le tileset ne changent pas du tout.
        // Meme rendu a l'oreille que pour un mur : suffixe adjectif ("Table jaune").
        // Retour testeur 29 juillet 2026 (tables et tabourets peints muets sur leur couleur).
        internal static string AppendObjectPaint(string name, PaintableColor paint)
        {
            string color = PaintColorName(paint);
            if (string.IsNullOrEmpty(color)) return name;
            return string.IsNullOrEmpty(name) ? color : name + " " + color;
        }

        // Memes libelles i18n que les murs/sols (paint.*) : une teinte du pinceau porte le meme
        // nom qu'elle soit appliquee a un mur, un sol ou un meuble.
        private static string PaintColorName(PaintableColor paint)
        {
            switch (paint)
            {
                case PaintableColor.Yellow: return Strings.L("paint.yellow");
                case PaintableColor.Green: return Strings.L("paint.green");
                case PaintableColor.Red: return Strings.L("paint.red");
                case PaintableColor.Purple: return Strings.L("paint.purple");
                case PaintableColor.Blue: return Strings.L("paint.blue");
                case PaintableColor.Brown: return Strings.L("paint.brown");
                case PaintableColor.White: return Strings.L("paint.white");
                case PaintableColor.Black: return Strings.L("paint.black");
                case PaintableColor.Orange: return Strings.L("paint.orange");
                case PaintableColor.Cyan: return Strings.L("paint.cyan");
                case PaintableColor.Pink: return Strings.L("paint.pink");
                case PaintableColor.Gray: return Strings.L("paint.grey");
                case PaintableColor.Peach: return Strings.L("paint.peach");
                case PaintableColor.Teal: return Strings.L("paint.teal");
                default: return null; // Unpainted / __max__ : rien a dire
            }
        }

        private static string PaintColorLabel(int tileset)
        {
            switch ((Tileset)tileset)
            {
                case Tileset.BaseBuildingYellow: return Strings.L("paint.yellow");
                case Tileset.BaseBuildingGreen: return Strings.L("paint.green");
                case Tileset.BaseBuildingRed: return Strings.L("paint.red");
                case Tileset.BaseBuildingPurple: return Strings.L("paint.purple");
                case Tileset.BaseBuildingBlue: return Strings.L("paint.blue");
                case Tileset.BaseBuildingBrown: return Strings.L("paint.brown");
                case Tileset.BaseBuildingWhite: return Strings.L("paint.white");
                case Tileset.BaseBuildingBlack: return Strings.L("paint.black");
                case Tileset.BaseBuildingOrange: return Strings.L("paint.orange");
                case Tileset.BaseBuildingPink: return Strings.L("paint.pink");
                case Tileset.BaseBuildingCyan: return Strings.L("paint.cyan");
                case Tileset.BaseBuildingGrey: return Strings.L("paint.grey");
                case Tileset.BaseBuildingPeach: return Strings.L("paint.peach");
                case Tileset.BaseBuildingTeal: return Strings.L("paint.teal");
                default: return null;
            }
        }

        // Decor de terrain pur, jamais interactif ni minable (confirme en jeu et par le
        // decompil : ni HealthCD/OnTakeDamage exploitable, contrairement p.ex. au corail).
        // Le nom en est tu au curseur (son/tick garde, cf. appelants) : sinon il peut gagner
        // l'annonce sur une case de bordure de trou/lac au lieu du bloc que le joueur y a pose.
        // Reste dans l'ObjectIndex partage (sonar de proximite, alerte feu, etc. non touches) -
        // ce filtre ne s'applique qu'a la resolution du NOM, ici et dans AnnounceCursorDetails.
        private static bool IsSilentDecor(ObjectID id)
            => id == ObjectID.Stalagmite || id == ObjectID.OasisStalagmite;

        // Libelle d'un sol. Priorites : cles cursor.* JSON > TryGetTileItemInfo (revele le
        // contenu reel du sol, varie par biome/tileset, ex. chrysalis) > nom brut.
        // rawFallback:false = pas de filet "nom d'enum brut" (appele sur le sol de BASE dans
        // les details Triangle+haut : soit on sait le nommer proprement, soit on se tait -
        // annoncer "Ground" sur chaque case nue serait pire que le silence).
        private static string GroundLabel(TileType g, int tileset = 0, bool rawFallback = true)
        {
            if (g == TileType.dugUpGround) return Strings.L("cursor.tilled");
            if (g == TileType.wateredGround) return Strings.L("cursor.watered");
            if (Strings.TryL("cursor." + g.ToString(), out string custom)) return custom;
            try
            {
                ObjectInfo info = PugDatabase.TryGetTileItemInfo(g, tileset);
                if (info != null)
                {
                    string name = InGameTtsCore.ResolveObjectName(info.objectID);
                    if (!string.IsNullOrEmpty(name)) return AppendPaintColor(name, tileset);
                }
            }
            catch { }
            return rawFallback ? g.ToString() : null;
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
                // Source (generateur) : produit du courant, jamais "hors tension". Sinon
                // consommateur : sous / hors tension (l'icone que voit un voyant).
                string powerKey = info.Power == PowerState.Source ? "cursor.generating"
                    : info.Power == PowerState.On ? "cursor.powered" : "cursor.unpowered";
                name = Join(name, Strings.L(powerKey));
                if (includeConnections && info.Connections != 0)
                {
                    string c = ConnectionLabel(info.Connections);
                    if (!string.IsNullOrEmpty(c)) name = Join(name, Strings.L("cursor.connected") + " " + c);
                }
            }
            // Cable present sous l'objet : si l'objet lui-meme n'annonce pas de tension propre
            // (ex. convoyeur non electrique pose sur un cable), on signale la tension du cable
            // -> on devine sa presence par l'indication de courant (sans dire "cable").
            else if (info.WirePower != PowerState.None)
                name = Join(name, Strings.L(info.WirePower == PowerState.On ? "cursor.powered" : "cursor.unpowered"));
            // Stockage : vide, ou nombre d'objets dedans (surveiller un stock sans l'ouvrir).
            if (info.HasStorage)
                name = Join(name, info.StorageCount == 0
                    ? Strings.L("cursor.storage_empty")
                    : info.StorageCount + " " + Strings.L("cursor.items"));
            return name;
        }

        // Ajoute l'etat d'une porte/portail (ouvert/ferme) ou d'un levier (active/desactive)
        // au libelle de l'objet survole.
        private static string AppendToggle(string name, in TileInfo info)
        {
            string label = ToggleLabel(info.ObjectId, info.Toggle);
            return string.IsNullOrEmpty(label) ? name : Join(name, label);
        }

        // Libelle seul de l'etat a bascule (sans le nom de l'objet), reutilise par la
        // surveillance du changement d'etat au Croix (GameplayInput.WatchCursorToggle).
        // Le levier a son propre couple de mots (interrupteur, pas un battant) ; toutes
        // les autres bascules connues (portes/portails bois et electriques) partagent
        // "ouvert(e)/ferme(e)".
        internal static string ToggleLabel(ObjectID objectId, ToggleState toggle)
        {
            if (toggle == ToggleState.None) return null;
            bool on = toggle == ToggleState.On;
            string key = objectId == ObjectID.Lever
                ? (on ? "cursor.lever_on" : "cursor.lever_off")
                : (on ? "cursor.open" : "cursor.closed");
            return Strings.L(key);
        }

        private static string Join(string a, string b)
            => string.IsNullOrEmpty(a) ? b : a + ", " + b;

        // Empile une clause de plus dans l'annonce details (Triangle+haut) : chaque couche
        // (plafond/mur/cable/objet/sol) est separee par ". " des autres, jamais fusionnee
        // par une simple virgule (reservee aux details internes d'UNE clause, cf. Join).
        // Clause vide -> ignoree silencieusement (ex. sol de base, sans libelle).
        private static string Stack(string acc, string add)
            => string.IsNullOrEmpty(add) ? acc : (string.IsNullOrEmpty(acc) ? add : acc + ". " + add);

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

        // --- Apercus pour le menu d'apprentissage des sons (SoundGuide) ---
        // Chaque apercu rejoue le son du curseur mais CENTRE (pan 0, hauteur neutre), au volume
        // de navigation regle, pour l'ecouter au calme. Le mur et le minerai passent par le
        // materiau PAR DEFAUT (le timbre exact depend de la tuile reelle en jeu).
        public static void PreviewCursorTick() => PlayMoveTick(0, 0);
        public static void PreviewWater() => PlaySpecialSurface(0, 0, SfxID.fish_splash_1_02);
        public static void PreviewPit() => PlaySpecialSurface(0, 0, SfxID.ui_plop_1_01);

        public static void PreviewWall()
            => GameplayAudio.PlayTableSpatialNoPitchDev(SfxTableID.defaultTileDestroy, SelfRenderPos(),
                WallSfxVolume * A11ySettings.NavigationVolume, 1f);

        public static void PreviewOre()
            => GameplayAudio.PlayTableSpatialNoPitchDev(SfxTableID.oreHit, SelfRenderPos(),
                WallSfxVolume * A11ySettings.NavigationVolume, 1f);

        // Marqueur interactible : le son porteur (tick) PUIS le marqueur greffe par-dessus,
        // exactement comme en jeu (le marqueur seul, tres faible, n'a pas de sens isole).
        public static void PreviewInteractable()
        {
            PlayMoveTick(0, 0);
            GameplayAudio.PlaySpatial(SfxID.charge_bar_ui_1, 0f, 1f, ObjectMarkerVolume * A11ySettings.NavigationVolume);
        }

        // Position RENDER du joueur (= centre, pan 0, distance nulle) pour les apercus de sons
        // de table spatialises. Joueur absent (hors monde) -> origine, sans incidence.
        private static Vector3 SelfRenderPos()
        {
            var p = Manager.main != null ? Manager.main.player : null;
            if (p == null) return Vector3.zero;
            int2 r = EntityMonoBehaviour.ToRenderFromWorld(ToTile(p.WorldPosition));
            return new Vector3(r.x, 0f, r.y);
        }

        private static void Reset()
        {
            _detached = false;
            _pending = false;
            _lineMode = false;
            StealsDpad = false;
            StealsCross = false;
            TileQuery.Active = false;
            GameplayAction.Disarm();
        }

        // Bouton de pose (LT = SECOND_INTERACT) physiquement maintenu. Lecture native : notre
        // patch laisse passer (SECOND_INTERACT n'est ni vole ni simule par un Held ici).
        private static bool SecondInteractHeld(PlayerController player)
        {
            try { return player.inputModule.IsButtonCurrentlyDown(PlayerInput.InputType.SECOND_INTERACT, false); }
            catch { return false; }
        }

        // Un objet a poser / appliquer au sol est en main : le jeu lui attache un PlacementCD
        // (meubles, murs, sols ET outils a zone). Discriminant fiable du contexte "pose" -> le
        // mode ligne ne se declenche jamais avec une arme ou un objet non posable.
        private static bool HasPlacement(PlayerController player)
        {
            try { return EntityUtility.HasComponentData<PlacementCD>(player.entity, player.world); }
            catch { return false; }
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
        // Arrivee resserree au CENTRE de la tuile : a 0.5 le perso s'arretait des le bord
        // de la case cible -> decentre, portee de pose/interaction faussee. On coupe bien
        // plus pres du centre et on DECELERE dans le dernier bout (SlowRadius) pour ne pas
        // depasser, avec un plancher de vitesse (MinSpeed) pour franchir la deadzone du jeu.
        private const float ArriveDist = 0.12f;
        private const float SlowRadius = 0.6f;
        private const float MinSpeed = 0.4f;
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
                        if (dist < ArriveDist) MoveCommand.Active = false;                       // arrive (proche du centre)
                        else if (math.length(ci.movementDirection) > StickDeadzone) MoveCommand.Active = false; // reprise main
                        else
                        {
                            // Vitesse pleine de loin, ralentie dans le dernier bout (sans tomber
                            // sous MinSpeed) -> le perso se cale pres du centre sans depasser.
                            float speed = math.clamp(dist / SlowRadius, MinSpeed, 1f);
                            ci.movementDirection = delta / dist * speed;
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
