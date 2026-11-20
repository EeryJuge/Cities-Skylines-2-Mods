using System;
using System.Collections.Generic;
using System.Globalization;
using SignsPlus.Data;
using UnityEngine;

namespace SignsPlus.Rendering
{
    /// <summary>
    /// Levha yazı biçimini (SignStore'da saklanan "spec") levha görüntüsüne çevirir.
    ///
    /// Satırlar | ile ayrılır. Bir satır:
    ///   [seçenekler]öğeler{>}sağdaki öğeler
    /// Seçenekler (virgülle, isteğe bağlı): kategori etiketi (blok), x0.75 gibi boyut çarpanı, hiza (orta / sag).
    /// Öğeler: düz yazı ve süslü parantezli öğeler
    ///   {s:ucak}      sembol            {s:ucak:k}  kutulu sembol (ters renk kare içinde)
    ///   {n:O-4}       numara kutusu (O/K turuncu, E yeşil, D beyaz; diğerleri ters renk)
    ///   {ok:sag}      satır içi ok
    ///   {>}           sonrası satırın sağ kenarına yaslanır (mesafe, ok, numara)
    /// Tek başına {sag} / {↗} … olan satır: büyük ok satırı (0.0.2 biçimi, aynen geçerli).
    /// Etiketler: otoyol, karayolu, normal, turistik, sinirdisi, kgm (ya da yesil, mavi, beyaz, kahverengi, sari, turuncu).
    /// </summary>
    public static class SignComposer
    {
        // Ölçüler metre ya da büyük harf yüksekliği (cap) katı
        private const float kEdgeInset = 0.025f;     // m — çerçevenin levha kenarından içeri mesafesi
        private const float kBorder = 0.02f;         // m — çerçeve kalınlığı
        private const float kCorner = 0.08f;         // m — levha köşe yarıçapı
        private const float kPadding = 0.10f;        // m — çerçeve ile içerik arası
        private const float kMaxCap = 0.45f;         // m — en büyük harf yüksekliği
        private const float kTracking = 0.02f;       // em
        private const float kLinePitch = 1.75f;      // cap
        private const float kItemGap = 0.4f;         // cap — satır içi öğeler arası
        private const float kRightGap = 1.2f;        // cap — sol ve sağ grup arası en az boşluk
        private const float kBlockPadX = 0.4f;       // cap — blok yatay iç boşluğu
        private const float kBigArrowPitch = 2.3f;   // cap
        private const float kBigArrowSize = 2.0f;    // cap
        private const float kSymbolH = 1.45f;        // cap
        private const float kBoxH = 1.45f;           // cap — numara kutusu ve kutulu sembol
        private const float kBoxTextCap = 0.85f;     // cap — numara kutusundaki yazı
        private const float kInlineArrow = 1.35f;    // cap

        public enum ArrowDir { None, Up, Down, Left, Right, UpRight, UpLeft, DownRight, DownLeft }
        private enum ItemKind { Text, Symbol, Number, Arrow }

        private sealed class Item
        {
            public ItemKind Kind;
            public string Text;
            public SymbolLibrary.Symbol Symbol;
            public bool Boxed;
            public ArrowDir Arrow;
            public float W, H;      // cap birimi
        }

        private sealed class Line
        {
            public bool HasBlock;
            public SignCategory Block;
            public float Size = 1f;
            public int Align;                    // 0 sol, 1 orta, 2 sağ
            public float SpanX, SpanW;           // satırın hizalandığı alan (blok içindeyse blok)
            public ArrowDir BigArrow;
            public readonly List<Item> Left = new List<Item>();
            public readonly List<Item> Right = new List<Item>();
            public float LeftW, RightW, Pitch;   // cap birimi, boyut çarpanı hariç
            public float Width => LeftW + (Right.Count > 0 ? kRightGap + RightW : 0f);
        }

