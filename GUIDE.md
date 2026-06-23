# Getting started with Core Keeper

*Cette page existe aussi en français : [GUIDE.fr.md](GUIDE.fr.md).*

This guide is a hands-on introduction to playing Core Keeper with the **CoreKeeperAccess** accessibility mod. It covers the game, how it works, and your very first steps.

It covers neither installation nor the full list of controls: for those, see the README. The living reference for controls is the **in-game help menu** (access key Triangle + a quick double-tap on the D-pad up), which always adapts to the situation.

---

## 1. What is Core Keeper

Core Keeper is a survival and underground-exploration game. Your character wakes up next to a large central crystal, the **Core**, in the middle of a cavern: everything starts from there.

The core loop comes down to a few verbs: **explore**, **dig** to gather resources, **craft** tools and equipment, **build** a base, then push further into ever-harder zones. You need to **eat** regularly to stave off hunger, and to **heal** after taking hits.

Later come **bosses** to defeat in order to unlock access to the next zones. That assumes you are equipped: at the start, you stay close to the Core.

---

## 2. How the game is "seen": the tile grid

Core Keeper is played in **top-down view**: a camera on the ceiling looks straight down. Your character is at the center of the screen, and it's the scenery that scrolls around them as you move.

Key point: **everything sits on a grid of square tiles**, like graph paper. The character occupies one tile; each piece of wall or floor, each object, each enemy occupies one or more tiles.

You move and orient yourself in **four directions**: up, down, left, right (and the diagonals). There is no height or depth: it's a flat plane.

In practice, you find your way mostly **by ear**: the mod lets you *hear* the environment. The grid is just a simple way to picture the layout.

To perceive all this without sight, the mod provides several complementary tools, each with its own use:

- The **tile cursor**: you move it tile by tile (D-pad) to inspect one specific tile around you. It announces what the tile holds — a wall (and its material), ore, floor, water, a pit, a placed object.
- The **laser cane**: you sweep it in a direction (right stick) to scan what it meets — walls, obstacles, named enemies. Handy for probing at a distance without moving tile by tile.
- The **proximity sonar**: it continuously sounds out what is right next to you, to sense your immediate surroundings while moving.

