# Known issues — CoreKeeperAccess

*Cette page existe aussi en français : [KNOWN_ISSUES.fr.md](KNOWN_ISSUES.fr.md).*

Current as of 1.0.17 beta. Please check this list before opening an issue — and if you hit one of these, no need to report it unless you have new details.

> **Setup tip — turn off Windows spatial sound** (Dolby Atmos / Windows Sonic for Headphones): it re-mixes the stereo field and blurs every directional cue (panning, left/right, the sonar). Plain stereo gives accurate positional audio.

## Not finalized in this open beta

- **Some sound cues are still placeholders** (proximity sonar, peaceful creatures and objects on the laser cane, the invalid-placement sound, beacon guidance): they work, but the final sounds aren't picked yet — they may change.
- **Not yet validated in the field**: the stun alert, and reading advanced automation machines (industry) at the cursor. They're built but not confirmed live.
- **Proximity scanner: excluding your own character and companion from the "creatures" category hasn't been checked in multiplayer** — a teammate should still show up; not yet confirmed live. More broadly, the mod's multiplayer behaviour hasn't been played with several people yet.
- **The whole 1.0.15 fix wave is coded but hasn't been played yet**: standard floors back in tile details, ancient relays excluded from ore prospecting, the kelp turtle returned to livestock, every deposit in range enumerated, full meal names on the hotbar, the dresser's hidden/shown state, and the entire boating side (cursor detection, shore sound on the laser cane, sonar and collision detector staying quiet on water, the leave-the-boat reminder in the help menu). If any of these misbehaves, that's a useful report.
- **The whole 1.0.16 wave is coded but hasn't been played yet, multiplayer-heavy**: spotting other players (a dedicated "Players" category in the proximity scanner, their name announced by the detached cursor and the laser cane when one is on a targeted tile or in the beam), a player-tracking ping (hold R3 + left stick to pick a connected player to follow by ear, off-screen included), a fix for musical instrument mode trapping you (Start or Escape now exit it — previously only Triangle+O did), painted furniture (table, stool…) announcing its color like walls and floors already do, and the "manage players" screen's ban/invite lists saying "no player here" instead of staying silent when empty. Since this needs a second player to exercise most of it, reports are especially useful here.
- **The whole 1.0.16 build 2 wave (boss announcements) is coded but hasn't been played yet.** Boss combat announcements are now the same across **every** boss in the game: the boss's name is spoken when it enrages, changes phase, becomes invulnerable — or becomes attackable again — and when it dies, including for bosses nobody has fought with the mod yet. On top of that: an incoming-attack warning on several bosses, appearance and disappearance for three of them, and a priority queue so two announcements no longer cut each other off. On a boss made of several parts, the health announcement now follows the main part instead of jumping between them. Finally, Azeos's wave-shape callouts and the health milestones now exist **in English** too (they were frozen in French), and any other language hears them through the screen reader. **None of this has been checked in combat** — only at game startup: a report from any boss fight is especially useful.
- **The whole 1.0.17 wave is coded but hasn't been played yet**: caveling floor tiles (and the ruins' big stone tiles) no longer hide what you place on top of them — a lamp or an electrical door standing on that floor was unfindable with the cursor, while a circular saw on the same spot announced itself fine; along the way, electrical devices with no interface (electrical door, wired lamp) are no longer overridden by any piece of furniture sharing their tile. The dialogue journal no longer shows the Core's awakening speech until you have actually talked to it: it pointed you toward the Great Wall while the wall stays deaf until then, which can make a run look stuck — journals already filled in error clean themselves up the next time you open one. Finally, the progress readout (Triangle+Down) says "3 crystals placed" instead of a misleading "activated", and explicitly asks you to go talk to the Core when that is the missing step.
- **The shore sound** (laser cane, while boating) **is a placeholder**: the timbre was picked by ear from the game's own sound bank and may still change. You can listen to it in the sound-learning menu, exploration category.
- **On a standard floor, tile details may stay silent** if the game has no name for that floor: the mod would rather say nothing than read out an internal English name. Worth reporting with the location — those get fixed case by case.

## Known bugs
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
