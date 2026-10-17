using System;
using System.Globalization;
using ForecastMod.Forecast;
using UnityEngine;
using UnityEngine.UI;
using VoxelTycoon;

namespace ForecastMod.UI
{
    /// <summary>
    /// The in-game forecast window, toggled with F6. Drawn with Unity's IMGUI (OnGUI).
    ///
    /// IMGUI is invisible to the game's own UI checks, so clicks would fall through to the map.
    /// An invisible Unity UI panel is kept exactly behind the window to make the game treat the
    /// mouse as "over UI" there (the game checks this via InputHelper.OverUI / the EventSystem).
    /// </summary>
    public class ForecastWindow : MonoBehaviour
    {
        public const KeyCode ToggleKey = KeyCode.F6;

        private const int WindowId = 0x46435354; // "FCST"
        private const float Width = 780f;
        private const float Height = 560f;
        private const int ChartW = 740;
        private const int ChartH = 210;
        private const int HistoryDays = 180;

        private static readonly string[] Tabs = { "Finance", "Demand", "Production" };

        public History History;

        private bool _visible;
        private int _tab;
        private Rect _rect = new Rect(80, 80, Width, Height);
        private Vector2 _scroll;
        private DateTime _refreshedOn;

        private FinanceForecastResult _finance;
        private DemandForecastResult _demand;
        private ProductionForecastResult _production;
        private ChartTexture _chart;
        private Texture2D _chartTexture;
        private string _error;

        private RectTransform _blocker;
        private GUIStyle _label, _small, _header, _box;

        // ---------------- Lifecycle ----------------

        private void Awake()
        {
            CreateInputBlocker();
        }

        private void OnDestroy()
        {
            if (_chartTexture != null) Destroy(_chartTexture);
            if (_blocker != null) Destroy(_blocker.root.gameObject);
        }

