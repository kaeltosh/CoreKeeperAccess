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
        private readonly Entry?[] _bySector = new Entry?[8];
        private int _autoIndex;
        private int _lastSector = -1;

        public CommandWheel(int axisXId, int axisYId, int confirmButtonId)
        {
            _axisXId = axisXId;
            _axisYId = axisYId;
            _confirmId = confirmButtonId;
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
        public void Tick(Joystick joy)
        {
            if (joy == null) { _lastSector = -1; return; }

            int sector = SectorOf(AxisById(joy, _axisXId), AxisById(joy, _axisYId));
            if (sector != _lastSector)
            {
                _lastSector = sector;
                if (sector >= 0)
                {
                    // Clic de cran facon "roue a boutons" a chaque changement de secteur.
                    // FIXME_menu_select = le son de navigation de menu du jeu (choix utilisateur).
                    // pitchDev=0 : on annule le random pitch que SfxUI applique par defaut (0.15),
                    // sinon chaque cran sonne a une hauteur differente. Args : pitch, reuse, volume, pitchDev.
                    AudioManager.SfxUI(SfxID.FIXME_menu_select, 1f, true, 1f, 0f);
                    var e = _bySector[sector];
                    if (e.HasValue) TtsText.Say(Strings.L(e.Value.LabelKey), true);
                }
            }

            if (sector >= 0 && ButtonDownById(joy, _confirmId) && _bySector[sector].HasValue)
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
