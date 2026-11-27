# Terraria TAS

A tool-assisted speedrun (TAS) framework for **Terraria 1.4.6.1** on Windows: frame advance, savestates,
input movies and determinism checks, with RNG prediction tools to come.

Terraria is a .NET/XNA game, so instead of emulating hardware, `TerrariaTas.exe` loads your own installed
`Terraria.exe` into its process and takes control of its clock, input, random number generators and
memory. The game files are never modified. See [docs/research.md](docs/research.md) for the full picture.

## Status

Early, but the core loop works end to end:

- Pause, frame advance, fast-forward, 9 savestate slots plus the movie start.
- Recording: play with your keyboard and mouse (in real time or frame by frame) and the movie writes itself,
  savestates included.
- Text movies ([format](docs/movie-format.md)) played through virtual keyboard, mouse and gamepad,
  including cursor targets in tile coordinates and `Repeat` blocks.
- `--new` creates a character, a world (size, evil, difficulty, seed) and the movie that starts with them. A movie
  pins its character and world files by checksum, so it always starts from the same files.
- The movie's player and world load straight from the title screen, with the game settings and key bindings it
  states (graphics options that change random draws, zoom, interface size, controls), whatever your
  configuration says. Achievements, resource packs and the texture cache are pinned too.
- `--verify`: replay across launches, from a mid-movie savestate and from the movie start state are
  frame-for-frame identical on the test movies, and the game's global state audits clean.
- `GameDiff` pinpoints what a Terraria update changes for the tool ([porting guide](docs/porting.md)).
- TAS Studio, a separate app with tool windows: input display, player position and velocity to the sub-pixel,
  timeline with savestates, monster/NPC/item lists, every RNG stream with upcoming values, drop forecasts with
  a kill log, damage rolls of the held weapon with a hit log, Teleportation Potion destinations with a teleport log, a
  map of the whole world with a coordinate crosshair, and a memory watch of any game value, logged per frame on
  request ([guide](docs/studio.md)).
- Hot reload: save the movie in a text editor and the game follows, replaying from a savestate when it already
  played through the change.

## Requirements

- Windows with Terraria **1.4.6.1** installed (Steam or GOG). The tool refuses other versions.
- A sound output (speakers, headphones, a monitor with sound or a virtual audio device). Without one, Terraria
  skips every sound and the random numbers they roll, so movies refuse to run.
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

