# Architecture

## Projects

```
src/TerrariaTas.Core       netstandard2.0  No game reference. Movie format, playback, recording and hot
                                           reload, RNG model, determinism self-test logic, hotkey map,
                                           memory watch paths, Studio protocol, the command line and the
                                           movies folder (Launch/). Unit-tested in CI.
src/TerrariaTas.Runtime    net48, x86      The engine inside the game process. Patches Terraria with Harmony.
src/TerrariaTas.Launcher   net48, x86      TerrariaTas.exe: command line, hosts Terraria, starts the runtime;
                                           --list without the game, --new as two game launches.
src/TerrariaTas.Studio     net8-windows    TAS Studio: tool windows, talks to the runtime over a named pipe.
tools/TerrariaTas.GameDiff net8            Reads Terraria.exe as data to pinpoint what an update changed.
tests/TerrariaTas.Core.Tests net8          Tests for Core.
```

Only Runtime and Launcher need a Terraria install to build. Anything that can be expressed without the
game (parsing, state machines, comparisons, RNG maths, the Studio protocol) belongs in Core, where it is
fast to test. Studio references Core only: it never loads the game.

## Runtime layout and dependency rules

```
Runtime/
  TasRuntime.cs        entry point: a TAS session, or the creation launch of --new
  Session/             composition root: the ONLY code that knows every feature
  Creation/            the creation launch of --new: character, world and movie, made by the game's own code
  Frames/              owns the main loop: one Update + one Draw per frame, pacing, fast-forward
  Input/               virtual keyboard, mouse and gamepad
  SaveStates/          capture/restore (Re-Logic's snapshot + every static value + what holds objects)
  Determinism/         pins every source of non-determinism that is not part of the movie (sound clock, music
                       choice, texture cache, achievements, resource packs, configuration saves, liquids
                       settled while a world loads), and applies the window size, game settings and key
                       bindings the movie states
  WorldEntry/          loads the movie's player and world from the title screen
  Hotkeys/             reads the TAS hotkeys (keys from hotkeys.txt) from the physical keyboard
  Status/              window title status line
  Overlay/             frame, position and velocity drawn over the game (drawing only)
  Verification/        state fingerprints, RNG tracer, global-state audit
  Inspection/          reads the world for Studio (player, NPCs, items, input as the game saw it, memory watch,
                       the world map)
  RngTools/            RNG stream positions, kill, hit and teleport logs, drop, damage and teleport forecasts
  Studio/              the named pipe Studio connects to (background threads, no game access)
  GameAccess/          shared typed access to game internals (RNG streams, version, global-state classes)
```

1. **A feature owns its hooks.** Each feature installs its own Harmony patches from a nested `Hooks`
   class in its own file. Reading a feature file tells you every game method it touches.
2. **Features never reference each other.** They expose small APIs; `Session/` wires them together.
   When a feature needs another one's state saved, the session registers it
   (`saveStates.Register("TAS cursor", input.CaptureState, input.RestoreState)`).
3. **Shared models live in `GameAccess/`.** Typed access to game internals that several features need
   (RNG streams, version info). Features may use it; it uses no feature.
4. **The session must not throw.** Any failure aborts with the reason and exit code 1. A TAS must never
   keep running in an unknown state, and an unattended run must never hang.
5. **Only the main thread touches the game, at a frame boundary.** Pipe I/O for Studio runs on background
   threads that only move bytes; `Session/StudioLink` reads the world and answers requests from `PlanTick`.
   Tools that run game code to predict something (the drop, damage and teleport forecasts) work on copies of the RNG
   streams and restore everything they touch, and must fail loudly if they cannot. Work too long for one tick (reading
   the world map) is spread over paused ticks.

## A frame

