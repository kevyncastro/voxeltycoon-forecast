# Changelog

All notable changes to Voxel Forecast. Versions follow [Semantic Versioning](https://semver.org/):
- **MAJOR**: breaks existing saves or removes features
- **MINOR**: new features
- **PATCH**: bug fixes

Add each change under [Unreleased]. When releasing to the Steam Workshop, move those notes under the new
version number.

## [Unreleased]

## [1.0.0] - 2026-10-17
First release on the Steam Workshop.

### Added
- In-game forecast window built with the game's own UI, with Finance, Demand and Production tabs.
  Open it with F6 or the "Forecast" button in the toolbar's extra tools menu.
- Finance forecast: 12-month projection of recurring net income and cash, with an 80% range, a chart of
  the last 180 days plus the forecast, and a warning when cash may go negative. Uses Holt smoothing with a
  damped trend (3+ months of history), an average (1-2 months), or the current month scaled up (no history).
  Loan interest is calculated exactly. New buildings and vehicle purchases are not included.
- Demand forecast, using the game's own level rules: for each city you deliver to, coverage, satisfaction
  (and where it is heading at the current delivery rate), and whether it levels up, is blocked by city
  population, stays stable, drops a level or closes, with days until it happens. Per-item shortfalls now and
  after the next level-ups, and unserved demand in unlocked regions as opportunities.
- Production forecast: mines and factories with actual vs maximum output and a diagnosis (full speed,
  blocked, starved); days until each mine's deposit runs out; warehouse stock trends with days until empty
  or full; and a per-item balance that says whether you need more producers (and roughly how many), or
  whether transport is the bottleneck.
- Filters on the Demand and Production tabs: by item, by city, and "Problems only".
- Click a store, mine, factory or warehouse card to move the camera there and open the building's window.
- History (daily cash and stock, monthly budget) is recorded inside the savegame. When the mod is added to
  an existing save, it starts from the game's previous budget month.
- Monthly reports in the game log (`Player.log`, lines tagged `[ForecastMod]`).
- Classic window on Shift+F6 (Unity IMGUI), an alternative to the main window.
