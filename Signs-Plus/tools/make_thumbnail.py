"""Signs+ mod sayfası küçük resmi ve yazı logosu (Transport+ / Demand+ ile aynı tarz).

Beyaz zemin, soluk ızgara, pastel levhalar (gantry, yer levhası, trafik işaretleri), Mini Metro tarzı gri
işaretler; ortada Unbounded 800 #1800ad "Signs+", altında Orbitron "BY RAGGEND" geçişli ve "ALPHA" etiketi.

Kullanım: python make_thumbnail.py <unbounded-800.woff> <orbitron-600.woff> <çıktı klasörü>
Çıktı: Thumbnail.png (1080×1080), Logo.png (şeffaf yazı logosu)
"""
import sys, os, math
from PIL import Image, ImageDraw, ImageFont, ImageFilter, ImageChops

S = 2                      # süper örnekleme
W = 1080 * S
NAVY = (24, 0, 173)
GRAD_A, GRAD_B = (94, 111, 250), (232, 104, 206)
GREY = (118, 118, 122)
P = {  # pastel kategori renkleri
    "green": (126, 206, 150), "blue": (122, 196, 238), "brown": (205, 160, 140), "yellow": (246, 208, 112),
    "orange": (246, 166, 120), "red": (240, 136, 140), "post": (226, 232, 240), "white": (255, 255, 255),
}

def s(v): return int(round(v * S))

def fade_mask(size, y_from, y_to, top_alpha=255, bottom_alpha=0):
    """Dikey geçiş maskesi: y_from'da top_alpha, y_to'da bottom_alpha (yerel koordinat)."""
    w, h = size
    m = Image.new("L", size, top_alpha)
    px = m.load()
    for y in range(h):
        if y <= y_from: a = top_alpha
        elif y >= y_to: a = bottom_alpha
        else:
            t = (y - y_from) / max(1, y_to - y_from)
            a = int(top_alpha + (bottom_alpha - top_alpha) * t)
        for x in range(w): px[x, y] = a
    return m

def paste_layer(base, layer, mask=None):
    if mask is not None:
        a = ImageChops.multiply(layer.split()[3], mask)
        layer.putalpha(a)
    base.alpha_composite(layer)

def panel(base, box, color, bars, arrow=None, fade=None, bar_color=(255, 255, 255), small_boxes=()):
    x0, y0, x1, y1 = [s(v) for v in box]
    lay = Image.new("RGBA", base.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    d.rounded_rectangle((x0, y0, x1, y1), radius=s(18), fill=color + (255,))
    d.rounded_rectangle((x0 + s(9), y0 + s(9), x1 - s(9), y1 - s(9)), radius=s(12), outline=bar_color + (255,), width=s(4))
    for (bx, by, bw) in bars:   # yazı satırları yerine yuvarlak çubuklar
        d.rounded_rectangle((s(bx), s(by), s(bx + bw), s(by + 22)), radius=s(11), fill=bar_color + (255,))
    for (bx, by, bw, col) in small_boxes:
        d.rounded_rectangle((s(bx), s(by), s(bx + bw), s(by + 30)), radius=s(6), fill=col + (255,), outline=bar_color + (255,), width=s(3))
    if arrow:
        cx, cy, ang, sz = arrow
        pts = [(0, -0.5), (0.42, -0.04), (0.14, -0.04), (0.14, 0.5), (-0.14, 0.5), (-0.14, -0.04), (-0.42, -0.04)]
        r = math.radians(ang)
        d.polygon([(s(cx + (px * math.cos(r) - py * math.sin(r)) * sz), s(cy + (px * math.sin(r) + py * math.cos(r)) * sz)) for px, py in pts], fill=bar_color + (255,))
    mask = None
    if fade:
        mask = fade_mask(base.size, s(fade[0]), s(fade[1]))
    paste_layer(base, lay, mask)

def marker(d, kind, x, y, r=17, dots=0, dot_side=1):
    w = s(5)
    X, Y, R = s(x), s(y), s(r)
    if kind == "circle":
        d.ellipse((X - R, Y - R, X + R, Y + R), fill=(255, 255, 255, 255), outline=GREY + (255,), width=w)
    elif kind == "square":
        d.rectangle((X - R, Y - R, X + R, Y + R), fill=(255, 255, 255, 255), outline=GREY + (255,), width=w)
    elif kind == "triangle":
        pts = [(X, Y - R * 1.15), (X + R * 1.05, Y + R * 0.75), (X - R * 1.05, Y + R * 0.75)]
        d.polygon(pts, fill=(255, 255, 255, 255))
        d.line(pts + [pts[0]], fill=GREY + (255,), width=w, joint="curve")
    elif kind == "plus":
        t = R * 0.42
        pts = [(X - t, Y - R), (X + t, Y - R), (X + t, Y - t), (X + R, Y - t), (X + R, Y + t), (X + t, Y + t), (X + t, Y + R),
               (X - t, Y + R), (X - t, Y + t), (X - R, Y + t), (X - R, Y - t), (X - t, Y - t)]
        d.polygon(pts, fill=(255, 255, 255, 255))
        d.line(pts + [pts[0]], fill=GREY + (255,), width=w)
    for i in range(dots):
        cx = X + dot_side * (R + s(14) + i * s(15))
        d.ellipse((cx - s(5), Y - s(5), cx + s(5), Y + s(5)), fill=GREY + (255,))

def gradient_text(text, font, size_h):
    """Yatay geçişli yazı (RGBA)."""
    bbox = font.getbbox(text)
    w, h = bbox[2] - bbox[0] + s(4), bbox[3] - bbox[1] + s(4)
    mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask).text((-bbox[0] + s(2), -bbox[1] + s(2)), text, font=font, fill=255)
    grad = Image.new("RGBA", (w, h))
    gp = grad.load()
    for x in range(w):
        t = x / max(1, w - 1)
        c = tuple(int(GRAD_A[i] + (GRAD_B[i] - GRAD_A[i]) * t) for i in range(3))
        for y in range(h): gp[x, y] = c + (255,)
    grad.putalpha(mask)
    return grad

