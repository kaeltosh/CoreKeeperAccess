# Problèmes connus — CoreKeeperAccess

*This page is also available in English: [KNOWN_ISSUES.md](KNOWN_ISSUES.md).*

À jour à la 1.0.12 beta. Merci de consulter cette liste avant d'ouvrir une issue — si vous rencontrez un de ces points, inutile de le signaler sauf élément nouveau.

> **Conseil d'installation — désactivez le son spatial Windows** (Dolby Atmos / Windows Sonic pour casque) : il re-mixe la stéréo et brouille tous les repères directionnels (panoramique, gauche/droite, le sonar). La stéréo simple donne un positionnement audio juste.

## Non finalisé dans cette bêta ouverte

- **Certains sons sont encore provisoires** (sonar de proximité, créatures et objets paisibles au laser, son de pose invalide, guidage par balises) : ils fonctionnent, mais les sons définitifs ne sont pas encore choisis — ils peuvent changer.
- **Pas encore validé sur le terrain** : l'alerte d'étourdissement, et la lecture des machines d'automation avancée (industrie) au curseur. C'est codé mais pas confirmé en conditions réelles.
- **Scanner de proximité : l'exclusion de votre propre personnage et de votre compagnon de la catégorie "créatures" n'a pas été vérifiée en multijoueur** — un coéquipier doit rester visible ; pas encore confirmé en conditions réelles.
- **Les statues d'invocation de boss et les invocations du sceptre invocateur sont traduites** — pas encore confirmé en conditions réelles.
- **Changement de barre rapide inversé** : c'est désormais Triangle+L1 (suivante) / Triangle+R1 (précédente), inversé par rapport à avant, suite au retour des testeurs — pas encore confirmé en conditions réelles après l'inversion.
- **Nouveau mode d'apprentissage des commandes** : tenez Triangle (ou R3, ou aucun des deux) et appuyez sur n'importe quel bouton pour entendre ce qu'il fait, sans qu'il s'exécute réellement — façon aide à la saisie d'un lecteur d'écran, en complément du mode nommage des boutons existant. Pas encore confirmé en conditions réelles.
- **Nouveau raccourci pour cycler les profils d'équipement** : fiche perso/inventaire ouvert (pas en station), Triangle+D-pad droite/gauche bascule vers le profil d'équipement suivant/précédent parmi les 3. Pas encore confirmé en conditions réelles.

## Bugs connus

- **Dans la fenêtre bétail, le libellé du bouton reproduction se répète parfois à la fin de la ligne d'état de l'animal.** Un correctif a été codé mais pas encore confirmé en conditions réelles.
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
