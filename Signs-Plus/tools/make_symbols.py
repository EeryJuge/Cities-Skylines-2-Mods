"""Signs+ sembol kütüphanesi üretici.

Her sembol 100×100'lük bir kutuda (y aşağı doğru) shapely şekilleriyle çizilir ve iki çıktı üretir:
  Resources/symbols.txt   → modun levhaya çizdiği çokgenler (DLL'e gömülü)
  UI/symbols/<ad>.svg     → editör penceresindeki simgeler (beyaz; <ad>-dark.svg koyu)

Katman rengi: "fg" = bulunduğu yerin yazı rengi; "#rrggbb" = sabit renk (ör. kızılay kırmızısı).
Biçim (SPSY1):
  S <ad> <genişlik oranı>      genişlik oranı: sembolün yükseklikle oranı (çoğu 1)
  L <renk>
  C x y x y ...                bir kontur; bir katmandaki konturlar çift-tek kuralıyla doldurulur

Kullanım: python make_symbols.py <proje kökü>
"""
import math, os, sys
from shapely.geometry import Polygon, Point, LineString, box, MultiPolygon
from shapely.ops import unary_union
from shapely import affinity

def rect(x0, y0, x1, y1): return box(x0, y0, x1, y1)
def circ(x, y, r): return Point(x, y).buffer(r, quad_segs=16)
def line(pts, w, cap="round"): return LineString(pts).buffer(w / 2, quad_segs=8, cap_style=cap, join_style="round")
def rrect(x0, y0, x1, y1, r): return box(x0 + r, y0 + r, x1 - r, y1 - r).buffer(r, quad_segs=8)
def poly(*pts): return Polygon(pts)
def U(*g): return unary_union(g)
def arc(cx, cy, r, a0, a1, n=24):
    return [(cx + r * math.cos(math.radians(a)), cy + r * math.sin(math.radians(a))) for a in [a0 + (a1 - a0) * i / n for i in range(n + 1)]]

SYMBOLS = {}   # ad -> (genişlik oranı, [(renk, şekil)])
def sym(name, ratio=1.0):
    def deco(fn):
        SYMBOLS[name] = (ratio, fn())
        return fn
    return deco

# ------------------------------------------------------------------ ulaşım
@sym("ucak")
def _():
    body = U(line([(50, 14), (50, 84)], 11), poly((44.5, 18), (50, 3), (55.5, 18)))
    wings = poly((45, 38), (55, 38), (95, 60), (95, 67), (55, 55), (45, 55), (5, 67), (5, 60))
    tail = poly((46, 76), (54, 76), (70, 88), (70, 93), (54, 88), (46, 88), (30, 93), (30, 88))
    g = affinity.rotate(U(body, wings, tail), 45, origin=(50, 50))
    return [("fg", g)]

@sym("kopru", 1.3)
def _():
    deck = rect(2, 62, 128, 70)
    towers = U(rect(36, 14, 43, 82), rect(87, 14, 94, 82))
    # ana kablo: kenarlardan kule tepelerine, kuleler arasında sarkık
    left = [(2 + 37.5 * t, 52 - 38 * t ** 0.8) for t in [i / 12 for i in range(13)]]
    mid = [(39.5 + 51 * t, 14 + 38 * (1 - (2 * t - 1) ** 2)) for t in [i / 24 for i in range(25)]]
    right = [(90.5 + 37.5 * t, 14 + 38 * t ** 1.25) for t in [i / 12 for i in range(13)]]
    cable = U(line(left, 3.2), line(mid, 3.2), line(right, 3.2))
    hangers = []
    for i in range(1, 8):
        t = i / 8; x = 39.5 + 51 * t; y = 14 + 38 * (1 - (2 * t - 1) ** 2)
        hangers.append(line([(x, y), (x, 63)], 1.6, "flat"))
    for x in (12, 22, 108, 118):
        y = 52 - 38 * ((x - 2) / 37.5) ** 0.8 if x < 60 else 14 + 38 * ((x - 90.5) / 37.5) ** 1.25
        hangers.append(line([(x, y), (x, 63)], 1.6, "flat"))
    water = U(*[line([(4 + 30 * k, 86), (12 + 30 * k, 82), (20 + 30 * k, 86), (28 + 30 * k, 82)], 3) for k in range(4)])
    return [("fg", U(deck, towers, cable, *hangers, water))]

