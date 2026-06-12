using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Text.RegularExpressions;
using PugMod;
using UnityEditor;
using UnityEngine;

namespace CoreKeeperAccess.Editor
{
    internal class A11yAutomationFlagWatcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach (var asset in importedAssets)
            {
                if (asset == A11yAutomation.FlagPath)
                {
                    A11yAutomation.TriggerFromPostprocessor();
                    return;
                }
            }
        }
    }


    [InitializeOnLoad]
    public static class A11yAutomation
    {
        public const string FlagPath = "Assets/A11yAutomation.flag.json";
        private const string LogPrefix = "[A11yAutomation]";

        static A11yAutomation()
        {
            EditorApplication.delayCall += RunPending;
            // Polling SANS focus : NVDA gele si Unity a le focus pendant un build
            // (appels UIA synchrones vers un thread principal bloque). On detecte donc
            // le flag sur disque via EditorApplication.update - qui tourne aussi editeur
            // en arriere-plan - et on declenche refresh + action sans que l'utilisateur
            // ait jamais a donner le focus a Unity.
            EditorApplication.update += PollFlag;
        }

        internal static void TriggerFromPostprocessor()
        {
            EditorApplication.delayCall += RunPending;
        }

        private const double PollInterval = 3.0; // secondes entre deux tests du flag
        private static double _nextPoll;
        private static bool _refreshIssued;   // refresh deja demande pour ce flag
        private static bool _failNotified;    // bip d'echec compile deja emis pour ce flag

        private static void PollFlag()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll) return;
            _nextPoll = EditorApplication.timeSinceStartup + PollInterval;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            if (!File.Exists(FlagPath))
            {
                _refreshIssued = false;
                _failNotified = false;
                return;
            }

            if (!_refreshIssued)
            {
                // 1er passage : importer les sources fraiches (et le flag lui-meme)
                // AVANT d'agir. La compile eventuelle suit ; l'action partira a un
                // prochain passage, hors compilation.
                _refreshIssued = true;
                Debug.Log($"{LogPrefix} flag detecte par polling (sans focus), refresh assets");
                AssetDatabase.Refresh();
                return;
            }

            // Ne jamais lancer un build sur un projet en erreur de compile : le
            // ModBuilder echouerait en silence ("scripts have compile errors").
            // On laisse le flag en place : l'action repartira une fois le code repare.
            if (EditorUtility.scriptCompilationFailed)
            {
                if (!_failNotified)
                {
                    _failNotified = true;
                    Debug.LogError($"{LogPrefix} compile en erreur, action du flag suspendue (le flag reste en place)");
                    NotifyDone(false);
                }
                return;
            }

            RunPending();
            _refreshIssued = false;
            _failNotified = false;
        }

        // Retour sonore de fin de build (l'Editor Unity est inaccessible NVDA, et le
        // build fige le PC : ce son est le seul signal "c'est termine").
        // Memes sons que fast-build : Asterisk = succes, Hand (Critical Stop) = echec.
        // EditorApplication.Beep en filet si SystemSounds est muet sous Mono.
        private static void NotifyDone(bool ok)
        {
            try { if (ok) SystemSounds.Asterisk.Play(); else SystemSounds.Hand.Play(); }
            catch { EditorApplication.Beep(); }
        }

        [Serializable]
        private class Flag
        {
            public string action;
            public string modName;
            public string gamePath;
        }

        public static void RunPending()
        {
            if (!File.Exists(FlagPath))
            {
                return;
            }

            Flag flag;
            try
            {
                var json = File.ReadAllText(FlagPath);
                flag = JsonUtility.FromJson<Flag>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogPrefix} failed to parse flag file: {e.Message}");
                return;
            }

            if (flag == null || string.IsNullOrEmpty(flag.action))
            {
                Debug.LogWarning($"{LogPrefix} empty action in flag file, skipping");
                return;
            }

            Debug.Log($"{LogPrefix} executing action: {flag.action}");

            try
            {
                switch (flag.action)
                {
                    case "update_sdk":
                        DoUpdateSdk(flag.gamePath);
                        break;
                    case "create_mod":
                        DoCreateMod(flag.modName);
                        break;
                    case "build_install":
                        DoBuildInstall(flag.modName, flag.gamePath);
                        break;
                    default:
                        Debug.LogError($"{LogPrefix} unknown action: {flag.action}");
                        return;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogPrefix} action {flag.action} threw: {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
                return;
            }

            File.Delete(FlagPath);
            var metaPath = FlagPath + ".meta";
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }
            AssetDatabase.Refresh();
            Debug.Log($"{LogPrefix} action {flag.action} completed, flag removed");
        }

        private static void DoUpdateSdk(string gamePath)
        {
            if (string.IsNullOrEmpty(gamePath))
            {
                gamePath = @"C:\Program Files (x86)\Steam\steamapps\common\Core Keeper";
            }

            Debug.Log($"{LogPrefix} update_sdk: importing game files from {gamePath}");
            ImporterWindow.UpdateFromGamePath(ImporterSettings.Instance, gamePath, out var added);
            EditorPrefs.SetString("PugMod/SDKWindow/GamePath", gamePath);
            Debug.Log($"{LogPrefix} update_sdk: imported {added.Count} assemblies");
            foreach (var a in added)
            {
                Debug.Log($"{LogPrefix}   - {a}");
            }
        }

        private static void DoCreateMod(string modName)
        {
            if (string.IsNullOrEmpty(modName))
            {
                Debug.LogError($"{LogPrefix} create_mod: modName is required");
                return;
            }

            Debug.Log($"{LogPrefix} create_mod: creating skeleton for {modName}");
            ModBuilderWindow.CreateNewMod(modName);
            Debug.Log($"{LogPrefix} create_mod: done. Check Assets/{modName}/");
        }

        private static void ConfigureNativePluginsForMod(string modName)
        {
            var pluginsDir = Path.Combine("Assets", modName, "Plugins", "x86_64");
            if (!Directory.Exists(pluginsDir))
            {
                Debug.Log($"{LogPrefix} no native plugins dir at {pluginsDir}, skipping native plugin config");
                return;
            }

            var dlls = Directory.GetFiles(pluginsDir, "*.dll", SearchOption.AllDirectories);
            foreach (var dllPath in dlls)
            {
                var assetPath = dllPath.Replace('\\', '/');
                var importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
                if (importer == null)
                {
                    Debug.LogWarning($"{LogPrefix} no PluginImporter for {assetPath}, skipping");
                    continue;
                }

                Debug.Log($"{LogPrefix} configuring {assetPath} as native Windows x86_64");
                importer.SetCompatibleWithAnyPlatform(false);
                importer.SetCompatibleWithEditor(true);
                importer.SetEditorData("CPU", "x86_64");
                importer.SetEditorData("OS", "Windows");
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
                importer.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "x86_64");
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows, false);
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneLinux64, false);
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX, false);
                importer.SaveAndReimport();
            }
        }

        private static void DoBuildInstall(string modName, string gamePath)
        {
            if (string.IsNullOrEmpty(modName))
            {
                Debug.LogError($"{LogPrefix} build_install: modName is required");
                NotifyDone(false);
                return;
            }

            if (string.IsNullOrEmpty(gamePath))
            {
                gamePath = @"C:\Program Files (x86)\Steam\steamapps\common\Core Keeper";
            }

            var modSettingsGuids = AssetDatabase.FindAssets($"t:PugMod.ModBuilderSettings");
            ModBuilderSettings modSettings = null;
            foreach (var guid in modSettingsGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var settings = AssetDatabase.LoadAssetAtPath<ModBuilderSettings>(path);
                if (settings != null && settings.metadata.name == modName)
                {
                    modSettings = settings;
                    break;
                }
            }

            if (modSettings == null)
            {
                Debug.LogError($"{LogPrefix} build_install: could not find ModBuilderSettings with name {modName}");
                NotifyDone(false);
                return;
            }

            string installPath;
            if (Directory.Exists(Path.Combine(gamePath, "CoreKeeper_Data")))
            {
                installPath = Path.Combine(gamePath, "CoreKeeper_Data", "StreamingAssets", "Mods");
            }
            else
            {
                Debug.LogError($"{LogPrefix} build_install: could not find CoreKeeper_Data at {gamePath}");
                NotifyDone(false);
                return;
            }

            ConfigureNativePluginsForMod(modName);

            Debug.Log($"{LogPrefix} build_install: building {modName} to {installPath}");
            ModBuilder.BuildMod(modSettings, installPath, success =>
            {
                if (!success)
                {
                    Debug.LogError($"{LogPrefix} build_install: ModBuilder.BuildMod returned failure");
                    NotifyDone(false);
                    return;
                }

                Debug.Log($"{LogPrefix} build_install: build OK, applying native plugin post-fix");
                PostBuildRelocateNatives(modName, installPath, gamePath);
                Debug.Log($"{LogPrefix} build_install: SUCCESS — {modName} installed at {installPath}/{modName}");
                NotifyDone(true);
            });
        }

        private static void PostBuildRelocateNatives(string modName, string installPath, string gamePath)
        {
            var installedModDir = Path.Combine(installPath, modName);
            var librariesDir = Path.Combine(installedModDir, "Libraries");
            if (!Directory.Exists(librariesDir))
            {
                Debug.Log($"{LogPrefix} no Libraries dir at {librariesDir}, nothing to relocate");
                return;
            }

            var nativeNames = new[] { "Tolk.dll", "nvdaControllerClient64.dll" };
            var relocated = new List<string>();
            foreach (var nativeName in nativeNames)
            {
                var src = Path.Combine(librariesDir, nativeName);
                if (!File.Exists(src))
                {
                    continue;
                }

                var dest = Path.Combine(gamePath, nativeName);
                try
                {
                    File.Copy(src, dest, true);
                    File.Delete(src);
                    relocated.Add(nativeName);
                    Debug.Log($"{LogPrefix} relocated native {nativeName}: removed from Libraries/, copied to {dest}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"{LogPrefix} failed to relocate {nativeName}: {e.GetType().Name}: {e.Message}");
                }
            }

            if (relocated.Count > 0)
            {
                CleanManifestEntries(Path.Combine(installedModDir, "ModManifest.json"), relocated);
            }
        }

        private static void CleanManifestEntries(string manifestPath, List<string> nativeNames)
        {
            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning($"{LogPrefix} no ModManifest.json at {manifestPath}");
                return;
            }

            var json = File.ReadAllText(manifestPath);
            foreach (var nativeName in nativeNames)
            {
                var pattern = @"\s*\{\s*""path"":\s*""Libraries/" + Regex.Escape(nativeName) + @"""\s*,\s*""guid"":\s*""[^""]*""\s*\}\s*,?";
                json = Regex.Replace(json, pattern, "");
            }
            json = Regex.Replace(json, @",(\s*\])", "$1");
            File.WriteAllText(manifestPath, json);
            Debug.Log($"{LogPrefix} removed {nativeNames.Count} native entries from {manifestPath}");
        }
    }
}
