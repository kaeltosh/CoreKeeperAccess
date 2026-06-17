using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CoreKeeperAccess.Localization;
using UnityEngine;

namespace CoreKeeperAccess.Navigation
{
    // Une replique archivee : sa SECTION (Coeur / tutoriels) et, pour le Coeur, son numero de
    // CONVERSATION (groupage : une session de dialogue rapprochee dans le temps). Le texte est
    // deja resolu/nettoye a la capture.
    internal sealed class DialogueEntry
    {
        public string section; // "core" | "tutorial"
        public int conv;        // conversation (Coeur) ; 1 = activation reconstruite, >=2 = live ; 0 pour les tutoriels
        public string text;
    }

    // Un GROUPE du journal pour la nav hierarchique (arborescence facon NVDA) : un titre
    // (« Cœur, conversation N » ou « Tutoriels ») et ses repliques. On deroule un groupe pour
    // lire ses lignes, on le replie pour revenir a la liste des groupes.
    internal sealed class DialogueGroup
    {
        public string label;
        public readonly List<string> lines = new List<string>();
    }

    // Journal des dialogues scenarises, persiste PAR MONDE. Deux sources : le Coeur (vrai
    // dialogue, groupe par conversation) et les TUTORIELS early-game (section dediee, pour ne
    // pas polluer le Coeur). Les repliques du Coeur sont fugaces (annoncees en interrupt, vite
    // ecrasees) et parfois ONE-SHOT par monde (le dialogue d'activation ne rejoue jamais) : on
    // les archive pour les relire dans l'onglet "Journal" de la carte.
    //
    // SERIALISATION MAISON (pas JsonUtility, qui jette les types hot-compile - cf. lecon MEMORY).
    // Format v2 :
    //   ligne 1 : "v2"
    //   ligne 2 : "nextConv=N"
    //   lignes  : "section|conv|texte"   (texte = tout apres le 2e '|')
    // Lecture retro-compatible v1 (lignes de texte brut) : migrees en section "core", conv 1.
    // Fichier : <persistentDataPath>/CoreKeeperAccess/dialogues/<worldId>.txt.
    internal static class DialogueLog
    {
        private const string HeaderV2 = "v2";
        private const string HeaderV1 = "v1";
        private const int MaxEntries = 400;
        private const float ConvGapSeconds = 30f; // au-dela, une nouvelle replique du Coeur ouvre une conversation
        private const int ActivationConv = 1;     // conversation reservee au dialogue d'activation

        private static int _worldId = int.MinValue;
        private static int _nextConv = 2;          // 1 reserve a l'activation -> le live commence a 2
        private static readonly List<DialogueEntry> _entries = new List<DialogueEntry>();

        // Suivi de la conversation courante du Coeur (en memoire, non persiste).
        private static int _lastCoreConv = -1;
        private static float _lastCoreTime = -9999f;

        // --- Capture ---

        // Replique du Coeur : groupage par conversation (rupture temporelle > ConvGapSeconds).
        public static void AddCore(string text)
        {
            text = Clean(text);
            if (text == null) return;
            EnsureLoaded();
            if (Contains(text)) return; // dedup global (hints du Coeur qui se rejouent)
            int conv;
            float now = Time.time;
            if (_lastCoreConv < 0 || now - _lastCoreTime > ConvGapSeconds) { conv = _nextConv++; }
            else conv = _lastCoreConv;
            _lastCoreConv = conv;
            _lastCoreTime = now;
            _entries.Add(new DialogueEntry { section = "core", conv = conv, text = text });
            Trim();
            Save();
        }

        // Message de tutoriel (early-game) : section dediee, pas de conversation.
        public static void AddTutorial(string text)
        {
            text = Clean(text);
            if (text == null) return;
            EnsureLoaded();
            if (Contains(text)) return;
            _entries.Add(new DialogueEntry { section = "tutorial", conv = 0, text = text });
            Trim();
            Save();
        }

        // Injecte le dialogue d'ACTIVATION reconstruit (one-shot deja joue) : conversation
        // reservee 1 -> il s'affiche en tete (chronologiquement premier). Dedup global.
        public static void SeedActivation(IList<string> lines)
        {
            if (lines == null || lines.Count == 0) return;
            EnsureLoaded();
            bool changed = false;
            foreach (var raw in lines)
            {
                var t = Clean(raw);
                if (t == null || Contains(t)) continue;
                _entries.Add(new DialogueEntry { section = "core", conv = ActivationConv, text = t });
                changed = true;
            }
            if (changed) { Trim(); Save(); }
        }

        // --- Lecture (onglet Journal) ---