@sym("feribot", 1.25)
def _():
    hull = poly((4, 58), (121, 58), (108, 78), (14, 78))
    cabin = U(rect(28, 42, 92, 58), rect(40, 28, 82, 42))
    win = U(*[rect(34 + 11 * k, 46, 41 + 11 * k, 53) for k in range(5)], *[rect(46 + 11 * k, 32, 53 + 11 * k, 38) for k in range(3)])
    stack = poly((64, 12), (76, 12), (74, 28), (66, 28))
    water = U(*[line([(4 + 30 * k, 90), (12 + 30 * k, 85), (20 + 30 * k, 90), (28 + 30 * k, 85)], 3.4) for k in range(4)])
    return [("fg", U(hull, cabin.difference(win), stack, water))]

@sym("tren", 0.8)
def _():
    body = rrect(6, 6, 74, 78, 12)
    win = rrect(14, 16, 66, 42, 4)
    lights = U(circ(20, 62, 5), circ(60, 62, 5))
    legs = U(poly((14, 78), (24, 78), (12, 96), (2, 96)), poly((56, 78), (66, 78), (78, 96), (68, 96)))
    return [("fg", U(body.difference(U(win, lights)), legs))]

@sym("otobus", 1.5)
def _():
    body = rrect(4, 14, 146, 76, 8)
    wins = U(*[rect(14 + 22 * k, 24, 32 + 22 * k, 44) for k in range(5)], rrect(124, 24, 138, 52, 2))
    door = rect(126, 54, 138, 70)
    wheels = U(circ(34, 78, 12), circ(116, 78, 12))
    hubs = U(circ(34, 78, 4.5), circ(116, 78, 4.5))
    return [("fg", U(body.difference(U(wins, door)), wheels).difference(hubs))]

@sym("araba", 1.6)
def _():
    lower = rrect(4, 44, 156, 76, 10)
    roof = poly((38, 46), (54, 16), (112, 16), (132, 46))
    wins = U(poly((52, 42), (62, 22), (82, 22), (82, 42)), poly((88, 42), (88, 22), (108, 22), (122, 42)))
    wheels = U(circ(40, 78, 13), circ(122, 78, 13))
    hubs = U(circ(40, 78, 5), circ(122, 78, 5))
    return [("fg", U(U(lower, roof).difference(wins), wheels).difference(hubs))]

@sym("kamyon", 1.6)
def _():
    box_ = rect(4, 12, 104, 70)
    cab = U(poly((110, 28), (138, 28), (156, 48), (156, 70), (110, 70)))
    win = poly((118, 34), (136, 34), (148, 48), (118, 48))
    chassis = rect(4, 66, 156, 74)
    wheels = U(circ(30, 78, 12), circ(56, 78, 12), circ(134, 78, 12))
    hubs = U(circ(30, 78, 4.5), circ(56, 78, 4.5), circ(134, 78, 4.5))
    return [("fg", U(box_, cab.difference(win), chassis, wheels).difference(hubs))]

@sym("otoyol", 1.2)
def _():
    # Avrupa otoyol işareti: iki yönlü yol ve üstünden geçen köprü
    bridge = U(rect(4, 16, 116, 30), rect(4, 30, 18, 56), rect(102, 30, 116, 56))
    lane_l = poly((10, 96), (55, 96), (58, 38), (50, 38))
    lane_r = poly((65, 96), (110, 96), (70, 38), (62, 38))
    return [("fg", U(bridge, lane_l, lane_r))]

@sym("cikis")
def _():
    road = rect(20, 6, 38, 96)
    branch = line([(29, 78), (32, 60), (44, 44), (62, 30), (70, 25)], 15)
    head = poly((86, 8), (90, 46), (56, 16))
    return [("fg", U(road, branch, head))]

@sym("tunel", 1.2)
def _():
    portal = rect(4, 8, 116, 92)
    hole = U(Polygon(arc(60, 54, 40, 180, 360)).buffer(0), rect(20, 53, 100, 92)).buffer(0.5).buffer(-0.5)
    road = poly((44, 92), (76, 92), (64, 40), (56, 40))
    return [("fg", U(portal.difference(hole), road))]

