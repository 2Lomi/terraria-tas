# Contributing

This will be a large engine. These rules keep it readable.

## Code

- **Readable first.** Short methods, intention-revealing names, no clever tricks. If a reviewer has to
  run the code in their head to understand it, rewrite it.
- **Comments explain why, not what.** Write one when the reason is not visible in the code: a Terraria
  quirk, a determinism trap, a non-obvious invariant. No commented-out code, no restating the code.
- **Encapsulation.** Follow [docs/architecture.md](docs/architecture.md): a feature lives in its own
  folder and owns its Harmony hooks; features do not reference each other; only `Session/` wires them;
  shared game models go to `GameAccess/`.
- **Game-independent logic goes to Core** and gets unit tests (`dotnet test`).
- **Fail fast.** Never leave the game running in an unknown state; abort with the reason.
- Prefer `nameof(Main.x)` over strings so the compiler catches renames. Private members need strings:
  `GameDiff` tracks those (`named` entries in `compat/terraria-surface.txt`).

## Verifying a change

- `dotnet test tests/TerrariaTas.Core.Tests`
- If the change touches the runtime: `TerrariaTas.exe movies/<movie>.tas --verify`, twice (the second
  launch also compares against the first). Mention the result in the PR. `.\build\port-check.ps1` does
  both launches for every movie whose player and world you have.
- If it touches new game members: regenerate `compat/terraria-surface.txt` (see docs/porting.md).

## Never commit

- Terraria binaries, content or decompiled source. Decompile locally for research, describe findings in
  your own words (docs/research.md), and reference members by name.
- Player or world files.
