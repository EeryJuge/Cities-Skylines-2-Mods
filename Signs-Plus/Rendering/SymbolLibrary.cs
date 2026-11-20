using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace SignsPlus.Rendering
{
    /// <summary>
    /// DLL'e gömülü sembol çokgenleri (tools/make_symbols.py üretir, biçim SPSY1).
    /// Her sembol 100 birim yüksekliğinde; genişliği Ratio × 100.
    /// </summary>
    public static class SymbolLibrary
    {
        public sealed class Layer
        {
            public bool UseForeground;     // true: bulunduğu yerin yazı rengi
            public Color32 Color;
            public readonly List<Vector2[]> Contours = new List<Vector2[]>();
        }

        public sealed class Symbol
        {
            public string Name;
            public float Ratio = 1f;
            public readonly List<Layer> Layers = new List<Layer>();
        }

        private static Dictionary<string, Symbol> s_Symbols;

        public static IReadOnlyDictionary<string, Symbol> All { get { Ensure(); return s_Symbols; } }

        public static bool TryGet(string name, out Symbol s)
        {
            Ensure();
            return s_Symbols.TryGetValue(name ?? "", out s);
        }

        private static void Ensure()
        {
            if (s_Symbols != null) return;
            s_Symbols = new Dictionary<string, Symbol>(StringComparer.OrdinalIgnoreCase);
            string text;
            using (var st = typeof(SymbolLibrary).Assembly.GetManifestResourceStream("SignsPlus.symbols.txt"))
            using (var r = new StreamReader(st)) text = r.ReadToEnd();
            Parse(text);
        }

        private static void Parse(string text)
        {
            var inv = CultureInfo.InvariantCulture;
            Symbol cur = null;
            Layer layer = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length < 2 || line[1] != ' ') continue;   // "SPSY1" başlığı da atlanır
                switch (line[0])
                {
                    case 'S':
                    {
                        var p = line.Split(' ');
                        cur = new Symbol { Name = p[1], Ratio = p.Length > 2 ? float.Parse(p[2], inv) : 1f };
                        s_Symbols[cur.Name] = cur;
                        layer = null;
                        break;
                    }
                    case 'L':
                    {
                        if (cur == null) break;
                        var c = line.Substring(2).Trim();
                        layer = new Layer { UseForeground = c == "fg" };
                        if (!layer.UseForeground && c.Length == 7 && c[0] == '#')
                            layer.Color = new Color32(
                                byte.Parse(c.Substring(1, 2), NumberStyles.HexNumber),
                                byte.Parse(c.Substring(3, 2), NumberStyles.HexNumber),
                                byte.Parse(c.Substring(5, 2), NumberStyles.HexNumber), 255);
                        cur.Layers.Add(layer);
                        break;
                    }
                    case 'C':
                    {
                        if (layer == null) break;
                        var p = line.Substring(2).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        var pts = new Vector2[p.Length / 2];
                        for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2(float.Parse(p[2 * i], inv), float.Parse(p[2 * i + 1], inv));
                        if (pts.Length >= 3) layer.Contours.Add(pts);
                        break;
                    }
                }
            }
        }

        /// <summary>Sembolü (x, y) sol üst köşesinden başlayarak, h piksel yüksekliğinde çizer.</summary>
        public static void Draw(SignRasterizer r, Symbol s, float x, float y, float h, Color32 fg)
        {
            float k = h / 100f;
            foreach (var layer in s.Layers)
            {
                var list = new List<Vector2[]>(layer.Contours.Count);
                foreach (var c in layer.Contours)
                {
                    var t = new Vector2[c.Length];
                    for (int i = 0; i < c.Length; i++) t[i] = new Vector2(x + c[i].x * k, y + c[i].y * k);
                    list.Add(t);
                }
                r.FillPaths(list, layer.UseForeground ? fg : layer.Color);
            }
        }
    }
}
