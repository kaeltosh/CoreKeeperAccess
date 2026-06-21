using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using Rewired;
using Unity.Mathematics;

namespace CoreKeeperAccess.Navigation
{
    // Menu carte accessible. Interagir avec un relais pose player.currentPortal et ouvre la carte
    // = l'interface de voyage rapide ; le double-tap Triangle l'ouvre aussi n'importe ou. On la
    // rend navigable en TTS.
    //
    // Refonte 21 juin : ce fichier n'est plus qu'un ORCHESTRATEUR mince. Le contenu est decoupe
    // en SECTIONS declaratives (MapSection) — destinations / points d'interet / mes balises /
    // journal — chacune autonome (s'annonce, se navigue, se confirme). Ici on ne fait que cycler
    // les sections aux bumpers (L1/R1) et deleguer le D-pad / la Croix a la section courante.
    // Ajouter une section future = une classe de plus dans le tableau, sans toucher la nav.
    internal static class TeleportNavigator
    {
        private const int DpadUp = 16, DpadDown = 18, DpadRight = 17, DpadLeft = 19, CrossButton = 6, Lb = 10, Rb = 11;

        private static bool _active;
        private static bool _coreReconstructAttempted; // reconstruction du dialogue d'activation deja reussie

        private static readonly DestinationsSection _destinations = new DestinationsSection();
        private static readonly PoiSection _poi = new PoiSection();
        private static readonly BeaconsSection _beacons = new BeaconsSection();
        private static readonly JournalSection _journal = new JournalSection();
        private static readonly MapSection[] _sections = { _destinations, _poi, _beacons, _journal };
        private static int _sectionIndex;
        private static MapSection Current => _sections[_sectionIndex];

        public static void Update()
        {
            // Carte ouverte = nav active, AVEC ou SANS relais (le double-tap Triangle ouvre la
            // carte hors relais : la section destinations est alors vide, les autres consultables).
            bool open = InputContext.MapOpen;
            if (open && !_active) Enter();
            else if (!open && _active) Exit();
            if (!_active) return;

            Current.Tick(); // selection differee d'une balise fraichement posee (section balises)
            HandleInput();
        }

        private static void Enter()
        {
            _active = true;
            UiSfx.Validate();   // ouverture du menu carte (le menu central du mod)
            RebuildAll();
            // Section de depart : destinations si on vient d'un relais (teleport possible), sinon
            // points d'interet (consultation pure).
            var player = Manager.main != null ? Manager.main.player : null;
            bool fromPortal = player != null && player.currentPortal != null && _destinations.HasReachable;
            _sectionIndex = fromPortal ? 0 : 1;
            AnnounceSection();
        }

        private static void Exit()
        {
            UiSfx.Entry();
            _active = false;
            foreach (var s in _sections) s.Clear();
            ActionMenu.Close();
        }

        // Scan UNIQUE des marqueurs, partage par toutes les sections (les sections de marqueurs
        // filtrent leur part, le journal l'ignore). + reconstruction one-shot du dialogue
        // d'activation du Core (retentee a chaque ouverture jusqu'a ce qu'une instance TheCore
        // soit chargee).
        private static void RebuildAll()
        {
            if (!_coreReconstructAttempted && CoreDialogueArchive.ReconstructActivation())
                _coreReconstructAttempted = true;
            var scanned = MapMarkerUtil.ScanMarkers();
            foreach (var s in _sections) s.Rebuild(scanned);
        }

        // Annonce le nom de la section courante puis son premier element (ou son vide).
        private static void AnnounceSection()
        {
            var s = Current;
            if (s.IsEmpty) { TtsText.Say(Strings.L(s.TitleKey) + ", " + Strings.L(s.EmptyKey), true); return; }
            TtsText.Say(Strings.L(s.TitleKey), true);
            s.Enter();
        }

        // Bumpers = faire defiler les sections. On re-scanne a chaque bascule (balises et POI ont
        // pu changer depuis l'ouverture : pose, scan de boss...).
        private static void SwitchSection(int dir)
        {
            RebuildAll();
            _sectionIndex = (_sectionIndex + dir + _sections.Length) % _sections.Length;
            UiSfx.Validate();   // changer de SECTION (bumpers) : son distinct de la nav d'entrees
            AnnounceSection();
        }

        private static void HandleInput()
        {
            // Saisie d'un nom OU menu contextuel ouvert : ils ont la main, on gele la nav.
            if (TextEntry.Active || ActionMenu.Active) return;
            var joy = ReInput.isReady ? ReInput.controllers.GetLastActiveController<Joystick>() : null;
            if (joy == null) return;
            // Touche access (Triangle) tenue : la croix directionnelle est reservee aux commandes.
            if (InfoKey.ModifierHeld) return;
            for (int i = 0; i < joy.buttonCount; i++)
            {
                if (!joy.GetButtonDown(i)) continue;
                int id = joy.ButtonElementIdentifiers[i].id;
                if (id == DpadUp) { Current.Move(-1); return; }
                if (id == DpadDown) { Current.Move(+1); return; }
                // Gauche/droite : seul le journal les consomme (cascade) ; ailleurs, D-pad natif.
                if ((id == DpadRight || id == DpadLeft) && Current.HandleDpadLR(id)) return;
                if (id == CrossButton) { Current.Confirm(); return; }
                if (id == Lb) { SwitchSection(-1); return; }
                if (id == Rb) { SwitchSection(+1); return; }
            }
        }

        // Touche access (Triangle + haut) : details / relecture du focus de la section courante.
        internal static void AnnounceDetail()
        {
            if (!_active) return;
            Current.Detail();
        }

        // Biome courant a la position du joueur (utilise par VitalsReadout). Reste ici en facade
        // pour ne pas changer l'appelant ; la resolution vit dans MapMarkerUtil.
        internal static string CurrentBiomeName(PlayerController player)
        {
            var w = player.WorldPosition;
            return MapMarkerUtil.ResolveBiome(new float2(w.x, w.z));
        }
    }
}
