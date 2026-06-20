# Journal des versions — CoreKeeperAccess

*This page is also available in English: [CHANGELOG.md](CHANGELOG.md).*

## Alpha 2 — juin 2026

Grosse fournée depuis l'Alpha 1. Le gros du jeu devient jouable au-delà des menus : construction, commerce, agriculture, combat de boss, multijoueur, et une nouvelle aide à la navigation.

### Navigation et repérage

- **Sonar de proximité** (activable dans le panneau de réglages) : une aide pour se déplacer en zone confinée. Le bip de pas est découplé du reste, des nappes de bruit signalent les murs autour de toi dans les quatre directions (gauche/droite par le panoramique, grave ou medium selon le timbre, mat pour un mur, clapotis pour l'eau ou un gouffre), et un petit « ding » marque les objets proches case par case. Trois volumes réglables, le tout coupable indépendamment.
- **Balises personnelles sur la carte** : un onglet « Mes balises » où tu poses un repère à ta position (Croix), le renommes ou le supprimes. Les noms sont mémorisés par monde et par emplacement.
- **Guidage par réseau de balises** : au-delà de poser des repères, tu peux te faire guider jusqu'à n'importe quel point de la carte (une balise ou un point d'intérêt). Croix sur la cible ouvre un menu : guidage par le **réseau** (qui suit le chemin de tes torches et balises, de proche en proche) ou guidage **direct** (à vol d'oiseau). Un carillon répété te donne la direction (gauche/droite par le panoramique, devant/derrière par la hauteur) et monte en volume à mesure que tu approches ; l'arrivée est annoncée. Le réseau se construit et se recalcule tout seul à partir de tes torches qui se voient l'une l'autre.

### Construction et lecture au curseur

- **Mode construction et pose accessibles** : calage directionnel (Triangle + L3) pour aligner ce que tu poses, pose multi-cases au curseur, rotation (Triangle + R1).
- **Lecture au curseur enrichie** : agriculture (sol labouré ou arrosé, état des plantes — prête à récolter, a soif, en croissance), stations de transformation (slots d'entrée et de sortie étiquetés, progression en pourcentage), machines, convoyeurs et électricité, seau et arrosoir vides ou pleins.
- **Taille de zone des outils** : pour la houe, l'arrosoir, la pelle ou le semoir, la taille de la zone d'effet est annoncée à la sélection et à chaque changement (Triangle + R1) — par exemple « zone 3x3 ». Ces outils n'annoncent plus une fausse « forme » comme s'ils étaient des meubles.

### Commerce

- **Marchand accessible** (sections Acheter et Vendre, valeurs, solde, tout vendre d'un coup) et **support complet des bourses** : panneau déployé automatiquement, contenu présenté en lignes, équiper et déséquiper une bourse à la manette.

### Combat

- **Combat de boss accessible** : ralenti de combat symétrique (qui ralentit aussi le boss, donc pas un avantage), repère sonore du centre de l'arène, détecteur des zones de feu au sol.
- **Visée du mortier automatique** : le viseur se cale tout seul sur l'ennemi que tu vises à la canne laser.
- **Alertes sonores d'états** : quand un état dangereux t'atteint, un son t'avertit aussitôt — un signal grave et menaçant pour les dégâts dans la durée (feu, acide, radiation), un autre pour l'étourdissement qui te bloque. Et la roue de stats (Triangle + stick gauche, secteur est) te donne désormais le dégât par seconde exact de chaque effet. Activable dans le panneau de réglages.
- **Alertes de vie faible** : quand ta vie descend, deux paliers te préviennent sans que tu aies à consulter quoi que ce soit. Sous 60 %, un double bip sec puis un battement de cœur lent qui revient régulièrement ; sous 20 %, une sirène montante puis le même battement de cœur, mais bien plus rapide — impossible à manquer. En te soignant, le cœur ralentit puis se tait dès que tu repasses au-dessus de 60 %. Activable dans le panneau de réglages.

### Personnage et progression

- **Onglet talents** : l'état de chaque talent est lu (verrouillé avec son prérequis, disponible, ou au maximum), ainsi que le nombre de points qu'il te reste à dépenser.
- **Roue de stats à la manette** (maintiens Triangle, puis pousse le stick gauche dans une direction) : un accès rapide à tes informations sans ouvrir de menu, la marche est mise en pause le temps de consulter. Chaque direction lit une donnée — vie et barrière, faim, mana et serviteurs invoqués, états actifs (empoisonné, en feu, ralenti par la bave…), avancement dans le monde, et prospection de minerai autour de toi. La régénération de vie ou de mana s'ajoute en fin d'annonce (par exemple « +4.2/s »). Au passage, la commande de position (Triangle + D-pad haut) indique maintenant aussi ton biome actuel.
- **Onglet âmes** : chaque emplacement indique son état — à débloquer, activée ou désactivée — en plus du nom et de l'effet des âmes obtenues, pour t'y retrouver sur la roue.
- **Forge d'amélioration** : améliore l'objet déposé d'un cran (Triangle + droite), avec le coût en matériaux à la demande (Triangle + haut).

### Multijoueur

- **Menus de gestion des joueurs lus** : sections, noms, et action de chaque bouton (administrateur, bannir, inviter, voir le profil, équipe JcJ), aussi bien sur l'écran dédié que dans le panneau « Joueurs connectés » du menu pause.
- **Pop-ups de confirmation lus** : la question et les libellés des options (Oui / Annuler…) sont annoncés dans tous les dialogues.

### Dialogue

- **Le Core te parle** : ses répliques sont désormais lues au lecteur d'écran.
- **Journal de dialogues** : un onglet « Journal » sur la carte archive ce que le Cœur te dit, monde par monde, pour le relire à tête reposée — utile car certains dialogues ne passent qu'une fois et sont vite écrasés. Navigation en menu déroulant : la liste des conversations, tu ouvres celle qui t'intéresse (droite), tu reviens à la liste (gauche). Les dialogues déjà passés (dont l'activation du Cœur) sont reconstitués, et les messages de tutoriel ont leur propre section pour ne pas mélanger.

### Réglages et audio

- **Panneau de réglages d'accessibilité in-game** (Triangle + Back) : navigable et entièrement vocalisé, modal, avec des réglages qui survivent aux mises à jour (volumes, aide à la direction, ralenti de combat, normalisation, etc.).
- **Refonte audio** : normalisation du volume plus juste (elle ne rate plus les sons brefs et claquants), volumes réglables jusqu'à 200 %, et un volume dédié à la navigation (curseur de tuile et canne laser).
- **Sons de menus unifiés** : tous les menus du mod (panneau de réglages, menus contextuels, roues, lecteur de carte et son journal) partagent désormais les mêmes sons de navigation, normalisés et pilotés par le volume général. La roue de stats reste silencieuse au survol (elle annonce déjà la valeur).
- **Noms de boutons au choix** : un réglage « Noms de boutons façon Xbox » (PlayStation par défaut) — le menu d'aide et tout ce qui cite un bouton s'affichent en Croix / Triangle / L2 ou en A / Y / LT selon ton choix.

### Menu d'aide et découverte de la manette

- **Menu d'aide contextuel** (maintiens Triangle, tape deux fois vers le haut) : la liste de tout ce que tu peux faire là où tu te trouves. Les commandes du mod (avec leur raccourci), que tu peux lancer directement depuis la liste, et les commandes du jeu lues sur ta vraie configuration de touches — justes même si tu remappes. La liste change selon le contexte (jeu, inventaire, carte).
- **Mode découverte de la manette** : à ta toute première entrée en jeu, un mode d'apprentissage se lance — presse un bouton ou bouge un stick, le mod te dit son nom et sa position physique (et te rappelle une fois qu'on peut cliquer les sticks). On en sort par un double-appui sur Rond, et un dernier message t'apprend comment rouvrir l'aide. Tu peux le relancer à tout moment depuis la première entrée du menu d'aide. Le menu principal restant accessible au clavier, un débutant peut créer son personnage puis apprendre la manette une fois en jeu.
- **Raccourcis d'inventaire aux gâchettes** : dans l'inventaire, R2 transfère l'objet sélectionné, L2 le lâche au sol, et Triangle + L2 le jette à la poubelle.

### Installation

- **Installation par double-clic** : plus besoin de ligne de commande ni de connaître l'emplacement du jeu. Tu télécharges le zip, tu l'extrais, tu double-cliques `Installer.cmd`, et c'est fini. Ton installation Steam de Core Keeper est trouvée toute seule, sur n'importe quel disque. La fenêtre reste ouverte à la fin pour que ton lecteur d'écran lise le résultat, et si une écriture est refusée, elle te dit clairement de relancer en administrateur.

## Alpha 1, build 54 — juin 2026

- **Les boss ont désormais leur propre bip dans la sentinelle d'aggro** : un ton grave et plus long, répété environ trois fois par seconde sur un canal dédié, au lieu de se fondre dans la file normale à un bip par seconde. Même langage positionnel (pan, hauteur verticale, volume-distance).
- **La canne laser voit à travers les gouffres et l'eau** : ces cases bloquent la marche mais pas la vue ni les flèches, donc le faisceau signale le bord (le plop / splash habituel) et continue — les ennemis et murs de l'autre côté sont détectés. Visez au travers et tirez.
- **Correctif carte** : le marqueur du troisième boss que le jeu lui-même laisse en anglais (« Larva Boss ») est maintenant lu « Ghorm le Dévoreur ». Signalez tout autre marqueur entendu en anglais.

## Alpha 1, build 52 — juin 2026

- **Le laser rapporte désormais aussi les cibles non hostiles** : créatures paisibles (insectes, chèvres, slimes dormants…) et objets posés (champignons, objets au sol, meubles, zones de fouille), une cible à la fois — la plus proche sur le faisceau. Chaque bord a son timbre ; le nom est dit au changement de cible. Un hostile dans le faisceau écrase toujours la piste paisible : aucune menace n'est jamais masquée.
- **Tes propres projectiles sont désormais ignorés** : les flèches tirées ne déclenchent plus le laser ni le curseur de tuile.
- **Nouveau : ping sonar sur Triangle + L1** — une photo sonore de ce qui est notable autour de toi (rayon de 12 cases) : un bip par cible, joués du plus proche au plus lointain (le rythme porte la distance), avec trois timbres : hostile, créature paisible, trouvaille (zone de fouille). Dit « Rien autour » si c'est vide. Le laser et la sentinelle d'aggro se taisent pendant la salve, puis reprennent. Tant que Triangle est tenu, L1 ne change plus de slot de barre rapide.

## Alpha 1 (build 51) — juin 2026

Première version distribuée aux testeurs. Tout est nouveau — voir le [README](README.fr.md) pour l'ensemble des fonctionnalités. Points notables de la dernière ligne droite :

- Croix valide désormais la saisie de nom (monde et personnage) à la manette ; l'entrée et la sortie du mode édition sont annoncées, avec le contenu du champ.
- Les cinématiques d'intro et de fin sont lues slide par slide, avec une annonce d'entrée (« maintenir Croix pour passer ») et une confirmation vocale du skip.
- L'écran du mode de personnage (Normal / Casual / Hardcore) ne fait plus défiler les modes à la Croix : il annonce le mode courant et comment le changer puis le valider.
- Le chargement rapide développeur (direct dans le monde 1 avec le perso 1) est désormais coupé par défaut ; les testeurs ont toujours le parcours menu normal.
