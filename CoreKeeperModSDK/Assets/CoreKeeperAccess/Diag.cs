using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace CoreKeeperAccess
{
    // Diagnostic runtime partage (lot audit, juin 2026). Un sous-systeme dont le tick
    // casse (maj du jeu, API renommee) doit le DIRE dans Player.log : un joueur aveugle
    // ne distingue pas "rien ici" de "le mod est mort". Un seul fichier de log (le
    // Player.log que les testeurs joignent deja), timecode en tete de chaque ligne
    // pour correler l'erreur avec ce que le joueur entendait au meme moment.
    internal static class Diag
    {
        // Plafond d'erreurs DISTINCTES par site : au-dela, une ligne "supprimees" puis
        // silence (un site qui en accumule autant est mort, inutile d'inonder le log).
        private const int MaxDistinctPerSite = 5;

        private static readonly HashSet<string> _seen = new HashSet<string>();
        private static readonly Dictionary<string, int> _distinctPerSite = new Dictionary<string, int>();

        // Timecode de session "mm:ss.mmm" depuis le boot (les lignes Player.log ne sont
        // pas datees par Unity : sans ca, aucune correlation erreur <-> vecu possible).
        public static string Stamp()
        {
            float t = Time.realtimeSinceStartup;
            int m = (int)(t / 60f);
            return string.Format("[{0:00}:{1:00.000}]", m, t - m * 60f);
        }

        public static void Log(string site, string message)
        {
            Debug.Log("[" + site + "] " + Stamp() + " " + message);
        }

        // Erreur d'un tick de sous-systeme. Dedupliquee par IDENTITE d'erreur : type
        // d'exception + haut de la pile - jamais le message brut, qui peut contenir des
        // valeurs variables et contourner la deduplication. Une erreur DIFFERENTE au
        // meme site logge a son tour (un booleen log-once unique masquerait la 2e).
        public static void Error(string site, Exception ex)
        {
            string identity = site + "|" + (ex == null ? "null" : ex.GetType().FullName) + "|" + TopFrame(ex);
            if (!_seen.Add(identity)) return;

            _distinctPerSite.TryGetValue(site, out int n);
            _distinctPerSite[site] = n + 1;
            if (n >= MaxDistinctPerSite)
            {
                if (n == MaxDistinctPerSite)
                    Debug.LogError("[" + site + "] " + Stamp() + " plafond atteint, erreurs distinctes suivantes supprimees pour ce site.");
                return;
            }

            Debug.LogError("[" + site + "] " + Stamp() + " " + ex);
        }

        // Avertissement deduplique par cle explicite (ex. un tag de balisage qui fuit
        // vers le TTS : la cle est le tag, pas le texte complet qui varie).
        public static void Warn(string site, string key, string message)
        {
            if (!_seen.Add(site + "|warn|" + key)) return;
            Debug.LogWarning("[" + site + "] " + Stamp() + " " + message);
        }

        private static string TopFrame(Exception ex)
        {
            string st = ex != null ? ex.StackTrace : null;
            if (string.IsNullOrEmpty(st)) return "";
            int nl = st.IndexOf('\n');
            return (nl > 0 ? st.Substring(0, nl) : st).Trim();
        }
    }

    // Cle d'identite stable d'une entite ECS : index + version packes en long.
    // L'index seul est RECYCLE par Unity (fix audit) : un mob mort puis un nouveau
    // spawn peuvent partager l'index -> deduplication erronee (annonce de nom ratee).
    internal static class EntityKey
    {
        public static long Of(Entity e) => ((long)e.Version << 32) | (uint)e.Index;
    }
}
