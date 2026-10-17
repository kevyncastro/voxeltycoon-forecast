using System;
using System.Collections.Generic;
using VoxelTycoon;
using VoxelTycoon.Serialization;

namespace ForecastMod
{
    /// <summary>One finished in-game month of company finances.</summary>
    public class MonthRecord
    {
        public int Year;
        public int Month;

        /// <summary>Cash at the moment the month ended.</summary>
        public double Money;
        public double Loan;

        /// <summary>Income (+) or expense (-) per budget category, indexed by (int)BudgetItem.</summary>
        public double[] Budget = new double[History.BudgetItemCount];

        public double Net
        {
            get
            {
                double sum = 0;
                foreach (double v in Budget) sum += v;
                return sum;
            }
        }
    }

    /// <summary>One in-game day snapshot, used for short-term trends.</summary>
    public class DayRecord
    {
        public int Year;
        public int Month;
        public int Day;
        public double Money;
    }

    /// <summary>
    /// Everything the mod remembers. The game itself only keeps the current and previous
    /// month of budget data, so we keep our own history and store it inside the savegame.
    /// </summary>
    public class History
    {
        // Bump this whenever the save format below changes, and handle the old version in Read().
        private const int SaveVersion = 1;

        public const int MaxMonths = 240; // 20 in-game years
        public const int MaxDays = 400;

        public static readonly int BudgetItemCount = Enum.GetValues(typeof(BudgetItem)).Length;

        public readonly List<MonthRecord> Months = new List<MonthRecord>();
        public readonly List<DayRecord> Days = new List<DayRecord>();

        public void AddMonth(MonthRecord record)
        {
            Months.Add(record);
            if (Months.Count > MaxMonths) Months.RemoveAt(0);
        }

        public void AddDay(DayRecord record)
        {
            Days.Add(record);
            if (Days.Count > MaxDays) Days.RemoveAt(0);
        }

        public void Clear()
        {
            Months.Clear();
            Days.Clear();
        }

        public void Write(StateBinaryWriter writer)
        {
            writer.WriteInt(SaveVersion);

            writer.WriteInt(BudgetItemCount);
            writer.WriteInt(Months.Count);
            foreach (MonthRecord m in Months)
            {
                writer.WriteInt(m.Year);
                writer.WriteInt(m.Month);
                writer.WriteDouble(m.Money);
                writer.WriteDouble(m.Loan);
                for (int i = 0; i < BudgetItemCount; i++) writer.WriteDouble(m.Budget[i]);
            }

            writer.WriteInt(Days.Count);
            foreach (DayRecord d in Days)
            {
                writer.WriteInt(d.Year);
                writer.WriteInt(d.Month);
                writer.WriteInt(d.Day);
                writer.WriteDouble(d.Money);
            }
        }

        public void Read(StateBinaryReader reader)
        {
            Clear();
            int version = reader.ReadInt();
            if (version > SaveVersion)
                throw new InvalidOperationException("Save was made with a newer Tycoon Forecast (data version " + version + ").");

            // Stored separately so a game update that adds budget categories doesn't break old saves.
            int savedBudgetCount = reader.ReadInt();
            int monthCount = reader.ReadInt();
            for (int n = 0; n < monthCount; n++)
            {
                var m = new MonthRecord
                {
                    Year = reader.ReadInt(),
                    Month = reader.ReadInt(),
                    Money = reader.ReadDouble(),
                    Loan = reader.ReadDouble(),
                };
                for (int i = 0; i < savedBudgetCount; i++)
                {
                    double v = reader.ReadDouble();
                    if (i < BudgetItemCount) m.Budget[i] = v;
                }
                Months.Add(m);
            }

            int dayCount = reader.ReadInt();
            for (int n = 0; n < dayCount; n++)
            {
                Days.Add(new DayRecord
                {
                    Year = reader.ReadInt(),
                    Month = reader.ReadInt(),
                    Day = reader.ReadInt(),
                    Money = reader.ReadDouble(),
                });
            }
        }
    }
}
