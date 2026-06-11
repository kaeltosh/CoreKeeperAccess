# CoreKeeperAccess — mod d'accessibilité pour Core Keeper

*This page is also available in English: [README.md](README.md).*

Mod qui rend **Core Keeper** jouable par des joueurs **aveugles** : tout passe par la synthèse vocale du lecteur d'écran (NVDA) et par un feedback sonore spatialisé. Menus, inventaire, artisanat, exploration, combat, téléportation — l'objectif est de jouer au jeu complet, en autonomie, à la manette.

**État : alpha.** Le mod est en test actif. Public visé pour cette phase : des testeurs à l'aise avec un clone de dépôt GitHub et une copie de fichiers.

## Philosophie

- **Égalité avec un joueur voyant, pas assistance.** Le mod révèle ce qu'un voyant perçoit (l'environnement, les menaces, les indices), il ne joue pas à votre place : pas de pathfinding magique, pas de spoilers. Les énigmes du jeu restent à résoudre.
- **Information par le son d'abord** : sons spatialisés (panoramique gauche/droite, hauteur grave/aiguë), la parole pour ce qui se nomme.
- **Client uniquement** : le mod lit le jeu et simule des entrées natives, il ne modifie pas les règles du jeu.

## Prérequis

- **Core Keeper** (Steam, Windows).
- **NVDA** lancé avant le jeu. (Le TTS passe par la bibliothèque Tolk ; NVDA est le seul lecteur testé, un repli SAPI est théoriquement possible.)
- **Une manette.** Le mod est conçu et testé manette (DualSense ; une manette Xbox devrait fonctionner, les boutons sont nommés ici façon PlayStation : Croix = A, Rond = B, Carré = X, Triangle = Y).
- Un clavier pour la saisie des noms (monde, personnage).
- Annonces du mod disponibles en **français** et **anglais** (suit la langue du jeu).

## Installation

### Avec le script (recommandé)

1. Cloner ou télécharger ce dépôt.
2. Ouvrir une invite de commandes ou PowerShell **dans le dossier du dépôt** et lancer :

