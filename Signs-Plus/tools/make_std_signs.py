"""Signs+ standart trafik işaretleri kütüphanesi (Türkiye).

Her işaret 100×100'lük bir kutuda (y aşağı doğru) shapely ile çizilir:
  - dış hat (outline): levhanın gerçek şekli; mod ön yüzü ve gri arkayı bu şekilde bir ağ olarak çizer
  - katmanlar: sabit renkli çokgenler (çift-tek kuralı)
  - yazı alanı (isteğe bağlı): sabit yazı (DUR) ya da kullanıcının değiştirebildiği değer (hız)
Çıktılar:
  Resources/std_signs.txt     → mod (DLL'e gömülü)
  UI/stdsigns/<ad>.png        → editördeki küçük resimler

Biçim (SPST1):
  S <ad> <grup>                       grup: tehlike | tanzim | bilgi
  O x y x y ...                       dış hat (dışbükey)
  L #rrggbb
  C x y x y ...
  T x y w h #rrggbb <param|sabit> <varsayılan yazı>   (yazıdaki boşluklar _ ile)

Kullanım: python make_std_signs.py <proje kökü> <kalın yazı tipi woff>
Tüm çizimler sıfırdan; KGM işaretlerinin genel düzenine göre sadeleştirilmiştir.
"""
import math, os, sys
from shapely.geometry import Polygon, Point, LineString, box
from shapely.ops import unary_union
from shapely import affinity

sys.path.insert(0, os.path.dirname(__file__))
import make_symbols as MS

RED, BLUE, YEL, WHITE, BLACK, GREEN, AMBER = "#c8102e", "#0a4f9c", "#f6be00", "#f2f2ec", "#1c1c1c", "#1f9a48", "#f2a900"

