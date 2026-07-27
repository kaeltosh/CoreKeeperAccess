# Journal des versions — CoreKeeperAccess

*This page is also available in English: [CHANGELOG.md](CHANGELOG.md).*

## 1.0.15 bêta — juillet 2026

Version issue des retours de la session de test : confirmations de terrain pour ce qui était en attente depuis 1.0.12/1.0.13, plus une vague de corrections signalées par les testeurs (voir aussi les problèmes connus, cette vague-là n'a pas encore été jouée).

### Ajouts

- **La prospection minerai (Triangle+gauche) repère aussi les gisements à foreuse.** Introduite non testée en 1.0.13, confirmée sur le terrain — un gisement d'écarlate trouvé grâce à elle, qui serait passé inaperçu autrement. Elle annonce maintenant **tous** les gisements à portée, du plus proche au plus lointain, et non plus seulement le plus proche : deux gisements côte à côte ne se masquent plus.
- **Mode d'apprentissage des commandes** : tiens Triangle (ou R3, ou aucun des deux) et appuie sur n'importe quel bouton pour entendre ce qu'il fait, sans qu'il s'exécute — l'équivalent de l'aide à la saisie d'un lecteur d'écran, en complément du mode qui nomme les boutons.
- **Raccourci de profils d'équipement** : fiche perso ou inventaire ouvert (pas en station), Triangle+croix directionnelle droite/gauche passe au profil d'équipement suivant/précédent parmi les trois.
- **Le nom complet des plats cuisinés est annoncé sur la barre rapide.** Deux soupes différentes ne se lisent plus toutes les deux « Soupe » : les ingrédients sont dits, comme dans l'inventaire.
- **La commode dit si l'apparence est masquée ou visible.** Sur un emplacement d'apparence vide, on entend désormais « équipement masqué » ou « équipement visible », et la bascule est réannoncée quand tu l'actionnes — avant, les deux états se lisaient « vide ».
- **Les détails de case (Triangle+Haut) annoncent de nouveau les sols standards** (terre, pierre…), en plus des dalles et sols spéciaux. Utile notamment pour les cultures.
- **Navigation en bateau** : le bateau est repérable au curseur détaché comme n'importe quel objet posé, la canne laser signale le rivage par un son dédié au lieu de traiter l'eau comme un obstacle, et le sonar comme le détecteur de collision cessent de sonner en permanence en pleine eau. Le menu d'aide rappelle aussi comment sortir du bateau, tant que tu navigues.

### Corrections

- **Le saut à la barre 1 / emplacement 1 (Triangle+Rond) ne plante plus le mod pendant qu'on joue d'un instrument de musique.** Confirmé sur le terrain : il sort proprement du mode instrument et équipe l'emplacement.
- **Le changement de barre rapide est inversé** suite au retour des testeurs : Triangle+L1 passe à la suivante, Triangle+R1 à la précédente.
- **Les relais anciens des ruines ne sont plus pris pour des gisements** par la prospection minerai.
- **Le Varechortue n'est plus classé comme ennemi** par la canne laser : c'est du bétail, et il est désormais traité comme tel — aussi bien au laser que par la sentinelle qui surveille les attaquants.
- **Dans la fenêtre du bétail, le libellé du bouton de reproduction ne se répète plus** à la fin de la ligne d'état de l'animal.

## 1.0.14 bêta — juillet 2026

Confirmé sur le terrain : la commande de détails de case empilés, introduite non testée en 1.0.13, plus un correctif sur la façon dont elle (et le survol normal du curseur) gèrent un câble électrique caché sous une dalle.

### Ajouts

- **Les détails de case (Triangle+Haut) annoncent désormais toutes les couches présentes sur la case pointée** (plafond, mur, câble électrique, objet posé, sol/dalle), au lieu d'une seule à la fois — utile pour les cases où plusieurs éléments se superposent. Introduit non testé en 1.0.13, confirmé fonctionnel sur le terrain.

### Corrections

- **Un câble électrique nu caché sous une dalle (dalle de pierre, pont, dalle à peindre…) n'a plus la priorité sur la dalle qui le recouvre réellement.** Au survol normal du curseur, seule la dalle est désormais annoncée, avec sa tension intégrée ("dalle de pierre, sous tension/hors tension") au lieu de nommer le câble directement — de toute façon invisible à l'œil. Dans les détails Triangle+Haut, la dalle est maintenant annoncée avant le câble, conforme à ce qui est réellement visible.

## 1.0.12 bêta — juillet 2026

Confirmations terrain pour du contenu introduit non testé en 1.0.10/1.0.11, plus un remap de touche.

### Ajouts

- **L'élevage (vache, chèvre, roly-poly, tortue, dodo, chameau) est maintenant accessible.** Attacher ou détacher un animal est annoncé, le scanner de proximité a une catégorie "Bétail" dédiée sortie des créatures classiques, et la fenêtre de gestion du bétail (nom, faim, reproduction, slots de nourriture) est lisible et navigable. Introduit non testé en 1.0.10, confirmé fonctionnel sur le terrain.
- **La couleur de peinture des murs et sols est désormais annoncée au curseur** (14 teintes). Confirmé fonctionnel.
- **Un popup de confirmation signale désormais quand supprimer un monde ou un personnage exige un appui long**, pas juste le bouton de confirmation. Confirmé fonctionnel.

### Corrections

- **Pêche : retrait de l'annonce native "Attrapé X"**, qui faisait doublon avec l'annonce de ramassage et pouvait répéter le nom de la prise précédente lors d'une resynchronisation réseau. L'annonce de ramassage seule couvre désormais les prises de pêche. Confirmé fonctionnel.

## 1.0.11 bêta — juillet 2026

### Ajouts

- **Le tutoriel manette complet imposé au premier lancement a été remplacé par un popup court**, qui rappelle comment rouvrir le menu d'aide quand tu en as besoin.

### Corrections

- **Le viseur canne sur les tomes d'invocation (classe Invocateur), introduit en test dans la 1.0.10, est confirmé fonctionnel et reste actif en permanence.** Le réglage temporaire de désactivation et son bip de confirmation ont été retirés.
- **Les annonces "interaction disponible" sont désormais coupées quand une fenêtre est ouverte** (inventaire, carte, menu pause, fiche perso). Un familier qui tourne autour ne spamme plus l'annonce pendant que tu es dans un menu.

---

## 1.0.10 bêta, build 2 — juillet 2026

### Changements

- **Changement de barre rapide et pivot d'objet ont échangé leurs touches.** Changer de barre rapide se fait désormais avec Triangle+R1 (suivante) / Triangle+L1 (précédente, qui récupère la place laissée libre par l'ancien ping sonar). Pivoter l'objet en main / redimensionner la zone d'un outil (houe, pelle...) se fait désormais avec Triangle+D-pad droite.

