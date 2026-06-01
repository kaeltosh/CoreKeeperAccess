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

        public static void Tick()
        {
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null || Manager.ui == null) { Reset(); return; }

            // Jeu normal seulement : si une fenetre prend le D-pad, on se retire.
            if (Manager.ui.isAnyInventoryShowing
                || (Manager.ui.characterWindow != null && Manager.ui.characterWindow.isShowing))
            { Reset(); return; }

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
            if (joy != null && DpadDir(joy, out int2 dir))
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
                    if (croixDown)
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
                        else if (TileQuery.ObjectId != ObjectID.None)
                        {
                            // Objet pose sur case NON bloquante (coffre, machine) -> interagir.
                            GameplayAction.Held = PlayerInput.InputType.INTERACT_WITH_OBJECT;
                            GameplayAction.Pressed = PlayerInput.InputType.INTERACT_WITH_OBJECT;
                        }
                        else if (TileQuery.ResultValid && TileQuery.ResultTile.Equals(_cursor))
                        {
                            // Case CONFIRMEE vide (lecture de tuile a jour pour cette case)
                            // -> s'y deplacer. Le garde-fou evite qu'un appui sur une case a
                            // peine survolee (lecture pas encore republiee, mur/objet vu comme
                            // "vide") provoque un deplacement non voulu sur un minable/objet.
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

            StealsCross = _detached; // vol de Croix actif uniquement curseur detache
        }

        private static void Announce()
        {
            // Tick SPATIALISE a chaque deplacement (meme sur du vide) : confirme la
            // position du curseur par rapport au joueur. Pan gauche-droite selon l'ecart
            // horizontal (borne au range = demi-largeur visible) ; pitch +1 demi-ton par
            // ligne d'ecart vertical (au-dessus = plus aigu). Son natif placeholder.
            var p = Manager.main != null ? Manager.main.player : null;
            int2 pt = p != null ? ToTile(p.WorldPosition) : _cursor;
            int dx = _cursor.x - pt.x, dy = _cursor.y - pt.y;
            float halfW = HalfWidthTiles();
            float pan = halfW > 0.1f ? Mathf.Clamp(dx / halfW, -1f, 1f) : 0f;
            float pitch = Mathf.Pow(2f, dy / 12f); // 1 demi-ton par ligne
            GameplayAudio.PlaySpatial(SfxID.inventory_select, pan, pitch, 0.4f);

            // Repere central : curseur sur la case du personnage. Sans coordonnees, c'est
            // le point d'ancrage pour se retrouver. On l'annonce et on s'arrete la (le sol
            // sous le perso n'est pas une info utile ici).
            if (dx == 0 && dy == 0)
            {
                TtsText.Say(Strings.L("cursor.player"), true);
                return;
            }

            // TTS pour le contenu remarquable (le sol de base reste muet en voix).
            // Priorite : objet/construction pose > mur > sol notable.
            string text = null;
            if (TileQuery.ObjectId != ObjectID.None)
                text = InGameTtsCore.ResolveObjectName(TileQuery.ObjectId);
            else if (TileQuery.HasWall)
                text = Strings.L("cursor.wall");
            else if (TileQuery.Ground != TileType.ground)
                text = TileQuery.Ground.ToString(); // nom brut, table i18n a venir

            if (!string.IsNullOrEmpty(text)) TtsText.Say(text, true);
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
