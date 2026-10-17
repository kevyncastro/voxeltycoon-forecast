using System;
using System.Globalization;
using System.Text;
using ForecastMod.Forecast;
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
            try { LogFinanceForecast(); } catch (Exception e) { _logger.LogException(e); }
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

        private void LogFinanceForecast()
        {
            FinanceForecastResult f = FinanceForecast.Compute(_history, Company.Current, Now);

            var sb = new StringBuilder();
            sb.AppendLine("=== Finance forecast (recurring income and costs only; new buildings/vehicles not included) ===");
            sb.Append("Based on ").Append(f.MonthsOfHistory).Append(" month(s), method: ").Append(f.Method)
              .Append(". Cash now ").Append(Money(f.CashNow)).AppendLine();
            sb.AppendLine("Month    net/month    cash at month end    (80% range)");
            foreach (FinanceForecastMonth m in f.Months)
            {
                sb.Append((m.Year + "-" + m.Month.ToString("00")).PadRight(7))
                  .Append(SignedMoney(m.Net).PadLeft(11))
                  .Append("  ").Append(Money(m.Cash).PadLeft(19))
                  .Append("    (").Append(Money(m.CashLow)).Append(" to ").Append(Money(m.CashHigh)).Append(')')
                  .AppendLine();
            }

            if (f.FirstNegative is int neg)
            {
                FinanceForecastMonth m = f.Months[neg];
                sb.Append("WARNING: cash goes negative around ").Append(m.Year).Append('-').Append(m.Month.ToString("00")).AppendLine(".");
            }
            else if (f.FirstNegativeRisk is int risk)
            {
                FinanceForecastMonth m = f.Months[risk];
                sb.Append("Caution: cash could go negative from ").Append(m.Year).Append('-').Append(m.Month.ToString("00"))
                  .AppendLine(" if income comes in at the low end.");
            }
            else
            {
                sb.Append("Cash stays positive for the next ").Append(f.Months.Count).AppendLine(" months.");
            }
            _logger.Log(sb.ToString());
        }

        private void LogDemand()
        {
            DemandForecastResult f = DemandForecast.Compute();
            var sb = new StringBuilder();

            sb.AppendLine("=== Demand forecast: cities you deliver to ===");
            if (f.Served.Count == 0) sb.AppendLine("(none yet)");
            foreach (DemandForecastEntry e in f.Served)
            {
                sb.Append(e.City).Append(" / ").Append(e.Item)
                  .Append("  L").Append(e.Level)
                  .Append("  delivered ").Append(e.DeliveredLast30Days).Append(" of ").Append(e.DemandPerMonth).Append("/mo")
                  .Append(" (").Append(Percent(e.Coverage)).Append(')')
                  .Append("  satisfaction ").Append(Percent(e.Satisfaction));
                if (Math.Abs(e.SteadySatisfaction - e.Satisfaction) >= 0.1f)
                    sb.Append(" heading to ").Append(Percent(e.SteadySatisfaction));
                sb.Append("  -> ").Append(Describe(e)).AppendLine();
            }

            sb.AppendLine("--- Per item (cities you deliver to) ---");
            foreach (ItemDemandSummary s in f.ServedByItem)
            {
                sb.Append(s.Item).Append(": delivering ").Append(s.DeliveredLast30Days)
                  .Append("/mo, wanted ").Append(s.DemandPerMonth)
                  .Append("/mo now, ").Append(s.NextLevelDemandPerMonth).Append("/mo after next level-ups")
                  .Append(" (").Append(s.Cities).Append(s.Cities == 1 ? " city)" : " cities)");
                int shortfall = s.DemandPerMonth - s.DeliveredLast30Days;
                if (shortfall > 0) sb.Append("  short by ").Append(shortfall).Append("/mo");
                sb.AppendLine();
            }

            sb.AppendLine("--- Unserved demand (opportunities, unlocked regions) ---");
            foreach (ItemDemandSummary s in f.UnservedByItem)
            {
                sb.Append(s.Item).Append(": ").Append(s.DemandPerMonth).Append("/mo across ")
                  .Append(s.Cities).Append(s.Cities == 1 ? " city" : " cities").AppendLine();
            }
            _logger.Log(sb.ToString());
        }

        private static string Describe(DemandForecastEntry e)
        {
            string when = e.DaysUntil is int days ? " in ~" + days + " days" : "";
            switch (e.Outlook)
            {
                case DemandOutlook.LevelUp:
                    return "levels up" + when + " (then wants " + e.NextLevelDemandPerMonth + "/mo, +" +
                           (e.NextLevelDemandPerMonth - e.DemandPerMonth) + ")";
                case DemandOutlook.LevelUpBlocked:
                    return "satisfied enough to grow, but city needs " + e.PopulationNeeded.ToString("#,0", CultureInfo.InvariantCulture) +
                           " people (has " + e.PopulationNow.ToString("#,0", CultureInfo.InvariantCulture) + ")";
                case DemandOutlook.Stable:
                    return "stable (needs 40%+ satisfaction to grow)";
                case DemandOutlook.LevelDown:
                    return "WARNING: drops a level" + when + " unless satisfaction reaches 20%";
                case DemandOutlook.Closing:
                    return "WARNING: store closes" + when + " unless satisfaction reaches 20%";
                default:
                    return "";
            }
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

        private static string SignedMoney(double v) => (v >= 0 ? "+" : "") + Money(v);

        private static string Percent(float v) => v.ToString("P0", CultureInfo.InvariantCulture);

        private static string FormatDate(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
