using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using Unity.Mathematics;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Ping de reperage d'un AUTRE JOUEUR en multi (design utilisateur, 5 aout 2026 - suite du
    // retour testeur "on ne se voit pas", cf. la categorie Joueurs du scanner).
    //
    // GESTE : R3 tenu (le modificateur du scanner) + stick GAUCHE = roue a 8 positions.
    //  - Secteur NORD = "desactive" (eteint le ping), puis un secteur par joueur connecte.
    //    Le jeu plafonne a 8 joueurs (ListConnectedPlayers.maxPlayerCount) -> 7 autres +
    //    "desactive" tiennent PILE dans la roue, aucune pagination necessaire.
    //  - Selection IMMEDIATE au survol (runOnHover) : entrer dans un secteur arme le ping,
    //    entrer dans "desactive" l'eteint. Pas de bouton de validation (design utilisateur).
    //  - UN SEUL joueur suivi a la fois. Le ping PERSISTE apres relachement de R3, jusqu'au
    //    retour sur "desactive" (ou la deconnexion de la cible).
    //  - Le stick gauche etant confisque par la roue, la marche est GELEE tant que R3 est
    //    tenu (AccessKeyMovementLockSystem, meme pont que la touche access et la roue de
    //    saut barre rapide).
    //
    // MAPPING AUDIO - conventions du beacon de navigation reprises telles quelles
    // (cf. BeaconGuide, demande explicite de l'utilisateur : "trois pitch suffisent") :
    //  - pan     = est/ouest, CONTINU plein (d.x / |d|, sans cran ni plafond).
    //  - pitch   = nord/sud, 3 crans francs (aigu / medium / grave, +-2,5 demi-tons),
    //              medium = meme ligne a +-AlignTiles.
    //  - cadence = DISTANCE (comme le mode DIRECT de BeaconGuide), mais normalisee a
    //              l'ECRAN et non a un rayon fixe en cases : t = ecart relatif au bord de
    //              la fenetre camera (0 = colle a nous, 1 = au bord de l'ecran).
    //  - volume  = FIXE tant que la cible est A L'ECRAN. Au-dela (t > 1), la cadence est
    //              deja saturee -> le volume prend le relais et decroit logarithmiquement
    //              jusqu'a un plancher. C'est le SEUL earcon du mod qui porte de l'info
    //              HORS ECRAN (demande utilisateur) : partout ailleurs on se limite a ce
    //              qu'un voyant voit. La parite est etablie ici - la carte du jeu marque
    //              TOUS les joueurs sans limite de distance (cf. fiche multiplayer), donc
    //              c'est de la compensation, pas un assist.
    //
    // Voie audio DEDIEE (GameplayAudio.PlayPing, Stop puis Play a chaque ping : la cadence
    // gouverne, la traine ne deborde jamais). SEPAREE de celle du beacon de guidage -> un
    // itineraire en cours et le ping joueur cohabitent sans s'ecraser.
    //
    // 100 % client, lecture seule : Manager.main.allPlayers (positions + pseudo), le meme
    // canal que les marqueurs joueur de la carte du jeu.
    internal static class PlayerPing
    {
        // Cadence : constantes du beacon (CadenceNear/CadenceFar), pour que les deux earcons
        // repetes du mod parlent la meme langue.
        private const float CadenceNear = 0.5f;   // colle a la cible
        private const float CadenceFar = 1f;      // au bord de l'ecran (et au-dela : sature)
        private const float AlignTiles = 1f;      // seuil serre du medium (meme ligne est/ouest)
        private const float PitchHigh = 1.1554f;  // nord (+2,5 demi-tons)
        private const float PitchMid = 1f;        // meme ligne
        private const float PitchLow = 0.8655f;   // sud (-2,5 demi-tons)
        private const float BaseVolume = 0.5f;    // x reglage panneau x volume maitre
        private const float ReachedTiles = 2f;    // on est dessus -> PAUSE (reprise si on s'eloigne)
        private const float OffScreenFloor = 0.25f; // plancher de volume hors ecran
        private const float OffScreenSpan = 8f;   // ecrans d'eloignement pour atteindre le plancher

        // Vrai tant que le stick gauche pilote la roue (donc que la marche doit rester gelee) :
        // lu par AccessKeyMovementLockSystem.
        public static bool WheelActive;

        private static CommandWheel _wheel = Empty();
        private static bool _wasHeld;
        private static bool _armed;   // le stick est-il repasse au neutre depuis l'ouverture ?

        private static PlayerController _target;   // joueur suivi (null = ping eteint)
        private static string _targetName = "";
        private static float _lastPing;
        private static bool _reachedAnnounced;     // "rejoint X" ne se dit qu'une fois par arrivee

        private static CommandWheel Empty() => new CommandWheel(0, 0, -1, runOnHover: true, tickSound: true);

        public static void Tick(PlayerController player)
        {
            bool eligible = player != null && InputContext.InGameFree;
            WheelActive = eligible && ScannerModifier.Held;

            if (!eligible)
            {
                _wheel.Tick(Vector2.zero, false); // repos : reset du secteur courant
                _wasHeld = false;
                return;
            }

            // --- Roue (R3 tenu + stick gauche) ---
            if (ScannerModifier.Held)
            {
                // Reconstruite a CHAQUE ouverture : la liste des connectes bouge.
                if (!_wasHeld) { BuildWheel(player); _armed = false; }
                var input = player.inputModule;
                Vector2 aim = input != null ? input.GetRawAxisInput(false, true, false) : Vector2.zero;

                // Garde-fou : si R3 est enfonce alors qu'on MARCHAIT deja (stick pousse), le
                // secteur sous le stick serait selectionne dans la seconde, sans intention.
                // On attend donc que le stick soit repasse au neutre une fois avant d'armer
                // la roue (Deadzone du CommandWheel = 0,5).
                if (!_armed)
                {
                    if (aim.sqrMagnitude < 0.25f) _armed = true;
                    else aim = Vector2.zero;
                }
                _wheel.Tick(aim, false);
            }
            else if (_wasHeld) _wheel.Tick(Vector2.zero, false);
            _wasHeld = ScannerModifier.Held;

            // --- Ping continu (independant de la tenue de R3) ---
            TickPing(player);
        }

        // Remplissage par ORDRE DE PRIORITE (Add) : 1 nord, 2 est, 3 sud, 4 ouest, puis les
        // diagonales - la convention de placement du moteur de roues. Nord = "desactive"
        // (la position la plus facile a viser sans retour visuel, et celle qu'on cherche en
        // urgence pour couper le son).
        private static void BuildWheel(PlayerController me)
        {
            var w = Empty();
            w.Add("", Disable);

            var all = Manager.main != null ? Manager.main.allPlayers : null;
            if (all != null)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    var pc = all[i];
                    if (pc == null || pc == me) continue;
                    var pick = pc; // capture locale (boucle)
                    w.Add("", () => Select(pick));
                }
            }
            _wheel = w;
        }

        private static void Select(PlayerController pc)
        {
            _target = pc;
            _targetName = NameOf(pc);
            _reachedAnnounced = false;
            _lastPing = -999f; // premier ping immediat, sans attendre la cadence
            TtsText.Say(_targetName, true);
        }

        private static void Disable()
        {
            _target = null;
            _targetName = "";
            TtsText.Say(Strings.L("ping.off"), true);
        }

        private static void TickPing(PlayerController me)
        {
            if (_target == null) return;

            // Toujours connecte ? (allPlayers est LA liste que suit aussi la carte du jeu)
            var all = Manager.main != null ? Manager.main.allPlayers : null;
            if (all == null || !all.Contains(_target))
            {
                TtsText.Say(Strings.L("ping.lost") + " " + _targetName, true);
                _target = null;
                _targetName = "";
                return;
            }

            float2 mine = new float2(me.WorldPosition.x, me.WorldPosition.z);
            float2 pos = new float2(_target.WorldPosition.x, _target.WorldPosition.z);
            float2 d = pos - mine;
            float dist = math.length(d);

            // Arrivee : PAUSE (le ping n'apprend plus rien quand on est dessus), REPRISE
            // automatique des qu'on s'eloigne. Meme design que le beacon du scanner.
            if (dist <= ReachedTiles)
            {
                if (!_reachedAnnounced)
                {
                    TtsText.Say(Strings.L("ping.reached") + " " + _targetName, true);
                    _reachedAnnounced = true;
                }
                return;
            }
            _reachedAnnounced = false;

            // Fenetre camera reelle, publiee chaque frame par ProximityScanner (qui tourne
            // juste avant nous). Repli sur une fenetre plausible si elle n'est pas encore la.
            float2 half = VisibilityScan.CamHalf;
            if (half.x < 1f || half.y < 1f) half = new float2(12f, 8f);
            float t = math.max(math.abs(d.x) / half.x, math.abs(d.y) / half.y);

            float interval = Mathf.Lerp(CadenceNear, CadenceFar, Mathf.Clamp01(t));
            if (Time.unscaledTime - _lastPing < interval) return;
            _lastPing = Time.unscaledTime;

            float pan = dist > 1e-3f ? Mathf.Clamp(d.x / dist, -1f, 1f) : 0f;
            float pitch = d.y > AlignTiles ? PitchHigh : (d.y < -AlignTiles ? PitchLow : PitchMid);
            float volume = BaseVolume * A11ySettings.PlayerPingVolume * OffScreenFalloff(t);
            GameplayAudio.PlayPing(ProximityScanner.PlayerSfx, pan, pitch, volume);
        }

        // Hors ecran (t > 1) la cadence est deja saturee : le volume prend le relais et
        // decroit LOGARITHMIQUEMENT - chaque ecran d'eloignement supplementaire retire la
        // meme proportion, donc l'echelle reste lisible meme tres loin (au lieu de s'ecraser
        // au plancher au bout de deux ecrans). Plancher OffScreenFloor a OffScreenSpan ecrans.
        private static float OffScreenFalloff(float t)
        {
            if (t <= 1f) return 1f;
            float k = Mathf.Clamp01(Mathf.Log(t, 2f) / Mathf.Log(OffScreenSpan, 2f));
            return Mathf.Lerp(1f, OffScreenFloor, k);
        }

        private static string NameOf(PlayerController pc)
        {
            if (pc != null && !string.IsNullOrEmpty(pc.playerName)) return pc.playerName;
            return Strings.L("scanner.cat.player");
        }

        // Apercu sonore (panneau de reglages) : un ping au centre, hauteur neutre, au volume
        // actuellement regle -> se calibrer a l'oreille sans cible active.
        public static void Preview() =>
            GameplayAudio.PlayPing(ProximityScanner.PlayerSfx, 0f, PitchMid, BaseVolume * A11ySettings.PlayerPingVolume);
    }
}
