# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Statut du projet

Mod d'accessibilité pour **Core Keeper**, nommé **`CoreKeeperAccess`**.

- **Jalon 1 atteint** (mai 2026) : mod squelette, charge + annonce "Mod accessibilité chargé" via NVDA au démarrage.
- **Jalon 2 atteint** (mai 2026) : TTS complet menus du jeu (titres, options, sliders, descriptions, boutons icone-only). Patch unique → `RadicalMenuOption.OnSelected` + `OnSkimLeft/Right` + `RadicalMenu.Activate`. Couvre menu principal, options, sélection monde, création monde, sélection perso, création perso, choix classe, profession.
- **Système i18n du mod en place** : JSON dans `Assets/CoreKeeperAccess/Conf/Localization/<lang>.json`, chargé via `LoadedMod.GetFile(...)` au boot, helper `Strings.L(key)` côté mod. Langue détectée via `I2.Loc.LocalizationManager.CurrentLanguageCode`, rechargement auto au changement de langue.

État du repo :
- `05_findings_recherche_web.md` — recherche initiale Claude Web. Plusieurs claims **vérifiés** en session, d'autres restent à vérifier.
- `CoreKeeperModSDK/` — SDK officiel Pugstorm cloné, projet Unity 6. Contient le mod `Assets/CoreKeeperAccess/` + outillage auto `Assets/Editor/A11yAutomation.cs`.
- `_Examples_extracted/` — 10 exemples de mods extraits de `CoreKeeperModSDK/Assets/Examples.zip`, hors `Assets/` pour ne pas polluer Unity.
- `third_party/Tolk/` — binaires Tolk + wrapper C# + licences (source). Wrapper `Tolk.cs` copié dans `Assets/CoreKeeperAccess/`, natives copiées dans `Assets/CoreKeeperAccess/Plugins/x86_64/`.
- `decompiled/` — décompilation dnSpyEx ciblée des DLL du jeu (Pug.Other, Pug.Base, Pug.ControlMapping, Pug.Mods, PugMod.SDK.Runtime, Assembly-CSharp). Grep ici avant de patcher.
- `tools/fast-build.ps1` — déploiement express du mod **sans passer par Unity** (section dédiée).

## Workflow fast-build (PowerShell, par défaut)

**Workflow standard pour itérer sur le code du mod.** `tools/fast-build.ps1` copie en direct les `.cs` (→ `Scripts/`) et les `.json` de `Conf/` (→ `Conf/`) vers l'install Steam, **sans rien demander à Unity**. ModLoader recompile le code via Roslyn au démarrage (pattern confirmé : `Successfully compiled CoreKeeperAccess` dans Player.log). Beep `SystemSounds.Asterisk` succès, `Hand` échec. **Lance le jeu par défaut** (ferme d'abord l'instance en cours, car ModLoader recompile au démarrage) ; `-NoLaunch` pour déployer sans lancer.

Usage :
```
& "C:\Users\flame\Documents\core keeper\tools\fast-build.ps1"
```

Cycle de dev typique : edit code → fast-build (lance le jeu par défaut) → naviguer en jeu → quitter → inspecter `%USERPROFILE%\AppData\LocalLow\Pugstorm\Core Keeper\Player.log` si erreur. Temps : 1-2 s vs 30+ s avec Unity.

**Pré-requis** : build Unity complet **au moins une fois** (génère `Bundles/`, `ModManifest.json`, natives dans le dossier d'install). Après ça, fast-build suffit pour toute modif code/JSON, **y compris ajout/suppression de fichiers source**.

**Validation compile en amont (depuis 16 juin 2026)** : script lance `check-compile.ps1` avant déploiement. Syntax error C# → annule déploiement (exit 3), signalée **tout de suite** — plus besoin d'attendre Player.log. `-NoCheck` pour forcer.

**Mode MIROIR (par défaut, depuis 16 juin 2026)** : install mise en miroir exact des sources de la branche courante — copie tout, **supprime orphelins** (résidus autre branche), **resynchronise `ModManifest.json`** (backup `.bak` avant). Conséquence : ajouter/supprimer un `.cs`/`.json` **ne réclame plus de build Unity**, alternance entre branches sans rebuild. `-NoMirror` → comportement historique (avertit + stoppe sur fichier non déclaré, sans rien supprimer). Script **auto-localise** son `ModSource` depuis son propre emplacement → chaque worktree déploie sa propre feature sans `-ModSource`.

**`dev.flag`** : posé d'office à chaque déploiement (auto-load monde 1/perso 1 + skip logos studio) ; `-NoDev` le retire pour tester comportement release. Jamais présent dans l'artefact distribué.

**Build Unity nécessaire uniquement pour** :
- un **nouveau système ECS** (`SystemBase`/`[WorldSystemFilter]`) → fast-build ne produit pas les `.g.cs` générés. On évite donc l'ECS quand c'est possible.
- un **asset Unity** (prefab, ScriptableObject sérialisé, texture, son) → fast-build ne rebuild pas les `Bundles/`.
- la **première installation** d'un mod (avant d'avoir la structure de base).
- Ces cas se traitent **depuis le slot main uniquement** (jamais ck2/ck3 — voir fiche mémoire deploy-workflow).

