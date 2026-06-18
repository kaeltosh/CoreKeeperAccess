# Problèmes connus — CoreKeeperAccess

*This page is also available in English: [KNOWN_ISSUES.md](KNOWN_ISSUES.md).*

À jour à l'alpha 2 (build 1). Merci de consulter cette liste avant d'ouvrir une issue — si vous rencontrez un de ces points, inutile de le signaler sauf élément nouveau.

## Bugs connus

- **Un générateur posé sur un câble ancien est muet au curseur de tuile.** Le réseau de câbles indestructibles des ruines du Core masque l'objet posé dessus (deux objets passifs, le collider du câble gagne). Le générateur fonctionne ; le curseur ne le nomme juste pas.
- **Les sols notables sont parfois annoncés en anglais brut** (nom interne de la tuile), quelle que soit la langue du jeu. Rare : le sol standard est muet par design, seuls les sols spéciaux sont concernés.

## Limites actuelles

- **Le nom des objets n'est annoncé qu'au premier ramassage de chaque type.** Comportement natif du jeu (les ramassages suivants ne font qu'incrémenter un compteur visuel muet). Une version future pourrait hooker l'ajout réel à l'inventaire.
- **La lumière et l'obscurité ne sont pas perçues.** L'éclairage temps réel est rendu par shader, illisible pour le mod. Approche prévue : lire les *sources* de lumière (torches en balises audio) plutôt que le rendu.
- **Pas d'assistance pour retourner sur le lieu de décès.** Hors mode Décontracté, l'inventaire tombe à l'endroit de la mort et rien ne guide pour y revenir — d'où la recommandation forte de jouer en Décontracté (personnage et monde), voir le README.
- **L'écran d'apparence du personnage (corps, peau, cheveux…) n'est pas adapté.** Sélecteurs carrousel purement cosmétiques ; ne touchez à rien et validez directement si l'apparence vous indiffère.
- **La saisie de nom exige un clavier physique.** Pas de clavier virtuel accessible pour les configurations 100 % manette.

## Pas encore couvert (prévu)

- **Le pilotage de l'automation avancée** (poser et configurer foreuses, convoyeurs, bras robotiques, réseau électrique via leurs menus) : la lecture au curseur est là (voir l'entrée lecture au curseur ci-dessus), mais les commander reste un jalon dédié à venir.
- **L'écran de remappage des contrôles.**

## Bon à savoir (par design)

- **Triangle est réquisitionné par le mod** comme modificateur d'accessibilité ; le binding natif de la carte est retiré de votre config manette. Le double-tap Triangle ouvre la carte à la place. Si vous désinstallez le mod, repassez par « contrôles par défaut » dans les options du jeu.
- **Un combo access muet signifie « rien à faire ici »** : les commandes contextuelles (réparer, recycler…) ne disent rien hors de leur contexte, c'est voulu.
- **Le déplacement au curseur est une ligne droite, sans pathfinding** — la même information qu'un voyant, philosophie du mod.
