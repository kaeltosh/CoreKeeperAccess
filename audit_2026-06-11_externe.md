# Audit externe — 11 juin 2026 (alpha 1, commit 2901537)

Audit spontané réalisé par un collègue testant sa grille d'évaluation pour mods d'accessibilité.
Périmètre : l'ensemble du code du mod tel que publié en alpha 1. Texte original en anglais,
reproduit verbatim plus bas.

## Fiabilité

Les deux affirmations chiffrées vérifiables ont été contrôlées sur le code et sont exactes :
31 blocs catch (comptés au grep), et clés d'entité par index seul à 4 sites (AggroSentinel,
LaserCane ×2, watcher d'interactibles de GameplayInput). Audit jugé fiable.

## Décisions prises sur la base de cet audit

**Retenu — lot prioritaire « fixes audit » (5 pièces), validé le 11 juin 2026 :**
1. Gestion d'erreurs runtime sur les ~6 catch système muets : collection dédupliquée
   (clé = type d'exception + haut de pile, jamais le message brut), marqueur par site,
   plafond par site, timecode de session sur toutes nos lignes de log, un seul Player.log.
2. Clés d'entité index+version aux 4 sites (l'index ECS est recyclé ; pire cas = annonce ratée).
3. Garde de fraîcheur étendue au routage miner/interagir.
4. Garde-fou anti-balisage dans le goulot TTS (TtsText.Say).
5. Assembleur de messages partagé (morceaux, vides sautés, déduplication, séparateur unique),
   livré avec ses assertions ; migration des sites d'appel existants progressive.

**Promu en chantier dédié :** moteur de keymaps v1 (registre de contextes + dispatcher
déclaratif de combos + relogement TriangleModifier/InfoKey), en précondition de la
2e roue de commandes — exactement le séquencement recommandé par l'audit.

**Écarté consciemment :** tests offline généralisés (1 dev, cycle de test en jeu très court)
et hygiène doc des chemins absolus (carnet de travail perso, réel seulement si contributeur
externe). Motivation supplémentaire du point 4-5 retenu : servir de modèle réutilisable
pour un éventuel futur projet d'accessibilité.

**En suspens :** annonce TTS « module en panne » (le log parlera, la voix pas encore —
essai derrière le dev.flag envisagé) ; contre-passe d'audit après le lot.

---

## Texte original de l'audit (verbatim)

### Overall verdict

This is a genuinely strong mod for its stage. It gets right most of what the rubric weighs heaviest, including several things the rubric calls ceiling behavior rather than floor. There is one significant tier 1 weakness (silent error swallowing around whole subsystems), one small tier 1 pattern violation (entity identity keys), and a couple of growth debts the author already seems aware of. Nothing here is the "predicts the rest of the mod" kind of rot; the trajectory looks right.

For context: it's a Core Keeper mod, about 5,000 lines of C# in well-separated files (no one-file-of-death), Harmony patches plus ECS bridge systems, Tolk/NVDA for speech, gamepad-first, English and French.

### What it does well

Truth discipline is mostly excellent. State is re-read live at speech time nearly everywhere: the inventory navigator stores UI element handles and reads contents fresh, the teleport list holds marker handles and resolves name, position, and distance at announce time, the aggro sentinel reads each monster's position at the moment its beep plays rather than when it was queued. Logic branches on enums and IDs (TileType, ObjectID, FactionID, faction filters), never on display text. It reads authoritative sources with real care: enemy "chasing you" comes from the game's own replicated combat flag rather than a homebrew heuristic, and the ore-prospecting radius is computed from the same stat the game's shader uses for sighted players, so mining talents apply equally. That last one is the project's stated philosophy (equality, not assistance) actually enforced in code.

There is exactly one speech chokepoint, TtsText.Say, and it logs everything spoken to Player.log with an [A11yTTS] prefix. Testers are told to attach that log to reports. For a speech-only interface that's a superb diagnostic decision: every word the player heard is reconstructible.

Boot-time patch verification is the standout tier 4 feature. At startup the mod enumerates every HarmonyPatch type in its own assembly, checks each actually applied, and logs an error naming the missing ones. It's auto-maintained, so future patches are covered for free. That is precisely the rubric's "a missing hook must announce itself," implemented better than most mature mods do it.

Sonification is treated as the first-class axis it must be in a real-time game. One consistent sound language is shared by the tile cursor, the laser cane, the aggro sentinel, and the prospect ding: pan carries east/west, pitch carries north/south at one semitone per row, volume carries distance. Density is managed (aggro beeps queue in 100 ms slots, never overlap, so the player can count attackers by ear). And where the engine fell short, they built their own audio path: a pooled set of audio sources because a shared source would let one sound's pitch clobber another's, a generated sine tone for the sentinel, and a reflection call to defeat the game's random pitch variation that would have scrambled the vertical-axis encoding. There's even a document acoustically analyzing 87 game sounds (duration, spectral centroid, tonality) as a reusable palette. This is exactly the "builds its own audio path rather than settling" tell the rubric credits.