## Workflow d'automatisation Unity (Editor inaccessible NVDA, fallback)

Éditeur Unity pas accessible NVDA. Contournement : `Assets/Editor/A11yAutomation.cs`, script editor `[InitializeOnLoad]` + `AssetPostprocessor`, piloté par `Assets/A11yAutomation.flag.json`. Trois actions :

- `update_sdk` : `PugMod.ImporterWindow.UpdateFromGamePath(...)` pour importer les ~200 DLL du jeu depuis l'install Steam.
- `create_mod` : `PugMod.ModBuilderWindow.CreateNewMod(modName)` pour créer la structure d'un nouveau mod.
- `build_install` : `PugMod.ModBuilder.BuildMod(...)` puis post-fix de relocation des natives.

**Quand utiliser Unity au lieu de fast-build** : ajout d'asset Unity, nouveau système ECS (fichiers `.g.cs` générés), premier build d'un nouveau mod. L'ajout/suppression d'un simple fichier source est désormais géré par le mode miroir de fast-build. **À faire depuis le slot main uniquement.**

**Pattern d'utilisation** : dépose le flag JSON, donne le focus à Unity (Alt+Tab), Unity recompile / détecte le flag, exécute l'action, supprime le flag. Loggé dans `%LOCALAPPDATA%\Unity\Editor\Editor.log` (chercher `[A11yAutomation]`).

**Important** : avant de citer un fait, vérifier qu'il est **confirmé** (sections ci-dessous précisent). Claims du fichier 05 non vérifiés restent suspects par défaut. Vérification via : décompilation DLL du jeu, lecture exemples livrés, doc officielle Pugstorm.

## Stack technique (confirmé)

- **Unity Editor 6000.0.59f2** — version que Hub installe via `Add project from disk` sur le SDK. README SDK indique 6000.0.58f2 mais obsolète, `ProjectSettings/ProjectVersion.txt` fait foi.
- Scripting backend Mono.
- Module Unity additionnel obligatoire : **Linux Build Support (Mono)** (sans ça, build impossible).
- **Harmony embarqué** dans le SDK : `CoreKeeperModSDK/Assets/Plugins/CoreKeeperModSDK/Harmony/` contient `0Harmony.dll`, `MonoMod.RuntimeDetour.dll`, `MonoMod.Utils.dll`. Harmony aussi présent dans le jeu.
- Gameplay en DOTS/ECS, rendu + UI en GameObject classique (claim hérité fichier 05, cohérent avec exemples, pas encore validé sur le code).
- DLL du jeu préfixées `Pug.*` — **confirmé** par inventaire de `CoreKeeper_Data/Managed/` (→ section Cartographie ci-dessous).

## Lifecycle d'un mod (confirmé)

Interface `PugMod.IMod` avec **5 méthodes** :
- `EarlyInit()` — appelée très tôt, avant le chargement du jeu
- `Init()` — jeu prêt
- `Shutdown()` — fermeture
- `ModObjectLoaded(UnityEngine.Object obj)` — callback quand un asset du mod est chargé
- `Update()` — appelée chaque frame

**Patches Harmony auto-appliqués** : `[HarmonyPatch(...)]` sur une classe suffit, SDK les applique automatiquement. Pas besoin d'instancier `Harmony` manuellement dans `EarlyInit()`. Désactivable via `disableHarmonyPatching` du manifest (ci-dessous).

## Format du manifest mod (confirmé)

Pas un `ModManifest.json` éditable à la main côté source — **ScriptableObject Unity sérialisé en YAML** (`Assets/<modName>.asset`). `ModManifest.json` généré par le ModBuilder au build, dans le mod installé. Champs clés :

