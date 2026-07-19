using System;
using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Gameplay;
using UnityEngine;

namespace CoreKeeperAccess.Settings
{
    // Panneau de reglages MAISON, entierement TTS (aucun asset Unity, aucune greffe sur le
    // menu options natif - verifie non injectable). Ouvre via Triangle + Back.
    //
    // Depuis le 21 juin 2026, ce n'est plus qu'un CLIENT du moteur generique TreeMenu
    // (Controls/TreeMenu.cs) : SettingsMenu ne fait que DECLARER son arbre (EnsureBuilt) et
    // gerer le specifique panneau (apercu sonar a fenetre temporisee). Toute la mecanique de
    // navigation / TTS / earcons vit dans TreeMenu, partagee avec ActionMenu et, a venir, le
    // lecteur de carte et le codex.
    //
    // Les reglages vivent dans A11ySettings (persistance immediate a chaque changement). Les
    // earcons (Preview) jouent au volume regle (Triangle + D-pad haut) ; les options de pur
    // comportement (snap, ralenti...) ont Preview null = pas de son.
    internal static class SettingsMenu
    {
        // Instance du moteur, configuree pour le panneau : annonce "ferme" a la fermeture,
        // timer d'apercu sonar tickee en tete de boucle, coupure de l'apercu a la fermeture.
        private static readonly TreeMenu _menu = new TreeMenu(
            closedKey: "settings.closed",
            onTick: SonarPreviewTick,
            onClose: StopPreviews);

        public static bool Active => _menu.Active;

        private static TreeMenu.Category _root;

        // --- Helpers de declaration (gardent EnsureBuilt lisible) ---
        // Chaque entree porte sa cle de libelle, sa cle de descriptif (desc) lue en queue, et
        // un apercu sonore optionnel (preview) joue sur Triangle + D-pad haut.
        private static TreeMenu.Toggle Tg(string key, string desc, Func<bool> get, Action<bool> set, Action preview = null)
            => new TreeMenu.Toggle { LabelKey = key, DescKey = desc, Get = get, Set = set, Preview = preview };

        // Slider de volume : toujours pas de 5 %, 0..200 % (amplification). Cas par defaut.
        private static TreeMenu.Slider Vol(string key, string desc, Func<float> get, Action<float> set, Action preview = null)
            => new TreeMenu.Slider { LabelKey = key, DescKey = desc, Get = get, Set = set, Step = 0.05f, Max = 2f, Preview = preview };

        private static TreeMenu.Category Cat(string key, string desc, params TreeMenu.Node[] children)
        {
            var c = new TreeMenu.Category { LabelKey = key, DescKey = desc };
            c.Children.AddRange(children);
            return c;
        }

