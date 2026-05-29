# Findings recherche web — préparation mod Core Keeper

*Mai 2026. Sources vérifiées par recherche web. Ce fichier remplace `05_architecture_mod_core_keeper.md` (retiré : trop spéculatif).*

## À quoi sert ce document

Recueillir les infos factuelles trouvées en recherche web sur le modding Core Keeper. Servira de contexte initial pour la session Claude Code de scoping technique (qui, elle, aura accès aux DLL décompilées du jeu).

**Ce qui est dans ce fichier** : seulement ce qui est sourcé sur des pages officielles Pugstorm, patch notes Steam, doc communautaire de référence, ou exemples de mods vérifiables.

**Ce qui n'y est PAS** : noms de classes du jeu, méthodes à patcher, estimations de durée. Ces choses se découvrent en lisant le code, pas en faisant de la recherche web.

---

## 1. Stack technique réelle

Core Keeper est passé d'**IL2CPP à Mono** dans le patch v0.6.3 (20 septembre 2023), en même temps que l'introduction du Mod SDK et de l'intégration mod.io. Le changement est confirmé sur le wiki Fandom officiel et sur les patch notes Steam.

**Implication pour le profil dev** : la note dans `01_profil_developpeur.md` qui catégorise Core Keeper comme "Unity 6 + IL2CPP" est obsolète. La stack actuelle est :

- Unity 6 (version Editor exacte à lire dans le README du SDK au moment du clone — j'ai vu `6000.0.58f2` et `6000.0.59f2` mentionnées à différents endroits, donc faire confiance au README plutôt qu'à ce document)
- Scripting backend **Mono**
- Harmony **embarqué avec le jeu** (confirmé dans le wiki modding, section Technologies and Tools)
- DOTS/ECS pour le gameplay runtime (joueurs, ennemis, placeables, tiles sont des entities ECS, pas des GameObjects)
- GameObject classique conservé pour le rendu graphique et l'UI

Source patch notes : https://store.steampowered.com/news/app/1621690/view/3716088977006375507

---

## 2. Conventions de code à connaître

D'après le wiki modding (page "Technologies and Tools" + sections de getting started) :

- Les DLLs du jeu sont préfixées `Pug.*` (pas `Assembly-CSharp.dll` comme dans la convention Unity standard). À chercher dans le dossier d'install Steam de Core Keeper.
- Les DLLs nommées au moins une fois dans la doc : `Pug.Core`, `Pug.Other`, `Pug.ECS.Components`, `PugWorldGen`.
- Decompilation possible avec **dnSpy** directement, vu que c'est du Mono. Pas besoin de Cpp2Il / Il2CppDumper.
- Interface mod : `IMod`. Au moins une méthode `EarlyInit()` (avant chargement du jeu, faire les patches Harmony ici) et `Init()` (jeu prêt). Info issue indirectement de la doc CoreLib qui dit *"Make sure to call `CoreLibMod.LoadModules(...)` in your mod `EarlyInit()` function"* — source secondaire, à confirmer en lisant le SDK officiel. D'autres méthodes du lifecycle peuvent exister, à vérifier dans le SDK.
- Manifest mod : fichier `ModManifest.json` à la racine du dossier mod.
- Dossier d'install des mods côté joueur : `CoreKeeper_Data/StreamingAssets/Mods/<nom_du_mod>/`.

---

## 3. Le matching multi et le tag "Application Type"

**Le problème par défaut** : en multi, le serveur compare les listes de mods chargés. Tout mismatch déclenche l'erreur `"Game version mismatch"`. Source : doc communautaire `for-multiplayer.md`.

**L'échappatoire officielle** : la doc reconnaît un tag `Application Type` au manifest mod. Citation directe :

> *Some mods only need to be installed client side (or server side), refer to the mod's "Application Type" tags.*

Source : https://github.com/CoreKeeperMods/Core-Keeper-Docs/blob/main/playing-with-mods/installing-mods/for-multiplayer.md