        public static Texture2D Compose(string spec, SignCategory baseCategory, float widthM, float heightM, int pixelsPerMeter, string name)
        {
            float ppm = pixelsPerMeter;
            float maxDim = Math.Max(widthM, heightM) * ppm;
            if (maxDim > 2048f) ppm *= 2048f / maxDim;   // doku 2048 pikseli geçmesin
            int W = Math.Max(16, (int)Math.Round(widthM * ppm));
            int H = Math.Max(16, (int)Math.Round(heightM * ppm));

            var baseStyle = Categories.Get(baseCategory);
            var r = new SignRasterizer(W, H);

            r.Clear(baseStyle.Background);
            // Küçük levhalarda (ek levha 90 × 30 cm) kenar boşlukları levhayla orantılı küçülür
            float small = Math.Min(widthM, heightM);
            float insetM = Math.Min(kEdgeInset, small * 0.04f), borderM = Math.Min(kBorder, small * 0.035f);
            float padM = Math.Min(kPadding, small * 0.06f), cornerM = Math.Min(kCorner, small * 0.12f);
            float inset = insetM * ppm;
            r.StrokeRoundRect(inset, inset, W - 2 * inset, H - 2 * inset, cornerM * ppm, Math.Max(1.5f, borderM * ppm), baseStyle.Border);

            var lines = Parse(spec);
            if (lines.Count == 0) return r.ToTexture(name);

            // Ölç
            bool anyBlock = false;
            float units = 0f, widest = 0f;
            foreach (var l in lines)
            {
                var cat = l.HasBlock ? l.Block : baseCategory;
                Measure(l, cat);
                units += l.Pitch * l.Size;
                widest = Math.Max(widest, l.Width * l.Size);
                anyBlock |= l.HasBlock;
            }

            float pad = inset + borderM * ppm + padM * ppm;
            float availW = W - 2 * pad, availH = H - 2 * pad;
            float padX = anyBlock ? kBlockPadX : 0f;
            float cap = Math.Min(availH / units, availW / Math.Max(0.01f, widest + 2 * padX));
            cap = Math.Min(cap, kMaxCap * ppm);
            float left = pad + padX * cap;
            float right = W - pad - padX * cap;

            float y = pad + (availH - units * cap) * 0.5f;

            // Önce bloklar (art arda gelen aynı kategori tek blok). Bloğun yeri ilk satırının hizasına göre.
            foreach (var l in lines) { l.SpanX = left; l.SpanW = right - left; }
            float yb = y;
            for (int i = 0; i < lines.Count;)
            {
                var l = lines[i];
                if (!l.HasBlock || l.BigArrow != ArrowDir.None) { yb += l.Pitch * l.Size * cap; i++; continue; }
                float top = yb, maxW = 0f;
                bool full = false;
                int j = i;
                while (j < lines.Count && lines[j].HasBlock && lines[j].BigArrow == ArrowDir.None && lines[j].Block == l.Block)
                {
                    maxW = Math.Max(maxW, lines[j].Width * lines[j].Size * cap);
                    full |= lines[j].Right.Count > 0;
                    yb += lines[j].Pitch * lines[j].Size * cap; j++;
                }
                if (full) maxW = right - left;
                float bx = l.Align == 1 ? left + (right - left - maxW) * 0.5f : l.Align == 2 ? right - maxW : left;
                for (int k = i; k < j; k++) { lines[k].SpanX = bx; lines[k].SpanW = maxW; }
                var st = Categories.Get(l.Block);
                float gap = 0.1f * cap;
                r.FillRoundRect(bx - padX * cap, top + gap, maxW + 2 * padX * cap, yb - top - 2 * gap, 0.03f * ppm, st.Background);
                i = j;
            }

            // Sonra satırlar
            foreach (var l in lines)
            {
                float lc = cap * l.Size;
                float h = l.Pitch * lc;
                float cy = y + h * 0.5f;
                var cat = l.HasBlock ? l.Block : baseCategory;
                var style = Categories.Get(cat);
                if (l.BigArrow != ArrowDir.None)
                {
                    DrawBigArrow(r, l.BigArrow, cy, kBigArrowSize * lc, pad, W - pad, style.Text);
                }
                else
                {
                    // Sağ grubu olan satır alanın iki ucuna yaslanır; diğerleri hizasına göre
                    float lw = l.Width * lc, x = l.SpanX;
                    if (l.Right.Count == 0)
                    {
                        if (l.Align == 1) x += (l.SpanW - lw) * 0.5f;
                        else if (l.Align == 2) x += l.SpanW - lw;
                    }
                    DrawItems(r, l.Left, x, cy, lc, cat, style);
                    if (l.Right.Count > 0) DrawItems(r, l.Right, l.SpanX + l.SpanW - l.RightW * lc, cy, lc, cat, style);
                }
                y += h;
            }

            return r.ToTexture(name);
        }

