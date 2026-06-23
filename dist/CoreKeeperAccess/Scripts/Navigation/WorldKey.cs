using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace CoreKeeperAccess.Navigation
{
    // Cle d'identite du MONDE pour toute notre persistance maison (graphe de nav, balises,
    // journal de dialogues).
    //
    // PIEGE CORRIGE (23 juin 2026) : on indexait nos fichiers par Manager.saves.GetWorldId(),
    // qui n'est PAS un identifiant de monde mais l'INDEX DU SLOT d'emplacement (0..30, un
    // index dans worldDataFiles[31]). Supprimer un monde puis en recreer un sur le MEME slot
    // reutilisait donc nos fichiers -> le nouveau monde heritait des balises / reseau / journal
    // de l'ancien (nav point par point cassee : on guidait vers des torches disparues). On
    // indexe desormais par WorldInfo.guid (GUID unique genere a la creation du monde via
    // ServerGuidCD, remis a vide a la suppression). Bonus : valable aussi en multi, ou l'index
    // de slot n'a aucun sens pour un invite alors que le guid est replique a tous.
    internal static class WorldKey
    {
        // Guid (assaini) du monde courant, ou null s'il n'est pas (encore) disponible : au menu,
        // ou juste avant que l'ECS publie le ServerGuidCD d'un monde tout neuf. Les magasins ne
        // chargent / n'ecrivent QUE sur une cle non nulle -> jamais de fichier fantome "vide".
        public static string Current()
        {
            try
            {
                var saves = Manager.saves;
                if (saves == null) return null;
                var info = saves.GetWorldInfo();
                var guid = info != null ? info.guid : null;
                if (string.IsNullOrEmpty(guid)) return null;
                return Sanitize(guid);
            }
            catch { return null; }
        }

        // Migration CoTE DEV UNIQUEMENT (garde dev.flag) : recopie une seule fois l'ancien
        // fichier nomme par index de slot vers le nom guid (si la cible n'existe pas deja).
        // Absente du build distribue -> les testeurs repartent d'un etat propre (decision
        // utilisateur). On ne SUPPRIME jamais l'ancien fichier (filet de securite). subDir =
        // "graph" | "beacons" | "dialogues".
        public static void MigrateLegacyIfDev(string subDir, string guidKey)
        {
            if (!CoreKeeperAccessMod.DevMode || string.IsNullOrEmpty(guidKey)) return;
            try
            {
                int slot = Manager.saves != null ? Manager.saves.GetWorldId() : -1;
                if (slot < 0) return;
                string dir = Path.Combine(Application.persistentDataPath, "CoreKeeperAccess", subDir);
                string legacy = Path.Combine(dir, slot + ".txt");
                string target = Path.Combine(dir, guidKey + ".txt");
                if (File.Exists(legacy) && !File.Exists(target))
                {
                    Directory.CreateDirectory(dir);
                    File.Copy(legacy, target);
                    Diag.Log("A11yWorldKey", "migrated " + subDir + " slot=" + slot + " -> " + guidKey);
                }
            }
            catch (Exception ex) { Diag.Error("A11yWorldKey", ex); }
        }

        // Supprime nos fichiers de persistance d'un monde a sa SUPPRESSION effective (hook sur
        // SaveManager.RemoveWorld, avant que le jeu n'efface le guid). On efface a la fois le
        // fichier par-guid (orphelin chez tous) ET le vieux fichier par-slot (legacy, present
        // uniquement chez le dev migre) : sans ca, supprimer puis recreer un monde sur le meme
        // slot ferait re-migrer le legacy vers le nouveau guid -> contamination chez le dev.
        public static void PurgeWorldFiles(int slot)
        {
            string guid = null;
            try
            {
                var info = Manager.saves != null ? Manager.saves.GetWorldInfo(slot) : null;
                if (info != null && !string.IsNullOrEmpty(info.guid)) guid = Sanitize(info.guid);
            }
            catch { }
            foreach (var sub in new[] { "graph", "beacons", "dialogues" })
            {
                DeleteFile(sub, slot.ToString()); // legacy par-slot (dev)
                if (guid != null) DeleteFile(sub, guid); // par-guid (orphelin)
            }
        }

        private static void DeleteFile(string subDir, string name)
        {
            try
            {
                string p = Path.Combine(Application.persistentDataPath, "CoreKeeperAccess", subDir, name + ".txt");
                if (File.Exists(p)) { File.Delete(p); Diag.Log("A11yWorldKey", "purged " + subDir + "/" + name); }
            }
            catch (Exception ex) { Diag.Error("A11yWorldKey", ex); }
        }

        // Le guid sert de nom de fichier : on ne garde que des caracteres surs (le format "N"
        // d'un Hash128 est deja hexa, mais on blinde contre toute surprise).
        private static string Sanitize(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
            return sb.Length > 0 ? sb.ToString() : "default";
        }
    }
}
