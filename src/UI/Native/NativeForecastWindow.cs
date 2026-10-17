using System;
using System.Globalization;
using ForecastMod.Forecast;
using UnityEngine;
using UnityEngine.UI;
using VoxelTycoon;
using VoxelTycoon.UI;
using VoxelTycoon.UI.Controls;
using VoxelTycoon.UI.Windows;

namespace ForecastMod.UI.Native
{
    /// <summary>
    /// The forecast window built from the game's own UI: a TabbedRichWindow (the same base as the
    /// City window) with Finance / Demand / Production tabs, each a game scroll view.
    /// Contents are rebuilt once per in-game day while the window is open.
    /// </summary>
    public class NativeForecastWindow : TabbedRichWindow
    {
        /// <summary>Set by the mod when a game starts.</summary>
        public static History History;

        private const float ContentWidth = 640f;
        private const int ChartW = 616;
        private const int ChartH = 190;
        private const int HistoryDays = 180;

        private Transform _finance, _demand, _production;
        private DateTime _refreshedOn;
        private Texture2D _chartTexture;
        private ChartTexture _chart;

        public static bool IsOpen => UIManager.Current?.FindWindow<NativeForecastWindow>() != null;

        public static void Toggle()
        {
            NativeForecastWindow window = UIManager.Current.FindWindow<NativeForecastWindow>();
            if (window != null)
            {
                window.Close();
                return;
            }
            window = UIManager.Current.CreateWindow<NativeForecastWindow>();
            window.Show();
        }

        // In the game this is "protected internal"; from another assembly it is overridden as "protected".
        protected override void InitializeFrame()
        {
            base.InitializeFrame();
            Title = "Tycoon Forecast";
            Width = ContentWidth;

            _finance = AddScrollTab("Finance", "");    // chart-line
            _demand = AddScrollTab("Demand", "");      // store
            _production = AddScrollTab("Production", ""); // industry
            Refresh();
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            if (LazyManager<TimeManager>.Current.DateTime.Date != _refreshedOn) Refresh();
        }

        protected override void OnClose()
        {
            base.OnClose();
            if (_chartTexture != null) Destroy(_chartTexture);
        }

        private Transform AddScrollTab(string name, string faSolidIcon)
        {
            ScrollRect scroll = Instantiate(R.UI.WindowScrollView);
            HorizontalOrVerticalLayoutGroup layout = scroll.content.GetComponent<HorizontalOrVerticalLayoutGroup>();
            if (layout != null)
            {
                layout.padding = new RectOffset(16, 16, 14, 18);
                layout.spacing = 6f;
                layout.childControlWidth = true;
                layout.childForceExpandWidth = true;
            }

            Panel panel = scroll.gameObject.AddComponent<Panel>();
            panel.CornersType = PanelCornerType.Round10;
            panel.EnabledCorners = PanelCorners.Bottom;
            UIColors.Solid.ContentBackground.Apply(panel);

            ScrollRectAutoResizer resizer = scroll.GetComponent<ScrollRectAutoResizer>();
            if (resizer != null) resizer.MaxHeight = 640;

            AddTab(FontIcon.FaSolid(faSolidIcon), name, scroll.transform, UIColors.Solid.ContentBackground);
            return scroll.content;
        }

        private void Refresh()
        {
            _refreshedOn = LazyManager<TimeManager>.Current.DateTime.Date;
            Company company = Company.Current;
            if (company == null || History == null) return;

            try
            {
                FinanceForecastResult finance = FinanceForecast.Compute(History, company, LazyManager<TimeManager>.Current.DateTime);
                DemandForecastResult demand = DemandForecast.Compute();
                ProductionForecastResult production = ProductionForecast.Compute(History, company, demand);

                Ui.Clear(_finance);
                BuildFinance(finance);
                Ui.Clear(_demand);
                BuildDemand(demand);
                Ui.Clear(_production);
                BuildProduction(production);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Ui.Clear(_finance);
                Ui.Label(_finance, Ui.Paint("Could not build the forecast: " + e.Message, Ui.BadColor));
            }
        }

        // ---------------- Finance ----------------

