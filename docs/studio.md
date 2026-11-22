# TAS Studio

Studio is a separate app whose tool windows read the game while you TAS: what is pressed, the movie around
the current frame with its savestates, monsters, NPCs and items, every RNG stream, and drop forecasts.
It talks to `TerrariaTas.exe` through a local pipe, so it can be started, closed or restarted at any time
without touching the game or the movie.

## Run

`.\run.ps1 my-movie --edit` (or any other command of the README) from the repository root builds `TerrariaTas.exe` and Studio, starts the game
and opens Studio once the game window is up: a game that refuses its arguments opens no Studio and says why
in red. By hand:

```
dotnet build src/TerrariaTas.Launcher -c Release
dotnet build src/TerrariaTas.Studio -c Release
src\TerrariaTas.Studio\bin\Release\net8.0-windows\TerrariaTas.Studio.exe
```

Start it before or after `TerrariaTas.exe`: it connects by itself, and reconnects when the game restarts.
Only one Studio can be connected at a time. Rebuild `TerrariaTas.exe` along with Studio: one built before
Studio existed has no link, and the hub says so when it finds such a game running. The hub window opens the tools; Studio remembers which tools
were open and where (`%LOCALAPPDATA%\TerrariaTas\studio.json`).

The TAS hotkeys (F1 to F10, or the keys of the game's `hotkeys.txt`, which Studio receives when it connects) work
in every Studio window as in the game window, and the hub's buttons show
theirs: ● Record (F1), Play / pause (F2), Frame +1 (F3), Save slot (F5), Load slot (F6), Restart (F9). The
timeline's "Save to slot N (F5)" does what F5 does too. "Keep Studio windows on top" keeps them above the game.

The hub's **● Record** button (F1) turns recording on and off; the hub's status turns red while recording. Only
movies opened with `--edit` (or just created with `--new`) record: a movie started to watch says so and stays as it is.
Recording takes its input from the game window only: a frame played from Studio's buttons, or while a
Studio window has focus, is recorded with nothing pressed. To record input, focus the game window and use
the hotkeys there.

## Tools

| Tool | Shows | Refreshes |
|---|---|---|
| Input display | A keyboard and a mouse. Filled: what the game read on the last frame. Outlined: the next frame's input, the movie's, or while recording your keyboard and mouse right now, with the pixel and tile the next frame aims at. | Always, also while playing |
| Player | Position (the top-left corner, as movement code moves it) and velocity exactly as the game holds them, sub-pixels included, with the tile and the offset inside it; what the last frame actually moved (collisions included); jump frames, wing and rocket time, dash cooldown, where the current fall started; the held item and its use timers; life, mana, defense, breath, immunity frames, luck; buffs with the frames they have left. A checkbox shows the frame, position and velocity over the game too (bottom right, drawn after the game's own draw: the simulation never sees it, but screenshots and recordings of the window do). | Always, also while playing |
| Timeline & savestates | The movie frame by frame around the current frame, with movie line numbers and breakpoints. "Load slot N" on the frame a slot was saved at, "Save to slot N" on the current frame, and a panel with every slot. Double-click a frame to run to it: the closest earlier savestate of this version of the movie is loaded first if needed, and the frames on the way replay the movie, also while recording. | Always |
| Monsters & bosses, Town NPCs & critters | Every NPC: HP, tile position, distance, coin value, AI values. Double-click one for its drop forecast. | Paused frames |
| Items on the ground | Every item lying in the world, with its prefix, stack, position and age. | Paused frames |
| RNG streams | Every random number generator: position (draws since it was seeded), draws during the last frame, and its upcoming values with what `Next(n)` gives for them. | Paused frames |
| Drop forecast | See below. | Paused frames |
| Memory watch | Any value of the game you name with a path, see below. | Always, also while playing |

World data is captured at paused frame boundaries (pause, frame advance, savestate load). While the game
runs, these windows keep the last paused frame and say so.

## Drop forecast

Each column is one way a kill can roll its loot, because each update phase draws from its own stream:

- **Swing kill** (`UpdatePlayers`): the killing blow is a weapon swing.
- **Projectile kill** (`UpdateProjectiles`): anything that is a projectile, so arrows, bullets, spears,
  yoyos, shortswords, minions, magic and dart traps.
- **NPC-update kill** (`UpdateNPCs`): lava, damage over time from debuffs, other NPCs.

Row *k* (the offset) is what the NPC drops if its loot roll starts *k* draws after the frame boundary you
are paused at, on that column's stream. Items show as their inventory pictures, with their prefix and
stack; hover a cell for the names. Pick an "Item to find" to highlight the rows where it drops and list the
first offsets per column. The header shows the luck the roll uses: luck changes both the odds and the
number of draws each roll takes.

Row +0, the next draw, is blue. The other rows cycle through gray shades by stream position, not by row: a
roll keeps its shade from frame to frame, so after a frame advance the shades of a column have moved up by
as many rows as its stream made draws. Each column title gives the exact count ("+68 last frame"), which
also covers frames longer than one cycle of shades. "Shades" sets the cycle length.

A kill spends draws before its loot: the crit and damage rolls, the hit and death sounds, the blood and
death particles (about 300 draws for a slime dying on screen), and everything else that drew from the same
stream earlier in that frame. The **kill log** shows where each real kill rolled: "loot at offset 332" means
the loot started 332 draws after the start of that frame. When a forecast was taken at the start of that
frame for that NPC, the log also says whether it was right.

A manipulation loop:

1. Pause before the frame where the kill happens and save a state.
2. Frame-advance through the kill. The kill log shows its offset, for example +332, and the result.
3. Find the closest offset where the item you want drops in that column, for example +350: the kill needs
   18 more draws before its loot.
4. Load the state and change what happens before the loot: kill a frame later, kill with another weapon
   (another column), get a crit or not, kill on or off screen... Repeat until the offset lands on a match.

### Limits

- The forecast runs the game's own drop code on copies of the streams, so no drop table is re-implemented.
  It leaves out only the kill counting (banners, bestiary, achievements) and the death events, which change
  the world. For bosses the death events also drop potions and hearts and draw before the coins: items are
  exact, coins and hearts can differ (the window says so).
- It uses the state of the paused frame: the NPC's position and biome, its coin value, your luck, life and
  mana. If they change before the kill, the outcome table changes too.
- Boss treasure bag contents are rolled when the bag is opened from the inventory, on the shared RNG.

## Memory watch

Type a path to a value and press Enter: it shows next to the path, updated on every frame, also while playing.

```
player.velocity.X              the local player's horizontal speed
player.position                 (x, y), exactly as the game holds the floats
Main.npc[3].ai[0]               an NPC's AI state
Main.windSpeedCurrent           a static field of a game class
player.buffType.Length          lengths of arrays and lists
player.HeldItem().type          a property: see below
```

A path starts from `player` or a game class whose global state the TAS already tracks (`Main`, `NPC`, `Player`,
`Projectile`, `Item`, `WorldGen`, `Lighting`, `Wiring`, `Collision`, the events...), by its short or full name, then
names fields (private ones too) and indexes arrays and lists. Reading a field never runs game code, so watching never
changes the game. A property's getter is game code: it is read only when the path says so with `Name()`, and then
it is up to you that it changes nothing (most getters only compute a value). Other classes are refused: one the game
has not set up yet would run its static constructor in the middle of the movie.

"Log every frame" makes the game write the values of every frame to `<movie>.watch.csv` in the TAS data folder (the
window title says where), fast-forwarded frames included: a frame number per row, in the order they ran, so a
savestate load shows as the frame numbers going back. Studio remembers the paths; closing the window stops the
watch and its log.

## How it works

- The link is the named pipe `TerrariaTas.Studio`. Messages are length-prefixed binary frames defined in
  Core (`TerrariaTas.Core.Studio`), so both sides share one definition; bump `StudioProtocol.Version` on any
  layout change.
- In the game, `Runtime/Studio` moves bytes on background threads only. Everything that touches the game
  runs on the main thread at a frame boundary: `Session/StudioLink` decides what to send,
  `Runtime/Inspection` reads the world and `Runtime/RngTools` tracks stream positions, records kills and
  computes forecasts.
- The movie goes over whole on connection, then as the lines from the first one that changed: recording
  only changes the end. While the game runs, at most four movie updates a second go out.
- Stream positions are found by stepping a model of each stream from its previous state until it matches,
  so tracking never hooks or changes a generator. They are part of savestates.
- A forecast swaps `Main.rand` for a copy of the stream, calls the NPC's drop methods, reads the items they
  spawned and restores everything (items, item-slot timers, emergency-stacking transfers, the NPC's fields).
  It fails loudly if anything was left changed.
- Item pictures travel over the link: the game's `.xnb` files are compressed in a format only XNA reads.
  `Runtime/Inspection/ItemIconReader` decodes each one once per connection with a private content manager,
  never through the game's asset repository, so the game loads its own textures exactly as without Studio.
