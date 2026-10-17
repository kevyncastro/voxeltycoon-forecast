# Contributing to Voxel Forecast

Thanks for helping! Bug reports, ideas and pull requests are all welcome.

## Reporting bugs and ideas

Open an [issue](https://github.com/kevyncastro/voxeltycoon-forecast/issues/new/choose) using the bug or idea template.
For bugs, the `[ForecastMod]` lines from your game log help a lot:
`%USERPROFILE%\AppData\LocalLow\VoxelTycoon\VoxelTycoon\Player.log`

Found a security problem? Please don't open a public issue; see [SECURITY.md](SECURITY.md).

## Setting up

You need your own copy of **Voxel Tycoon** (Steam) and the **.NET 8 SDK** (or Visual Studio 2022).

The project compiles against the game's own DLLs in `VoxelTycoon_Data\Managed`, so it can only be built on a
machine with the game installed. That's also why there is no automated build on GitHub.

```
dotnet build -c Release
```

If the game isn't in `D:\Steam\steamapps\common\VoxelTycoon`, pass its folder:

```
dotnet build -c Release -p:VTGameDir="X:\SteamLibrary\steamapps\common\VoxelTycoon"
```

The build copies the mod into the game's `Content\ForecastMod` folder. Close the game before building, start it,
and enable the mod for a save (Load game → ⋯ → Add or remove mods).

## Never commit game files

Voxel Tycoon's code and assets belong to its developers. Do **not** commit:

- the game's DLLs (`VoxelTycoon.dll`, `UnityEngine*.dll`, `0Harmony.dll`, ...)
- decompiled game source, or snippets copied from it
- game assets (textures, models, localization files)

To learn the game's API, decompile `VoxelTycoon.dll` locally (for example with ILSpy) into a folder **outside**
this repository, and write your own code based on what you learn.

## Making changes

1. Fork the repository and create a branch from `main`.
2. Keep each pull request to one change, and match the style of the surrounding code.
3. Test it in-game, and say in the pull request what you tested.
4. Add a line under `[Unreleased]` in [CHANGELOG.md](CHANGELOG.md).
5. Don't change `<Version>` in `ForecastMod.csproj`; it's set at release time.

### Save data

The mod stores its history inside savegames (`src/History.cs`). If you change what is written:

- bump `SaveVersion`, and
- keep `Read()` able to load every older version, so players' existing saves still work.

## Pull requests

All changes to `main` go through a pull request and need a review from the maintainer before they're merged.
Pull requests are squash-merged, so your branch can have as many commits as you like.

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
