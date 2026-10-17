# Tycoon Forecast (Voxel Tycoon mod)

Records your company's finances, city demand and production chains, and (in later stages) forecasts where they are heading.

## Build

```
dotnet build -c Release
```

The build copies `ForecastMod.dll` and `mod.json` into `<game>\Content\ForecastMod\`.
If the game is not in `D:\Steam\steamapps\common\VoxelTycoon`, pass the path:

```
dotnet build -c Release -p:VTGameDir="X:\SteamLibrary\steamapps\common\VoxelTycoon"
```

The project compiles against the game's own DLLs in `VoxelTycoon_Data\Managed`, so no .NET Framework developer pack is needed. Close the game before building, since it locks the loaded DLL.

## Files

| File | What it does |
|---|---|
| `src/ForecastMod.cs` | Entry point. The game finds it because it extends `Mod`, then calls its hooks (game started, every frame, save, load). |
| `src/Sampler.cs` | Watches the in-game calendar. Records cash every day and the full budget every month, and logs demand and production reports. |
| `src/History.cs` | The recorded data, and how it is written into and read from the savegame. |
| `src/Forecast/Holt.cs` | Trend smoothing (Holt's method, damped) used by the forecasts. |
| `src/Forecast/FinanceForecast.cs` | 12-month cash projection, 80% range and negative-cash warnings. |
| `mod.json` | Pack metadata. The game skips any `Content` folder without one. |

## Where to see output

`%USERPROFILE%\AppData\LocalLow\VoxelTycoon\VoxelTycoon\Player.log`. Search it for `[ForecastMod]`.

## Versioning

There are two separate version numbers:

| What | Where | When to change it |
|---|---|---|
| **Mod version** (`0.1.0`) | `<Version>` in `ForecastMod.csproj` | Only for Steam Workshop releases. Fixed at `0.1.0` during initial development. Shown in `Player.log` as `Tycoon Forecast 0.1.0 loaded.` |
| **Save data version** (`1`) | `SaveVersion` in `src/History.cs` | Only when the data written into savegames changes. `Read()` must still handle every older version. |

During development, add each change under `[Unreleased]` in `CHANGELOG.md`; no version bumps or tags.

To release on Steam:
1. Set `<Version>` and move the `[Unreleased]` notes in `CHANGELOG.md` under that version.
2. Commit, then tag: `git tag v1.0.0` and `git push origin main --tags`.

## Roadmap

1. Data collection and log reports (done)
2. Finance forecast (Holt smoothing, runway) (current, logged only)
3. Demand forecast (coverage, level-up needs, closing risk)
4. Production chains (utilization, bottlenecks, item balance, storage time-to-empty)
5. In-game window with charts
