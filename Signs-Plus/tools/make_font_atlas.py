"""Signs+ yazı atlası üretici.

Overpass (SIL OFL 1.1) fontunun latin ve latin-ext alt kümelerinden Türkçe dahil karakterleri
tek bir PNG atlasına (beyaz, alfa kanalında harf) ve basit bir metin metrik dosyasına yazar.
Mod bu iki dosyayı DLL içine gömülü kaynak olarak okur.

Kullanım: python make_font_atlas.py <latin.woff> <latin-ext.woff> <çıktı klasörü> [ad]
  Normal: overpass 600 → font_overpass; kalın (otoyol, numara kutuları): overpass 700 → font_overpass_bold
"""
import sys, os
from PIL import Image, ImageFont
from fontTools.ttLib import TTFont

SIZE = 96          # atlas yazı boyutu (px); levhada ölçeklenir
PAD = 4            # harfler arası boşluk (taşma olmasın)
ATLAS_W = 1024

CHARS = [chr(c) for c in range(32, 127)] + list("ŞşİıĞğÜüÖöÇçÂâÎîÛû°·–—’") 

def main(latin, ext, out, name="font_overpass"):
    fonts = []
    for p in (latin, ext):
        fonts.append((ImageFont.truetype(p, SIZE), TTFont(p).getBestCmap()))
    ascent, descent = fonts[0][0].getmetrics()
    glyphs = []
    for ch in CHARS:
        font = next((f for f, cm in fonts if ord(ch) in cm), None)
        if font is None:
            print("eksik:", ch); continue
        adv = font.getlength(ch)
        if ch == " ":
            glyphs.append((ch, None, 0, 0, adv)); continue
        x0, y0, x1, y1 = font.getbbox(ch, anchor="ls")   # taban çizgisine göre
        w, h = x1 - x0, y1 - y0
        img = Image.new("L", (w, h), 0)
        from PIL import ImageDraw
        ImageDraw.Draw(img).text((-x0, -y0), ch, font=font, fill=255, anchor="ls")
        glyphs.append((ch, img, x0, y0, adv))
    # satır satır yerleştir
    x = y = PAD; row_h = 0; placed = []
    for ch, img, xo, yo, adv in glyphs:
        if img is None:
            placed.append((ch, 0, 0, 0, 0, 0, 0, adv)); continue
        w, h = img.size
        if x + w + PAD > ATLAS_W:
            x = PAD; y += row_h + PAD; row_h = 0
        placed.append((ch, x, y, w, h, xo, yo, adv, img))
        x += w + PAD; row_h = max(row_h, h)
    H = 1
    while H < y + row_h + PAD: H *= 2
    atlas = Image.new("RGBA", (ATLAS_W, H), (255, 255, 255, 0))
    lines = [f"SPFA1 {SIZE} {ascent} {descent} {ATLAS_W} {H}"]
    for p in placed:
        ch, gx, gy, w, h, xo, yo, adv = p[:8]
        if len(p) > 8:
            alpha = p[8]
            tile = Image.new("RGBA", (w, h), (255, 255, 255, 0)); tile.putalpha(alpha)
            atlas.paste(tile, (gx, gy))
        # y ekseni: atlas üstten aşağı; yo = harfin üst kenarının taban çizgisine uzaklığı (negatif = yukarıda)
        lines.append(f"{ord(ch)} {gx} {gy} {w} {h} {xo} {yo} {adv:.2f}")
    os.makedirs(out, exist_ok=True)
    atlas.save(os.path.join(out, name + ".png"))
    open(os.path.join(out, name + ".txt"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
    print("atlas", atlas.size, "harf", len(placed))

if __name__ == "__main__":
    main(*sys.argv[1:5])