        // Groupes pour la nav hierarchique : Coeur d'abord (un groupe par conversation, ordre
        // croissant), puis Tutoriels (un groupe unique). Chaque groupe porte son titre et ses
        // repliques.
        public static List<DialogueGroup> BuildGroups()
        {
            EnsureLoaded();
            var order = new List<int>();
            for (int i = 0; i < _entries.Count; i++) order.Add(i);
            order.Sort((a, b) =>
            {
                var ea = _entries[a]; var eb = _entries[b];
                int sa = ea.section == "tutorial" ? 1 : 0;
                int sb = eb.section == "tutorial" ? 1 : 0;
                if (sa != sb) return sa.CompareTo(sb);
                if (ea.conv != eb.conv) return ea.conv.CompareTo(eb.conv);
                return a.CompareTo(b); // stable : ordre d'insertion
            });

            // Numerotation RELATIVE des conversations du Coeur (1, 2, 3... dans l'ordre).
            var convRank = new Dictionary<int, int>();
            int rank = 0;
            foreach (var i in order)
            {
                var e = _entries[i];
                if (e.section != "tutorial" && !convRank.ContainsKey(e.conv)) convRank[e.conv] = ++rank;
            }

            var groups = new List<DialogueGroup>();
            string curKey = null;
            DialogueGroup cur = null;
            foreach (var i in order)
            {
                var e = _entries[i];
                bool isTut = e.section == "tutorial";
                string key = isTut ? "tut" : "core:" + e.conv;
                if (key != curKey)
                {
                    cur = new DialogueGroup
                    {
                        label = isTut
                            ? Strings.L("map.journal.tutorials")
                            : Strings.L("map.journal.core") + ", " + Strings.L("journal.conversation") + " " + convRank[e.conv]
                    };
                    groups.Add(cur);
                    curKey = key;
                }
                cur.lines.Add(e.text);
            }
            return groups;
        }

        // --- Interne ---

        private static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            text = text.Replace("\r", "").Replace("\n", " ").Trim();
            return text.Length == 0 ? null : text;
        }

        private static bool Contains(string text)
        {
            foreach (var e in _entries) if (e.text == text) return true;
            return false;
        }

        private static void Trim()
        {
            if (_entries.Count > MaxEntries) _entries.RemoveRange(0, _entries.Count - MaxEntries);
        }

        private static void EnsureLoaded()
        {
            int id = CurrentWorldId();
            if (id == _worldId) return;
            _worldId = id;
            _nextConv = 2;
            _lastCoreConv = -1;
            _lastCoreTime = -9999f;
            _entries.Clear();
            try
            {
                string path = FilePath(id);
                if (!File.Exists(path)) return;
                var lines = File.ReadAllLines(path, Encoding.UTF8);
                bool v2 = lines.Length > 0 && lines[0] == HeaderV2;
                foreach (var raw in lines)
                {
                    if (string.IsNullOrEmpty(raw) || raw == HeaderV1 || raw == HeaderV2) continue;
                    if (raw.StartsWith("nextConv=")) { int.TryParse(raw.Substring(9), out _nextConv); continue; }
                    if (v2) ParseEntry(raw);
                    else AddLoaded("core", ActivationConv, raw); // migration v1 -> tout en core/conv 1
                }
                if (_nextConv < 2) _nextConv = 2;
            }
            catch (Exception ex) { Diag.Error("A11yDialogueLog", ex); }
            Diag.Log("A11yDialogueLog", "load world=" + id + " n=" + _entries.Count + " nextConv=" + _nextConv);
        }

        private static void ParseEntry(string line)
        {
            int b1 = line.IndexOf('|');
            if (b1 < 0) return;
            int b2 = line.IndexOf('|', b1 + 1);
            if (b2 < 0) return;
            string section = line.Substring(0, b1);
            if (!int.TryParse(line.Substring(b1 + 1, b2 - b1 - 1), out int conv)) return;
            AddLoaded(section, conv, line.Substring(b2 + 1));
        }

        // Ajout au chargement : dedup global (purge les doublons deja ecrits sur disque).
        private static void AddLoaded(string section, int conv, string text)
        {
            text = Clean(text);
            if (text == null || Contains(text)) return;
            _entries.Add(new DialogueEntry { section = section, conv = conv, text = text });
        }

        private static void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append(HeaderV2).Append('\n');
                sb.Append("nextConv=").Append(_nextConv).Append('\n');
                foreach (var e in _entries)
                    sb.Append(e.section).Append('|').Append(e.conv).Append('|').Append(e.text).Append('\n');
                File.WriteAllText(FilePath(_worldId), sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) { Diag.Error("A11yDialogueLog", ex); }
        }

        private static int CurrentWorldId()
        {
            try { return Manager.saves != null ? Manager.saves.GetWorldId() : 0; }
            catch { return 0; }
        }

        private static string FilePath(int id)
        {
            string dir = Path.Combine(Application.persistentDataPath, "CoreKeeperAccess", "dialogues");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, id + ".txt");
        }
    }
}
