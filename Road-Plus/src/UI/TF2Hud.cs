// Panel shown on the left while the Road+ tool is active (Unity IMGUI).
// Styled like the game's mod panels: title, tabs, icon list, description and "Leave tool".
using TF2YolAraci.Core;
using TF2YolAraci.Systems;
using UnityEngine;

namespace TF2YolAraci.UI
{
    public class TF2Hud : MonoBehaviour
    {
        private enum Tab { Mode, Grade, Smoothing, Keys, Shape, ConnectHeight, SlopeHeight }

        private const int kWindowId = 0x7F2A;
        private const float kWidth = 330f;
        private static readonly double[] kGradeSteps = { -0.10, -0.05, 0.05, 0.10 };

        private Tab m_Tab = Tab.Mode;
        private Rect m_WindowRect = new Rect(16f, 110f, kWidth, 10f);
        private Tab m_LastTab = Tab.Mode;
        private ToolMode m_LastTool = ToolMode.Draw;
        private bool m_StylesReady;
        private GUIStyle m_Panel, m_Header, m_Sub, m_Close, m_TabStyle, m_TabSelStyle, m_Item, m_ItemSel, m_Icon, m_IconSel,
            m_DescTitle, m_Desc, m_Ok, m_Warn, m_Footer, m_Small, m_SmallBtn, m_Value, m_Divider, m_ToolBtn, m_ToolBtnSel;

        public static void Create()
        {
            var go = new GameObject("TF2YolAraci_Hud");
            DontDestroyOnLoad(go);
            go.AddComponent<TF2Hud>();
        }

        private void OnGUI()
        {
            if (!ToolStatus.active || TF2RoadToolSystem.Instance == null)
            {
                ToolStatus.panelRect = default(Rect);
                ToolStatus.pointerOverPanel = false;
                return;
            }
            EnsureStyles();

            float scale = Mathf.Max(1f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            // The window keeps its auto-sized rect between frames; it shrinks again when the tab changes.
            if (m_LastTab != m_Tab) { m_WindowRect.height = 10f; m_LastTab = m_Tab; }
            m_WindowRect = GUILayout.Window(kWindowId, m_WindowRect, DrawWindow, GUIContent.none, m_Panel, GUILayout.Width(kWidth));

            // Event.current.mousePosition is already in the scaled GUI space, same as m_WindowRect.
            ToolStatus.pointerOverPanel = m_WindowRect.Contains(Event.current.mousePosition);
            if (Event.current.type == EventType.Repaint)
                ToolStatus.panelRect = new Rect(m_WindowRect.x * scale, m_WindowRect.y * scale, m_WindowRect.width * scale, m_WindowRect.height * scale);
        }

        private void DrawWindow(int id)
        {
            TF2RoadToolSystem tool = TF2RoadToolSystem.Instance;

            // Header
            GUILayout.BeginHorizontal();
            GUILayout.Space(28f);
            GUILayout.FlexibleSpace();
            GUILayout.Label("ROAD+", m_Header);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", m_Close, GUILayout.Width(28f), GUILayout.Height(26f))) ToolStatus.closeRequested = true;
            GUILayout.EndHorizontal();
            GUILayout.Label(ToolStatus.roadName + ClassSuffix(ToolStatus.roadClass), m_Sub);
            GUILayout.Space(8f);

            // Tool: Draw / Connect / Slope
            GUILayout.BeginHorizontal();
            ToolButton(tool, ToolMode.Draw, "Draw");
            ToolButton(tool, ToolMode.Connect, "Connect");
            ToolButton(tool, ToolMode.Slope, "Slope");
            GUILayout.EndHorizontal();
            if (m_LastTool != tool.Tool)
            {
                m_LastTool = tool.Tool;
                m_Tab = tool.Tool == ToolMode.Draw ? Tab.Mode : tool.Tool == ToolMode.Connect ? Tab.Shape : Tab.SlopeHeight;
            }
            GUILayout.Space(6f);

            // Tabs of the current tool
            GUILayout.BeginHorizontal();
            switch (tool.Tool)
            {
                case ToolMode.Draw:
                    TabButton(Tab.Mode, "Mode");
                    TabButton(Tab.Grade, "Grade");
                    TabButton(Tab.Smoothing, "Smoothing");
                    break;
                case ToolMode.Connect:
                    TabButton(Tab.Shape, "Shape");
                    TabButton(Tab.ConnectHeight, "Height");
                    break;
                default:
                    TabButton(Tab.SlopeHeight, "Height");
                    break;
            }
            TabButton(Tab.Keys, "Keys");
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);

            string title, text;
            switch (m_Tab)
            {
                case Tab.Mode: DrawModeTab(tool, out title, out text); break;
                case Tab.Grade: DrawGradeTab(tool, out title, out text); break;
                case Tab.Smoothing: DrawSmoothingTab(tool, out title, out text); break;
                case Tab.Shape: DrawShapeTab(tool, out title, out text); break;
                case Tab.ConnectHeight: DrawConnectHeightTab(tool, out title, out text); break;
                case Tab.SlopeHeight: DrawSlopeHeightTab(tool, out title, out text); break;
                default: DrawKeysTab(tool, out title, out text); break;
            }

            // Description
            GUILayout.Space(10f);
            GUILayout.Label(title, m_DescTitle);
            if (!string.IsNullOrEmpty(text)) GUILayout.Label(text, m_Desc);

            // Status
            GUILayout.Space(8f);
            GUILayout.Box(GUIContent.none, m_Divider, GUILayout.Height(1f), GUILayout.ExpandWidth(true));
            GUILayout.Space(6f);
            DrawStatus();

            GUILayout.Space(10f);
            if (GUILayout.Button("Leave tool", m_Footer, GUILayout.Height(34f))) ToolStatus.closeRequested = true;
        }

