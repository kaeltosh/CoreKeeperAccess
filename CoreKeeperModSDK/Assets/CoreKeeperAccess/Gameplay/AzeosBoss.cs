using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using Unity.Mathematics;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Combat d'Azeos the Sky Titan (BirdBoss) : lit AzeosScanSystem (piliers classes par
    // canal, cristaux, etat du boss) et declenche les earcons. Design complet -> memoire
    // "Azeos the Sky Titan (BirdBoss) - reference a11y".
    internal static class AzeosBoss
    {
        // Juges encore trop faibles a l'oreille (2e test en jeu) : montes a fond de marge.
        // Sons foudre normalises a crete -6 dBFS (0.5) -> x2.0 atteint pile 1.0 plein-echelle,
        // sans saturation (normalisation exacte, pas un plafond approximatif). Balise cristal
        // = sinus GENERE plein-echelle -> reste a 0.28/0.45.
        private const float ColonneVolume = 2.0f;
        private const float LigneVolume = 2.0f;
        private const float CristalGuideVolume = 0.28f;
        private const float CristalUrgentVolume = 0.45f;
        // Pan/pitch EXAGERES en 3 positions par axe (gauche/centre/droite, haut/centre/bas)
        // plutot qu'un degrade continu - plus facile a lire pour se caler dessus. Sous le
        // seuil sur un axe -> "centre" sur cet axe.
        private const float CristalAlignThreshold = 1.5f; // cases
        private const float CristalPitchLow = 0.5f;
        private const float CristalPitchHigh = 2f;

        private static bool _everSeenBoss;
        private static bool _wasAppeared;
        private static bool _wasEnraged;
        private static bool _cristalAnnounced;

        public static void Tick()
        {
            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null || !InputContext.InGameFree || InputContext.MenuOpen)
            {
                Reset();
                return;
            }

            float2 me = new float2(player.WorldPosition.x, player.WorldPosition.z);

            TickBossState();
            TickRangees(me);
            TickCristal(me);
        }

        private static void Reset()
        {
            GameplayAudio.SetAzeosColonne(false, 0f, 0f);
            GameplayAudio.SetAzeosLigneHaut(false, 0f);
            GameplayAudio.SetAzeosLigneBas(false, 0f);
            GameplayAudio.SetCristalGuide(false, 0f, 1f, 0f, false);
            _everSeenBoss = false;
            _cristalAnnounced = false;
        }

        // Atterrissage / teleportation / enrage : lus directement sur les composants natifs
        // (BirdBossHasAppearedCD, EnrageStateCD), tous deux confirmes repliques au client -
        // pas de contournement necessaire ici, contrairement aux piliers.
        private static void TickBossState()
        {
            if (!AzeosBossScan.Found) { _everSeenBoss = false; return; }

            bool appeared = AzeosBossScan.HasAppeared;
            bool enraged = AzeosBossScan.Enraged;

            if (!_everSeenBoss)
            {
                _everSeenBoss = true;
                _wasAppeared = appeared;
                _wasEnraged = enraged;
                return; // pas d'annonce a la toute premiere lecture (evite un faux "atterrit" en arrivant dans l'arene)
            }

            if (appeared && !_wasAppeared) TtsText.Say(Strings.L("boss.azeos.land"), true);
            else if (!appeared && _wasAppeared) TtsText.Say(Strings.L("boss.azeos.teleport"), true);
            _wasAppeared = appeared;

            if (enraged && !_wasEnraged) TtsText.Say(Strings.L("boss.azeos.enrage"), true);
            _wasEnraged = enraged;
        }

        // Colonne (rangee V, pan est-ouest) + lignes du dessus/dessous (rangee H, volume par
        // distance) : chacune suit le pilier le plus proche de SON canal, aucun besoin
        // d'attendre un mouvement mesurable - le canal est deja connu depuis le spawn.
        private static void TickRangees(float2 me)
        {
            if (!AzeosBeamScan.ResultValid || AzeosBeamScan.Count == 0)
            {
                GameplayAudio.SetAzeosColonne(false, 0f, 0f);
                GameplayAudio.SetAzeosLigneHaut(false, 0f);
                GameplayAudio.SetAzeosLigneBas(false, 0f);
                return;
            }

            bool haveCol = false; float2 colPos = default; float colBest = float.MaxValue;
            bool haveHaut = false; float hautBest = float.MaxValue;
            bool haveBas = false; float basBest = float.MaxValue;

            for (int i = 0; i < AzeosBeamScan.Count; i++)
            {
                var b = AzeosBeamScan.Beams[i];
                float d2 = math.distancesq(b.Pos, me);
                switch (b.Channel)
                {
                    case AzeosBeamChannel.ColonneV:
                        if (d2 < colBest) { colBest = d2; colPos = b.Pos; haveCol = true; }
                        break;
                    case AzeosBeamChannel.LigneHaut:
                        if (d2 < hautBest) { hautBest = d2; haveHaut = true; }
                        break;
                    case AzeosBeamChannel.LigneBas:
                        if (d2 < basBest) { basBest = d2; haveBas = true; }
                        break;
                }
            }

            if (haveCol)
            {
                float2 d = colPos - me;
                float pan = GameplayAudio.PanFromTiles(d.x);
                float vol = GameplayAudio.DistanceTrim(math.length(d)) * ColonneVolume;
                GameplayAudio.SetAzeosColonne(true, pan, vol);
            }
            else GameplayAudio.SetAzeosColonne(false, 0f, 0f);

            if (haveHaut)
                GameplayAudio.SetAzeosLigneHaut(true, GameplayAudio.DistanceTrim(Mathf.Sqrt(hautBest)) * LigneVolume);
            else GameplayAudio.SetAzeosLigneHaut(false, 0f);

            if (haveBas)
                GameplayAudio.SetAzeosLigneBas(true, GameplayAudio.DistanceTrim(Mathf.Sqrt(basBest)) * LigneVolume);
            else GameplayAudio.SetAzeosLigneBas(false, 0f);
        }

        // Balise de guidage cristal : demarre DES LE SPAWN (destruction preventive avant
        // activation = strategie ideale), s'intensifie si un cristal ACTIF (soin en cours,
        // urgent) est present - celui-la prime alors sur un cristal juste spawne inactif.
        // Echelle de pan/pitch RESSERREE (CristalPanRange) pour une precision fine a
        // l'approche jusqu'au contact de minage, distincte de l'echelle large du curseur.
        private static void TickCristal(float2 me)
        {
            if (!AzeosStoneScan.ResultValid || AzeosStoneScan.Count == 0)
            {
                GameplayAudio.SetCristalGuide(false, 0f, 1f, 0f, false);
                _cristalAnnounced = false;
                return;
            }

            bool haveActive = false; float2 activePos = default; float activeBest = float.MaxValue;
            bool haveAny = false; float2 anyPos = default; float anyBest = float.MaxValue;

            for (int i = 0; i < AzeosStoneScan.Count; i++)
            {
                var s = AzeosStoneScan.Stones[i];
                float d2 = math.distancesq(s.Pos, me);
                if (s.Active && d2 < activeBest) { activeBest = d2; activePos = s.Pos; haveActive = true; }
                if (d2 < anyBest) { anyBest = d2; anyPos = s.Pos; haveAny = true; }
            }

            if (!haveAny) { GameplayAudio.SetCristalGuide(false, 0f, 1f, 0f, false); _cristalAnnounced = false; return; }

            bool urgent = haveActive;
            float2 target = urgent ? activePos : anyPos;
            float2 d = target - me;
            float dist = math.length(d);

            if (dist < 1.5f && !_cristalAnnounced)
            {
                _cristalAnnounced = true;
                TtsText.Say(Strings.L(urgent ? "boss.azeos.crystal_contact_urgent" : "boss.azeos.crystal_contact"), true);
            }
            if (dist >= 3f) _cristalAnnounced = false;

            float pan = d.x < -CristalAlignThreshold ? -1f : d.x > CristalAlignThreshold ? 1f : 0f;
            float pitch = d.y < -CristalAlignThreshold ? CristalPitchLow : d.y > CristalAlignThreshold ? CristalPitchHigh : 1f;
            float vol = urgent ? CristalUrgentVolume : CristalGuideVolume;
            GameplayAudio.SetCristalGuide(true, pan, pitch, vol, urgent);
        }
    }
}
