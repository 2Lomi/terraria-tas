# Versions and porting to a new Terraria patch

## Version scheme

A release is named `<Terraria version>-r<revision>`, for example `1.4.5.8-r1`: the first part says
which game build it runs on, the revision counts releases for that build. `build/Version.props` is the
single place where the targeted Terraria version is written.

- The build stops with an explicit error if the installed `Terraria.exe` is another version.
- `TerrariaTas.exe` refuses to run on another version (`--ignore-version` to override, at your own risk).
- Movies record the version they were made on (`Terraria:` header) and warn on mismatch.

Git tags follow the release name: `v1.4.5.8-r1`. When a patch drops, the last release for the old version
stays available under its tag; `main` moves to the new version.

## When Terraria updates

1. **Keep the old build.** Copy the old `Terraria.exe` somewhere before Steam updates it (or get it with
   the Steam console `download_depot`), so you can diff the two.
2. **See what changed for us:**
   ```
   dotnet run --project tools/TerrariaTas.GameDiff -- check "<new>/Terraria.exe" compat/terraria-surface.txt
   ```
   - `MISSING`: a member we use is gone or renamed. Find its replacement.
   - `CHANGED patched`: a method we hook changed. Re-read it; the hook's assumptions may no longer hold.
   - `CHANGED mirrored`: a method we re-implement changed (e.g. the RNG). Update the model.
   - `CHANGED calls`: informational.
3. **See what changed in general** (new RNG call sites, new systems):
   ```
   dotnet run --project tools/TerrariaTas.GameDiff -- diff old/Terraria.exe new/Terraria.exe
   dotnet run --project tools/TerrariaTas.GameDiff -- rng old/Terraria.exe old-rng.txt
   dotnet run --project tools/TerrariaTas.GameDiff -- rng new/Terraria.exe new-rng.txt
   ```
4. **Retarget and rebuild.** Set `TargetTerrariaVersion` in `build/Version.props`, reset `TasRevision` to 0,
   build. Compile errors list every direct use of a removed or changed member.
5. **Check Re-Logic's snapshot list.** If `Terraria.Testing.StateSnapshot` changed, compare with
   `SaveStateStore` extras: something may now be covered twice, or no longer covered.
6. **Verify determinism.** Run `TerrariaTas.exe <movie> --verify` twice on the reference movies. Both launches
   must report `in sync`.
7. **Record the new surface** and commit it with the port:
   ```
   dotnet run --project tools/TerrariaTas.GameDiff -- surface "<new>/Terraria.exe" compat/terraria-surface.txt src/TerrariaTas.Launcher/bin/Debug/net48/TerrariaTas.Runtime.dll src/TerrariaTas.Launcher/bin/Debug/net48/TerrariaTas.Core.dll
   ```
8. Release `v<new version>-r1`.
