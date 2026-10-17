# Changelog

All notable changes to Voxel Forecast. Versions follow [Semantic Versioning](https://semver.org/):
- **MAJOR**: breaks existing saves or removes features
- **MINOR**: new features
- **PATCH**: bug fixes

The version stays at 0.1.0 during initial development. All changes are listed under
[Unreleased] until the first Steam Workshop release, which gets the first real version number.

## [Unreleased]
### Added
- Data collection: daily cash snapshots and monthly budget per category, stored in the savegame.
- History is seeded from the game's previous budget month when the mod is added to an existing save.
- Monthly log reports for finance, city demand and production (mines and factories).
- Mod version shown in the game log on load.
- Finance forecast: 12-month projection of recurring net income and cash, with an 80% range and
  warnings when cash may go negative. Uses Holt smoothing with a damped trend (3+ months of history),
  an average (1-2 months), or the current month scaled up (no history). Loan interest is calculated exactly.
- Demand forecast, using the game's own level rules: for each city you deliver to, coverage, satisfaction
  (and where it is heading at the current delivery rate), and whether it levels up, is blocked by city
  population, stays stable, drops a level or closes, with days until it happens. Per-item totals with
  shortfalls and next-level needs, and unserved demand per item as opportunities.
- Production forecast: mines and factories with actual vs maximum output, utilization and a diagnosis
  (full speed, blocked, starved, idle); days until each mine's deposit runs out; warehouse stock trends
  with days until empty or full; and a per-item balance of production vs factory and city needs that says
  whether you need more producers (and roughly how many), or whether transport is the bottleneck.
- In-game forecast window built with the game's own UI (the same tabbed window style as the City window),
  with Finance, Demand and Production tabs. Open it with F6 or the "Forecast" button in the toolbar's
  extra tools menu. The Finance tab has a cash chart: 180 days of history, the 12-month forecast and its
  80% range.
- Filters on the Demand and Production tabs: by item, by city (Demand), and "Problems only"
  (shortfalls, blocked or starved buildings, demands not growing, warehouses near empty or full).
- Click a store, mine, factory or warehouse card to move the camera there and open the building's own window.
- Classic IMGUI version of the window on Shift+F6, kept for comparison until the native window is proven.
  Clicks and scrolling over it don't reach the map behind it.
- Daily stock levels of your warehouses and mines are recorded in the savegame (save data version 2;
  saves from version 1 still load).

### Changed
- The demand log no longer lists every city demand with the game's "closes in ~250 days" number,
  which shows for healthy demands too. A demand only closes at level 1 with satisfaction under 20%.
- The production log is replaced by the production forecast report.
