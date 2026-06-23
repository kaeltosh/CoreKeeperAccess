using System;
using CoreKeeperAccess.Gameplay;
using CoreKeeperAccess.Settings;
using UnityEngine;

namespace CoreKeeperAccess.Controls
{
    // Menu d'APPRENTISSAGE des sons du mod, entierement TTS. Lance depuis le menu d'aide
    // (entree "Apprendre les sons", sous "Apprendre la manette"). On parcourt un catalogue
    // d'earcons range par domaine ; au SURVOL d'une entree, son libelle et son explication
    // sont lus, PUIS le son lui-meme est joue (mode auto-apercu du TreeMenu) - on apprend la
    // grammaire sonore au calme, sans avoir a rencontrer chaque situation en jeu.
    //
    // CLIENT du moteur generique TreeMenu (comme SettingsMenu / ActionMenu) : on declare
    // l'arbre (EnsureBuilt) et on gere le specifique = la nappe de sonar, son CONTINUE qu'on
    // arme pour une courte fenetre puis qu'on coupe (meme mecanique que le panneau).
    internal static class SoundGuide
    {
        // autoPreview: true -> le son de l'entree survolee joue tout seul (le cur du menu).
        private static readonly TreeMenu _menu = new TreeMenu(
            closedKey: "sound.closed",
            onTick: SonarPreviewTick,
            onClose: StopSonarPreview,
            autoPreview: true,
            silentNav: true);

        public static bool Active => _menu.Active;

        private static TreeMenu.Category _root;

        // --- Helpers de declaration ---
        private static TreeMenu.Command Leaf(string key, string desc, Action preview)
            => new TreeMenu.Command { LabelKey = key, DescKey = desc, Preview = preview };

        private static TreeMenu.Category Cat(string key, params TreeMenu.Node[] children)
        {
            var c = new TreeMenu.Category { LabelKey = key };
            c.Children.AddRange(children);
            return c;
        }

        // Apercu PONCTUEL : coupe d'abord une eventuelle nappe de sonar restee active d'un
        // survol precedent (sinon elle deborderait sur le son joue ici), puis joue le son.
        private static Action One(Action sound) => () => { StopSonarPreview(); sound(); };

        // --- Construction de l'arbre (un domaine = une categorie) ---
        private static void EnsureBuilt()
        {
            if (_root != null) return;
            _root = new TreeMenu.Category { LabelKey = "sound.title" };

            // 1) Exploration : tout ce qui sert a se REPERER et se DEPLACER (curseur de tuile,
            // sonar, pas, guidage) + les sons NON hostiles du laser (creature / objet au sol).
            // Pas de "laser impact" : il rejoue deja les sons du curseur (mur/eau/gouffre) -> doublon.
            _root.Children.Add(Cat("sound.cat.explore",
                Leaf("sound.cursortick", "sound.desc.cursortick", One(BuildModeNavigator.PreviewCursorTick)),
                Leaf("sound.wall", "sound.desc.wall", One(BuildModeNavigator.PreviewWall)),
                Leaf("sound.water", "sound.desc.water", One(BuildModeNavigator.PreviewWater)),
                Leaf("sound.pit", "sound.desc.pit", One(BuildModeNavigator.PreviewPit)),
                Leaf("sound.interactable", "sound.desc.interactable", One(BuildModeNavigator.PreviewInteractable)),
                Leaf("sound.ore", "sound.desc.ore", One(BuildModeNavigator.PreviewOre)),
                Leaf("sound.sonarfront", "sound.desc.sonarfront", () => StartSonar(0)),
                Leaf("sound.sonarback", "sound.desc.sonarback", () => StartSonar(1)),
                Leaf("sound.sonarpit", "sound.desc.sonarpit", () => StartSonar(2)),
                Leaf("sound.stepbeep", "sound.desc.stepbeep",
                    One(() => GameplayAudio.PlayTone(0f, 1f, A11ySettings.DirectionTickVolume))),
                Leaf("sound.beaconguide", "sound.desc.beaconguide", One(BeaconGuide.Preview)),
                Leaf("sound.lasercreature", "sound.desc.lasercreature", One(LaserCane.PreviewPassive)),
                Leaf("sound.laserobject", "sound.desc.laserobject", One(LaserCane.PreviewObject))));

            // 2) Combat : menaces et alertes de survie. Le laser ENNEMI atterrit ici (signal
            // de combat), separe du reste de la canne (repere dans Exploration).
            _root.Children.Add(Cat("sound.cat.combat",
                Leaf("sound.sentinelmob", "sound.desc.sentinelmob", One(AggroSentinel.PreviewMob)),
                Leaf("sound.sentinelboss", "sound.desc.sentinelboss", One(AggroSentinel.PreviewBoss)),
                Leaf("sound.laserenemy", "sound.desc.laserenemy", One(LaserCane.Preview)),
                Leaf("sound.fire", "sound.desc.fire", One(FireProximity.Preview)),
                Leaf("sound.health60", "sound.desc.health60",
                    One(() => GameplayAudio.PlayHealthWarn(A11ySettings.HealthAlertsVolume))),
                Leaf("sound.health20", "sound.desc.health20",
                    One(() => GameplayAudio.PlayHealthAlert(A11ySettings.HealthAlertsVolume))),
                Leaf("sound.healthbeat", "sound.desc.healthbeat",
                    One(() => GameplayAudio.PlayHealthCritical(A11ySettings.HealthBeatVolume))),
                Leaf("sound.dot", "sound.desc.dot",
                    One(() => GameplayAudio.PlayConditionEarcon(false, A11ySettings.ConditionEarconsVolume))),
                Leaf("sound.stun", "sound.desc.stun",
                    One(() => GameplayAudio.PlayConditionEarcon(true, A11ySettings.ConditionEarconsVolume)))));
        }

        // --- Ouverture / boucle (delegues au moteur) ---
        // Lance depuis le menu d'aide (HelpCatalog) : l'ActionMenu s'est ferme avant de nous
        // appeler, on ouvre donc notre propre instance dans la foulee.
        public static void Start()
        {
            EnsureBuilt();
            _menu.Open(_root);
        }

        public static void Tick() => _menu.Tick();

        // --- Apercu sonore du SONAR (nappe CONTINUE, comme le panneau de reglages) ---
        // which : 0 = mur medium (avant/cotes), 1 = mur grave (arriere), 2 = gouffre (clapotis).
        // On coupe d'abord la nappe en cours puis on arme la nouvelle pour une courte fenetre,
        // refermee au Tick (SonarPreviewTick) ou a la fermeture du menu (onClose).
        private const float SonarPreviewDur = 0.9f;
        private static float _sonarStopAt;

        private static void StartSonar(int which)
        {
            ProximitySonar.StopPreview();
            if (which == 2) ProximitySonar.StartPreviewPit();
            else ProximitySonar.StartPreview(which);
            _sonarStopAt = Time.unscaledTime + SonarPreviewDur;
        }

        private static void StopSonarPreview()
        {
            if (_sonarStopAt <= 0f) return;
            ProximitySonar.StopPreview();
            _sonarStopAt = 0f;
        }

        private static void SonarPreviewTick()
        {
            if (_sonarStopAt > 0f && Time.unscaledTime >= _sonarStopAt) StopSonarPreview();
        }
    }
}
