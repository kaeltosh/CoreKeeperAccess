# Changelog — CoreKeeperAccess

*Cette page existe aussi en français : [CHANGELOG.fr.md](CHANGELOG.fr.md).*

## Alpha 2 — June 2026

A big batch since Alpha 1. Most of the game beyond the menus becomes playable: building, trading, farming, boss fights, multiplayer, and a new navigation aid.

### Navigation and orientation

- **Proximity sonar** (toggled from the settings panel): an aid for moving around in tight spaces. The footstep beep is decoupled from the rest, sheets of noise mark the walls around you in all four directions (left/right via panning, deep or mid timbre, dull for a wall, a splash for water or a chasm), and a small "ding" marks nearby objects tile by tile. Three adjustable volumes, each toggleable on its own.
- **Personal map beacons**: a "My beacons" tab where you drop a marker at your position (Cross), rename it, or delete it. Names are remembered per world and per location.

### Building and cursor reading

- **Accessible building and placement**: directional snapping (Triangle + L3) to line up what you place, multi-tile placement at the cursor, rotation (Triangle + R1).
- **Richer cursor reading**: farming (tilled or watered ground, plant state — ready to harvest, thirsty, growing), processing stations (labelled input and output slots, progress percentage), machines, conveyors and electricity, empty or full bucket and watering can.
- **Tool effect-zone size**: for the hoe, watering can, shovel or seeder, the size of the effect area is announced on selection and on every change (Triangle + R1) — e.g. "zone 3x3". These tools no longer announce a bogus "footprint" as if they were furniture.

### Trading

- **Accessible merchant** (Buy and Sell sections, values, balance, sell everything at once) and **full pouch support**: the panel expands automatically, contents are presented in rows, equip and unequip a pouch on the gamepad.

### Combat

- **Accessible boss fights**: symmetrical combat slow-motion (it slows the boss too, so it's not an advantage), an audio cue for the arena center, and a detector for fire zones on the ground.
- **Automatic mortar aiming**: the reticle locks on by itself to the enemy you're aiming at with the laser cane.

### Character and progression

- **Talents tab**: each talent's state is spoken (locked with its prerequisite, available, or maxed out), along with how many points you have left to spend.
- **Controller stats wheel** (hold Triangle, then push the left stick in a direction): quick access to your information without opening a menu, with movement paused while you check. Each direction reads one piece of data — health and barrier, hunger, mana and active minions, active conditions (poisoned, burning, slowed by slime…), world progress, and ore prospecting around you. Health or mana regeneration is appended at the end (e.g. "+4.2/s"). The position command (Triangle + D-pad up) now also tells you your current biome.

### Multiplayer

- **Player management menus are read**: sections, names, and each button's action (admin, ban, invite, view profile, PvP team), both on the dedicated screen and in the pause menu's "Connected players" panel.
- **Confirmation pop-ups are read**: the question and the option labels (Yes / Cancel…) are announced in every dialog.

### Dialogue

- **The Core speaks to you**: its lines are now read by the screen reader.

### Settings and audio

- **In-game accessibility settings panel** (Triangle + Back): navigable and fully voiced, modal, with settings that survive updates (volumes, direction assist, combat slow-motion, normalization, etc.).
- **Audio overhaul**: more accurate volume normalization (it no longer misses short, snappy sounds), volumes adjustable up to 200%, and a dedicated volume for navigation (tile cursor and laser cane).

## Alpha 1, build 54 — June 2026

- **Bosses now have their own beep in the aggro sentinel**: a deep, longer tone repeated about three times per second on a dedicated channel, instead of blending into the regular one-beep-per-second queue. Same positional language (pan, vertical pitch, distance volume).
- **The laser cane sees across chasms and water**: those tiles block walking but not sight or arrows, so the beam now reports the edge (the familiar plop / splash) and keeps going — enemies and walls on the far side are detected. Aim across and shoot.
- **Map fix**: the third-boss map marker that the game itself leaves untranslated ("Larva Boss") is now spoken as "Ghorm the Devourer" (and properly translated in French). Report any other English-only markers you hear.

## Alpha 1, build 52 — June 2026

- **The laser now reports non-hostile targets too**: peaceful creatures (insects, goats, dormant slimes...) and placed objects (mushrooms, dropped items, furniture, digging spots), one target at a time — the closest on the beam. Each side has its own timbre; the name is spoken when the target changes. A hostile in the beam always overrides the peaceful track, so no threat ever gets masked.
- **Your own projectiles are now ignored**: fired arrows no longer trigger the laser or the tile cursor.
- **New: ping sonar on Triangle + L1** — a sound snapshot of everything notable around you (12-tile radius): one beep per target, played from nearest to farthest (the timing carries the distance), with three timbres: hostile, peaceful creature, find (digging spot). Says "Nothing around" when empty. The laser and the aggro sentinel stay quiet during the salvo, then resume. While Triangle is held, L1 no longer switches hotbar slot.

## Alpha 1 (build 51) — June 2026

First version distributed to testers. Everything is new — see the [README](README.md) for the full feature set. Notable items from the final pre-release stretch:

- Cross now confirms name input (world and character) on gamepad; entering and leaving edit mode is announced, with the field's content.
- The intro and ending cinematics are read slide by slide, with an entry announcement ("hold Cross to skip") and a spoken confirmation when skipped.
- The character mode screen (Normal / Casual / Hardcore) no longer cycles modes when pressing Cross: it now announces the current mode and how to change and confirm it.
- The developer fast-load (straight into world 1 with character 1) is now off by default; testers always get the normal menu flow.
