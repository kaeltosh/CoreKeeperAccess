# Bien démarrer avec Core Keeper

*This page is also available in English: [GUIDE.md](GUIDE.md).*

Ce guide est une prise en main pour découvrir Core Keeper avec le mod d'accessibilité **CoreKeeperAccess**. Il présente le jeu, sa logique et les tout premiers pas.

Il ne couvre ni l'installation ni la liste complète des commandes : pour cela, voir le README. La référence vivante des contrôles reste le **menu d'aide en jeu** (touche d'accès Triangle + double appui rapide sur la croix directionnelle vers le haut), qui s'adapte toujours à la situation.

---

## 1. C'est quoi Core Keeper

Core Keeper est un jeu de survie et d'exploration souterraine. Le personnage se réveille à côté d'un grand cristal central, le **Cœur**, au milieu d'une caverne : tout part de là.

La boucle de base tient en quelques verbes : **explorer**, **creuser** pour récolter des ressources, **fabriquer** des outils et de l'équipement, **construire** une base, puis s'enfoncer plus loin dans des zones de plus en plus difficiles. Il faut **manger** régulièrement pour ne pas avoir faim, et **se soigner** après avoir pris des coups.

Plus tard viennent des **boss** à vaincre pour débloquer l'accès aux zones suivantes. Cela suppose d'être équipé : au début, on reste à proximité du Cœur.

---

## 2. Comment le jeu est « vu » : la grille de tuiles

Core Keeper se joue en **vue de dessus** : une caméra placée au plafond regarde droit vers le bas. Le personnage est au centre de l'écran, et c'est le décor qui défile autour de lui pendant les déplacements.

Point essentiel : **tout est posé sur une grille de cases carrées**, comme un quadrillage. Le personnage occupe une case ; chaque morceau de mur ou de sol, chaque objet, chaque ennemi occupe une ou plusieurs cases.

On se déplace et on se repère en **quatre directions** : haut, bas, gauche, droite (et les diagonales). Il n'y a ni hauteur ni profondeur : c'est un plan à plat.

En pratique, le repérage se fait surtout **à l'oreille** : le mod fait *entendre* l'environnement. La grille n'est qu'une façon simple de se représenter les lieux.

Pour percevoir tout cela sans la vue, le mod fournit plusieurs outils complémentaires, chacun son usage :

- Le **curseur de tuile** : on le déplace case par case (croix directionnelle) pour inspecter une case précise alentour. Il annonce ce qu'elle contient — un mur (et son matériau), du minerai, du sol, de l'eau, un trou, un objet posé.
- La **canne laser** : on la balaie dans une direction (stick droit) pour scanner ce qu'elle rencontre — murs, obstacles, ennemis nommés. Utile pour sonder au loin sans avancer case par case.
- Le **sonar de proximité** : il sonne en continu ce qui se trouve à proximité immédiate, pour percevoir l'environnement proche pendant les déplacements.

À tout moment, **Triangle + un appui sur la croix directionnelle vers le haut** — la **touche info** — donne le détail de ce qui est sélectionné : ici, le contenu précis de la case visée par le curseur (matériau d'un mur, nature du sol…). Un double appui rapide, lui, ouvre le menu d'aide.

---

## 3. Créer un monde et un personnage

Avant de jouer, le jeu fait créer un monde puis un personnage. Ces écrans, comme tous les menus, se naviguent à la **manette** : la **croix directionnelle** déplace la sélection entre les options, le bouton **Croix** valide. Le mod lit chaque option à voix haute au fil de la navigation. Le mode découverte de la manette, qui détaille tous les boutons, se lance ensuite automatiquement à la première entrée en jeu.

La **difficulté recommandée**, pour le personnage comme pour le monde, est **Décontracté**.

---

## 4. Les commandes de base et les premiers pas

Tout passe par la **touche d'accès du mod : le bouton Triangle**. Maintenu, il transforme la croix directionnelle et quelques boutons en couche de commandes du mod. La liste complète et à jour se trouve dans le **menu d'aide** (Triangle + double appui vers le haut) — c'est le premier raccourci à mémoriser, car il donne accès à tout le reste.

Le minimum pour démarrer :

- **Se déplacer** : le stick gauche.
- **Inspecter alentour** : la croix directionnelle déplace le curseur de tuile, case par case.
- **Bouton Croix** : quand le curseur de tuile est **détaché** du personnage (posé sur une case avec la croix directionnelle), le bouton Croix agit sur cette case — s'y **déplacer** si elle est vide, **interagir** si c'est un objet (coffre, établi…), la **miner** ou la **frapper** si c'est solide (mur, bloc, minerai).
- **Actions de l'objet tenu** : l'**action principale** (gâchette droite) et l'**action secondaire** (gâchette gauche) se servent de l'objet en main — détail à la section barre d'action.
- **Ouvrir l'inventaire**, **ouvrir la carte** : voir le menu d'aide pour les boutons exacts (la carte s'ouvre par un double appui sur Triangle).

### Les premiers objectifs

Le jeu lui-même guide les premiers instants, dans cet ordre :

1. **Ramasser du bois** (et les ressources de base autour du Cœur).
2. **Fabriquer un établi**, la première table de craft.
3. **Fabriquer une torche** : un peu de lumière pour les voyants, et surtout un **repère** — les torches posées servent de points d'appui à la navigation par balises du mod.
4. **Fabriquer une pioche**, pour creuser le minerai.
5. **Fondre du minerai** afin d'obtenir des barres de métal.

C'est la rampe d'entrée que suit tout nouveau joueur. Une fois ces bases posées, on commence à creuser alentour, à s'équiper et à agrandir la base.

---

## 5. L'écran d'inventaire

L'inventaire regroupe tout ce que le personnage possède. On y trouve trois grandes zones :

- Le **sac** : tous les objets ramassés.
- L'**équipement** : ce que le personnage porte (armure, accessoires).
- La **barre d'action** : les slots de raccourci, détaillés à la section suivante.

À l'ouverture d'un **coffre** ou d'une **station** (établi, fonderie…), son contenu s'ajoute à l'écran comme une zone supplémentaire.

Le mod fait naviguer **par sections** : on passe d'un groupe à l'autre (sac, équipement, barre rapide, coffre…) et on parcourt les objets de chacune, annoncés à voix haute. Le détail d'un objet ou d'une recette (matériaux requis, par exemple) s'obtient avec la **touche info** : **Triangle + un appui sur la croix directionnelle vers le haut**. Les autres gestes utiles (transférer, lâcher, jeter…) figurent dans le menu d'aide lorsqu'ils s'appliquent.

Les actions d'inventaire que la manette ne propose pas directement (trier, empiler rapidement, lâcher au sol…) sont réunies dans une **roue d'actions** : inventaire ouvert, incliner le **stick gauche** la fait apparaître, on pointe la commande voulue, puis on la valide avec **R3** (clic du stick droit).

---

## 6. La barre d'action (barre rapide)

La **barre d'action** est la rangée de slots de raccourci. Elle sert à sélectionner rapidement l'objet tenu **en main** : un outil, une arme, un objet à poser ou de la nourriture.

Principe à retenir : **l'objet tenu détermine ce que font les deux actions de jeu**. L'**action principale** (gâchette droite) mine, frappe ou tire selon l'objet : une pioche mine, une arme de mêlée frappe, une arme à distance tire. L'**action secondaire** (gâchette gauche) s'adapte elle aussi : poser un objet, manger un plat, boire une potion.

On navigue dans la barre pour changer de slot, et l'objet en main est annoncé à chaque changement. En jeu, **Triangle + droite ou gauche** (croix directionnelle) déplace le **focus** de la barre d'action d'une rangée à l'autre : il parcourt ainsi, ligne par ligne, tout le contenu de l'inventaire, pour une sélection rapide sans l'ouvrir. Mieux vaut vérifier l'objet tenu avant d'agir : c'est la cause la plus fréquente d'une action qui ne produit pas l'effet attendu.

---

## 7. Survie et combat

### Surveiller son état

L'état du personnage se lit à tout moment avec la **roue d'état** : **Triangle + stick gauche**. Elle annonce la vie, le mana, la faim, les conditions en cours, la progression et la prospection. C'est le réflexe pour faire le point, surtout quand la vie baisse ou que la faim se fait sentir.

### La sentinelle d'aggro

Pas besoin de balayer en permanence : la **sentinelle d'aggro** veille automatiquement. Chaque monstre qui attaque émet un **bip spatialisé**, placé dans sa direction, à raison d'**un bip par seconde au maximum** par ennemi (un même ennemi ne sonne donc jamais plus d'une fois par seconde). Les bips ne se superposent pas : ils s'enchaînent dans une **file d'attente**, chacun attendant son tour. Ainsi deux assaillants donnent deux bips successifs, localisés chacun à sa position — de quoi suivre plusieurs menaces à l'oreille. C'est le complément passif de la canne laser : la canne sert à chercher activement, la sentinelle prévient quand le personnage est pris pour cible.

Quand la sentinelle se déclenche, le jeu passe **automatiquement au ralenti** tant qu'un ennemi reste à l'attaque : c'est normal et voulu, le temps est rendu pour laisser réagir aux sons. Le ralenti est **symétrique** : *tout* ralentit, y compris le personnage lui-même — ses déplacements comme sa cadence de tir et d'attaque. C'est donc une compensation, pas un avantage. Aucune manipulation : cela se fait tout seul.

### Frapper un ennemi

Le repérage et la visée des ennemis passent par la **canne laser** (stick droit). Pour attaquer :

1. **Balayer le stick droit** dans la direction présumée de l'ennemi.
2. Quand le laser passe sur un ennemi, un **son** le signale et l'ennemi est **annoncé**.
3. **Frapper** avec l'action principale (gâchette droite).

Important : le fait que le laser **détecte** un ennemi ne veut pas dire qu'il est **à portée** de l'arme. La canne repère un ennemi même éloigné ; avec une arme de mêlée comme une épée, il faut donc **se rapprocher** pour que les coups portent réellement. Une arme à distance, elle, peut toucher de plus loin.

---

## 8. Construire et poser des objets proprement (les outils du mod)

Poser un objet ou un mur au bon endroit est l'une des opérations les plus délicates sans la vue, car la pose normale du jeu **« devine »** l'emplacement à partir de la direction du personnage, de façon imprévisible. Le mod offre deux modes de pose fiables.

### La pose au curseur (pose précise, case par case)

Mode de base, déterministe :

1. Sélectionner l'objet à poser dans la **barre d'action**.
2. Repérer la case d'emplacement avec le **curseur de tuile** (croix directionnelle).
3. Pour se mettre à portée, viser une case **adjacente** à cet emplacement avec le curseur, puis appuyer sur le **bouton Croix** : le personnage se déplace jusqu'à la case visée.
4. Ramener le **curseur de tuile** sur la case d'emplacement.
5. Appuyer sur la **gâchette gauche** : l'objet est posé.

Des retours sonores accompagnent l'action : un **tic** en cas de réussite, un **son d'invalidité** si la case n'est pas constructible (déjà occupée, hors de portée…).

### La pose en ligne (pour aligner une rangée)

Pratique pour poser un mur ou un sol bien droit sur plusieurs cases :

1. Activer au préalable la **direction assistée**, qui cale le déplacement sur les axes cardinaux (pour marcher bien droit).
2. Placer le **curseur de tuile** sur une case **adjacente** au personnage.
3. **Maintenir la gâchette gauche** enfoncée : le curseur se **verrouille** sur ce décalage.
4. Sans relâcher, **marcher avec le stick gauche** : le curseur suit le personnage et l'objet se pose case par case, en une ligne nette.

Un tic sonore accompagne chaque pose.

### Bon à savoir

- **Triangle + R1** fait **pivoter** l'objet en main (utile pour les meubles et les machines orientées) ; le cap est annoncé à chaque cran.
- Ce même raccourci **Triangle + R1** règle aussi la **taille de la zone** des outils à large rayon (houe, arrosoir, pelle…) : le mod annonce la zone (par exemple « zone 3 sur 3 »), pour savoir exactement ce qui sera labouré ou arrosé.

---

## Et ensuite ?

Une fois ces bases acquises, le reste s'apprend en jouant, le menu d'aide restant toujours accessible. Le mod couvre bien plus que ce guide : navigation par balises, sonar de proximité, alertes de vie et d'état, combat assisté à l'oreille, lecture de la carte, marchands, et davantage. Le README et le menu d'aide en jeu sont les deux références pour aller plus loin.
