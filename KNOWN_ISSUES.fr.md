# Problèmes connus — CoreKeeperAccess

*This page is also available in English: [KNOWN_ISSUES.md](KNOWN_ISSUES.md).*

À jour à la 1.0.11 beta. Merci de consulter cette liste avant d'ouvrir une issue — si vous rencontrez un de ces points, inutile de le signaler sauf élément nouveau.

> **Conseil d'installation — désactivez le son spatial Windows** (Dolby Atmos / Windows Sonic pour casque) : il re-mixe la stéréo et brouille tous les repères directionnels (panoramique, gauche/droite, le sonar). La stéréo simple donne un positionnement audio juste.

## Non finalisé dans cette bêta ouverte

- **Certains sons sont encore provisoires** (sonar de proximité, créatures et objets paisibles au laser, son de pose invalide, guidage par balises) : ils fonctionnent, mais les sons définitifs ne sont pas encore choisis — ils peuvent changer.
- **Pas encore validé sur le terrain** : les menus de gestion multijoueur en vraie session multi, l'alerte d'étourdissement, et la lecture des machines d'automation avancée (industrie) au curseur. C'est codé mais pas confirmé en conditions réelles.
- **Scanner de proximité : l'exclusion de votre propre personnage et de votre compagnon de la catégorie "créatures" n'a pas été vérifiée en multijoueur** — un coéquipier doit rester visible ; pas encore confirmé en conditions réelles.
- **La plante immobilisante (piège végétal) est désormais alertée par la sentinelle** — pas encore confirmé en conditions réelles.
- **Les statues d'invocation de boss et les invocations du sceptre invocateur sont traduites** — pas encore confirmé en conditions réelles.
- **Attraper ou lâcher un animal à la laisse (vache, chèvre, tortue…) est désormais annoncé** — pas encore confirmé en conditions réelles.
- **Le scanner de proximité a maintenant une catégorie dédiée au bétail**, séparée des créatures classiques — pas encore confirmé en conditions réelles.
- **La fenêtre de gestion du bétail (nom, faim, reproduction, slots de nourriture) est désormais lisible et navigable** — pas encore confirmé en conditions réelles.
- **La couleur de peinture murs/sols est désormais annoncée au curseur** (14 teintes) — pas encore confirmé en conditions réelles.
- **Un popup de confirmation signale désormais que supprimer un monde ou un personnage exige un appui long** — pas encore confirmé en conditions réelles.
- **Pêche : l'annonce native "Attrapé X" a été retirée.** Elle doublonnait l'annonce de ramassage et pouvait, lors d'une resynchronisation réseau, répéter le nom de la prise précédente au lieu de la nouvelle. L'annonce de ramassage seule couvre désormais la pêche — pas encore confirmé en conditions réelles.
- **Les annonces "interaction disponible" sont désormais coupées quand une fenêtre est ouverte** (inventaire, carte, menu pause, fiche perso) — objectif : qu'un familier ou PNJ à proximité ne spamme plus l'annonce pendant que tu es dans un menu — pas encore confirmé en conditions réelles.

## Bugs connus

- **L'arbre de talents du familier se réinitialise au rechargement du monde.** Quand vous quittez puis ré-entrez dans un monde, les données de talents du familier en mémoire reviennent à leur état de base (limite moteur). Le familier fonctionne toujours, mais ses talents sont temporairement invisibles pour le mod. **Contournement** : ramassez le familier puis reposez-le — le jeu régénère alors ses données correctement.
- **Un générateur posé sur un câble ancien est muet au curseur de tuile.** Le réseau de câbles indestructibles des ruines du Core masque l'objet posé dessus (deux objets passifs, le collider du câble gagne). Le générateur fonctionne ; le curseur ne le nomme juste pas.
- **Les sols notables sont parfois annoncés en anglais brut** (nom interne de la tuile), quelle que soit la langue du jeu. Rare : le sol standard est muet par design, seuls les sols spéciaux sont concernés.

## Limites actuelles

- **Le nom des objets n'est annoncé qu'au premier ramassage de chaque type.** Comportement natif du jeu (les ramassages suivants ne font qu'incrémenter un compteur visuel muet). Une version future pourrait hooker l'ajout réel à l'inventaire.
- **La lumière et l'obscurité ne sont pas perçues.** L'éclairage temps réel est rendu par shader, illisible pour le mod. Approche prévue : lire les *sources* de lumière (torches en balises audio) plutôt que le rendu.
- **Pas d'assistance pour retourner sur le lieu de décès.** Hors mode Décontracté, l'inventaire tombe à l'endroit de la mort et rien ne guide pour y revenir — d'où la recommandation forte de jouer en Décontracté (personnage et monde), voir le README.
- **La saisie de nom exige un clavier physique.** Pas de clavier virtuel accessible pour les configurations 100 % manette.

## Pas encore couvert (prévu)

- **Le pilotage de l'automation avancée** (poser et configurer foreuses, convoyeurs, bras robotiques, réseau électrique via leurs menus) : la lecture au curseur est là (voir l'entrée lecture au curseur ci-dessus), mais les commander reste un jalon dédié à venir.
- **L'écran de remappage des contrôles.**

## Bon à savoir (par design)

- **Triangle est réquisitionné par le mod** comme modificateur d'accessibilité ; le binding natif de la carte est retiré de votre config manette. Le double-tap Triangle ouvre la carte à la place. Si vous désinstallez le mod, repassez par « contrôles par défaut » dans les options du jeu.
- **Un combo access muet signifie « rien à faire ici »** : les commandes contextuelles (réparer, recycler…) ne disent rien hors de leur contexte, c'est voulu.
- **Le déplacement au curseur est une ligne droite, sans pathfinding** — la même information qu'un voyant, philosophie du mod.