### Corrections

- **Corrigé : une fausse annonce "zone 1x1" à l'équipement d'un outil à zone réglable (houe, arrosoir, pelle...).** La taille de zone est maintenant annoncée correctement dès la prise en main, et ne se fait plus couper par l'annonce de durabilité de l'outil.
- **Corrigé : le décor destructible (tables...) était encore classé "Ennemi" par le scanner et déclenchait la sentinelle en le tapant**, malgré un premier correctif. La faction seule ne suffisait pas à distinguer un meuble d'un monstre ; le filtre exige désormais la présence du marqueur "créature" du jeu.

---

## 1.0.9 (bêta ouverte) — juillet 2026

Scanner de proximité par catégorie, états portes/leviers, tri de coffre, réglage de la roue de barre rapide, et corrections.

### Ajouts

- **Scanner de proximité (R3 tenu) : navigation par catégorie.** Le D-pad parcourt ce qui est visible à l'écran par catégorie (ennemis, créatures paisibles, PNJ marchands, plantes, ressources, coffres), puis les entrées individuelles au sein d'une catégorie (earcon positionnel + nom vocal). R3+L3 envoie un beacon continu vers la cible courante : il se met en pause à l'arrivée, reprend si vous vous éloignez, et saute vers la plus proche si la cible disparaît. Feature permanente (pas de bascule on/off), remplace l'ancien ping sonar (Triangle+L1).
- **Annonce d'état pour portes, portails et leviers.** Au survol du curseur ou en approchant ("interaction disponible"), l'état ouvert/fermé d'une porte ou d'un portail, ou activé/désactivé d'un levier, est annoncé avec son nom. Le changement d'état est aussi signalé dès qu'il se produit, curseur détaché ou collé.
- **Trier le coffre ouvert depuis la roue d'actions inventaire.** Le "trier" natif manette ne triait que votre propre sac ; une nouvelle entrée de roue (dernier secteur libre) appelle directement le bouton de tri du coffre.
- **Roue de saut barre rapide : latence de déclenchement réglable.** Nouveau réglage "latence de déclenchement de la roue" (0-300 ms, défaut 120 ms) dans le panneau d'accessibilité : un appui bref sous ce seuil change de slot au pas-à-pas classique (aucune latence ajoutée), une tenue plus longue ouvre la roue de saut. Les deux systèmes de sélection coexistent sur R1/L1.

