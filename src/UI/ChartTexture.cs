using System;
using System.Collections.Generic;
using UnityEngine;

namespace ForecastMod.UI
{
    /// <summary>One line on a chart: points as (x, y) in data units.</summary>
    public class ChartSeries
    {
        public readonly List<Vector2> Points = new List<Vector2>();
        public Color Color = Color.white;
        public bool Dashed;
    }

    /// <summary>A shaded range between a low and a high line (e.g. an 80% forecast range).</summary>
    public class ChartBand
    {
        public readonly List<Vector2> Low = new List<Vector2>();
        public readonly List<Vector2> High = new List<Vector2>();
        public Color Color = new Color(1, 1, 1, 0.15f);
    }

    /// <summary>
    /// Draws a simple line chart into a Texture2D. The game has no chart widget, so we paint
    /// pixels ourselves; the texture is only rebuilt when the data changes (once per in-game day).
    /// </summary>
    public class ChartTexture
    {
        public static readonly Color Background = new Color(0.10f, 0.11f, 0.13f, 1f);
        public static readonly Color Grid = new Color(1f, 1f, 1f, 0.08f);
        public static readonly Color Zero = new Color(1f, 0.35f, 0.35f, 0.6f);
        public static readonly Color Today = new Color(1f, 1f, 1f, 0.35f);

        public readonly List<ChartSeries> Series = new List<ChartSeries>();
        public readonly List<ChartBand> Bands = new List<ChartBand>();

        /// <summary>X position of a vertical "today" marker, or null.</summary>
        public float? TodayX;

        public float MinX, MaxX, MinY, MaxY;

        private Color[] _pixels;
        private int _w, _h;

        /// <summary>Fits the axes around all series and bands, with a little headroom.</summary>
        public void FitBounds()
        {
            MinX = MinY = float.MaxValue;
            MaxX = MaxY = float.MinValue;
            foreach (ChartSeries s in Series) foreach (Vector2 p in s.Points) Include(p);
            foreach (ChartBand b in Bands)
            {
                foreach (Vector2 p in b.Low) Include(p);
                foreach (Vector2 p in b.High) Include(p);
            }
            if (MinX > MaxX) { MinX = 0; MaxX = 1; MinY = 0; MaxY = 1; return; }
            if (Mathf.Approximately(MinX, MaxX)) MaxX = MinX + 1;
            float pad = Mathf.Max((MaxY - MinY) * 0.08f, Mathf.Abs(MaxY) * 0.02f, 1f);
            MinY -= pad;
            MaxY += pad;
        }

        public Texture2D Render(int width, int height, Texture2D reuse = null)
        {
            _w = width;
            _h = height;
            _pixels = new Color[width * height];
            for (int i = 0; i < _pixels.Length; i++) _pixels[i] = Background;

            for (int g = 1; g < 4; g++) HLine(height * g / 4, Grid);
            if (MinY < 0 && MaxY > 0) HLine(Py(0), Zero);
            if (TodayX is float t) VLine(Px(t), Today);

            foreach (ChartBand b in Bands) FillBand(b);
            foreach (ChartSeries s in Series)
            {
                for (int i = 1; i < s.Points.Count; i++)
                    Line(Px(s.Points[i - 1].x), Py(s.Points[i - 1].y), Px(s.Points[i].x), Py(s.Points[i].y), s.Color, s.Dashed);
            }

            Texture2D tex = reuse != null && reuse.width == width && reuse.height == height
                ? reuse
                : new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels(_pixels);
            tex.Apply(false);
            return tex;
        }

        /// <summary>Screen-independent position (0..1 from the left/bottom) of a data value, for labels.</summary>
        public float NormY(float y) => (y - MinY) / (MaxY - MinY);
        public float NormX(float x) => (x - MinX) / (MaxX - MinX);

        private void Include(Vector2 p)
        {
            MinX = Mathf.Min(MinX, p.x); MaxX = Mathf.Max(MaxX, p.x);
            MinY = Mathf.Min(MinY, p.y); MaxY = Mathf.Max(MaxY, p.y);
        }

        private int Px(float x) => Mathf.RoundToInt(NormX(x) * (_w - 1));
        private int Py(float y) => Mathf.RoundToInt(NormY(y) * (_h - 1));

        private void FillBand(ChartBand b)
        {
            int n = Math.Min(b.Low.Count, b.High.Count);
            for (int i = 1; i < n; i++)
            {
                int x0 = Px(b.Low[i - 1].x), x1 = Px(b.Low[i].x);
                for (int x = x0; x <= x1; x++)
                {
                    float t = x1 == x0 ? 0 : (float)(x - x0) / (x1 - x0);
                    int lo = Py(Mathf.Lerp(b.Low[i - 1].y, b.Low[i].y, t));
                    int hi = Py(Mathf.Lerp(b.High[i - 1].y, b.High[i].y, t));
                    for (int y = lo; y <= hi; y++) Blend(x, y, b.Color);
                }
            }
        }

        private void Line(int x0, int y0, int x1, int y1, Color c, bool dashed)
        {
            // Bresenham, 2 px thick.
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy, step = 0;
            while (true)
            {
                if (!dashed || (step / 6) % 2 == 0)
                {
                    Blend(x0, y0, c); Blend(x0, y0 + 1, c); Blend(x0 + 1, y0, c);
                }
                step++;
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        private void HLine(int y, Color c) { for (int x = 0; x < _w; x++) Blend(x, y, c); }
        private void VLine(int x, Color c) { for (int y = 0; y < _h; y++) Blend(x, y, c); }

        private void Blend(int x, int y, Color c)
        {
            if (x < 0 || y < 0 || x >= _w || y >= _h) return;
            int i = y * _w + x;
            _pixels[i] = Color.Lerp(_pixels[i], new Color(c.r, c.g, c.b, 1f), c.a);
        }
    }
}