        private void BuildFinance(FinanceForecastResult f)
        {
            if (f.Months.Count == 0) { Ui.Label(_finance, "No data yet."); return; }

            Transform summary = Ui.Card(_finance);
            Stat(summary, "Cash now", Money(f.CashNow), 170);
            Stat(summary, "Recurring net income", SignedMoney(f.Months[0].Net) + " / month", 200);
            Stat(summary, "Based on", f.MonthsOfHistory + " month(s), " + f.Method, -1);

            if (f.FirstNegative is int neg)
                Ui.Label(_finance, Ui.Bold("Cash goes negative around " + MonthName(f.Months[neg]) + "."), 14, color: Ui.BadColor);
            else if (f.FirstNegativeRisk is int risk)
                Ui.Label(_finance, Ui.Bold("Cash could go negative from " + MonthName(f.Months[risk]) + " if income comes in at the low end."), 14, color: Ui.WarnColor);
            else
                Ui.Label(_finance, Ui.Bold("Cash stays positive for the next " + f.Months.Count + " months."), 14, color: Ui.GoodColor);

            Ui.Heading(_finance, "Cash: last " + HistoryDays + " days and next 12 months");
            Transform chartCard = Ui.Card(_finance, LayoutHelper.Orientation.Vertical);
            BuildChart(f);
            Ui.Image(chartCard, _chartTexture, ChartW, ChartH);
            Transform axis = Ui.Row(chartCard, 0f);
            Ui.Label(axis, "−" + HistoryDays + " days", 11, color: Ui.SubtleColor);
            Ui.Label(axis, "today", 11, color: Ui.SubtleColor, align: TextAnchor.UpperCenter);
            Ui.Label(axis, "+12 months", 11, color: Ui.SubtleColor, align: TextAnchor.UpperRight);
            Ui.Label(chartCard,
                Ui.Paint("━ history", Ui.TextColor) + "    " + Ui.Paint("┅ forecast", ForecastColor) +
                "    " + Ui.Paint("▒ 80% range", ForecastColor) +
                "    scale " + Short(_chart.MinY) + " – " + Short(_chart.MaxY) +
                "    New buildings and vehicles are not included.", 11, color: Ui.SubtleColor);

            Ui.Heading(_finance, "Month by month");
            Transform table = Ui.Card(_finance, LayoutHelper.Orientation.Vertical);
            TableRow(table, true, "Month", "Net / month", "Cash at month end", "80% range");
            foreach (FinanceForecastMonth m in f.Months)
            {
                string cash = m.Cash < 0 ? Ui.Paint(Money(m.Cash), Ui.BadColor) : Money(m.Cash);
                TableRow(table, false, MonthName(m), SignedMoney(m.Net), cash, Short(m.CashLow) + " – " + Short(m.CashHigh));
            }
        }

        private static void Stat(Transform parent, string title, string value, float width)
        {
            Transform col = Ui.Column(parent, 0f);
            if (width > 0) col.gameObject.AddComponent<LayoutElement>().preferredWidth = width;
            else col.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            Ui.Label(col, title.ToUpper(), 10, bold: true, color: Ui.SubtleColor);
            Ui.Label(col, value, width > 0 ? 16 : 12, bold: width > 0);
        }

        private static void TableRow(Transform table, bool header, string a, string b, string c, string d)
        {
            Transform row = Ui.Row(table, 8f);
            int size = header ? 10 : 12;
            Color color = header ? Ui.SubtleColor : Ui.TextColor;
            Ui.Label(row, header ? a.ToUpper() : a, size, header, color, 90);
            Ui.Label(row, header ? b.ToUpper() : b, size, header, color, 130, TextAnchor.UpperRight);
            Ui.Label(row, header ? c.ToUpper() : c, size, header, color, 170, TextAnchor.UpperRight);
            Ui.Label(row, header ? d.ToUpper() : d, size, header, color, -1, TextAnchor.UpperRight);
        }

        private static readonly Color HistoryColor = Ui.TextColor;
        private static readonly Color ForecastColor = ColorHelper.FromHexString("2f80ed");

        private void BuildChart(FinanceForecastResult f)
        {
            DateTime today = LazyManager<TimeManager>.Current.DateTime.Date;
            var chart = new ChartTexture
            {
                TodayX = 0,
                Background = ColorHelper.FromHexString("f7f7f7"),
                Grid = new Color(0f, 0f, 0f, 0.07f),
                Zero = new Color(0.78f, 0.14f, 0.14f, 0.6f),
                Today = new Color(0f, 0f, 0f, 0.3f),
            };

            var history = new ChartSeries { Color = HistoryColor };
            foreach (DayRecord d in History.Days)
            {
                float x = (float)(new DateTime(d.Year, d.Month, d.Day) - today).TotalDays;
                if (x >= -HistoryDays) history.Points.Add(new Vector2(x, (float)d.Money));
            }
            var start = new Vector2(0, (float)f.CashNow);
            history.Points.Add(start);

            var forecast = new ChartSeries { Color = ForecastColor, Dashed = true };
            var band = new ChartBand { Color = new Color(ForecastColor.r, ForecastColor.g, ForecastColor.b, 0.18f) };
            forecast.Points.Add(start);
            band.Low.Add(start);
            band.High.Add(start);
            foreach (FinanceForecastMonth m in f.Months)
            {
                var monthEnd = new DateTime(m.Year, m.Month, DateTime.DaysInMonth(m.Year, m.Month));
                float x = (float)(monthEnd - today).TotalDays;
                forecast.Points.Add(new Vector2(x, (float)m.Cash));
                band.Low.Add(new Vector2(x, (float)m.CashLow));
                band.High.Add(new Vector2(x, (float)m.CashHigh));
            }

            chart.Bands.Add(band);
            chart.Series.Add(history);
            chart.Series.Add(forecast);
            chart.FitBounds();
            chart.MinX = -HistoryDays;
            _chart = chart;
            _chartTexture = chart.Render(ChartW, ChartH, _chartTexture);
        }