def circ(x, y, r, n=48): return Point(x, y).buffer(r, quad_segs=n // 4)
def line(pts, w, cap="flat"): return LineString(pts).buffer(w / 2, quad_segs=8, cap_style=cap, join_style="mitre")
def rline(pts, w): return LineString(pts).buffer(w / 2, quad_segs=8, cap_style="round", join_style="round")
def rrect(x0, y0, x1, y1, r): return box(x0 + r, y0 + r, x1 - r, y1 - r).buffer(r, quad_segs=6)
def poly(*p): return Polygon(p)
def U(*g): return unary_union(g)
def rounded(g, r): return g.buffer(-r, join_style="round").buffer(r, quad_segs=6)

# ------------------------------------------------------------------ şekiller
H3 = 100 * math.sqrt(3) / 2
OUT = {
    "circle": circ(50, 50, 50, 64),
    "triangle": rounded(poly((50, 50 - H3 / 2 - 0), (100, 50 + H3 / 2), (0, 50 + H3 / 2)), 6),
    "invtriangle": rounded(poly((0, 50 - H3 / 2), (100, 50 - H3 / 2), (50, 50 + H3 / 2)), 6),
    "octagon": Polygon([(50 + 54.12 * math.cos(math.radians(22.5 + 45 * k)), 50 + 54.12 * math.sin(math.radians(22.5 + 45 * k))) for k in range(8)]),
    "diamond": rounded(poly((50, 0), (100, 50), (50, 100), (0, 50)), 4),
    "square": rrect(0, 0, 100, 100, 7),
    "rect": rrect(0, 30, 100, 70, 4),
}

SIGNS = {}   # ad -> dict(group, shape, layers, text)
def sign(name, group, shape):
    def deco(fn):
        res = fn()
        layers, text = (res if isinstance(res, tuple) else (res, None))
        SIGNS[name] = dict(group=group, shape=shape, layers=layers, text=text)
        return fn
    return deco

def inset(shape, d): return OUT[shape].buffer(-d, join_style="round")

# ------------------------------------------------------------------ ortak parçalar
def warn_base():
    o = OUT["triangle"]; i = inset("triangle", 9)
    return [(RED, o), (WHITE, i)]

def ring_base(inner=WHITE):
    return [(RED, OUT["circle"]), (inner, circ(50, 50, 40))]

def blue_circle():
    return [(WHITE, OUT["circle"]), (BLUE, circ(50, 50, 48))]

def blue_square():
    return [(WHITE, OUT["square"]), (BLUE, rrect(2.5, 2.5, 97.5, 97.5, 5))]

def slash(width=8, angle=45):
    s = affinity.rotate(box(50 - width / 2, 0, 50 + width / 2, 100), angle, origin=(50, 50))
    return s.intersection(circ(50, 50, 40))

def arrow(pts, w=8, head=16, headlen=14):
    """Ucu son noktada olan kalın ok."""
    (x0, y0), (x1, y1) = pts[-2], pts[-1]
    d = math.hypot(x1 - x0, y1 - y0); ux, uy = (x1 - x0) / d, (y1 - y0) / d
    bx, by = x1 - ux * headlen, y1 - uy * headlen
    shaft = line(pts[:-1] + [(bx + ux * 0.5, by + uy * 0.5)], w)
    tip = poly((x1, y1), (bx - uy * head / 2, by + ux * head / 2), (bx + uy * head / 2, by - ux * head / 2))
    return U(shaft, tip)

def pedestrian(cx=50, cy=60, s=1.0):
    g = U(circ(cx + 3 * s, cy - 22 * s, 4.6 * s),
          rline([(cx + 2 * s, cy - 15 * s), (cx - 2 * s, cy + 2 * s)], 6.5 * s),
          rline([(cx - 2 * s, cy + 2 * s), (cx - 9 * s, cy + 18 * s)], 5 * s),
          rline([(cx - 2 * s, cy + 2 * s), (cx + 4 * s, cy + 9 * s), (cx + 6 * s, cy + 18 * s)], 5 * s),
          rline([(cx + 1 * s, cy - 11 * s), (cx - 7 * s, cy - 2 * s)], 4 * s),
          rline([(cx + 1 * s, cy - 11 * s), (cx + 9 * s, cy - 4 * s)], 4 * s))
    return g

def car_front(x0, y0, w, col):
    h = w * 0.62
    body = rrect(x0, y0 + h * 0.38, x0 + w, y0 + h * 0.86, w * 0.08)
    roof = poly((x0 + w * 0.17, y0 + h * 0.4), (x0 + w * 0.28, y0), (x0 + w * 0.72, y0), (x0 + w * 0.83, y0 + h * 0.4))
    wind = poly((x0 + w * 0.27, y0 + h * 0.36), (x0 + w * 0.34, y0 + h * 0.1), (x0 + w * 0.66, y0 + h * 0.1), (x0 + w * 0.73, y0 + h * 0.36))
    wheels = U(rrect(x0 + w * 0.06, y0 + h * 0.8, x0 + w * 0.26, y0 + h, 1.5), rrect(x0 + w * 0.74, y0 + h * 0.8, x0 + w * 0.94, y0 + h, 1.5))
    lights = U(circ(x0 + w * 0.17, y0 + h * 0.6, w * 0.07), circ(x0 + w * 0.83, y0 + h * 0.6, w * 0.07))
    return (col, U(body, roof, wheels).difference(U(wind, lights)))

def symbol(name, x0, y0, h):
    """make_symbols.py'deki bir sembolü (beyaz) verilen yere yerleştirir."""
    ratio, layers = MS.SYMBOLS[name]
    layers = MS.bounds_fix(name, ratio, layers)
    g = unary_union([s for _, s in layers])
    return affinity.translate(affinity.scale(g, h / 100, h / 100, origin=(0, 0)), x0, y0)

# ------------------------------------------------------------------ tehlike uyarı (T)
@sign("dikkat", "tehlike", "triangle")
def _():
    return warn_base() + [(BLACK, U(rrect(45.5, 38, 54.5, 70, 3), circ(50, 78.5, 4.8)))]

@sign("viraj-sag", "tehlike", "triangle")
def _():
    return warn_base() + [(BLACK, arrow([(44, 86), (44, 64), (64, 44)], w=7.5, head=19, headlen=14))]

@sign("viraj-sol", "tehlike", "triangle")
def _():
    return warn_base() + [(BLACK, arrow([(56, 86), (56, 64), (36, 44)], w=7.5, head=19, headlen=14))]

@sign("kavsak", "tehlike", "triangle")
def _():
    return warn_base() + [(BLACK, U(box(45.5, 36, 54.5, 84), box(30, 56, 70, 64)))]

@sign("isik", "tehlike", "triangle")
def _():
    return warn_base() + [(BLACK, rrect(40, 36, 60, 84, 5)), (RED, circ(50, 46, 5.5)), (AMBER, circ(50, 60, 5.5)), (GREEN, circ(50, 74, 5.5))]

@sign("yaya", "tehlike", "triangle")
def _():
    return warn_base() + [(BLACK, pedestrian(50, 63, 0.95))]

@sign("daralma", "tehlike", "triangle")
def _():
    return warn_base() + [(BLACK, U(line([(37, 86), (37, 68), (44, 56), (44, 36)], 6), line([(63, 86), (63, 68), (56, 56), (56, 36)], 6)))]

@sign("kasis", "tehlike", "triangle")
def _():
    g = U(box(24, 74, 76, 80), affinity.scale(circ(39, 74, 9), 1, 1.0).intersection(box(0, 0, 100, 74)), affinity.scale(circ(61, 74, 9), 1, 1.0).intersection(box(0, 0, 100, 74)))
    return warn_base() + [(BLACK, g)]

@sign("donel", "tehlike", "triangle")
def _():
    ring = circ(50, 62, 14).difference(circ(50, 62, 8.5))
    heads = []
    for a in (0, 120, 240):
        r = math.radians(a - 90)
        cx, cy = 50 + 11.25 * math.cos(r), 62 + 11.25 * math.sin(r)
        t = affinity.rotate(poly((-6, 0), (6, 0), (0, 8)), a + 90, origin=(0, 0))
        heads.append(affinity.translate(t, cx, cy))
    return warn_base() + [(BLACK, U(ring, *heads))]

@sign("iki-yon", "tehlike", "triangle")
def _():
    return warn_base() + [(BLACK, U(arrow([(42, 84), (42, 38)], w=6, head=14, headlen=11), arrow([(58, 38), (58, 84)], w=6, head=14, headlen=11)))]

# ------------------------------------------------------------------ tanzim (TT)
@sign("dur", "tanzim", "octagon")
def _():
    o = OUT["octagon"]
    return ([(WHITE, o), (RED, o.buffer(-3, join_style="mitre"))], ("16 36 68 28", WHITE, "sabit", "DUR"))

@sign("yolver", "tanzim", "invtriangle")
def _():
    return [(RED, OUT["invtriangle"]), (WHITE, inset("invtriangle", 11))]

@sign("anayol", "tanzim", "diamond")
def _():
    return [(WHITE, OUT["diamond"]), (YEL, poly((50, 17), (83, 50), (50, 83), (17, 50)))]

@sign("girilmez", "tanzim", "circle")
def _():
    return [(RED, OUT["circle"]), (WHITE, box(16, 42, 84, 58))]

@sign("kapali", "tanzim", "circle")
def _():
    return ring_base()

@sign("hiz", "tanzim", "circle")
def _():
    return (ring_base(), ("18 30 64 40", BLACK, "param", "50"))

@sign("sola-donulmez", "tanzim", "circle")
def _():
    return ring_base() + [(BLACK, arrow([(56, 80), (56, 50), (30, 50)], w=8, head=18, headlen=13)), (RED, slash())]

@sign("saga-donulmez", "tanzim", "circle")
def _():
    return ring_base() + [(BLACK, arrow([(44, 80), (44, 50), (70, 50)], w=8, head=18, headlen=13)), (RED, slash(angle=-45))]

@sign("u-donulmez", "tanzim", "circle")
def _():
    u = U(line([(58, 80), (58, 44)], 8), circ(48, 44, 14).difference(circ(48, 44, 6)).intersection(box(0, 0, 100, 44)), line([(38, 44), (38, 56)], 8))
    tip = poly((38, 70), (29, 56), (47, 56))
    return ring_base() + [(BLACK, U(u, tip)), (RED, slash())]

@sign("gecmek-yasak", "tanzim", "circle")
def _():
    return ring_base() + [car_front(19, 36, 29, RED), car_front(52, 36, 29, BLACK)]

@sign("park-yasak", "tanzim", "circle")
def _():
    return ring_base(BLUE) + [(RED, slash(9))]

@sign("duraklama-yasak", "tanzim", "circle")
def _():
    return ring_base(BLUE) + [(RED, U(slash(9), slash(9, -45)))]

@sign("mecburi-ileri", "tanzim", "circle")
def _():
    return blue_circle() + [(WHITE, arrow([(50, 80), (50, 20)], w=11, head=28, headlen=20))]

@sign("mecburi-sag", "tanzim", "circle")
def _():
    return blue_circle() + [(WHITE, arrow([(20, 50), (80, 50)], w=11, head=28, headlen=20))]

@sign("mecburi-sol", "tanzim", "circle")
def _():
    return blue_circle() + [(WHITE, arrow([(80, 50), (20, 50)], w=11, head=28, headlen=20))]

@sign("mecburi-ileri-sag", "tanzim", "circle")
def _():
    return blue_circle() + [(WHITE, U(arrow([(42, 82), (42, 20)], w=9, head=22, headlen=16), arrow([(42, 58), (50, 50), (78, 50)], w=9, head=22, headlen=16)))]

@sign("mecburi-ileri-sol", "tanzim", "circle")
def _():
    return blue_circle() + [(WHITE, U(arrow([(58, 82), (58, 20)], w=9, head=22, headlen=16), arrow([(58, 58), (50, 50), (22, 50)], w=9, head=22, headlen=16)))]

@sign("sagdan-git", "tanzim", "circle")
def _():
    return blue_circle() + [(WHITE, arrow([(29, 29), (71, 71)], w=11, head=28, headlen=20))]

@sign("soldan-git", "tanzim", "circle")
def _():
    return blue_circle() + [(WHITE, arrow([(71, 29), (29, 71)], w=11, head=28, headlen=20))]

@sign("donel-mecburi", "tanzim", "circle")
def _():
    parts = []
    for a in (0, 120, 240):
        arc = [(50 + 24 * math.cos(math.radians(t)), 50 + 24 * math.sin(math.radians(t))) for t in range(a + 190, a + 280, 6)]
        parts.append(arrow(arc, w=9, head=21, headlen=12))
    return blue_circle() + [(WHITE, U(*parts))]

# ------------------------------------------------------------------ bilgi (B)
@sign("park", "bilgi", "square")
def _():
    return blue_square() + [(WHITE, symbol("otopark", 30, 16, 68))]

@sign("yaya-gecidi", "bilgi", "square")
def _():
    tri = rounded(poly((50, 12), (88, 82), (12, 82)), 3)
    zebra = U(*[box(26 + 10 * k, 72, 31 + 10 * k, 78) for k in range(5)])
    return blue_square() + [(WHITE, tri), (BLACK, U(pedestrian(50, 54, 0.78), zebra))]

@sign("tek-yon", "bilgi", "rect")
def _():
    return [(WHITE, OUT["rect"]), (BLUE, rrect(2, 32, 98, 68, 3)), (WHITE, arrow([(10, 50), (90, 50)], w=8, head=24, headlen=16))]

@sign("cikmaz", "bilgi", "square")
def _():
    return blue_square() + [(WHITE, box(41, 34, 59, 86)), (RED, box(22, 18, 78, 34))]

@sign("durak", "bilgi", "square")
def _():
    return blue_square() + [(WHITE, symbol("otobus", 10, 26, 54))]

@sign("hastane", "bilgi", "square")
def _():
    return blue_square() + [(WHITE, symbol("hastane", 22, 18, 64))]

@sign("ilk-yardim", "bilgi", "square")
def _():
    return blue_square() + [(WHITE, rrect(18, 18, 82, 82, 4)), (RED, circ(44, 50, 22).difference(circ(53, 50, 18)))]

ORDER = ["dikkat", "viraj-sag", "viraj-sol", "kavsak", "isik", "yaya", "daralma", "kasis", "donel", "iki-yon",
         "dur", "yolver", "anayol", "hiz", "girilmez", "kapali", "sola-donulmez", "saga-donulmez", "u-donulmez", "gecmek-yasak",
         "park-yasak", "duraklama-yasak", "mecburi-ileri", "mecburi-sag", "mecburi-sol", "mecburi-ileri-sag", "mecburi-ileri-sol",
         "sagdan-git", "soldan-git", "donel-mecburi",
         "park", "yaya-gecidi", "tek-yon", "cikmaz", "durak", "hastane", "ilk-yardim"]

NAMES = {   # ad: (Türkçe, İngilizce)
    "dikkat": ("Dikkat", "Danger"), "viraj-sag": ("Sağa tehlikeli viraj", "Bend to the right"), "viraj-sol": ("Sola tehlikeli viraj", "Bend to the left"),
    "kavsak": ("Kontrolsüz kavşak", "Crossroads"), "isik": ("Işıklı işaret cihazı", "Traffic lights"), "yaya": ("Yaya geçidi (uyarı)", "Pedestrians"),
    "daralma": ("Daralan kaplama", "Road narrows"), "kasis": ("Kasisli yol", "Uneven road"), "donel": ("Dönel kavşak (uyarı)", "Roundabout ahead"),
    "iki-yon": ("İki yönlü trafik", "Two-way traffic"), "dur": ("Dur", "Stop"), "yolver": ("Yol ver", "Give way"), "anayol": ("Ana yol", "Priority road"),
    "hiz": ("Azami hız sınırı", "Speed limit"), "girilmez": ("Girilmez", "No entry"), "kapali": ("Taşıt trafiğine kapalı", "No vehicles"),
    "sola-donulmez": ("Sola dönülmez", "No left turn"), "saga-donulmez": ("Sağa dönülmez", "No right turn"), "u-donulmez": ("U dönüşü yapılmaz", "No U-turn"),
    "gecmek-yasak": ("Öndeki taşıtı geçmek yasak", "No overtaking"), "park-yasak": ("Park etmek yasak", "No parking"),
    "duraklama-yasak": ("Duraklamak ve park etmek yasak", "No stopping"), "mecburi-ileri": ("İleri mecburi yön", "Ahead only"),
    "mecburi-sag": ("Sağa mecburi yön", "Turn right"), "mecburi-sol": ("Sola mecburi yön", "Turn left"),
    "mecburi-ileri-sag": ("İleri ve sağa mecburi yön", "Ahead or right"), "mecburi-ileri-sol": ("İleri ve sola mecburi yön", "Ahead or left"),
    "sagdan-git": ("Sağdan gidiniz", "Keep right"), "soldan-git": ("Soldan gidiniz", "Keep left"), "donel-mecburi": ("Ada etrafında dönünüz", "Roundabout"),
    "park": ("Park yeri", "Parking"), "yaya-gecidi": ("Yaya geçidi", "Pedestrian crossing"), "tek-yon": ("Tek yönlü yol", "One way"),
    "cikmaz": ("Çıkmaz yol", "Dead end"), "durak": ("Otobüs durağı", "Bus stop"), "hastane": ("Hastane", "Hospital"), "ilk-yardim": ("İlk yardım", "First aid"),
}

def contours(g):
    polys = [g] if isinstance(g, Polygon) else list(getattr(g, "geoms", []))
    for p in polys:
        if p.is_empty or not isinstance(p, Polygon): continue
        yield list(p.exterior.coords)[:-1]
        for r in p.interiors: yield list(r.coords)[:-1]

def render_png(spec, path, font_path, size=96):
    from PIL import Image, ImageDraw, ImageChops, ImageFont
    S = 4 * size / 100
    W = 4 * size
    img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    for color, g in spec["layers"]:
        mask = Image.new("1", (W, W), 0)
        for c in contours(g):
            m = Image.new("1", (W, W), 0); ImageDraw.Draw(m).polygon([(x * S, y * S) for x, y in c], fill=1)
            mask = ImageChops.logical_xor(mask, m)
        img.paste(Image.new("RGBA", (W, W), tuple(int(color[k:k + 2], 16) for k in (1, 3, 5)) + (255,)), (0, 0), mask.convert("L"))
    if spec["text"]:
        rect, color, kind, txt = spec["text"]
        x, y, w, h = map(float, rect.split())
        size_px = int(h * S * 1.15)
        font = ImageFont.truetype(font_path, size_px)
        while font.getlength(txt) > w * S and size_px > 8:
            size_px -= 2; font = ImageFont.truetype(font_path, size_px)
        d = ImageDraw.Draw(img)
        d.text(((x + w / 2) * S, (y + h / 2) * S), txt, font=font, fill=color, anchor="mm")
    img.resize((size, size), Image.LANCZOS).save(path)

def main(root, font_path):
    out = ["SPST1"]
    png_dir = os.path.join(root, "UI", "stdsigns"); os.makedirs(png_dir, exist_ok=True)
    for name in ORDER:
        s = SIGNS[name]
        out.append(f"S {name} {s['group']}")
        o = OUT[s["shape"]].simplify(0.05)
        out.append("O " + " ".join(f"{x:.2f} {y:.2f}" for x, y in list(o.exterior.coords)[:-1]))
        for color, g in s["layers"]:
            g = g.intersection(OUT[s["shape"]]).simplify(0.12)
            out.append(f"L {color}")
            for c in contours(g):
                out.append("C " + " ".join(f"{x:.2f} {y:.2f}" for x, y in c))
        if s["text"]:
            rect, color, kind, txt = s["text"]
            out.append(f"T {rect} {color} {kind} {txt.replace(' ', '_')}")
        render_png(s, os.path.join(png_dir, name + ".png"), font_path)
    with open(os.path.join(root, "Resources", "std_signs.txt"), "w") as f: f.write("\n".join(out) + "\n")
    # Editör için liste (UI/src/stdsigns.ts) ve İngilizce adlar (Data/StdSignNames.cs)
    import json
    items = []
    for name in ORDER:
        s = SIGNS[name]; t = s["text"]
        items.append({"id": name, "group": s["group"], "param": bool(t and t[2] == "param"), "def": t[3] if t else "", "tr": NAMES[name][0]})
    with open(os.path.join(root, "UI", "src", "stdsigns.ts"), "w", encoding="utf-8") as f:
        f.write("// tools/make_std_signs.py üretir. Elle düzenlemeyin.\n")
        f.write("export type StdSign = { id: string; group: string; param: boolean; def: string; tr: string };\n")
        f.write("export const STD_SIGNS: StdSign[] = " + json.dumps(items, ensure_ascii=False, indent=1) + ";\n")
    rows_en = "\n".join(f'            {{ "Std.{n}", "{NAMES[n][1]}" }},' for n in ORDER)
    rows_tr = "\n".join(f'            {{ "Std.{n}", "{NAMES[n][0]}" }},' for n in ORDER)
    with open(os.path.join(root, "Data", "StdSignNames.cs"), "w", encoding="utf-8") as f:
        f.write(f"""// tools/make_std_signs.py üretir. Elle düzenlemeyin.
using System.Collections.Generic;

namespace SignsPlus.Data
{{
    /// <summary>Standart işaret adları; arayüz "SignsPlus.UI.Std.<ad>" anahtarıyla okur.</summary>
    internal static class StdSignNames
    {{
        public static readonly Dictionary<string, string> EN = new Dictionary<string, string>
        {{
{rows_en}
        }};

        public static readonly Dictionary<string, string> TR = new Dictionary<string, string>
        {{
{rows_tr}
        }};
    }}
}}
""")
    print(len(ORDER), "işaret")

if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
