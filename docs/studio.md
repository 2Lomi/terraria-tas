# TAS Studio

Studio is a separate app whose tool windows read the game while you TAS: what is pressed, the movie around
the current frame with its savestates, monsters, NPCs and items, every RNG stream, drop, damage and teleport
forecasts, and a map of the whole world.
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
| Damage rolls | The crit and damage of the held weapon's next hits for every offset of the swing and projectile streams, what a target would lose, and a hit log. See below. | Paused frames |
| Teleport forecast | Where a Teleportation Potion lands for every offset of the players' stream, with the compass and depth meter readings there, and a teleport log. See below. | Paused frames |
| World map | The whole world as the game's map draws it, fully revealed, to pan and zoom, with a crosshair pointer that reads the tile, compass and depth under it. See below. | Read when asked, at a paused frame; your position live |
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

## Damage rolls

The crit and damage of the next hit of the weapon you hold, depending on when the hit rolls. Each column is one kind
of hit, rolling on the stream of its update phase:

- **Swing** (`UpdatePlayers`): the held item's own swing, for items whose swing hits (not shortswords, guns, staffs...).
- **Projectile** (`UpdateProjectiles`): one of your projectiles. Its damage is that of your projectile in flight
  closest to the target, as its hit uses it (an arrow carries its ammo's damage, a thrown flail doubles it, a rocket
  doubles it on its main target...), or else that of what your weapon would shoot now, with the ammo it would use (the
  column says which). The arrows of town NPCs and falling stars are not yours, even though the game counts them as
  the local player's in single player: they roll no crit and no luck, and they show neither here nor in the hit log.

Row *k* is the hit whose first roll comes *k* draws after the paused frame boundary. A hit rolls its crit first: one
draw for a swing, one per damage class for a projectile (none for minions, which cannot crit). Then the damage roll
(`Main.DamageVar`): ±15% in 1% steps, one draw, plus one or two when your luck is not 0 (good luck may roll again and
keep the higher result, bad luck the lower). A cell reads `CRIT 14 → 24 KILL`: the hit crits, rolls 14, and the target
loses 24 (half its defense off, your armor penetration back, doubled for the crit), which is enough to kill it.

The target is the nearest enemy until you pick one; "(no target)" shows the rolls only. "Base damage" and "Crit %"
replace the game's values, for example with the damage of a projectile you are about to fire. "Highlight" marks
crits, kills, or rolls of at least some damage, and "Next match" jumps to the next one. Row +0 is blue, crits yellow.