Releases are zips named after the Terraria version they run on (`TerrariaTas-1.4.6.1-r1.zip`): unzip anywhere and
run `TerrariaTas.exe` from there (TAS Studio is in `Studio\` and needs the
[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)). Maintainers build one with
`.\build\package.ps1`, which prints the command that publishes it.

## Use

In PowerShell, from the repository root:

| Command | What it does |
|---|---|
| `.\run.ps1 --new` | Creates a new character and a new world, and a movie that starts with them, then records it from its first frame. |
| `.\run.ps1 --new speedy --crimson --expert` | The same in a crimson world in expert mode, as `movies\speedy.tas`. |
| `.\run.ps1 speedy` | Plays the movie from its start, to watch: nothing records into it or changes it. |
| `.\run.ps1 speedy --edit` | Opens the movie to work on it: paused at its start, F1 records, savestates, and saving it in a text editor reloads it. |
| `.\run.ps1 --list` | Lists the movies with the character and world each starts with, and checks those files. |
| `.\run.ps1 speedy --verify` | Determinism self-test, see [Checking determinism](#checking-determinism). |
| `.\run.ps1` | Free play: the game as usual, with the TAS hotkeys once in a world. |

`run.ps1` builds everything, starts the game and opens TAS Studio once the game window is up (`-NoBuild` and
`-NoStudio` skip those). A movie is a name in the `movies` folder (`speedy` is `movies\speedy.tas`) or a path. When
the game refuses its arguments, the reason shows in red and Studio stays closed. Without the script, run
`src\TerrariaTas.Launcher\bin\Release\net48\TerrariaTas.exe` with the same arguments (`--help` lists them all); it
finds movies in the `movies` folder of the current folder, or the one `--movies <dir>` names.

The game opens, loads the movie's world and starts at frame 0, the movie start. The window title shows the frame and
state. The window keeps the movie's size (no resizing or maximizing), and frames wait while it is minimized.
Watching plays straight through to the end of the movie, where it pauses (breakpoints only pause `--edit` sessions);
the hotkeys below still pause, advance frames and load savestates, but recording and reloading are off.

### New movies

`--new` makes everything a movie starts from: a softcore character, a small corruption world in normal difficulty
with a random seed, and the movie, named after the time (`movies\run-20261118-153205.tas`) unless you give it a name.
Choices, in any order after `--new`:

| Option | Choices |
|---|---|
| world evil | `--corruption` (default), `--crimson`, or `--evil corruption\|crimson` |
| difficulty | `--normal` (default), `--expert`, `--master`, `--journey`, or `--difficulty <...>`. Journey worlds only take journey characters, so `--journey` makes a journey character. |
| world size | `--small` (default), `--medium`, `--large`, or `--size <...>` |
| world seed | `--seed <number>` (default: random). Special seeds (for the worthy, drunk world...) are not supported yet. |
| your own files | `--player <file>` or `--world <file>`: start with a character or in a world of the data folder instead of new ones |

The character and the world are made by the game's own creation code, exactly as its menus would, and saved in the
TAS data folder, `Documents\My Games\Terraria TAS`, under `Players\` and `Worlds\`, named after the movie. A first
game window does that and closes; a second one starts recording. Recording right after world generation in the same
game launch would carry what generation leaves in the game's memory into the movie.

### A movie and its files

A movie names its character and world files, and pins them with their SHA-256 checksums (`PlayerChecksum`,
`WorldChecksum` in its header): every launch of the movie starts from the very same files, on any machine. A movie
does not start from a file that changed (a world played in, a character played with) and says so; put the original
file back. Movies made before checksums existed play with a warning, and `--edit` adds the checksums the first time
(keeping the previous file as `<movie>.tas.bak`). To share a movie, share the `.tas` file with its two files.

TAS sessions never write to these files, nor to your normal saves, nor into the Terraria install folder (when the game
logs an error there, such as a crash, the log goes to the data folder's `client-crashlog.txt`). They never write the data folder's
`achievements.dat`, and with a movie loaded the game never saves its `config.json` and `input profiles.json`: the
movie's settings and key bindings never become yours. What TAS sessions write there is the window size and mode, into
`config.json` before launch because Terraria creates its window from it (with a language and a client id when the
file has none: otherwise a new data folder would be the game's first launch every time), `hotkeys.txt` with the default
TAS hotkeys when it is missing, and with `--new`, the new character and world (and the game's `favorites.json` notes
them).

| Hotkey | Action |
|---|---|
| F1 | recording on / off |
| F2 | play / pause |
| F3 | frame advance (hold to repeat) |
| F4 | fast-forward while held |
| F5 / F6 | save / load the current slot |
| F7 / F8 | previous / next slot (1-9) |
| F9 | back to the movie start |
| F10 | reload the movie file now (saving it in an editor reloads it anyway) |

The hotkeys work while the game window has focus. The game never sees them (nor records them), so Terraria's
own F7-F10 toggles are off during TAS sessions. F11 (hide UI) is an ordinary game key: it works in free play,
past the end of the movie, and while recording, where it is recorded like any other key.

To use other keys, edit `hotkeys.txt` in the data folder (the first launch creates it with the keys above) and
restart the TAS. One `Action: Key` line per hotkey, keys named as in movies; nothing after the colon unbinds one:

```
FrameAdvance: V
FastForward: B
ReloadMovie:
```

The actions are `ToggleRecording`, `PlayPause`, `FrameAdvance`, `FastForward`, `SaveState`, `LoadState`,
`PreviousSlot`, `NextSlot`, `Restart`, `ReloadMovie`, and `ToggleOverlay`, which has no key until you give it one: it
shows the frame, position and velocity over the game (TAS Studio's Player window has a checkbox for it too). Keep them off keys your movies press: the game never sees
a hotkey. TAS Studio takes the game's keys when it connects.

Other options: `--movies <dir>` (another movies folder), `--data <dir>` (another data folder), `--terraria <dir>`.

### Recording

`.\run.ps1 --new` waits at frame 0 of the new movie with recording on. Play with the game window focused: F2 plays in
real time, F3 advances one frame with whatever keys and mouse buttons you hold at that moment, and savestates
work as usual. Every frame you play writes your input into the movie at that frame and ends the movie there,
so loading a savestate and playing again replaces what came after it: that is how you redo a section. Loading
a savestate while recording also brings back the input it was made with, so the movie always matches the game.
While recording and paused, the game draws nothing, so Windows' own cursor shows over it: it is where the next
frame aims. TAS Studio's input display gives its exact pixel and the tile under it.

To go on with a movie later, open it with `--edit` (`.\run.ps1 speedy --edit`); `--new` never replaces a movie that
exists. F1 turns recording on (and pauses) or off at any time, also in an existing movie: run to the frame you want
to redo, press F1, play. With recording off, frames replay the movie and loading savestates never changes it. The movie file
is saved a second after you stop, every ten seconds while you play, and when the game closes; the first save
over an existing movie keeps the previous version as `<movie>.tas.bak`, and a game crash saves it too. Only the game window's input is
recorded: while another window has focus, recorded frames have nothing pressed. Typing into chat, signs or
name fields does not work yet in movies: the characters are dropped, since a movie cannot hold them.

### Editing the movie while the game runs

With the movie open in `--edit` (or recording a `--new` one), edit the `.tas` file in any text editor and save it: the
game loads it within half a second (F10 at once). When the
game already played through a line you changed, the TAS loads the latest savestate made before that line and replays
the movie back to the frame it was on, paused there, so the result of the edit shows right away. Savestates of
other versions of the movie are skipped; the movie start (slot 0) always qualifies. Lines changed ahead of the
current frame simply play when the game gets there. A file with an error is not loaded: the window title names the
line, and the TAS waits for the next save. Header changes (`Seed`, settings...) take effect the next time the TAS
starts.

Saving the file while recording is fine too: the edit wins. Frames recorded but not saved yet go to
`<movie>.recording-<date>-<time>.tas` next to the movie, and recording stops. The TAS never writes over an edit.

### TAS Studio

Tool windows that read the game while you TAS: what is pressed (and what the movie presses next), the player's
position, velocity and timers exactly as the game holds them, the movie
around the current frame with load/save buttons on its savestates, monsters, town NPCs and items, every RNG
stream with its upcoming values, a drop forecast that shows, for any monster, after how many random draws
the item you want drops, the crit and damage of your weapon's next hits by draw, where a Teleportation Potion lands
by draw, a map of the whole world to pan and zoom with a crosshair that reads the tile, compass and depth under it,
and a memory watch of any value you name (`player.velocity.X`, `Main.npc[3].ai[0]`). Start
it before or after the game; it connects by itself. `run.ps1` builds and
opens it for you. Details in [docs/studio.md](docs/studio.md).

```
dotnet build src/TerrariaTas.Studio -c Release
src\TerrariaTas.Studio\bin\Release\net8.0-windows\TerrariaTas.Studio.exe
```

### Checking determinism

```
.\run.ps1 my-movie --verify
```

Plays the movie, replays it from a savestate taken halfway and from the start, and compares every frame
(and the previous launch's run). The window closes by itself; the result is printed and written to
`verify-result.txt` in the data folder, with the first diverging frame, which state differs and which game
method drew a different random number. Exit code 0 means in sync. The report also lists game values that a
savestate load did not bring back, and values that differ from the previous launch at the movie start: both
lists should be empty, and anything on them is a desync waiting for the right movie.

### After a Terraria update

```
dotnet run --project tools/TerrariaTas.GameDiff -- check "<Terraria folder>\Terraria.exe" compat/terraria-surface.txt
```

lists every game member the TAS depends on that disappeared or changed. Full procedure in [docs/porting.md](docs/porting.md).

## Documentation

- [docs/research.md](docs/research.md): how Terraria works under the hood for TAS purposes, determinism findings, RNG map
- [docs/architecture.md](docs/architecture.md): code layout and the rules that keep features independent
- [docs/movie-format.md](docs/movie-format.md): the `.tas` format
- [docs/studio.md](docs/studio.md): TAS Studio, its tools and how to read a drop forecast
- [docs/porting.md](docs/porting.md): versioning and porting to a new game version
- [TODO.md](TODO.md): roadmap
- [CONTRIBUTING.md](CONTRIBUTING.md): code rules

## Legal

Not affiliated with or endorsed by Re-Logic. You need your own copy of Terraria; this repository contains
no game code or assets. TAS sessions run the game offline: no Steam connection, achievements or cloud saves.

MIT License.