        // --- Construction de l'arbre (categories par domaine) ---
        // Volume general et Normalisation restent a la racine (transverses, acces direct) ;
        // le reste se range par domaine. Ajouter un reglage = 1 ligne dans la bonne categorie.
        private static void EnsureBuilt()
        {
            if (_root != null) return;
            _root = new TreeMenu.Category { LabelKey = "settings.title" };

            _root.Children.Add(Vol("settings.mastervolume", "settings.desc.mastervolume",
                () => A11ySettings.MasterVolume, A11ySettings.SetMasterVolume,
                () => GameplayAudio.PlayTone(0f, 1f, 0.6f)));

            _root.Children.Add(Tg("settings.hotbarwheel", "settings.desc.hotbarwheel",
                () => A11ySettings.HotbarWheelEnabled, A11ySettings.SetHotbarWheelEnabled));

            _root.Children.Add(new TreeMenu.Slider
            {
                LabelKey = "settings.hotbarwheelhold", DescKey = "settings.desc.hotbarwheelhold",
                Get = () => A11ySettings.HotbarWheelHoldMs, Set = A11ySettings.SetHotbarWheelHoldMs,
                Step = 10f, Min = 0f, Max = 300f, Raw = true, RawUnitKey = "settings.unit.ms",
            });

            _root.Children.Add(Cat("settings.cat.navigation", "settings.desc.cat.navigation",
                Tg("settings.stepbeep", "settings.desc.stepbeep", () => A11ySettings.StepBeep, A11ySettings.SetStepBeep,
                    () => GameplayAudio.PlayTone(0f, 1f, A11ySettings.DirectionTickVolume)),
                Vol("settings.directiontick", "settings.desc.directiontick", () => A11ySettings.DirectionTickVolume, A11ySettings.SetDirectionTickVolume,
                    () => GameplayAudio.PlayTone(0f, 1f, A11ySettings.DirectionTickVolume)),
                Tg("settings.snap", "settings.desc.snap", () => A11ySettings.SnapDirectional, A11ySettings.SetSnapDirectional),
                Vol("settings.navvolume", "settings.desc.navvolume", () => A11ySettings.NavigationVolume, A11ySettings.SetNavigationVolume,
                    () => { GameplayAudio.PlaySpatial(SfxID.inventory_select, 0f, 1f, A11ySettings.NavigationVolume); LaserCane.Preview(); }),
                Vol("settings.guidevolume", "settings.desc.guidevolume", () => A11ySettings.GuideVolume, A11ySettings.SetGuideVolume,
                    BeaconGuide.Preview),
                Tg("settings.muteinteractcursor", "settings.desc.muteinteractcursor", () => A11ySettings.MuteInteractInCursor, A11ySettings.SetMuteInteractInCursor),
                Vol("settings.scannervolume", "settings.desc.scannervolume", () => A11ySettings.ScannerVolume, A11ySettings.SetScannerVolume,
                    () => GameplayAudio.PlaySpatial(SfxID.inventory_doot, 0f, 1f, A11ySettings.ScannerVolume)),
                Tg("settings.darknessgate", "settings.desc.darknessgate", () => A11ySettings.DarknessGate, A11ySettings.SetDarknessGate,
                    () => GameplayAudio.PlayDarknessEarcon(0f, 1f, A11ySettings.NavigationVolume))));

            _root.Children.Add(Cat("settings.cat.sonar", "settings.desc.cat.sonar",
                Tg("settings.sonar", "settings.desc.sonar", () => A11ySettings.ProximitySonar, A11ySettings.SetProximitySonar,
                    () => StartSonarPreview(2)),
                Vol("settings.sonarvolume", "settings.desc.sonarvolume", () => A11ySettings.SonarVolume, A11ySettings.SetSonarVolume,
                    () => StartSonarPreview(2)),
                Vol("settings.sonarmedium", "settings.desc.sonarmedium", () => A11ySettings.SonarVolMedium, A11ySettings.SetSonarVolMedium,
                    () => StartSonarPreview(0)),
                Vol("settings.sonargrave", "settings.desc.sonargrave", () => A11ySettings.SonarVolGrave, A11ySettings.SetSonarVolGrave,
                    () => StartSonarPreview(1)),
                Tg("settings.objectding", "settings.desc.objectding", () => A11ySettings.ObjectDing, A11ySettings.SetObjectDing,
                    ProximitySonar.PreviewDing),
                Tg("settings.collisionradar", "settings.desc.collisionradar", () => A11ySettings.CollisionRadar, A11ySettings.SetCollisionRadar,
                    StartRadarPreview),
                Vol("settings.collisionradarvolume", "settings.desc.collisionradarvolume", () => A11ySettings.CollisionRadarVolume, A11ySettings.SetCollisionRadarVolume,
                    StartRadarPreview),
                new TreeMenu.Slider
                {
                    LabelKey = "settings.collisionradarrange", DescKey = "settings.desc.collisionradarrange",
                    Get = () => A11ySettings.CollisionRadarRange, Set = A11ySettings.SetCollisionRadarRange,
                    Step = 1f, Min = 2f, Max = 6f, Raw = true,
                    Preview = StartRadarPreview,
                }));

            _root.Children.Add(Cat("settings.cat.combat", "settings.desc.cat.combat",
                Tg("settings.slowmo", "settings.desc.slowmo", () => A11ySettings.CombatSlowMo, A11ySettings.SetCombatSlowMo),
                new TreeMenu.Slider
                {
                    LabelKey = "settings.slowmospeed", DescKey = "settings.desc.slowmospeed",
                    Get = () => A11ySettings.SlowMoSpeed, Set = A11ySettings.SetSlowMoSpeed,
                    Step = 0.05f, Min = 0.3f, Max = 0.7f,
                },
                Tg("settings.sentinel", "settings.desc.sentinel", () => A11ySettings.SentinelEnabled, A11ySettings.SetSentinelEnabled,
                    AggroSentinel.PreviewMob),
                Vol("settings.sentinelvolume", "settings.desc.sentinelvolume", () => A11ySettings.SentinelVolume, A11ySettings.SetSentinelVolume,
                    AggroSentinel.PreviewMob),
                Vol("settings.sentinelbossvolume", "settings.desc.sentinelbossvolume", () => A11ySettings.SentinelBossVolume, A11ySettings.SetSentinelBossVolume,
                    AggroSentinel.PreviewBoss),
                Tg("settings.fire", "settings.desc.fire", () => A11ySettings.FireEnabled, A11ySettings.SetFireEnabled,
                    FireProximity.Preview),
                Vol("settings.firevolume", "settings.desc.firevolume", () => A11ySettings.FireVolume, A11ySettings.SetFireVolume,
                    FireProximity.Preview),
                Tg("settings.bosshealth", "settings.desc.bosshealth", () => A11ySettings.BossHealthCallouts, A11ySettings.SetBossHealthCallouts,
                    () => GameplayAudio.PlayBossHealthCallout(50)),
                Vol("settings.bosshealthvolume", "settings.desc.bosshealthvolume", () => A11ySettings.BossHealthVolume, A11ySettings.SetBossHealthVolume,
                    () => GameplayAudio.PlayBossHealthCallout(50))));

            _root.Children.Add(Cat("settings.cat.alerts", "settings.desc.cat.alerts",
                Tg("settings.conditionearcons", "settings.desc.conditionearcons", () => A11ySettings.ConditionEarcons, A11ySettings.SetConditionEarcons,
                    () => GameplayAudio.PlayConditionEarcon(false, A11ySettings.ConditionEarconsVolume)),
                Vol("settings.conditionearconsvolume", "settings.desc.conditionearconsvolume", () => A11ySettings.ConditionEarconsVolume, A11ySettings.SetConditionEarconsVolume,
                    () => GameplayAudio.PlayConditionEarcon(false, A11ySettings.ConditionEarconsVolume)),
                Tg("settings.healthalerts", "settings.desc.healthalerts", () => A11ySettings.HealthAlerts, A11ySettings.SetHealthAlerts,
                    () => GameplayAudio.PlayHealthAlert(A11ySettings.HealthAlertsVolume)),
                Vol("settings.healthalertsvolume", "settings.desc.healthalertsvolume", () => A11ySettings.HealthAlertsVolume, A11ySettings.SetHealthAlertsVolume,
                    () => GameplayAudio.PlayHealthAlert(A11ySettings.HealthAlertsVolume)),
                Vol("settings.healthbeatvolume", "settings.desc.healthbeatvolume", () => A11ySettings.HealthBeatVolume, A11ySettings.SetHealthBeatVolume,
                    () => GameplayAudio.PlayHealthCritical(A11ySettings.HealthBeatVolume)),
                new TreeMenu.Slider
                {
                    LabelKey = "settings.healththresholdalert", DescKey = "settings.desc.healththresholdalert",
                    Get = () => A11ySettings.HealthAlertThreshold, Set = A11ySettings.SetHealthAlertThreshold,
                    Step = 0.05f, Min = 0.4f, Max = 0.9f,
                    // Apercu = le son joue au franchissement du seuil d'alerte (les bips d'avertissement).
                    Preview = () => GameplayAudio.PlayHealthWarn(A11ySettings.HealthAlertsVolume),
                },
                new TreeMenu.Slider
                {
                    LabelKey = "settings.healththresholdcrit", DescKey = "settings.desc.healththresholdcrit",
                    Get = () => A11ySettings.HealthCritThreshold, Set = A11ySettings.SetHealthCritThreshold,
                    Step = 0.05f, Min = 0.1f, Max = 0.35f,
                    // Apercu = le son joue a l'entree en zone critique (la sirene).
                    Preview = () => GameplayAudio.PlayHealthAlert(A11ySettings.HealthAlertsVolume),
                }));

            _root.Children.Add(Cat("settings.cat.pickup", "settings.desc.cat.pickup",
                Tg("settings.pickup", "settings.desc.pickup", () => A11ySettings.PickupAnnounce, A11ySettings.SetPickupAnnounce),
                Tg("settings.pickupblocks", "settings.desc.pickupblocks", () => A11ySettings.PickupFilterBlocks, A11ySettings.SetPickupFilterBlocks),
                Tg("settings.pickuptotal", "settings.desc.pickuptotal", () => A11ySettings.PickupTotal, A11ySettings.SetPickupTotal)));

            _root.Children.Add(Tg("settings.normalize", "settings.desc.normalize",
                () => A11ySettings.NormalizeAudio, A11ySettings.SetNormalizeAudio));

            _root.Children.Add(Tg("settings.xboxbuttons", "settings.desc.xboxbuttons",
                () => A11ySettings.XboxButtons, A11ySettings.SetXboxButtons));
        }

