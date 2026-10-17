using System;
using System.Collections.Generic;
using VoxelTycoon;

namespace ForecastMod.Forecast
{
    public class FinanceForecastMonth
    {
        public int Year;
        public int Month;

        /// <summary>Forecast recurring net income for the whole month (includes loan interest).</summary>
        public double Net;

        /// <summary>Projected cash at the end of the month, with an 80% range.</summary>
        public double Cash;
        public double CashLow;
        public double CashHigh;
    }

    public class FinanceForecastResult
    {
        public int MonthsOfHistory;
        public string Method;
        public double CashNow;
        public readonly List<FinanceForecastMonth> Months = new List<FinanceForecastMonth>();

        /// <summary>Index into Months of the first month ending with negative cash, or null.</summary>
        public int? FirstNegative;

        /// <summary>Index into Months of the first month whose low estimate is negative, or null.</summary>
        public int? FirstNegativeRisk;
    }

    /// <summary>
    /// "If you change nothing, where does your cash go?"
    ///
    /// Only recurring money is forecast (cargo/passenger income, running costs, loan interest).
    /// One-off spending (new buildings, vehicles, research, loans) is left out, because those are
    /// your decisions, and one big purchase would otherwise make the whole future look bad.
    /// </summary>
    public static class FinanceForecast
    {
        public const int Horizon = 12;

        // 80% of outcomes fall within ±1.28 standard deviations.
        private const double Z80 = 1.28;

        public static readonly BudgetItem[] Recurring =
        {
            BudgetItem.Trade,
            BudgetItem.PassengerTransportation,
            BudgetItem.VehicleRunningCosts,
            BudgetItem.BuildingRunningCosts,
        };

        public static FinanceForecastResult Compute(History history, Company company, DateTime now)
        {
            int n = history.Months.Count;
            var result = new FinanceForecastResult
            {
                MonthsOfHistory = n,
                CashNow = company.Money,
                Method = n >= 3 ? "trend (Holt)" : n >= 1 ? "average of recorded months" : "current month so far",
            };

            double elapsed = FractionOfMonthElapsed(now);

            // Forecast each recurring category separately; [h-1] = forecast for month h.
            var perMonth = new double[Horizon];
            foreach (BudgetItem item in Recurring)
            {
                double[] f = ForecastCategory(history, company, item, elapsed);
                for (int h = 0; h < Horizon; h++) perMonth[h] += f[h];
            }

            // Loan interest is exact: the game charges Loan × LoanInterest at each month end.
            double interest = -company.Loan * company.LoanInterest;

            double sigma = NetStdDev(history, perMonth[0]);

            double cash = company.Money;
            double variance = 0;
            DateTime month = new DateTime(now.Year, now.Month, 1);
            for (int h = 1; h <= Horizon; h++)
            {
                double net = perMonth[h - 1] + interest;

                // The current month is partly over and its income is already in Money,
                // so only the remaining part of it is still to come.
                double share = h == 1 ? 1 - elapsed : 1;
                cash += perMonth[h - 1] * share + interest;
                variance += share * sigma * sigma;
                double spread = Z80 * Math.Sqrt(variance);

                result.Months.Add(new FinanceForecastMonth
                {
                    Year = month.Year,
                    Month = month.Month,
                    Net = net,
                    Cash = cash,
                    CashLow = cash - spread,
                    CashHigh = cash + spread,
                });

                if (result.FirstNegative == null && cash < 0) result.FirstNegative = h - 1;
                if (result.FirstNegativeRisk == null && cash - spread < 0) result.FirstNegativeRisk = h - 1;
                month = month.AddMonths(1);
            }

            return result;
        }

        private static double[] ForecastCategory(History history, Company company, BudgetItem item, double elapsed)
        {
            var values = new List<double>();
            foreach (MonthRecord m in history.Months) values.Add(m.Budget[(int)item]);

            var f = new double[Horizon];
            if (values.Count >= 3)
            {
                var holt = new Holt();
                holt.Fit(values);
                for (int h = 1; h <= Horizon; h++) f[h - 1] = holt.Forecast(h);
            }
            else if (values.Count > 0)
            {
                double sum = 0;
                foreach (double v in values) sum += v;
                for (int h = 0; h < Horizon; h++) f[h] = sum / values.Count;
            }
            else if (elapsed > 0.1)
            {
                // No history at all: scale this month so far up to a full month.
                double full = company.CurrentBudgetPeriod.Get(item) / elapsed;
                for (int h = 0; h < Horizon; h++) f[h] = full;
            }
            return f;
        }

        /// <summary>
        /// How far off a one-month prediction of total recurring net income usually is.
        /// Falls back to 15% of the forecast until there are enough months to measure it.
        /// </summary>
        private static double NetStdDev(History history, double nextMonthNet)
        {
            var totals = new List<double>();
            foreach (MonthRecord m in history.Months)
            {
                double t = 0;
                foreach (BudgetItem item in Recurring) t += m.Budget[(int)item];
                totals.Add(t);
            }

            var holt = new Holt();
            holt.Fit(totals);
            return holt.ErrorStdDev(minErrors: 3) ?? 0.15 * Math.Abs(nextMonthNet);
        }

        private static double FractionOfMonthElapsed(DateTime now)
        {
            int days = DateTime.DaysInMonth(now.Year, now.Month);
            return Math.Min(1, (now.Day - 1 + now.TimeOfDay.TotalDays) / days);
        }
    }
}