        // ---- Tabs ------ ----------------------------------------------------------------------------------------

        private void DrawModeTab(TF2RoadToolSystem tool, out string title, out string text)
        {
            if (Item("~", "Terrain", tool.Mode == HeightMode.Terrain)) tool.SetMode(HeightMode.Terrain);
            if (Item("/", "Graded", tool.Mode == HeightMode.Graded)) tool.SetMode(HeightMode.Graded);
            if (Item("—", "Flat", tool.Mode == HeightMode.Flat)) tool.SetMode(HeightMode.Flat);

            title = ModeName(tool.Mode);
            text = tool.Mode == HeightMode.Terrain ? "The road follows a smoothed version of the terrain. Set the amount in the Smoothing tab."
                 : tool.Mode == HeightMode.Graded ? "The road climbs or descends from the start at a fixed grade. Pick the grade in the Grade tab."
                 : "The road stays level at the start height.";
            text += "\nSmall height differences are fixed by sloping the terrain.\nShortcut: Tab";
        }

        private void DrawGradeTab(TF2RoadToolSystem tool, out string title, out string text)
        {
            foreach (double g in kGradeSteps)
            {
                bool allowed = System.Array.IndexOf(ToolStatus.allowedGrades, g) >= 0;
                bool selected = tool.Mode == HeightMode.Graded && System.Math.Abs(tool.Grade - g) < 1e-9;
                if (Item(g > 0 ? "▲" : "▼", Percent(g, true), selected, allowed))
                {
                    tool.SetGrade(g);
                    tool.SetMode(HeightMode.Graded);
                }
            }

            // End node height
            GUILayout.Space(8f);
            GUILayout.Label("End height", m_Small);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("−1", m_SmallBtn, GUILayout.Height(28f))) tool.AdjustEndHeight(-1f);
            if (GUILayout.Button("−0.25", m_SmallBtn, GUILayout.Height(28f))) tool.AdjustEndHeight(-0.25f);
            GUILayout.Label(ToolStatus.heightOffset.ToString("+0.##;-0.##;0", Inv) + " m", m_Value, GUILayout.Height(28f));
            if (GUILayout.Button("+0.25", m_SmallBtn, GUILayout.Height(28f))) tool.AdjustEndHeight(0.25f);
            if (GUILayout.Button("+1", m_SmallBtn, GUILayout.Height(28f))) tool.AdjustEndHeight(1f);
            if (GUILayout.Button("0", m_SmallBtn, GUILayout.Height(28f))) tool.ResetEndHeight();
            GUILayout.EndHorizontal();

            title = tool.Mode == HeightMode.Graded ? "Grade " + Percent(tool.Grade, true) : "Choose a grade";
            text = "Limit for this road type: " + Percent(ToolStatus.slopeLimitForClass, false) + ". Steps above the limit are disabled."
                 + "\nEnd height works in every mode. Shortcuts: [ ] and Page Up / Page Down";
        }

