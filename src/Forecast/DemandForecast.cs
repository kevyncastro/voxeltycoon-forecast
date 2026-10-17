using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelTycoon;
using VoxelTycoon.Cities;

namespace ForecastMod.Forecast
{
    public enum DemandOutlook
    {
        LevelUp,         // satisfaction is good; levels up after enough monthly checks
        LevelUpBlocked,  // satisfaction is good, but the city is too small for the next level
        Stable,          // 20-40% satisfaction: no growth, no decline
        LevelDown,       // under 20% satisfaction at level 2+: drops a level after 4 checks
        Closing,         // under 20% satisfaction at level 1: the store closes after 9 checks
    }

    public class DemandForecastEntry
    {
        public CityDemand Demand;
        public string City;
        public string Item;

        public int Level;
        public int DemandPerMonth;
        public int NextLevelDemandPerMonth;
        public int DeliveredLast30Days;

        /// <summary>Delivered ÷ demanded over the last 30 days (can exceed 1).</summary>
        public float Coverage;

        public float Satisfaction;

        /// <summary>Where satisfaction settles if deliveries stay at the current rate.</summary>
        public float SteadySatisfaction;

        public DemandOutlook Outlook;

        /// <summary>In-game days until the outlook happens (level change or closing), if it applies.</summary>
        public int? DaysUntil;

        /// <summary>For LevelUpBlocked: the city population needed, and what it has.</summary>
        public int PopulationNeeded;
        public int PopulationNow;
    }

    public class ItemDemandSummary
    {
        public string Item;
        public int Cities;
        public int DemandPerMonth;
        public int NextLevelDemandPerMonth;
        public int DeliveredLast30Days;
    }

    public class DemandForecastResult
    {
        /// <summary>Demands you have delivered to at least once.</summary>
        public readonly List<DemandForecastEntry> Served = new List<DemandForecastEntry>();

        /// <summary>Totals per item across served demands.</summary>
        public readonly List<ItemDemandSummary> ServedByItem = new List<ItemDemandSummary>();

        /// <summary>Totals per item across demands you have never delivered to (unlocked regions only).</summary>
        public readonly List<ItemDemandSummary> UnservedByItem = new List<ItemDemandSummary>();
    }

    /// <summary>
    /// Forecasts city demands using the game's own rules (from CityDemand.InvalidateLevel):
    /// - Satisfaction = average over the last 45 days of (consumed ÷ needed) per day.
    /// - Once a month each demand is checked. Grade = satisfaction in 20% steps
    ///   (VeryLow under 20%, Low 20-40%, Normal 40%+ ...).
    /// - Above Low for max(level+1, 3) checks in a row, and the city is big enough → level up.
    /// - Below Low for 4 checks in a row → level down.
    /// - At level 1 and below Low for 9 checks in a row → the store closes.
    /// </summary>
    public static class DemandForecast
    {
        private const float DaysPerMonthCheck = 30f;
        private const float WorldSecondsPerMonthCheck = 900f; // same constant the game uses

        public static DemandForecastResult Compute()
        {
            var result = new DemandForecastResult();
            var served = new Dictionary<string, ItemDemandSummary>();
            var unserved = new Dictionary<string, ItemDemandSummary>();
            float worldTime = LazyManager<TimeManager>.Current.WorldTime;

            ImmutableList<City> cities = Manager<CityManager>.Current.Cities;
            for (int c = 0; c < cities.Count; c++)
            {
                City city = cities[c];
                if (city.Region == null || city.Region.State != RegionState.Unlocked) continue;

                CityDemandCollection demands = city.Demands;
                for (int i = 0; i < demands.Count; i++)
                {
                    CityDemand d = demands[i];
                    string item = d.Item?.DisplayName?.ToString() ?? "?";

                    if (d.DeliveredCounter.Lifetime > 0)
                    {
                        DemandForecastEntry e = Forecast(d, city, item, worldTime);
                        result.Served.Add(e);
                        Add(served, item, d, e.DeliveredLast30Days);
                    }
                    else
                    {
                        Add(unserved, item, d, 0);
                    }
                }
            }

            result.ServedByItem.AddRange(served.Values);
            result.UnservedByItem.AddRange(unserved.Values);
            result.ServedByItem.Sort((a, b) => string.CompareOrdinal(a.Item, b.Item));
            result.UnservedByItem.Sort((a, b) => b.DemandPerMonth.CompareTo(a.DemandPerMonth));
            return result;
        }

        private static DemandForecastEntry Forecast(CityDemand d, City city, string item, float worldTime)
        {
            int delivered = d.DeliveredCounter.Sum();
            float coverage = d.Demand > 0 ? (float)delivered / d.Demand : 1f;

            var e = new DemandForecastEntry
            {
                Demand = d,
                City = city.Name,
                Item = item,
                Level = d.Level,
                DemandPerMonth = d.Demand,
                NextLevelDemandPerMonth = d.GetDemand(d.Level + 1),
                DeliveredLast30Days = delivered,
                Coverage = coverage,
                Satisfaction = d.Satisfaction,
                SteadySatisfaction = Mathf.Clamp01(coverage),
            };

            // Days until this demand's next monthly check (each demand has its own schedule).
            float sinceBirthday = worldTime - d.Birthday;
            float monthFraction = (sinceBirthday / WorldSecondsPerMonthCheck) % 1f;
            float daysToNextCheck = (1f - monthFraction) * DaysPerMonthCheck;

            Grade grade = GradeHelper.ToGrade(d.Satisfaction);
            if (grade > Grade.Low)
            {
                int checksNeeded = Math.Max(d.Level + 1, 3) - d.LevelIncreaseValue;
                e.PopulationNeeded = d.GetPopulation(d.Level + 1);
                e.PopulationNow = city.Population;
                e.Outlook = city.Population >= e.PopulationNeeded ? DemandOutlook.LevelUp : DemandOutlook.LevelUpBlocked;
                e.DaysUntil = DaysUntil(checksNeeded, daysToNextCheck);
            }
            else if (grade == Grade.Low)
            {
                e.Outlook = DemandOutlook.Stable;
            }
            else if (d.Level > 1)
            {
                e.Outlook = DemandOutlook.LevelDown;
                e.DaysUntil = DaysUntil(4 - d.LevelDecreaseValue, daysToNextCheck);
            }
            else if (WorldSettings.Current.DemandsNeverClose)
            {
                e.Outlook = DemandOutlook.Stable;
            }
            else
            {
                e.Outlook = DemandOutlook.Closing;
                e.DaysUntil = d.DaysToClosing; // accurate in this case: the game counts toward 9 checks
            }
            return e;
        }

        private static int DaysUntil(int checksNeeded, float daysToNextCheck)
        {
            checksNeeded = Math.Max(checksNeeded, 1);
            return Mathf.CeilToInt(daysToNextCheck + (checksNeeded - 1) * DaysPerMonthCheck);
        }

        private static void Add(Dictionary<string, ItemDemandSummary> map, string item, CityDemand d, int delivered)
        {
            if (!map.TryGetValue(item, out ItemDemandSummary s))
            {
                s = new ItemDemandSummary { Item = item };
                map[item] = s;
            }
            s.Cities++;
            s.DemandPerMonth += d.Demand;
            s.NextLevelDemandPerMonth += d.GetDemand(d.Level + 1);
            s.DeliveredLast30Days += delivered;
        }
    }
}