### Corrections

- **Corrigé : la barre rapide pouvait signaler deux cases fantômes au-delà de ses 10 vraies cases.** La barre d'action persistante du jeu reste active derrière l'écran d'inventaire plein écran et était lue comme des emplacements en trop ; la navigation est désormais bornée aux 10 vraies cases.
- **Le scanner de proximité ne liste plus votre propre personnage ni votre compagnon posé sous "créatures".**

---

## 1.0.8 (bêta ouverte) — juillet 2026

Renforcement d'objet, annonce de vie du boss, bascule de catégories de recettes, et corrections.

### Ajouts

- **Renforcement d'objet à la station de réparation.** Triangle+Bas bascule entre les modes réparation et renforcement (annoncé) ; Triangle+Droite applique le mode courant, et le coût affiché (Triangle+Haut) suit le mode choisi. Le renforcement booste la durabilité maximale d'un objet au-delà de son plafond normal.
- **Annonce de vie des boss tous les 10 %.** Pendant un combat de boss, la vie restante est annoncée par palier de 10 %, à la baisse comme à la remontée (ne masque jamais un soin en cours).
- **Bascule entre catégories de recettes sur un établi/station qui en regroupe plusieurs.** Certaines stations conservent aussi les recettes d'un modèle antérieur (par exemple une enclume améliorée qui garde les recettes de l'enclume précédente) : Triangle+Droite/Gauche passe d'une catégorie à l'autre, avec annonce du nom du modèle affiché. Muet si la station n'a qu'une seule catégorie.
- **Réglage "Silence interaction en curseur".** Dans le panneau d'accessibilité, catégorie Navigation : coupe l'annonce redondante "interaction disponible" pendant l'usage du curseur de tuile détaché (qui annonce déjà l'objet survolé). Désactivé par défaut.

### Corrections

- **Forge d'amélioration : entrée et sortie correctement annoncées.** Le slot où déposer l'objet et celui qui prévisualise le résultat tombaient auparavant dans une catégorie générique incorrecte ; ils sont maintenant rattachés à la section artisanat avec leurs rôles clairs, et le résultat prévisualisé (gain de statistiques) est désormais lisible.
- **Le compagnon (canne laser) ne masque plus un objet plus loin.** Un familier ou un serviteur posé entre vous et une plante ou un coffre plus éloigné ne bloque plus sa détection.

---

## 1.0.7 (bêta ouverte) — juillet 2026

Roue de saut dans la barre rapide, détecteur de collision, combat d'Azeos et corrections.

### Ajouts

