using System;
using System.Collections.Generic;

namespace ForecastMod.Forecast
{
    /// <summary>
    /// Holt's linear smoothing with a damped trend.
    ///
    /// Tracks two numbers while walking through the history:
    ///   Level = "what a normal month looks like right now"
    ///   Trend = "how much it changes per month"
    /// The trend is damped (multiplied by Phi each month ahead) so a few good months
    /// don't turn into runaway growth over a 12-month forecast.
    /// </summary>
    public class Holt
    {
        public double Alpha = 0.5; // how fast the level reacts to new months
        public double Beta = 0.2;  // how fast the trend adapts
        public double Phi = 0.9;   // trend damping per month ahead (1 = no damping)

        public double Level { get; private set; }
        public double Trend { get; private set; }

        /// <summary>Errors of each one-month-ahead prediction made while fitting.</summary>
        public List<double> Errors { get; } = new List<double>();

        public void Fit(IList<double> values)
        {
            Errors.Clear();
            Level = 0;
            Trend = 0;
            if (values.Count == 0) return;

            Level = values[0];
            for (int i = 1; i < values.Count; i++)
            {
                double predicted = Level + Phi * Trend;
                double y = values[i];
                Errors.Add(y - predicted);

                double previousLevel = Level;
                Level = Alpha * y + (1 - Alpha) * predicted;
                Trend = Beta * (Level - previousLevel) + (1 - Beta) * Phi * Trend;
            }
        }

        /// <summary>Predicted value h months after the last fitted month (h = 1 is next month).</summary>
        public double Forecast(int h)
        {
            // Damped trend: Level + (Phi + Phi^2 + ... + Phi^h) * Trend
            double damping = 0;
            double p = 1;
            for (int i = 1; i <= h; i++)
            {
                p *= Phi;
                damping += p;
            }
            return Level + damping * Trend;
        }

        /// <summary>Standard deviation of the fitting errors, or null if there are too few to trust.</summary>
        public double? ErrorStdDev(int minErrors = 2)
        {
            if (Errors.Count < minErrors) return null;
            double sumSq = 0;
            foreach (double e in Errors) sumSq += e * e;
            return Math.Sqrt(sumSq / Errors.Count);
        }
    }
}
