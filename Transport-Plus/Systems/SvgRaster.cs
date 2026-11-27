using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace TransportPlus.Systems
{
    /// <summary>
    /// Haritanın SVG'sini PNG'ye çevirir. Oyunun arayüz motoru (Gameface) canvas'tan piksel okuyamıyor
    /// (toDataURL, getImageData, toBlob yok), bu yüzden PNG burada, oyunla gelen System.Drawing (GDI+) ile çizilir.
    /// Yalnızca transitMap.ts'nin ürettiği düz öğeleri bilir: svg, rect, path, line, circle, text.
    /// System.Drawing yüklenemezse çağıran SVG kaydeder; bu sınıfa erişim yalnızca TryRender üzerinden olur.
    /// </summary>
    internal static class SvgRaster
    {
        /// <summary>Uzun kenar ve toplam piksel sınırı: büyük şehirlerde bile birkaç yüz MB'ı geçmesin.</summary>
        private const int kMaxSide = 8000;
        private const long kMaxPixels = 36_000_000;

        /// <summary>SVG'yi PNG olarak path'e yazar. Hata olursa false döner ve nedeni error'a yazılır.</summary>
        public static bool TryRender(string svg, string path, out int width, out int height, out string error)
        {
            width = height = 0;
            error = null;
            try
            {
                // System.Drawing türleri yalnızca Render'ın içinde: derleme/yükleme hatası burada yakalanır
                Render(svg, path, out width, out height);
                return true;
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        private static readonly Regex sTag = new Regex(@"<(svg|rect|path|line|circle|text)\b([^>]*?)(/?)>", RegexOptions.Compiled);
        private static readonly Regex sAttr = new Regex(@"([\w:-]+)=""([^""]*)""", RegexOptions.Compiled);
        private static readonly Regex sTransform = new Regex(@"(translate|rotate|scale)\(([^)]*)\)", RegexOptions.Compiled);
        private static readonly Regex sPathTok = new Regex(@"[MmLlHhVvCcQqZz]|-?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?", RegexOptions.Compiled);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Render(string svg, string path, out int width, out int height)
        {
            Match root = sTag.Match(svg);
            if (!root.Success || root.Groups[1].Value != "svg") throw new FormatException("not an SVG document");
            Dictionary<string, string> ra = Attrs(root.Groups[2].Value);
            float[] vb = Nums(Get(ra, "viewBox"));
            if (vb.Length != 4 || vb[2] <= 0 || vb[3] <= 0) throw new FormatException("missing viewBox");
            float w = Num(Get(ra, "width"), vb[2]), h = Num(Get(ra, "height"), vb[3]);
            float k = Math.Min(1f, Math.Min(kMaxSide / Math.Max(w, h), (float)Math.Sqrt(kMaxPixels / (double)(w * h))));
            width = Math.Max(1, (int)Math.Round(w * k));
            height = Math.Max(1, (int)Math.Round(h * k));

            using (var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.Clear(Color.Transparent);
                float s = Math.Min(width / vb[2], height / vb[3]);
                var baseM = new Matrix();
                baseM.Translate((width - vb[2] * s) / 2f, (height - vb[3] * s) / 2f);
                baseM.Scale(s, s);
                baseM.Translate(-vb[0], -vb[1]);

                var fonts = new FontCache();
                try
                {
                    int pos = root.Index + root.Length;
                    for (Match m = sTag.Match(svg, pos); m.Success; m = sTag.Match(svg, pos))
                    {
                        pos = m.Index + m.Length;
                        string tag = m.Groups[1].Value;
                        Dictionary<string, string> a = Attrs(m.Groups[2].Value);
                        string text = null;
                        if (tag == "text" && m.Groups[3].Value != "/")
                        {
                            int end = svg.IndexOf("</text>", pos, StringComparison.Ordinal);
                            if (end < 0) break;
                            text = Unescape(svg.Substring(pos, end - pos));
                            pos = end + 7;
                        }
                        if (tag == "svg") continue;

                        using (Matrix mt = baseM.Clone())
                        {
                            ApplyTransform(mt, Get(a, "transform"));
                            g.Transform = mt;
                            float alpha = Num(Get(a, "opacity"), 1f);
                            switch (tag)
                            {
                                case "rect": Paint(g, a, RectPath(a), alpha); break;
                                case "circle":
                                {
                                    float cx = Num(Get(a, "cx"), 0), cy = Num(Get(a, "cy"), 0), r = Num(Get(a, "r"), 0);
                                    var gp = new GraphicsPath();
                                    if (r > 0) gp.AddEllipse(cx - r, cy - r, 2 * r, 2 * r);
                                    Paint(g, a, gp, alpha);
                                    break;
                                }
                                case "line":
                                {
                                    var gp = new GraphicsPath();
                                    gp.AddLine(Num(Get(a, "x1"), 0), Num(Get(a, "y1"), 0), Num(Get(a, "x2"), 0), Num(Get(a, "y2"), 0));
                                    a["fill"] = "none";
                                    Paint(g, a, gp, alpha);
                                    break;
                                }
                                case "path": Paint(g, a, TracePath(Get(a, "d") ?? ""), alpha); break;
                                case "text": DrawText(g, a, text ?? "", fonts, alpha); break;
                            }
                        }
                    }
                }
                finally
                {
                    fonts.Dispose();
                    baseM.Dispose();
                }
                bmp.Save(path, ImageFormat.Png);
            }
        }

        // ---------------------------------------------------------------- çizim

        private static void Paint(Graphics g, Dictionary<string, string> a, GraphicsPath gp, float alpha)
        {
            using (gp)
            {
                Color fill;
                if (TryColor(Get(a, "fill") ?? "#000000", alpha * Num(Get(a, "fill-opacity"), 1f), out fill))
                    using (var b = new SolidBrush(fill)) g.FillPath(b, gp);
                Color stroke;
                if (TryColor(Get(a, "stroke"), alpha * Num(Get(a, "stroke-opacity"), 1f), out stroke))
                    using (Pen p = MakePen(a, stroke)) g.DrawPath(p, gp);
            }
        }

        private static Pen MakePen(Dictionary<string, string> a, Color c)
        {
            float w = Math.Max(0.01f, Num(Get(a, "stroke-width"), 1f));
            var p = new Pen(c, w);
            switch (Get(a, "stroke-linecap"))
            {
                case "round": p.StartCap = p.EndCap = LineCap.Round; p.DashCap = DashCap.Round; break;
                case "square": p.StartCap = p.EndCap = LineCap.Square; break;
                default: p.StartCap = p.EndCap = LineCap.Flat; break;
            }
            switch (Get(a, "stroke-linejoin"))
            {
                case "round": p.LineJoin = LineJoin.Round; break;
                case "bevel": p.LineJoin = LineJoin.Bevel; break;
                default: p.LineJoin = LineJoin.Miter; p.MiterLimit = 4f; break;
            }
            float[] dash = Nums(Get(a, "stroke-dasharray"));
            if (dash.Length > 0)
            {
                // GDI+ kesik çizgi uzunluklarını kalem kalınlığı cinsinden ister
                if (dash.Length % 2 == 1) { var d2 = new float[dash.Length * 2]; dash.CopyTo(d2, 0); dash.CopyTo(d2, dash.Length); dash = d2; }
                for (int i = 0; i < dash.Length; i++) dash[i] = Math.Max(0.01f, dash[i] / w);
                p.DashPattern = dash;
            }
            return p;
        }

        private static GraphicsPath RectPath(Dictionary<string, string> a)
        {
            float x = Num(Get(a, "x"), 0), y = Num(Get(a, "y"), 0), w = Num(Get(a, "width"), 0), h = Num(Get(a, "height"), 0);
            float r = Math.Min(Num(Get(a, "rx"), 0), Math.Min(w / 2f, h / 2f));
            var gp = new GraphicsPath();
            if (w <= 0 || h <= 0) return gp;
            if (r <= 0.01f) { gp.AddRectangle(new RectangleF(x, y, w, h)); return gp; }
            float d = 2 * r;
            gp.AddArc(x, y, d, d, 180, 90);
            gp.AddArc(x + w - d, y, d, d, 270, 90);
            gp.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            gp.AddArc(x, y + h - d, d, d, 90, 90);
            gp.CloseFigure();
            return gp;
        }

        private static GraphicsPath TracePath(string d)
        {
            var gp = new GraphicsPath(FillMode.Winding);
            MatchCollection tok = sPathTok.Matches(d);
            int i = 0;
            char cmd = 'M';
            float x = 0, y = 0, sx = 0, sy = 0;
            bool open = false;
            Func<float> n = () => i < tok.Count ? float.Parse(tok[i++].Value, CultureInfo.InvariantCulture) : 0f;
            while (i < tok.Count)
            {
                string t = tok[i].Value;
                if (t.Length == 1 && char.IsLetter(t[0])) { cmd = t[0]; i++; }
                int before = i;
                switch (cmd)
                {
                    case 'M': case 'm':
                    {
                        float nx = n(), ny = n();
                        if (cmd == 'm') { nx += x; ny += y; }
                        gp.StartFigure();
                        x = sx = nx; y = sy = ny; open = true;
                        cmd = cmd == 'm' ? 'l' : 'L';
                        break;
                    }
                    case 'L': case 'l':
                    {
                        float nx = n(), ny = n();
                        if (cmd == 'l') { nx += x; ny += y; }
                        gp.AddLine(x, y, nx, ny); x = nx; y = ny;
                        break;
                    }
                    case 'H': case 'h':
                    {
                        float nx = n(); if (cmd == 'h') nx += x;
                        gp.AddLine(x, y, nx, y); x = nx;
                        break;
                    }
                    case 'V': case 'v':
                    {
                        float ny = n(); if (cmd == 'v') ny += y;
                        gp.AddLine(x, y, x, ny); y = ny;
                        break;
                    }
                    case 'C': case 'c':
                    {
                        float x1 = n(), y1 = n(), x2 = n(), y2 = n(), nx = n(), ny = n();
                        if (cmd == 'c') { x1 += x; y1 += y; x2 += x; y2 += y; nx += x; ny += y; }
                        gp.AddBezier(x, y, x1, y1, x2, y2, nx, ny); x = nx; y = ny;
                        break;
                    }
                    case 'Q': case 'q':
                    {
                        float qx = n(), qy = n(), nx = n(), ny = n();
                        if (cmd == 'q') { qx += x; qy += y; nx += x; ny += y; }
                        // ikinci dereceden eğri → kübik
                        gp.AddBezier(x, y, x + 2f / 3f * (qx - x), y + 2f / 3f * (qy - y), nx + 2f / 3f * (qx - nx), ny + 2f / 3f * (qy - ny), nx, ny);
                        x = nx; y = ny;
                        break;
                    }
                    case 'Z': case 'z':
                        if (open) gp.CloseFigure();
                        x = sx; y = sy; open = false;
                        break;
                }
                // Hiç sayı okunmadıysa (Z ya da eksik veri) sıradaki harf olmayan parçayı atla: sonsuz döngüye girme
                if (i == before && i < tok.Count && !char.IsLetter(tok[i].Value[0])) i++;
            }
            return gp;
        }

        private static void DrawText(Graphics g, Dictionary<string, string> a, string text, FontCache fonts, float alpha)
        {
            if (text.Length == 0) return;
            float size = Num(Get(a, "font-size"), 12f);
            int weight = (int)Num(Get(a, "font-weight"), 400f);
            FontFamily family = fonts.Family(Get(a, "font-family"));
            FontStyle style = weight >= 600 ? FontStyle.Bold : FontStyle.Regular;
            if (Get(a, "font-style") == "italic") style |= FontStyle.Italic;
            if (!family.IsStyleAvailable(style)) style = FontStyle.Regular;

            float x = Num(Get(a, "x"), 0), y = Num(Get(a, "y"), 0);
            // SVG'de y taban çizgisidir; GDI+ yazıyı em kutusunun üstünden yerleştirir
            float ascent = size * family.GetCellAscent(style) / family.GetEmHeight(style);
            string anchor = Get(a, "text-anchor");
            if (anchor == "middle" || anchor == "end")
            {
                float tw = fonts.Measure(text, family, style, size);
                x -= anchor == "middle" ? tw / 2f : tw;
            }
            using (var gp = new GraphicsPath(FillMode.Winding))
            {
                gp.AddString(text, family, (int)style, size, new PointF(x, y - ascent), StringFormat.GenericTypographic);
                Color stroke;
                if (TryColor(Get(a, "stroke"), alpha, out stroke))
                    using (var p = new Pen(stroke, Math.Max(0.01f, Num(Get(a, "stroke-width"), 1f))) { LineJoin = LineJoin.Round })
                        g.DrawPath(p, gp);
                Color fill;
                if (TryColor(Get(a, "fill") ?? "#000000", alpha, out fill))
                    using (var b = new SolidBrush(fill)) g.FillPath(b, gp);
            }
        }

        /// <summary>Yazı tipleri: SVG'deki aile yoksa (Overpass Windows'ta kurulu değil) Segoe UI, o da yoksa Arial.</summary>
        private sealed class FontCache : IDisposable
        {
            private readonly Dictionary<string, FontFamily> m_Families = new Dictionary<string, FontFamily>();
            private readonly List<FontFamily> m_Owned = new List<FontFamily>();
            private readonly Bitmap m_Probe = new Bitmap(1, 1);
            private readonly Graphics m_ProbeG;

            public FontCache() { m_ProbeG = Graphics.FromImage(m_Probe); }

            public FontFamily Family(string css)
            {
                css = css ?? "";
                FontFamily f;
                if (m_Families.TryGetValue(css, out f)) return f;
                var names = new List<string>();
                foreach (string part in css.Split(',')) { string p = part.Trim().Trim('\'', '"'); if (p.Length > 0 && p != "sans-serif" && p != "serif") names.Add(p); }
                names.Add("Segoe UI"); names.Add("Arial");
                foreach (string name in names)
                {
                    try { f = new FontFamily(name); m_Owned.Add(f); break; }
                    catch (ArgumentException) { f = null; }
                }
                if (f == null) f = FontFamily.GenericSansSerif;
                m_Families[css] = f;
                return f;
            }

            public float Measure(string text, FontFamily family, FontStyle style, float size)
            {
                using (var font = new Font(family, size, style, GraphicsUnit.Pixel))
                    return m_ProbeG.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic).Width;
            }

            public void Dispose()
            {
                m_ProbeG.Dispose();
                m_Probe.Dispose();
                foreach (FontFamily f in m_Owned) f.Dispose();
            }
        }

        // ---------------------------------------------------------------- ayrıştırma

        private static void ApplyTransform(Matrix m, string t)
        {
            if (string.IsNullOrEmpty(t)) return;
            foreach (Match tm in sTransform.Matches(t))
            {
                float[] v = Nums(tm.Groups[2].Value);
                switch (tm.Groups[1].Value)
                {
                    case "translate": m.Translate(v.Length > 0 ? v[0] : 0, v.Length > 1 ? v[1] : 0); break;
                    case "rotate": m.Rotate(v.Length > 0 ? v[0] : 0); break;
                    case "scale": m.Scale(v.Length > 0 ? v[0] : 1, v.Length > 1 ? v[1] : (v.Length > 0 ? v[0] : 1)); break;
                }
            }
        }

        private static bool TryColor(string s, float alpha, out Color c)
        {
            c = Color.Empty;
            if (string.IsNullOrEmpty(s) || s == "none" || s == "transparent") return false;
            int a = Clamp255(alpha * 255f);
            if (s[0] == '#')
            {
                string h = s.Substring(1);
                if (h.Length == 3) h = new string(new[] { h[0], h[0], h[1], h[1], h[2], h[2] });
                int rgb;
                if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb)) return false;
                c = Color.FromArgb(a, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
                return true;
            }
            if (s.StartsWith("rgb", StringComparison.Ordinal))
            {
                float[] v = Nums(s.Substring(s.IndexOf('(') + 1).TrimEnd(')'));
                if (v.Length < 3) return false;
                float va = v.Length > 3 ? v[3] : 1f;
                c = Color.FromArgb(Clamp255(alpha * va * 255f), Clamp255(v[0]), Clamp255(v[1]), Clamp255(v[2]));
                return true;
            }
            Color named = Color.FromName(s);
            if (!named.IsKnownColor) return false;
            c = Color.FromArgb(a, named.R, named.G, named.B);
            return true;
        }

        private static int Clamp255(float v) => v < 0 ? 0 : v > 255 ? 255 : (int)Math.Round(v);

        private static Dictionary<string, string> Attrs(string src)
        {
            var d = new Dictionary<string, string>();
            foreach (Match m in sAttr.Matches(src)) d[m.Groups[1].Value] = m.Groups[2].Value;
            return d;
        }

        private static string Get(Dictionary<string, string> a, string key)
        {
            string v;
            return a.TryGetValue(key, out v) ? v : null;
        }

        private static float Num(string s, float fallback)
        {
            float v;
            if (string.IsNullOrEmpty(s)) return fallback;
            s = s.Trim();
            if (s.EndsWith("px", StringComparison.Ordinal)) s = s.Substring(0, s.Length - 2);
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        private static float[] Nums(string s)
        {
            if (string.IsNullOrEmpty(s)) return new float[0];
            var list = new List<float>();
            foreach (string p in s.Split(new[] { ' ', ',', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                float v;
                if (float.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) list.Add(v);
            }
            return list.ToArray();
        }

        private static string Unescape(string s) => s.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&#39;", "'").Replace("&amp;", "&");
    }
}