        private void Update()
        {
            if (Input.GetKeyDown(ToggleKey) && !InputHelper.IsInputFieldFocused())
            {
                // F6 opens the native window; Shift+F6 opens this older IMGUI window (kept until the native one is proven).
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                {
                    _visible = !_visible;
                    if (_visible) Refresh();
                }
                else
                {
                    try { Native.NativeForecastWindow.Toggle(); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }

            if (_visible && LazyManager<TimeManager>.Current.DateTime.Date != _refreshedOn) Refresh();

            UpdateInputBlocker();
        }

        private void Refresh()
        {
            _refreshedOn = LazyManager<TimeManager>.Current.DateTime.Date;
            _error = null;
            try
            {
                Company company = Company.Current;
                if (company == null || History == null) return;
                _finance = FinanceForecast.Compute(History, company, LazyManager<TimeManager>.Current.DateTime);
                _demand = DemandForecast.Compute();
                _production = ProductionForecast.Compute(History, company, _demand);
                BuildFinanceChart();
            }
            catch (Exception e)
            {
                _error = e.Message;
                Debug.LogException(e);
            }
        }

        // ---------------- Drawing ----------------

        private void OnGUI()
        {
            if (!_visible) return;
            EnsureStyles();

            float scale = UiScale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            _rect = GUI.Window(WindowId, _rect, DrawWindow, "Voxel Forecast (classic)   (Shift+F6 to close)");
            _rect.x = Mathf.Clamp(_rect.x, 0, Screen.width / scale - _rect.width);
            _rect.y = Mathf.Clamp(_rect.y, 0, Screen.height / scale - _rect.height);
        }

        private static float UiScale => Mathf.Max(1f, Screen.height / 1080f);

        private void DrawWindow(int id)
        {
            if (GUI.Button(new Rect(_rect.width - 26, 3, 22, 18), "x")) _visible = false;

            GUILayout.Space(4);
            int tab = GUILayout.Toolbar(_tab, Tabs, GUILayout.Height(26));
            if (tab != _tab) { _tab = tab; _scroll = Vector2.zero; }
            GUILayout.Space(6);

            if (_error != null) GUILayout.Label(Paint("Error: " + _error, Red), _label);

            _scroll = GUILayout.BeginScrollView(_scroll);
            if (_tab == 0) DrawFinance();
            else if (_tab == 1) DrawDemand();
            else DrawProduction();
            GUILayout.EndScrollView();

            GUI.DragWindow(new Rect(0, 0, _rect.width, 22));
        }

        private void DrawFinance()
        {
            FinanceForecastResult f = _finance;
            if (f == null || f.Months.Count == 0) { GUILayout.Label("No data yet.", _label); return; }

            FinanceForecastMonth next = f.Months[0];
            GUILayout.Label("Cash now <b>" + Money(f.CashNow) + "</b>     Recurring net income <b>" + SignedMoney(next.Net) +
                            "</b>/month     Forecast from " + f.MonthsOfHistory + " month(s) of history, " + f.Method, _label);

            if (f.FirstNegative is int neg)
                GUILayout.Label(Paint("Cash goes negative around " + MonthName(f.Months[neg]) + ".", Red), _header);
            else if (f.FirstNegativeRisk is int risk)
                GUILayout.Label(Paint("Cash could go negative from " + MonthName(f.Months[risk]) + " if income is at the low end.", Yellow), _header);
            else
                GUILayout.Label(Paint("Cash stays positive for the next " + f.Months.Count + " months.", Green), _header);

            GUILayout.Space(4);
            Rect chart = GUILayoutUtility.GetRect(ChartW, ChartH, GUILayout.ExpandWidth(false));
            if (_chartTexture != null) GUI.DrawTexture(chart, _chartTexture);
            DrawChartLabels(chart);
            GUILayout.Label(Paint("━ cash history", "#FFFFFF") + "     " + Paint("┅ forecast", "#6FB3FF") +
                            "     " + Paint("▒ 80% range", "#6FB3FF") + "     " + Paint("│ today", "#AAAAAA") +
                            "     New buildings and vehicles are not included.", _small);

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            for (int col = 0; col < 2; col++)
            {
                GUILayout.BeginVertical(_box, GUILayout.Width(362));
                Row("<b>Month</b>", "<b>Net/month</b>", "<b>Cash at month end</b>", "<b>80% range</b>");
                for (int i = col * 6; i < Math.Min(f.Months.Count, col * 6 + 6); i++)
                {
                    FinanceForecastMonth m = f.Months[i];
                    string cash = m.Cash < 0 ? Paint(Money(m.Cash), Red) : Money(m.Cash);
                    Row(MonthName(m), SignedMoney(m.Net), cash, Short(m.CashLow) + " – " + Short(m.CashHigh));
                }
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();
        }

        private void Row(string a, string b, string c, string d)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(a, _small, GUILayout.Width(52));
            GUILayout.Label(b, _small, GUILayout.Width(80));
            GUILayout.Label(c, _small, GUILayout.Width(100));
            GUILayout.Label(d, _small, GUILayout.Width(110));
            GUILayout.EndHorizontal();
        }

        private void DrawChartLabels(Rect r)
        {
            if (_chart == null) return;
            for (int g = 0; g <= 4; g++)
            {
                float v = Mathf.Lerp(_chart.MinY, _chart.MaxY, g / 4f);
                float y = r.yMax - r.height * g / 4f - 9;
                GUI.Label(new Rect(r.x + 4, y, 120, 18), Paint(Short(v), "#AAAAAA"), _small);
            }
            GUI.Label(new Rect(r.x + 4, r.yMax + 1, 140, 18), Paint(-HistoryDays + " days", "#AAAAAA"), _small);
            if (_chart.TodayX is float t)
                GUI.Label(new Rect(r.x + r.width * _chart.NormX(t) - 18, r.yMax + 1, 60, 18), Paint("today", "#AAAAAA"), _small);
            GUI.Label(new Rect(r.xMax - 70, r.yMax + 1, 80, 18), Paint("+12 months", "#AAAAAA"), _small);
            GUILayout.Space(14);
        }

        private void DrawDemand()
        {
            DemandForecastResult f = _demand;
            if (f == null) { GUILayout.Label("No data yet.", _label); return; }

            GUILayout.Label("<b>Cities you deliver to</b>", _header);
            if (f.Served.Count == 0) GUILayout.Label("None yet.", _label);
            foreach (DemandForecastEntry e in f.Served)
            {
                GUILayout.BeginHorizontal(_box);
                GUILayout.Label("<b>" + e.City + "</b>  " + e.Item + "  (level " + e.Level + ")", _label, GUILayout.Width(250));
                GUILayout.Label(e.DeliveredLast30Days + " / " + e.DemandPerMonth + " per month\n" +
                                Bar(e.Coverage) + " " + Percent(e.Coverage), _small, GUILayout.Width(140));
                string sat = "satisfaction " + Percent(e.Satisfaction);
                if (Math.Abs(e.SteadySatisfaction - e.Satisfaction) >= 0.1f) sat += "\nheading to " + Percent(e.SteadySatisfaction);
                GUILayout.Label(sat, _small, GUILayout.Width(110));
                GUILayout.Label(Paint(Sampler.Describe(e), OutlookColor(e.Outlook)), _small);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);
            GUILayout.Label("<b>Per item</b>", _header);
            foreach (ItemDemandSummary s in f.ServedByItem)
            {
                int shortfall = s.DemandPerMonth - s.DeliveredLast30Days;
                string text = "<b>" + s.Item + "</b>: delivering " + s.DeliveredLast30Days + "/mo, wanted " + s.DemandPerMonth +
                              "/mo now, " + s.NextLevelDemandPerMonth + "/mo after next level-ups";
                if (shortfall > 0) text += "   " + Paint("short by " + shortfall + "/mo", Yellow);
                GUILayout.Label(text, _label);
            }

            GUILayout.Space(8);
            GUILayout.Label("<b>Unserved demand (opportunities)</b>", _header);
            if (f.UnservedByItem.Count == 0) GUILayout.Label("None in your unlocked regions.", _label);
            foreach (ItemDemandSummary s in f.UnservedByItem)
                GUILayout.Label(s.Item + ": " + s.DemandPerMonth + "/mo across " + s.Cities + (s.Cities == 1 ? " city" : " cities"), _label);
        }

        private void DrawProduction()
        {
            ProductionForecastResult f = _production;
            if (f == null) { GUILayout.Label("No data yet.", _label); return; }

            GUILayout.Label("<b>Item balance (per month)</b>", _header);
            foreach (ItemBalance b in f.Items)
            {
                string text = "<b>" + b.Item + "</b>: made " + b.ProducedPerMonth + " (max " + b.MaxProductionPerMonth + ")";
                if (b.FactoryNeedPerMonth > 0) text += ", factories use " + b.UsedByFactoriesPerMonth + " (need " + b.FactoryNeedPerMonth + ")";
                if (b.CityWantNowPerMonth > 0) text += ", cities get " + b.DeliveredToCitiesPerMonth + " (want " + b.CityWantNowPerMonth + ")";
                string color = b.Verdict.StartsWith("SHORT") ? Red : b.Verdict.StartsWith("balanced") ? Green : Yellow;
                GUILayout.Label(text + "\n    " + Paint(b.Verdict, color), _label);
            }

            GUILayout.Space(8);
            GUILayout.Label("<b>Mines</b>", _header);
            if (f.Mines.Count == 0) GUILayout.Label("None.", _label);
            foreach (MineReport m in f.Mines)
            {
                GUILayout.BeginHorizontal(_box);
                GUILayout.Label("<b>" + m.Name + "</b>\n" + m.Item, _small, GUILayout.Width(170));
                GUILayout.Label(m.MinedPerMonth + " of " + m.MaxPerMonth + "/mo\n" + Bar(m.Utilization) + " " + Percent(m.Utilization), _small, GUILayout.Width(140));
                string text = Paint(m.Diagnosis, DiagnosisColor(m.Diagnosis));
                if (m.DaysUntilDepleted is int days) text += "\ndeposit lasts " + Sampler.Duration(days);
                GUILayout.Label(text, _small);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);
            GUILayout.Label("<b>Factories</b>", _header);
            if (f.Devices.Count == 0) GUILayout.Label("None.", _label);
            foreach (DeviceReport d in f.Devices)
            {
                GUILayout.BeginHorizontal(_box);
                GUILayout.Label("<b>" + d.Name + "</b>\n" + d.Recipe, _small, GUILayout.Width(170));
                GUILayout.Label(d.ProducedPerMonth + " of " + d.MaxPerMonth + "/mo\n" + Bar(d.Utilization) + " " + Percent(d.Utilization), _small, GUILayout.Width(140));
                GUILayout.Label(Paint(d.Diagnosis, DiagnosisColor(d.Diagnosis)) + "\ninputs " + Percent(d.InputFill) +
                                " full, outputs " + Percent(d.OutputFill) + " full", _small);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);
            GUILayout.Label("<b>Warehouses</b>", _header);
            if (f.Storages.Count == 0) GUILayout.Label("None.", _label);
            foreach (StorageReport s in f.Storages)
            {
                string text = "<b>" + s.Name + "</b> (" + s.Item + ")  " + s.Count + "/" + s.Capacity + "   ";
                if (s.ChangePerDay is double change)
                {
                    text += (change >= 0 ? "+" : "") + change.ToString("0.#", CultureInfo.InvariantCulture) + "/day";
                    if (s.DaysUntilEmpty is int empty) text += ", " + Paint("empty in ~" + empty + " days", empty < 30 ? Red : Yellow);
                    else if (s.DaysUntilFull is int full) text += ", " + Paint("full in ~" + full + " days", full < 30 ? Red : Yellow);
                    else text += ", steady";
                }
                else text += Paint("trend after 3 days of samples", "#AAAAAA");
                GUILayout.Label(text, _label);
            }
        }

        // ---------------- Chart ----------------

        private void BuildFinanceChart()
        {
            DateTime today = LazyManager<TimeManager>.Current.DateTime.Date;
            var chart = new ChartTexture { TodayX = 0 };

            var history = new ChartSeries { Color = new Color(1f, 1f, 1f, 0.9f) };
            foreach (DayRecord d in History.Days)
            {
                float x = (float)(new DateTime(d.Year, d.Month, d.Day) - today).TotalDays;
                if (x >= -HistoryDays) history.Points.Add(new Vector2(x, (float)d.Money));
            }

            var start = new Vector2(0, (float)_finance.CashNow);
            history.Points.Add(start);

            var forecast = new ChartSeries { Color = new Color(0.44f, 0.70f, 1f, 1f), Dashed = true };
            var band = new ChartBand { Color = new Color(0.44f, 0.70f, 1f, 0.18f) };
            forecast.Points.Add(start);
            band.Low.Add(start);
            band.High.Add(start);
            foreach (FinanceForecastMonth m in _finance.Months)
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

        // ---------------- Input blocker ----------------

        private void CreateInputBlocker()
        {
            var canvasObject = new GameObject("ForecastMod.InputBlocker");
            DontDestroyOnLoad(canvasObject);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            canvasObject.AddComponent<GraphicRaycaster>();

            var panel = new GameObject("Blocker");
            panel.transform.SetParent(canvasObject.transform, false);
            var image = panel.AddComponent<Image>();
            image.color = new UnityEngine.Color(0, 0, 0, 0); // invisible, but still catches the mouse
            image.raycastTarget = true;

            _blocker = panel.GetComponent<RectTransform>();
            _blocker.anchorMin = _blocker.anchorMax = Vector2.zero;
            _blocker.pivot = Vector2.zero;
            panel.SetActive(false);
        }

        private void UpdateInputBlocker()
        {
            if (_blocker == null) return;
            _blocker.gameObject.SetActive(_visible);
            if (!_visible) return;

            // IMGUI measures from the top-left, Unity UI from the bottom-left.
            float s = UiScale;
            _blocker.anchoredPosition = new Vector2(_rect.x * s, Screen.height - (_rect.y + _rect.height) * s);
            _blocker.sizeDelta = new Vector2(_rect.width * s, _rect.height * s);
        }

        // ---------------- Helpers ----------------

        private const string Red = "#FF6B6B";
        private const string Yellow = "#F2C94C";
        private const string Green = "#7CD992";

        private void EnsureStyles()
        {
            if (_label != null) return;
            _label = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true, fontSize = 13 };
            _small = new GUIStyle(_label) { fontSize = 12 };
            _header = new GUIStyle(_label) { fontSize = 14 };
            _box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(6, 6, 4, 4), margin = new RectOffset(0, 0, 2, 2) };
        }

        private static string Paint(string text, string hex) => "<color=" + hex + ">" + text + "</color>";

        private static string OutlookColor(DemandOutlook o)
        {
            switch (o)
            {
                case DemandOutlook.LevelUp: return Green;
                case DemandOutlook.LevelDown:
                case DemandOutlook.Closing: return Red;
                default: return Yellow;
            }
        }

        private static string DiagnosisColor(string diagnosis)
        {
            if (diagnosis.StartsWith("BLOCKED") || diagnosis.StartsWith("STARVED") || diagnosis.StartsWith("deposit")) return Red;
            return diagnosis.StartsWith("running") ? Green : Yellow;
        }

        /// <summary>A small text progress bar, e.g. ■■■■■■□□□□.</summary>
        private static string Bar(float fraction)
        {
            int filled = Mathf.Clamp(Mathf.RoundToInt(fraction * 10), 0, 10);
            string color = fraction >= 0.9f ? Green : fraction >= 0.5f ? Yellow : Red;
            return Paint(new string('■', filled), color) + Paint(new string('□', 10 - filled), "#666666");
        }

        private static string MonthName(FinanceForecastMonth m) => m.Year + "-" + m.Month.ToString("00");
        private static string Money(double v) => v.ToString("#,0", CultureInfo.InvariantCulture);
        private static string SignedMoney(double v) => (v >= 0 ? "+" : "") + Money(v);
        private static string Percent(float v) => v.ToString("P0", CultureInfo.InvariantCulture);

        /// <summary>Compact money: 1.2M, 340k.</summary>
        private static string Short(double v)
        {
            double a = Math.Abs(v);
            if (a >= 1e6) return (v / 1e6).ToString("0.00", CultureInfo.InvariantCulture) + "M";
            if (a >= 1e3) return (v / 1e3).ToString("0", CultureInfo.InvariantCulture) + "k";
            return v.ToString("0", CultureInfo.InvariantCulture);
        }
    }
}
