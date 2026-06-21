# CoreKeeperAccess — mod d'accessibilité pour Core Keeper

*This page is also available in English: [README.md](README.md).*

Mod qui rend **Core Keeper** jouable par des joueurs **aveugles** : tout passe par la synthèse vocale du lecteur d'écran (NVDA) et par un feedback sonore spatialisé. Menus, inventaire, artisanat, exploration, combat, téléportation — l'objectif est de jouer au jeu complet, en autonomie, à la manette.

**Version 1.0, bêta ouverte.** Le mod est encore en test actif, mais il est ouvert à tous : l'installation se fait par **double-clic**, sans dépôt à cloner ni fichiers à copier à la main.

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

### Double-clic (recommandé)

1. Télécharger le zip de la version depuis la page **[Releases](https://github.com/kaeltosh/CoreKeeperAccess/releases)**.
2. L'extraire n'importe où.
3. **Double-cliquer sur `Installer.cmd`.** C'est tout — pas de ligne de commande, aucun chemin à taper. Votre installation Steam de Core Keeper est trouvée automatiquement, sur n'importe quel disque.
4. Une fenêtre s'ouvre, indique ce qui a été fait, et attend une touche pour que votre lecteur d'écran lise le résultat.
5. Lancer NVDA, puis le jeu. Au menu principal vous devez entendre : « Mod accessibilité chargé », suivi de la version (par exemple « 1.0 beta, build 1 »).

Notes :
- **Avertissement au 1er lancement.** Windows peut signaler que le fichier « provient d'un autre ordinateur » (Mark of the Web / SmartScreen). C'est normal pour tout script téléchargé. Choisir « Informations complémentaires » → « Exécuter quand même », ou clic droit sur `Installer.cmd` → Propriétés → cocher « Débloquer ».
- **« Accès refusé » ?** Si l'installeur signale une écriture refusée (jeu sous `Program Files` avec permissions strictes), clic droit sur `Installer.cmd` → « Exécuter en tant qu'administrateur », puis relancer. Il ne demande jamais les droits admin de lui-même quand ce n'est pas nécessaire.
- **Jeu introuvable ?** Dans le rare cas où la détection échoue, utilisez l'installation manuelle ci-dessous.
- Pour mettre à jour plus tard, télécharger le nouveau zip et redouble-cliquer sur `Installer.cmd` (jeu fermé).

### Installation manuelle (alternative)

1. Copier le dossier `dist/CoreKeeperAccess` dans le dossier des mods du jeu :
   `<Core Keeper>/CoreKeeper_Data/StreamingAssets/Mods/`
   (chemin Steam typique : `C:\Program Files (x86)\Steam\steamapps\common\Core Keeper`).
2. Copier les deux DLL de `dist/natives` (`Tolk.dll` et `nvdaControllerClient64.dll`) **à la racine du jeu**, à côté de `CoreKeeper.exe`.

### Désinstallation

Supprimer le dossier `Mods/CoreKeeperAccess` et les deux DLL de la racine du jeu. Le mod réquisitionne le bouton Triangle (son action native « ouvrir la carte » est relogée) ; après l'avoir retiré, repassez par « contrôles par défaut » dans les options du jeu pour restaurer Triangle.

## L'aide en jeu

Vous n'avez jamais à mémoriser une fiche de commandes. Chaque fonctionnalité ci-dessous rappelle sa commande principale, mais la liste complète et adaptée au contexte est toujours dans l'aide en jeu.

- **À votre toute première partie**, un mode découverte de la manette se lance automatiquement : appuyez sur les boutons et bougez les sticks, chacun est nommé et situé pour vous. Il se termine en vous donnant le raccourci du menu d'aide.
- **Le menu d'aide** (Triangle + double-tap D-pad haut) liste à tout moment les commandes disponibles **dans le contexte courant** (monde, inventaire, carte, menu) — avec les vrais boutons, renommés façon PlayStation ou Xbox selon vos réglages. Le rouvrir relance aussi le mode découverte de la manette si vous le souhaitez.

L'ensemble suit le remapping du jeu et votre préférence de nomenclature : il dit donc toujours la vérité sur votre configuration. Triangle est le modificateur d'accessibilité du mod : tant qu'il est tenu, le D-pad, les sticks et les bumpers déclenchent les commandes citées plus bas.

## Les fonctionnalités en détail

Tout ce qui suit est décrit côté joueur : à quoi ça sert, ce que vous entendez, et la commande principale.

### Menus

Chaque menu est lu à la navigation : titres, options, sliders et sélecteurs, sélection et création de monde et de personnage, menus multijoueur (gestion des joueurs, pop-ups de confirmation). Les **champs de nom** (monde, personnage) annoncent l'entrée et la sortie du mode édition et relisent ce que vous tapez — **Croix** valide. Les **cinématiques** d'intro et de fin se lisent toutes seules slide par slide ; **maintenir Croix une seconde** pour passer. Navigation au D-pad, gauche/droite ajuste sliders et sélecteurs.

### Le curseur de tuile — votre main

Le curseur tâte le terrain case par case autour de vous et nomme ce qu'il touche ; c'est aussi par lui qu'on agit. Chaque déplacement joue un son — le panoramique donne la gauche/droite, la hauteur donne le haut/bas. Un tick discret = case libre, un son de matériau = mur ou bloc, un « ding » = minerai dans le mur, un petit marqueur aigu = objet interactif, un plop = trou, une éclaboussure = eau. « Mur scellé » = indestructible, n'insistez pas. **Déplacez le curseur au D-pad** ; **Croix** agit sur la case du curseur (miner, interagir, ou s'y déplacer) ; bouger au stick gauche le recolle au personnage.

### La canne laser — votre canne blanche longue portée

Un faisceau balaye dans la direction que vous visez **au stick droit** et vous dit ce qu'il y a droit devant : le premier obstacle (vous entendez le « mur d'en face »), et les menaces sur le chemin. Les ennemis sont signalés par un bip positionnel plus leur nom ; les créatures paisibles et les objets posés ont leurs propres timbres, plus doux (un hostile les écrase toujours). Les gouffres et l'eau n'arrêtent pas le faisceau — vous entendez le bord, puis ce qu'il y a au-delà, pour viser au travers et tirer. Le curseur et la canne parlent la même langue sonore : une même case sonne pareil dans les deux.

### Sonar de proximité

Une aide pour les zones confinées, **à activer dans le panneau de réglages**. Des nappes de bruit signalent les murs autour de vous dans les quatre directions (gauche/droite au panoramique, timbre mat pour un mur, clapotis pour l'eau ou un gouffre), et un petit « ding » marque les objets proches case par case.

### Sentinelle d'aggro

Entièrement automatique. **Chaque ennemi en train de vous attaquer émet un bip une fois par seconde** ; avec plusieurs assaillants les bips se chevauchent en file, et le rythme vous dit en gros combien sont sur vous. Un **boss** a son propre bip grave et rapide sur un canal dédié — impossible à confondre.

### Annonces automatiques

Sans rien faire, vous entendez : l'objet en main au changement de slot, « interaction disponible » quand un objet utilisable est à portée, les messages flottants du jeu (tutoriels, « trop dur », besoin d'énergie…), et les ramassages (nommés, totalisés, avec une alerte de sac plein). Les **alertes de vie faible** se déclenchent toutes seules : un avertissement sous un seuil réglable et un battement de cœur dont la cadence dit la gravité. Les **alertes d'états** retentissent une fois quand un effet de dégâts dans le temps (feu, acide, radiation…) ou un étourdissement vous touche.

### Roue de stats

Lire une donnée sans ouvrir de menu — **tenez Triangle et poussez le stick gauche** vers un secteur, et ce secteur parle (la marche est gelée le temps de consulter). Secteurs : vie et barrière, faim, mana et serviteurs, conditions actives (empoisonné, en feu…), avancement dans le monde, et prospection de minerai autour de vous. La régénération de vie et de mana s'ajoute en fin d'annonce.

### Panneau de réglages

Un panneau de réglages d'accessibilité à part (**Triangle + Back**), navigué au D-pad, avec des descriptions parlées et des aperçus sonores pour chaque entrée. Vous y réglez les volumes (par fonctionnalité : navigation, guidage, sonar, sentinelles, alertes, battements de cœur…), l'aide à la direction, le ralenti de combat, le sonar de proximité, la normalisation audio, les seuils d'alerte, la nomenclature des boutons PlayStation/Xbox, et plus encore. Les réglages sont mémorisés et conservés d'une version à l'autre.

### Inventaire et artisanat

Ouvrez et fermez l'inventaire avec **Carré**. Navigation par sections avec **LB / RB** (barre rapide, sac, équipement, artisanat, coffre, statistiques…), et le **D-pad** se déplace dans une section. Les recettes sont lues avec leurs matériaux manquants (« fabricable » / « manque N X ») ; **Croix** prend, pose, active un onglet, ou fabrique (le résultat arrive « en main »). **RT** déplace vite un objet — il le transfère vers l'autre conteneur ouvert —, **LT** le lâche. Sont aussi couverts : la fiche de stats, les talents (état et points à dépenser), les âmes (état de chaque emplacement) et les onglets. Quelques actions (trier, empiler vite, ramasser la moitié, pages de barre rapide, poubelle) vivent sur une **roue d'actions** au stick gauche — poussez vers un secteur pour entendre l'action, puis **clic R3** pour l'exécuter.

### Marchand et bourses

En interagissant avec un marchand, des sections Acheter et Vendre s'ouvrent (LB / RB) ; chaque article est lu avec son prix, et côté Vendre la valeur de revente d'un objet de votre sac est annoncée. Côté Acheter, **Croix** achète l'article sélectionné. La vente, elle, n'est pas automatique : il faut déposer l'objet dans la zone de vente — le plus simple est de le sélectionner dans votre sac et d'utiliser **RT** pour l'y déposer vite —, puis **Triangle + gauche** vend tout d'un coup. **Triangle + haut** lit votre solde de pièces et le total de la transaction. Les **bourses** (pochettes de rangement) sont gérées : le panneau se déploie automatiquement, le contenu est présenté en lignes, et vous équipez ou déséquipez une bourse à la manette.

### Station de réparation et de recyclage

Fabriquée à l'établi, la station s'ouvre comme une section d'inventaire normale. Ignorez ses boutons visuels : sur l'objet sélectionné, **Triangle + droite** le répare (sur n'importe quel slot affiché), **Triangle + gauche** recycle tout ce qui est déposé contre des pièces détachées et une partie des matériaux, **Triangle + haut** lit les détails de l'objet enrichis du coût de réparation et du gain de recyclage, et **RT** dépose ou récupère vite l'objet sélectionné (le recyclage agit sur ce qui est déposé dans la station).

### Forge d'amélioration

La forge se pilote en trois temps : déposez l'objet dans le slot de la forge (**RT** depuis le sac), naviguez jusqu'à la section **artisanat** (LB / RB), puis **Triangle + droite** l'améliore d'un cran de qualité — **Triangle + haut** lit le coût en matériaux.

### Construction et agriculture

**Le jeu n'autorise pas la pose à distance** : il faut être à proximité de la case visée, alors approchez-vous d'abord. Visez la case avec le curseur de tuile et **LT** pose l'objet en main ; **Triangle + R1** fait pivoter ce que vous posez ; **Triangle + L3** bascule l'**aide à la direction**, qui cale vos déplacements sur les axes cardinaux pour avancer droit et aligner proprement. Pour les objets qui couvrent plusieurs tuiles, le curseur indique l'emprise (par exemple « zone 3x3 ») ; pour les outils de terrain (houe, arrosoir, pelle, semoir), **Triangle + R1** cycle la taille de la zone d'effet. La lecture au curseur couvre le sol labouré ou arrosé, l'état des plantes (prête à récolter, a soif, en croissance), les stations de transformation (slots d'entrée/sortie, progression en pourcentage) et les bases d'industrie (convoyeurs, électricité, machines).

### Combat

Le **ralenti** (à activer dans le panneau de réglages) s'enclenche dès que vous entrez en combat — au moment où la sentinelle d'aggro s'active — et ralentit l'écoulement du temps en jeu : tout ralentit, vous comme vos ennemis, ce n'est donc pas un avantage, juste de la marge pour réagir. Son intensité est réglable.

Les **zones dangereuses au sol** liées au combat (feu, poison…) sont signalées, et les **boss** ont leur propre bip dédié sur la sentinelle (voir plus haut).

Les trois premiers boss ont d'ailleurs été vaincus en conditions réelles, écran éteint, au mode de difficulté le plus faible.

Les combats de boss les plus difficiles et complexes seront livrés au fil des versions avec leur lot d'aides dédiées.

### Dialogue et journal

Les répliques du Cœur sont lues à voix haute automatiquement, et un **journal** les archive conversation par conversation pour les relire plus tard (les tutoriels sont rangés à part). Le journal est l'une des catégories de la carte (voir ci-dessous).

### Carte et balises

La carte accessible s'ouvre n'importe où (**double-tap Triangle**). Elle a trois catégories que vous faites défiler avec **LB / RB** et parcourez en listes au **D-pad haut/bas** ; **Triangle + haut** donne les détails d'un élément (coordonnées, biome, cap en degrés, distance) :
- **Points d'intérêt** : boss scannés, votre tombe, marqueurs. **Croix** ouvre un menu d'actions (dont le guidage, voir la section suivante).
- **Mes balises** : vos repères posés à la main. Une ligne « nouvelle balise ici » en pose un à votre position ; **Croix** sur une balise existante ouvre son menu (guidage, renommer, supprimer). Les noms sont mémorisés par monde et par emplacement.
- **Journal** : les conversations archivées du Cœur.

### Navigation : le réseau de torches et le guidage

C'est l'un des systèmes les plus récents du mod, voici donc l'idée en entier. Le principe : vous bâtissez votre propre carte routière en jouant, et le mod vous guide à l'oreille le long de cette carte.

**Poser une torche ajoute un point.** Chaque torche que vous posez devient un point du réseau (les portes que vous franchissez aussi). C'est un geste naturel qui rend déjà deux services dans le jeu — éclairer et révéler la carte autour — auxquels le mod en ajoute un troisième : faire de cette torche un point de votre réseau de navigation.

**Marcher d'une torche à l'autre crée le lien.** Le réseau se tisse quand vous passez à pied sur une torche existante : le mod relie alors ce point au précédent que vous venez de frôler et retient ce trajet comme un passage sûr — la preuve, c'est que vous venez de le parcourir. Un saut (téléportation) coupe la continuité : aucun faux lien entre deux points qu'aucun chemin réel ne relie. Si une balise est détruite, le point et ses trajets survivent : le mod ne coupe jamais un lien sur une simple absence.

**Conseil : une torche à chaque intersection.** Le guidage relie deux torches voisines **en ligne droite**, ce n'est pas un calcul d'itinéraire qui contournerait les murs. Posez donc une torche à chaque coude et à chaque carrefour : ainsi chaque segment droit suit vraiment le couloir, et la carotte ne vous envoie jamais dans un mur.

**Recalculer le réseau.** Dans votre base, où tout est dense et chargé autour de vous, l'entrée « Recalculer le réseau » (en queue de l'onglet « Mes balises ») re-scanne les environs et tisse ou corrige les liens locaux d'après ce qui est réellement franchissable, sans avoir à reparcourir chaque segment. Au loin, dans les zones non chargées, le mod ne touche jamais à vos liens : là, seul le passage physique fait foi. Pensez à le relancer chaque fois que vous aménagez — meubles posés, murs creusés ou bâtis, pièces réagencées — pour que le guidage reste fidèle au terrain ; et placez une torche aux endroits stratégiques au moins une fois, car ce sont elles qui ancrent les nœuds dont le nouveau maillage a besoin pour se tracer correctement. Le même onglet offre aussi « Rejoindre le réseau le plus proche ».

