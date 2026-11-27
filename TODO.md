# TODO

Ordered by milestone. Pick an unchecked item, open an issue saying you're on it, keep the PR focused.
See [docs/research.md](docs/research.md) for the background of each item.

## M0: Foundation

- [x] Host the user's Terraria in-process (no files modified, Steam not required)
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

- [x] Weather desyncs (`Main.UpdateWeather`, "8 vs 7 calls") from the start state, from savestates and across launches: cloud counters and the draw-time cloud layer were missing from savestates, and booting left launch-dependent values behind (wind, weather and cloud counters, critter chances, the travel shop, animations, GUID- and clock-seeded generators, the game clock, the cursor). Savestates now restore every static value of the global-state classes; the movie start rerolls the world session from the movie seed and puts back what booting advanced
- [x] Global-state audit in `--verify`: static values that do not come back after a load, and values that differ from the previous launch at the movie start
- [x] Sound state: `LegacySoundPlayer.PlaySound` decides random rolls from whether earlier sounds still play; it now asks a game-frame clock (`SoundClock`), and the sound and ambient volumes never read 0 there
- [x] Music choice: the day and underground track rolls on `Main.rand` depended on the real audio engine; a track now counts as playing when it was the previous update's music, and a muted game decides music like an audible one (`MusicChoice`)
- [x] Game settings that change the simulation (graphics options that draw from the RNG or spawn dust, zoom, interface size, map, controls) are part of the movie header, fresh-install values when absent; 4K support pinned off
- [x] Item animations: the movie start restored an empty copy taken before the splash screen registered them, so a Fallen Star or soul on screen crashed the game; rebuilt with the game's own setup
- [x] Smart cursor on/off reset at the movie start; typed characters (chat, signs) dropped during movies
- [ ] A movie that exercises the sound clock (thunderstorm, splashes, boss roars, items 53/55/159) under `--verify`
- [x] Journey mode sliders committed on a real-clock check, and a player saved while dead had the real time since saving taken off the respawn timer: neither reads the clock now
- [ ] Machines without an audio device: movies refuse to run there (Terraria then skips every sound and the random numbers they roll). Supporting them needs a simulated sound engine: the legacy sound player without sound objects, sound lengths read from the content files, the music choice with the disabled audio system
- [x] Data folder files that changed the simulation: key bindings and the forward zoom mode are in the movie header (`Bind` lines, `ForwardZoom`); achievements start empty and `achievements.dat` is never written (progress and popups in savestates); resource packs are not used; with a movie loaded the game no longer saves `config.json` and `input profiles.json` (the movie's settings used to become the local ones)
- [ ] Reference movies covering combat, enemy spawns, bosses, liquids, events, inventory and crafting, each verified in CI-like runs on a dev machine
- [ ] UI in `Draw`: verify inventory clicks, crafting, reforging, chests and NPC shops replay in sync
- [x] Lazy textures: gameplay reads whether NPC and gore textures are loaded (NPC animation frames, gore sizes), and they loaded the first time something drew them, so earlier attempts changed later frames. All of them load at boot (0.3 s, about 140 MB)
- [x] Background sizes: savestates put back `Main.backgroundWidth`/`backgroundHeight`, which the game records once when a background texture first loads, so a state saved before a background first showed left its size at 0 and its drawing skipped `Main.rand` draws (found by `--verify` replaying a teleport into the glowing mushroom biome). Savestates leave them alone now
- [x] The install folder is never written: the game's crash log goes to the TAS data folder (the game wrote `client-crashlog.txt` into its working folder, the install folder)
- [x] Minimized window skips `Draw` (XNA), which would desync: frames wait while it is minimized (drawing anyway would make Terraria shrink its screen to the minimum)
- [x] Window size locked during a movie (resizing changed the screen size the game simulates with); `Resolution` limited to what Terraria can make, and a movie start at another size aborts
- [x] World loading: the loading thread and the menu both drew from `Main.rand`, so a world with moving water (any new one) settled its liquids differently on every launch; liquids now draw from a generator of the loading thread while a world loads
- [x] Player and world files pinned by SHA-256 in the movie header (`PlayerChecksum`, `WorldChecksum`), checked before the game starts; `--edit` adds them to older movies
- [ ] Cross-machine check: same movie, two PCs, same fingerprints (GPU, CPU, OS differences)

## M2: TAS workflow

- [x] Recording: F1 (or `--new`) records the game window's keyboard and mouse into the movie, merging identical frames; recording a frame ends the movie there; savestates keep the input they were made with so loading one puts the movie on its timeline; autosave keeps the file's own text up to the first change, with a `.bak`
- [x] Recording: an outside edit of the movie file while recording got overwritten by the next autosave; now the edit is loaded and the unsaved recorded frames go to `<movie>.recording-<time>.tas`
- [ ] Gamepad input in movies (format and recording); today the virtual gamepad is always idle
- [x] Hot reload: when the movie file changes, load the newest savestate before the first edited frame and replay to the current frame (`MovieEdit`, `MovieFile.ChangedOnDisk`)
- [ ] Redraw the screen after loading a state without running draw-time game logic (e.g. keep a screenshot with each state)
- [ ] Savestates on disk and smaller in memory (tile deltas against the movie start, compression)
- [x] Configurable hotkeys: `hotkeys.txt` in the data folder (created with F1-F10), sent to TAS Studio on connection
- [x] Side window: TAS Studio (`src/TerrariaTas.Studio`) with input display, timeline and savestates, NPC and item lists, RNG streams, drop forecast ([docs/studio.md](docs/studio.md))
- [x] Studio: player panel (position and velocity with sub-pixels, movement of the last frame, jump/wing/dash timers, item use, life, mana, buffs), live while playing
- [x] In-game overlay option: frame, position and velocity drawn over the game after its own draw (a per-frame log of player and RNG state is identical with it on and off), toggled from Studio's Player window or the `ToggleOverlay` hotkey
- [x] Movie commands: `Repeat`/`EndRepeat` blocks (recording into one writes it out round by round); comments are kept up to the first line recording changes
- [ ] Labels in movies and "run to label" in Studio (needs a syntax decision: full-line comments, or a dedicated line such as `# label: name`)
- [x] Command line: a movie alone plays to watch (nothing can change it), `--edit` works on it, `--new` creates a movie with a new character and world, `--list` lists movies and checks their files; movies are found by name in the movies folder
- [x] New movies create their start: `--new` makes a softcore character and a small corruption world in normal difficulty, or what `--crimson`, `--expert`/`--master`/`--journey`, `--medium`/`--large` and `--seed` say, with the game's own creation code, in a game launch of its own (recording then starts in a fresh one)
- [ ] `--new` with special seeds (for the worthy, drunk world, not the bees... and the 1.4.5 secret seeds), and character choices (mediumcore, hardcore, appearance)
- [ ] Start from boot: character creation and world creation inside the movie, for categories that time them
- [ ] Text input (chat, signs, naming) through the virtual keyboard (Terraria reads characters through IME/window messages), and recorded; until then, characters typed on the real keyboard are dropped on every frame the TAS feeds
- [x] Memory watch: paths over game objects (`player.velocity.X`, `Main.npc[3].ai[0]`), shown live in Studio and logged per frame to CSV; fields only unless a property is called explicitly (`Name()`)
- [ ] TAS Studio editing: a piano roll over the timeline, editing input from Studio, and a "greenzone" of per-frame savestates
- [x] World map in Studio: every tile in the game's own map colors, fully revealed, read from the running game at a paused frame; pan and zoom, a white crosshair pointer reading the tile, compass and depth under it; you, the spawn and teleport destinations marked
- [ ] World map: the map travels as 4-byte palette indexes and lengths (24 MB for a large world); bytes and variable-length lengths would make it several times smaller

## M3: RNG tools

- [x] RNG inspector: shared RNG and every stream, with position, draws per frame and the next values (Studio "RNG streams")
- [x] Drop forecast: item and coin drops of any NPC for every offset of the kill streams, by running the game's drop code on copies of the stream, plus a kill log with each real kill's stream and offset (Studio "Drop forecast")
- [ ] Drop forecast for bosses: simulate the death events (potions, hearts) so coins and hearts after them are exact too
- [ ] Kill cost: predict how many draws a given hit spends before the loot (crit, damage, sounds, particles), so the forecast row of a planned kill is known without trying it
- [x] Damage rolls: crit and damage of the held weapon's next hit for every offset of the swing and projectile streams (the game's `Main.DamageVar` on copies of the streams), what a target loses, and a hit log with each real hit's offset (Studio "Damage rolls")
- [ ] Damage rolls of special hits: timed crits, monk armor, whip tags, Pyroclast, banners and the like draw between the rolls or change the damage
- [ ] Oracle predictions by running real game code on a cloned RNG: reforge result, crafted prefix
- [x] Teleport forecast: where a Teleportation Potion lands for every offset of `UpdatePlayers` (the game's `Utils.CheckForGoodTeleportationSpot` on copies of the stream), with compass and depth readings, a target picked on the world map, and a teleport log (Studio "Teleport forecast")
- [ ] Teleport forecast for the Magic Conch and the Demon Conch
- [ ] Teleport planning across frames: which upcoming frame lands where for a given input, by replaying from a savestate (with the brute-force search API)
- [x] Live check of the damage rolls, teleport forecast and world map (2026-11-23): 22 hits (pickaxe swings, throwing knives, shortsword stabs, arrows, crits) and 4 teleports all as forecast, with a forecast at every frame boundary; the potion rolls on the 16th frame of its use; `--verify` in sync on combat and teleport movies; a large world's map reads in 1.2 s. Found and fixed: town NPCs' arrows and falling stars counted as the player's projectiles, no forecast at frame 0 (the streams do not exist yet), shots forecast without their ammo's damage, map labels covering each other
- [ ] Brute-force search API: from a savestate, try N delays or input variants, score the outcome, report the best
- [ ] NPC spawn forecast (`SpawnNPCs` stream): which frames spawn what, given the player's path
- [ ] Event forecast at dusk/dawn (`UpdateTime`): Blood Moon, eclipse, invasions, meteors, traveling merchant
- [ ] Random tile update forecast (`UpdateWorldGen`): Plantera bulbs, life fruit
- [ ] Boss pattern predictors (per boss, from `NPC.AI` on `UpdateNPCs`)
- [x] Per-version RNG map diff in the porting checklist: `compat/terraria-rng.txt` fingerprints every method that draws random numbers, `GameDiff rng-check` lists what a new build changed (run by `build/port-check.ps1`)

## M4: Project and releases

- [x] CI: build and test Core, build GameDiff
- [x] Release packaging: `build/package.ps1` zips `TerrariaTas.exe`, its DLLs, TAS Studio, the docs and the smoke-test movie per Terraria version (`artifacts/TerrariaTas-<version>.zip`) and prints the `gh release create` command that attaches it to a GitHub release
- [x] Porting automation: `build/port-check.ps1` runs `GameDiff check`, rebuilds, and verifies each reference movie twice (every `movies/*.tas` whose player and world are in the data folder, or the ones given)
- [x] Port to Terraria 1.4.6.1 (2026-11-27): 1.4.6 seeds every world-update stream from the clock at launch (`Main._sessionRngSeed ^ name.GetHashCode()`), so TAS sessions now give `Main.SwapRandom` the movie's `Seed` instead, and the RNG tools compute stream seeds the same way. The other changed hooks still hold (version numbers, music invasion groups, hit-code proc sources, whip tag damage). `port-check` clean (my-run, run2 in sync across launches), `--new` normal and journey, a new world's movie verified twice, 4 teleports as forecast. Movies made on 1.4.5.8 replay differently on 1.4.6.1 (other stream seeds)
- [ ] Release `v1.4.6.1-r1` (`build/package.ps1`)