@sym("otopark", 0.8)
def _():
    stem = rect(10, 6, 30, 94)
    bowl = U(rect(20, 6, 42, 62), circ(42, 34, 28)).difference(U(rect(30, 22, 42, 46), circ(42, 34, 12)))
    bowl = bowl.difference(rect(0, 0, 20, 100))
    return [("fg", U(stem, bowl))]

@sym("benzin", 0.85)
def _():
    body = rrect(6, 10, 56, 94, 6)
    win = rrect(14, 20, 48, 42, 3)
    hose = line([(56, 30), (70, 40), (70, 74), (78, 78), (82, 70), (82, 34), (74, 22)], 6)
    base = rect(0, 88, 62, 96)
    return [("fg", U(body.difference(win), hose, base))]

# ------------------------------------------------------------------ yerler
@sym("merkez")
def _():
    ring = circ(50, 50, 44).difference(circ(50, 50, 33))
    return [("fg", U(ring, circ(50, 50, 14)))]

@sym("hastane")
def _():
    return [("fg", U(rect(16, 8, 36, 92), rect(64, 8, 84, 92), rect(36, 40, 64, 58)))]

@sym("ilkyardim")
def _():
    crescent = circ(46, 50, 36).difference(circ(58, 50, 30))
    return [("#ffffff", circ(50, 50, 48)), ("#e30a17", crescent)]

@sym("sanayi", 1.3)
def _():
    base = rect(4, 52, 126, 94)
    teeth = U(*[poly((4 + 30 * k, 52), (4 + 30 * k, 28), (34 + 30 * k, 52)) for k in range(4)])
    chimney = rect(104, 8, 116, 52)
    wins = U(*[rect(12 + 22 * k, 64, 24 + 22 * k, 78) for k in range(5)])
    return [("fg", U(base, teeth, chimney).difference(wins))]

@sym("stadyum", 1.5)
def _():
    outer = affinity.scale(circ(0, 0, 1), 72, 44).buffer(0)
    outer = affinity.translate(outer, 75, 50)
    inner = affinity.translate(affinity.scale(circ(0, 0, 1), 58, 31), 75, 50)
    field = rect(46, 38, 104, 62)
    return [("fg", U(outer.difference(inner), field))]

@sym("cami", 1.2)
def _():
    dome = U(Polygon(arc(60, 60, 34, 180, 360)).buffer(0), rect(26, 58, 94, 94))
    finial = U(line([(60, 26), (60, 12)], 3), circ(60, 10, 3))
    minarets = U(*[U(rect(x - 4, 22, x + 4, 94), poly((x - 4, 22), (x, 4), (x + 4, 22)), rect(x - 6, 40, x + 6, 44)) for x in (10, 110)])
    door = U(Polygon(arc(60, 80, 9, 180, 360)).buffer(0), rect(51, 80, 69, 94))
    return [("fg", U(dome, finial, minarets).difference(door))]

@sym("plaj", 1.1)
def _():
    canopy = Polygon(arc(55, 46, 44, 180, 360)).buffer(0)
    scallops = U(*[circ(11 + 88 * (k + 0.5) / 4, 53, 12) for k in range(4)])
    canopy = canopy.difference(scallops)
    pole = line([(55, 6), (55, 84)], 5, "flat")
    water = U(*[line([(4 + 28 * k, 94), (11 + 28 * k, 89), (18 + 28 * k, 94), (25 + 28 * k, 89)], 3.4) for k in range(4)])
    return [("fg", U(canopy, pole, water))]

@sym("yaris", 1.0)
def _():
    # damalı bayrak (yarış pisti) — İstanbul Park için
    pole = rect(8, 6, 15, 96)
    flag = rect(15, 8, 92, 60)
    cells = []
    for i in range(6):
        for j in range(4):
            if (i + j) % 2 == 0:
                cells.append(rect(15 + i * 77 / 6, 8 + j * 13, 15 + (i + 1) * 77 / 6, 8 + (j + 1) * 13))
    frame = flag.difference(flag.buffer(-2.5, join_style="mitre"))
    return [("fg", U(pole, frame, *cells))]