        // ---------------- Demand ----------------

        private void BuildDemand(DemandForecastResult f)
        {
            Ui.Heading(_demand, "Cities you deliver to");
            if (f.Served.Count == 0) Ui.Label(_demand, "None yet.", color: Ui.SubtleColor);
            foreach (DemandForecastEntry e in f.Served)
            {
                Transform card = Ui.Card(_demand);

                Transform name = Ui.Column(card, 0f);
                name.gameObject.AddComponent<LayoutElement>().preferredWidth = 150;
                Ui.Label(name, e.City, 13, bold: true);
                Ui.Label(name, e.Item + " · level " + e.Level, 11, color: Ui.SubtleColor);

                Transform supply = Ui.Column(card, 3f);
                supply.gameObject.AddComponent<LayoutElement>().preferredWidth = 120;
                Ui.Label(supply, e.DeliveredLast30Days + " / " + e.DemandPerMonth + " per month", 11);
                Ui.Bar(supply, e.Coverage, 110);

                string sat = "Satisfaction " + Ui.Bold(Percent(e.Satisfaction));
                if (Math.Abs(e.SteadySatisfaction - e.Satisfaction) >= 0.1f) sat += "\nheading to " + Percent(e.SteadySatisfaction);
                Ui.Label(card, sat, 11, width: 110);

                Ui.Label(card, Sampler.Describe(e), 11, color: OutlookColor(e.Outlook));
            }

            Ui.Heading(_demand, "Per item");
            Transform items = Ui.Card(_demand, LayoutHelper.Orientation.Vertical);
            if (f.ServedByItem.Count == 0) Ui.Label(items, "Nothing delivered yet.", 12, color: Ui.SubtleColor);
            foreach (ItemDemandSummary s in f.ServedByItem)
            {
                int shortfall = s.DemandPerMonth - s.DeliveredLast30Days;
                string text = Ui.Bold(s.Item) + ": delivering " + s.DeliveredLast30Days + "/mo, wanted " + s.DemandPerMonth +
                              "/mo now, " + s.NextLevelDemandPerMonth + "/mo after next level-ups";
                if (shortfall > 0) text += "   " + Ui.Paint("short by " + shortfall + "/mo", Ui.WarnColor);
                Ui.Label(items, text, 12);
            }

            Ui.Heading(_demand, "Unserved demand (opportunities)");
            Transform opp = Ui.Card(_demand, LayoutHelper.Orientation.Vertical);
            if (f.UnservedByItem.Count == 0) Ui.Label(opp, "None in your unlocked regions.", 12, color: Ui.SubtleColor);
            foreach (ItemDemandSummary s in f.UnservedByItem)
                Ui.Label(opp, Ui.Bold(s.Item) + ": " + s.DemandPerMonth + "/mo across " + s.Cities + (s.Cities == 1 ? " city" : " cities"), 12);
        }

        // ---------------- Production ----------------