**Précédent prouvé** : le mod `InventoryKeeper` par orionsync (disponible sur Thunderstore, archivé) est explicitement marqué "This mod is Client Side Only" et fonctionne. Il modifie le comportement perçu côté client (garder l'inventaire à la mort) sans toucher au serveur.

**Stratégie retenue : un seul artefact, avec toggle on/off, marqué Client Side Only en ceinture-et-bretelles**

Le mod est publié une seule fois, comme un seul fichier. Au démarrage, il lit un flag de config local (par exemple un fichier `enabled.flag` dans son dossier, ou un `config.json` avec une clé `enabled`). Si le flag dit "off", le `EarlyInit()` ne charge aucun module, n'installe aucun patch Harmony, ne fait rien. Si le flag dit "on", tout démarre normalement.

Côté pratique :

- Chez moi : flag à "on", tout tourne
- Chez ma femme : elle installe le même mod (même fichier), flag à "off", le mod existe pour la signature serveur mais n'affecte rien
- Le mod est aussi marqué `Application Type: Client Side Only` au manifest mod.io : ceinture-et-bretelles. Si le tag fait son office, ma femme n'a même pas besoin d'installer ; si pour une raison quelconque il ne suffit pas pour certains hooks (ECS, etc.), le toggle off prend le relais

**Avantages** :

- Un seul artefact à maintenir et publier
- Garantie absolue de signature identique des deux côtés vu que c'est littéralement le même fichier
- Couvre tous les cas : que `Client Side Only` marche, qu'il échoue partiellement, ou qu'il échoue complètement
- Aucune ligne de code n'est dupliquée entre une version "principale" et une version "stub"
- À chaque update du mod, ma femme met à jour son fichier (idéalement automatisé via mod.io) et le matching reste valide

**Test à faire quand même** (pas un go/no-go bloquant, juste de la validation) :

- Vérifier que le mod, avec flag à off, ne fait absolument rien au démarrage (pas de hook actif, pas de query ECS, pas de Tolk chargé)
- Vérifier que la connexion multi tient avec moi flag-on et ma femme flag-off
- Tester par curiosité si en plus le tag `Client Side Only` permet à ma femme de ne rien installer du tout

Ce test peut se faire **après** avoir avancé sur le code du mod, pas avant. L'architecture ne dépend plus de la réponse.

---

## 4. Le tag "Script (Elevated Access)" — nouveau et obligatoire pour TTS

Découverte qui n'apparaît pas dans les fichiers projet existants : Pugstorm a introduit une catégorie **"Access Type"** sur mod.io. Les mods qui accèdent à des ressources hors du jeu (fichiers utilisateur, internet) doivent porter le tag `Script (Elevated Access)`.

Citation officielle (annonce Pugstorm relayée sur la doc communautaire) :

> *When installed, these mods have increased access to resources outside of the game such as user files and the internet. [...] Any mod that needs to run with elevated access will require the Access Type "Script (Elevated Access)". Users will also experience a warning pop-up that appears when they subscribe to elevated-access mods.*

Source : https://core-keeper-modding.gitbook.io/modding-wiki/concepts/elevated-access

**Implication pour le mod a11y** : Tolk est une DLL native qui interagit avec NVDA/JAWS/SAPI au niveau système. Le mod aura donc besoin de **Elevated Access**. Pas un blocage, mais à savoir :

- Le tag doit être ajouté au manifest et sur la page mod.io
- L'utilisateur verra un avertissement au moment de souscrire au mod
- La documentation utilisateur devra expliquer pourquoi (interfaçage screen reader) pour rassurer

**À vérifier en phase de R&D** : est-ce que charger une DLL native déclenche automatiquement une exigence d'Elevated Access, ou est-ce que ça dépend du runtime (P/Invoke vs autre) ? À demander sur le Discord `#mod-creators` au besoin.

Note connexe vue dans le même patch : Pugstorm a aussi ajouté un tag `Asset` pour les mods qui ne contiennent que des assets non-script. Pas notre cas mais bon à savoir.

---

## 5. Stabilité du SDK et inertie de maintenance

**Le SDK casse souvent entre versions**. La doc officielle dit explicitement :

> *Unfortunately, arbitrary things breaking between releases seems to be a very common occurrence in Core Keeper modding. Instead of trying to update your modding SDK, it is best to clone and set up a new one from scratch every time an update is released.*

Source : https://github.com/CoreKeeperMods/Core-Keeper-Docs/blob/main/creating-mods/updating-your-modding-sdk.md

**Implication architecture** : minimiser la surface de hook dans le code du mod. Plus on patche de méthodes internes du jeu, plus on aura à réparer à chaque patch Core Keeper. À garder en tête au moment du scoping.

Le patch v1.2.0.5 de mars 2026 mentionne : *"Added a feature to detect more startup issues which may be caused by mods. The game will now try to restart without mods if possible."* — donc le jeu fait maintenant un effort pour ne pas crasher complètement à cause d'un mod buggé, ce qui aide en phase de dev.

---

## 6. CoreLib — la lib communautaire

CoreLib est une bibliothèque communautaire (CoreMods/CoreLib sur Thunderstore et mod.io) qui ajoute des modules réutilisables au-dessus du SDK officiel : modules `Entity`, `Component`, `System`, `Audio`, `JsonLoader`, `ChatCommands`, `NativeTranspiler`. Très utilisée par les mods de référence.

**Point d'attention pour le multi** : CoreLib utilise des fichiers de config `CoreLib.ModEntityID.cfg` et `CoreLib.TilesetID.cfg`. La doc CoreLib dit :

> *If you are playing with friends MAKE SURE to sync your CoreLib.ModEntityID.cfg and CoreLib.TilesetID.cfg config files. If anything inside does not match you WILL encounter issues connecting.*

Source : https://core-keeper.thunderstore.io/package/CoreMods/CoreLib/

**Implication** : si on utilise CoreLib, le design "client-side-only sans rien chez la femme" peut être compromis selon les modules CoreLib activés. À vérifier au cas par cas dans la session de scoping technique. Si possible, partir sans CoreLib et n'en réimporter que les modules dont on a vraiment besoin et qui ne déclenchent pas de matching.

---

## 7. Mods de référence à étudier en priorité

Quatre mods existants qui couvrent les patterns dont on aura besoin :

| Mod | Auteur | Pattern utile à lire |
|---|---|---|
| `Enemy Health Bars` | moorowl | Overlay visuel sur entities (proche du module Visual phase 3) |
| `InventoryKeeper` | orionsync | Client-side-only fonctionnel (validation du design multi) |
| `Auto Fish` | xiaoye97 | Annonce d'événements de jeu (proche du module TTS EventAnnouncer) |
| `Placement Plus` | limoka8 | Modification d'UI de placement (interaction avec l'UI manager du jeu) |