@sym("kamera", 1.2)
def _():
    body = rrect(6, 26, 114, 84, 8)
    top = rrect(30, 14, 60, 30, 3)
    lens = circ(60, 55, 21).difference(circ(60, 55, 13))
    flash = rect(88, 34, 104, 42)
    return [("fg", U(body, top).difference(U(circ(60, 55, 21), flash)).union(lens).union(circ(60, 55, 6)))]

@sym("liman")
def _():
    ring = circ(50, 14, 9).difference(circ(50, 14, 4))
    shank = rect(46, 22, 54, 88)
    bar = rect(30, 30, 70, 37)
    curve = line(arc(50, 56, 36, 20, 160), 8, "flat")
    flukes = U(poly((80, 64), (93, 57), (88, 76)), poly((20, 64), (7, 57), (12, 76)))
    return [("fg", U(ring, shank, bar, curve, flukes))]

@sym("dinlenme", 1.2)
def _():
    # dinlenme tesisi: piknik masası ve ağaç
    top = rect(8, 50, 74, 58)
    legs = U(line([(20, 58), (12, 92)], 5, "flat"), line([(62, 58), (70, 92)], 5, "flat"))
    seat = rect(4, 72, 78, 78)
    crown = U(circ(98, 30, 18), circ(90, 44, 14), circ(106, 44, 14))
    trunk = rect(95, 44, 101, 92)
    return [("fg", U(top, legs, seat, crown, trunk))]

ORDER = ["ucak", "kopru", "feribot", "liman", "tren", "otobus", "araba", "kamyon", "otoyol", "cikis", "tunel",
         "otopark", "benzin", "dinlenme", "merkez", "hastane", "ilkyardim", "sanayi", "stadyum", "cami", "plaj", "yaris", "kamera"]

def contours(g):
    polys = [g] if isinstance(g, Polygon) else list(getattr(g, "geoms", []))
    for p in polys:
        if p.is_empty: continue
        yield list(p.exterior.coords)[:-1]
        for r in p.interiors: yield list(r.coords)[:-1]

def bounds_fix(name, ratio, layers):
    """Şekilleri kutuya sığdırır: 0..100*ratio × 0..100, en-boy korunur, ortalanır."""
    g = unary_union([s for _, s in layers])
    x0, y0, x1, y1 = g.bounds
    W, H = 100 * ratio, 100
    s = min(W / (x1 - x0), H / (y1 - y0))
    dx = (W - (x1 - x0) * s) / 2 - x0 * s
    dy = (H - (y1 - y0) * s) / 2 - y0 * s
    return [(c, affinity.translate(affinity.scale(sh, s, s, origin=(0, 0)), dx, dy)) for c, sh in layers]

def main(root):
    out_txt = ["SPSY1"]
    svg_dir = os.path.join(root, "UI", "symbols"); os.makedirs(svg_dir, exist_ok=True)
    for name in ORDER:
        ratio, layers = SYMBOLS[name]
        layers = bounds_fix(name, ratio, layers)
        out_txt.append(f"S {name} {ratio:.3f}")
        paths, paths_dark = [], []
        for color, shape in layers:
            shape = shape.simplify(0.15)
            out_txt.append(f"L {color}")
            d = []
            for c in contours(shape):
                out_txt.append("C " + " ".join(f"{x:.2f} {y:.2f}" for x, y in c))
                d.append("M" + " L".join(f"{x:.1f} {y:.1f}" for x, y in c) + "Z")
            for lst, fg in ((paths, "#ffffff"), (paths_dark, "#1c1c1c")):
                fill = fg if color == "fg" else color
                lst.append(f'<path fill="{fill}" fill-rule="evenodd" d="{" ".join(d)}"/>')
        # Editör simgeleri: beyaz (koyu zemin) ve koyu (açık zemin) iki sürüm
        for suffix, lst in (("", paths), ("-dark", paths_dark)):
            svg = f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {100 * ratio:.0f} 100" width="{100 * ratio:.0f}" height="100">{"".join(lst)}</svg>'
            with open(os.path.join(svg_dir, name + suffix + ".svg"), "w") as f: f.write(svg)
    with open(os.path.join(root, "Resources", "symbols.txt"), "w") as f: f.write("\n".join(out_txt) + "\n")
    print(len(ORDER), "sembol")

if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), ".."))