        private void DrawSmoothingTab(TF2RoadToolSystem tool, out string title, out string text)
        {
            bool terrain = tool.Mode == HeightMode.Terrain;
            if (Item("·", "Light", terrain && tool.Smoothing == SmoothingLevel.Light)) { tool.SetSmoothing(SmoothingLevel.Light); tool.SetMode(HeightMode.Terrain); }
            if (Item("··", "Medium", terrain && tool.Smoothing == SmoothingLevel.Medium)) { tool.SetSmoothing(SmoothingLevel.Medium); tool.SetMode(HeightMode.Terrain); }
            if (Item("···", "Strong", terrain && tool.Smoothing == SmoothingLevel.Strong)) { tool.SetSmoothing(SmoothingLevel.Strong); tool.SetMode(HeightMode.Terrain); }

            title = "Smoothing: " + SmoothingName(tool.Smoothing);
            text = tool.Smoothing == SmoothingLevel.Light ? "The road stays close to the terrain and follows small bumps."
                 : tool.Smoothing == SmoothingLevel.Medium ? "Smooths out small bumps, follows larger hills."
                 : "The smoothest road; it departs further from the terrain.";
            text += terrain ? "\nShortcut: Shift + Tab" : "\nOnly used in Terrain mode; selecting a level switches to Terrain mode.";
        }

        private void DrawShapeTab(TF2RoadToolSystem tool, out string title, out string text)
        {
            if (Item("—", "Straight", tool.Shape == ConnectShape.Straight)) tool.SetShape(ConnectShape.Straight);
            if (Item("S", "Smooth", tool.Shape == ConnectShape.Smooth)) tool.SetShape(ConnectShape.Smooth);
            if (Item("(", "Arc", tool.Shape == ConnectShape.Arc)) tool.SetShape(ConnectShape.Arc);
            if (Item("@", "Spiral", tool.Shape == ConnectShape.Spiral)) tool.SetShape(ConnectShape.Spiral);

            if (tool.Shape == ConnectShape.Spiral)
            {
                GUILayout.Space(8f);
                GUILayout.Label("Spiral radius", m_Small);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("−5", m_SmallBtn, GUILayout.Height(28f))) tool.AdjustSpiralRadius(-5f);
                GUILayout.Label(ToolStatus.spiralRadius.ToString("0", Inv) + " m", m_Value, GUILayout.Height(28f));
                if (GUILayout.Button("+5", m_SmallBtn, GUILayout.Height(28f))) tool.AdjustSpiralRadius(5f);
                if (GUILayout.Button(ToolStatus.spiralFlip ? "Side: flipped" : "Side: auto", m_SmallBtn, GUILayout.Height(28f))) tool.FlipSpiral();
                GUILayout.EndHorizontal();
            }