**Le guidage.** Sur un point d'intérêt ou une balise, **Croix** ouvre un menu avec deux modes :
- **Par le réseau** : le mod calcule le plus court chemin le long de vos torches et balises et vous y fait avancer de proche en proche, par la route sûre que vous avez déjà dégagée.
- **Direct** : à vol d'oiseau, sans tenir compte des obstacles.

Dans les deux modes, un carillon répété fonctionne comme une carotte tendue devant vous : le panoramique donne la gauche/droite, la hauteur le devant/derrière, et la cadence dit si vous tenez la ligne — sur la route elle est rapide, dès que vous déviez elle ralentit — pendant que le volume monte à mesure que vous approchez. L'arrivée est annoncée.

## Pour les testeurs

- **Consultez [KNOWN_ISSUES.fr.md](KNOWN_ISSUES.fr.md) avant de signaler** — bugs connus, limites actuelles et comportements voulus y sont listés.
- La **version et le numéro de build** sont annoncés au démarrage — donnez les deux dans tout rapport. Les nouveautés de chaque version sont dans le [journal des versions](CHANGELOG.fr.md).
- Le journal du jeu est dans `%USERPROFILE%\AppData\LocalLow\Pugstorm\Core Keeper\Player.log`. Tout ce que le mod prononce y est tracé avec le préfixe `[A11yTTS]` ; joignez ce fichier aux rapports de bug.
- **F9** (clavier) : mode diagnostic manette — annonce chaque bouton/axe pressé avec son identifiant. Pratique pour signaler un problème de mapping.
- Multijoueur : testé et fonctionnel. Le mod étant côté client, vous seul avez besoin de l'installer — vos partenaires n'ont rien à faire de leur côté.
- Rapports : ouvrez une issue GitHub avec le build, ce que vous faisiez, ce que vous attendiez, ce qui s'est passé, et le Player.log.

## Comment ça marche (pour les curieux)

Le mod utilise le système de mod officiel de Core Keeper (PugMod) : le code C# est recompilé par le jeu au lancement, les hooks passent par Harmony, le TTS par la bibliothèque Tolk qui parle à NVDA. Aucun fichier du jeu n'est modifié. Le mod demande l'accès « élevé » du ModLoader (`skipSafetyChecks`) car Tolk est une DLL native — c'est le prix du TTS.

## Licences et crédits

- **Code du mod** : licence MIT (fichier `LICENSE`).
- **[Tolk](https://github.com/dkager/tolk)** de Davy Kager : LGPL-3.0 (`third_party/Tolk/LICENSE.txt`).
- **nvdaControllerClient** (NV Access) : LGPL-2.1 (`third_party/Tolk/LICENSE-NVDA.txt`).
- Les deux DLL ci-dessus sont distribuées telles quelles, en fichiers séparés : vous pouvez les remplacer par vos propres versions.
- Core Keeper est un jeu de Pugstorm, édité par Fireshine Games. Ce mod n'est pas affilié.
