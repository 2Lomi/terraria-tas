# Movie format (`.tas`)

A movie is a plain text file: a header, then one line per run of identical inputs.
It is meant to be written and edited by hand, diffed and reviewed like code.

```
# Comments start with '#', anywhere on a line.
Terraria: 1.4.6.1
Player: Runner.plr
PlayerChecksum: 8e77513daddd0df5f1d474ed718947a02d664913f070043e5cb7519ab5a36ca3
World: Forest.wld
WorldChecksum: 8b59db088813ba5a50acc98398bfb1ae36972497c321dbce95dff74f7c08ee79
Seed: 1234
Date: 2026-06-15
Resolution: 1280x720

   30                      # 30 frames, nothing pressed
   90 D                    # hold D (walk right) for 90 frames
   15 D Space              # walk right and jump
    1 D2                   # select hotbar slot 2
   60 LMB @672,392         # hold left click with the cursor at window pixel (672, 392)
***                        # breakpoint: playback pauses here
   40 LMB @t1520,310       # aim at the center of tile (1520, 310), whatever the camera does
    3 WheelUp              # scroll up one notch on each of 3 frames
```

## Header

`Name: value` lines before the first input line. All optional.

| Property | Meaning | Default |
|---|---|---|
| `Terraria` | Game version the movie was made on. A mismatch is reported when playing. | none |
| `Player` | Player file inside the data folder's `Players/` directory. | none |
| `PlayerChecksum` | SHA-256 of that file (64 hexadecimal digits): the movie only starts from this exact file. | none (not checked) |
| `World` | World file inside the data folder's `Worlds/` directory. | none |
| `WorldChecksum` | SHA-256 of that file: the movie only starts from this exact file. | none (not checked) |
| `Seed` | Seed of the random generators at the movie start: the shared one (`Main.rand`), and the per-phase streams, each seeded from it combined with its name. The game takes both from the clock when it starts; the movie fixes them. | `0` |
| `Date` | Calendar date the game sees (`yyyy-MM-dd`). Halloween and Christmas content depends on it. | `2026-06-15` |
| `Resolution` | Window size (`WIDTHxHEIGHT`), in even numbers from `800x600` to `1920x1200` (Terraria's limits). It changes mouse coordinates and what is on screen, so the window keeps it while the movie is loaded: it cannot be resized or maximized, and frames wait while it is minimized. | `1280x720` |
| `Zoom`, `UIScale`... | Game settings that change the simulation: see [Game settings](#game-settings). | a fresh install's |
| `Bind <action>` | Keys and mouse buttons of a control: see [Key bindings](#key-bindings). | a fresh install's |

With `Player` and `World` set, the TAS loads them straight from the title screen and frame 0 is the
first frame in the world. Without them, you enter a world through the menus yourself and the movie
starts there (free play).

`--new` creates the player and world files and writes both checksums. With them, the TAS refuses to start the
movie from a file that changed (a world played in, a character played with): it would not replay. A movie without
checksums starts from the files as they are, with a warning, and opening it with `--edit` adds the checksums of the
files it finds. To make a movie start from other files on purpose, change `Player` or `World`, delete the checksum
line and open the movie with `--edit`.

### Game settings

Some of Terraria's options change what the game simulates, not just what it shows: a few graphics options make
drawing take numbers from the random generator that weather, reforging and bags share, or spawn dust and gore
into the pools gameplay uses; others change how input is understood or where the interface is. A movie fixes all
of them: the TAS applies them when the movie loads, whatever your configuration says. From there they are game
state like any other: changing one during the movie (settings menu, zoom keys) is input the movie replays, and
savestates keep them.

A movie that does not state a setting gets the value of a fresh install. `--new` writes into the new movie the
settings of your TAS configuration that differ from those. Floats are written exactly as the game holds them
(as in its `config.json`), so an interface size picked with the slider replays to the pixel.

| Setting | Fresh install | Values | Changes |
|---|---|---|---|
| `Zoom` | `1` | 1 to 2 | the mouse-to-world mapping, the lit area, snow |
| `UIScale` | `auto` | 0.5 to 2, or `auto` | where inventory, crafting and chest slots are; `auto` is what a fresh install picks for the window height (height / 1080 × 1.1) |
| `Background` | `on` | on, off | the cloud layer weather reads, mushroom background rolls |
| `FlashyEffects` | `on` | on, off | lightning rolls and the light of a flash |
| `SwayInWind` | `on` | on, off | particles, dust and critter flies emitted by grass and trees |
| `BloodAndGore` | `on` | on, off | off rolls dust and gore again |
| `PickupText` | `on` | on, off | crafting flashes that spawn dust |
| `WaterfallLimit` | `1000` | 0 to 100000 | waterfall dust and light |
| `ScreenShake` | `on` | on, off | the camera |
| `StormEffects` | `on` | on, off | whether the aurora (whose light gameplay reads) shows during storms |
| `Map` | `on` | on, off | the map key, pylon teleports, inventory map buttons |
| `MapScale` | `1` | 0.5 to 1 | where the minimap buttons are |
| `MinimapFrame` | `Default` | Default, Golden, Remix, Sticks, StoneGold, TwigLeaf, Leaf, Retro, Valkyrie | where the minimap buttons are |
| `SmartCursor` | `toggle` | toggle, hold | what the smart cursor key does |
| `SmartCursorReleaseMidUse` | `off` | on, off | hold mode: releasing the key ends smart cursor during an item use |
| `SmartCursorBlocks` | `off` | on, off | smart cursor also aims common blocks |
| `SmartAxeAfterPickaxe` | `off` | on, off | smart cursor looks for trees after ore |
| `ShiftClickTrash` | `off` | on, off | Shift-click sends items to the trash |
| `QuickTrash` | `on` | on, off | Ctrl-click trashes or sells |
| `ArmorSetBonusKey` | `down` | down, up | direction that triggers armor set bonuses |
| `FavoriteKey` | `LeftAlt` | a key name | key held to favorite an item by clicking it |
| `Autoswing` | `off` | on, off | every damaging item reuses itself |
| `AutoDoors` | `everything` | off, gamepad, everything | doors open by walking into them |
| `HoverControl` | `hold` | hold, click | hovering with wings and mounts |
| `DoubleTapDash` | `on` | on, off | double-tapping a direction dashes |
| `Crafting` | `modern` | modern, classic | the crafting interface |
| `QuickStack` | `quick` | quick, smart | what the nearby-chests stack button does |
| `CraftFromChests` | `on` | on, off | recipes use nearby chests |
| `ForwardZoom` | `hold` | hold, click, always, off | when a rifle scope, the sniper rifle or binoculars move the camera towards the cursor (an option of the control profile, "Forward Zoom" in the controls menu) |

Settings that only matter on some machines are pinned for every movie instead: graphics quality, lighting mode,
frame skip, 4K support (off: movies up to 1920x1200), and the volumes, which only change what you hear (a muted
game picks music and rolls sounds exactly like an audible one).

The rest of the data folder is pinned too:

- **Achievements.** A movie starts with no achievement completed, as on a fresh install. They change what a
  movie does: the inventory shows a card for the next achievement (hovering it keeps clicks from the world), and
  completing one shows a popup that catches clicks for a few seconds. TAS sessions never write `achievements.dat`,
  and savestates keep the progress and the popups.
- **Resource packs.** TAS sessions use the game's own content. Packs can replace sounds (their length decides
  random rolls) and fonts and texts (they place buttons). The packs stay selected in `config.json` for the
  normal game.
- **Your configuration.** With a movie loaded, the game never saves `config.json` or `input profiles.json`. The
  movie's settings and key bindings never replace yours. Only the window size and mode are written into
  `config.json`, before launch, because Terraria creates its window from it.

A player saved while dead starts the movie with the respawn timer the file has; the game would otherwise take off
the real time since saving.

The machine needs an audio device. Without one, Terraria skips every sound and the random numbers sounds roll, so
the TAS refuses to play or record a movie there.

### Key bindings

The input lines say which keys are down; the key bindings decide what they do. A movie fixes them: the TAS puts
them in place when the movie loads, whatever `input profiles.json` says. An action the movie does not bind keeps a
fresh install's keys ("Redigit's Pick"). `--new` writes the bindings of your selected control profile that differ
from those into the new movie, so you record with your own keys.

```
Bind Grapple: F                # F grapples instead of E
Bind Jump: Space W             # several inputs: any of them
Bind MouseRight: RMB XB1
Bind QuickMount:               # nothing after the colon: unbound
```

Inputs are written as in input lines: key names and `LMB` `RMB` `MMB` `XB1` `XB2`. The actions are Terraria's
own names, the ones in `input profiles.json`:

| Action | Fresh install |
|---|---|
| `MouseLeft`, `MouseRight` | `LMB`, `RMB` |
| `Up`, `Down`, `Left`, `Right` | `W`, `S`, `A`, `D` |
| `Jump`, `Inventory`, `Grapple` | `Space`, `Escape`, `E` |
| `SmartSelect`, `SmartCursor` | `LeftShift`, `LeftControl` |
| `QuickMount`, `QuickHeal`, `QuickMana`, `QuickBuff` | `R`, `H`, `J`, `B` |
| `MapZoomIn`, `MapZoomOut`, `MapAlphaUp`, `MapAlphaDown` | `Add`, `Subtract`, `PageUp`, `PageDown` |
| `MapFull`, `MapStyle` | `M`, `Tab` |
| `Hotbar1` ... `Hotbar10` | `D1` ... `D9`, `D0` |
| `ViewZoomIn`, `ViewZoomOut` | `OemPlus`, `OemMinus` |
| `Loadout1`, `Loadout2`, `Loadout3` | `F1`, `F2`, `F3` |
| `ToggleCameraMode`, `ToggleCreativeMenu` | `F4`, `C` |
| `Throw`, `HotbarMinus`, `HotbarPlus`, `PreviousLoadout`, `NextLoadout`, `Dash`, `ArmorSetAbility`, `LockOn`, `RadialHotbar`, `RadialQuickbar`, `DpadRadial1`-`4`, `DpadSnap1`-`4`, `MenuUp`, `MenuDown`, `MenuLeft`, `MenuRight` | unbound |

F1-F4 are TAS hotkeys, which the game never sees from your keyboard: to switch loadouts or open camera mode
while recording, bind those actions to other keys (`Bind Loadout1: Z`).

Like settings, bindings are game state once the movie runs: rebinding in the controls menu is input the movie
replays, and savestates keep it. Switching to another control profile there does nothing during a movie (it
would bring in the local file). Menus outside a world use bindings of their own, which movies do not fix.

## Input lines

`<frames> <inputs...>` where inputs are separated by spaces:

| Input | Meaning |
|---|---|
| `A`, `Space`, `D1`, `LeftShift`, `Escape`... | A key held down, by its XNA `Keys` name (case-insensitive). |
| `LMB` `RMB` `MMB` `XB1` `XB2` | Mouse buttons held down. |
| `WheelUp` `WheelDown` | One wheel notch on every frame of the line. Repeat the token for more. |
| `@x,y` | Cursor at window pixel (x, y). |
| `@tX,Y` | Cursor on the center of tile (X, Y), converted with the camera of each frame. |

Every input applies to every frame of its line. The cursor position is the exception: it stays
where the last line put it, like a real mouse. Until a line places it, it rests at the center of the window.

What a key does is up to the movie's [key bindings](#key-bindings): a fresh install's unless the header binds
it (`A`/`D` walk, `Space` jump, `Escape` inventory, `D1`-`D0` hotbar...).

## Breakpoints

A line with `***` pauses playback before the frame of the next input line. Fast-forward stops there. Watching a
movie (started without `--edit`) plays through breakpoints and only pauses at the end.

## Repeat blocks

`Repeat <rounds>` ... `EndRepeat` plays the input lines between them that many times, as if they were written out:

```
Repeat 20          # mash the hotbar for 40 frames
   1 D1
   1 D2
EndRepeat
```

The cursor carries over from round to round like everywhere else: a line without `@` keeps the cursor of the line
before it, which for the first line of a round is the last cursor of the round before. Blocks cannot be nested, hold
no breakpoint (put `***` before or after the block), and play at most 100000 lines. Line numbers in TAS Studio are
the ones of the block's text, so every round shows the same numbers.

Recording into a block (or loading a savestate made inside one while recording) writes the block out round by round
from its `Repeat` line, since the rounds no longer match: the text before the block stays as it is.

## Recorded movies

Recording (F1, see the README) writes this same format. Each recorded frame extends the last line when its
input is the same, and starts a new line otherwise. The cursor is recorded in window pixels and written only
when it moves (`   12 D LMB @655,391`), and wheel notches land on the frame they were turned. The TAS hotkeys
and keys the format has no name for (Windows key, media keys) are left out.

Recording frame N ends the movie at N: lines and breakpoints after it belong to the attempt being replaced.
When it saves, the TAS keeps the file's own text (comments, spacing, tile targets) up to the first line
recording changed, and writes the lines from there.

## Timing quirk

Terraria reads the keyboard at the end of an update and acts on it during the next one, so a key
pressed on frame N affects frame N + 1. The mouse is read at the start of the update and affects frame N.
The movie stores what the devices report on each frame; the game decides when it reacts.
