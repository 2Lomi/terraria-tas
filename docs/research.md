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
| Clock-seeded RNGs (`new UnifiedRandom()`, `Main.rand` at init, `TileFrameSeed` from a GUID) | Different sequences every launch | Seeded from the movie at its start, counters for the rest |
| Threaded world loading | Variable number of frames before the world is ready | The movie starts on the first update after the loader thread returned |
| Autosave, settings menus, Steam threads | Disk writes, settings changes, background work | Disabled or pinned |
| Lighting engine | Steps once per frame (minimap, scene metrics, scan, blur); drives the layer-refresh schedule and **biome detection** | Phase reset at movie start, state in savestates |
| Visual systems sharing `Main.rand` (item icon animations, tile animation counters, weather vane, ambient critters, layer refreshes) | Their state decides how many samples they draw from the RNG that reforges and weather share | State in savestates, schedules reset at movie start |
| Visual emission into shared pools | Dust spawned by drawing can make gameplay `Dust.NewDust` calls find no free slot, which shifts gameplay streams | Draw schedule made deterministic (above) |

`TerrariaTas.exe movie.tas --verify` measures all of this: it fingerprints every frame (players,
NPCs, projectiles, items, camera, weather, every RNG stream), replays from a mid-movie savestate and from
the start state, and compares against the previous launch. It traces every draw from the shared RNG, so a
desync report names the exact game method whose behaviour diverged, and it prints the full list of
methods that drew from it.

State as of this writing, on a 512-frame movie: replay across launches in sync, replay from a mid-movie
savestate in sync, replay from the start state diverges at frame 377 inside `Main.UpdateWeather` (one
weather input is still missing from savestates). See TODO.md.

## 4. Savestates

`SaveStateStore` = Re-Logic's gameplay and input snapshots + what they leave out, as a declarative list:
the shared RNG, previous-frame device states, frame counters read by AI, UI flags, the visual systems
above, lighting engine state, scene metrics, weather/thunder state, the draw schedule. Other features add
their own state through `SaveStateStore.Register` (the virtual cursor, the frame clock).

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

`GameDiff rng` lists the 1,200+ game methods that draw random numbers, with call counts; diffing it between
versions shows where RNG behaviour changed. Where the things speedrunners care about roll:

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

Three ways to predict, from simplest to most specific:

1. **Search with savestates.** Load, wait N frames or change an input, run, look at the outcome. This works
   for every case and only needs fast savestates and fast-forward. It is how most TAS manipulation is done.
2. **Run the real game code on a copy of the RNG.** Clone the generator, call the game function that
   rolls (`Item.Prefix`, `Main.DamageVar`, a drop rule), read the result, put the original generator back.
   No game logic is re-implemented, so it survives updates.
3. **Model the RNG.** `TerrariaRandom` reproduces any generator from its state (`GameRng.ToModel`) to list
   upcoming values, e.g. the next 50 `Next(100)` rolls of `UpdateNPCs`.

Because each world phase has its own stream, an outcome only depends on how many draws that phase made
before it, not on unrelated visual effects. That makes options 2 and 3 practical in 1.4.5.

## 7. Following game updates

- `build/Version.props` names the one Terraria version a release targets. The build refuses another
  `Terraria.exe`, the runtime refuses to start on one.
- Recompiling against the new `Terraria.exe` reports every renamed or removed member we use directly.
- `GameDiff surface` records every Terraria member the TAS depends on (patched, mirrored, called, read, or
  reached by name such as private fields) with a fingerprint of its IL that ignores metadata tokens and
  compiler-generated numbering. `GameDiff check` lists what disappeared or changed in a new build.
- `--verify` on reference movies tells whether the port is deterministic again.

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