        /* ------------------------------------------------------------ ölçüm ve çizim */

        private static bool BoldFor(SignCategory c) => c == SignCategory.Otoyol;

        private static void Measure(Line l, SignCategory cat)
        {
            if (l.BigArrow != ArrowDir.None) { l.Pitch = kBigArrowPitch; return; }
            var font = BoldFor(cat) ? FontAtlas.Bold : FontAtlas.Default;
            float tallest = 1f;
            foreach (var list in new[] { l.Left, l.Right })
                foreach (var it in list)
                {
                    switch (it.Kind)
                    {
                        case ItemKind.Text:
                            it.W = font.Measure(it.Text, kTracking) / font.CapHeight; it.H = 1f; break;
                        case ItemKind.Symbol:
                            if (it.Boxed) { it.H = kBoxH; it.W = it.Symbol.Ratio * kBoxH * 0.72f + kBoxH * 0.28f; }
                            else { it.H = kSymbolH; it.W = it.Symbol.Ratio * kSymbolH; }
                            break;
                        case ItemKind.Number:
                        {
                            var bf = FontAtlas.Bold;
                            it.H = kBoxH;
                            it.W = Math.Max(kBoxH, bf.Measure(it.Text, kTracking) / bf.CapHeight * kBoxTextCap + 0.8f);
                            break;
                        }
                        case ItemKind.Arrow:
                            it.H = it.W = kInlineArrow; break;
                    }
                    tallest = Math.Max(tallest, it.H);
                }
            l.LeftW = Sum(l.Left);
            l.RightW = Sum(l.Right);
            l.Pitch = Math.Max(kLinePitch, tallest + 0.45f);
        }

        private static float Sum(List<Item> items)
        {
            float w = 0f;
            for (int i = 0; i < items.Count; i++) w += items[i].W + (i > 0 ? kItemGap : 0f);
            return w;
        }

        private static void DrawItems(SignRasterizer r, List<Item> items, float x, float cy, float cap, SignCategory cat, CategoryStyle style)
        {
            var font = BoldFor(cat) ? FontAtlas.Bold : FontAtlas.Default;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                float w = it.W * cap, h = it.H * cap;
                switch (it.Kind)
                {
                    case ItemKind.Text:
                        r.DrawText(font, it.Text, x, cy + 0.5f * cap, cap / font.CapHeight, kTracking, style.Text);
                        break;
                    case ItemKind.Symbol:
                        if (it.Boxed)
                        {
                            r.FillRoundRect(x, cy - h * 0.5f, w, h, 0.12f * h, style.Text);
                            float sh = h * 0.72f;
                            SymbolLibrary.Draw(r, it.Symbol, x + (w - it.Symbol.Ratio * sh) * 0.5f, cy - sh * 0.5f, sh, style.Background);
                        }
                        else SymbolLibrary.Draw(r, it.Symbol, x, cy - h * 0.5f, h, style.Text);
                        break;
                    case ItemKind.Number:
                        DrawNumberBox(r, it.Text, x, cy - h * 0.5f, w, h, cap, style);
                        break;
                    case ItemKind.Arrow:
                        DrawArrowAt(r, it.Arrow, x + w * 0.5f, cy, w, style.Text);
                        break;
                }
                x += w + kItemGap * cap;
            }
        }

        private static readonly Color32 kWhite = new Color32(242, 242, 236, 255);
        private static readonly Color32 kBlack = new Color32(28, 28, 28, 255);