- **Roue de saut dans la barre rapide.** Maintiens R1 pour ouvrir une roue à 10 positions au stick gauche (la marche se gèle le temps de la tenue) ou L1 pour la même roue au stick droit (la canne laser se tait le temps de la tenue) : pointe une direction pour équiper directement le slot correspondant de la barre active. Triangle+Rond saute directement au premier slot de la première barre. Activable/désactivable dans les réglages.
- **Détecteur de collision directionnel.** Aide optionnelle (désactivée par défaut, à activer dans les réglages) : dans l'axe où tu pousses le stick gauche, un bruit aigu de plus en plus fort t'avertit avant un mur, un trou ou de l'eau infranchissable, à portée réglable.
- **Détection améliorée des piliers de foudre d'Azeos.** Le combat contre Azeos le Titan du Ciel repère plus fiablement le comportement de chaque pilier de foudre, avec une alerte plus précoce.
- **Drone d'arène pour les combats de Titans.** Pendant un combat de boss de type Titan, un drone sonore indique la position du centre de l'arène, avec un comportement distinct en combat et hors combat.

### Corrections

- **Flèches "changer de barre" masquées par défaut.** Les deux boutons visuels aux extrémités de la barre d'action n'apparaissent plus par défaut — ils n'apportaient rien en audio et généraient une annonce parasite ("appuie pour changer de barre").
- **Stalagmites de nouveau silencieuses au curseur et au laser.** Ce décor n'a jamais été minable ni interactif ; il ne se confond plus avec d'autres objets, sans toucher au reste de l'index.

---

## 1.0.6 (bêta ouverte) — juin 2026

Arbre de talents du familier, drone de détection des relais, et améliorations de la liste des POI.

### Ajouts

- **Arbre de talents du familier.** Accessible depuis l'équipement : appuie sur le bouton du slot du familier pour ouvrir son arbre de talents complet. Chaque talent indique son nom et son état (verrouillé, disponible ou acheté). Triangle+Droite achète un talent ; les points disponibles sont annoncés sur le bouton. Une entrée « Réinitialiser les talents » en fin de liste rembourse tous les points dépensés, avec une confirmation TTS succès ou pièces insuffisantes.
- **Drone "Relais proche".** Quand un relais de téléportation non encore activé est visible à l'écran, un drone sinusoïdal pulsé se déclenche. Le panoramique indique gauche/droite, la hauteur du son indique avant/arrière. Le drone cesse dès que le relais quitte l'écran ou est activé. Ajouté à l'apprentissage des sons (catégorie Exploration).

### Améliorations

- **Liste des POI réorganisée et filtrée.** Les points d'intérêt sont maintenant triés par ordre alphabétique. Les relais de téléportation déjà activés apparaissent dans la liste. Les portails et relais non encore découverts sont masqués, pour être cohérents avec la carte native.

### Corrections

- **Nom du set bonus corrigé.** Les ensembles d'armure s'annonçaient avec leur identifiant anglais interne (ex. : "LarvaSet") au lieu du nom localisé correct (ex. : "Armure Larve").

---

## 1.0.5 (bêta ouverte) — juin 2026

Tooltip des équipements enrichi et corrections.

### Ajouts

- **Set bonus dans le tooltip.** Le tooltip d'une pièce d'armure appartenant à un ensemble affiche maintenant le nom du set et le nombre de pièces équipées (ex. : "Armure Larve, 2/3"). Triangle+Haut révèle le détail complet : chaque bonus par seuil (actif ou inactif), et la liste des pièces manquantes.
- **Niveau du familier.** Le tooltip du familier équipable indique son niveau et sa progression dans le niveau courant en pourcentage (ex. : "Niveau 5, 30%").

### Corrections

- **Max serviteurs affiché correctement.** La roue de stats affichait "0 serviteur max" même sans équipement dédié — la base de 1 serviteur n'était pas comptée.
- **Sonar silencieux au menu principal.** En quittant un monde, le sonar de proximité continuait à sonner dans le menu principal. Il s'arrête maintenant dès la sortie du monde.

---

## 1.0.4 (bêta ouverte) — juin 2026

