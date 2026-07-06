using System.Collections.Generic;
using CoreKeeperAccess.Controls;
using CoreKeeperAccess.Localization;
using CoreKeeperAccess.Patches;
using Unity.Mathematics;
using UnityEngine;

namespace CoreKeeperAccess.Gameplay
{
    // Scanner de proximite (R3 tenu) : earcon positionnel a la demande sur les choses VISIBLES
    // A L'ECRAN, navigables par categorie. Design (cf. conversation) :
    //  - R3 tenu + D-pad haut/bas = categorie suivante/precedente (saute les vides, bouclee).
    //  - R3 tenu + D-pad gauche/droite = objet suivant/precedent dans la categorie (bouclee).
    //  - Chaque pas = earcon positionnel (pan est/ouest, hauteur nord/sud, volume distance,
    //    meme grammaire que le reste du mod) + nom TTS de l'entite.
    //  - R3 + L3 = bascule un BEACON CONTINU (mode DIRECT de BeaconGuide, vol d'oiseau) vers
    //    l'entree courante. Suit la navigation si on renavigue pendant que le beacon est arme.
    //    Cible = SNAPSHOT resynchronise a chaque rescan (pas de vrai tracking image par image).
    //  - Disparition (ramasse, tue, sorti de l'ecran) : bascule auto sur la plus proche restante
    //    de la MEME categorie (annoncee). Categorie videe -> arret + signal, PAS de bascule vers
    //    une autre categorie (choix explicite de l'utilisateur, on n'empiete pas dessus).
    //
    // Detection "ecran reel" : meme technique que la sentinelle d'aggro / les balises de relais
    // (camera ORTHOGRAPHIQUE centree joueur -> demi-largeur/hauteur en cases, PAS un rayon fixe).
    // Objets poses (coffres, plantes, ressources) lus directement dans ObjectIndex (deja alimente
    // par TileReaderSystem) ; creatures (ennemis / passifs / PNJ marchands) via VisibilityScan
    // (pont ECS dedie, cf. TileReader.cs).
    internal static class ProximityScanner
    {
        internal enum Category { Enemy, Passive, Merchant, Plant, Resource, Chest }

        // Ordre de cyclage FIXE (comme les positions de roue) : previsible au D-pad haut/bas.
        private static readonly Category[] CatOrder =
        {
            Category.Enemy, Category.Passive, Category.Merchant,
            Category.Plant, Category.Resource, Category.Chest,
        };

        internal struct Entry
        {
            public long Key;     // identite stable : position (objets) ou EntityKey (creatures)
            public float2 Pos;
            public ObjectID Id;
            public Category Cat;
        }

        // Ressources sauvages reconnues (liste a etoffer au fil des decouvertes, meme convention
        // que l'ancien PingSonar.IsFind pour les zones de fouille).
        private static readonly HashSet<ObjectID> WildResourceIds = new HashSet<ObjectID>
        {
            ObjectID.Mushroom, ObjectID.GiantMushroom, ObjectID.GiantMushroom2, ObjectID.GlowingMushroom,
        };

        // Timbres PLACEHOLDER (domaine son = utilisateur, a choisir a l'oreille) : repris des
        // familles deja utilisees ailleurs dans le mod (canne laser / ancien ping sonar).
        private const SfxID EnemySfx = SfxID.proximity_sensor_set;
        private const SfxID PassiveSfx = SfxID.inventory_doot;
        private const SfxID MerchantSfx = SfxID.menu_select2;
        private const SfxID PlantSfx = SfxID.inventory_ding;
        private const SfxID ResourceSfx = SfxID.inventory_ding;
        private const SfxID ChestSfx = SfxID.chestopen;

        private const float RebuildInterval = 0.25f; // meme cadence que l'ObjectIndex
        private static float _nextRebuild;

        private static readonly List<Entry>[] _byCat = BuildBuffers();
        private static List<Entry>[] BuildBuffers()
        {
            var a = new List<Entry>[CatOrder.Length];
            for (int i = 0; i < a.Length; i++) a[i] = new List<Entry>();
            return a;
        }

        // --- Etat de navigation (persiste entre deux tenues de R3) ---
        private static int _catOrderIdx = -1; // index dans CatOrder, -1 = jamais ouvert
        private static int _entIndex = -1;    // index dans _byCat[categorie courante]
        private static long _curKey;          // identite de l'entree courante (suit les rebuilds)
        private static bool _beaconArmed;     // R3+L3 actif, cible = entree courante
        private static bool _wasHeld;