def title_block(f_main, f_sub, with_glow):
    """Logo bloğu: 'Signs+', altında solda ALPHA etiketi, sağda BY RAGGEND."""
    text = "Signs+"
    bb = f_main.getbbox(text)
    tw, th = bb[2] - bb[0], bb[3] - bb[1]
    sub = gradient_text("BY RAGGEND", f_sub, 0)
    pad = s(30)
    W2, H2 = tw + 2 * pad, th + sub.height + s(40) + 2 * pad
    blk = Image.new("RGBA", (W2, H2), (0, 0, 0, 0))
    d = ImageDraw.Draw(blk)
    d.text((pad - bb[0], pad - bb[1]), text, font=f_main, fill=NAVY + (255,))
    sy = pad + th + s(30)
    blk.alpha_composite(sub, (pad + tw - sub.width, sy))
    # ALPHA etiketi
    f_badge = f_sub.font_variant(size=int(f_sub.size * 0.82))
    abb = f_badge.getbbox("ALPHA")
    aw, ah = abb[2] - abb[0], abb[3] - abb[1]
    bx0, by0 = pad + s(6), sy - s(6)
    d.rounded_rectangle((bx0, by0, bx0 + aw + s(28), by0 + ah + s(18)), radius=s(10), fill=NAVY + (255,))
    d.text((bx0 + s(14) - abb[0], by0 + s(9) - abb[1]), "ALPHA", font=f_badge, fill=(255, 255, 255, 255))
    return blk

