# Terraria TAS

A tool-assisted speedrun (TAS) framework for **Terraria 1.4.5.8** on Windows: frame advance, savestates,
input movies and determinism checks, with RNG prediction tools to come.

Terraria is a .NET/XNA game, so instead of emulating hardware, `TerrariaTas.exe` loads your own installed
`Terraria.exe` into its process and takes control of its clock, input, random number generators and
memory. The game files are never modified. See [docs/research.md](docs/research.md) for the full picture.

## Status

Early, but the core loop works end to end:

- Pause, frame advance, fast-forward, 9 savestate slots plus the movie start.
- Text movies ([format](docs/movie-format.md)) played through virtual keyboard, mouse and gamepad,
  including cursor targets in tile coordinates.
- The movie's player and world load straight from the title screen.
- `--verify`: on a 512-frame test movie, replay across launches and replay from a mid-movie savestate are
  frame-for-frame identical. Replay from the movie start state still diverges late (frame 377) in weather.
  That is the first item of the [TODO list](TODO.md).
- `GameDiff` pinpoints what a Terraria update changes for the tool ([porting guide](docs/porting.md)).

## Requirements

- Windows with Terraria **1.4.5.8** installed (Steam or GOG). The tool refuses other versions.
- [.NET SDK 8](https://dotnet.microsoft.com/download) or newer to build (Windows already has the .NET
  Framework 4.8 runtime the game uses, and Terraria installs XNA).

## Build

```
dotnet build src/TerrariaTas.Launcher -c Release
```

The build looks for Terraria in the default Steam folder. Elsewhere, set the `TERRARIA_DIR` environment
variable or create `build/Terraria.user.props`:

```xml
<Project><PropertyGroup><TerrariaDir>D:\Games\Terraria</TerrariaDir></PropertyGroup></Project>
```

## Use

1. Copy a player (`.plr`) and a world (`.wld`) into `Documents\My Games\Terraria TAS\Players` and `...\Worlds`.
   TAS sessions run on these copies and never write to them, nor to your normal saves.
2. Put their file names in a movie (start from [movies/smoke-test.tas](movies/smoke-test.tas)).
3. Run:
   ```
   src\TerrariaTas.Launcher\bin\Release\net48\TerrariaTas.exe movies\smoke-test.tas
   ```
   The game opens, loads the world and waits paused at frame 0. The window title shows the frame and state.
   Without a movie, the game runs normally (free play) with the TAS hotkeys available once in a world.

| Hotkey | Action |
|---|---|
| NumPad0 | play / pause |
| NumPad1 | frame advance (hold to repeat) |
| NumPad2 | fast-forward while held |
| NumPad4 / NumPad5 | save / load the current slot |
| NumPad7 / NumPad8 | previous / next slot (1-9) |
| NumPad9 | back to the movie start |
| NumPad6 | reload the movie file after editing it |

Options: `--play` (start playing immediately), `--data <dir>` (another data folder), `--terraria <dir>`.

### Checking determinism

```
TerrariaTas.exe movies\my-movie.tas --verify
```

Plays the movie, replays it from a savestate taken halfway and from the start, and compares every frame
(and the previous launch's run). The window closes by itself; the result is printed and written to
`verify-result.txt` in the data folder, with the first diverging frame, which state differs and which game
method drew a different random number. Exit code 0 means in sync.

### After a Terraria update

```
dotnet run --project tools/TerrariaTas.GameDiff -- check "<Terraria folder>\Terraria.exe" compat/terraria-surface.txt
```

lists every game member the TAS depends on that disappeared or changed. Full procedure in [docs/porting.md](docs/porting.md).

## Documentation

- [docs/research.md](docs/research.md): how Terraria works under the hood for TAS purposes, determinism findings, RNG map
- [docs/architecture.md](docs/architecture.md): code layout and the rules that keep features independent
- [docs/movie-format.md](docs/movie-format.md): the `.tas` format
- [docs/porting.md](docs/porting.md): versioning and porting to a new game version
- [TODO.md](TODO.md): roadmap
- [CONTRIBUTING.md](CONTRIBUTING.md): code rules

## Legal

Not affiliated with or endorsed by Re-Logic. You need your own copy of Terraria; this repository contains
no game code or assets. TAS sessions run the game offline: no Steam connection, achievements or cloud saves.

MIT License.