        public static void Tick(PlayerController player)
        {
            bool eligible = player != null && InputContext.InGameFree;
            VisibilityScan.Active = eligible;
            if (!eligible) { _wasHeld = false; return; }

            float2 playerPos = new float2(player.WorldPosition.x, player.WorldPosition.z);
            var cam0 = Manager.camera != null ? Manager.camera.gameCamera : null;
            float2 camHalf = cam0 != null
                ? new float2(cam0.orthographicSize * cam0.aspect + 1f, cam0.orthographicSize + 1f)
                : new float2(12f, 8f);
            VisibilityScan.Center = playerPos;
            VisibilityScan.CamHalf = camHalf;

            // Le beacon vient de s'arreter tout seul (arrivee sur la cible : BeaconGuide.Tick
            // tourne juste avant nous dans GameplayInput) OU est deja en PAUSE depuis un tick
            // precedent. Tant qu'arme : PAUSE tant que l'entite existe et qu'on reste pres
            // d'elle ; REPRISE sur la MEME entite si on s'en eloigne ; SAUT vers la plus proche
            // restante de la MEME categorie seulement si l'entite a DISPARU.
            if (_beaconArmed && !BeaconGuide.Active) HandlePausedBeacon(playerPos);

            if (Time.unscaledTime >= _nextRebuild)
            {
                _nextRebuild = Time.unscaledTime + RebuildInterval;
                Rebuild(playerPos, camHalf);
                Reconcile(playerPos);
            }

            bool held = ScannerModifier.Held;
            if (held && !_wasHeld) OpenAnnounce(playerPos);
            _wasHeld = held;
            if (!held) return;

            if (ScannerModifier.DpadUpPressed) CycleCategory(playerPos, 1);
            else if (ScannerModifier.DpadDownPressed) CycleCategory(playerPos, -1);
            else if (ScannerModifier.DpadRightPressed) CycleEntity(playerPos, 1);
            else if (ScannerModifier.DpadLeftPressed) CycleEntity(playerPos, -1);

            if (ScannerModifier.ToggleBeacon) ToggleBeaconTarget(playerPos);
        }

        // --- Construction de la vue (objets via ObjectIndex + creatures via VisibilityScan) ---
        private static void Rebuild(float2 playerPos, float2 camHalf)
        {
            for (int i = 0; i < _byCat.Length; i++) _byCat[i].Clear();

            foreach (var kv in ObjectIndex.Map)
            {
                var e = kv.Value;
                Category cat;
                if (e.Plant != PlantState.None) cat = Category.Plant;
                else if (e.HasStorage) cat = Category.Chest;
                else if (WildResourceIds.Contains(e.Id)) cat = Category.Resource;
                else continue;

                float2 p = new float2((int)(kv.Key >> 32), (int)(uint)kv.Key);
                float2 d = p - playerPos;
                if (Mathf.Abs(d.x) > camHalf.x || Mathf.Abs(d.y) > camHalf.y) continue;
                _byCat[(int)cat].Add(new Entry { Key = kv.Key, Pos = p, Id = e.Id, Cat = cat });
            }

            for (int i = 0; i < VisibilityScan.Count; i++)
            {
                var t = VisibilityScan.Targets[i];
                _byCat[(int)t.Cat].Add(new Entry { Key = t.Key, Pos = t.Pos, Id = t.Obj, Cat = t.Cat });
            }

            for (int i = 0; i < _byCat.Length; i++)
            {
                var list = _byCat[i];
                list.Sort((a, b) => math.lengthsq(a.Pos - playerPos).CompareTo(math.lengthsq(b.Pos - playerPos)));
            }
        }

        // Marge de reprise au-dela du seuil d'arrivee de BeaconGuide (1 case) : le joueur doit
        // clairement s'etre eloigne, pas juste avoir bouge d'un pixel a la limite (anti-flicker).
        private const float ResumeDistanceTiles = 2f;