        // --- Ouverture / fermeture / boucle (delegues au moteur) ---
        public static void Open()
        {
            EnsureBuilt();
            _menu.Open(_root);
        }

        public static void Close() => _menu.Close();

        public static void Tick() => _menu.Tick();

        // --- Apercu sonore du SONAR (specifique au panneau) ---
        // Le sonar est une nappe CONTINUE -> on l'arme pour une courte fenetre puis on coupe
        // (au Tick du moteur via SonarPreviewTick, ou a la fermeture via onClose). which : 0
        // medium, 1 grave, 2 les deux. Les autres apercus (tons, dings, earcons) sont des sons
        // ponctuels et passent directement par leur lambda Preview.
        private const float SonarPreviewDur = 0.9f;
        private static float _sonarStopAt;
        private static float _radarStopAt;

        private static void StartSonarPreview(int which)
        {
            ProximitySonar.StartPreview(which);
            _sonarStopAt = Time.unscaledTime + SonarPreviewDur;
        }

        private static void StopSonarPreview()
        {
            if (_sonarStopAt <= 0f) return;
            ProximitySonar.StopPreview();
            _sonarStopAt = 0f;
        }

        // Apercu du DETECTEUR DE COLLISION, meme grammaire fenetree que le sonar (nappe
        // continue -> armee pour une courte duree puis coupee).
        private static void StartRadarPreview()
        {
            CollisionRadar.StartPreview();
            _radarStopAt = Time.unscaledTime + SonarPreviewDur;
        }

        private static void StopRadarPreview()
        {
            if (_radarStopAt <= 0f) return;
            CollisionRadar.StopPreview();
            _radarStopAt = 0f;
        }

        // Coupe les apercus a nappe continue au bout de leur fenetre, meme si aucun bouton.
        // Branche en onTick du moteur -> tickee tant que le panneau est ouvert.
        private static void SonarPreviewTick()
        {
            if (_sonarStopAt > 0f && Time.unscaledTime >= _sonarStopAt) StopSonarPreview();
            if (_radarStopAt > 0f && Time.unscaledTime >= _radarStopAt) StopRadarPreview();
        }

        private static void StopPreviews()
        {
            StopSonarPreview();
            StopRadarPreview();
        }
    }
}
