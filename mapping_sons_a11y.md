# Palette sonore a11y — Core Keeper (présélection par sonorité)

Les 87 sons retenus par l'utilisateur **pour leur sonorité** (matière première réaffectable, sans lien avec leur fonction d'origine en jeu). Tous jouables nativement via `SfxID.<nom>` (correspondance exacte vérifiée).

Classement **objectif** issu de l'analyse acoustique (`analyse_sons_a11y.csv` : durée, centroïde spectral = registre, flatness = tonal/bruité, pitch dominant). L'« usage a11y suggéré » découle du **timbre**, pas de la fonction de jeu — à affiner à l'oreille.

> Le mapping *événement a11y → son* viendra plus tard, quand on listera les événements à sonoriser. Ceci est l'**inventaire** de la palette.

---

## 1. CLIC / BIP court (12) — navigation, curseur, focus, toggles
Très courts (< 0,35 s), nets. Idéaux pour du feedback rapide et répétable sans fatigue.

| son | durée | registre |
|---|---|---|
| twitch | 0,06 s | 1973 Hz |
| inventory_select | 0,11 s | 2202 Hz |
| tock | 0,11 s | 1779 Hz |
| chestclose | 0,12 s | 2310 Hz |
| chestopen | 0,12 s | 2103 Hz |
| maxWindupBlip2 | 0,12 s | 1979 Hz |
| ridiculous | 0,17 s | 1632 Hz |
| shoop | 0,19 s | 950 Hz |
| fed | 0,23 s | 1065 Hz |
| spotted | 0,25 s | 2422 Hz |
| maxWindupBlip3 | 0,28 s | 1083 Hz |
| heal | 0,31 s | 1064 Hz |

## 2. TON AIGU clair (23) — signaux positifs & alertes attentionnelles
Registre haut (centroïde > 2,5 kHz), perçant, attire l'attention. Pour collecte / succès / level-up et alertes qui doivent « ressortir ».

| son | durée | registre |
|---|---|---|
| itemSwitch3 | 0,13 s | 3227 Hz |
| TerrariaSlime_Hurt | 0,15 s | 6113 Hz |
| jump | 0,20 s | 3076 Hz |
| swoosh | 0,20 s | 3259 Hz |
| FIXME_menu_select ⚠️ | 0,23 s | 11238 Hz (très sifflant) |
| tallCrystalGrassDmg | 0,25 s | 4143 Hz |
| ore | 0,35 s | 3609 Hz |
| proximity_sensor_off | 0,53 s | 4721 Hz |
| remote_clicker_2_01 | 0,55 s | 5911 Hz |
| proximity_sensor_set | 0,56 s | 7769 Hz |
| glassDamage | 0,62 s | 5504 Hz |
| charge_bar_ui_1 | 0,73 s | 3862 Hz |
| player_hit_object_rope_1_01 | 0,81 s | 4046 Hz |
| metalImpactSmall | 0,84 s | 2954 Hz |
| FIXME_chop_wood1 ⚠️ | 0,90 s | 6237 Hz |
| wallSand | 0,94 s | 5768 Hz |
| clapperOld | 0,95 s | 2567 Hz |
| successTone | 1,03 s | 5744 Hz |
| twinkle2 | 1,25 s | 8289 Hz |
| menu_denied | 1,30 s | 3253 Hz |
| player_destroy_metal_2_02 | 1,30 s | 3431 Hz |
| skillPointHighTwinkle1 | 1,40 s | 2726 Hz |
| birdMagic1 | 1,48 s | 9851 Hz |

## 3. TON MEDIUM (11) — infos neutres, états, compteurs
Registre moyen, ni perçant ni sourd. Pour de l'information « de fond » non urgente.

| son | durée | registre |
|---|---|---|
| loco | 0,43 s | 2055 Hz |
| maxWindupBlip1 | 0,47 s | 965 Hz |
| ui_plop_1_01 | 0,50 s | 1115 Hz |
| robot_enemy_phrase_1_02 | 0,61 s | 1429 Hz |
| stoneBricksDamage2 | 0,64 s | 919 Hz |
| minionFireMiteChirpHigh | 0,84 s | 2065 Hz |
| robot_enemy_phrase_1_03 | 0,91 s | 1767 Hz |
| drumkitB2 | 1,00 s | 1155 Hz |
| minionFireMiteChirpLow | 1,03 s | 1483 Hz |
| bigFireworkCountdown2 | 1,21 s | 915 Hz |
| puddle2 | 0,45 s | 1777 Hz |