        // --- Pause / reprise / saut apres une arrivee (BeaconGuide s'est arrete tout seul) ---
        // L'entite visee a-t-elle DISPARU ? -> saute sur la plus proche restante de la MEME
        // categorie et relance. Toujours la mais le joueur s'est ELOIGNE ? -> relance sur la
        // MEME entite. Toujours la et joueur pres d'elle -> PAUSE, rien a faire.
        private static void HandlePausedBeacon(float2 playerPos)
        {
            if (_catOrderIdx < 0) { _beaconArmed = false; return; }
            var list = _byCat[(int)CatOrder[_catOrderIdx]];

            int found = -1;
            for (int i = 0; i < list.Count; i++) if (list[i].Key == _curKey) { found = i; break; }

            if (found < 0)
            {
                // Disparue pendant la pause : saut sur la plus proche restante (triee par distance).
                if (list.Count > 0)
                {
                    _entIndex = 0;
                    var e = list[0];
                    _curKey = e.Key;
                    PlayEarcon(playerPos, e);
                    BeaconGuide.StartDirect(ToTile(e.Pos), InGameTtsCore.ResolveObjectName(e.Id)); // annonce "Guidage vers X"
                }
                else
                {
                    _beaconArmed = false;
                    _entIndex = -1;
                    TtsText.Say(Strings.L("scanner.categoryempty"), true);
                }
                return;
            }

            _entIndex = found;
            var entry = list[found];
            if (math.distance(playerPos, entry.Pos) > ResumeDistanceTiles)
                BeaconGuide.StartDirect(ToTile(entry.Pos), InGameTtsCore.ResolveObjectName(entry.Id)); // reprise, annonce
            // Sinon : toujours pres d'elle, on reste en pause (rien a jouer).
        }

        // --- Reconciliation apres rebuild : suit l'entite courante, gere sa disparition ---
        private static void Reconcile(float2 playerPos)
        {
            if (_catOrderIdx < 0) { FindFirstNonEmptySilent(); return; }

            var list = _byCat[(int)CatOrder[_catOrderIdx]];
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Key != _curKey) continue;
                _entIndex = i;
                // Toujours la meme entite : resynchro silencieuse du beacon si arme (suit un
                // rescan periodique, pas un vrai tracking image par image).
                if (_beaconArmed) BeaconGuide.RetargetDirectSilent(ToTile(list[i].Pos));
                return;
            }

