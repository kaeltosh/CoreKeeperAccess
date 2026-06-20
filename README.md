# CoreKeeperAccess — accessibility mod for Core Keeper

*Cette page existe aussi en français : [README.fr.md](README.fr.md).*

A mod that makes **Core Keeper** playable by **blind players**: everything goes through the screen reader's speech (NVDA) and spatialized audio feedback. Menus, inventory, crafting, exploration, combat, teleportation — the goal is to play the full game, independently, on a gamepad.

**Status: alpha.** The mod is under active testing. Target audience for this phase: testers comfortable with cloning a GitHub repository and copying files.

## Philosophy

- **Equality with a sighted player, not assistance.** The mod reveals what a sighted player perceives (the environment, threats, clues); it does not play for you: no magic pathfinding, no spoilers. The game's puzzles remain yours to solve.
- **Information through sound first**: spatialized audio (left/right panning, low/high pitch), speech for what has a name.
- **Client-side only**: the mod reads the game and simulates native inputs; it does not change the game's rules.

## Requirements

- **Core Keeper** (Steam, Windows).
- **NVDA** running before the game starts. (TTS goes through the Tolk library; NVDA is the only tested screen reader, a SAPI fallback is theoretically possible.)
- **A gamepad.** The mod is designed and tested with a controller (DualSense; an Xbox controller should work too). Buttons are named PlayStation-style here: Cross = A, Circle = B, Square = X, Triangle = Y.
- A keyboard for typing names (world, character).
- Mod announcements available in **English** and **French** (follows the game's language).

## Installation

### Double-click (recommended)

1. Download the release zip from the **[Releases](https://github.com/kaeltosh/CoreKeeperAccess/releases)** page.
2. Extract it anywhere (right-click the zip → "Extract All").
3. **Double-click `Installer.cmd`.** That's it — no command line, no path to type. Your Steam install of Core Keeper is found automatically, on any drive.
4. A window opens, reports what it did, and waits for you to press a key so your screen reader can read the result.
5. Start NVDA, then the game. At the main menu you should hear: "Accessibility mod loaded", followed by the version (for example "alpha 2, build 1").

Notes:
- **First launch warning.** Windows may say the file "came from another computer" (Mark of the Web / SmartScreen). This is normal for any downloaded script. Choose "More info" → "Run anyway", or right-click `Installer.cmd` → Properties → tick "Unblock".
- **"Access denied"?** If the installer reports a denied write (game under `Program Files` with strict permissions), right-click `Installer.cmd` → "Run as administrator", and run it again. It does not ask for admin on its own when it isn't needed.
- **Game not found?** In the rare case auto-detection fails, run from a console: `powershell -ExecutionPolicy Bypass -File .\install.ps1 -GamePath "<path to Core Keeper>"`.
- To update later, just download the new zip and double-click `Installer.cmd` again (game closed).

### With the script directly (alternative)

Open PowerShell **in the extracted folder** and run:

```
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

The `-ExecutionPolicy Bypass` part is required: by default Windows refuses to run downloaded PowerShell scripts. It only applies to this one command.

### Manual install (alternative)

1. Copy the `dist/CoreKeeperAccess` folder into the game's mods folder:
   `<Core Keeper>/CoreKeeper_Data/StreamingAssets/Mods/`
   (typical Steam path: `C:\Program Files (x86)\Steam\steamapps\common\Core Keeper`).
2. Copy the two DLLs from `dist/natives` (`Tolk.dll` and `nvdaControllerClient64.dll`) **to the game's root folder**, next to `CoreKeeper.exe`.

## First game: recommended difficulty

Pick the **Casual** mode **for both the character AND the world**. The reason matters: in the other modes, dying drops your inventory at the place of death, and the mod does not yet offer any assistance to find your way back there — your items would be very hard to recover. In Casual, you keep everything when you die.

**Uninstall**: delete the `Mods/CoreKeeperAccess` folder and the two DLLs from the game's root. If you remove the mod, go through "default controls" in the game's options to restore the map button on Triangle (the mod takes it over, see below).

## What the mod covers today

- **All menus**: titles, options, sliders, world/character selection and creation, read as you navigate; multiplayer menus (player management, confirmation pop-ups).
- **Name input**: edit mode entry and exit announced, content read aloud, confirm with Cross.
- **Intro and ending cinematics**: text read slide by slide, skip announced.
- **Inventory and crafting**: section-based navigation, recipes with missing materials, stats sheet, talents (state and points to spend), souls (each slot's state), tabs, merchant and pouches.
- **Exploration**: sonified tile cursor, ore prospecting, announcements for placed objects and nearby interactions, the game's floating messages, proximity sonar for tight spaces.
- **Building and farming**: multi-tile placement at the cursor with directional snapping and rotation, ground and plant reading, processing stations and automation basics (conveyors, electricity) read at the cursor.
- **Combat**: laser scanning cane, aggro sentinel (beeps when a monster is attacking you), accessible boss fights (symmetrical slow-motion, arena center, fire zones), automatic mortar aiming.
- **Dialogue**: the Core's lines are read, and a journal archives them for re-reading.
- **Teleportation, map and navigation**: waypoints navigable as a list (direction, distance, biome), points of interest, personal beacons, and audio guidance to any point (torch network or as the crow flies).

## Controls guide

### The game's native controls (kept as-is)

The mod only intercepts what is listed further down (Triangle, plus the D-pad and bumpers while the inventory is open). Everything else is the vanilla game:

In the world:

- **Left stick**: move.
- **Right stick**: aim, the character turns (the mod grafts the laser cane onto it, see below).
- **RT**: use the held item — attack, mine continuously, fish…
- **LT**: secondary interaction — place the held item, dig with a shovel.
- **Cross**: interact with what is in front of you; rotate the item being placed.
- **Circle**: use the off-hand item.
- **LB / RB**: previous / next hotbar item (the mod announces the held item).
- **L3**: torch quick-swap. **R3**: run faster.
- **Square**: open and close the inventory. **Start**: pause.
- **Musical instrument in hand**: almost every button plays notes, Triangle included (the only case where the mod leaves it its native role).

Inventory open, kept as-is: Cross (pick up / put down, take all), RT (quick move), LT (drop), Circle (close). The native D-pad and bumpers (sort, quick stack, hotbar pages, pick up half) are however requisitioned for navigation — their functions are relocated to the action wheel, see the inventory section.

### The access key: Triangle

Triangle is taken over by the mod as its **accessibility modifier** (its native "open map" action is relocated; see double-tap). While Triangle is held, the D-pad, sticks and bumpers trigger commands:

- **Triangle + up**: your **location** in the world (position, coordinates, biome); on the detached cursor, on the map, or in a station, the **details** of the current element (pointed tile, destination, repair cost…).
- **Triangle + down**: inventory open, transfer the selected item. (In the world, health / hunger / mana have moved to the stats wheel.)
- **Triangle + right**: **upgrade** the item placed in the forge; otherwise, **repair** the selected item at the repair station.
- **Triangle + left**: **sell everything** at a merchant; otherwise, **salvage everything** at the station. (Ore prospecting has moved to the stats wheel.)
- **Triangle + L1**: ping sonar — a sound snapshot of everything notable around you (12-tile radius): one beep per target, nearest to farthest, with three timbres (hostile, peaceful creature, find). "Nothing around" if empty. While Triangle is held, L1 does not switch hotbar slot.
- **Triangle + left stick**: stats wheel — push the stick toward a sector to read one piece of data without opening a menu (movement is paused while you check): health and barrier, hunger, mana and minions, active conditions (poisoned, burning…), world progress, ore prospecting around you. Health/mana regeneration is appended at the end.
- **Triangle + R1**: for a field tool (hoe, watering can, shovel, seeder), switches to the next effect-zone size (announced). For a placeable object, rotates it.
- **Triangle + L3**: toggles **direction assist** — your movement snaps to the four cardinal directions, handy to walk straight and line up a build. Stays on until toggled again.
- **Triangle + Back (Select)**: open the accessibility settings panel (volumes, direction assist, combat slow-motion, proximity sonar, audio normalization…). D-pad navigation, settings remembered and preserved across versions.
- **Double-tap Triangle**: open the map anywhere (points-of-interest category).

A combo outside its context says nothing: if it is silent, it has no meaning here.

### Menus

- Native D-pad navigation, everything is read. Left/right adjusts sliders and selectors.
- **Name field** (world, character): opening announces "Editing" plus the content. Type on the keyboard. **Cross = confirm**, Circle or Escape = cancel.
- **Cinematic**: the text reads itself. **Hold Cross for one second = skip.**

### Inventory (windows open)

- **LB / RB**: previous / next section (hotbar, bag, equipment, crafting, chest, statistics…).
- **D-pad**: move within the section.
- **Cross**: pick up / put down, activate a tab, **craft** the selected recipe (the result lands "in hand").
- **RT**: quick-move an item. **LT**: drop.
- **Action wheel on the left stick**: push the stick toward a sector (the action is announced), **R3 click = execute**. Actions: sort, quick stack, pick up half, next/previous hotbar page, trash.

### World (no windows open)

Two complementary tools to perceive space, and they speak the same sound language (the same tile produces the same sound through both):

- **The tile cursor is your hand**: it feels the terrain tile by tile around you, names what it touches, and it is also how you act (mine, place, walk).
- **The laser cane is your long-range white cane**: it points in the right stick's direction and tells you what lies straight ahead — the first obstacle, and the threats along the way.

- **Tile cursor on the D-pad**: it detaches from the character and inspects tile by tile, with a sound per step (panning = left/right, pitch = up/down). Moving with the left stick snaps it back to the character.
  - Cursor sounds: soft tick = free tile; material sound = wall or block; ding = ore in the wall; small high-pitched marker added = interactive object; plop = pit; splash = water. "Sealed wall" = indestructible, don't bother.
- **Cross on the cursor's tile**: mine (wall), interact (object), or walk there in a straight line (empty tile).
- **LT**: place the held item on the cursor's tile (dig, if a shovel is equipped).
- **Laser cane on the right stick**: a beam sweeps in the stick's direction, plays the sound of the first blocking tile (the "wall ahead") and flags enemies along the path with a positional beep plus their name. Peaceful creatures and placed objects get their own softer timbres (a hostile always overrides them). Chasms and water do not stop the beam: you hear the edge (plop / splash), then what lies beyond — aim across and shoot.
- **Aggro sentinel**: automatic. Queued beeps = that many monsters currently attacking you. A **boss** gets its own deep, fast beep on a dedicated channel — unmistakable.
- Automatic announcements: held item on slot change, "interaction available" when a usable object is in range, the game's floating messages (tutorials, "too hard", energy needed…), pickups.
- **Proximity sonar** (enable it in the settings panel): an aid for tight spaces. Sheets of noise mark the walls around you in all four directions (left/right via panning, a dull timbre for a wall, a splash for water or a chasm), and a small "ding" marks nearby objects tile by tile.

### Building, placement and farming

- **Placing**: aim the tile cursor at the tile and **LT** drops the held item. **Triangle + R1** rotates what you place; **Triangle + L3** (direction assist) snaps your movement to the axes for clean alignment.
- **Multi-tile placement**: for objects that span several tiles, the cursor states the footprint (e.g. "zone 3x3"); for field tools (hoe, watering can, shovel, seeder), **Triangle + R1** changes the effect-zone size.
- **Cursor reading**: tilled or watered ground, plant state (ready to harvest, thirsty, growing), processing stations (input and output slots, progress percentage), and automation basics (conveyors, electricity, machines).

### Boss fights

During a boss fight, several aids kick in:

- **Symmetrical slow-motion** (enable it in the settings panel): slows time down — the boss too, so it's not an advantage — to give you room to react.
- **Arena center cue**: a sound locates the center of the combat zone for you.
- **Fire zone detector**: fire zones on the ground are flagged.
- **Automatic mortar aiming**: with a mortar (lobbed weapon), the reticle locks on by itself to the enemy you're aiming at with the laser cane.

### Repair and salvage station

The station is crafted at the workbench (wood + copper bars) and opens by interacting with it. Its six slots show up as a normal inventory section (bumpers). Ignore its visual buttons: everything goes through the access key, on the selected item:

- **Triangle + right**: repair the selected item — works on any displayed slot (bag, hotbar, equipment).
- **Triangle + left**: salvage everything deposited in the station, for scrap parts and a share of the materials.
- **Triangle + up**: item details, enriched with the repair cost and the salvage yield.
- **Triangle + down**: transfer the selected item, as in any inventory.

These commands are silent when the station is not open.

### Merchant

Interacting with a merchant opens the panel with two sections (bumpers): **Buy** and **Sell**. Each item is read with its price; on the Sell side, the resale value of an item in your bag is announced.

- **Cross**: buy / sell the selected item.
- **Triangle + up**: your coin balance and the transaction total.
- **Triangle + left**: sell everything at once.

**Pouches** (storage bags) are supported: the panel expands automatically, contents are presented in rows, and you equip or unequip a pouch on the gamepad.

### Upgrade forge

Place an item in the forge, then:

- **Triangle + right**: upgrade the placed item by one quality tier.
- **Triangle + up**: the material cost of the upgrade.

### Map, beacons and guidance

Interacting with an ancient waypoint (or double-tapping Triangle anywhere) opens the accessible map. **LB / RB** cycle four categories; **D-pad up / down** browses the current category's list; **Triangle + up** gives the element's details (coordinates, biome, heading in degrees, distance).

- **Destinations**: teleportable waypoints (sorted from the world center outward, stable numbers — waypoint 1 is the Core). **Cross** = teleport.
- **Points of interest**: scanned bosses, grave, markers. **Cross** opens an action menu (see guidance).
- **My beacons**: markers you drop by hand. A "new beacon" row drops one at your position; **Cross** on an existing beacon opens its menu (guidance, rename, delete). Names are remembered per world and per location.
- **Journal**: what the Core told you, archived conversation by conversation (tutorials kept apart). **Cross** opens a conversation, **D-pad left** steps back to the list.

**Audio guidance**: on a point of interest or a beacon, the Cross menu offers two modes:
- **By network**: follows the path of your torches and beacons hop by hop (the mod automatically links torches that are within sight of one another).
- **Direct**: as the crow flies.

In both cases, a repeating chime gives you the direction (left/right via panning, ahead/behind via pitch) and rises in volume as you get closer. Arrival is announced.

## For testers

- **Check [KNOWN_ISSUES.md](KNOWN_ISSUES.md) before reporting** — known bugs, current limitations, and intentional behaviors are listed there.
- The **version and build number** are announced at startup — include both in every report. Each version's changes are listed in the [changelog](CHANGELOG.md).
- The game log is at `%USERPROFILE%\AppData\LocalLow\Pugstorm\Core Keeper\Player.log`. Everything the mod speaks is traced there with the `[A11yTTS]` prefix; attach this file to bug reports.
- **F9** (keyboard): gamepad diagnostic mode — announces every pressed button/axis with its identifier. Handy for reporting mapping issues.
- Multiplayer: tested and working. Since the mod is client-side, only you need to install it — your partners have nothing to do on their end.
- Reports: open a GitHub issue with the build number, what you were doing, what you expected, what happened, and the Player.log.

## How it works (for the curious)

The mod uses Core Keeper's official mod system (PugMod): the C# code is recompiled by the game at launch, hooks go through Harmony, TTS goes through the Tolk library which talks to NVDA. No game file is modified. The mod requires the ModLoader's "elevated" access (`skipSafetyChecks`) because Tolk is a native DLL — that is the price of TTS.

## Licenses and credits

- **Mod code**: MIT license (`LICENSE` file).
- **[Tolk](https://github.com/dkager/tolk)** by Davy Kager: LGPL-3.0 (`third_party/Tolk/LICENSE.txt`).
- **nvdaControllerClient** (NV Access): LGPL-2.1 (`third_party/Tolk/LICENSE-NVDA.txt`).
- Both DLLs above are shipped as-is, as separate files: you may replace them with your own builds.
- Core Keeper is a game by Pugstorm, published by Fireshine Games. This mod is not affiliated.