The **hit log** lists every hit you land on an NPC: the stream, the offset its first roll was at, the crit, the damage
roll and what the NPC lost, and, when a forecast was taken at the start of that frame for the same weapon or
projectile, whether it was right (not when Base damage or Crit % replaced the game's values). The
manipulation loop is the drop forecast's: pause before the hit, save, advance through it, read its offset in the log,
find the closest row with the hit you want, load and change what is drawn before the hit (another frame, another
position, another weapon).

### Limits

- The rolls of an ordinary hit. Special effects draw between the crit and the damage roll, or change the damage:
  timed crits, monk armor, Golem's sentry backpack, Pyroclast, the Keybrand, whip tags, banners, the parry bonus,
  piercing projectiles losing damage... The hit log always shows what a real hit did.
- "→" applies the NPC's defense, armor penetration (yours, the projectile's, Ichor, Broken Armor, Betsy's Curse), the
  crit and the NPC's damage-taken multiplier. Some bosses change the damage further in their own code.

## Teleport forecast

Where a Teleportation Potion sends you, depending on when its destination is rolled. The potion rolls it during the
players' update (`UpdatePlayers` stream), on the 16th frame of its use, when its use timer is down to 2. While you
are drinking one, the window says during which frame.

Row *k* is where you land if the roll starts *k* draws after the paused frame boundary. The roll picks random places at
least 100 tiles from the left, right and top edges, down to 100 tiles above the bottom, two draws per place, until
one fits: from there you land on a floor less than 100 tiles below, where you fit with room to move a tile each way,
with no lava or hurting tiles (spikes...) around you, and not in the Lihzahrd temple before Plantera or the dungeon
before Skeletron. After 1000 tries you stay where you are ("no spot found"). The columns give the tile you stand on (under the middle of your feet), what the compass and the depth meter
read there, the distance to the target, and how many numbers the roll drew.

To aim for a place, right-click it on the World map (or type a tile and press Look there) and set the radius: the rows
landing there turn green, and the line above the table lists their offsets. Double-click a row, or "Show on map", to
see it on the map, which marks the first destinations (+0, +1...) and the one selected here.

Rows often repeat: a roll that needs several tries ends on the same place as the roll that starts at its last try
(+0 taking 8 draws lands where +2 taking 6 does, and +4, +6...).

The **teleport log** lists every potion you drink: the offset its roll was at, where you landed, and whether the
forecast taken at the start of that frame was right.

A manipulation loop:

1. Drink the potion and pause on the frame before the roll ("rolls during the next frame"). Save a state.
2. Advance one frame. The log shows the offset, for example +6: the players' update drew 6 numbers before the roll.
3. Find the first green row at or after it, for example +40: the roll needs 34 more draws before it.
4. Load and change what the players' update draws before the roll: drink a frame later or earlier (each frame in
   between moves the stream by its own draws: "+N last frame" in the window's header shows how many), move or jump
   (footstep dust), swing something first...

### Limits

- Each row runs the game's search, up to 1000 tries. The header says how long a forecast took: with "Refresh on every
  paused frame", the game waits that long at each paused frame, so lower Rows if frame advance gets slow.
- The forecast is per draw, not per frame. A frame ahead lands where the draws of the frames before it lead: those
  depend on what you do in them, so step there (or replay there from a savestate) to read its offset exactly.
- Destinations depend on the tiles and a few world flags as they are at the paused frame: digging, building or
  liquids moving before the roll change them.
- Only the Teleportation Potion for now. The Magic Conch, the Demon Conch and the Shellphone search differently.

## World map

The whole world as the game's own map draws it, fully revealed: every tile in the color of the in-game map, read from
the running game at a paused frame (the tiles as they are now, not as the world file was saved). Opening the window
reads it at the next paused frame inside a world, a share on each paused tick (about a second for a large world).
"Read the map again" after digging, building or loading another state.

- Drag (left or middle button) to move, the wheel to zoom, from the whole world to 32 pixels per tile.
- The pointer is a crosshair of white lines across the map. Next to it, and at the bottom of the window: the tile
  under it, and what the compass and depth meter would read for a player standing there, computed as the game does
  (two "feet" per tile, from the world's center column and from the surface line).
- Marked: you (cyan, live while the game runs), the spawn point, the first destinations of the Teleport forecast
  (yellow; "Teleport destinations" sets how many, and a place several rows lead to is marked once with its first
  offsets: "+0 +2 +4 … (8 rolls)") and the one selected there (pink), and the teleport target (orange circle). When
  labels would cover each other, yours and the selected one win; a destination's label without room is left out.
- Right-click a place: the Teleport forecast looks for landings within its radius of it. "Clear target" removes it.

The 40 tiles along each edge, which the player can never reach, show black. The map is only read: your exploration
map, what the in-game map reveals, does not change.

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
  It fails loudly if anything was left changed. At the movie start no stream exists yet: the game creates each one
  the first time its phase runs, from the movie's seed and the stream's name, so forecasts at frame 0 start from such a
  fresh stream.
- The damage and teleport forecasts work like the drop forecast: they run the game's own code (`Main.DamageVar`,
  `Utils.CheckForGoodTeleportationSpot`) on copies of the streams and put back what it touched (the teleport search
  leaves collision flags behind); the few lines they mirror (crit rolls, the defense step, the potion's arguments) carry
  `[MirrorsGameMethod]`, so `GameDiff` flags them when the game changes those methods. The ammo of a shot comes from
  the game's own `Player.PickAmmo`, told not to consume. The hit and teleport logs only read, from hooks on the hit
  code and on `Player.TeleportationPotion`.
- The world map comes from the game's map code (`MapHelper.CreateMapTile`, `GetMapTileXnaColor`), read on the main
  thread at paused frame boundaries for 40 ms per tick, and travels run-length encoded, 64 rows per message.
- Item pictures travel over the link: the game's `.xnb` files are compressed in a format only XNA reads.
  `Runtime/Inspection/ItemIconReader` decodes each one once per connection with a private content manager,
  never through the game's asset repository, so the game loads its own textures exactly as without Studio.
