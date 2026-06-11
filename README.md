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

### With the script (recommended)

1. Clone or download this repository.
2. Open a command prompt or PowerShell **in the repository folder** and run:

```
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

3. Start NVDA, then the game. At the main menu you should hear: "Accessibility mod loaded", followed by the version (for example "alpha 1, build 51").

Notes:
- The `-ExecutionPolicy Bypass` part is required: by default Windows refuses to run downloaded PowerShell scripts. It only applies to this one command.
- If the game is not in the default Steam location, add `-GamePath "<path to Core Keeper>"` at the end of the command.
- If you get an "access denied" error (game installed under Program Files with strict permissions), re-run the same command from a console opened **as administrator**.
- To update after a `git pull`, just run the script again (game closed).

### Manual install (alternative)

1. Copy the `dist/CoreKeeperAccess` folder into the game's mods folder:
   `<Core Keeper>/CoreKeeper_Data/StreamingAssets/Mods/`
   (typical Steam path: `C:\Program Files (x86)\Steam\steamapps\common\Core Keeper`).
2. Copy the two DLLs from `dist/natives` (`Tolk.dll` and `nvdaControllerClient64.dll`) **to the game's root folder**, next to `CoreKeeper.exe`.

## First game: recommended difficulty

Pick the **Casual** mode **for both the character AND the world**. The reason matters: in the other modes, dying drops your inventory at the place of death, and the mod does not yet offer any assistance to find your way back there — your items would be very hard to recover. In Casual, you keep everything when you die.

**Uninstall**: delete the `Mods/CoreKeeperAccess` folder and the two DLLs from the game's root. If you remove the mod, go through "default controls" in the game's options to restore the map button on Triangle (the mod takes it over, see below).

## What the mod covers today

- **All menus**: titles, options, sliders, world/character selection and creation, read as you navigate.
- **Name input**: edit mode entry and exit announced, content read aloud, confirm with Cross.
- **Intro and ending cinematics**: text read slide by slide, skip announced.
- **Inventory and crafting**: section-based navigation, recipes with missing materials, stats sheet, talents, souls, tabs.
- **Exploration**: sonified tile cursor, ore prospecting, announcements for placed objects and nearby interactions, the game's floating messages.
- **Combat**: laser scanning cane, aggro sentinel (beeps when a monster is attacking you).
- **Teleportation and map**: waypoints navigable as a list (direction, distance, biome), points of interest.

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

Triangle is taken over by the mod as its **accessibility modifier** (its native "open map" action is relocated; see double-tap). While Triangle is held, the D-pad triggers commands:

- **Triangle + up**: contextual details on the current element (cursor tile, map destination, repair cost…).
- **Triangle + down**: outside inventory, health / hunger / mana / barrier. Inventory open: transfer the selected item.
- **Triangle + right**: outside inventory, character position. Repair station open: repair the selected item.
- **Triangle + left**: outside inventory, prospecting — direction and distance of the nearest ore vein, with a positional ding. Station open: salvage everything.
- **Triangle + L1**: ping sonar — a sound snapshot of everything notable around you (12-tile radius): one beep per target, nearest to farthest, with three timbres (hostile, peaceful creature, find). "Nothing around" if empty. While Triangle is held, L1 does not switch hotbar slot.
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
- **Laser cane on the right stick**: a beam sweeps in the stick's direction, plays the sound of the first blocking tile (the "wall ahead") and flags enemies along the path with a positional beep plus their name.
- **Aggro sentinel**: automatic. Queued beeps = that many monsters currently attacking you.
- Automatic announcements: held item on slot change, "interaction available" when a usable object is in range, the game's floating messages (tutorials, "too hard", energy needed…), pickups.

### Repair and salvage station

The station is crafted at the workbench (wood + copper bars) and opens by interacting with it. Its six slots show up as a normal inventory section (bumpers). Ignore its visual buttons: everything goes through the access key, on the selected item:

- **Triangle + right**: repair the selected item — works on any displayed slot (bag, hotbar, equipment).
- **Triangle + left**: salvage everything deposited in the station, for scrap parts and a share of the materials.
- **Triangle + up**: item details, enriched with the repair cost and the salvage yield.
- **Triangle + down**: transfer the selected item, as in any inventory.

These commands are silent when the station is not open.

### Map and teleportation

Interacting with an ancient waypoint (or double-tapping Triangle anywhere) opens the accessible map:

- **D-pad up / down**: browse the list (waypoints sorted from the world center outward, stable numbers — waypoint 1 is the Core).
- **LB / RB**: switch between **destinations** (teleportable) and **points of interest** (scanned bosses, markers).
- **Cross**: teleport to the selected destination.
- **Triangle + up**: details (coordinates, biome, heading in degrees, distance).

## For testers

- **Check [KNOWN_ISSUES.md](KNOWN_ISSUES.md) before reporting** — known bugs, current limitations, and intentional behaviors are listed there.
- The **version and build number** are announced at startup — include both in every report. Each version's changes are listed in the [changelog](CHANGELOG.md).
- The game log is at `%USERPROFILE%\AppData\LocalLow\Pugstorm\Core Keeper\Player.log`. Everything the mod speaks is traced there with the `[A11yTTS]` prefix; attach this file to bug reports.
- **F9** (keyboard): gamepad diagnostic mode — announces every pressed button/axis with its identifier. Handy for reporting mapping issues.
- Multiplayer: untested at this stage. The mod is client-side; please test in single player.
- Reports: open a GitHub issue with the build number, what you were doing, what you expected, what happened, and the Player.log.

## How it works (for the curious)

The mod uses Core Keeper's official mod system (PugMod): the C# code is recompiled by the game at launch, hooks go through Harmony, TTS goes through the Tolk library which talks to NVDA. No game file is modified. The mod requires the ModLoader's "elevated" access (`skipSafetyChecks`) because Tolk is a native DLL — that is the price of TTS.

## Licenses and credits

- **Mod code**: MIT license (`LICENSE` file).
- **[Tolk](https://github.com/dkager/tolk)** by Davy Kager: LGPL-3.0 (`third_party/Tolk/LICENSE.txt`).
- **nvdaControllerClient** (NV Access): LGPL-2.1 (`third_party/Tolk/LICENSE-NVDA.txt`).
- Both DLLs above are shipped as-is, as separate files: you may replace them with your own builds.
- Core Keeper is a game by Pugstorm, published by Fireshine Games. This mod is not affiliated.