At any time, **Triangle + a single press on the D-pad up** — the **info key** — gives the detail of whatever is selected: here, the precise contents of the tile under the cursor (a wall's material, the kind of floor…). A quick double-tap, on the other hand, opens the help menu.

---

## 3. Creating a world and a character

Before playing, the game has you create a world and then a character. These screens, like all menus, are navigated with the **gamepad**: the **D-pad** moves the selection between options, the **Cross** button confirms. The mod reads each option aloud as you navigate. The controller discovery mode, which details every button, then starts automatically the first time you enter the game.

The **recommended difficulty**, for both character and world, is **Casual**.

---

## 4. The basic controls and first steps

Everything goes through the **mod's access key: the Triangle button**. Held down, it turns the D-pad and a few buttons into a layer of mod commands. The full, up-to-date list is in the **help menu** (Triangle + double-tap up) — that's the first shortcut to memorize, because it leads to everything else.

The bare minimum to get going:

- **Move**: the left stick.
- **Inspect around you**: the D-pad moves the tile cursor, tile by tile.
- **Cross button**: when the tile cursor is **detached** from the character (placed on a tile with the D-pad), the Cross button acts on that tile — **move** there if it's empty, **interact** if it's an object (chest, workbench…), **mine** or **hit** it if it's solid (wall, block, ore).
- **Held-item actions**: the **primary action** (right trigger) and the **secondary action** (left trigger) use the item in hand — see the action bar section.
- **Open the inventory**, **open the map**: see the help menu for the exact buttons (the map opens with a double-tap on Triangle).

### Your first objectives

The game itself guides your first moments, in this order:

1. **Gather wood** (and the basic resources around the Core).
2. **Craft a workbench**, your first crafting table.
3. **Craft a torch**: a bit of light for sighted players, and above all a **landmark** — placed torches act as anchor points for the mod's beacon navigation.
4. **Craft a pickaxe**, to dig ore.
5. **Smelt ore** to get metal bars.

This is the on-ramp every new player follows. Once these basics are in place, you start digging around you, gearing up and expanding your base.

---

## 5. The inventory screen

The inventory holds everything the character owns. It has three main areas:

- The **bag**: every item you've picked up.
- The **equipment**: what the character wears (armor, accessories).
- The **action bar**: the shortcut slots, detailed in the next section.

When you open a **chest** or a **station** (workbench, furnace…), its contents are added to the screen as an extra area.

The mod has you navigate **by sections**: you move from one group to another (bag, equipment, hotbar, chest…) and go through the items of each, announced aloud. The detail of an item or a recipe (required materials, for instance) is obtained with the **info key**: **Triangle + a single press on the D-pad up**. The other useful gestures (transfer, drop, trash…) are in the help menu when they apply.

The inventory actions the gamepad doesn't offer directly (sort, quick-stack, drop on the ground…) are gathered in an **action wheel**: with the inventory open, tilting the **left stick** brings it up, you point at the command you want, then confirm it with **R3** (right stick click).

---

## 6. The action bar (hotbar)

The **action bar** is the row of shortcut slots. It lets you quickly select the item held **in hand**: a tool, a weapon, an object to place, or food.

Key principle: **the held item determines what the two game actions do**. The **primary action** (right trigger) mines, hits or shoots depending on the item: a pickaxe mines, a melee weapon strikes, a ranged weapon shoots. The **secondary action** (left trigger) adapts too: place an object, eat a dish, drink a potion.

You navigate the bar to change slot, and the item in hand is announced on each change. In game, **Triangle + right or left** (D-pad) moves the **focus** of the action bar from one row to another: it goes through your whole inventory, line by line, for a quick selection without opening it. Best to check the held item before acting: it's the most frequent cause of an action that doesn't do what you expected.

---

## 7. Survival and combat

### Checking your status

Your character's status can be read at any time with the **status wheel**: **Triangle + left stick**. It announces health, mana, hunger, current conditions, progress and prospecting. It's the reflex for taking stock, especially when health drops or hunger sets in.

### The aggro sentinel

No need to sweep constantly: the **aggro sentinel** watches automatically. Each monster that attacks emits a **spatialized beep**, placed in its direction, at a rate of **one beep per second at most** per enemy (so a given enemy never sounds more than once a second). The beeps don't overlap: they queue up, each waiting its turn. So two attackers give two successive beeps, each located at its position — enough to track several threats by ear. It's the passive counterpart of the laser cane: the cane is for searching actively, the sentinel warns you when the character is being targeted.

When the sentinel triggers, the game **automatically goes into slow motion** as long as an enemy keeps attacking: this is normal and intended, time is given back to let you react to the sounds. The slowdown is **symmetric**: *everything* slows down, including the character — their movement as well as their rate of fire and attack. So it's compensation, not an advantage. Nothing to do: it happens on its own.

### Hitting an enemy

Spotting and aiming at enemies goes through the **laser cane** (right stick). To attack:

1. **Sweep the right stick** in the presumed direction of the enemy.
2. When the laser passes over an enemy, a **sound** signals it and the enemy is **announced**.
3. **Strike** with the primary action (right trigger).

Important: the laser **detecting** an enemy does not mean it's **within range** of your weapon. The cane spots an enemy even far away; with a melee weapon like a sword, you therefore need to **get closer** for the hits to actually land. A ranged weapon, on the other hand, can hit from farther.

---

## 8. Building and placing objects cleanly (the mod's tools)

Placing an object or a wall in the right spot is one of the trickiest operations without sight, because the game's normal placement **"guesses"** the spot from the character's facing direction, unpredictably. The mod offers two reliable placement modes.

### Cursor placement (precise, tile by tile)

The basic, deterministic mode:

1. Select the object to place in the **action bar**.
2. Spot the target tile with the **tile cursor** (D-pad).
3. To get within range, aim at a tile **adjacent** to that spot with the cursor, then press the **Cross button**: the character moves to the aimed tile.
4. Bring the **tile cursor** back to the target tile.
5. Press the **left trigger**: the object is placed.

Sound feedback accompanies the action: a **tick** on success, an **invalid sound** if the tile can't be built on (already occupied, out of range…).

### Line placement (to align a row)

Handy for laying a wall or floor straight across several tiles:

1. First enable **assisted direction** (**Triangle + L3**, the left stick click), which snaps movement to the cardinal axes (to walk perfectly straight).
2. Place the **tile cursor** on a tile **adjacent** to the character.
3. **Hold the left trigger** down: the cursor **locks** onto that offset.
4. Without releasing, **walk with the left stick**: the cursor follows the character and the object is placed tile by tile, in a clean line.

A tick sounds on each placement.

### Good to know

- **Triangle + R1** **rotates** the item in hand (useful for furniture and oriented machines); the heading is announced at each step.
- That same **Triangle + R1** shortcut also sets the **area size** of wide-radius tools (hoe, watering can, shovel…): the mod announces the area (for example "3 by 3 area"), so you know exactly what will be tilled or watered.

---

## And then?

Once these basics are in hand, the rest is learned by playing, with the help menu always available. The mod covers far more than this guide: beacon navigation, proximity sonar, life and status alerts, combat assisted by ear, map reading, merchants, and more. The README and the in-game help menu are your two references for going further.