        private void BuildProduction(ProductionForecastResult f)
        {
            Ui.Heading(_production, "Item balance (per month)");
            if (f.Items.Count == 0) Ui.Label(_production, "Nothing produced yet.", color: Ui.SubtleColor);
            foreach (ItemBalance b in f.Items)
            {
                Transform card = Ui.Card(_production, LayoutHelper.Orientation.Vertical);
                string text = Ui.Bold(b.Item) + "   made " + b.ProducedPerMonth + " (max " + b.MaxProductionPerMonth + ")";
                if (b.FactoryNeedPerMonth > 0) text += " · factories use " + b.UsedByFactoriesPerMonth + " (need " + b.FactoryNeedPerMonth + ")";
                if (b.CityWantNowPerMonth > 0) text += " · cities get " + b.DeliveredToCitiesPerMonth + " (want " + b.CityWantNowPerMonth + ")";
                Ui.Label(card, text, 12);
                Color color = b.Verdict.StartsWith("SHORT") ? Ui.BadColor : b.Verdict.StartsWith("balanced") ? Ui.GoodColor : Ui.WarnColor;
                Ui.Label(card, b.Verdict, 11, color: color);
            }

            Ui.Heading(_production, "Mines");
            if (f.Mines.Count == 0) Ui.Label(_production, "None.", color: Ui.SubtleColor);
            foreach (MineReport m in f.Mines)
            {
                string extra = m.DaysUntilDepleted is int days ? "deposit lasts " + Sampler.Duration(days) : null;
                BuildingCard(m.Name, m.Item, m.MinedPerMonth, m.MaxPerMonth, m.Utilization, m.Diagnosis, extra);
            }

            Ui.Heading(_production, "Factories");
            if (f.Devices.Count == 0) Ui.Label(_production, "None.", color: Ui.SubtleColor);
            foreach (DeviceReport d in f.Devices)
            {
                string extra = "inputs " + Percent(d.InputFill) + " full, outputs " + Percent(d.OutputFill) + " full";
                BuildingCard(d.Name, d.Recipe, d.ProducedPerMonth, d.MaxPerMonth, d.Utilization, d.Diagnosis, extra);
            }

            Ui.Heading(_production, "Warehouses");
            if (f.Storages.Count == 0) Ui.Label(_production, "None.", color: Ui.SubtleColor);
            foreach (StorageReport s in f.Storages)
            {
                Transform card = Ui.Card(_production);
                Transform name = Ui.Column(card, 0f);
                name.gameObject.AddComponent<LayoutElement>().preferredWidth = 170;
                Ui.Label(name, s.Name, 12, bold: true);
                Ui.Label(name, s.Item, 11, color: Ui.SubtleColor);

                Transform fill = Ui.Column(card, 3f);
                fill.gameObject.AddComponent<LayoutElement>().preferredWidth = 120;
                float fraction = s.Capacity > 0 ? (float)s.Count / s.Capacity : 0f;
                Ui.Label(fill, s.Count + " / " + s.Capacity, 11);
                Ui.Bar(fill, fraction, 110, ForecastColor);

                string trend;
                Color color = Ui.TextColor;
                if (s.ChangePerDay is double change)
                {
                    trend = (change >= 0 ? "+" : "") + change.ToString("0.#", CultureInfo.InvariantCulture) + " per day";
                    if (s.DaysUntilEmpty is int empty) { trend += ", empty in ~" + empty + " days"; color = empty < 30 ? Ui.BadColor : Ui.WarnColor; }
                    else if (s.DaysUntilFull is int full) { trend += ", full in ~" + full + " days"; color = full < 30 ? Ui.BadColor : Ui.WarnColor; }
                    else trend += ", steady";
                }
                else
                {
                    trend = "trend after 3 days of samples";
                    color = Ui.SubtleColor;
                }
                Ui.Label(card, trend, 11, color: color);
            }
        }

        private void BuildingCard(string title, string subtitle, int actual, int max, float utilization, string diagnosis, string extra)
        {
            Transform card = Ui.Card(_production);

            Transform name = Ui.Column(card, 0f);
            name.gameObject.AddComponent<LayoutElement>().preferredWidth = 170;
            Ui.Label(name, title, 12, bold: true);
            Ui.Label(name, subtitle, 11, color: Ui.SubtleColor);

            Transform rate = Ui.Column(card, 3f);
            rate.gameObject.AddComponent<LayoutElement>().preferredWidth = 120;
            Ui.Label(rate, actual + " of " + max + "/mo (" + Percent(utilization) + ")", 11);
            Ui.Bar(rate, utilization, 110);

            Transform info = Ui.Column(card, 2f);
            info.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            Ui.Label(info, diagnosis, 11, color: DiagnosisColor(diagnosis));
            if (extra != null) Ui.Label(info, extra, 11, color: Ui.SubtleColor);
        }

        // ---------------- Helpers ----------------

        private static Color OutlookColor(DemandOutlook o)
        {
            switch (o)
            {
                case DemandOutlook.LevelUp: return Ui.GoodColor;
                case DemandOutlook.LevelDown:
                case DemandOutlook.Closing: return Ui.BadColor;
                default: return Ui.WarnColor;
            }
        }

        private static Color DiagnosisColor(string d)
        {
            if (d.StartsWith("BLOCKED") || d.StartsWith("STARVED") || d.StartsWith("deposit")) return Ui.BadColor;
            return d.StartsWith("running") ? Ui.GoodColor : Ui.WarnColor;
        }

        private static string MonthName(FinanceForecastMonth m) => m.Year + "-" + m.Month.ToString("00");
        private static string Money(double v) => "$" + v.ToString("#,0", CultureInfo.InvariantCulture);
        private static string SignedMoney(double v) => (v >= 0 ? "+" : "−") + Money(Math.Abs(v));
        private static string Percent(float v) => v.ToString("P0", CultureInfo.InvariantCulture);

        private static string Short(double v)
        {
            double a = Math.Abs(v);
            if (a >= 1e6) return (v / 1e6).ToString("0.00", CultureInfo.InvariantCulture) + "M";
            if (a >= 1e3) return (v / 1e3).ToString("0", CultureInfo.InvariantCulture) + "k";
            return v.ToString("0", CultureInfo.InvariantCulture);
        }
    }
}