Combat de la Hive Mother, guide vers le sigil d'invocation pour tous les boss, et sol acide détecté.

### Ajouts

- **Combat Hive Mother.** Le mod annonce le tir acide ("La ruche tire !") juste avant que le projectile parte, et l'enrage ("La ruche s'enrage !") au changement de phase. Les œufs en train d'éclore sont détectés par la sentinelle d'aggro et bippent positionnellement — tu sais où ils sont sans les voir.
- **Guide sonore vers le sigil d'invocation.** Quand tu entres dans la salle d'un boss, un drone discret t'indique la direction de la rune d'invocation (pan gauche/droite, pitch haut/bas). À 1,5 case, le mod annonce "Rune d'invocation".

### Corrections

- **Faux positif canne laser sur les œufs de ruche.** La canne laser bippait sur les œufs passifs de la Hive Mother comme si c'étaient des ennemis. Ils sont maintenant ignorés jusqu'à leur éclosion.

### Améliorations

- **Détection des zones de danger plus réactive.** Le sol acide et les pièges de feu sont scannés deux fois plus vite — utile quand on court ou quand un tir de mortier crée une zone de feu d'un coup.

---

## 1.0.3 (bêta ouverte) — juin 2026

Nommage des sols spéciaux et des plantes en croissance.

### Corrections

- **Sols spéciaux nommés correctement.** Certains types de sols (chrysalis, sol gluant…) s'annonçaient avec leur nom technique interne au lieu d'un nom lisible. Le curseur résout maintenant le vrai contenu du sol selon le biome.
- **Plantes en croissance sans traduction.** Quelques plantes (`GrubKapokPlant`, `CoralRootPlant`, `GleamRootPlant`) s'annonçaient en anglais. Elles ont maintenant leur nom français.

---

## 1.0.2 (bêta ouverte) — juin 2026

Lisibilité des menus du jeu, un écran pour apprendre les sons, et un correctif important sur la mémoire de navigation propre à chaque monde.

### Ajouts

- **Menu d'apprentissage des sons.** Depuis le menu d'aide, un nouvel écran te laisse écouter chaque son du mod, classé par catégorie (sonification des tuiles et du déplacement, combat) ; le son se joue au survol pour que tu apprennes à les reconnaître.
- **Guide de démarrage rapide.** Un nouveau document (français et anglais), lié depuis le README, accompagne le nouveau joueur : présentation du jeu, perception à l'oreille (curseur de tuile, canne laser, sonar), création de monde et de personnage, commandes de base.

### Améliorations

- **Les barres du menu Options sont lues en pourcentage.** Les réglages à barre (volumes notamment) annonçaient une suite de symboles illisible ; ils annoncent désormais un pourcentage clair, qui se met à jour quand tu changes la valeur.
- **Création de partie plus claire.** Le bouton « graine aléatoire » est maintenant annoncé, et les deux onglets en haut de l'écran (Général et Monde) le sont aussi, avec un mot sur ce que chacun contient.
- **Apprentissage de la manette affiné.** Les boutons sont nommés plus simplement, un rappel signale les deux petits boutons centraux si tu ne les as pas essayés, et l'écran final peut être réécouté à volonté.
- **Noms de personnalisation traduisibles.** Les noms des variantes de personnage (couleurs, coupes, morphologies) vivent désormais dans les fichiers de langue — l'anglais est fourni, et d'autres langues peuvent être ajoutées facilement.

### Correctifs

- **La navigation et le journal suivent le monde, pas l'emplacement de sauvegarde.** Si tu supprimais un monde puis en recréais un sur le même emplacement, le nouveau héritait des balises, du réseau de navigation et du journal de l'ancien. Chaque monde a maintenant sa propre mémoire, et supprimer un monde efface proprement ses données de navigation.

## 1.0.1 (bêta ouverte) — juin 2026

Petite mise à jour de finitions par-dessus la 1.0.

### Ajouts

