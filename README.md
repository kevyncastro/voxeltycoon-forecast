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
| `mod.json` | Pack metadata. The game skips any `Content` folder without one. |

## Where to see output

`%USERPROFILE%\AppData\LocalLow\VoxelTycoon\VoxelTycoon\Player.log`. Search it for `[ForecastMod]`.

## Roadmap

1. Data collection and log reports (current)
2. Finance forecast (Holt smoothing, runway, affordability)
3. Demand forecast (coverage, level-up needs, closing risk)
4. Production chains (utilization, bottlenecks, item balance, storage time-to-empty)
5. In-game window with charts
