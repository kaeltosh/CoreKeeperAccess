using System;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using Rewired;
using UnityEngine;

namespace CoreKeeperAccess.Controls
{
    // Roue de commandes generique (moteur de keymaps) : 8 positions au stick, survol
    // d'un secteur = clic de cran + label TTS, bouton de validation = execution +
    // confirmation "lancé". L'appelant decide QUAND la roue est active (contexte) et
    // avec QUEL stick / bouton (ids physiques passes au constructeur).
    //
    // Indexation des positions (DECISION UTILISATEUR, 12 juin 2026) : une roue se
    // remplit par ORDRE DE PRIORITE via Add(), le moteur place automatiquement :
    // 1 nord, 2 est, 3 sud, 4 ouest, 5 nord-est, 6 sud-est, 7 sud-ouest, 8 nord-ouest
    // (cardinaux d'abord - les plus faciles a viser au stick sans retour visuel -,
    // puis diagonales, le tout en sens horaire). AddAtSector reste disponible pour
    // les dispositions DEJA APPRISES (roue 1 inventaire : ne pas bouger les reflexes).
    internal sealed class CommandWheel
    {
        private const float Deadzone = 0.5f;

        private struct Entry
        {
            public string LabelKey;
            public Action Run;
        }

        // Index utilisateur (0-base) -> secteur physique (0=N, 1=NE, ... 7=NO, horaire).
        private static readonly int[] IndexToSector = { 0, 2, 4, 6, 1, 3, 5, 7 };

        private readonly int _axisXId, _axisYId, _confirmId;
        private readonly bool _runOnHover;
        private readonly bool _tickSound;
        private readonly Entry?[] _bySector = new Entry?[8];
        private int _autoIndex;
        private int _lastSector = -1;

        // runOnHover=false (defaut) : survol = annonce du LABEL, le bouton de validation
        // EXECUTE (roue d'actions). runOnHover=true : survol = EXECUTION directe, pas de
        // bouton (roue de lecture, ou Run() annonce lui-meme la valeur ; confirmButtonId
        // ignore, passer -1).
        // tickSound (defaut true) : clic de cran a chaque changement de secteur. Le couper
        // (roue de LECTURE qui annonce deja la valeur en TTS au survol, ex. roue de stats) :
        // le clic y serait redondant et indesirable.
        public CommandWheel(int axisXId, int axisYId, int confirmButtonId, bool runOnHover = false, bool tickSound = true)
        {
            _axisXId = axisXId;
            _axisYId = axisYId;
            _confirmId = confirmButtonId;
            _runOnHover = runOnHover;
            _tickSound = tickSound;
        }

        // Remplissage par priorite : le 1er Add prend le nord, le 2e l'est, etc.
        public void Add(string labelKey, Action run)
        {
            while (_autoIndex < IndexToSector.Length && _bySector[IndexToSector[_autoIndex]].HasValue)
                _autoIndex++; // saute les positions deja prises par AddAtSector
            if (_autoIndex >= IndexToSector.Length)
            {
                Debug.LogError("[A11yWheelDiag] " + Diag.Stamp() + " roue pleine, commande '" + labelKey + "' ignoree");
                return;
            }
            AddAtSector(IndexToSector[_autoIndex++], labelKey, run);
        }

        // Placement explicite (0=N ... 7=NO horaire) : dispositions deja apprises.
        public void AddAtSector(int sector, string labelKey, Action run)
        {
            _bySector[sector] = new Entry { LabelKey = labelKey, Run = run };
        }

        // A appeler chaque frame ou la roue est active (contexte gere par l'appelant).
        // Lit le stick et le bouton sur le Joystick physique (ids passes au constructeur).
        public void Tick(Joystick joy)
        {
            if (joy == null) { _lastSector = -1; return; }
            bool confirm = _confirmId >= 0 && ButtonDownById(joy, _confirmId);
            Process(AxisById(joy, _axisXId), AxisById(joy, _axisYId), confirm);
        }

        // Surcharge : axes fournis directement (x=est/droite, y=nord/haut), pour les roues
        // qui lisent leur stick via l'API du jeu (mapping monde) plutot que les ids
        // physiques du Joystick. Stick au neutre (Vector2.zero) => repos + reset du secteur.
        public void Tick(Vector2 stick, bool confirmDown)
        {
            Process(stick.x, stick.y, confirmDown);
        }

        private void Process(float x, float y, bool confirmDown)
        {
            int sector = SectorOf(x, y);
            if (sector != _lastSector)
            {
                _lastSector = sector;
                if (sector >= 0)
                {
                    // Clic de cran facon "roue a boutons" a chaque changement de secteur, via la
                    // grammaire de nav PARTAGEE (UiSfx.Entry) : NORMALISE et pilote par le volume
                    // general, comme tous les menus maison. (Avant : AudioManager.SfxUI direct,
                    // hors master.)
                    if (_tickSound) UiSfx.Entry();
                    var e = _bySector[sector];
                    if (e.HasValue)
                    {
                        // Roue de lecture : Run() annonce directement la valeur au survol.
                        // Roue d'actions : on ne dit que le label, l'execution attend le bouton.
                        if (_runOnHover) e.Value.Run();
                        else TtsText.Say(Strings.L(e.Value.LabelKey), true);
                    }
                }
            }

            if (!_runOnHover && sector >= 0 && confirmDown && _bySector[sector].HasValue)
            {
                var e = _bySector[sector].Value;
                e.Run();
                // Confirmation au clic : "Trier, lancé". Distinct du simple survol qui ne
                // dit que "Trier". Donne un retour meme quand l'action n'a pas d'effet sonore.
                TtsText.Say(Strings.L(e.LabelKey) + ", " + Strings.L("wheel.done"), true);
            }
        }

        // Secteur 0=N,1=NE,2=E,3=SE,4=S,5=SO,6=O,7=NO ; -1 = centre (deadzone).
        private static int SectorOf(float x, float y)
        {
            if (x * x + y * y < Deadzone * Deadzone) return -1;
            float ang = Mathf.Atan2(x, y) * Mathf.Rad2Deg; // 0 = haut (Nord), 90 = droite (Est)
            if (ang < 0f) ang += 360f;
            return ((int)Mathf.Round(ang / 45f)) % 8;
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
}
