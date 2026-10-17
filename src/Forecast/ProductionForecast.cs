using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelTycoon;
using VoxelTycoon.Buildings;
using VoxelTycoon.Recipes;

namespace ForecastMod.Forecast
{
    public class MineReport
    {
        public string Name;
        public string Item;
        public int MinedPerMonth;      // actual, last 30 days
        public int MaxPerMonth;        // at full speed: 1 item per OutputInterval
        public float Utilization;
        public string Diagnosis;
        public float? DepositLeft;
        public int? DaysUntilDepleted;
    }

    public class DeviceReport
    {
        public string Name;
        public string Recipe;
        public int ProducedPerMonth;   // actual items output, last 30 days
        public int MaxPerMonth;        // items/month at full speed
        public float Utilization;
        public float InputFill;
        public float OutputFill;
        public string Diagnosis;
    }

    public class StorageReport
    {
        public string Name;
        public string Item;
        public int Count;
        public int Capacity;
        public int Samples;
        public double? ChangePerDay;   // null until there are enough daily samples
        public int? DaysUntilEmpty;
        public int? DaysUntilFull;
    }

    public class ItemBalance
    {
        public string Item;
        public int Producers;
        public int ProducedPerMonth;
        public int MaxProductionPerMonth;
        public int UsedByFactoriesPerMonth;
        public int FactoryNeedPerMonth;     // what your factories would use at full speed
        public int DeliveredToCitiesPerMonth;
        public int CityWantNowPerMonth;
        public int CityWantNextPerMonth;    // after the served demands level up
        public string Verdict;
    }

    public class ProductionForecastResult
    {
        public readonly List<MineReport> Mines = new List<MineReport>();
        public readonly List<DeviceReport> Devices = new List<DeviceReport>();
        public readonly List<StorageReport> Storages = new List<StorageReport>();
        public readonly List<ItemBalance> Items = new List<ItemBalance>();
    }

    /// <summary>
    /// How your production chains are doing and where they are heading.
    ///
    /// Game timing (from Mine/Device code): 900 world seconds = 30 in-game days.
    /// A mine makes 1 item per OutputInterval; a factory runs one recipe cycle per Duration.
    /// </summary>
    public static class ProductionForecast
    {
        private const float WorldSecondsPerMonth = 900f;
        private const int RegressionDays = 14;

        /// <summary>Your built warehouses and mines, which hold stock we sample daily.</summary>
        public static List<StorageNetworkBuilding> TrackedStorages(Company company)
        {
            var result = new List<StorageNetworkBuilding>();
            Collect<Warehouse>(company, result);
            Collect<Mine>(company, result);
            return result;
        }

        public static ProductionForecastResult Compute(History history, Company company, DemandForecastResult demand)
        {
            var result = new ProductionForecastResult();
            var balance = new Dictionary<string, ItemBalance>();
            var buildings = LazyManager<BuildingManager>.Current;

            ImmutableList<Mine> mines = buildings.GetAll<Mine>();
            for (int i = 0; i < mines.Count; i++)
            {
                Mine mine = mines[i];
                if (mine.Company != company || !mine.IsBuilt) continue;
                MineReport r = ReportMine(mine);
                result.Mines.Add(r);

                ItemBalance b = Get(balance, r.Item);
                b.Producers++;
                b.ProducedPerMonth += r.MinedPerMonth;
                b.MaxProductionPerMonth += r.MaxPerMonth;
            }

            ImmutableList<Device> devices = buildings.GetAll<Device>();
            for (int i = 0; i < devices.Count; i++)
            {
                Device device = devices[i];
                if (device.Company != company || !device.IsBuilt || device.Recipe == null) continue;
                result.Devices.Add(ReportDevice(device, balance));
            }

            foreach (StorageNetworkBuilding s in TrackedStorages(company))
            {
                if (s is Warehouse) result.Storages.Add(ReportStorage(s, history));
            }

            foreach (ItemDemandSummary d in demand.ServedByItem)
            {
                ItemBalance b = Get(balance, d.Item);
                b.DeliveredToCitiesPerMonth += d.DeliveredLast30Days;
                b.CityWantNowPerMonth += d.DemandPerMonth;
                b.CityWantNextPerMonth += d.NextLevelDemandPerMonth;
            }

            foreach (ItemBalance b in balance.Values)
            {
                b.Verdict = Verdict(b);
                result.Items.Add(b);
            }
            result.Items.Sort((a, b) => string.CompareOrdinal(a.Item, b.Item));
            return result;
        }

