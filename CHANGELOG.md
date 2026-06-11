# Changelog — CoreKeeperAccess

*Cette page existe aussi en français : [CHANGELOG.fr.md](CHANGELOG.fr.md).*

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
