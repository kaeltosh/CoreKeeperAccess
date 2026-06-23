using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;

namespace CoreKeeperAccess.Navigation
{
    internal sealed class BeaconEntry { public int x; public int y; public string name; }

    // Magasin des NOMS de balises, persiste PAR MONDE, indexe par POSITION (la case). Le
    // jeu sauvegarde deja les marqueurs eux-memes (position + couleur, entites serialisees) ;
    // nous on ne garde que le nom - invariant fondateur du reseau de navigation (cf. fiche
    // beacon-navigation). Embryon du futur magasin de noeuds.
    // Fichier : <persistentDataPath>/CoreKeeperAccess/beacons/<worldId>.txt.
    //
    // SERIALISATION MAISON (pas JsonUtility) : le code du mod est compile a l'EXECUTION
    // (Roslyn), donc le systeme de serialisation natif d'Unity - fige au build - ne
    // reconnait PAS nos types runtime : JsonUtility laissait silencieusement tomber la
    // List<BeaconEntry> (n=1 en memoire -> json {"nextId":4} sur disque). On ecrit donc un
    // format texte trivial, sans dependance au registre de types Unity :
    //   ligne 1 : "v1"        (version)
    //   ligne 2 : "nextId=N"
    //   lignes  : "x|y|nom"   (nom = tout apres le 2e '|', peut contenir des '|' et espaces)
    internal static class BeaconStore
    {
        private const string Header = "v1";
        private static string _worldKey; // monde charge en memoire (sentinelle, guid - cf. WorldKey)
        private static int _nextId = 1;
        private static readonly Dictionary<long, BeaconEntry> _byPos = new Dictionary<long, BeaconEntry>();

        private static long Key(int2 p) => ((long)p.x << 32) ^ (uint)p.y;

        // Recharge si le monde courant a change (changement de save sans relancer le jeu).
        private static void EnsureLoaded()
        {
            string key = WorldKey.Current();
            if (key == null || key == _worldKey) return; // pas pret (menu) ou deja charge
            _worldKey = key;
            _nextId = 1;
            _byPos.Clear();
            WorldKey.MigrateLegacyIfDev("beacons", key); // dev seulement : recupere l'ancien fichier par slot
            try
            {
                string path = FilePath(key);
                if (File.Exists(path))
                    foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
                        ParseLine(raw);
            }
            catch (Exception ex) { Diag.Error("A11yBeaconStore", ex); }
            Diag.Log("A11yBeaconStore", "load world=" + key + " n=" + _byPos.Count + " nextId=" + _nextId);
        }

        private static void ParseLine(string line)
        {
            if (string.IsNullOrEmpty(line) || line == Header) return;
            if (line.StartsWith("nextId="))
            {
                int.TryParse(line.Substring(7), out _nextId);
                return;
            }
            int b1 = line.IndexOf('|');
            if (b1 < 0) return;
            int b2 = line.IndexOf('|', b1 + 1);
            if (b2 < 0) return;
            if (!int.TryParse(line.Substring(0, b1), out int x)) return;
            if (!int.TryParse(line.Substring(b1 + 1, b2 - b1 - 1), out int y)) return;
            string name = line.Substring(b2 + 1);
            _byPos[Key(new int2(x, y))] = new BeaconEntry { x = x, y = y, name = name };
        }

        private static string FilePath(string key)
        {
            string dir = Path.Combine(Application.persistentDataPath, "CoreKeeperAccess", "beacons");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, key + ".txt");
        }

        private static void Save()
        {
            if (_worldKey == null) return; // pas de monde identifie -> jamais de fichier fantome
            try
            {
                var sb = new StringBuilder();
                sb.Append(Header).Append('\n');
                sb.Append("nextId=").Append(_nextId).Append('\n');
                foreach (var e in _byPos.Values)
                    sb.Append(e.x).Append('|').Append(e.y).Append('|').Append(e.name).Append('\n');
                File.WriteAllText(FilePath(_worldKey), sb.ToString(), new UTF8Encoding(false));
                Diag.Log("A11yBeaconStore", "save world=" + _worldKey + " n=" + _byPos.Count);
            }
            catch (Exception ex) { Diag.Error("A11yBeaconStore", ex); }
        }

        // Force le (re)chargement du monde courant (et sa migration dev) sans rien lire :
        // appele a l'entree en jeu pour que la conversion ne depende pas d'un acces aux noms.
        public static void Warmup() => EnsureLoaded();

        // Oublie le monde charge en memoire (cache vide, sentinelle remise a zero) : appele a la
        // suppression d'un monde pour qu'aucun Flush tardif ne reecrive le fichier qu'on vient
        // d'effacer, et que le prochain monde recharge proprement depuis le disque.
        public static void ForgetCurrent()
        {
            _worldKey = null;
            _nextId = 1;
            _byPos.Clear();
        }

        public static string GetName(int2 pos)
        {
            EnsureLoaded();
            return _byPos.TryGetValue(Key(pos), out var e) ? e.name : null;
        }

        public static void SetName(int2 pos, string name)
        {
            EnsureLoaded();
            name = (name ?? "").Replace("\r", "").Replace("\n", " "); // jamais de saut de ligne dans un nom
            long k = Key(pos);
            if (_byPos.TryGetValue(k, out var e)) e.name = name;
            else _byPos[k] = new BeaconEntry { x = pos.x, y = pos.y, name = name };
            Diag.Log("A11yBeaconStore", "setname (" + pos.x + "," + pos.y + ") = " + name);
            Save();
        }

        public static void Remove(int2 pos)
        {
            EnsureLoaded();
            if (_byPos.Remove(Key(pos))) Save();
        }

        // Numero monotone pour les noms auto (ne recule pas apres suppression).
        public static int NextAutoNumber()
        {
            EnsureLoaded();
            int n = _nextId++;
            Save();
            return n;
        }
    }
}