        private static MineReport ReportMine(Mine mine)
        {
            float interval = mine.SharedData.OutputInterval;
            int max = interval > 0 ? Mathf.RoundToInt(WorldSecondsPerMonth / interval) : 0;
            int mined = mine.ResourcesMinedCounter.Sum();

            var r = new MineReport
            {
                Name = Label(mine),
                Item = ItemName(mine.Storage?.Item ?? mine.Deposit?.Item),
                MinedPerMonth = mined,
                MaxPerMonth = max,
                Utilization = max > 0 ? (float)mined / max : 0f,
            };

            if (mine.Deposit != null && mine.SharedData.RequiresDeposit)
            {
                r.DepositLeft = mine.Deposit.Count;
                if (mined > 0) r.DaysUntilDepleted = Mathf.FloorToInt(mine.Deposit.Count / (mined / 30f));
            }

            if (mine.DepositIsEmpty()) r.Diagnosis = "deposit used up";
            else if (!mine.IsEnabled) r.Diagnosis = "disabled";
            else if (mine.Storage != null && mine.Storage.RemainingCapacity == 0)
                r.Diagnosis = "BLOCKED: storage full, add vehicles to collect";
            else if (r.Utilization >= 0.9f) r.Diagnosis = "running at full speed";
            else r.Diagnosis = "below full speed (new, or blocked earlier)";
            return r;
        }

        private static DeviceReport ReportDevice(Device device, Dictionary<string, ItemBalance> balance)
        {
            Recipe recipe = device.Recipe;
            float cyclesPerMonth = recipe.Duration > 0 ? WorldSecondsPerMonth / recipe.Duration : 0f;

            float outputPerCycle = 0;
            foreach (RecipeItem o in recipe.OutputItems) outputPerCycle += o.Count;

            int produced = device.ItemsProducedCounter.Sum();
            int max = Mathf.RoundToInt(cyclesPerMonth * outputPerCycle);
            float actualCycles = outputPerCycle > 0 ? produced / outputPerCycle : 0f;

            // Split actual output between output items by their recipe share.
            foreach (RecipeItem o in recipe.OutputItems)
            {
                ItemBalance b = Get(balance, ItemName(o.Item));
                b.Producers++;
                b.ProducedPerMonth += Mathf.RoundToInt(actualCycles * o.Count);
                b.MaxProductionPerMonth += Mathf.RoundToInt(cyclesPerMonth * o.Count);
            }
            foreach (RecipeItem input in recipe.InputItems)
            {
                ItemBalance b = Get(balance, ItemName(input.Item));
                b.UsedByFactoriesPerMonth += Mathf.RoundToInt(actualCycles * input.Count);
                b.FactoryNeedPerMonth += Mathf.RoundToInt(cyclesPerMonth * input.Count);
            }

            var r = new DeviceReport
            {
                Name = Label(device),
                Recipe = recipe.DisplayName?.ToString() ?? "?",
                ProducedPerMonth = produced,
                MaxPerMonth = max,
                Utilization = max > 0 ? (float)produced / max : 0f,
                InputFill = device.GetInputBuffersFill01(),
                OutputFill = device.GetOutputBuffersFill01(),
            };

            if (!device.IsEnabled) r.Diagnosis = "disabled";
            else if (r.Utilization >= 0.9f) r.Diagnosis = "running at full speed";
            else if (r.OutputFill >= 0.9f) r.Diagnosis = "BLOCKED: output isn't collected";
            else if (r.InputFill < 0.2f) r.Diagnosis = "STARVED: inputs aren't arriving";
            else r.Diagnosis = "below full speed";
            return r;
        }