        private static void DrawNumberBox(SignRasterizer r, string text, float x, float y, float w, float h, float cap, CategoryStyle ctx)
        {
            Color32 bg, fg;
            char c = text.Length > 0 ? char.ToUpperInvariant(text[0]) : ' ';
            switch (c)
            {
                case 'O': case 'K': { var s = Categories.Get(SignCategory.KGM); bg = s.Background; fg = s.Text; break; }
                case 'E': { var s = Categories.Get(SignCategory.Otoyol); bg = s.Background; fg = s.Text; break; }
                case 'D': bg = kWhite; fg = kBlack; break;
                default: bg = ctx.Text; fg = ctx.Background; break;
            }
            float rad = 0.12f * h;
            r.FillRoundRect(x, y, w, h, rad, bg);
            float stroke = Math.Max(1f, 0.07f * cap);
            r.StrokeRoundRect(x + stroke, y + stroke, w - 2 * stroke, h - 2 * stroke, rad - stroke, stroke, fg);
            // Zemin levhanınkiyle aynıysa (ör. turuncu levhada O kutusu) dışına da ince çizgi
            if (bg.r == ctx.Background.r && bg.g == ctx.Background.g && bg.b == ctx.Background.b)
                r.StrokeRoundRect(x, y, w, h, rad, stroke, fg);
            var bf = FontAtlas.Bold;
            float tc = kBoxTextCap * cap;
            float scale = tc / bf.CapHeight;
            float tw = bf.Measure(text, kTracking) * scale;
            r.DrawText(bf, text, x + (w - tw) * 0.5f, y + h * 0.5f + tc * 0.5f, scale, kTracking, fg);
        }

        /* ------------------------------------------------------------ okuma */