Tous accessibles sur https://mod.io/g/corekeeper ou https://core-keeper.thunderstore.io/. Le code source GitHub de chacun est généralement linké sur leur page mod.io.

---

## 8. Outillage à installer pour la R&D

À installer si pas déjà fait :

- **Unity Hub** : https://unity.com/download
- **Unity Editor** version Unity 6 exacte indiquée dans le README du SDK
- **dnSpy** ou **ILSpy** pour décompiler les DLLs Mono : https://github.com/dnSpy/dnSpy
- **IDE C#** : VS ou Rider (Rider recommandé par la doc communautaire)

Setup du projet :

- **Clone du SDK officiel** : `git clone https://github.com/Pugstorm/CoreKeeperModSDK`
- **UnityExplorer DOTS** pour inspecter les entities ECS en runtime : https://mod.io/g/corekeeper/r/installing-unity-explorer

---

## 9. Ressources documentaires consolidées

### Officiel Pugstorm
- SDK GitHub : https://github.com/Pugstorm/CoreKeeperModSDK
- Doc officielle (nécessite JavaScript pour s'afficher) : https://mod.io/g/corekeeper/r/core-keeper-mod-sdk-introduction
- Hub mods : https://mod.io/g/corekeeper

### Documentation communautaire (la plus utile en pratique)
- Repo docs : https://github.com/CoreKeeperMods/Core-Keeper-Docs
- Wiki GitBook : https://core-keeper-modding.gitbook.io/modding-wiki
  - **Endpoint d'interrogation IA documenté** : `GET https://core-keeper-modding.gitbook.io/modding-wiki/home.md?ask=<question>` — la doc dit explicitement qu'un agent peut interroger en langage naturel. À tenter dans Claude Code.

### Communauté humaine
- Discord officiel Core Keeper, channels `#mod-creators` et `#mod-help`
- Channel à rejoindre **avant** de commencer à coder, pour annoncer le projet et avoir des relais en cas de blocage

---

## 10. Ce que ce fichier NE dit PAS (et qui sera à découvrir en session Claude Code)

À ne pas inventer en attendant. Cette liste sera le point de départ de la prochaine session de scoping technique :

- Le nom exact de la classe qui gère l'input clavier/souris/manette dans Core Keeper
- Le nom exact de la classe ou du système ECS qui gère le déplacement du joueur
- Si un pathfinding réutilisable existe nativement (pour le clic-pour-bouger)
- Comment lire l'entity du joueur et ses composants (HP, inventaire, position)
- Comment requêter les entities ennemis dans un rayon donné
- Comment lire le contenu d'une tile à partir de coordonnées
- Comment injecter un GameObject HUD additionnel
- Quels événements le jeu expose pour le démarrage de combat boss
- Quel anti-cheat éventuel (mentionné par un hébergeur tiers, non confirmé officiellement)
- Si le tag `Application Type: Client Side Only` couvre tous nos hooks ECS sans desync (à tester par curiosité, mais l'architecture avec toggle on/off ne dépend plus de la réponse)

Tout ceci se découvre en décompilant `Pug.Core.dll` et consorts, et en lisant le code de mods de référence. Pas par recherche web.
