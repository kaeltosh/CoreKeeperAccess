# Known issues — CoreKeeperAccess

*Cette page existe aussi en français : [KNOWN_ISSUES.fr.md](KNOWN_ISSUES.fr.md).*

Current as of 1.0.13 beta. Please check this list before opening an issue — and if you hit one of these, no need to report it unless you have new details.

> **Setup tip — turn off Windows spatial sound** (Dolby Atmos / Windows Sonic for Headphones): it re-mixes the stereo field and blurs every directional cue (panning, left/right, the sonar). Plain stereo gives accurate positional audio.

## Not finalized in this open beta

- **Some sound cues are still placeholders** (proximity sonar, peaceful creatures and objects on the laser cane, the invalid-placement sound, beacon guidance): they work, but the final sounds aren't picked yet — they may change.
- **Not yet validated in the field**: the stun alert, and reading advanced automation machines (industry) at the cursor. They're built but not confirmed live.
- **Proximity scanner: excluding your own character and companion from the "creatures" category hasn't been checked in multiplayer** — a teammate should still show up; not yet confirmed live.
- **Boss summon statues and scepter summons are now translated** — not yet confirmed live.
- **Hotbar switching controls have been swapped**: it's now Triangle+L1 (next) / Triangle+R1 (previous), reversed from before, based on tester feedback — not yet confirmed live after the swap.
- **New command-learn mode**: hold Triangle (or R3, or press neither) and press any button to hear what it does, without it actually triggering — a screen-reader-style "input help" complementing the existing button-naming mode. Not yet confirmed live.
- **New shortcut to cycle equipment presets**: with the character/inventory window open (not at a station), Triangle+D-pad right/left switches to the next/previous of your 3 equipment presets. Not yet confirmed live.
- **Tile details (Triangle+Up) enriched**: now announces every layer present on the targeted tile (ceiling, wall, electrical wire, placed object, floor covering) instead of just one at a time — useful for tiles where several things overlap (a wire under a machine, flooring placed over an object…). Not yet confirmed live.
- **Ore prospecting (Triangle+Left) extended to drill-mined deposits**: besides a buried vein, it now also finds the nearest mineable deposit (both can be announced in the same sweep if found at the same time). Not yet confirmed live.

## Known bugs

- **In the livestock window, the breeding toggle's label sometimes repeats at the end of the animal's status line.** A fix has been coded but not yet confirmed live.
- **Familiar talent tree resets on world reload.** When you leave and re-enter a world, the familiar's in-memory talent data resets to its base state (a game-engine limitation). The familiar still works, but its talents are temporarily invisible to the mod. **Workaround**: pick up the familiar and place it again — this forces the game to regenerate its data correctly.
- **A generator placed on top of an ancient wire is silent to the tile cursor.** The indestructible wire network found in the Core ruins masks the object placed on it (two passive objects, the wire's collider wins). The generator works fine; the cursor just won't name it.
- **Notable floor tiles are sometimes announced in raw English** (internal tile name), whatever the game language. Rare: standard ground is silent by design, only special floors are affected.

## Current limitations

- **Item names are only spoken on the first pickup of each type.** That is native game behavior (later pickups only update a silent visual counter). A future version may hook the actual inventory insert.
- **Light and darkness are not perceived.** Real-time lighting is shader-rendered and unreadable by the mod. Planned approach: reading light *sources* (torches as audio beacons) instead of the rendered light.
- **No assistance to return to your place of death.** In non-Casual modes your inventory drops where you died, and nothing guides you back to it — hence the strong recommendation to play Casual (character and world), see the README.
- **Name input requires a physical keyboard.** There is no accessible on-screen keyboard for gamepad-only setups.

## Not covered yet (planned)

- **Active control of advanced automation** (placing and configuring drills, conveyor belts, robot arms, the electricity network through their menus): reading them at the cursor is in (see the cursor reading entry above), but driving them is planned as a dedicated milestone.
- **In-game control remapping screen.**

## Good to know (by design)

- **Triangle is taken over by the mod** as the accessibility modifier; the native map binding is removed from your controller config. Double-tap Triangle opens the map instead. If you uninstall the mod, restore "default controls" in the game's options.
- **A silent access combo means "nothing to do here"**: contextual commands (repair, salvage…) say nothing outside their context, by design.
- **Cursor walking is a straight line, no pathfinding** — same information a sighted player has, by design philosophy.
