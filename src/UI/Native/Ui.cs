using UnityEngine;
using UnityEngine.UI;
using VoxelTycoon;
using VoxelTycoon.UI;

namespace ForecastMod.UI.Native
{
    /// <summary>
    /// Small helpers that build Unity UI elements in the game's own style
    /// (Open Sans font, the game's colors, rounded Panel cards), using the game's layout helpers.
    /// </summary>
    public static class Ui
    {
        // Text colors for the game's light content background.
        public static readonly Color TextColor = ColorHelper.FromHexString("3d3d3d");
        public static readonly Color SubtleColor = new Color(0f, 0f, 0f, 0.53f);
        public static readonly Color GoodColor = ColorHelper.FromHexString("5f9a0f");
        public static readonly Color WarnColor = ColorHelper.FromHexString("d08000");
        public static readonly Color BadColor = ColorHelper.FromHexString("c82323");
        public static readonly Color CardColor = Color.white;
        public static readonly Color BarBackground = ColorHelper.FromHexString("d4d4d4");

        public static Text Label(Transform parent, string text, int size = 13, bool bold = false, Color? color = null,
                                 float width = -1f, TextAnchor align = TextAnchor.UpperLeft)
        {
            GameObject go = parent.AddUIChild("Label");
            Text t = go.AddComponent<Text>();
            t.font = bold ? R.Fonts.OpenSans.OpenSansSemiBold : R.Fonts.OpenSans.OpenSansRegular;
            t.fontSize = size;
            t.color = color ?? TextColor;
            t.alignment = align;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = text;

            LayoutElement le = go.AddComponent<LayoutElement>();
            if (width > 0) { le.preferredWidth = width; le.minWidth = width; }
            else le.flexibleWidth = 1f;
            return t;
        }

        /// <summary>Small grey uppercase section heading, like the game's table headers.</summary>
        public static Text Heading(Transform parent, string text)
        {
            Text t = Label(parent, text.ToUpper(), 11, bold: true, color: SubtleColor);
            t.GetComponent<LayoutElement>().minHeight = 22;
            t.alignment = TextAnchor.LowerLeft;
            return t;
        }

        public static Transform Row(Transform parent, float spacing = 10f, TextAnchor align = TextAnchor.MiddleLeft)
        {
            Transform row = parent.AddUIChild("Row").transform;
            LayoutHelper.MakeLayoutGroup(row, LayoutHelper.Orientation.Horizontal, new RectOffset(), spacing, align);
            return row;
        }

        public static Transform Column(Transform parent, float spacing = 6f, RectOffset padding = null)
        {
            Transform col = parent.AddUIChild("Column").transform;
            LayoutHelper.MakeLayoutGroup(col, LayoutHelper.Orientation.Vertical, padding ?? new RectOffset(), spacing);
            return col;
        }

        /// <summary>A white rounded card, like the rows in the game's city demand list.</summary>
        public static Transform Card(Transform parent, LayoutHelper.Orientation orientation = LayoutHelper.Orientation.Horizontal)
        {
            GameObject go = parent.AddUIChild("Card");
            Panel panel = go.AddComponent<Panel>();
            panel.CornersType = PanelCornerType.Round5;
            panel.EnabledCorners = PanelCorners.All;
            panel.color = CardColor;
            panel.raycastTarget = false;
            LayoutHelper.MakeLayoutGroup(go.transform, orientation, new RectOffset(12, 12, 8, 8), 12f, TextAnchor.MiddleLeft);
            return go.transform;
        }

        /// <summary>A thin progress bar, filled to <paramref name="fraction"/> (0..1).</summary>
        public static void Bar(Transform parent, float fraction, float width = 110f, Color? color = null)
        {
            GameObject bg = parent.AddUIChild("Bar");
            Panel back = bg.AddComponent<Panel>();
            back.CornersType = PanelCornerType.Round3;
            back.EnabledCorners = PanelCorners.All;
            back.color = BarBackground;
            back.raycastTarget = false;
            LayoutElement le = bg.AddComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = width;
            le.preferredHeight = le.minHeight = 6f;

            GameObject fillGo = bg.transform.AddUIChild("Fill");
            Panel fill = fillGo.AddComponent<Panel>();
            fill.CornersType = PanelCornerType.Round3;
            fill.EnabledCorners = PanelCorners.All;
            fill.color = color ?? ColorFor(fraction);
            fill.raycastTarget = false;
            RectTransform rt = fill.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        public static RawImage Image(Transform parent, Texture texture, float width, float height)
        {
            GameObject go = parent.AddUIChild("Image");
            RawImage img = go.AddComponent<RawImage>();
            img.texture = texture;
            img.raycastTarget = false;
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = width;
            le.preferredHeight = le.minHeight = height;
            return img;
        }

        public static void Space(Transform parent, float height)
        {
            LayoutElement le = parent.AddUIChild("Space").AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = height;
        }

        public static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                Object.Destroy(child);
            }
        }

        public static Color ColorFor(float fraction) => fraction >= 0.9f ? GoodColor : fraction >= 0.5f ? WarnColor : BadColor;

        public static string Paint(string text, Color c) => "<color=#" + ColorUtility.ToHtmlStringRGBA(c) + ">" + text + "</color>";

        public static string Bold(string text) => "<b>" + text + "</b>";
    }
}