## 4. SON GRAVE (9) — impacts, refus, alertes sourdes
Registre bas (< 800 Hz), « lourd ». Pour du négatif / impact / événement pesant.

| son | durée | registre |
|---|---|---|
| itemSwitch5 | 0,06 s | 676 Hz |
| bubble | 0,09 s | 304 Hz (très grave) |
| Plop | 0,09 s | 555 Hz |
| menu_select2 | 0,11 s | 724 Hz |
| jump2 | 0,14 s | 750 Hz |
| slimeJump3 | 0,18 s | 644 Hz |
| drinking | 0,41 s | 523 Hz |
| knockback | 0,72 s | 396 Hz |
| scarletDestroy | 1,38 s | 429 Hz |

## 5. TEXTURE bruitée (18) — déplacement, actions physiques, ambiances
Spectre étalé (bruité), peu tonal. Pour sols/footsteps, creuser/frapper, frottements.

| son | durée | registre |
|---|---|---|
| itemSwitch | 0,18 s | 4397 Hz |
| shoveldig | 0,35 s | 4843 Hz |
| grassImpact | 0,36 s | 7522 Hz |
| bow | 0,36 s | 5663 Hz |
| spearImpact | 0,38 s | 8484 Hz |
| robot_patroller_movement_one_shot_2_2_01 | 0,48 s | 7974 Hz |
| ui_offhand_click_1_01 | 0,52 s | 7411 Hz |
| remote_clicker_1_01 | 0,52 s | 10539 Hz |
| grassImpactHard | 0,54 s | 6947 Hz |
| gate_move_1_02 | 0,71 s | 8328 Hz |
| gate_move_1_01 | 0,71 s | 8469 Hz |
| drumkitDs2 | 0,83 s | 6116 Hz |
| hit_destroy_robot_scrap_1_02 | 0,90 s | 7096 Hz |
| hit_destroy_robot_scrap_1_01 | 0,90 s | 9386 Hz |
| magic_beam_end_1_03 | 0,91 s | 7046 Hz |
| robot_patroller_movement_one_shot_3_01 | 0,94 s | 8427 Hz |
| mummyProjectile2 | 1,34 s | 6264 Hz |
| stone_void_wall_hit_1_03 | 1,40 s | 6308 Hz |

## 6. LONG / boucle (14) — états persistants, sources de proximité
Longs (> 1,5 s) ou bouclés. À jouer en loop (danger continu, source proche) ou à **trimmer** sur l'attaque pour un usage court (les impacts à longue traîne).

| son | durée | registre | note |
|---|---|---|---|
| fish_splash_1_01 | 1,50 s | 5016 Hz | |
| metal_footstep_1_03 | 1,52 s | 3022 Hz | traîne |
| wall | 1,56 s | 1716 Hz | |
| fish_splash_1_02 | 1,63 s | 4890 Hz | |
| zealotBladeShimmer3 | 1,66 s | 7488 Hz | scintillant |
| melody_C6 | 1,69 s | 805 Hz | note tenue |
| wood_mining_2_02 | 1,70 s | 2793 Hz | |
| twinkle | 1,85 s | 4973 Hz | trimmable |
| stone_wall_hit_2_02 | 1,90 s | 4752 Hz | trimmable |
| rock_wood_mining_1_02 | 2,22 s | 2411 Hz | |
| zealotBladeImpact2 | 2,57 s | 5284 Hz | trimmable |
| **lowhealth** | **9,03 s** | 105 Hz | alarme/battement bouclé, pas un bip |
| **lowhleath2** | **9,03 s** | 123 Hz | idem (2e candidat) |
| **torchFireCracklingLoop01** | **32,6 s** | 2536 Hz | boucle d'ambiance (feu) |

---

## Observations
- **`lowhealth` / `lowhleath2` durent 9 s** : ce sont des **boucles d'alarme** (très graves, ~110 Hz), pas des signaux ponctuels. Pour une alerte « tic » courte, prendre plutôt un bip de la palette 1 ou 4.
- **`torchFireCracklingLoop01` = 32 s** : pure ambiance, à ne jouer qu'en loop atténué (proximité d'une source de feu/lumière).
- Plusieurs « LONG » (twinkle, zealotBladeImpact, stone_wall_hit) sont des **impacts nets + longue traîne** → trimmables sur l'attaque pour du court.
- **`FIXME_menu_select`** monte à 11 kHz (très sifflant) — à tester, peut être fatigant.
- Le pan / distance / pitch étant gérés nativement par l'API audio du jeu, **aucune variante à pré-générer** côté AccessSound pour ces sons.
