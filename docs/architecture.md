# Architecture

## Projects

```
src/TerrariaTas.Core       netstandard2.0  No game reference. Movie format, playback state machine,
                                           RNG model, determinism self-test logic. Unit-tested in CI.
src/TerrariaTas.Runtime    net48, x86      The engine inside the game process. Patches Terraria with Harmony.
src/TerrariaTas.Launcher   net48, x86      TerrariaTas.exe: command line, hosts Terraria, starts the runtime.
tools/TerrariaTas.GameDiff net8            Reads Terraria.exe as data to pinpoint what an update changed.
tests/TerrariaTas.Core.Tests net8          Tests for Core.
```

Only Runtime and Launcher need a Terraria install to build. Anything that can be expressed without the
game (parsing, state machines, comparisons, RNG maths) belongs in Core, where it is fast to test.

## Runtime layout and dependency rules

```
Runtime/
  TasRuntime.cs        entry point
  Session/             composition root: the ONLY code that knows every feature
  Frames/              owns the main loop: one Update + one Draw per frame, pacing, fast-forward
  Input/               virtual keyboard, mouse and gamepad
  SaveStates/          capture/restore (Re-Logic's snapshot + what it misses)
  Determinism/         pins every source of non-determinism that is not part of the movie
  WorldEntry/          loads the movie's player and world from the title screen
  Hotkeys/             reads TAS hotkeys from the physical keyboard
  Status/              window title status line
  Verification/        state fingerprints, RNG tracer
  GameAccess/          shared typed access to game internals (RNG streams, version)
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

## A frame

```
XNA Game.Tick
 └─ Main.Update  ── FrameLoop prefix ──► IFrameDriver.PlanTick()   (TasSession)
      │                                    ├─ poll hotkeys
      │                                    ├─ boot / enter world / play
      │                                    ├─ PlaybackController.Next() → run? input? present?
      │                                    └─ VirtualInput.Apply(movie line)
      │            deterministic GameTime, frame-skip pins
      └─ Terraria update (input read, world update on its RNG streams)
 └─ Game.BeginDraw ── allowed once per frame ──► Main.Draw (UI logic runs here) ──► Present (skipped in fast-forward)
```

The frame boundary (before `PlanTick` returns) is where savestates are taken and restored and where the
self-test fingerprints the game.

## Savestates

`SaveStateStore.Capture` = `StateSnapshot.Gameplay` + `StateSnapshot.Input` (Re-Logic) + a list of extra
fields. Extras are declared in one place (`SaveStateStore` constructor) as type + member names and copied
with Re-Logic's deep cloner. When `--verify` reports a desync, the missing state usually belongs in that list.

## Adding a feature

1. Create `Runtime/<Feature>/` with one class and, if it patches the game, a nested `Hooks` class.
2. Keep game-independent logic in Core with tests.
3. Wire it in `TasSession` (construct, `Install(harmony)`, register savestate parts, react to events).
4. If it touches new game members, regenerate `compat/terraria-surface.txt` (see porting.md).
5. Run `--verify` on a movie that exercises it.