```
XNA Game.Tick
 └─ Main.Update  ── FrameLoop prefix ──► IFrameDriver.PlanTick()   (TasSession)
      │                                    ├─ poll hotkeys, take Studio commands
      │                                    ├─ after a frame ran: RNG positions, kill log
      │                                    ├─ boot / enter world / play
      │                                    ├─ PlaybackController.Next() → run? input? present?
      │                                    │    (recording: VirtualInput.ReadRealDevices → into the movie)
      │                                    ├─ VirtualInput.Apply(movie line)
      │                                    └─ StudioLink.Publish (status; world and forecasts when paused)
      │            deterministic GameTime, frame-skip pins
      └─ Terraria update (input read, world update on its RNG streams)
 └─ Game.BeginDraw ── allowed once per frame ──► Main.Draw (UI logic runs here) ──► Present (skipped in fast-forward)
```

The frame boundary (before `PlanTick` returns) is where savestates are taken and restored and where the
self-test fingerprints the game.

## Savestates

`SaveStateStore.Capture` = `StateSnapshot.Gameplay` + `StateSnapshot.Input` (Re-Logic) + every plain static
value of the classes in `GameAccess/GlobalState` (`SaveStates/StaticFieldState`) + a list of extra fields that
hold objects. Extras are declared in one place (`SaveStateStore` constructor) as type + member names and copied
with Re-Logic's deep cloner. The `--verify` report lists every audited field that did not come back after a load:
a value of a class outside `GlobalState.Types` goes there, an object in the extras list.

## Recording

`PlaybackController` plays the movie through a `MovieRecorder` (Core). While recording, a frame's input comes
from `VirtualInput.ReadRealDevices` (the game window's keyboard and mouse), goes into the movie at that frame,
which ends the movie there, and is then applied through the virtual devices like any movie line: the game reads
exactly what was recorded. Each savestate keeps the input its game state was made with (`InputHistory`).
Loading one while recording puts the movie on that timeline, and run-to-frame only starts from savestates of
the current version of the movie. `MovieRecorder.Text` keeps the file's own text up to the first changed line,
and the session saves it through `MovieFile`: atomic replace, and a `.bak` of the version from before the session.

## Hot reload

`MovieFile` remembers the text it last read or saved, so it knows when an editor changed the file. A few times a
second the session asks it, and once the editor is done writing, loads the new version. `MovieEdit` (Core) compares
the two versions with the input the game state was made with: when the game played through changed input while
following the movie, `RunToFrame` loads the latest savestate of the new version and replays back to the current
frame. Recorded frames not saved yet go to a copy of the movie (`MovieFile.SaveCopy`), and `SaveMovie` never writes
over a file that changed on disk.

## Launches

`LaunchArguments` (Core) reads the command line into a mode: watch (`Play`), `Edit`, `New`, `Verify`, `List` or free
play. `MovieLibrary` (Core) finds movies by name, picks the path and file names of a new one and builds the `--list`
table, checking each movie's player and world against the checksums of its header (`FileChecksum`).

`--new` takes two game launches. The launcher first runs itself with the internal `--create` mode:
`Creation/CreationSession` boots to the title screen, makes the character with the game's character creation screen
(`UICharacterCreation`, the steps of its Create button) and the world with `WorldGen.CreateNewWorld`, as the world
creation screen does, writes the movie (names, checksums, the configuration's settings and bindings) and exits. Only
then does the launcher start the recording session, in a fresh process: world generation leaves state behind in the
game's memory, and the movie start must be the one every later launch gets.

A session checks the movie's files before the game starts (`TasSession.CheckStartFiles`): a checksum that does not
match stops it, and `--edit` adds missing checksums to the header (`MovieHeaderText.AddChecksums`). Watching and
verifying get no `MovieFile` to write to, so nothing records, reloads or saves.

## Adding a feature

1. Create `Runtime/<Feature>/` with one class and, if it patches the game, a nested `Hooks` class.
2. Keep game-independent logic in Core with tests.
3. Wire it in `TasSession` (construct, `Install(harmony)`, register savestate parts, react to events).
4. If it touches new game members, regenerate `compat/terraria-surface.txt` (see porting.md).
5. Run `--verify` on a movie that exercises it.
