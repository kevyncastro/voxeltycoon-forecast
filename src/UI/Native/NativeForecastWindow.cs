using System;
using System.Collections.Generic;
using System.Globalization;
using ForecastMod.Forecast;
using UnityEngine;
using UnityEngine.UI;
using VoxelTycoon;
using VoxelTycoon.Buildings;
using VoxelTycoon.Cities;
using VoxelTycoon.Game.UI;
using VoxelTycoon.Recipes;
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
        // Window content width minus the scroll view padding (2 x 16) and the card padding (2 x 12).
        private const int ChartW = (int)ContentWidth - 32 - 24;

        // Shared column widths, so cards on every tab line up.
        private const float NameWidth = 160f;
        private const float RateWidth = 130f;
        private const float SatisfactionWidth = 110f;
        private const int ChartH = 190;
        private const float YAxisWidth = 40f;
        private const float AxisGap = 6f;
        private const int PlotW = ChartW - (int)YAxisWidth - (int)AxisGap;
        private const int HistoryDays = 180;

        private Transform _finance, _demand, _production;
        private DateTime _refreshedOn;
        private Texture2D _chartTexture;
        private ChartTexture _chart;
        private DemandForecastResult _demandResult;
        private ProductionForecastResult _productionResult;

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
            Title = "Voxel Forecast";
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

                // Kept so filter clicks can rebuild the tabs without recomputing.
                _demandResult = demand;
                _productionResult = production;
                RebuildFiltered();
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
            if (f.Months.Count == 0) { Ui.Empty(_finance, "No data yet."); return; }

            Transform summary = Ui.Card(_finance);
            // Top-align so the small titles line up even when a value wraps onto two lines.
            summary.GetComponent<HorizontalOrVerticalLayoutGroup>().childAlignment = TextAnchor.UpperLeft;
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

            // Y axis labels on the left, then the chart; together exactly ChartW wide.
            Transform chartRow = Ui.Row(chartCard, AxisGap, TextAnchor.UpperLeft);
            Transform yAxis = Ui.FixedColumn(chartRow, YAxisWidth);
            for (int i = 0; i < 3; i++)
            {
                float value = Mathf.Lerp(_chart.MaxY, _chart.MinY, i / 2f);
                TextAnchor anchor = i == 0 ? TextAnchor.UpperRight : i == 1 ? TextAnchor.MiddleRight : TextAnchor.LowerRight;
                Text label = Ui.Label(yAxis, Short(value), 10, color: Ui.SubtleColor, align: anchor);
                label.GetComponent<LayoutElement>().minHeight = label.GetComponent<LayoutElement>().preferredHeight = ChartH / 3f;
            }
            Ui.Image(chartRow, _chartTexture, PlotW, ChartH);

            // X axis labels, with "today" centred under the today line.
            float todayPx = PlotW * _chart.NormX(0);
            Transform axis = Ui.Row(chartCard, 0f, TextAnchor.UpperLeft);
            Ui.FixedColumn(axis, YAxisWidth + AxisGap);
            Ui.Label(axis, "−" + HistoryDays + " days", 10, color: Ui.SubtleColor, width: Mathf.Max(1f, todayPx - 30f));
            Ui.Label(axis, "today", 10, color: Ui.SubtleColor, width: 60f, align: TextAnchor.UpperCenter);
            Ui.Label(axis, "+12 months", 10, color: Ui.SubtleColor, align: TextAnchor.UpperRight);

            Ui.Space(chartCard, 2f);
            Ui.Label(chartCard,
                Ui.Paint("━ history", Ui.TextColor) + "    " + Ui.Paint("┅ forecast", ForecastColor) +
                "    " + Ui.Paint("▒ 80% range", ForecastColor) +
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
            Transform col = width > 0 ? Ui.FixedColumn(parent, width, 2f) : Ui.FlexibleColumn(parent, 2f);
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
            _chartTexture = chart.Render(PlotW, ChartH, _chartTexture);
        }

        // ---------------- Filters ----------------

        // Shared by the Demand and Production tabs, kept across the daily refresh.
        private string _itemFilter;   // null = all items
        private string _cityFilter;   // null = all cities (Demand tab only)
        private bool _problemsOnly;

        private const int ChipsPerRow = 6;

        private bool ItemMatches(string item) => _itemFilter == null || item == _itemFilter;

        private void BuildFilters(Transform parent, IEnumerable<string> items, IEnumerable<string> cities)
        {
            var itemList = new SortedSet<string>(items, StringComparer.Ordinal);
            if (_itemFilter != null) itemList.Add(_itemFilter);

            Transform bar = Ui.Column(parent, 6f);
            Transform row = Ui.ChipRow(bar);
            Ui.Chip(row, "All items", _itemFilter == null, () => SetFilter(ref _itemFilter, null));
            int n = 1;
            foreach (string item in itemList)
            {
                if (n++ % ChipsPerRow == 0) row = Ui.ChipRow(bar);
                string value = item;
                Ui.Chip(row, item, _itemFilter == item, () => SetFilter(ref _itemFilter, value));
            }

            row = Ui.ChipRow(bar);
            Ui.Chip(row, "Problems only", _problemsOnly, () =>
            {
                _problemsOnly = !_problemsOnly;
                RebuildFiltered();
            });

            if (cities != null)
            {
                var cityList = new SortedSet<string>(cities, StringComparer.Ordinal);
                if (cityList.Count > 1 || _cityFilter != null)
                {
                    n = 1;
                    Ui.Chip(row, "All cities", _cityFilter == null, () => SetFilter(ref _cityFilter, null));
                    foreach (string city in cityList)
                    {
                        if (++n % ChipsPerRow == 0) row = Ui.ChipRow(bar);
                        string value = city;
                        Ui.Chip(row, city, _cityFilter == city, () => SetFilter(ref _cityFilter, value));
                    }
                }
            }
        }

        private void SetFilter(ref string field, string value)
        {
            field = value;
            RebuildFiltered();
        }

        private void RebuildFiltered()
        {
            if (_demandResult != null) { Ui.Clear(_demand); BuildDemand(_demandResult); }
            if (_productionResult != null) { Ui.Clear(_production); BuildProduction(_productionResult); }
        }

        private static void Showing(Transform parent, int shown, int total, string what)
        {
            if (shown == total) return;
            Ui.Label(parent, "Showing " + shown + " of " + total + " " + what + ".", 11, color: Ui.SubtleColor);
        }

        // ---------------- Clicking through to the game ----------------

        /// <summary>Moves the camera to the building and opens its own window, like clicking it on the map.</summary>
        private static void GoTo(Building building)
        {
            if (building == null) return;
            GameCameraViewHelper.TryGoTo(building);
            LazyManager<BuildingUIManager>.Current.ShowWindow(building);
        }

        private static void GoTo(CityDemand demand)
        {
            if (demand.Store != null) GoTo(demand.Store);
            else GameUIHelper.ShowCityWindow(demand.City);
        }

        // ---------------- Demand ----------------

        private void BuildDemand(DemandForecastResult f)
        {
            var served = new List<DemandForecastEntry>();
            foreach (DemandForecastEntry e in f.Served)
            {
                bool problem = e.Outlook != DemandOutlook.LevelUp || e.Coverage < 0.9f;
                if (ItemMatches(e.Item) && (_cityFilter == null || e.City == _cityFilter) && (!_problemsOnly || problem))
                    served.Add(e);
            }

            var items = new List<string>();
            var cities = new List<string>();
            foreach (DemandForecastEntry e in f.Served) { items.Add(e.Item); cities.Add(e.City); }
            foreach (ItemDemandSummary s in f.UnservedByItem) items.Add(s.Item);
            BuildFilters(_demand, items, cities);

            Ui.Heading(_demand, "Cities you deliver to");
            Showing(_demand, served.Count, f.Served.Count, "demands");
            if (served.Count == 0) Ui.Empty(_demand, f.Served.Count == 0 ? "None yet." : "Nothing matches the filters.");
            foreach (DemandForecastEntry e in served)
            {
                Transform card = Ui.Card(_demand);

                Transform name = Ui.FixedColumn(card, NameWidth);
                Ui.Label(name, e.City, 13, bold: true);
                Ui.Label(name, e.Item + " · level " + e.Level, 11, color: Ui.SubtleColor);

                Transform supply = Ui.FixedColumn(card, RateWidth, 4f);
                Ui.Label(supply, e.DeliveredLast30Days + " / " + e.DemandPerMonth + " per month", 11);
                Ui.Bar(supply, e.Coverage, RateWidth - 10);

                string sat = "Satisfaction " + Ui.Bold(Percent(e.Satisfaction));
                if (Math.Abs(e.SteadySatisfaction - e.Satisfaction) >= 0.1f) sat += "\nheading to " + Percent(e.SteadySatisfaction);
                Ui.Label(card, sat, 11, width: SatisfactionWidth);

                Ui.Label(card, ShortOutlook(e), 11, color: OutlookColor(e.Outlook));

                CityDemand demand = e.Demand;
                Ui.MakeClickable(card, () => GoTo(demand), "Go to this store and open it");
            }

            // Per-item totals from the cards shown, so they follow the city and item filters.
            var byItem = new SortedDictionary<string, ItemDemandSummary>(StringComparer.Ordinal);
            foreach (DemandForecastEntry e in served)
            {
                if (!byItem.TryGetValue(e.Item, out ItemDemandSummary s)) byItem[e.Item] = s = new ItemDemandSummary { Item = e.Item };
                s.Cities++;
                s.DeliveredLast30Days += e.DeliveredLast30Days;
                s.DemandPerMonth += e.DemandPerMonth;
                s.NextLevelDemandPerMonth += e.NextLevelDemandPerMonth;
            }

            Ui.Heading(_demand, "Per item");
            Transform items2 = Ui.Card(_demand, LayoutHelper.Orientation.Vertical);
            if (byItem.Count == 0) Ui.Label(items2, "Nothing to show.", 12, color: Ui.SubtleColor);
            foreach (ItemDemandSummary s in byItem.Values)
            {
                int shortfall = s.DemandPerMonth - s.DeliveredLast30Days;
                string text = Ui.Bold(s.Item) + ": delivering " + s.DeliveredLast30Days + "/mo, wanted " + s.DemandPerMonth +
                              "/mo now, " + s.NextLevelDemandPerMonth + "/mo after next level-ups";
                if (shortfall > 0) text += "   " + Ui.Paint("short by " + shortfall + "/mo", Ui.WarnColor);
                Ui.Label(items2, text, 12);
            }

            // Opportunities are not problems, and they are totals per item (not per city).
            if (_problemsOnly || _cityFilter != null) return;
            Ui.Heading(_demand, "Unserved demand (opportunities)");
            Transform opp = Ui.Card(_demand, LayoutHelper.Orientation.Vertical);
            int shownOpp = 0;
            foreach (ItemDemandSummary s in f.UnservedByItem)
            {
                if (!ItemMatches(s.Item)) continue;
                shownOpp++;
                Ui.Label(opp, Ui.Bold(s.Item) + ": " + s.DemandPerMonth + "/mo across " + s.Cities + (s.Cities == 1 ? " city" : " cities"), 12);
            }
            if (shownOpp == 0) Ui.Label(opp, "None in your unlocked regions.", 12, color: Ui.SubtleColor);
        }

        // ---------------- Production ----------------

        private void BuildProduction(ProductionForecastResult f)
        {
            var items = new List<string>();
            foreach (ItemBalance b in f.Items) items.Add(b.Item);
            BuildFilters(_production, items, null);

            Ui.Heading(_production, "Item balance (per month)");
            int shown = 0;
            foreach (ItemBalance b in f.Items)
            {
                bool problem = !b.Verdict.StartsWith("balanced") && !b.Verdict.StartsWith("not used");
                if (!ItemMatches(b.Item) || (_problemsOnly && !problem)) continue;
                shown++;
                Transform card = Ui.Card(_production, LayoutHelper.Orientation.Vertical);
                string text = Ui.Bold(b.Item) + "   made " + b.ProducedPerMonth + " (max " + b.MaxProductionPerMonth + ")";
                if (b.FactoryNeedPerMonth > 0) text += " · factories use " + b.UsedByFactoriesPerMonth + " (need " + b.FactoryNeedPerMonth + ")";
                if (b.CityWantNowPerMonth > 0) text += " · cities get " + b.DeliveredToCitiesPerMonth + " (want " + b.CityWantNowPerMonth + ")";
                Ui.Label(card, text, 12);
                Color color = b.Verdict.StartsWith("SHORT") ? Ui.BadColor : b.Verdict.StartsWith("balanced") ? Ui.GoodColor : Ui.WarnColor;
                Ui.Label(card, Soften(b.Verdict), 11, color: color);
            }
            if (shown == 0) Ui.Empty(_production, f.Items.Count == 0 ? "Nothing produced yet." : "Nothing matches the filters.");

            Ui.Heading(_production, "Mines");
            shown = 0;
            foreach (MineReport m in f.Mines)
            {
                if (!ItemMatches(m.Item) || (_problemsOnly && m.Diagnosis.StartsWith("running"))) continue;
                shown++;
                string extra = m.DaysUntilDepleted is int days ? "deposit lasts " + Sampler.Duration(days) : null;
                BuildingCard(m.Building, m.Name, m.Item, m.MinedPerMonth, m.MaxPerMonth, m.Utilization, m.Diagnosis, extra);
            }
            if (shown == 0) Ui.Empty(_production, f.Mines.Count == 0 ? "No mines." : "Nothing matches the filters.");

            Ui.Heading(_production, "Factories");
            shown = 0;
            foreach (DeviceReport d in f.Devices)
            {
                if (!DeviceMatches(d) || (_problemsOnly && d.Diagnosis.StartsWith("running"))) continue;
                shown++;
                string extra = "inputs " + Percent(d.InputFill) + " full, outputs " + Percent(d.OutputFill) + " full";
                BuildingCard(d.Building, d.Name, d.Recipe, d.ProducedPerMonth, d.MaxPerMonth, d.Utilization, d.Diagnosis, extra);
            }
            if (shown == 0) Ui.Empty(_production, f.Devices.Count == 0 ? "No factories." : "Nothing matches the filters.");

            Ui.Heading(_production, "Warehouses");
            shown = 0;
            foreach (StorageReport s in f.Storages)
            {
                bool problem = (s.DaysUntilEmpty ?? int.MaxValue) < 30 || (s.DaysUntilFull ?? int.MaxValue) < 30;
                if (!ItemMatches(s.Item) || (_problemsOnly && !problem)) continue;
                shown++;

                Transform card = Ui.Card(_production);
                Transform name = Ui.FixedColumn(card, NameWidth);
                Ui.Label(name, s.Name, 12, bold: true);
                Ui.Label(name, s.Item, 11, color: Ui.SubtleColor);

                Transform fill = Ui.FixedColumn(card, RateWidth, 4f);
                float fraction = s.Capacity > 0 ? (float)s.Count / s.Capacity : 0f;
                Ui.Label(fill, s.Count + " / " + s.Capacity, 11);
                Ui.Bar(fill, fraction, RateWidth - 10, ForecastColor);

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

                Building building = s.Building;
                Ui.MakeClickable(card, () => GoTo(building), "Go to this warehouse and open it");
            }
            if (shown == 0) Ui.Empty(_production, f.Storages.Count == 0 ? "No warehouses." : "Nothing matches the filters.");
        }

        /// <summary>A factory matches the item filter if the item is one of its recipe's inputs or outputs.</summary>
        private bool DeviceMatches(DeviceReport d)
        {
            if (_itemFilter == null) return true;
            Recipe recipe = d.Building?.Recipe;
            if (recipe == null) return false;
            foreach (RecipeItem i in recipe.InputItems) if (i.Item?.DisplayName?.ToString() == _itemFilter) return true;
            foreach (RecipeItem o in recipe.OutputItems) if (o.Item?.DisplayName?.ToString() == _itemFilter) return true;
            return false;
        }

        private void BuildingCard(Building building, string title, string subtitle, int actual, int max, float utilization, string diagnosis, string extra)
        {
            Transform card = Ui.Card(_production);

            Transform name = Ui.FixedColumn(card, NameWidth);
            Ui.Label(name, title, 12, bold: true);
            Ui.Label(name, subtitle, 11, color: Ui.SubtleColor);

            Transform rate = Ui.FixedColumn(card, RateWidth, 4f);
            Ui.Label(rate, actual + " of " + max + "/mo (" + Percent(utilization) + ")", 11);
            Ui.Bar(rate, utilization, RateWidth - 10);

            Transform info = Ui.FlexibleColumn(card, 2f);
            Ui.Label(info, Soften(diagnosis), 11, color: DiagnosisColor(diagnosis));
            if (extra != null) Ui.Label(info, extra, 11, color: Ui.SubtleColor);

            Ui.MakeClickable(card, () => GoTo(building), "Go to this building and open it");
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
        // "67%", as the game writes it (the invariant "P0" format gives "67 %").
        private static string Percent(float v) => Mathf.RoundToInt(v * 100f) + "%";

        /// <summary>Card-sized version of the demand outlook; the log keeps the longer sentence.</summary>
        private static string ShortOutlook(DemandForecastEntry e)
        {
            string days = e.DaysUntil is int d ? "~" + d + " days" : "soon";
            switch (e.Outlook)
            {
                case DemandOutlook.LevelUp:
                    return "Levels up in " + days + "\nthen " + e.NextLevelDemandPerMonth + "/mo (+" + (e.NextLevelDemandPerMonth - e.DemandPerMonth) + ")";
                case DemandOutlook.LevelUpBlocked:
                    return "City too small to grow\nneeds " + e.PopulationNeeded.ToString("#,0", CultureInfo.InvariantCulture) +
                           " people (has " + e.PopulationNow.ToString("#,0", CultureInfo.InvariantCulture) + ")";
                case DemandOutlook.Stable:
                    return "Stable\nneeds 40% satisfaction to grow";
                case DemandOutlook.LevelDown:
                    return "Drops a level in " + days + "\nunless satisfaction reaches 20%";
                case DemandOutlook.Closing:
                    return "Store closes in " + days + "\nunless satisfaction reaches 20%";
                default:
                    return "";
            }
        }

        /// <summary>"BLOCKED: x" → "Blocked: x". Capitals help when scanning the log, but shout in the window.</summary>
        private static string Soften(string text)
        {
            foreach (string word in new[] { "BLOCKED", "STARVED", "SHORT", "WARNING" })
            {
                if (text.StartsWith(word)) return word[0] + word.Substring(1).ToLower() + text.Substring(word.Length);
            }
            return text.Length > 0 ? char.ToUpper(text[0]) + text.Substring(1) : text;
        }

        private static string Short(double v)
        {
            double a = Math.Abs(v);
            if (a >= 1e6) return (v / 1e6).ToString("0.00", CultureInfo.InvariantCulture) + "M";
            if (a >= 1e3) return (v / 1e3).ToString("0", CultureInfo.InvariantCulture) + "k";
            return v.ToString("0", CultureInfo.InvariantCulture);
        }
    }
}