```
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

3. Lancer NVDA, puis le jeu. Au menu principal vous devez entendre : « Mod accessibilité chargé », suivi de la version (par exemple « alpha 1, build 51 »).

Notes :
- Le `-ExecutionPolicy Bypass` est nécessaire : par défaut Windows refuse d'exécuter les scripts PowerShell téléchargés. Il ne vaut que pour cette commande.
- Si le jeu n'est pas à l'emplacement Steam par défaut, ajouter `-GamePath "<chemin de Core Keeper>"` en fin de commande.
- En cas d'erreur « accès refusé » (jeu sous Program Files avec permissions strictes), relancer la même commande depuis une console ouverte **en administrateur**.
- Pour mettre à jour après un `git pull`, relancer simplement le script (jeu fermé).

### Installation manuelle (alternative)

1. Copier le dossier `dist/CoreKeeperAccess` dans le dossier des mods du jeu :
   `<Core Keeper>/CoreKeeper_Data/StreamingAssets/Mods/`
   (chemin Steam typique : `C:\Program Files (x86)\Steam\steamapps\common\Core Keeper`).
2. Copier les deux DLL de `dist/natives` (`Tolk.dll` et `nvdaControllerClient64.dll`) **à la racine du jeu**, à côté de `CoreKeeper.exe`.

## Première partie : difficulté recommandée

Choisissez le mode **Décontracté** (Casual) **pour le personnage ET pour le monde**. La raison est importante : dans les autres modes, mourir fait tomber votre inventaire sur le lieu du décès, et le mod n'offre pas encore d'assistance pour y retourner — vos objets seraient très difficiles à retrouver. En décontracté, vous gardez tout à la mort.

**Désinstallation** : supprimer le dossier `Mods/CoreKeeperAccess` et les deux DLL de la racine du jeu. Si vous retirez le mod, repassez par « contrôles par défaut » dans les options du jeu pour restaurer le bouton carte sur Triangle (le mod le réquisitionne, voir plus bas).

## Ce que le mod couvre aujourd'hui

- **Tous les menus** : titres, options, sliders, sélection et création de monde/personnage, lus à la navigation.
- **Saisie de nom** : entrée et sortie du mode édition annoncées, contenu lu, validation à la Croix.
- **Cinématiques** d'intro et de fin : texte lu slide par slide, skip annoncé.
- **Inventaire et artisanat** : navigation par sections, recettes avec matériaux manquants, fiche de stats, talents, âmes, onglets.
- **Exploration** : curseur de tuile sonifié, prospection de minerai, annonce des objets posés et des interactions à portée, messages flottants du jeu.
- **Combat** : canne laser de repérage, sentinelle d'aggro (bips quand un monstre vous attaque).
- **Téléportation et carte** : relais navigables en liste (direction, distance, biome), points d'intérêt.

## Guide des commandes

### Les contrôles natifs du jeu (conservés)

Le mod n'intercepte que ce qui est cité plus bas (Triangle, et le D-pad plus les bumpers quand l'inventaire est ouvert). Tout le reste est le jeu d'origine :

En jeu, dans le monde :

- **Stick gauche** : se déplacer.
- **Stick droit** : viser, le personnage s'oriente (le mod y greffe la canne laser, voir plus bas).
- **RT** : utiliser l'objet en main — attaquer, miner en continu, pêcher…
- **LT** : interaction secondaire — poser l'objet en main, creuser à la pelle.
- **Croix** : interagir avec ce qui est devant soi ; pivoter l'objet en cours de placement.
- **Rond** : utiliser l'objet de la main secondaire.
- **LB / RB** : objet précédent / suivant de la barre rapide (le mod annonce l'objet en main).
- **L3** : sortir/ranger la torche (échange rapide). **R3** : courir plus vite.
- **Carré** : ouvrir et fermer l'inventaire. **Start** : pause.
- **Instrument de musique en main** : presque tous les boutons jouent des notes, Triangle compris (seul cas où le mod lui laisse son rôle natif).

Inventaire ouvert, conservés tels quels : Croix (prendre/poser, tout ramasser), RT (déplacement rapide), LT (lâcher), Rond (fermer). En revanche le D-pad et les bumpers natifs (tri, empilage, pages de barre rapide, ramasser la moitié) sont réquisitionnés pour la navigation — leurs fonctions sont relogées sur la roue d'actions, voir la section inventaire.

### La touche access : Triangle

Triangle est réquisitionné par le mod comme **modificateur d'accessibilité** (son action native « ouvrir la carte » est relogée, voir double-tap). Tant que Triangle est tenu, le D-pad déclenche des commandes :

- **Triangle + haut** : détails contextuels sur l'élément courant (case du curseur, destination sur la carte, coût de réparation…).
- **Triangle + bas** : hors inventaire, vie / faim / mana / barrière. Inventaire ouvert : transférer l'objet sélectionné.
- **Triangle + droite** : hors inventaire, position du personnage. Station de réparation ouverte : réparer l'objet sélectionné.
- **Triangle + gauche** : hors inventaire, prospection — direction et distance du filon de minerai le plus proche, avec un ding positionnel. Station ouverte : tout recycler.
- **Triangle + L1** : ping sonar — une photo sonore de ce qui est notable autour de toi (rayon de 12 cases) : un bip par cible, du plus proche au plus lointain, avec trois timbres (hostile, créature paisible, trouvaille). « Rien autour » si c'est vide. Tant que Triangle est tenu, L1 ne change pas de slot de barre rapide.
- **Double-tap Triangle** : ouvrir la carte n'importe où (catégorie points d'intérêt).

Un combo hors contexte ne dit rien : s'il est muet, c'est qu'il n'a pas de sens ici.

### Menus

- Navigation native au D-pad, tout est lu. Gauche/droite ajuste sliders et sélecteurs.
- **Champ de nom** (monde, personnage) : l'ouverture annonce « Édition » et le contenu. Tapez au clavier. **Croix = valider**, Rond ou Échap = annuler.
- **Cinématique** : le texte se lit tout seul. **Maintenir Croix une seconde = passer.**

### Inventaire (fenêtres ouvertes)

- **LB / RB** : section précédente / suivante (barre rapide, sac, équipement, artisanat, coffre, statistiques…).
- **D-pad** : se déplacer dans la section.
- **Croix** : prendre / poser, activer un onglet, **fabriquer** la recette sélectionnée (le résultat arrive « en main »).
- **RT** : déplacement rapide d'objet. **LT** : lâcher.
- **Roue d'actions au stick gauche** : pousser le stick vers un secteur (l'action est annoncée), **clic R3 = exécuter**. Actions : trier, empiler vite, ramasser la moitié, page de barre rapide suivante/précédente, jeter à la poubelle.

### Monde (hors fenêtres)

Deux outils complémentaires pour percevoir l'espace, et ils parlent la même langue sonore (une même case produit le même son dans les deux) :

- **Le curseur de tuile**, c'est votre main : il tâte le terrain case par case autour de vous, nomme ce qu'il touche, et c'est aussi par lui qu'on agit (miner, poser, se déplacer).
- **La canne laser**, c'est votre canne blanche longue portée : elle pointe dans la direction du stick droit et vous dit ce qu'il y a droit devant — le premier obstacle, et les menaces sur le chemin.

- **Curseur de tuile au D-pad** : il se détache du personnage et s'inspecte case par case, avec un son par déplacement (panoramique = gauche/droite, hauteur = haut/bas). Bouger au stick gauche le recolle au personnage.
  - Sons du curseur : tick discret = case libre ; son de matériau = mur ou bloc ; ding = minerai dans le mur ; petit marqueur aigu en plus = objet interactif ; plop = trou ; éclaboussure = eau. « Mur scellé » = indestructible, n'insistez pas.
- **Croix sur la case du curseur** : miner (mur), interagir (objet), ou s'y déplacer en ligne droite (case vide).
- **LT** : poser l'objet en main sur la case du curseur (creuser, si une pelle est équipée).
- **Canne laser au stick droit** : un faisceau balaye dans la direction du stick, joue le son de la première case bloquante (le « mur d'en face ») et signale les ennemis sur le trajet par un bip positionnel plus leur nom.
- **Sentinelle d'aggro** : automatique. Des bips en file = autant de monstres en train de vous attaquer.
- Annonces automatiques : objet en main au changement de slot, « interaction disponible » quand un objet utilisable est à portée, messages flottants du jeu (tutoriels, « trop dur », besoin d'énergie…), ramassages.

### Station de réparation et de recyclage

La station se fabrique à l'établi (bois + barres de cuivre) et s'ouvre en interagissant. Ses six emplacements apparaissent comme une section d'inventaire normale (bumpers). Ignorez ses boutons visuels : tout passe par la touche access, sur l'objet sélectionné :

- **Triangle + droite** : réparer l'objet sélectionné — fonctionne sur n'importe quel slot affiché (sac, barre rapide, équipement).
- **Triangle + gauche** : tout recycler (le contenu déposé dans la station), contre des pièces détachées et une partie des matériaux.
- **Triangle + haut** : détails de l'objet, enrichis du coût de réparation et du gain de recyclage.
- **Triangle + bas** : transférer l'objet sélectionné, comme dans tout inventaire.

Ces commandes sont muettes si la station n'est pas ouverte.

### Carte et téléportation

Interagir avec un relais ancien (ou double-tap Triangle n'importe où) ouvre la carte accessible :

- **D-pad haut / bas** : parcourir la liste (relais triés du centre du monde vers l'extérieur, numéros stables — le relais 1 est le Core).
- **LB / RB** : basculer entre **destinations** (téléportables) et **points d'intérêt** (boss scannés, marqueurs).
- **Croix** : se téléporter à la destination sélectionnée.
- **Triangle + haut** : détails (coordonnées, biome, cap en degrés, distance).

## Pour les testeurs

- **Consultez [KNOWN_ISSUES.fr.md](KNOWN_ISSUES.fr.md) avant de signaler** — bugs connus, limites actuelles et comportements voulus y sont listés.
- La **version et le numéro de build** sont annoncés au démarrage — donnez les deux dans tout rapport. Les nouveautés de chaque version sont dans le [journal des versions](CHANGELOG.fr.md).
- Le journal du jeu est dans `%USERPROFILE%\AppData\LocalLow\Pugstorm\Core Keeper\Player.log`. Tout ce que le mod prononce y est tracé avec le préfixe `[A11yTTS]` ; joignez ce fichier aux rapports de bug.
- **F9** (clavier) : mode diagnostic manette — annonce chaque bouton/axe pressé avec son identifiant. Pratique pour signaler un problème de mapping.
- Multijoueur : non testé à ce stade. Le mod est client-side ; jouez en solo pour les tests.
- Rapports : ouvrez une issue GitHub avec le build, ce que vous faisiez, ce que vous attendiez, ce qui s'est passé, et le Player.log.

## Comment ça marche (pour les curieux)

Le mod utilise le système de mod officiel de Core Keeper (PugMod) : le code C# est recompilé par le jeu au lancement, les hooks passent par Harmony, le TTS par la bibliothèque Tolk qui parle à NVDA. Aucun fichier du jeu n'est modifié. Le mod demande l'accès « élevé » du ModLoader (`skipSafetyChecks`) car Tolk est une DLL native — c'est le prix du TTS.

## Licences et crédits

- **Code du mod** : licence MIT (fichier `LICENSE`).
- **[Tolk](https://github.com/dkager/tolk)** de Davy Kager : LGPL-3.0 (`third_party/Tolk/LICENSE.txt`).
- **nvdaControllerClient** (NV Access) : LGPL-2.1 (`third_party/Tolk/LICENSE-NVDA.txt`).
- Les deux DLL ci-dessus sont distribuées telles quelles, en fichiers séparés : vous pouvez les remplacer par vos propres versions.
- Core Keeper est un jeu de Pugstorm, édité par Fireshine Games. Ce mod n'est pas affilié.
