# Movie format (`.tas`)

A movie is a plain text file: a header, then one line per run of identical inputs.
It is meant to be written and edited by hand, diffed and reviewed like code.

```
# Comments start with '#', anywhere on a line.
Terraria: 1.4.5.8
Player: Runner.plr
World: Forest.wld
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
| `World` | World file inside the data folder's `Worlds/` directory. | none |
| `Seed` | Seed of the shared RNG (`Main.rand`) at the movie start. The per-phase streams are seeded by the world itself. | `0` |
| `Date` | Calendar date the game sees (`yyyy-MM-dd`). Halloween and Christmas content depends on it. | `2026-06-15` |
| `Resolution` | Window size (`WIDTHxHEIGHT`). It changes mouse coordinates and what is on screen. | `1280x720` |

With `Player` and `World` set, the TAS loads them straight from the title screen and frame 0 is the
first frame in the world. Without them, you enter a world through the menus yourself and the movie
starts there (free play).

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
where the last line put it, like a real mouse.

Terraria's keybindings are the defaults of a fresh data folder (`A`/`D` walk, `Space` jump,
`Escape` inventory, `D1`-`D0` hotbar...).

## Breakpoints

A line with `***` pauses playback before the frame of the next input line. Fast-forward stops there.

## Timing quirk

Terraria reads the keyboard at the end of an update and acts on it during the next one, so a key
pressed on frame N affects frame N + 1. The mouse is read at the start of the update and affects frame N.
The movie stores what the devices report on each frame; the game decides when it reacts.
