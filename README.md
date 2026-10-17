# Voxel Forecast (Voxel Tycoon mod)

Records your company's finances, city demand and production chains, and forecasts where they are heading.

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
| `src/Forecast/DemandForecast.cs` | City demand outlook (level up/down, closing) from the game's rules, plus per-item totals. |
| `src/Forecast/ProductionForecast.cs` | Mine/factory utilization and bottlenecks, deposit depletion, warehouse trends, item balance. |
| `src/UI/Native/NativeForecastWindow.cs` | The in-game window (F6 or toolbar button), built on the game's `TabbedRichWindow`. |
| `src/UI/Native/Ui.cs` | Helpers that build text, cards and progress bars in the game's style. |
| `src/UI/Native/ForecastToolbarAction.cs` | The "Forecast" toolbar button. |
| `src/UI/ForecastWindow.cs` | Handles the F6 hotkey, and the classic IMGUI window (Shift+F6), kept until the native window is proven. |
| `src/UI/ChartTexture.cs` | Draws line charts with a shaded range into a texture. |

## Using it in-game

Press **F6**, or use the **Forecast** button in the toolbar's extra tools menu, to open or close the
forecast window. It refreshes once per in-game day while open. **Shift+F6** opens the classic window.

- **Filters** (Demand and Production tabs): pick an item, a city, or "Problems only". The filters are shared
  between the two tabs and stay set while the window is open.
- **Click a card** (a store, mine, factory or warehouse) to move the camera to that building and open its window.
The same reports are also written to `Player.log` when you load a save and at each month end.
| `mod.json` | Pack metadata. The game skips any `Content` folder without one. `Tags` must come from the game's fixed list (e.g. `Utils`, `Gameplay`, `Other`), or Workshop publishing fails its check. |
| `preview.png` | Image for the mod list and Steam Workshop (square, 512x512 or more, 1 MB at most). Edit `art/preview.html`, then run `art/render-preview.ps1`. |

## Where to see output

`%USERPROFILE%\AppData\LocalLow\VoxelTycoon\VoxelTycoon\Player.log`. Search it for `[ForecastMod]`.

## Versioning

There are two separate version numbers:

| What | Where | When to change it |
|---|---|---|
| **Mod version** (`0.1.0`) | `<Version>` in `ForecastMod.csproj` | Only for Steam Workshop releases. Fixed at `0.1.0` during initial development. Shown in `Player.log` as `Voxel Forecast 0.1.0 loaded.` |
| **Save data version** (`2`) | `SaveVersion` in `src/History.cs` | Only when the data written into savegames changes. `Read()` must still handle every older version. |

During development, add each change under `[Unreleased]` in `CHANGELOG.md`; no version bumps or tags.

To release on Steam:
1. Set `<Version>` and move the `[Unreleased]` notes in `CHANGELOG.md` under that version.
2. Commit, then tag: `git tag v1.0.0` and `git push origin main --tags`.

## Roadmap

1. Data collection and log reports (done)
2. Finance forecast (Holt smoothing, runway) (done)
3. Demand forecast (coverage, level-up needs, closing risk) (done)
4. Production chains (utilization, bottlenecks, item balance, storage time-to-empty) (done)
5. In-game window with charts, filters and click-to-locate (done)

## Contributing

Bug reports, ideas and pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for setup and guidelines,
and [SECURITY.md](SECURITY.md) to report a vulnerability privately.

## License

[MIT](LICENSE). Voxel Tycoon and its assets belong to their developers; this repository contains none of them.