        private static List<Line> Parse(string spec)
        {
            var list = new List<Line>();
            if (string.IsNullOrEmpty(spec)) return list;
            foreach (var raw in spec.Split('|'))
            {
                var s = raw.Trim();
                if (s.Length == 0) continue;
                var line = new Line();

                if (s.StartsWith("["))
                {
                    int close = s.IndexOf(']');
                    if (close > 0)
                    {
                        bool any = false;
                        foreach (var opt in s.Substring(1, close - 1).Split(','))
                        {
                            var o = opt.Trim();
                            if (o.Length == 0) continue;
                            if (Categories.TryParse(o, out var c)) { line.HasBlock = true; line.Block = c; any = true; }
                            else if ((o[0] == 'x' || o[0] == 'X') && float.TryParse(o.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                            { line.Size = Math.Max(0.4f, Math.Min(2f, f)); any = true; }
                            else if (o.ToLowerInvariant() == "orta") { line.Align = 1; any = true; }
                            else if (o.ToLowerInvariant() == "sag" || o.ToLowerInvariant() == "sağ") { line.Align = 2; any = true; }
                        }
                        if (any) s = s.Substring(close + 1).Trim();
                    }
                }

                // Tek başına ok: büyük ok satırı
                if (s.StartsWith("{") && s.EndsWith("}") && s.IndexOf('{', 1) < 0)
                {
                    var d = ParseArrow(s.Substring(1, s.Length - 2));
                    if (d != ArrowDir.None) { line.BigArrow = d; list.Add(line); continue; }
                }

                var target = line.Left;
                int i = 0;
                var sb = new System.Text.StringBuilder();
                while (i < s.Length)
                {
                    char ch = s[i];
                    if (ch == '{')
                    {
                        int close = s.IndexOf('}', i + 1);
                        if (close < 0) { sb.Append(ch); i++; continue; }
                        var tok = s.Substring(i + 1, close - i - 1).Trim();
                        i = close + 1;
                        if (tok == ">") { FlushText(sb, target); target = line.Right; continue; }
                        var item = ParseToken(tok);
                        if (item != null) { FlushText(sb, target); target.Add(item); }
                        continue;
                    }
                    sb.Append(ch); i++;
                }
                FlushText(sb, target);
                if (line.Left.Count + line.Right.Count > 0) list.Add(line);
            }
            return list;
        }

        private static void FlushText(System.Text.StringBuilder sb, List<Item> target)
        {
            var t = sb.ToString().Trim();
            if (t.Length > 0) target.Add(new Item { Kind = ItemKind.Text, Text = t });
            sb.Clear();
        }

        private static Item ParseToken(string tok)
        {
            int colon = tok.IndexOf(':');
            if (colon < 0)
            {
                var d = ParseArrow(tok);
                return d != ArrowDir.None ? new Item { Kind = ItemKind.Arrow, Arrow = d } : null;
            }
            var key = tok.Substring(0, colon).Trim().ToLowerInvariant();
            var val = tok.Substring(colon + 1).Trim();
            switch (key)
            {
                case "s":
                {
                    bool boxed = false;
                    int c2 = val.IndexOf(':');
                    if (c2 >= 0) { boxed = val.Substring(c2 + 1).Trim().ToLowerInvariant() == "k"; val = val.Substring(0, c2).Trim(); }
                    return SymbolLibrary.TryGet(val, out var sym) ? new Item { Kind = ItemKind.Symbol, Symbol = sym, Boxed = boxed } : null;
                }
                case "n":
                    return val.Length > 0 ? new Item { Kind = ItemKind.Number, Text = val } : null;
                case "ok":
                {
                    var d = ParseArrow(val);
                    return d != ArrowDir.None ? new Item { Kind = ItemKind.Arrow, Arrow = d } : null;
                }
            }
            return null;
        }

        public static ArrowDir ParseArrow(string t)
        {
            switch (t.Trim().ToLowerInvariant())
            {
                case "↑": case "yukari": case "yukarı": return ArrowDir.Up;
                case "↓": case "asagi": case "aşağı": return ArrowDir.Down;
                case "←": case "sol": return ArrowDir.Left;
                case "→": case "sag": case "sağ": return ArrowDir.Right;
                case "↗": case "sagust": case "sağüst": return ArrowDir.UpRight;
                case "↖": case "solust": case "solüst": return ArrowDir.UpLeft;
                case "↘": case "sagalt": case "sağalt": return ArrowDir.DownRight;
                case "↙": case "solalt": return ArrowDir.DownLeft;
            }
            return ArrowDir.None;
        }

        /* ------------------------------------------------------------ oklar */

        // Yukarı bakan ok, birim kutuda (y aşağı doğru artar): uç üstte
        private static readonly Vector2[] kArrowUp =
        {
            new Vector2(0f, -0.5f), new Vector2(0.42f, -0.04f), new Vector2(0.14f, -0.04f), new Vector2(0.14f, 0.5f),
            new Vector2(-0.14f, 0.5f), new Vector2(-0.14f, -0.04f), new Vector2(-0.42f, -0.04f),
        };

        private static float Angle(ArrowDir dir)
        {
            switch (dir)
            {
                case ArrowDir.Up: return 0;
                case ArrowDir.UpRight: return 45;
                case ArrowDir.Right: return 90;
                case ArrowDir.DownRight: return 135;
                case ArrowDir.Down: return 180;
                case ArrowDir.DownLeft: return 225;
                case ArrowDir.Left: return 270;
                default: return 315;
            }
        }

        private static void DrawArrowAt(SignRasterizer r, ArrowDir dir, float cx, float cy, float size, Color32 color)
        {
            // Çapraz oklar kutuya sığsın diye biraz küçülür
            if (dir == ArrowDir.UpRight || dir == ArrowDir.UpLeft || dir == ArrowDir.DownRight || dir == ArrowDir.DownLeft) size *= 0.85f;
            float rad = Angle(dir) * Mathf.Deg2Rad, cs = Mathf.Cos(rad), sn = Mathf.Sin(rad);
            var pts = new Vector2[kArrowUp.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                var p = kArrowUp[i] * size;
                pts[i] = new Vector2(cx + p.x * cs - p.y * sn, cy + p.x * sn + p.y * cs);
            }
            r.FillPolygon(pts, color);
        }

        private static void DrawBigArrow(SignRasterizer r, ArrowDir dir, float cy, float size, float left, float right, Color32 color)
        {
            float cx;
            switch (dir)
            {
                case ArrowDir.Up: case ArrowDir.Down: cx = (left + right) * 0.5f; break;
                case ArrowDir.Right: case ArrowDir.UpRight: case ArrowDir.DownRight: cx = right - size * 0.6f; break;
                default: cx = left + size * 0.6f; break;
            }
            DrawArrowAt(r, dir, cx, cy, size, color);
        }
    }
}
