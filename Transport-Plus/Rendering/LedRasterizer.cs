using System;
using System.Collections.Generic;
using UnityEngine;

namespace TransportPlus.Rendering
{
    /// <summary>
    /// Basit CPU çizici (Signs+'tan): yuvarlak dikdörtgen, çokgen ve atlas harfleri; hat tabelası için LED nokta deseni.
    /// Tampon sol üstten satır satır tutulur; dokuya aktarırken çevrilir.
    /// </summary>
    public sealed class LedRasterizer
    {
        public readonly int Width, Height;
        private readonly Color32[] m_Px;

        public LedRasterizer(int width, int height)
        {
            Width = width; Height = height;
            m_Px = new Color32[width * height];
        }

        public void Clear(Color32 c)
        {
            for (int i = 0; i < m_Px.Length; i++) m_Px[i] = c;
        }

        private void Blend(int x, int y, Color32 c, float a)
        {
            if (a <= 0f || x < 0 || y < 0 || x >= Width || y >= Height) return;
            if (a > 1f) a = 1f;
            int i = y * Width + x;
            var d = m_Px[i];
            float ca = a * c.a / 255f;
            m_Px[i] = new Color32(
                (byte)(d.r + (c.r - d.r) * ca),
                (byte)(d.g + (c.g - d.g) * ca),
                (byte)(d.b + (c.b - d.b) * ca),
                (byte)Math.Max(d.a, (int)(255 * ca)));
        }

        // Yuvarlak dikdörtgene işaretli uzaklık (içeride negatif). Merkez (cx, cy), yarı boyut (hx, hy).
        private static float SdRoundRect(float px, float py, float cx, float cy, float hx, float hy, float r)
        {
            float qx = Math.Abs(px - cx) - hx + r;
            float qy = Math.Abs(py - cy) - hy + r;
            float ox = Math.Max(qx, 0f), oy = Math.Max(qy, 0f);
            return (float)Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0f) - r;
        }

        public void FillRoundRect(float x, float y, float w, float h, float r, Color32 c)
        {
            if (w <= 0 || h <= 0) return;
            r = Math.Min(r, Math.Min(w, h) * 0.5f);
            float cx = x + w * 0.5f, cy = y + h * 0.5f, hx = w * 0.5f, hy = h * 0.5f;
            int x0 = Math.Max(0, (int)Math.Floor(x)), x1 = Math.Min(Width - 1, (int)Math.Ceiling(x + w));
            int y0 = Math.Max(0, (int)Math.Floor(y)), y1 = Math.Min(Height - 1, (int)Math.Ceiling(y + h));
            for (int py = y0; py <= y1; py++)
                for (int px = x0; px <= x1; px++)
                {
                    float d = SdRoundRect(px + 0.5f, py + 0.5f, cx, cy, hx, hy, r);
                    Blend(px, py, c, 0.5f - d);
                }
        }

        public void StrokeRoundRect(float x, float y, float w, float h, float r, float stroke, Color32 c)
        {
            if (w <= 0 || h <= 0) return;
            r = Math.Min(r, Math.Min(w, h) * 0.5f);
            float cx = x + w * 0.5f, cy = y + h * 0.5f, hx = w * 0.5f, hy = h * 0.5f;
            float ri = Math.Max(0f, r - stroke);
            int x0 = Math.Max(0, (int)Math.Floor(x)), x1 = Math.Min(Width - 1, (int)Math.Ceiling(x + w));
            int y0 = Math.Max(0, (int)Math.Floor(y)), y1 = Math.Min(Height - 1, (int)Math.Ceiling(y + h));
            for (int py = y0; py <= y1; py++)
                for (int px = x0; px <= x1; px++)
                {
                    float fx = px + 0.5f, fy = py + 0.5f;
                    float dOut = SdRoundRect(fx, fy, cx, cy, hx, hy, r);
                    float dIn = SdRoundRect(fx, fy, cx, cy, hx - stroke, hy - stroke, ri);
                    float a = Math.Min(Clamp01(0.5f - dOut), Clamp01(0.5f + dIn));
                    Blend(px, py, c, a);
                }
        }

        /// <summary>Tek çokgen doldurma. Noktalar piksel koordinatında.</summary>
        public void FillPolygon(Vector2[] pts, Color32 c) => FillPaths(new[] { pts }, c);

        private readonly List<float> m_Xs = new List<float>();

