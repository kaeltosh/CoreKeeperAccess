# Changelog — CoreKeeperAccess

*Cette page existe aussi en français : [CHANGELOG.fr.md](CHANGELOG.fr.md).*

## 1.0.8 (open beta) — July 2026

Item reinforcement, boss health callouts, recipe category switching, and fixes.

### Additions

- **Item reinforcement at the repair station.** Triangle+Down toggles between repair and reinforce mode (announced); Triangle+Right applies the current mode, and the cost shown (Triangle+Up) follows the chosen mode. Reinforcing boosts an item's maximum durability beyond its normal cap.
- **Boss health callouts every 10%.** During a boss fight, remaining health is announced in 10% steps, both dropping and recovering (never masking a heal in progress).
- **Switch between recipe categories on a crafting station that bundles several.** Some stations also keep the recipes of an earlier model (for example, an upgraded anvil that keeps the previous anvil's recipes): Triangle+Right/Left moves between categories, announcing the name of the model currently shown. Silent if the station only has one category.
- **"Silence cursor interaction" setting.** In the accessibility panel, Navigation category: mutes the redundant "interaction available" announcement while using the detached tile cursor (which already announces the hovered object). Off by default.

### Fixes

- **Upgrade forge: input and output now correctly announced.** The slot where you drop the item and the one previewing the result used to fall into the wrong generic category; they're now attached to the crafting section with clear roles, and the previewed result (stat gain) is now readable.
- **The companion (laser cane) no longer blocks a farther object.** A placed familiar or servant standing between you and a farther plant or chest no longer blocks its detection.

---

## 1.0.7 (open beta) — July 2026

Hotbar jump wheel, directional collision detector, Azeos combat, and fixes.

### Additions

- **Hotbar jump wheel.** Hold R1 to open a 10-position wheel on the left stick (walking freezes while held) or L1 for the same wheel on the right stick (the laser cane goes quiet while held): point a direction to equip that slot of the active bar directly. Triangle+Circle jumps straight to the first slot of the first bar. Toggleable in settings.
- **Directional collision detector.** Optional aid (off by default, enable it in settings): in the direction you push the left stick, an increasingly loud high-pitched sound warns you before an impassable wall, pit, or water, with adjustable range.
- **Improved detection of Azeos's lightning pillars.** The fight against Azeos the Sky Titan now tracks each pillar's actual behavior more reliably, with an earlier warning.
- **Arena drone for Titan fights.** During a Titan-type boss fight, a sound drone marks the center of the arena, with distinct behavior in and out of combat.

### Fixes

- **"Swap hotbar" arrows hidden by default.** The two visual buttons at the ends of the action bar no longer show up by default — they added nothing in audio and generated a stray announcement ("press to swap hotbar").
- **Stalagmites silent again at the cursor and laser.** This scenery was never mineable or interactive; it no longer gets confused with other objects, without touching the rest of the object index.

---

## 1.0.6 (open beta) — June 2026

Familiar talent tree, relay beacon drone, and POI list improvements.

### Additions

- **Familiar talent tree.** Accessible from the equipment screen: press the button on your familiar's slot to open its full talent tree. Each talent shows its name and status (locked, available, or purchased). Triangle+Right purchases a talent; the points available are announced on the button. A "Reset talents" entry at the end of the list refunds all spent points, with a TTS confirmation of success or insufficient coins.
- **"Nearby relay" drone.** When an unactivated teleport relay is visible on screen, a pulsing sine drone activates. Pan indicates left/right, pitch indicates forward/backward. The drone stops as soon as the relay leaves the screen or is activated. Added to the sound guide (Exploration category).

### Improvements

- **POI list reorganized and filtered.** Points of interest are now sorted alphabetically by name. Activated teleport relays appear in the list. Portals and relays that haven't been discovered yet are hidden, matching the behavior of the native map.

### Fixes

- **Set bonus name corrected.** Armor sets were announced with their internal English identifier (e.g. "LarvaSet") instead of the localized name (e.g. "Larva Armor").

---

## 1.0.5 (open beta) — June 2026

Enriched equipment tooltips and bug fixes.

### Additions

- **Set bonus in tooltip.** The tooltip of an armor piece belonging to a set now shows the set name and how many pieces you have equipped (e.g. "Larva Armor, 2/3"). Triangle+Up reveals the full detail: each threshold bonus (active or inactive) and the list of missing pieces.
- **Familiar level.** The equippable familiar's tooltip now shows its level and the current level's XP progress as a percentage (e.g. "Level 5, 30%").

### Fixes

- **Correct max-servant count.** The stats wheel was showing "0 servants max" even without dedicated equipment — the base value of 1 was not being counted.
- **Sonar silent on the main menu.** When leaving a world, the proximity sonar kept playing in the main menu. It now stops as soon as the world is exited.

---

## 1.0.4 (open beta) — June 2026

Hive Mother combat, summoning sigil guide for all bosses, and acid ground detection.

### Additions

- **Hive Mother combat.** The mod announces the acid spit ("The hive is spitting!") just before the projectile launches, and the enrage ("The hive is enraging!") on phase change. Hatching eggs are tracked by the aggro sentinel and beep positionally — you know where they are without seeing them.
- **Summoning sigil audio guide.** When you enter a boss room, a subtle drone points toward the summoning rune (left/right pan, up/down pitch). At 1.5 tiles away, the mod announces "Summoning rune".

### Fixes

- **Laser cane false positive on hive eggs.** The laser cane was beeping on passive Hive Mother eggs as if they were enemies. They are now ignored until they hatch.

### Improvements

- **Hazard zone detection is more reactive.** Acid ground and fire traps are now scanned twice as fast — helpful when running or when a mortar shot suddenly creates a fire zone.

---

## 1.0.3 (open beta) — June 2026

Special ground tiles and growing plants now named correctly.

### Fixes

- **Special ground tiles named correctly.** Some ground types (chrysalis, slime ground…) were announced with their internal technical name instead of a readable label. The cursor now resolves the actual ground content based on the biome.
- **Growing plants without French translation.** A few plants (`GrubKapokPlant`, `CoralRootPlant`, `GleamRootPlant`) were announced in English. They now have their French name.

---

## 1.0.2 (open beta) — June 2026

Game-menu readability, a screen to learn the sounds, and an important fix to each world's own navigation memory.

### Additions

- **Sound learning menu.** From the help menu, a new screen lets you listen to each of the mod's sounds, sorted by category (tile and movement sonification, combat); the sound plays on hover so you can learn to recognise them.
- **Quick-start guide.** A new document (English and French), linked from the README, walks a new player through the basics: game overview, perception by ear (tile cursor, laser cane, sonar), world and character creation, core controls.

### Improvements

- **Options-menu bars read as a percentage.** Slider settings (volumes in particular) used to announce an unreadable string of symbols; they now announce a clear percentage that updates as you change the value.
- **Clearer world creation.** The "random seed" button is now announced, and the two tabs at the top of the screen (General and World) are too, with a word on what each contains.
- **Refined controller learning.** Buttons are named more simply, a reminder points out the two small central buttons if you haven't tried them, and the final screen can be replayed at will.
- **Customisation names are localisable.** Character variant names (colours, hairstyles, body types) now live in the language files — English is provided, and other languages can be added easily.

### Fixes

- **Navigation and journal follow the world, not the save slot.** If you deleted a world and then created a new one in the same slot, the new world inherited the old one's beacons, navigation network and journal. Each world now has its own memory, and deleting a world cleanly erases its navigation data.

## 1.0.1 (open beta) — June 2026

A small polish update on top of 1.0.

### Additions

- **Skill level-ups are announced.** Whenever a skill (mining, running, melee, magic…) gains a level, the mod announces its name and the level reached. At the milestones that grant a talent point, both are merged into a single announcement ("… level 15, new talent point available") so neither cuts the other off.
- **The character creation screen is now readable cosmetically.** Variants that weren't announced at all now are: skin, hair, eye and clothing colours (distinct, correct names per category), body types named by build (Sturdy / Slim), and hairstyles.

### Fixes

- **Journal: the Core's awakening dialogue no longer shows too early.** It only appears in the journal once the Core is actually activated, instead of showing up as soon as the world loads.

## 1.0 (open beta) — June 2026

CoreKeeperAccess leaves alpha and opens to a wider audience. This 1.0 consolidates Alpha 2 with fixes, polish and a large internal rework for the long run — every Alpha 2 feature below is included.

### Building and controls

- **Place objects in a straight line while walking.** Detach the tile cursor with the D-pad onto a cell next to you, then hold the place button (LT): the relative offset locks, the cursor follows you as you move, and the object is placed cell after cell — so you can lay a clean line of walls or floor instead of the game scattering them onto "the nearest free spot". A discreet sound ticks on each cell crossed (a counter by ear); pairs well with assisted direction (Triangle + L3) for perfectly straight lines.
- **Walk-to (Cross) now stops at the center of the tile.** It used to stop as soon as it was within half a tile of the target, leaving you off-center and skewing your placement/interaction reach (e.g. reaching a workbench one tile diagonally). You now settle near the center.
- **Quick hotbar swap on controller.** Triangle + right / Triangle + left switch to the next / previous hotbar row in-game (the D-pad being taken by the tile cursor); the item now in hand is announced.
- **More reliable automation readout.** Conveyor direction, electrical state (generating / powered / unpowered) and the charge of a cable running under a structure are now announced correctly, and an ore deposit is no longer drowned out by the surrounding machines when you hover it.
- **Default settings tuned from real play.** Out-of-the-box defaults were calibrated during play (master and navigation volumes, proximity sonar on by default, quieter health alerts, total-owned readout on pickup, etc.). This only affects a fresh install — your own saved settings are untouched, and everything stays adjustable in the settings panel.

### Fixes and polish

- **The pause menu no longer opens on top of the mod's own menus.** Pressing Start while a mod menu was open (controller learning, settings panel, context menu, name entry) used to pop the game's pause menu over it. It now waits until you close the mod menu.
- **D-pad directions are named clearly.** In the help menu and the controller learning mode, the four directions are spoken as "directional pad up/down/left/right" (or "D-pad …" in Xbox style), so they're no longer confused with the stick.

### Under the hood

- **Large internal refactor**: the menu engine is now shared between the settings panel and the context menus, and the map reader is restructured into self-contained sections. Nothing you can hear — it's groundwork to add future menus (a codex, a guided tutorial) cleanly and to keep the mod robust against game updates.

## Alpha 2 — June 2026

A big batch since Alpha 1. Most of the game beyond the menus becomes playable: building, trading, farming, boss fights, multiplayer, and a new navigation aid.

### Navigation and orientation

- **Proximity sonar** (toggled from the settings panel): an aid for moving around in tight spaces. The footstep beep is decoupled from the rest, sheets of noise mark the walls around you in all four directions (left/right via panning, deep or mid timbre, dull for a wall, a splash for water or a chasm), and a small "ding" marks nearby objects tile by tile. Three adjustable volumes, each toggleable on its own.
- **Personal map beacons**: a "My beacons" tab where you drop a marker at your position (Cross), rename it, or delete it. Names are remembered per world and per location.
- **Beacon-network guidance**: beyond dropping markers, you can be guided to any point on the map (a beacon or a point of interest). Cross on the target opens a menu: **network** guidance (which follows the path of your torches and beacons, hop by hop) or **direct** guidance (as the crow flies). A repeating chime gives you the direction (left/right via panning, ahead/behind via pitch) and rises in volume as you get closer; arrival is announced. The network builds and recalculates itself from your torches that are within sight of one another.

### Building and cursor reading

- **Accessible building and placement**: directional snapping (Triangle + L3) to line up what you place, multi-tile placement at the cursor, rotation (Triangle + R1).
- **Richer cursor reading**: farming (tilled or watered ground, plant state — ready to harvest, thirsty, growing), processing stations (labelled input and output slots, progress percentage), machines, conveyors and electricity, empty or full bucket and watering can.
- **Tool effect-zone size**: for the hoe, watering can, shovel or seeder, the size of the effect area is announced on selection and on every change (Triangle + R1) — e.g. "zone 3x3". These tools no longer announce a bogus "footprint" as if they were furniture.

### Trading

- **Accessible merchant** (Buy and Sell sections, values, balance, sell everything at once) and **full pouch support**: the panel expands automatically, contents are presented in rows, equip and unequip a pouch on the gamepad.

### Combat

- **Accessible boss fights**: symmetrical combat slow-motion (it slows the boss too, so it's not an advantage), an audio cue for the arena center, and a detector for fire zones on the ground.
- **Automatic mortar aiming**: the reticle locks on by itself to the enemy you're aiming at with the laser cane.
- **Status sound alerts**: when a dangerous status hits you, a sound warns you right away — a deep, ominous cue for damage over time (fire, acid, radiation), another for the stun that locks you. And the stats wheel (Triangle + left stick, East sector) now gives you the exact damage-per-second of each effect. Toggleable from the settings panel.
- **Low-health alerts**: as your health drops, two tiers warn you without having to check anything. Below 60%, a dry double beep then a slow heartbeat that comes back at a steady pace; below 20%, a rising siren then the same heartbeat, but much faster — impossible to miss. As you heal, the heartbeat slows down then goes silent as soon as you're back above 60%. Toggleable from the settings panel.

### Character and progression

- **Talents tab**: each talent's state is spoken (locked with its prerequisite, available, or maxed out), along with how many points you have left to spend.
- **Controller stats wheel** (hold Triangle, then push the left stick in a direction): quick access to your information without opening a menu, with movement paused while you check. Each direction reads one piece of data — health and barrier, hunger, mana and active minions, active conditions (poisoned, burning, slowed by slime…), world progress, and ore prospecting around you. Health or mana regeneration is appended at the end (e.g. "+4.2/s"). The position command (Triangle + D-pad up) now also tells you your current biome.
- **Souls tab**: each slot now states its status — to unlock, enabled, or disabled — on top of the name and effect of souls you own, so you can find your way around the wheel.
- **Upgrade forge**: upgrades the item you placed by one tier (Triangle + right), with the material cost on demand (Triangle + up).

### Multiplayer

- **Player management menus are read**: sections, names, and each button's action (admin, ban, invite, view profile, PvP team), both on the dedicated screen and in the pause menu's "Connected players" panel.
- **Confirmation pop-ups are read**: the question and the option labels (Yes / Cancel…) are announced in every dialog.

### Dialogue

- **The Core speaks to you**: its lines are now read by the screen reader.
- **Dialogue journal**: a "Journal" tab on the map archives what the Core tells you, world by world, so you can re-read it at your own pace — handy since some dialogues only play once and get overwritten fast. Drop-down navigation: the list of conversations, you open the one you want (right) and step back to the list (left). Dialogues you already went through (including the Core's activation) are reconstructed, and tutorial messages get their own section to avoid clutter.

### Settings and audio

- **In-game accessibility settings panel** (Triangle + Back): navigable and fully voiced, modal, with settings that survive updates (volumes, direction assist, combat slow-motion, normalization, etc.).
- **Audio overhaul**: more accurate volume normalization (it no longer misses short, snappy sounds), volumes adjustable up to 200%, and a dedicated volume for navigation (tile cursor and laser cane).
- **Unified menu sounds**: every mod menu (settings panel, context menus, wheels, map reader and its journal) now shares the same navigation sounds, normalized and driven by the master volume. The stats wheel stays silent on hover (it already announces the value).
- **Button name style**: an "Xbox-style button names" setting (PlayStation by default) — the help menu and anything that names a button show Cross / Triangle / L2 or A / Y / LT depending on your choice.

### Help menu and controller discovery

- **Contextual help menu** (hold Triangle, tap up twice): the list of everything you can do where you are. Mod commands (with their shortcut), runnable straight from the list, and the game's own commands read from your real key bindings — correct even if you remap. The list changes with context (gameplay, inventory, map).
- **Controller discovery mode**: on your very first time entering gameplay, a learning mode starts — press a button or move a stick and the mod tells you its name and physical position (and reminds you once that the sticks can be clicked). You leave it with a double-press of Circle, and a final message teaches you how to reopen help. You can relaunch it anytime from the first entry of the help menu. Since the main menu stays keyboard-accessible, a beginner can create their character then learn the controller once in game.
- **Inventory trigger shortcuts**: in the inventory, R2 transfers the selected item, L2 drops it on the ground, and Triangle + L2 throws it in the trash.

### Installation

- **Double-click install**: no command line, no need to know where the game lives. Download the zip, extract it, double-click `Installer.cmd`, done. Your Steam install of Core Keeper is found automatically, on any drive. The window stays open at the end so your screen reader can read the result, and if a write is denied it tells you clearly to re-run as administrator.

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
