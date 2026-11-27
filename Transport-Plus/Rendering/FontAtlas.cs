using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace TransportPlus.Rendering
{
    /// <summary>
    /// DLL'e gömülü yazı atlası (Signs+ ile aynı Overpass 700 atlası, OFL lisanslı; Resources/OFL-Overpass.txt).
    /// Harfler CPU'da tabela dokusuna kopyalanır, bu yüzden atlasın yalnızca alfa kanalı bellekte tutulur.
    /// </summary>
    public sealed class FontAtlas
    {
        public struct Glyph
        {
            public int X, Y, W, H;      // atlasta, sol üstten
            public float XOff, YOff;    // taban çizgisine göre harfin sol üst köşesi (YOff negatif = yukarıda)
            public float Advance;
        }

        public readonly int Size;       // atlas yazı boyutu (px)
        public readonly int Ascent, Descent;
        public readonly int Width, Height;
        private readonly byte[] m_Alpha;  // sol üstten satır satır
        private readonly Dictionary<int, Glyph> m_Glyphs = new Dictionary<int, Glyph>();

        /// <summary>Büyük harf yüksekliği (atlas px). Yazı boyutu buna göre hesaplanır.</summary>
        public readonly float CapHeight;

        private FontAtlas(string metrics, byte[] png)
        {
            var lines = metrics.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var head = lines[0].Trim().Split(' ');
            if (head[0] != "SPFA1") throw new InvalidDataException("Yazı atlası biçimi tanınmadı");
            Size = int.Parse(head[1], CultureInfo.InvariantCulture);
            Ascent = int.Parse(head[2], CultureInfo.InvariantCulture);
            Descent = int.Parse(head[3], CultureInfo.InvariantCulture);
            Width = int.Parse(head[4], CultureInfo.InvariantCulture);
            Height = int.Parse(head[5], CultureInfo.InvariantCulture);
            for (int i = 1; i < lines.Length; i++)
            {
                var p = lines[i].Trim().Split(' ');
                if (p.Length < 8) continue;
                m_Glyphs[int.Parse(p[0], CultureInfo.InvariantCulture)] = new Glyph
                {
                    X = int.Parse(p[1], CultureInfo.InvariantCulture),
                    Y = int.Parse(p[2], CultureInfo.InvariantCulture),
                    W = int.Parse(p[3], CultureInfo.InvariantCulture),
                    H = int.Parse(p[4], CultureInfo.InvariantCulture),
                    XOff = float.Parse(p[5], CultureInfo.InvariantCulture),
                    YOff = float.Parse(p[6], CultureInfo.InvariantCulture),
                    Advance = float.Parse(p[7], CultureInfo.InvariantCulture),
                };
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, png, false)) throw new InvalidDataException("Yazı atlası PNG okunamadı");
            if (tex.width != Width || tex.height != Height) throw new InvalidDataException("Yazı atlası boyutu metrik dosyasıyla uyuşmuyor");
            var px = tex.GetPixels32();   // Unity: alt satırdan başlar
            m_Alpha = new byte[Width * Height];
            for (int y = 0; y < Height; y++)
            {
                int src = (Height - 1 - y) * Width;
                int dst = y * Width;
                for (int x = 0; x < Width; x++) m_Alpha[dst + x] = px[src + x].a;
            }
            UnityEngine.Object.Destroy(tex);

            CapHeight = TryGet('H', out var h) ? -h.YOff : Size * 0.7f;
        }

        public bool TryGet(int codepoint, out Glyph g) => m_Glyphs.TryGetValue(codepoint, out g);

        /// <summary>Atlas pikselinin alfa değeri (0-1), çift doğrusal örnekleme. Atlas dışı 0.</summary>
        public float Sample(float x, float y)
        {
            x -= 0.5f; y -= 0.5f;
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float fx = x - x0, fy = y - y0;
            float a00 = At(x0, y0), a10 = At(x0 + 1, y0), a01 = At(x0, y0 + 1), a11 = At(x0 + 1, y0 + 1);
            return ((a00 * (1 - fx) + a10 * fx) * (1 - fy) + (a01 * (1 - fx) + a11 * fx) * fy) / 255f;
        }

        private float At(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return 0f;
            return m_Alpha[y * Width + x];
        }

        /// <summary>Metnin atlas pikseli cinsinden genişliği (harf aralığı dahil).</summary>
        public float Measure(string text, float trackingEm)
        {
            float w = 0f;
            for (int i = 0; i < text.Length; i++)
            {
                if (TryGet(text[i], out var g)) w += g.Advance;
                else if (TryGet('?', out var q)) w += q.Advance;
                if (i < text.Length - 1) w += trackingEm * Size;
            }
            return w;
        }

        private static FontAtlas s_Bold;

        /// <summary>Overpass 700: hat tabelaları.</summary>
        public static FontAtlas Bold => s_Bold ?? (s_Bold = Load("font_overpass_bold"));

        private static FontAtlas Load(string name)
        {
            var asm = typeof(FontAtlas).Assembly;
            string metrics;
            using (var s = asm.GetManifestResourceStream("TransportPlus." + name + ".txt"))
            using (var r = new StreamReader(s)) metrics = r.ReadToEnd();
            byte[] png;
            using (var s = asm.GetManifestResourceStream("TransportPlus." + name + ".png"))
            using (var m = new MemoryStream()) { s.CopyTo(m); png = m.ToArray(); }
            return new FontAtlas(metrics, png);
        }
    }
}