        /// <summary>
        /// Çok konturlu çokgen (delikli şekiller), çift-tek kuralı. Tarama çizgisi başına 4 alt satır,
        /// yatayda tam kapsama; kenarlar yumuşak.
        /// </summary>
        public void FillPaths(IList<Vector2[]> paths, Color32 c)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var pts in paths)
                foreach (var p in pts) { minX = Math.Min(minX, p.x); minY = Math.Min(minY, p.y); maxX = Math.Max(maxX, p.x); maxY = Math.Max(maxY, p.y); }
            int x0 = Math.Max(0, (int)Math.Floor(minX)), x1 = Math.Min(Width - 1, (int)Math.Ceiling(maxX));
            int y0 = Math.Max(0, (int)Math.Floor(minY)), y1 = Math.Min(Height - 1, (int)Math.Ceiling(maxY));
            if (x1 < x0 || y1 < y0) return;
            int w = x1 - x0 + 1;
            var cov = new float[w + 1];
            const int S = 4;
            for (int py = y0; py <= y1; py++)
            {
                Array.Clear(cov, 0, cov.Length);
                for (int s = 0; s < S; s++)
                {
                    float sy = py + (s + 0.5f) / S;
                    m_Xs.Clear();
                    foreach (var pts in paths)
                        for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
                        {
                            var a = pts[i]; var b = pts[j];
                            if ((a.y > sy) != (b.y > sy)) m_Xs.Add(a.x + (sy - a.y) * (b.x - a.x) / (b.y - a.y));
                        }
                    if (m_Xs.Count < 2) continue;
                    m_Xs.Sort();
                    for (int k = 0; k + 1 < m_Xs.Count; k += 2) AddSpan(cov, w, m_Xs[k] - x0, m_Xs[k + 1] - x0, 1f / S);
                }
                for (int i = 0; i < w; i++) if (cov[i] > 0f) Blend(x0 + i, py, c, cov[i]);
            }
        }

        private static void AddSpan(float[] cov, int w, float a, float b, float wgt)
        {
            if (a < 0f) a = 0f;
            if (b > w) b = w;
            if (b <= a) return;
            int ia = (int)a, ib = (int)b;
            if (ia == ib) { cov[ia] += (b - a) * wgt; return; }
            cov[ia] += (ia + 1 - a) * wgt;
            for (int i = ia + 1; i < ib; i++) cov[i] += wgt;
            if (ib < w) cov[ib] += (b - ib) * wgt;
        }

        /// <summary>
        /// Metni taban çizgisi (x, baseline) noktasından başlayarak yazar. scale = hedef piksel / atlas pikseli.
        /// </summary>
        public void DrawText(FontAtlas font, string text, float x, float baseline, float scale, float trackingEm, Color32 c)
        {
            int ss = scale < 0.6f ? 2 : 1;   // küçültürken titremeyi azaltmak için alt örnekleme
            float pen = x;
            for (int i = 0; i < text.Length; i++)
            {
                if (!font.TryGet(text[i], out var g) && !font.TryGet('?', out g)) continue;
                if (g.W > 0 && g.H > 0)
                {
                    float gx = pen + g.XOff * scale, gy = baseline + g.YOff * scale;
                    int tx0 = (int)Math.Floor(gx), ty0 = (int)Math.Floor(gy);
                    int tx1 = (int)Math.Ceiling(gx + g.W * scale), ty1 = (int)Math.Ceiling(gy + g.H * scale);
                    for (int ty = ty0; ty <= ty1; ty++)
                        for (int tx = tx0; tx <= tx1; tx++)
                        {
                            float a = 0f;
                            for (int sy = 0; sy < ss; sy++)
                                for (int sx = 0; sx < ss; sx++)
                                {
                                    float ax = (tx + (sx + 0.5f) / ss - gx) / scale;
                                    float ay = (ty + (sy + 0.5f) / ss - gy) / scale;
                                    if (ax < -1 || ay < -1 || ax > g.W + 1 || ay > g.H + 1) continue;
                                    a += font.Sample(g.X + ax, g.Y + ay);
                                }
                            Blend(tx, ty, c, a / (ss * ss));
                        }
                }
                pen += g.Advance * scale + trackingEm * font.Size * scale;
            }
        }

        /// <summary>
        /// LED nokta deseni: tampon cell piksellik hücrelere bölünür, her hücrenin ortasındaki yuvarlak nokta yanar,
        /// aradaki boşluk karartılır (yazı uzaktan düz, yakından noktalı görünür). Arka plan rengine dokunmaz.
        /// </summary>
        public void LedDots(int cell, Color32 background, float gapLight)
        {
            float r = cell * 0.42f;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int i = y * Width + x;
                    var p = m_Px[i];
                    if (p.r == background.r && p.g == background.g && p.b == background.b) continue;
                    float dx = (x % cell) + 0.5f - cell * 0.5f, dy = (y % cell) + 0.5f - cell * 0.5f;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy);
                    float k = Clamp01(r + 0.5f - d);
                    float m = gapLight + (1f - gapLight) * k;
                    m_Px[i] = new Color32(
                        (byte)(background.r + (p.r - background.r) * m),
                        (byte)(background.g + (p.g - background.g) * m),
                        (byte)(background.b + (p.b - background.b) * m), 255);
                }
        }

        /// <summary>Metnin bu yazı tipinde, verilen ölçekte piksel genişliği.</summary>
        public static float TextWidth(FontAtlas font, string text, float scale, float trackingEm) => font.Measure(text, trackingEm) * scale;

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        /// <summary>Tamponu dokuya aktarır (Unity alttan yukarı okur). Mipmap üretilir, CPU kopyası bırakılır.</summary>
        public Texture2D ToTexture(string name)
        {
            var flipped = new Color32[m_Px.Length];
            for (int y = 0; y < Height; y++)
                Array.Copy(m_Px, y * Width, flipped, (Height - 1 - y) * Width, Width);
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, true, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
            };
            tex.SetPixels32(flipped);
            tex.Apply(true, true);
            return tex;
        }
    }
}
