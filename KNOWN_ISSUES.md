# Known issues — CoreKeeperAccess

*Cette page existe aussi en français : [KNOWN_ISSUES.fr.md](KNOWN_ISSUES.fr.md).*

Current as of alpha 2 (build 1). Please check this list before opening an issue — and if you hit one of these, no need to report it unless you have new details.

## Known bugs

- **A generator placed on top of an ancient wire is silent to the tile cursor.** The indestructible wire network found in the Core ruins masks the object placed on it (two passive objects, the wire's collider wins). The generator works fine; the cursor just won't name it.
- **Notable floor tiles are sometimes announced in raw English** (internal tile name), whatever the game language. Rare: standard ground is silent by design, only special floors are affected.

## Current limitations

- **Item names are only spoken on the first pickup of each type.** That is native game behavior (later pickups only update a silent visual counter). A future version may hook the actual inventory insert.
- **Light and darkness are not perceived.** Real-time lighting is shader-rendered and unreadable by the mod. Planned approach: reading light *sources* (torches as audio beacons) instead of the rendered light.
- **No assistance to return to your place of death.** In non-Casual modes your inventory drops where you died, and nothing guides you back to it — hence the strong recommendation to play Casual (character and world), see the README.
- **The character appearance screen (body, skin, hair…) is not adapted.** It is purely cosmetic carousel selectors; pick nothing and validate directly if you don't care about looks.
- **Name input requires a physical keyboard.** There is no accessible on-screen keyboard for gamepad-only setups.

## Not covered yet (planned)

- **Active control of advanced automation** (placing and configuring drills, conveyor belts, robot arms, the electricity network through their menus): reading them at the cursor is in (see the cursor reading entry above), but driving them is planned as a dedicated milestone.
- **In-game control remapping screen.**

## Good to know (by design)

- **Triangle is taken over by the mod** as the accessibility modifier; the native map binding is removed from your controller config. Double-tap Triangle opens the map instead. If you uninstall the mod, restore "default controls" in the game's options.
- **A silent access combo means "nothing to do here"**: contextual commands (repair, salvage…) say nothing outside their context, by design.
- **Cursor walking is a straight line, no pathfinding** — same information a sighted player has, by design philosophy.