        private static StorageReport ReportStorage(StorageNetworkBuilding s, History history)
        {
            var r = new StorageReport
            {
                Name = Label(s),
                Item = ItemName(s.Storage.Item),
                Count = s.Storage.Count,
                Capacity = s.Storage.Capacity,
            };

            if (!history.Stock.TryGetValue(s.Id, out List<int> samples)) return r;
            r.Samples = samples.Count;
            if (samples.Count < 3) return r;

            // Straight line through the last two weeks of daily counts: deliveries come in bursts,
            // so this is steadier than comparing two single days.
            int from = Math.Max(0, samples.Count - RegressionDays);
            double slope = Slope(samples, from);
            r.ChangePerDay = slope;

            if (slope < -0.5 && r.Count > 0) r.DaysUntilEmpty = (int)Math.Floor(r.Count / -slope);
            else if (slope > 0.5 && r.Count < r.Capacity) r.DaysUntilFull = (int)Math.Floor((r.Capacity - r.Count) / slope);
            return r;
        }

        private static string Verdict(ItemBalance b)
        {
            int need = b.FactoryNeedPerMonth + b.CityWantNowPerMonth;
            int needNext = b.FactoryNeedPerMonth + b.CityWantNextPerMonth;
            int used = b.UsedByFactoriesPerMonth + b.DeliveredToCitiesPerMonth;

            if (need == 0) return "not used by your factories or the cities you serve";
            if (b.Producers == 0) return "SHORT: you have nothing producing this (need " + need + "/mo)";

            string verdict;
            int perProducer = Math.Max(1, b.MaxProductionPerMonth / b.Producers);
            if (b.MaxProductionPerMonth < need)
            {
                int more = (int)Math.Ceiling((need - b.MaxProductionPerMonth) / (double)perProducer);
                verdict = "SHORT: even at full speed you make " + b.MaxProductionPerMonth + "/mo but need " + need +
                          "/mo, about " + more + " more producer" + (more == 1 ? "" : "s");
            }
            else if (b.ProducedPerMonth < need * 0.9)
            {
                verdict = "producers could cover it but run below full speed; check blocked or idle buildings above";
            }
            else if (used < need * 0.9)
            {
                verdict = "enough is produced, but only " + used + "/mo arrives: transport is the bottleneck";
            }
            else
            {
                verdict = "balanced";
            }

            if (needNext > need && b.MaxProductionPerMonth < needNext && b.MaxProductionPerMonth >= need)
            {
                int more = (int)Math.Ceiling((needNext - b.MaxProductionPerMonth) / (double)perProducer);
                verdict += "; after level-ups you'll need " + needNext + "/mo, about " + more + " more producer" + (more == 1 ? "" : "s");
            }
            return verdict;
        }

        private static double Slope(List<int> ys, int from)
        {
            int n = ys.Count - from;
            double meanX = (n - 1) / 2.0, meanY = 0;
            for (int i = from; i < ys.Count; i++) meanY += ys[i];
            meanY /= n;

            double num = 0, den = 0;
            for (int i = 0; i < n; i++)
            {
                double dx = i - meanX;
                num += dx * (ys[from + i] - meanY);
                den += dx * dx;
            }
            return den > 0 ? num / den : 0;
        }

        private static void Collect<T>(Company company, List<StorageNetworkBuilding> result) where T : StorageNetworkBuilding
        {
            ImmutableList<T> all = LazyManager<BuildingManager>.Current.GetAll<T>();
            for (int i = 0; i < all.Count; i++)
            {
                T b = all[i];
                if (b.Company == company && b.IsBuilt && b.Storage != null) result.Add(b);
            }
        }

        private static ItemBalance Get(Dictionary<string, ItemBalance> map, string item)
        {
            if (!map.TryGetValue(item, out ItemBalance b))
            {
                b = new ItemBalance { Item = item };
                map[item] = b;
            }
            return b;
        }

        private static string ItemName(Item item) => item?.DisplayName?.ToString() ?? "?";

        private static string Label(Building b) => (b.DisplayName?.ToString() ?? "?") + " #" + b.Id;
    }
}