            // Disparue (ramassee / tuee / sortie de l'ecran).
            bool listening = ScannerModifier.Held || _beaconArmed;
            if (list.Count > 0)
            {
                _entIndex = 0; // triee par distance -> la plus proche restante
                if (listening) SetCurrent(playerPos, announceCategory: false, interrupt: true);
                else
                {
                    _curKey = list[0].Key;
                    if (_beaconArmed) BeaconGuide.RetargetDirectSilent(ToTile(list[0].Pos));
                }
            }
            else
            {
                _entIndex = -1;
                if (_beaconArmed)
                {
                    BeaconGuide.Stop(false);
                    _beaconArmed = false;
                    TtsText.Say(Strings.L("scanner.categoryempty"), true);
                }
                else if (ScannerModifier.Held)
                {
                    TtsText.Say(Strings.L("scanner.categoryempty"), true);
                }
            }
        }

        private static void FindFirstNonEmptySilent()
        {
            for (int i = 0; i < CatOrder.Length; i++)
            {
                if (_byCat[(int)CatOrder[i]].Count == 0) continue;
                _catOrderIdx = i;
                _entIndex = 0;
                _curKey = _byCat[(int)CatOrder[i]][0].Key;
                return;
            }
        }

        // --- Ouverture (R3 seul, avant tout D-pad) : reannonce categorie + 1re entree ---
        private static void OpenAnnounce(float2 playerPos)
        {
            if (_catOrderIdx < 0)
            {
                FindFirstNonEmptySilent();
                if (_catOrderIdx < 0) { TtsText.Say(Strings.L("scanner.none"), true); return; }
                SetCurrent(playerPos, announceCategory: true, interrupt: true);
                return;
            }

            var list = _byCat[(int)CatOrder[_catOrderIdx]];
            if (list.Count == 0) { TtsText.Say(Strings.L("scanner.categoryempty"), true); return; }
            _entIndex = 0; // fraiche ouverture : repart sur la 1re entree de la categorie courante
            SetCurrent(playerPos, announceCategory: true, interrupt: true);
        }

        private static void CycleCategory(float2 playerPos, int dir)
        {
            if (_catOrderIdx < 0)
            {
                FindFirstNonEmptySilent();
                if (_catOrderIdx < 0) { TtsText.Say(Strings.L("scanner.none"), true); return; }
                SetCurrent(playerPos, announceCategory: true, interrupt: true);
                return;
            }

            int n = CatOrder.Length;
            // step < n (PAS <=) : ne revient JAMAIS sur la categorie de depart - une seule
            // categorie non vide doit rester silencieuse (et garder sa position d'entree),
            // pas se re-annoncer en remettant l'index a 0.
            for (int step = 1; step < n; step++)
            {
                int idx = ((_catOrderIdx + dir * step) % n + n) % n;
                if (_byCat[(int)CatOrder[idx]].Count == 0) continue;
                _catOrderIdx = idx;
                _entIndex = 0;
                SetCurrent(playerPos, announceCategory: true, interrupt: true);
                return;
            }
            // Aucune autre categorie non vide (une seule a du monde) : rien de plus a annoncer.
        }

        private static void CycleEntity(float2 playerPos, int dir)
        {
            if (_catOrderIdx < 0) return;
            var list = _byCat[(int)CatOrder[_catOrderIdx]];
            if (list.Count == 0) return;
            int n = list.Count;
            _entIndex = ((_entIndex + dir) % n + n) % n; // bouclee
            SetCurrent(playerPos, announceCategory: false, interrupt: true);
        }

        private static void SetCurrent(float2 playerPos, bool announceCategory, bool interrupt)
        {
            var list = _byCat[(int)CatOrder[_catOrderIdx]];
            var e = list[_entIndex];
            _curKey = e.Key;
            string name = InGameTtsCore.ResolveObjectName(e.Id);
            string text = announceCategory ? Strings.L(CategoryKey(e.Cat)) + ". " + name : name;
            TtsText.Say(text, interrupt);
            PlayEarcon(playerPos, e);
            // Beacon arme : suit silencieusement la nouvelle selection (le TTS ci-dessus dit
            // deja le nom, pas besoin de repeter "Guidage vers X").
            if (_beaconArmed) BeaconGuide.RetargetDirectSilent(ToTile(e.Pos));
        }

        private static void ToggleBeaconTarget(float2 playerPos)
        {
            if (_beaconArmed)
            {
                BeaconGuide.Stop(true);
                _beaconArmed = false;
                return;
            }
            if (_catOrderIdx < 0) { TtsText.Say(Strings.L("scanner.none"), true); return; }
            var list = _byCat[(int)CatOrder[_catOrderIdx]];
            if (_entIndex < 0 || _entIndex >= list.Count) { TtsText.Say(Strings.L("scanner.none"), true); return; }
            var e = list[_entIndex];
            _beaconArmed = true;
            BeaconGuide.StartDirect(ToTile(e.Pos), InGameTtsCore.ResolveObjectName(e.Id));
        }

        private static void PlayEarcon(float2 playerPos, Entry e)
        {
            float2 d = e.Pos - playerPos;
            float pan = GameplayAudio.PanFromTiles(d.x);
            float pitch = Mathf.Clamp(Mathf.Pow(2f, d.y / 12f), 0.5f, 2f);
            float vol = A11ySettings.ScannerVolume * GameplayAudio.DistanceTrim(math.length(d));
            GameplayAudio.PlaySpatial(SfxFor(e.Cat), pan, pitch, vol);
        }

        private static SfxID SfxFor(Category c)
        {
            switch (c)
            {
                case Category.Enemy: return EnemySfx;
                case Category.Passive: return PassiveSfx;
                case Category.Merchant: return MerchantSfx;
                case Category.Plant: return PlantSfx;
                case Category.Resource: return ResourceSfx;
                case Category.Chest: return ChestSfx;
                default: return PassiveSfx;
            }
        }

        private static string CategoryKey(Category c)
        {
            switch (c)
            {
                case Category.Enemy: return "scanner.cat.enemy";
                case Category.Passive: return "scanner.cat.passive";
                case Category.Merchant: return "scanner.cat.merchant";
                case Category.Plant: return "scanner.cat.plant";
                case Category.Resource: return "scanner.cat.resource";
                case Category.Chest: return "scanner.cat.chest";
                default: return "";
            }
        }

        private static int2 ToTile(float2 p) => new int2(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y));
    }
}