def main(f_unb, f_orb, out):
    img = Image.new("RGBA", (W, W), (255, 255, 255, 255))
    d = ImageDraw.Draw(img)
    # soluk ızgara
    for k in range(0, 1081, 54):
        d.line((s(k), 0, s(k), W), fill=(246, 248, 251, 255), width=s(2))
        d.line((0, s(k), W, s(k)), fill=(246, 248, 251, 255), width=s(2))

    # gantry: kiriş ve ayaklar
    lay = Image.new("RGBA", img.size, (0, 0, 0, 0)); dl = ImageDraw.Draw(lay)
    dl.rectangle((0, s(150), W, s(176)), fill=P["post"] + (255,))
    dl.rectangle((s(56), s(150), s(72), W), fill=P["post"] + (255,))
    dl.rectangle((s(1008), s(150), s(1024), W), fill=P["post"] + (255,))
    paste_layer(img, lay, fade_mask(img.size, s(420), s(620)))

    # gantry levhaları (aşağı doğru solar)
    panel(img, (120, 196, 500, 470), P["green"], [(152, 262, 150), (152, 304, 230), (152, 346, 190)],
          arrow=(310, 420, 180, 46), fade=(330, 470), small_boxes=[(152, 214, 64, P["green"]), (226, 214, 52, P["orange"])])
    panel(img, (560, 196, 960, 450), P["blue"], [(592, 222, 240), (592, 264, 170)],
          arrow=(905, 400, 45, 52), fade=(310, 450))

    # alt bölüm: yer levhaları ve işaretler (yukarıdan aşağı belirir)
    lay = Image.new("RGBA", img.size, (0, 0, 0, 0)); dl = ImageDraw.Draw(lay)
    for x in (160, 320): dl.rectangle((s(x), s(950), s(x + 14), W), fill=P["post"] + (255,))
    for x in (670, 810): dl.rectangle((s(x), s(970), s(x + 14), W), fill=P["post"] + (255,))
    dl.rectangle((s(956), s(880), s(968), W), fill=P["post"] + (255,))
    dl.rectangle((s(484), s(1010), s(496), W), fill=P["post"] + (255,))
    paste_layer(img, lay)
    panel(img, (96, 820, 390, 955), P["brown"], [(126, 850, 190), (126, 892, 120)], fade=None)
    panel(img, (600, 845, 880, 975), P["yellow"], [(630, 875, 160), (630, 917, 110)], bar_color=(255, 255, 255))
    lay = Image.new("RGBA", img.size, (0, 0, 0, 0)); dl = ImageDraw.Draw(lay)
    cx, cy, r = 962, 830, 56           # hız sınırı
    dl.ellipse((s(cx - r), s(cy - r), s(cx + r), s(cy + r)), fill=P["red"] + (255,))
    dl.ellipse((s(cx - r * 0.74), s(cy - r * 0.74), s(cx + r * 0.74), s(cy + r * 0.74)), fill=(255, 255, 255, 255))
    tx, ty, ts = 490, 955, 112         # tehlike üçgeni
    tri = [(s(tx), s(ty - ts * 0.55)), (s(tx + ts * 0.58), s(ty + ts * 0.45)), (s(tx - ts * 0.58), s(ty + ts * 0.45))]
    dl.polygon(tri, fill=P["red"] + (255,))
    k = 0.62
    tri2 = [(s(tx), s(ty - ts * 0.55 * k + 4)), (s(tx + ts * 0.58 * k), s(ty + ts * 0.45 * k + 4)), (s(tx - ts * 0.58 * k), s(ty + ts * 0.45 * k + 4))]
    dl.polygon(tri2, fill=(255, 255, 255, 255))
    dl.rounded_rectangle((s(tx - 5), s(ty - 22), s(tx + 5), s(ty + 10)), radius=s(4), fill=GREY + (255,))
    dl.ellipse((s(tx - 5), s(ty + 16), s(tx + 5), s(ty + 26)), fill=GREY + (255,))
    mask = Image.new("L", img.size, 255)
    paste_layer(img, lay, mask)

    # yazının arkasını beyaza yaklaştır (Demand+'taki gibi)
    halo = Image.new("L", img.size, 0)
    ImageDraw.Draw(halo).rounded_rectangle((s(60), s(480), s(1020), s(790)), radius=s(80), fill=215)
    halo = halo.filter(ImageFilter.GaussianBlur(s(40)))
    white = Image.new("RGBA", img.size, (255, 255, 255, 255)); white.putalpha(halo)
    img.alpha_composite(white)

    # Mini Metro işaretleri
    d = ImageDraw.Draw(img)
    # marker(d, "circle", 120, 196, dots=2)
    # marker(d, "triangle", 960, 196, dots=3)
    # marker(d, "square", 96, 820, dots=1)
    # marker(d, "circle", 880, 845, r=15, dots=2, dot_side=-1)

    # başlık
    f_main = ImageFont.truetype(f_unb, s(196))
    f_sub = ImageFont.truetype(f_orb, s(36))
    blk = title_block(f_main, f_sub, True)
    img.alpha_composite(blk, ((W - blk.width) // 2, s(486)))

    os.makedirs(out, exist_ok=True)
    img.convert("RGB").resize((1080, 1080), Image.LANCZOS).save(os.path.join(out, "Thumbnail.png"))
    # şeffaf yazı logosu
    logo = title_block(ImageFont.truetype(f_unb, s(150)), ImageFont.truetype(f_orb, s(30)), False)
    logo = logo.resize((logo.width // S, logo.height // S), Image.LANCZOS)
    logo.save(os.path.join(out, "Logo.png"))
    print("yazıldı:", out)

if __name__ == "__main__":
    main(*sys.argv[1:4])
