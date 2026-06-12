# Journal des versions — CoreKeeperAccess

*This page is also available in English: [CHANGELOG.md](CHANGELOG.md).*

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