Polling is disciplined, not a polling spine. Events are used where the game fires them (menu selection, chat, floating messages, equip changes, all via Harmony postfixes), and where sampling is unavoidable it's stratified by urgency: laser at 20 Hz, aggro at 5 Hz, object index at 4 Hz, interactables at 5 Hz, vitals at 2 Hz, with results bridged from ECS systems to the mod once per frame.

Process and honesty are well above alpha norms. The changelog is one line per change in player language with hotkeys named. KNOWN_ISSUES separates known bugs, current limitations, planned work, and intentional behaviors, including disclosures like "notable floor tiles sometimes announced in raw English" — a tier 1 leak they found and documented rather than hid. Localization is real: the en and fr files have perfect key parity (113 keys each), language switches reload at runtime, and the shipped dist copy is byte-identical with the source. Version and build number announce at boot and are required in bug reports. The Triangle key takeover is documented with uninstall-and-restore instructions. There's also no platform guessing anywhere: the project keeps decompiled game source as reference and the comments cite confirmed in-game behavior rather than assumptions.

### The main problem: silent failure handling

This is the one place the mod genuinely violates tier 1. There are 31 catch blocks; about 27 swallow the exception with no logging, and only one error path logs. Many of the silent ones are legitimate expected-fallback chains (try the game's text processor, fall back to the localization lookup) — the rubric explicitly permits those. The bad ones are the catch-everything wrappers around entire subsystem updates: the tile reader system's whole update, the object index rebuild, the laser cane system's whole scan, the biome resolver. If a game update changes any of those internals, the tile cursor or the laser cane just goes mute, and a blind player cannot distinguish "nothing there" from "the mod broke." That's the exact failure the rubric ranks above everything else.

What makes this very fixable: the author already solved it once. The aggro sentinel system catches, logs the exception once with a clear marker, and suppresses repeats so a 5 Hz loop doesn't spam. That log-once pattern just needs to be applied to every system-level catch — roughly six call sites. I'd rank this the single highest-value change in the codebase, and it's small.

A second, smaller tier 1 item: stale identity keys. The aggro sentinel's "already announced" set, the laser cane's new-target detection, and the interactable watcher all key entities by entity index alone. Unity ECS recycles indices; the version field exists precisely to disambiguate, and the rubric names this pattern. The blast radius here is bounded — these keys only gate name announcements and are pruned every scan, so the worst case is a missed announcement rather than a wrong one — but the fix is one line per site: key by the pair of index and version instead of index alone.

One more marginal case in the same family: when the player presses the action button on an adjacent tile, the mine-versus-interact routing reads the last published tile result, which can be a frame or so stale. The move case is already guarded by a freshness check; extending that same guard to the mine and interact branches would close it.

### Growth debts to take on deliberately

These are not defects today, but each is the seed of a named rot pattern, and the right time to address them is before the next batch of features, not after.

- Message composition. Announcements are glued by hand with comma-space concatenation across many call sites. The larger builders already use the right embryo (a parts list with dedup, joined once), but there's no shared composition helper that owns separators and empty parts. This is exactly where the rubric says garbled speech eventually comes from. Extracting one small builder now is cheap; retrofitting it across twice the codebase later is not. Relatedly, the chokepoint does no markup stripping — the game's text processor output is trusted to be clean. A strip-or-assert at Say would be cheap insurance against a rich-text tag reaching the player's ears.
- Input contexts. Who owns the D-pad right now is negotiated by ad hoc booleans across files (the inventory suppression flag, the cursor's steal flags, the modifier-held flag, the laser-cane-active flag) plus carefully ordered Tick calls with comments explaining the ordering. At the current five-ish contexts this is managed, and the comments show real thought about fall-through (the access key works inside every context — the rubric's per-handler bubbling question, answered correctly). But this is the input-spaghetti seed, and the code comments say a keymaps engine is planned. I'd make the handler-and-context stack a precondition for the next input-consuming feature rather than a someday item.
- No tests. There is a clever offline compile checker that reproduces the ModLoader's exact compile environment without launching the game, which is real discipline — but compile-pass is not behavior-pass. The codebase already has pure, game-free functions begging for a small suite: the wheel sector math, the grid-neighbour scoring, the enum-name splitter, the text cleaner, the movement-state evaluator. The text cleaning path is the highest-value target, since it's the chain of string transforms every announcement passes through.
- Doc hygiene, minor: CLAUDE.md and the tool scripts default to machine-specific absolute paths from the author's machine, and reference local directories (decompiled game source, extracted examples) that aren't in the repo. Fine for a private working doc, but worth a pass before anyone else contributes.
