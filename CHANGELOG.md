# Changelog

All notable changes to Tycoon Forecast. Versions follow [Semantic Versioning](https://semver.org/):
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

### Changed
- The demand log no longer lists every city demand with the game's "closes in ~250 days" number,
  which shows for healthy demands too. A demand only closes at level 1 with satisfaction under 20%.