- `guid` — identifiant unique du mod (auto-généré par le SDK).
- `name` — nom du mod.
- `requiredOn` — tag client/server **confirmé**. Enum `ModMetadata.ModExistsOn`, au moins `None` (= 0, client only, ce qu'on veut) et `ClientAndServer` (= 2). **Mod a11y client-side → garder à `0`**.
- `accessesExtraAssemblies` — `1` pour référencer les DLL du jeu (Pug.*, Assembly-CSharp, etc.). Obligatoire.
- `disableHarmonyPatching` — `0` par défaut (auto-apply) ; `1` désactive.
- **`skipSafetyChecks`** — **CRITIQUE**. `0` par défaut : ModLoader refuse tout `[DllImport]`, tout `System.Runtime.InteropServices`, tout `Marshal.*` à la compile. `1` : vérifications désactivées. **Obligatoire pour tout mod utilisant Tolk ou toute autre DLL native**. Côté mod.io, correspond probablement au tag "Script (Elevated Access)".
- `files` — liste des fichiers du mod (généré automatiquement au build).
- `buildBundles`, `buildLinux` — flags de build (cible Linux nécessaire pour publier).

**Sandbox runtime du ModLoader** : Core Keeper compile le C# du mod à l'exécution via RoslynCSharp, mode de sécurité activé par défaut. Erreur typique sans `skipSafetyChecks: 1` :

```
Assembly 'X' has failed code security verification.
Illegal PInvoke References = 'N'
Illegal usage of disallowed convention PInvoke targeting call site: ...
```

## Workflow build (confirmé via doc gitbook + exemples + bypass automation)

Workflow officiel passe par l'éditeur Unity (PugMod → Open Mod SDK Window → Mod Settings → Install Mod). **Inaccessible NVDA**, remplacé par `A11yAutomation.cs` qui appelle directement `ModBuilder.BuildMod`.

**Bug connu du ModBuilder pour les natives**. Dans `Packages/dev.pugstorm.mod/SDK/Editor/ModBuilder.cs:BuildLibraries()`, le builder copie **toutes les `*.dll`** (managed ET natives) dans `Libraries/` du mod installé. ModLoader essaie ensuite de charger chaque DLL de `Libraries/` comme assembly managed C# → `BadImageFormatException` pour les natives.

**Workaround** dans `A11yAutomation.PostBuildRelocateNatives` : après chaque build, déplace `Tolk.dll` + `nvdaControllerClient64.dll` de `<mod>/Libraries/` vers la racine de l'install Core Keeper (où le P/Invoke .NET les trouve par convention de recherche DLL Windows), retire les entrées correspondantes du `ModManifest.json`.

À terme : PR au SDK Pugstorm pour distinguer native/managed via PluginImporter, ou charger les natives manuellement via `LoadLibrary` avec chemin résolu.

## Contraintes architecturales

**Minimiser la surface de hook.** SDK casse régulièrement entre patches Core Keeper. Chaque méthode patchée = dette de maintenance.

**Multi : un seul artefact toggleable.** Matching multi compare les listes de mods chargés. Stratégie : mod publié unique, lit un flag local au démarrage. Si "off" → `EarlyInit()` ne charge rien, aucun patch installé. `requiredOn` du manifest probablement notre point d'ancrage côté SDK (à vérifier).

**Server-authoritative** (confirmé par `TeleportAfterEating`) : modifier le gameplay (déplacement, etc.) → passer par `QueueInputAction()` au serveur, jamais modifier l'état directement. UI / TTS / lecture de données côté client uniquement (cas principal mod a11y) : **pas de contrainte**.

**Tolk déclenche Elevated Access.** Toute DLL native (dont Tolk) impose le tag `Script (Elevated Access)` sur mod.io. Utilisateur final verra un warning à la souscription. À documenter clairement côté utilisateur.

**CoreLib : à manier avec prudence.** Si on l'utilise, certains modules forcent du matching multi via `CoreLib.ModEntityID.cfg` / `CoreLib.TilesetID.cfg`. Préférer s'en passer.

## Cartographie des DLL critiques (confirmée par décompilation)

Toutes les DLL du jeu sont dans `C:\Program Files (x86)\Steam\steamapps\common\Core Keeper\CoreKeeper_Data\Managed\`. Pointer dnSpy là pour décompiler. Repo a déjà la décompil de plusieurs DLL clés dans `decompiled/` (Pug.Other, Pug.Base, Pug.ControlMapping, Pug.Mods, PugMod.SDK.Runtime, Assembly-CSharp).

**Découvertes majeures (jalon 2)** :
- **`Assembly-CSharp.dll` n'est PAS le main assembly** — mini stub de 11 KB, juste post-processing graphique. Code gameplay et UI ailleurs.
- **`Pug.UI.dll` n'existe pas**. L'UI menu est dans `Pug.Other.dll`.
- **`Pug.Other.dll` (6 MB) est le main assembly réel** : gameplay, UI, menus, character customization, world generation hooks, etc. 1520+ fichiers décompilés.

Mapping par rôle :
- **`Pug.Other.dll`** — main code gameplay + UI. Les menus (RadicalMenu et descendants), `WorldSlot`, `CharacterCustomizationOption_Selection`, `PugText`, `Manager`, etc. Décompilé en `decompiled/Pug.Other/`.
- **`Pug.Base.dll`** — utilitaires de base (493 KB).
- **`Pug.ControlMapping.dll`** — couche au-dessus de Rewired pour mapper input → actions du jeu. Contient `ControlMappingManager`, `ControlMappingMenu`. Cible pour le remapping intelligent.
- **`Pug.Mods.dll`** — logique gameplay (Factions, Fishing, Talents, LootTable). PAS l'API mod.
- **`PugMod.SDK.Runtime.dll`** — interfaces SDK PugMod (IMod, IModLoader, ILocalization, LoadedMod, ModMetadata, API static).
- **`I2.dll`** — I2 Localization. `LocalizationManager.GetTranslation`, `CurrentLanguageCode`, `OnLocalizeEvent`.
- **`Rewired.dll` + `Rewired_Core.dll` + `Rewired_Windows.dll`** — système d'input bas niveau.
- **`Accessibility.dll`** — toujours à explorer. Pas encore investigué.

## Architecture des menus (confirmée jalon 2)

**Toute l'UI menu du jeu** repose sur `RadicalMenu` (abstract base) et ses options `RadicalMenuOption`. **27 types de menus** énumérés dans `RadicalMenu.MenuType` (PAUSE, OPTIONS, SELECT_WORLD, CHARACTER_CUSTOMIZATION, etc.). Tous les sous-menus héritent de `RadicalMenu`.

**Points d'accroche du TTS (notre patch)** :
- `RadicalMenuOption.OnSelected` (postfix) → annonce de l'option focalisée. Méthode terminal de toutes les voies de sélection (nav clavier/manette + appels directs comme `SetupSaveSlots`).
- `RadicalMenuOption.OnSkimLeft/OnSkimRight` (postfix, force=true) → annonce de la nouvelle valeur après un skim (sliders, toggles, sélecteurs).
- `RadicalMenu.Activate` (prefix + postfix) → reset du déduplicateur et annonce "Titre. Option courante" en un seul appel Tolk. Le prefix met `SuppressDuringActivate=true` pour étouffer le OnSelected appelé pendant l'init du menu.

**Filtrage critique** :
- `IsSelected()` check dans le patch OnSelected — sans ça, menus appellent OnSelected en cascade sur plusieurs options à l'init (init visuels), annonce inaudible (interrupt=true écrase tout). Check ne laisse passer que la "vraie" sélection courante.
- Déduplication par `GetInstanceID()` (pas par texte) — sinon slots multiples avec même label se chevauchent silencieusement.

**Construction du label TTS** :
- `labelText` + `valueText` (champs publics standards de `RadicalMenuOption`)
- + numéro de slot via `option.GetComponentInParent<WorldSlot>().number` (pour les écrans "Sélectionner un monde")
- + tous les `PugText` enfants Unity (`GetComponentsInChildren<PugText>(false)`)
- + tous les `PugText` champs déclarés via reflection (pour capter les PugText non-enfants comme `typeText`, `roleTitleText`). Skip si `gameObject.activeInHierarchy == false` (sinon vestiges du préfab pour un slot vide), skip si nom de champ contient "shadow".
- + fallback i18n hardcodé pour les options icone-only (`WorldSlotMoreOption`, `WorldSlotDeleteOption`, `SaveSlotDeleteOption`).

**Résolution du texte** : `PugText.ProcessText(text.GetText())` au lieu d'appeler `LocalizationManager.GetTranslation` directement. ProcessText gère **format fields** (substitution `{0}` `{1}` etc.) en plus de la localisation. Skip si résultat contient encore `{N}` (format non substitué) ou commence par "missing:" (clé I2 inexistante).

## API PugMod (confirmée)

- `PugMod.API.ModLoader` (`IModLoader`) — **n'a pas `GetMod(name)`** côté interface SDK (seulement côté impl du jeu). Itérer manuellement sur `LoadedMods` pour trouver un mod par nom.
- `PugMod.API.Localization.GetLocalizedTerm(string term)` — résout une clé I2 via `LocalizationManager.GetTranslation` du jeu. API stable.
- `LoadedMod.GetFile(string path)` — lit un fichier du dossier d'install du mod en `byte[]`. Utilisé par `Strings.Load()` pour charger nos JSON de traduction.

## Tolk (TTS / screen reader) — décision et conformité licence

**Décision** : Tolk complet (abstraction screen reader standard). Code du mod reste sous licence permissive (MIT prévu). Tolk intégré en **DLL séparée non statiquement liée** → respecte LGPL sans contaminer le code du mod.

**Fichiers présents dans `third_party/Tolk/`** :
- `Tolk.dll` (v1.0.0.0, ~120 KB) — la DLL native principale
- `nvdaControllerClient64.dll` (~150 KB) — client NVDA, chargé dynamiquement par Tolk
- `Tolk.cs` — wrapper C# officiel (namespace `DavyKager.Tolk`), à inclure dans le code du mod
- `LICENSE.txt` — LGPL-3.0 (Tolk)
- `LICENSE-NVDA.txt` — LGPL-2.1 (nvdaControllerClient)

**API Tolk côté C#** (vu dans `Tolk.cs`) :
- `Tolk.Load()` — à appeler avant tout (dans `EarlyInit` ou `Init`)
- `Tolk.Output(string text, bool interrupt = false)` — annonce TTS (méthode principale)
- `Tolk.Speak(...)`, `Tolk.Braille(...)`, `Tolk.Silence()`, `Tolk.IsSpeaking()`, `Tolk.DetectScreenReader()`
- `Tolk.Unload()` — à appeler dans `Shutdown`

**Obligations LGPL à respecter dans la distribution du mod** :
- Distribuer `LICENSE.txt` et `LICENSE-NVDA.txt` avec le mod
- Mentionner Tolk dans la doc utilisateur (attribution + lien vers https://github.com/dkager/tolk)
- Garder Tolk.dll et nvdaControllerClient64.dll en DLL séparées, jamais liées statiquement
- Permettre à l'utilisateur de remplacer ces DLL par une version modifiée (en pratique automatique vu qu'elles sont séparées)

## Exemples de mods livrés avec le SDK

Décompressés dans `_Examples_extracted/Examples/` :
- **`BurstDisable`** + **`TeleportAfterEating`** — les plus minimaux, bon point de départ pour comprendre la structure (cs + .asmdef + .asset manifest)
- `EnemyExample`, `ItemExample`, `WorkbenchExample` — patterns de contenu
- `SystemExample` — pattern ECS
- `RpcExample` — communication réseau
- `ModCommandsExample` — console + commandes custom
- `SpawnStuffFromTiles` — interaction avec les tiles

## Mods de référence externes

| Pattern | Mod | Auteur |
|---|---|---|
| Overlay visuel sur entities ECS | Enemy Health Bars | moorowl |
| Client-side-only fonctionnel en multi | InventoryKeeper | orionsync |
| Annonce d'événements de jeu | Auto Fish | xiaoye97 |
| Modification d'UI de placement | Placement Plus | limoka8 |

Sources GitHub linkées depuis les pages mod.io / Thunderstore.

## Ressources

- SDK officiel : https://github.com/Pugstorm/CoreKeeperModSDK
- Wiki modding (GitBook) : https://core-keeper-modding.gitbook.io/modding-wiki
  - Endpoint d'interrogation IA : `GET https://core-keeper-modding.gitbook.io/modding-wiki/home.md?ask=<question>` — utilisable via WebFetch
- Repo docs communautaire : https://github.com/CoreKeeperMods/Core-Keeper-Docs
- Discord Core Keeper : channels `#mod-creators` et `#mod-help`

## Build & tests

**Jalon 1 atteint** (mai 2026). Le mod `CoreKeeperAccess` se construit via :

1. Déposer `Assets/A11yAutomation.flag.json` avec `{"action":"build_install","modName":"CoreKeeperAccess","gamePath":"<chemin Core Keeper>"}`
2. Donner le focus à Unity Editor
3. Le script auto-exécute : ConfigureNativePluginsForMod → BuildMod → PostBuildRelocateNatives (déplace natives + nettoie manifest)
4. Le mod installé est dans `<gamePath>/CoreKeeper_Data/StreamingAssets/Mods/CoreKeeperAccess/`
5. Lancer Core Keeper → entendre "Mod accessibilité chargé" via NVDA au menu principal

Logs : `%LOCALAPPDATA%\Unity\Editor\Editor.log` (côté SDK) et `%LOCALAPPDATA%Low\Pugstorm\Core Keeper\Player.log` (côté jeu, pour diagnostiquer les erreurs de chargement mod).
