using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace SignsPlus.Rendering
{
    /// <summary>
    /// Standart trafik işaretleri (tools/make_std_signs.py üretir, biçim SPST1, DLL'e gömülü).
    /// Her işaret 100×100'lük kutuda: dış hat (levhanın gerçek şekli), renkli katmanlar ve isteğe bağlı yazı alanı.
    /// Levha yazısı (spec): "hiz:70" gibi — ad, varsa iki noktadan sonra değer. Boşsa varsayılan işaret.
    /// </summary>
    public static class StandardSigns
    {
        public const string DefaultId = "dikkat";

        public sealed class Layer
        {
            public Color32 Color;
            public readonly List<Vector2[]> Contours = new List<Vector2[]>();
        }

        public sealed class Sign
        {
            public string Id, Group;
            public Vector2[] Outline;          // 100'lük kutuda, y aşağı
            public readonly List<Layer> Layers = new List<Layer>();
            public bool HasText, TextIsParam;
            public float TextX, TextY, TextW, TextH;   // 100'lük kutuda
            public Color32 TextColor;
            public string DefaultText;
        }

        private static Dictionary<string, Sign> s_Signs;

        public static IReadOnlyDictionary<string, Sign> All { get { Ensure(); return s_Signs; } }

        public static bool TryGet(string id, out Sign s)
        {
            Ensure();
            return s_Signs.TryGetValue(id ?? string.Empty, out s);
        }

        /// <summary>"hiz:70" → (hiz, 70). Bilinmeyen ya da boş ad varsayılan işarete düşer.</summary>
        public static Sign Resolve(string spec, out string param)
        {
            Ensure();
            param = null;
            string id = (spec ?? string.Empty).Trim();
            int colon = id.IndexOf(':');
            if (colon >= 0) { param = id.Substring(colon + 1).Trim(); id = id.Substring(0, colon).Trim(); }
            if (!s_Signs.TryGetValue(id, out var s)) s = s_Signs[DefaultId];
            return s;
        }

        private static void Ensure()
        {
            if (s_Signs != null) return;
            s_Signs = new Dictionary<string, Sign>(StringComparer.OrdinalIgnoreCase);
            string text;
            using (var st = typeof(StandardSigns).Assembly.GetManifestResourceStream("SignsPlus.std_signs.txt"))
            using (var r = new StreamReader(st)) text = r.ReadToEnd();
            Parse(text);
        }

        private static Vector2[] Points(string[] p, int start)
        {
            var inv = CultureInfo.InvariantCulture;
            var pts = new Vector2[(p.Length - start) / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2(float.Parse(p[start + 2 * i], inv), float.Parse(p[start + 2 * i + 1], inv));
            return pts;
        }

        private static Color32 Hex(string c)
        {
            if (c.Length != 7 || c[0] != '#') return new Color32(255, 0, 255, 255);
            return new Color32(byte.Parse(c.Substring(1, 2), NumberStyles.HexNumber), byte.Parse(c.Substring(3, 2), NumberStyles.HexNumber), byte.Parse(c.Substring(5, 2), NumberStyles.HexNumber), 255);
        }

        private static void Parse(string text)
        {
            var inv = CultureInfo.InvariantCulture;
            Sign cur = null;
            Layer layer = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length < 2 || line[1] != ' ') continue;   // "SPST1" başlığı atlanır
                var p = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                switch (line[0])
                {
                    case 'S':
                        cur = new Sign { Id = p[1], Group = p.Length > 2 ? p[2] : string.Empty };
                        s_Signs[cur.Id] = cur;
                        layer = null;
                        break;
                    case 'O':
                        if (cur != null) cur.Outline = Points(p, 1);
                        break;
                    case 'L':
                        if (cur == null) break;
                        layer = new Layer { Color = Hex(p[1]) };
                        cur.Layers.Add(layer);
                        break;
                    case 'C':
                        if (layer == null) break;
                        var pts = Points(p, 1);
                        if (pts.Length >= 3) layer.Contours.Add(pts);
                        break;
                    case 'T':
                        if (cur == null || p.Length < 8) break;
                        cur.HasText = true;
                        cur.TextX = float.Parse(p[1], inv); cur.TextY = float.Parse(p[2], inv); cur.TextW = float.Parse(p[3], inv); cur.TextH = float.Parse(p[4], inv);
                        cur.TextColor = Hex(p[5]);
                        cur.TextIsParam = p[6] == "param";
                        cur.DefaultText = p[7].Replace('_', ' ');
                        break;
                }
            }
        }

        /// <summary>İşaretin kare dokusu (kenar = sizeM metre). Dış hattın dışı saydam.</summary>
        public static Texture2D Compose(string spec, float sizeM, int pixelsPerMeter, string name)
        {
            var sign = Resolve(spec, out var param);
            int N = Math.Max(32, Math.Min(1024, (int)Math.Round(sizeM * pixelsPerMeter * 1.6f)));   // küçük işarette biraz daha keskin
            float k = N / 100f;
            var r = new SignRasterizer(N, N);
            // Kenardaki yumuşatma, dış katmanın rengine karışsın (açık renkli saçak olmasın)
            var edge = sign.Layers.Count > 0 ? sign.Layers[0].Color : new Color32(242, 242, 236, 255);
            r.Clear(new Color32(edge.r, edge.g, edge.b, 0));
            foreach (var layer in sign.Layers)
            {
                var list = new List<Vector2[]>(layer.Contours.Count);
                foreach (var c in layer.Contours)
                {
                    var t = new Vector2[c.Length];
                    for (int i = 0; i < c.Length; i++) t[i] = new Vector2(c[i].x * k, c[i].y * k);
                    list.Add(t);
                }
                r.FillPaths(list, layer.Color);
            }
            if (sign.HasText)
            {
                string text = sign.TextIsParam && !string.IsNullOrEmpty(param) ? param : sign.DefaultText;
                if (text.Length > 6) text = text.Substring(0, 6);
                var font = FontAtlas.Bold;
                float cap = sign.TextH * 0.82f * k;
                float scale = cap / font.CapHeight;
                float w = font.Measure(text, 0f) * scale;
                float maxW = sign.TextW * k;
                if (w > maxW) { scale *= maxW / w; cap *= maxW / w; w = maxW; }
                float x = (sign.TextX + sign.TextW * 0.5f) * k - w * 0.5f;
                float baseline = (sign.TextY + sign.TextH * 0.5f) * k + cap * 0.5f;
                r.DrawText(font, text, x, baseline, scale, 0f, sign.TextColor);
            }
            return r.ToTexture(name);
        }
    }
}