            title = ShapeName(tool.Shape);
            text = tool.Shape == ConnectShape.Straight ? "A straight road between the two points."
                 : tool.Shape == ConnectShape.Smooth ? "A smooth curve that continues the roads ending at both points."
                 : tool.Shape == ConnectShape.Arc ? "A circular arc that continues the road at the first point."
                 : "Loops as many times as the slope limit needs to climb or descend to the second point, then heads straight to it. "
                   + "Use it when the two points are too steep to connect directly.";
            text += "\nPick the first point, then the second. Both keep their height.\nShortcut: Tab";
        }

        private void DrawConnectHeightTab(TF2RoadToolSystem tool, out string title, out string text)
        {
            if (Item("/", "Even slope", tool.ConnectHeight == EvenHeight.Even)) tool.SetConnectHeight(EvenHeight.Even);
            if (Item("~", "Follow terrain", tool.ConnectHeight == EvenHeight.Terrain)) tool.SetConnectHeight(EvenHeight.Terrain);
            if (tool.ConnectHeight == EvenHeight.Terrain) SmoothingRow(tool);

            title = tool.ConnectHeight == EvenHeight.Even ? "Even slope" : "Follow terrain";
            text = tool.ConnectHeight == EvenHeight.Even
                ? "One constant grade from the first point to the second."
                : "Follows the smoothed terrain; both ends stay at their heights.";
            text += "\nShortcut: Shift + Tab";
        }

        private void DrawSlopeHeightTab(TF2RoadToolSystem tool, out string title, out string text)
        {
            if (Item("/", "Even slope", tool.SlopeHeight == EvenHeight.Even)) tool.SetSlopeHeight(EvenHeight.Even);
            if (Item("—", "Level", tool.SlopeHeight == EvenHeight.Level)) tool.SetSlopeHeight(EvenHeight.Level);
            if (Item("~", "Follow terrain", tool.SlopeHeight == EvenHeight.Terrain)) tool.SetSlopeHeight(EvenHeight.Terrain);
            if (tool.SlopeHeight == EvenHeight.Terrain) SmoothingRow(tool);

            GUILayout.Space(6f);
            bool prev = GUI.enabled;
            GUI.enabled = prev && ToolStatus.canUndo;
            if (GUILayout.Button("Undo last slope", m_SmallBtn, GUILayout.Height(28f))) tool.RequestUndo();
            GUI.enabled = prev;

            title = tool.SlopeHeight == EvenHeight.Even ? "Even slope" : tool.SlopeHeight == EvenHeight.Level ? "Level" : "Follow terrain";
            text = tool.SlopeHeight == EvenHeight.Even ? "Removes bumps: one constant grade from the first node to the last."
                 : tool.SlopeHeight == EvenHeight.Level ? "Every node takes the height of the first node (the last node moves too)."
                 : "Follows the smoothed terrain between the two nodes.";
            text += "\nPick the first node, then the last; the road between them is highlighted. Side roads keep their shape and follow at the junctions."
                  + "\nShortcut: Tab";
        }

        private void SmoothingRow(TF2RoadToolSystem tool)
        {
            GUILayout.BeginHorizontal();
            foreach (SmoothingLevel l in new[] { SmoothingLevel.Light, SmoothingLevel.Medium, SmoothingLevel.Strong })
                if (GUILayout.Button(SmoothingName(l), tool.Smoothing == l ? m_TabSelStyle : m_SmallBtn, GUILayout.Height(28f))) tool.SetSmoothing(l);
            GUILayout.EndHorizontal();
        }

        private void DrawKeysTab(TF2RoadToolSystem tool, out string title, out string text)
        {
            string keys;
            switch (tool.Tool)
            {
                case ToolMode.Connect:
                    keys = "Left click\tPick first / second point\n" +
                           "Right click\tCancel the pick\n" +
                           "Tab\tShape\n" +
                           "Shift + Tab\tHeight: even / terrain\n" +
                           "PgUp / PgDn\tSpiral radius ±5 m\n";
                    break;
                case ToolMode.Slope:
                    keys = "Left click\tPick first / last node, apply\n" +
                           "Right click\tCancel the pick\n" +
                           "Tab\tEven / level / terrain\n" +
                           "Ctrl + Z\tUndo the last slope\n";
                    break;
                default:
                    keys = "Left click\tStart / build\n" +
                           "Right click\tEnd the chain\n" +
                           "Tab\tHeight mode\n" +
                           "Shift + Tab\tSmoothing\n" +
                           "[  ]\tGrade step\n" +
                           "PgUp / PgDn\tEnd height ±1 m (Shift: ±0.25 m)\n" +
                           "Ctrl (hold)\tRelease tangent lock\n";
                    break;
            }
            GUILayout.Label(keys + "Shift + R\tToggle Road+", m_Desc);
            title = "Shortcuts";
            text = "Works with Road Builder roads, asset roads, tracks and paths. The tool stays active when you pick another road from the menu.";
        }

        private void DrawStatus()
        {
            if (!ToolStatus.hasStart && string.IsNullOrEmpty(ToolStatus.message))
            {
                GUILayout.Label(ToolStatus.tool == ToolMode.Draw ? "Left click on the map to start. Start on a road end to continue it."
                    : ToolStatus.tool == ToolMode.Connect ? "Pick the first road end or road point."
                    : "Pick the first node of the road stretch.", m_Desc);
                return;
            }
            if (ToolStatus.hasPlan)
            {
                GUILayout.Label("Length " + ToolStatus.length.ToString("0", Inv) + " m   ·   Steepest " + Percent(ToolStatus.maxSlope, false)
                                + " (limit " + Percent(ToolStatus.slopeLimit, false) + ")", m_Desc);
            }
            if (!string.IsNullOrEmpty(ToolStatus.message))
                GUILayout.Label(ToolStatus.message, ToolStatus.canBuild ? m_Ok : m_Warn);
        }

        // ---- Parts ----- ----------------------------------------------------------------------------------------

        private void ToolButton(TF2RoadToolSystem tool, ToolMode mode, string label)
        {
            if (GUILayout.Button(label, tool.Tool == mode ? m_ToolBtnSel : m_ToolBtn, GUILayout.Height(32f))) tool.SetTool(mode);
        }

        private void TabButton(Tab tab, string label)
        {
            if (GUILayout.Button(label, m_Tab == tab ? m_TabSelStyle : m_TabStyle, GUILayout.Height(28f))) m_Tab = tab;
        }

        /// <summary>List row with an icon. Returns true when clicked.</summary>
        private bool Item(string icon, string label, bool selected, bool enabled = true)
        {
            bool prev = GUI.enabled;
            GUI.enabled = prev && enabled;
            bool clicked = GUILayout.Button(label, selected ? m_ItemSel : m_Item, GUILayout.Height(38f));
            Rect r = GUILayoutUtility.GetLastRect();
            GUI.Label(new Rect(r.x + 8f, r.y + 7f, 24f, 24f), icon, selected ? m_IconSel : m_Icon);
            GUI.enabled = prev;
            GUILayout.Space(4f);
            return clicked && enabled;
        }

        private void EnsureStyles()
        {
            if (m_StylesReady) return;
            m_StylesReady = true;

            Color text = Hex(0xDFE7EF), dim = Hex(0x9FB0C3), white = Color.white;

            m_Panel = Box(Rounded(Hex(0x1C2836, 0.96f), Hex(0x33465C), 8), 14, 14, 12, 14);

            m_Header = Label(15, white, FontStyle.Bold, TextAnchor.MiddleCenter);
            m_Header.wordWrap = false; // "ROAD+" must not break at the '+'
            m_Sub = Label(12, dim, FontStyle.Normal, TextAnchor.MiddleCenter);
            m_Close = Label(16, dim, FontStyle.Bold, TextAnchor.MiddleCenter);
            m_Close.hover.textColor = white;

            m_TabStyle = Button(null, null, dim, 13, TextAnchor.MiddleCenter, 10, 10);
            m_TabStyle.hover.textColor = white;
            m_TabSelStyle = Button(Rounded(Hex(0x8FC7F0), Hex(0x8FC7F0), 5), null, Hex(0x10202E), 13, TextAnchor.MiddleCenter, 10, 10);
            m_TabSelStyle.fontStyle = FontStyle.Bold;

            m_Item = Button(Rounded(Hex(0x253447), Hex(0x3A4F66), 5), Rounded(Hex(0x2C3F55), Hex(0x4A6582), 5), text, 13, TextAnchor.MiddleLeft, 44, 10);
            m_ItemSel = Button(Rounded(Hex(0x2F6FB3), Hex(0x5AA2E6), 5), Rounded(Hex(0x377BC2), Hex(0x6BB0EE), 5), white, 13, TextAnchor.MiddleLeft, 44, 10);
            m_ItemSel.fontStyle = FontStyle.Bold;

            m_Icon = Box(Rounded(Hex(0x18222E), Hex(0x3A4F66), 4), 0, 0, 0, 0);
            m_Icon.alignment = TextAnchor.MiddleCenter; m_Icon.fontSize = 14; m_Icon.fontStyle = FontStyle.Bold;
            m_Icon.normal.textColor = Hex(0xCFE3F5);
            m_IconSel = new GUIStyle(m_Icon);
            m_IconSel.normal.background = Rounded(Hex(0x245A94), Hex(0x8FC7F0), 4);
            m_IconSel.normal.textColor = white;

            m_DescTitle = Label(13, white, FontStyle.Bold, TextAnchor.UpperLeft);
            m_Desc = Label(12, Hex(0xB8C6D4), FontStyle.Normal, TextAnchor.UpperLeft);
            m_Desc.wordWrap = true;
            m_Ok = Label(12, Hex(0x7FD38A), FontStyle.Bold, TextAnchor.UpperLeft); m_Ok.wordWrap = true;
            m_Warn = Label(12, Hex(0xFF9A6B), FontStyle.Bold, TextAnchor.UpperLeft); m_Warn.wordWrap = true;
            m_Small = Label(12, dim, FontStyle.Bold, TextAnchor.UpperLeft);
            m_Value = Label(13, white, FontStyle.Bold, TextAnchor.MiddleCenter);
            m_Value.fixedWidth = 64f;

            m_SmallBtn = Button(Rounded(Hex(0x253447), Hex(0x3A4F66), 5), Rounded(Hex(0x2C3F55), Hex(0x4A6582), 5), text, 12, TextAnchor.MiddleCenter, 6, 6);
            m_Footer = Button(Rounded(Hex(0x233142), Hex(0x4A6079), 6), Rounded(Hex(0x2B3C50), Hex(0x6B84A0), 6), text, 13, TextAnchor.MiddleCenter, 10, 10);

            m_ToolBtn = Button(Rounded(Hex(0x253447), Hex(0x3A4F66), 6), Rounded(Hex(0x2C3F55), Hex(0x4A6582), 6), text, 13, TextAnchor.MiddleCenter, 8, 8);
            m_ToolBtnSel = Button(Rounded(Hex(0xE07B24), Hex(0xF5A35A), 6), Rounded(Hex(0xE88A38), Hex(0xF7B272), 6), white, 13, TextAnchor.MiddleCenter, 8, 8);
            m_ToolBtnSel.fontStyle = FontStyle.Bold;

            m_Divider = new GUIStyle();
            m_Divider.normal.background = Solid(Hex(0x33465C));
        }

        // ---- Helpers --- -------------------------------------------------------------------------------------

        private static GUIStyle Label(int size, Color color, FontStyle style, TextAnchor anchor)
        {
            var s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = style, alignment = anchor, richText = false };
            s.normal.textColor = color;
            s.padding = new RectOffset(0, 0, 2, 2);
            return s;
        }

        private static GUIStyle Box(Texture2D bg, int left, int right, int top, int bottom)
        {
            var s = new GUIStyle(GUI.skin.box) { padding = new RectOffset(left, right, top, bottom), border = new RectOffset(8, 8, 8, 8), margin = new RectOffset(0, 0, 0, 0) };
            s.normal.background = bg;
            return s;
        }

        private static GUIStyle Button(Texture2D normal, Texture2D hover, Color textColor, int size, TextAnchor anchor, int padLeft, int padRight)
        {
            var s = new GUIStyle(GUI.skin.button)
            {
                fontSize = size,
                alignment = anchor,
                padding = new RectOffset(padLeft, padRight, 4, 4),
                margin = new RectOffset(2, 2, 2, 2),
                border = new RectOffset(6, 6, 6, 6),
            };
            s.normal.background = normal;
            s.hover.background = hover ?? normal;
            s.active.background = hover ?? normal;
            s.focused.background = normal;
            s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = textColor;
            return s;
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        /// <summary>Rounded, bordered 9-slice texture.</summary>
        private static Texture2D Rounded(Color fill, Color border, int radius)
        {
            int size = radius * 2 + 4;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float cx = Mathf.Clamp(px, radius, size - radius), cy = Mathf.Clamp(py, radius, size - radius);
                    float d = Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy));
                    bool corner = d > 0.001f;
                    float edge = corner ? radius - d : Mathf.Min(Mathf.Min(px, py), Mathf.Min(size - px, size - py));
                    float coverage = Mathf.Clamp01(edge + 0.5f);
                    float borderMix = Mathf.Clamp01(1.5f - edge);
                    Color c = Color.Lerp(fill, border, borderMix);
                    c.a *= coverage;
                    t.SetPixel(x, y, c);
                }
            }
            t.Apply();
            return t;
        }

        private static Color Hex(int rgb, float alpha = 1f) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);

        private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        private static string ClassSuffix(RoadClass c) =>
            c == RoadClass.Highway ? "  ·  highway" : c == RoadClass.Ramp ? "  ·  highway ramp" : c == RoadClass.Rail ? "  ·  rail" : "";

        private static string ShapeName(ConnectShape s) =>
            s == ConnectShape.Straight ? "Straight" : s == ConnectShape.Smooth ? "Smooth" : s == ConnectShape.Arc ? "Arc" : "Spiral";

        private static string ModeName(HeightMode m) =>
            m == HeightMode.Terrain ? "Terrain" : m == HeightMode.Graded ? "Graded" : "Flat";

        private static string SmoothingName(SmoothingLevel s) =>
            s == SmoothingLevel.Light ? "Light" : s == SmoothingLevel.Medium ? "Medium" : "Strong";

        private static string Percent(double v, bool signed)
        {
            string sign = v < 0 ? "-" : (signed && v > 0 ? "+" : "");
            return sign + System.Math.Abs(v * 100).ToString("0.#", Inv) + "%";
        }
    }
}