- **Montée de niveau des compétences annoncée.** Chaque fois qu'une compétence (minage, course, corps à corps, magie…) gagne un niveau, le mod l'annonce avec son nom et le niveau atteint. Aux paliers qui octroient un point de talent, l'info est réunie en une seule annonce (« … niveau 15, nouveau point de talent disponible ») pour qu'aucune ne se fasse couper par l'autre.
- **L'écran de création de personnage devient lisible côté cosmétique.** Les variantes qui n'étaient pas du tout annoncées le sont désormais : couleurs de peau, cheveux, yeux et vêtements (noms distincts et corrects par catégorie), morphologies nommées par carrure (Robuste / Svelte), et coupes de cheveux.

### Correctifs

- **Journal : le dialogue d'éveil du Cœur ne s'affiche plus trop tôt.** Il n'apparaît dans le journal que lorsque le Cœur est réellement activé, au lieu de se montrer dès le chargement du monde.

## 1.0 (bêta ouverte) — juin 2026

CoreKeeperAccess sort de l'alpha et s'ouvre à un public plus large. Cette 1.0 consolide l'Alpha 2 : correctifs, finitions, et une grosse refonte interne pour la pérennité — tout le contenu de l'Alpha 2 ci-dessous est inclus.

### Construction et contrôles

- **Poser des objets en ligne droite en marchant.** Détache le curseur de tuile à la croix directionnelle sur une case juste à côté de toi, puis maintiens le bouton de pose (LT) : l'écart relatif se verrouille, le curseur te suit pendant que tu te déplaces, et l'objet se pose case après case — tu traces ainsi une ligne nette de murs ou de sol au lieu que le jeu les éparpille sur « la case libre la plus proche ». Un son discret marque chaque case franchie (un compteur à l'oreille) ; se marie avec la direction assistée (Triangle + L3) pour des lignes parfaitement droites.
- **Le déplacement par Croix s'arrête au centre de la case.** Avant, il s'arrêtait dès qu'il était à une demi-case de la cible, te laissant décentré et faussant ta portée de pose/interaction (par exemple atteindre un établi situé une case en diagonale). Tu te cales maintenant près du centre.
- **Changement rapide de barre d'action à la manette.** Triangle + droite / Triangle + gauche passent à la barre d'action suivante / précédente en jeu (la croix directionnelle étant prise par le curseur de tuile) ; l'objet désormais en main est annoncé.
- **Lecture de l'industrie fiabilisée.** Le sens des convoyeurs, l'état électrique (génère / sous tension / hors tension) et la tension d'un câble passant sous une structure sont désormais annoncés correctement, et un gisement n'est plus noyé sous les machines voisines quand tu le survoles.
- **Réglages par défaut calibrés à l'usage.** Les valeurs d'usine ont été affinées au fil du jeu (volumes général et navigation, sonar de proximité activé d'office, alertes de vie plus douces, total possédé annoncé au ramassage, etc.). Ça ne concerne qu'une installation neuve — tes propres réglages enregistrés ne bougent pas, et tout reste modifiable dans le panneau de réglages.

### Correctifs et finitions

- **Le menu pause ne s'ouvre plus par-dessus les menus du mod.** Appuyer sur Start alors qu'un menu du mod était ouvert (apprentissage manette, panneau de réglages, menu contextuel, saisie de nom) ouvrait le menu pause du jeu par-dessus. Il attend maintenant que vous fermiez le menu du mod.
- **Les directions de la croix directionnelle sont nommées clairement.** Dans le menu d'aide et le mode découverte de la manette, les quatre directions sont annoncées « croix directionnelle haut/bas/gauche/droite » (ou « D-pad … » en style Xbox), pour ne plus les confondre avec le stick.

### Sous le capot

- **Grosse refonte interne** : le moteur de menus est désormais partagé entre le panneau de réglages et les menus contextuels, et le lecteur de carte est restructuré en sections autonomes. Rien d'audible — c'est le terrain préparé pour ajouter proprement de futurs menus (un codex, un tutoriel guidé) et garder le mod robuste face aux mises à jour du jeu.

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
