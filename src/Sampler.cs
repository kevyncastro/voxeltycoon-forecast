using System;
using System.Globalization;
using System.Text;
using VoxelTycoon;
using VoxelTycoon.Buildings;
using VoxelTycoon.Cities;

namespace ForecastMod
{
    /// <summary>
    /// Watches the in-game calendar and records a snapshot whenever a day or month ends.
    /// Runs from OnLateUpdate, so the game has already rolled its budget over to the new
    /// month (Company.OnUpdate does that) by the time we look.
    /// </summary>
    public class Sampler
    {
        private readonly History _history;
        private readonly Logger _logger;

        private bool _started;
        private DateTime _lastDate;

        public Sampler(History history, Logger logger)
        {
            _history = history;
            _logger = logger;
        }

        /// <summary>Call once the game world is ready.</summary>
        public void Start()
        {
            _lastDate = Now;
            _started = true;

            // A save that never had this mod has no history yet. The game still remembers last
            // month's budget, so use that as a first data point instead of starting empty.
            if (_history.Months.Count == 0 && Company.Current != null)
            {
                SeedFromPreviousMonth();
            }

            _logger.Log("Sampler started on " + FormatDate(_lastDate) + " with " +
                        _history.Months.Count + " months and " + _history.Days.Count + " days of history.");
            LogReports();
        }

        public void Tick()
        {
            if (!_started || Company.Current == null) return;

            DateTime now = Now;
            if (now.Date == _lastDate.Date) return;

            // Game time can jump several days in one frame at high speed; one snapshot is enough.
            bool monthChanged = now.Year != _lastDate.Year || now.Month != _lastDate.Month;
            DateTime endedDay = _lastDate;
            _lastDate = now;

            RecordDay(endedDay);
            if (monthChanged)
            {
                RecordMonth(endedDay);
                LogReports();
            }
        }

        private static DateTime Now => LazyManager<TimeManager>.Current.DateTime;

        private void RecordDay(DateTime day)
        {
            _history.AddDay(new DayRecord
            {
                Year = day.Year,
                Month = day.Month,
                Day = day.Day,
                Money = Company.Current.Money,
            });
        }

        private void RecordMonth(DateTime monthDay)
        {
            Company company = Company.Current;
            var record = new MonthRecord
            {
                Year = monthDay.Year,
                Month = monthDay.Month,
                Money = company.Money,
                Loan = company.Loan,
            };
            // The month that just ended is now the game's "previous" budget period.
            for (int i = 0; i < History.BudgetItemCount; i++)
            {
                record.Budget[i] = company.PreviousBudgetPeriod.Get((BudgetItem)i);
            }
            _history.AddMonth(record);
        }

        private void SeedFromPreviousMonth()
        {
            Company company = Company.Current;
            DateTime prev = _lastDate.AddMonths(-1);

            double currentNet = 0;
            for (int i = 0; i < History.BudgetItemCount; i++)
            {
                currentNet += company.CurrentBudgetPeriod.Get((BudgetItem)i);
            }

            var record = new MonthRecord
            {
                Year = prev.Year,
                Month = prev.Month,
                // Cash at the end of last month = cash now minus what has happened so far this month.
                Money = company.Money - currentNet,
                Loan = company.Loan,
            };
            for (int i = 0; i < History.BudgetItemCount; i++)
            {
                record.Budget[i] = company.PreviousBudgetPeriod.Get((BudgetItem)i);
            }
            _history.AddMonth(record);
        }

        // ---------- Reports written to the game log (stage 1: prove we can read the data) ----------

        private void LogReports()
        {
            try { LogFinance(); } catch (Exception e) { _logger.LogException(e); }
            try { LogDemand(); } catch (Exception e) { _logger.LogException(e); }
            try { LogProduction(); } catch (Exception e) { _logger.LogException(e); }
        }

        private void LogFinance()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Finance history (last 6 months) ===");
            int from = Math.Max(0, _history.Months.Count - 6);
            for (int n = from; n < _history.Months.Count; n++)
            {
                MonthRecord m = _history.Months[n];
                sb.Append(m.Year).Append('-').Append(m.Month.ToString("00"))
                  .Append("  cash ").Append(Money(m.Money))
                  .Append("  net ").Append(Money(m.Net));
                for (int i = 0; i < History.BudgetItemCount; i++)
                {
                    if (m.Budget[i] != 0)
                        sb.Append("  ").Append((BudgetItem)i).Append(' ').Append(Money(m.Budget[i]));
                }
                sb.AppendLine();
            }
            _logger.Log(sb.ToString());
        }

        private void LogDemand()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== City demand ===");
            // The game's ImmutableList has no enumerator, so loop by index.
            ImmutableList<City> cities = Manager<CityManager>.Current.Cities;
            for (int c = 0; c < cities.Count; c++)
            {
                City city = cities[c];
                CityDemandCollection demands = city.Demands;
                for (int i = 0; i < demands.Count; i++)
                {
                    CityDemand d = demands[i];
                    int delivered30 = d.DeliveredCounter.Sum();
                    sb.Append(city.Name).Append(" / ").Append(d.Item?.DisplayName?.ToString() ?? "?")
                      .Append("  level ").Append(d.Level)
                      .Append("  demand ").Append(d.Demand).Append("/mo")
                      .Append("  next level ").Append(d.GetDemand(d.Level + 1)).Append("/mo")
                      .Append("  delivered(30d) ").Append(delivered30)
                      .Append("  satisfaction ").Append(d.Satisfaction.ToString("P0", CultureInfo.InvariantCulture))
                      .Append("  closes in ").Append(d.DaysToClosing).Append("d")
                      .AppendLine();
                }
            }
            _logger.Log(sb.ToString());
        }

        private void LogProduction()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Production (your buildings) ===");
            Company company = Company.Current;

            ImmutableList<Device> devices = LazyManager<BuildingManager>.Current.GetAll<Device>();
            for (int n = 0; n < devices.Count; n++)
            {
                Device device = devices[n];
                if (device.Company != company || device.Recipe == null) continue;
                sb.Append("Device ").Append(device.DisplayName?.ToString() ?? "?")
                  .Append(" #").Append(device.Id)
                  .Append("  recipe ").Append(device.Recipe.DisplayName?.ToString() ?? "?")
                  .Append("  duration ").Append(device.Recipe.Duration.ToString("0.##", CultureInfo.InvariantCulture))
                  .Append("  produced(30d) ").Append(device.ItemsProducedCounter.Sum())
                  .Append("  in ").Append(device.GetInputBuffersFill01().ToString("P0", CultureInfo.InvariantCulture))
                  .Append("  out ").Append(device.GetOutputBuffersFill01().ToString("P0", CultureInfo.InvariantCulture))
                  .AppendLine();
            }

            ImmutableList<Mine> mines = LazyManager<BuildingManager>.Current.GetAll<Mine>();
            for (int n = 0; n < mines.Count; n++)
            {
                Mine mine = mines[n];
                if (mine.Company != company) continue;
                sb.Append("Mine ").Append(mine.DisplayName?.ToString() ?? "?")
                  .Append(" #").Append(mine.Id)
                  .Append("  mined(30d) ").Append(mine.ResourcesMinedCounter.Sum())
                  .AppendLine();
            }
            _logger.Log(sb.ToString());
        }

        private static string Money(double v) => v.ToString("#,0", CultureInfo.InvariantCulture);

        private static string FormatDate(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
