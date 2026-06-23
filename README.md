# CoreKeeperAccess — accessibility mod for Core Keeper

*Cette page existe aussi en français : [README.fr.md](README.fr.md).*

A mod that makes **Core Keeper** playable by **blind players**: everything goes through the screen reader's speech (NVDA) and spatialized audio feedback. Menus, inventory, crafting, exploration, combat, teleportation — the goal is to play the full game, independently, on a gamepad.

**Version 1.0, open beta.** The mod is still under active testing, but it is open to everyone: installation is a single file to run (press Enter), no repository to clone, no files to copy by hand.

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

## Getting started

New to Core Keeper? The **[quick start guide](GUIDE.md)** introduces the game, its top-down view, world/character creation and the first controls — a good read before diving in.

## Installation

**Download:** grab the latest version (the zip) from the **[Releases](https://github.com/kaeltosh/CoreKeeperAccess/releases)** page.

### With the installer (recommended)

1. Download the release zip from the **[Releases](https://github.com/kaeltosh/CoreKeeperAccess/releases)** page.
2. Extract it anywhere.
3. **Select `Installer.cmd` and press Enter.** That's it — no command line, no path to type. Your Steam install of Core Keeper is found automatically, on any drive.
4. A window opens, reports what it did, and waits for you to press a key so your screen reader can read the result.
5. Start NVDA, then the game. At the main menu you should hear: "Accessibility mod loaded", followed by the version (for example "1.0 beta, build 1").

Notes:
- **First launch warning.** Windows may say the file "came from another computer" (Mark of the Web / SmartScreen). This is normal for any downloaded script. Choose "More info" → "Run anyway", or right-click `Installer.cmd` → Properties → tick "Unblock".
- **"Access denied"?** If the installer reports a denied write (game under `Program Files` with strict permissions), right-click `Installer.cmd` → "Run as administrator", and run it again. It does not ask for admin on its own when it isn't needed.
- **Game not found?** In the rare case auto-detection fails, use the manual install below.
- To update later, just download the new zip and run `Installer.cmd` again (select it and press Enter, game closed).

### Manual install (alternative)

1. Copy the `dist/CoreKeeperAccess` folder into the game's mods folder:
   `<Core Keeper>/CoreKeeper_Data/StreamingAssets/Mods/`
   (typical Steam path: `C:\Program Files (x86)\Steam\steamapps\common\Core Keeper`).
2. Copy the two DLLs from `dist/natives` (`Tolk.dll` and `nvdaControllerClient64.dll`) **to the game's root folder**, next to `CoreKeeper.exe`.

### Uninstall

Delete the `Mods/CoreKeeperAccess` folder and the two DLLs from the game's root folder. The mod takes over the Triangle button (its native "open map" action is relocated); after removing the mod, go through "default controls" in the game's options to restore Triangle.

## In-game help

You never have to memorize a control sheet. Each feature below recalls its main command, but the complete, context-aware list is always in the in-game help.

- **The first time you enter the game**, a controller discovery mode starts automatically: press buttons and move sticks, each one is named and located for you. It ends by telling you the shortcut for the help menu.
- **The help menu** (Triangle + double-tap the D-pad up) lists, at any moment, the commands available **in the current context** (world, inventory, map, menu) — with the real buttons, renamed PlayStation- or Xbox-style depending on your settings. Reopening it also re-runs the controller discovery mode if you want it.

The whole thing follows the game's remapping and your button-naming preference, so it always tells the truth about your setup. Triangle is the mod's accessibility modifier: held down, it turns the D-pad, sticks and bumpers into the commands listed below.

## Features in detail

Everything below is described from the player's side: what it is for, what you hear, and the main command.

### Menus

Every menu is read as you navigate: titles, options, sliders and selectors, world and character selection and creation, multiplayer menus (player management, confirmation pop-ups). **Name fields** (world, character) announce when you enter and leave edit mode and read back what you type — **Cross** confirms. **Intro and ending cinematics** read themselves slide by slide; **hold Cross for one second** to skip. Navigate with the D-pad, adjust sliders and selectors with left/right.

### The tile cursor — your hand

The cursor feels the terrain tile by tile around you and names what it touches; it is also how you act. Each step plays a sound — panning tells you left/right, pitch tells you up/down. A soft tick is a free tile, a material sound is a wall or block, a "ding" is ore inside the wall, a small high marker means an interactive object, a plop is a pit, a splash is water. "Sealed wall" means indestructible — don't bother. **Move the cursor with the D-pad**; **Cross** acts on the cursor's tile (mine, interact, or walk there); moving with the left stick snaps it back to your character.

### The laser cane — your long-range white cane

A beam sweeps in the direction you aim **with the right stick** and tells you what lies straight ahead: the first obstacle (you hear the "wall ahead"), and the threats along the way. Enemies are flagged with a positional beep plus their name; peaceful creatures and placed objects get their own softer timbres (a hostile always overrides them). Chasms and water do not stop the beam — you hear the edge, then what lies beyond, so you can aim across and shoot. The cursor and the cane speak the same sound language: the same tile sounds the same through both.

### Proximity sonar

An aid for tight spaces, **toggled in the settings panel**. Sheets of noise mark the walls around you in all four directions (left/right through panning, a dull timbre for a wall, a splash for water or a chasm), and a small "ding" marks nearby objects tile by tile.

### Aggro sentinel

Fully automatic. **Each enemy currently attacking you emits one beep per second**; with several attackers the beeps overlap into a queue, so the rhythm tells you roughly how many are on you. A **boss** gets its own deep, fast beep on a dedicated channel — unmistakable.

### Automatic announcements

Without doing anything, you hear: the held item when you switch hotbar slot, "interaction available" when a usable object is in range, the game's floating messages (tutorials, "too hard", energy needed…), and pickups (named, totaled, with a full-bag alert). **Low-health alerts** kick in on their own: a warning below a configurable threshold and a heartbeat whose pace tells you how critical things are. **Status alerts** fire once when a damage-over-time effect (fire, acid, radiation…) or a stun hits you.

### Stats wheel

Read a single piece of data without opening a menu — **hold Triangle and push the left stick** toward a sector, and that sector speaks (your movement is paused while you check). Sectors: health and barrier, hunger, mana and minions, active conditions (poisoned, burning…), world progress, and ore prospecting around you. Health and mana regeneration is appended at the end.

### Settings panel

An accessibility settings panel of its own (**Triangle + Back**), navigated with the D-pad, with spoken descriptions and sound previews for each entry. You tune volumes (per feature: navigation, guidance, sonar, sentinels, alerts, heartbeats…), direction assist, combat slow-motion, proximity sonar, audio normalization, alert thresholds, PlayStation/Xbox button naming, and more. Settings are remembered and kept across versions.

### Inventory and crafting

Open and close it with **Square**. Navigation by sections with **LB / RB** (hotbar, bag, equipment, crafting, chest, statistics…), and the **D-pad** moves within a section. Recipes are read with their missing materials ("craftable" / "missing N of X"); **Cross** picks up, puts down, activates a tab, or crafts (the result lands "in hand"). **RT** quick-moves an item — transferring it to the other open container — **LT** drops it. Also covered: the stats sheet, talents (state and points to spend), souls (each slot's state), and tabs. A few actions (sort, quick stack, pick up half, hotbar pages, trash) live on an **action wheel** on the left stick — push toward a sector to hear the action, then **click R3** to run it.

### Merchant and pouches

Interacting with a merchant opens Buy and Sell sections (LB / RB); every item is read with its price, and on the Sell side the resale value of an item in your bag is announced. On the Buy side, **Cross** buys the selected item. Selling is not automatic: you must drop the item into the sell area — the easy way is to select it in your bag and **RT** to quick-move it there — then **Triangle + left** sells everything at once. **Triangle + up** reads your coin balance and the transaction total. **Pouches** (storage bags) are supported: the panel expands automatically, contents are presented in rows, and you equip or unequip a pouch on the gamepad.

### Repair and salvage station

Crafted at the workbench, the station opens like a normal inventory section. Ignore its visual buttons: on the selected item, **Triangle + right** repairs it (works on any displayed slot), **Triangle + left** salvages everything deposited for scrap and a share of the materials, **Triangle + up** reads item details enriched with the repair cost and salvage yield, and **RT** quick-moves the selected item in or out of the station (salvage acts on what you deposit there).

### Upgrade forge

It works in three steps: drop the item into the forge's slot (**RT** from your bag), move to the **crafting** section (LB / RB), then **Triangle + right** upgrades it one quality tier — **Triangle + up** reads the material cost.

### Building and farming

**The game does not allow placing at a distance**: you must stand next to the target tile, so move close first. Aim the tile cursor at the tile and **LT** drops the held item; **Triangle + R1** rotates what you place; **Triangle + L3** toggles **direction assist**, which snaps your movement to the cardinal axes so you can walk straight and line builds up cleanly. For objects that span several tiles, the cursor states the footprint (e.g. "zone 3x3"); for field tools (hoe, watering can, shovel, seeder), **Triangle + R1** cycles the effect-zone size. Reading at the cursor covers tilled or watered ground, plant state (ready to harvest, thirsty, growing), processing stations (input/output slots, progress percentage), and automation basics (conveyors, electricity, machines).

### Combat

**Slow-motion** (enabled in the settings panel) kicks in the moment you enter combat — when the aggro sentinel activates — and slows the flow of in-game time: everything slows down, you and your enemies alike, so it is not an advantage, just room to react. Its strength is adjustable.

**Dangerous ground zones** tied to combat (fire, poison…) are flagged, and **bosses** get their own dedicated beep on the sentinel (see above).

The first three bosses have, in fact, been beaten in real conditions — screen off, on the easiest difficulty.

The hardest, most complex boss fights will ship over future versions with their own dedicated aids.

### Dialogue and journal

The Core's lines are read aloud automatically, and a **journal** archives them conversation by conversation so you can re-read them later (tutorials are kept apart). The journal is one of the map's categories (see below).

### Map and beacons

The accessible map opens anywhere (**double-tap Triangle**). It has three categories you cycle with **LB / RB** and browse as lists with the **D-pad up/down**; **Triangle + up** gives an element's details (coordinates, biome, heading in degrees, distance):
- **Points of interest**: scanned bosses, your grave, markers. **Cross** opens an action menu (including guidance, see the next section).
- **My beacons**: markers you drop by hand. A "new beacon here" row drops one at your position; **Cross** on an existing beacon opens its menu (guidance, rename, delete). Names are remembered per world and per location.
- **Journal**: the Core's archived conversations.

### Navigation: the torch network and guidance

This is one of the mod's newest systems, so here is the idea in full. The point: you build your own road map as you play, and the mod guides you by ear along it.

**Placing a torch adds a point.** Every torch you place becomes a point of the network (so do the doors you walk through). It is a natural gesture that already does two things in-game — it lights the area and reveals the map around it — to which the mod adds a third: turning that torch into a point of your navigation network.

**Walking from one torch to another creates the link.** The network weaves itself when you walk over an existing torch: the mod then links that point to the previous one you just passed and remembers the trip as a safe passage — the proof is that you just walked it. A jump (teleport) breaks the continuity: no false link between two points that no real path connects. If a beacon is destroyed, the point and its trips survive: the mod never cuts a link on a mere absence.

**Tip: a torch at every intersection.** Guidance links two neighboring torches **in a straight line**; it is not route-finding that would go around walls. So drop a torch at every corner and junction: that way each straight segment really follows the corridor, and the carrot never sends you into a wall.

**Recalculating the network.** In your base, where everything around you is dense and loaded, the "Recalculate network" entry (at the bottom of the "My beacons" tab) re-scans your surroundings and weaves or fixes the local links from what is actually passable, without you having to re-walk every segment. Far away, in unloaded areas, the mod never touches your links: out there, only physically walking a path counts. Re-run it whenever you build or rearrange — furniture placed, walls dug or raised, rooms reshaped — so guidance stays true to the terrain; and drop a torch at the strategic spots at least once, since those are what anchor the nodes the new mesh needs to lay itself out correctly. The same tab also offers "Join the nearest network".

**Guidance.** On a point of interest or a beacon, **Cross** opens a menu with two modes:
- **By network**: the mod computes the shortest path along your torches and beacons and walks you there hop by hop, the safe way you already cleared.
- **Direct**: as the crow flies, ignoring obstacles.

In both modes a chime repeats and works like a carrot held in front of you: panning tells you left/right, pitch tells you ahead/behind, and the cadence tells you whether you are holding the line — fast when you are on the route, slowing down as soon as you drift — while the volume rises as you close in. Arrival is announced.

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
