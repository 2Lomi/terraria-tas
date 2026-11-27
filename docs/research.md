# Research: tool-assisting Terraria

What it takes to TAS Terraria, what the game offers, and what we found while building the foundation.
Everything here was checked against Terraria **1.4.5.8** (Windows, Steam build 24893155).

## 1. Not an emulator, a host

Console TAS tools emulate the whole machine, so they own the clock, the inputs and the memory.
Terraria is a PC game written in C# (.NET Framework 4, 32-bit, Microsoft XNA 4). There is no CPU to
emulate, but the same control can be obtained more simply:

| Emulator TAS | Terraria TAS (this project) |
|---|---|
| Emulates CPU and hardware | Loads the user's `Terraria.exe` into its own process (`TerrariaTas.exe`) |
| Frame = one video frame of the console | Frame = one `Main.Update` + one `Main.Draw` |
| Savestate = copy of emulated RAM | Savestate = deep copy of the game objects (see 4) |
| Read memory at addresses | Read typed objects: `Main.player[0].velocity`, `Main.npc[i].ai[0]` |
| Virtual controller | Virtual XNA keyboard, mouse and gamepad |

The host patches methods at runtime with [Harmony](https://github.com/pardeike/Harmony). Terraria's
files are never modified or redistributed. libTAS (Linux, process-level) is the closest generic
tool; it does not cover Windows XNA games.

Hosting details: Terraria embeds its dependencies (ReLogic, Steamworks.NET, Newtonsoft.Json...) as
resources and resolves them itself; XNA finds `Content/` next to the entry assembly (cached in
`TitleLocation._titleLocation`); native DLLs load from the exe folder. `GameHost` reproduces all three.
Steam is not needed: `SocialAPI.Initialize(SocialMode.None)` is the DRM-free code path, which also keeps
TAS runs away from achievements and cloud saves.

## 2. What Terraria 1.4.5 gives us

Re-Logic ships its own testing tools inside the release build, in `Terraria.Testing`:

- **`StateSnapshot.Gameplay` / `StateSnapshot.Input`**: capture and restore of tiles, players, NPCs,
  projectiles, items, chests, tile entities, world flags, lighting lists and RNG streams, built on a
  fast IL-generating deep cloner (`Terraria.Testing.Cloning.DeepCloning`). Re-Logic maintains the
  component list, which lowers our maintenance cost on every update. Our savestates build on it.
- **`RecordReplay`**: per-update input recording and replay of world updates (no UI).
- **`WorldUpdateStepper`**: pauses world updates and steps them one at a time.
- **`QuickLoad`**: loads a player and a world straight from the title screen. `WorldLoader` uses the same recipe.
- Debug chat commands for all of the above (`Terraria.Testing.ChatCommands`, disabled in release).

The other big change is **per-phase RNG streams**. Every world-update phase swaps `Main.rand` for its
own generator, seeded from the world seed on first use (`Main.SwapRandom`):

`DoUpdateInWorld`, `UpdateParticleSystems_World`, `UpdatePlayers`, `SpawnNPCs`, `SpawnNPC`,
`UpdatePressurePlates`, `UpdateNPCs`, `UpdateGores`, `UpdateProjectiles`, `UpdateItems`, `UpdateDust`,
`UpdateLeashedEntities`, `UpdateFloatingText`, `UpdateTime`, `UpdateWorldGen`, `UpdateServerOrClient`, `UpdateLiquid`.

Every stream gets the same seed, the world's, so they all walk one number sequence, each at its own
position. The stream table is created once, in `Main.Initialize`, and never cleared: leaving to the menu and
entering a world again continues the streams, and only restarting the game resets them.

`WorldGen.genRand` is now just `Main.rand`, so random tile updates (Plantera bulbs, life fruit, grass
spread) draw from `UpdateWorldGen`. Dust spawned by NPC updates no longer shifts NPC spawning. Everything
outside world updates (menus, UI such as reforging and bags, weather, drawing) shares the base
`Main.rand`, which is seeded from the clock at startup.

All generators are `UnifiedRandom`: Knuth's subtractive generator, the algorithm of seeded
`System.Random`. `TerrariaTas.Core.Rng.TerrariaRandom` is an exact model, tested against `System.Random`.

## 3. Determinism

A TAS needs the same inputs to produce the same game, every launch. Sources of divergence found so far,
and how each is handled (`Runtime/Determinism`, `Runtime/Frames`):

| Source | Effect | Handling |
|---|---|---|
| XNA fixed/variable time step, Terraria frame-skip modes and update accumulator | Updates skipped or batched depending on real time | One Update + one Draw per frame, `GameTime` derived from the frame count, `FrameSkipMode.Off` |
| Window repaint (`WM_PAINT` → `Game.DrawFrame`) | Extra draws; Terraria runs UI logic and RNG inside `Draw` | Draws refused outside a frame (`Game.BeginDraw` gate) |
| Window focus (`FocusHelper`) | Game pauses and drops input when unfocused | Always focused |
| `Main.qaStyle` "Auto" | Graphics quality follows real FPS and changes **liquid physics** and dust limits | Pinned to High |
| `Lighting.Mode` setter | Resets the render schedule on every assignment | Assigned only when it differs |
| `DateTime.Now` | Halloween/Christmas content | `checkXMas`/`isHalloweenDateNow` use the movie's date |
| Clock- and GUID-seeded generators: `new UnifiedRandom()`, `FastRandom.CreateWithRandomSeed()` (wind gust timing **during play**, stars, light flicker), `Main.rand` at init, `TileFrameSeed`, `UnpausedUpdateSeed` | Different sequences every launch | Seeded from the movie at its start; generators created without a seed get seeds from counters that the movie start resets and savestates keep |
| What booting and world loading roll at a moment that depends on how long they took: wind and weather counters, the cloud layer, critter spawn chances, the traveling merchant's shop, stars, clouds | The movie starts from a different world every launch | Rolled again at the movie start from the movie seed, on a generator of their own (the movie's `Main.rand` sequence is untouched) |
| Everything that advances on every frame from boot, title screen included: tile, wall, waterfall and critter cage animations, item icon animations (the scrying orb draws from `Main.rand`), color pulses, `timeForVisualEffects` (read by AI), the game clock | Same, plus RNG draws on their own schedule | Put back to their boot values at the movie start; the game clock starts every movie at one hour |
| The mouse position before the movie places the cursor | Terraria only updates its cursor when the mouse moves, so it keeps what it picked up while loading | Cursor placed at the window center at the movie start |
| Threaded world loading | Variable number of frames before the world is ready | The movie starts on the first update after the loader thread returned |
| Autosave, settings menus, Steam threads | Disk writes, settings changes, background work | Disabled or pinned |
| Lighting engine | Steps once per frame (minimap, scene metrics, scan, blur); drives the layer-refresh schedule and **biome detection** | Phase reset and flicker generators reseeded at movie start, state in savestates |
| Visual systems sharing `Main.rand` (item icon animations, tile animation counters, weather vane, ambient critters, layer refreshes, clouds, stars) | Their state decides how many samples they draw from the RNG that reforges and weather share | State in savestates, schedules reset at movie start |
| Draw-time state read by updates (`cloudBGAlpha` moves while the sky is drawn and feeds the cloud counts `UpdateWeather` rolls on) | Depends on what was drawn, including the title screen | In savestates (every static value is), reset at movie start |
| Visual emission into shared pools | Dust spawned by drawing can make gameplay `Dust.NewDust` calls find no free slot, which shifts gameplay streams | Draw schedule made deterministic (above) |
| Sound state (`LegacySoundPlayer.PlaySound` asks whether earlier sounds still play: which thunder sound, whether an item, NPC hit or death, roar or splash sound plays and rolls its pitch) | Pausing or fast-forwarding changed random draws on the gameplay streams, so drops | `SoundClock` answers from game frames; the sound and ambient volumes never read 0 there (a sound scaled to 0 returned before its rolls) |
| Music choice (`UpdateAudio_DecideOnNewMusic` rolls `Main.rand` between two versions of the day and underground tracks on every update where the audio engine reports neither as playing; music volume 0 skipped the choice and blocked music boxes) | `Main.rand` draws, and so weather, reforges and bags, depended on how fast the real track started, fast-forward and the machine | A track counts as playing when it was the music of the previous update (`MusicChoice`); the music volume only changes what you hear |
| User settings that change the simulation: background, flashy effects, sway in wind, blood and gore, pickup text, waterfall limit, screen shake, storm effects (RNG, dust, gore, light, camera), zoom and interface size (mouse mapping; a fresh install picks the interface size from the screen height), map and minimap, control options | The same movie played differently on another configuration | Part of the movie header, applied when it loads, fresh-install values when absent ([format](movie-format.md#game-settings)); 4K support pinned off (its graphics profile depends on the desktop and GPU) |
| Item animations exist only once the splash screen ends | The movie start put back a copy taken earlier, empty: no animation ran, and a Fallen Star or soul on screen crashed the game | Rebuilt at the movie start with the game's own setup |
| Smart cursor on/off (`SmartCursorWanted_Mouse`) lives as long as the game process | Depended on what happened earlier in the session | Off at the movie start |
| Characters typed into chat, signs and names arrive through window messages, apart from the keyboard state | Typing during a movie or a recording would not replay | Dropped on every frame the TAS feeds the input |
| Animated forest backgrounds take their textures from the animation frame | Left at the title screen's last frame | Set again at the movie start with the game's own animation step |
| NPC and gore textures load the first time something draws them and stay loaded for the process, and gameplay reads whether they are: before, an NPC's animation frame does not advance (some AI reads it) and a gore has no size (its collision box) | Earlier attempts, and frames played before loading a savestate, changed later frames | All NPC and gore textures loaded at boot (`TexturePreload`, about 130 MB of the 32-bit process). Item and projectile textures are loaded right before gameplay measures them; tiles, walls and backgrounds only matter to drawing |
| Background textures load the first time a background is drawn (at once, `ImmediateLoad`), and `Main.LoadBackground` records their size in `Main.backgroundWidth`/`backgroundHeight` then, never again. Drawing a background of size 0 stops early, before some of its `Main.rand` draws (the mushroom background draws three) | Savestates restored those sizes like any static value: loading a state saved before a background was first drawn put its size back to 0 for good, and a replay through a teleport into another biome drew fewer numbers (`--verify` on a Teleportation Potion movie, frame 36, `DrawSurfaceBG_Mushroom`) | Savestates leave the sizes alone (`SaveStates/StaticFieldState`): they describe what this process loaded, and every background loads with its size the first time it is needed |
| Key bindings and the forward zoom mode (`input profiles.json`) | The same keys did something else on another configuration | Part of the movie header (`Bind` lines, `ForwardZoom`), fresh-install values when absent ([format](movie-format.md#key-bindings)); switching control profiles does nothing during a movie |
| Resource packs | They can replace sounds (whose length decides random rolls, see the sound row), fonts and texts (which place buttons); the game itself rejects textures whose size differs from vanilla | TAS sessions use the game's own content (`AssetSourceController.UseResourcePacks` gets no packs); the selection stays in `config.json` |
| Achievements (`achievements.dat`, written as they complete) | The inventory's achievement card (hovering it keeps clicks from the world) and the unlock popups (they catch clicks) depended on earlier sessions | None completed at the movie start, the file is never written, progress and popups in savestates |
| The game saves `config.json` and `input profiles.json` from settings menus and when quitting through the menu | A movie's settings and bindings would have become the local configuration, and so the next `--new` movie's | No saves while a movie is loaded |
| Journey mode sliders commit a change when the real clock reads at least the time of the last commit | Always, unless Windows sets its clock back | Always |
| A player saved while dead enters the world with the real time since saving taken off the respawn timer | The movie start depended on when it was played | The timer the file has |
| World loading runs on a thread of its own while the main thread runs the menu, and both draw from `Main.rand` (`WorldGen.genRand` is `Main.rand` in 1.4.5): settling the liquids (`Liquid.Update` rounds some tiles up at random) took whatever numbers the interleaving left it | A world with water still moving (any newly generated one) started movies with other liquids on every launch (`Liquid.numLiquid`, `Main.liquid` in the global-state audit) | While a world loads, liquids draw from a generator of the loading thread seeded from the world (`WorldLoadPins`); the other loading draws are rolled again at the movie start |
| Player and world files: a movie made with a world, played on a world of the same name that someone played in since | Desyncs from frame 0, or a different run altogether | The header pins both files by SHA-256 (`PlayerChecksum`, `WorldChecksum`), checked before the game starts |
| A data folder whose `config.json` has no `Language` or `ClientUUID` (a new one: sessions with a movie never save the configuration) | Every launch was the game's first: the language selection flag set, a new client id | Both written before launch when missing |
| Background texture sizes (`Main.backgroundWidth`, `backgroundHeight`) are recorded when a background is first drawn, so at the movie start they depend on what the title screen drew | Only drawing reads them, and drawing loads a background at once (`ImmediateLoad`) when it first needs one | Not audited at the movie start |
| Creating the character and the world in the launch that records | World generation leaves its state in the game's memory (generation flags, structures, the shared RNG), which later launches of the movie never have | `--new` creates them in a launch of its own, then records in a fresh one |
| No audio device: Terraria skips every sound and the music choice | None of their random rolls happen | Movies refuse to play or record; free play only warns |
| Window size: the back buffer follows the window, and Terraria's screen size follows the back buffer on its next draw (`UpdateDisplaySettings`), also when Windows makes a window smaller to fit the screen | Mouse coordinates, lighting range, what is on screen, dust near it | Written to `config.json` before launch, resizing and maximizing off while a movie is loaded, and a movie start at another size than the movie's aborts |
| Minimized window: XNA skips the draw of every frame (`Game.DrawFrame`). Drawing anyway is no way out: Terraria's `SetDisplayMode` reads the empty client area and shrinks the screen to its minimum | Frames without their draw, where interface logic and random rolls run | Frames wait while the window is minimized; a minimize and restore in the middle of a movie verifies in sync |

Still open:

- Movies cannot run on a machine without an audio device. Supporting one would take a simulated sound engine
  (the sound player, its sound lengths and the music choice) that runs without the device.
- Menus outside a world use the local menu key bindings; they only matter if a movie leaves the world.
- An audio device that disappears during a session (unplugged headset) has not been tested.

`TerrariaTas.exe movie.tas --verify` measures all of this: it fingerprints every frame (players,
NPCs, projectiles, items, camera, weather, every RNG stream), replays from a mid-movie savestate and from
the start state, and compares against the previous launch. It traces every draw from the shared RNG, so a
desync report names the exact game method whose behaviour diverged, and it prints the full list of
methods that drew from it.

It also audits the game's global state, before a desync happens: every static value of the global-state
classes (`GameAccess/GlobalState`) is compared right after each savestate load with its value when the state
was saved (anything listed is state savestates miss), and at the movie start with the previous launch
(anything listed comes from outside the movie). Both lists are empty today, apart from frame-rate counters
measured from the real clock that no gameplay reads.

State as of this writing, on a 512-frame movie in a rainy world: replay across launches, from a mid-movie
savestate and from the start state all in sync, two launches in a row. See TODO.md for what is not covered yet.

## 4. Savestates

`SaveStateStore` = Re-Logic's gameplay and input snapshots + what they leave out: every plain static value
(numbers, flags, enums, strings, structs and arrays of those) of `Main`, `WorldGen`, `NPC`, `Player` and the
other global-state classes, rather than a hand-picked list that kept missing counters (weather, tile counting,
input targets, draw-time state); then, as a declarative list, what holds objects: the shared RNG, item
animations, clouds and stars, lighting engine state, scene metrics, the draw schedule. Other features add their
own state through `SaveStateStore.Register` (the virtual cursor, the frame clock, RNG positions, the seed
counters, the sound clock).

Cost: tiles dominate. A tile is 14 bytes copied into a 16-byte slot, so a small world (4200×1200) takes
about 80 MB per savestate and a large world (8400×2400) about 320 MB. The host is a 32-bit process; it is
marked large-address-aware (4 GB). Delta-compressing tiles against the start state is the obvious next step.

## 5. Input

Terraria reads input only through `Keyboard.GetState()`, `Mouse.GetState()` and `GamePad.GetState()`.
Replacing those three gives full control of menus, UI and gameplay, independent of keybindings or window focus.

- Keyboard input is consumed one update after it is read (`DoUpdate_HandleInput` reads the keyboard after
  `PlayerInput.UpdateInput` ran), mouse input in the same update.
- Most UI logic (inventory, crafting, reforging, chests, the title menus) runs inside `Main.Draw`.
  That is why a frame must always include exactly one draw, even in fast-forward.
- Cursor to world: `MouseWorld = screenPosition + center + (raw - center) / RenderZoom`, mirrored when
  gravity is flipped. `CursorMapper` inverts it so a movie can aim at a tile (`@tX,Y`).

## 6. RNG manipulation

`GameDiff rng` lists the 1,200+ game methods that draw random numbers, with call counts and a fingerprint of their
code (`compat/terraria-rng.txt` for the targeted version); `GameDiff rng-check` lists the ones a new build changed.
Where the things speedrunners care about roll:

| What | Where | Generator |
|---|---|---|
| Enemy drops, coins (`NPC.NPCLoot_DropItems`, `NPCLoot_DropMoney`) | NPC death during NPC/projectile/player updates | the phase's stream |
| Damage and crits (`Main.DamageVar`, crit checks) | where the hit happens (player, projectile or NPC update) | the phase's stream |
| NPC spawns (`NPC.SpawnNPC`) | spawn checks every frame per player | `SpawnNPCs` / `SpawnNPC` |
| Boss and enemy AI patterns | `NPC.AI` | `UpdateNPCs` |
| Blood Moon, eclipse, invasions, meteors, traveling merchant | dusk/dawn logic | `UpdateTime` |
| Plantera bulbs, life fruit, chlorophyte, grass spread | random tile updates | `UpdateWorldGen` |
| Reforging, crafting prefixes, bags and crates opened from the inventory | UI code inside `Draw` | shared `Main.rand` |
| Wind, rain, thunder | `Main.UpdateWeather` | shared `Main.rand` |
| Teleportation Potion destination (`Utils.CheckForGoodTeleportationSpot`) | item use, `Player.ItemCheck` | `UpdatePlayers` |

Three ways to predict, from simplest to most specific:

1. **Search with savestates.** Load, wait N frames or change an input, run, look at the outcome. This works
   for every case and only needs fast savestates and fast-forward. It is how most TAS manipulation is done.
2. **Run the real game code on a copy of the RNG.** Clone the generator, call the game function that
   rolls (`Item.Prefix`, `Main.DamageVar`, a drop rule), read the result, put the original generator back.
   No game logic is re-implemented, so it survives updates.
3. **Model the RNG.** `TerrariaRandom` reproduces any generator from its state (`GameRng.ToModel`) to list
   upcoming values, e.g. the next 50 `Next(100)` rolls of `UpdateNPCs`.

Because each world phase has its own stream, an outcome only depends on how many draws that phase made
before it, not on what other phases or drawing did. That makes options 2 and 3 practical in 1.4.5. Inside a
phase, visual effects still count: particles and sounds spawned by the phase draw from its stream (see below).

### Enemy drops

Drops are rolled when the NPC dies, inside `NPC.NPCLoot`, on whatever stream is active when the killing blow
lands: `UpdatePlayers` for a weapon swing, `UpdateProjectiles` for any projectile, `UpdateNPCs` for lava,
debuffs and other NPCs. In order:

1. The hit, before the loot: crit roll, damage variance (`Main.DamageVar`), hit sound pitch, then
   `HitEffect`: blood particles, and on death the death particles and gore (a slime's death is 50 particles,
   about 300 draws).
2. `NPCLoot`: kill counting (banners are a counter in 1.4.5, no item, no draw), a draw for statue-spawned
   enemies, then the drop rules: global rules first (keys, souls, mechanical boss summons, holiday drops; a
   rule whose condition fails draws nothing), then the NPC's own. A typical rule is "luck roll < chance",
   then a draw for the stack, then `Item.NewItem` draws a prefix (gear only) and two velocity values.
3. Death events: nothing for ordinary enemies; potions, hearts and celebrations for bosses.
4. Coins: at least 8 draws, plus a few per coin stack. Hearts and stars: two luck rolls whose branch depends
   on the player's life and mana.

Luck (`Luck.RollLuck`) costs one draw at luck 0 and at least one more at any other value, so luck changes
the number of draws as well as the odds. Boss treasure bags are rolled when opened from the inventory, on
the shared RNG.

What the hit spends depends on more than the inputs:

- `Dust.NewDust` only draws when the spawn point is near the screen (a margin of up to 400 px that shrinks as
  the dust pool fills), and draws more once the pool is over half full. Camera position and window size
  change drops. The movie's `Resolution` header pins the window size.
- Many sounds draw a random pitch (`LegacySoundPlayer`), but only when they actually play: not at sound
  volume 0, not far from the camera, not without an audio device (Terraria also forces the volume to 0 when
  music fails to load). A few sounds also skip while their previous instance is still playing. The TAS
  does not pin the volume yet (TODO).
- Gore is not limited to the screen, but with Blood and Gore off each piece draws two more numbers.

TAS Studio's drop forecast (docs/studio.md) runs the three drop methods of `NPCLoot` on copies of a stream for
every offset and undoes what they spawned; the kill log records the stream and offset each real kill used.

### Damage rolls

A hit by the player rolls on the stream of the phase it happens in, crit first, then damage:

- A swing (`Player.ProcessHitAgainstNPC`, `UpdatePlayers`): `Main.rand.Next(1, 101) <= GetWeaponCrit(item)`, drawn
  for every hit, even at 0% crit. Then modifiers that draw nothing in the ordinary case (rapid attack bonus, banners,
  the parry bonus, a few weapons), then `Main.DamageVar(damage, luck)`.
- A projectile (`Projectile.Damage_PVE_Inner`, `UpdateProjectiles`): the damage starts as the projectile's `damage`
  times `Damage_GetProjectileSpecificDamageMultiplier` (flails swung ×1.2 or thrown ×2, rockets ×2 on their main
  target, mines ×3 lying still...), with a few more changes by type (Stardust Dragon size, Desert Tiger count). Then,
  for projectiles of a player (not traps or NPC projectiles), one `Next(1, 101)` per damage class the projectile has, melee, ranged and magic in that order, against
  the player's crit of that class plus the projectile's `bonusCritChance`: every one is drawn, even after a success.
  Minions have no class and roll none. Then timed crits, monk armor and Golem's sentry backpack can draw, whip tags can
  change the damage and the crit, and `Main.DamageVar` rolls, without luck for traps and NPC projectiles. In single
  player those also have the local player as `owner` (the Guide's arrows, falling stars, which are `trap` projectiles):
  only `Projectile.OwnedBySomeone` (neither `npcProj` nor `trap`) tells the player's own apart.
- `Main.DamageVar(dmg, luck)`: `dmg * (1 + Next(-15, 16) / 100)`; with luck above 0, a `NextFloat() < luck` draw and, if
  it passes, a second roll keeping the higher; below 0 the same keeping the lower; rounded. One draw at luck 0, two or
  three otherwise.
- `NPC.StrikeNPC_Inner`: the NPC loses `max(1, damage - defense / 2)`, doubled on a crit, times its
  `takenDamageMultiplier` when above 1. Armor penetration was added to the damage before
  (`NPC.checkArmorPenetration`: half the penetration, at most half the defense).
- The hit effects (sounds, blood, the death particles) come after these rolls.

TAS Studio's damage rolls run the crit rolls and `Main.DamageVar` on copies of the streams for every offset; the hit
log measures the offset at the damage roll of each real hit and subtracts the crit rolls.

### Random teleports

`Player.TeleportationPotion` runs from `Player.ItemCheck` while the potion is used (use time 17): the use timer is set
on the first frame of the use and counts down once per frame before the potion's code, which teleports when it reads
2, so on the 16th frame of the use (checked in the game: a use starting on frame 21 rolled on frame 36). It runs during the players'
update, so it rolls on `UpdatePlayers`, where bubble blocks are not solid: the world update clears
`Main.tileSolid[379]` before the players' update and sets it back after `UpdateTime`.

The destination is `Utils.CheckForGoodTeleportationSpot`: a tile at `Next(100, maxTilesX - 98)`,
`Next(100, UnderworldLayer + 102)` (two draws per attempt, clamped 45 tiles from the edges), then a fall of less than
100 tiles to a floor. An attempt fails on solid tiles at the spot, Lihzahrd walls before Plantera, dungeon walls below
the surface before Skeletron, lava or hurting tiles where the player ends up, no room to move a tile in each direction
(`Collision.TileCollision`), and in the dual-dungeon seed a spot walled in by unbreakable walls. After 1000 attempts
the player stays where it is. Nothing else is random: the result depends on the stream, the tiles, the player's size
and gravity, and those world flags (the velocity it is given only reaches a collision call whose result is unused).
It writes the collision flags (`Collision.up`, `down`, `stair`, `honey`...), which every collision check sets before
reading them, and blank tiles into the null slots its fall reaches under a world smaller than the largest (the tile
array is made for the largest world). The forecast puts both back.

The other random teleports search differently: `Player.MagicConch` (to an ocean) with
`TeleportHelpers.RequestMagicConchTeleportPosition`, `Player.DemonConch` with up to nine
`CheckForGoodTeleportationSpot` calls around the middle of the underworld and a `Next(2)` for the side. TAS Studio's
teleport forecast covers the potion; the teleport log records the stream, offset and landing of each one drunk.

### The world map

The map's colors come from `MapHelper`: `CreateMapTile(x, y, light)` picks a map type for a tile (its block, else its
wall or liquid, else a background by depth: a sky gradient, dirt, rock or the underworld), and `GetMapTileXnaColor`
gives its color, with paint and light. A cave background turns snowy near snow found on the player's exploration map
(`Main.Map`, allocated once at the largest world size): the check reads 36 tiles around the tile, which near the left
edge falls before the start of the array. TAS Studio's world map calls both with full light for every tile, and paints
the 40 unreachable tiles along each edge (`Main.offLimitBorderTiles`) itself.

## 7. Following game updates

- `build/Version.props` names the one Terraria version a release targets. The build refuses another
  `Terraria.exe`, the runtime refuses to start on one.
- Recompiling against the new `Terraria.exe` reports every renamed or removed member we use directly.
- `GameDiff surface` records every Terraria member the TAS depends on (patched, mirrored, called, read, or
  reached by name such as private fields) with a fingerprint of its IL that ignores metadata tokens and
  compiler-generated numbering. `GameDiff check` lists what disappeared or changed in a new build.
- `GameDiff rng-check` does the same for every method that draws random numbers: what a new build changed there can
  move drops, spawns and manipulation routes even when the TAS itself needs no change.
- `--verify` on reference movies tells whether the port is deterministic again.
- `build/port-check.ps1` runs all of the above in one go.

See [porting.md](porting.md).

## 8. Prior art

- [libTAS](https://github.com/clementgallet/libTAS): generic Linux TAS tool (process-level savestates, time virtualization).
- [CelesteTAS](https://github.com/EverestAPI/CelesteTAS-EverestInterop): input-file format, Studio editor, breakpoints. Our movie format is close to it.
- [Celeste SpeedrunTool](https://github.com/DemoJameson/CelesteSpeedrunTool): savestates by deep-cloning game objects.
- BizHawk TAStudio: piano-roll editing and greenzone (savestate per frame), the reference for a future editor.
- tModLoader: not used. Runs must be on the vanilla game, so the TAS hosts the official executable.

## Legal

The repository contains only original code. Users supply their own copy of Terraria. Decompiling the
game for interoperability research is done locally and its output is never committed.
