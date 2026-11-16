# TODO

Ordered by milestone. Pick an unchecked item, open an issue saying you're on it, keep the PR focused.
See [docs/research.md](docs/research.md) for the background of each item.

## M0: Foundation

- [x] Host the user's Terraria 1.4.5.8 in-process (no files modified, Steam not required)
- [x] Frame loop: one Update + one Draw per frame, deterministic game clock, repaint-proof
- [x] Pause, frame advance (hold to repeat), fast-forward without presenting frames
- [x] Text movie format with header, breakpoints, screen and tile cursor targets (+ parser tests)
- [x] Virtual keyboard, mouse and gamepad fed from the movie
- [x] Load the movie's player and world straight from the title screen
- [x] Savestates (9 slots + movie start) built on Re-Logic's `StateSnapshot`
- [x] Determinism pins: Steam, focus, calendar, quality, frame skip, autosave, file writes, clock-seeded RNGs, draw schedule, lighting phase
- [x] `--verify` self-test: per-frame fingerprints, replay from savestates, comparison across launches, RNG call tracer and census
- [x] RNG model (`TerrariaRandom`) tested against `System.Random`
- [x] `GameDiff`: dependency surface with IL fingerprints, update check, RNG map, game diff
- [x] Build and runtime version guards

## M1: Airtight determinism

- [ ] **Start-state replay desyncs at frame 377 in `Main.UpdateWeather`** (`movies/smoke-test.tas --verify`). One weather-related input is still missing from `SaveStateStore`; the RNG trace shows the extra call
- [ ] Reference movies covering combat, enemy spawns, bosses, liquids, events, inventory and crafting, each verified in CI-like runs on a dev machine
- [ ] UI in `Draw`: verify inventory clicks, crafting, reforging, chests and NPC shops replay in sync
- [ ] Asset loading is asynchronous: check whether anything gameplay-related (e.g. NPC frame heights from textures) depends on load timing; force immediate loads if so
- [ ] Minimized window skips `Draw` (XNA), which would desync: keep drawing, or pause playback while minimized
- [ ] Lock the window size during a movie (resizing changes the screen size the game simulates with)
- [ ] Cross-machine check: same movie, two PCs, same fingerprints (GPU, CPU, OS differences)
- [ ] Decide how to treat `Main.rand` consumers that only exist in some graphics settings (e.g. "waves quality", backgrounds)

## M2: TAS workflow

- [ ] Recording: capture real input into movie lines (merge identical frames)
- [ ] Hot reload: when the movie file changes, load the newest savestate before the first edited frame and replay to the current frame
- [ ] Redraw the screen after loading a state without running draw-time game logic (e.g. keep a screenshot with each state)
- [ ] Savestates on disk and smaller in memory (tile deltas against the movie start, compression)
- [ ] Configurable hotkeys (laptops have no numpad)
- [ ] Info overlay or side window: frame, position and velocity with sub-pixels, RNG indices, nearby NPCs
- [ ] Movie commands: `Repeat`/`EndRepeat`, labels, "run to label", comments preserved by the writer
- [ ] Start from boot: character creation and world creation inside the movie
- [ ] `World: generate <seed> <size> <evil> <difficulty>` header to create the start world deterministically
- [ ] Text input (chat, signs, naming) through the virtual keyboard (Terraria reads characters through IME/window messages)
- [ ] Memory watch: named expressions over game objects, shown live and logged per frame
- [ ] TAS Studio: a separate editor with a piano roll and a "greenzone" of per-frame savestates

## M3: RNG tools

- [ ] RNG inspector: shared RNG and every stream, with index and the next values (`GameRng.ToModel`)
- [ ] Oracle predictions by running real game code on a cloned RNG: reforge result, crafted prefix, damage roll and crit, coin drop, item drops through `ItemDropSolver` in dry-run mode
- [ ] Brute-force search API: from a savestate, try N delays or input variants, score the outcome, report the best
- [ ] NPC spawn forecast (`SpawnNPCs` stream): which frames spawn what, given the player's path
- [ ] Event forecast at dusk/dawn (`UpdateTime`): Blood Moon, eclipse, invasions, meteors, traveling merchant
- [ ] Random tile update forecast (`UpdateWorldGen`): Plantera bulbs, life fruit
- [ ] Boss pattern predictors (per boss, from `NPC.AI` on `UpdateNPCs`)
- [ ] Per-version RNG map diff in the porting checklist

## M4: Project and releases

- [x] CI: build and test Core, build GameDiff
- [ ] Release packaging: zip of `TerrariaTas.exe` and its DLLs per Terraria version, attached to a GitHub release
- [ ] Porting automation: one script running `GameDiff check`, rebuild and `--verify` on the reference movies
