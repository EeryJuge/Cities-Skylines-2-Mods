"""Signs+ asset tanımları — Blender üreticisi (build_assets.py) ve modun yüz tablosu (Data/Faces.cs) buradan üretilir.

Koordinatlar Blender'da, metre: X sağ, Y geri (levhanın ön yüzü -Y'ye bakar), Z yukarı. Pivot = direğin zemindeki tabanı.
FBX dışa aktarımı (ileri -Z, yukarı Y) ve Unity içe aktarımından sonra: Blender (x, y, z) → oyun (-x, z, -y);
levhanın ön yüzü oyunda nesnenin +Z (ileri) yönüne bakar.

Asset adlarında alt çizgi KULLANILMAZ: CS2 Asset Importer dosya adını alt çizgilerden bölerek okur
(ad_LOD1, ad_BaseColor ...). "SignsPlus_YerLevhasi_300x200" adı bu yüzden içe aktarılamadı.
"""

PANEL_T = 0.03      # alüminyum panel kalınlığı
RAIL_D = 0.04       # panel arkasındaki yatay kuşaklama profillerinin derinliği
BEHIND = PANEL_T + RAIL_D   # taşıyıcı yapı bu Y değerinden başlar

# Panel: (ad, merkez x, alt z, genişlik, yükseklik[, tür])
#   tür yoksa "panel": asset'te gerçek bir levha paneli vardır, mod yazıyı önüne çizer
#   "std": standart trafik işareti; asset'te panel yok, mod işaretin şeklini (daire, üçgen...) ve gri arkasını çizer
#   "plate": ek levha (beyaz dikdörtgen, yazı); boşsa hiç çizilmez
# Parça türleri: box(x0,y0,z0,x1,y1,z1) metal, cyl(x,y,z0,z1,r) metal, concrete(x0,y0,z0,x1,y1,z1)

ASSETS = {
    "SignsPlusYerLevhasi": {
        "desc": "Yer levhası, iki yuvarlak direk, 3,0 × 2,0 m panel",
        "panels": [("A", 0.0, 2.2, 3.0, 2.0)],
        "parts": [
            ("cyl", -0.9, BEHIND + 0.038, 0.0, 4.1, 0.038),
            ("cyl", 0.9, BEHIND + 0.038, 0.0, 4.1, 0.038),
            ("concrete", -1.05, BEHIND - 0.12, -0.05, -0.75, BEHIND + 0.19, 0.06),
            ("concrete", 0.75, BEHIND - 0.12, -0.05, 1.05, BEHIND + 0.19, 0.06),
        ],
    },
    "SignsPlusLBayrak": {
        "desc": "L tipi bayrak direk (kutu profil), 4,0 × 2,5 m panel",
        "panels": [("A", 4.0, 5.6, 4.0, 2.5)],
        "parts": [
            ("box", -0.15, BEHIND + 0.02, 0.0, 0.15, BEHIND + 0.32, 8.0),          # direk
            ("box", 0.15, BEHIND + 0.03, 7.45, 6.2, BEHIND + 0.21, 7.7),           # üst kol
            ("box", 0.15, BEHIND + 0.03, 6.0, 6.2, BEHIND + 0.21, 6.2),            # alt kol
            ("box", -0.3, BEHIND - 0.13, 0.01, 0.3, BEHIND + 0.47, 0.05),           # taban plakası
            ("concrete", -0.5, BEHIND - 0.33, -0.05, 0.5, BEHIND + 0.67, 0.01),     # temel (zeminle aynı hizada)
        ],
    },
    "SignsPlusTDirek": {
        "desc": "T tipi çift kollu direk (kutu profil), iki adet 3,5 × 2,5 m panel",
        "panels": [("A", -3.45, 5.6, 3.5, 2.5), ("B", 3.45, 5.6, 3.5, 2.5)],
        "parts": [
            ("box", -0.15, BEHIND + 0.02, 0.0, 0.15, BEHIND + 0.32, 8.0),
            ("box", -5.3, BEHIND + 0.03, 7.45, 5.3, BEHIND + 0.21, 7.7),
            ("box", -5.3, BEHIND + 0.03, 6.0, 5.3, BEHIND + 0.21, 6.2),
            ("box", -0.3, BEHIND - 0.13, 0.01, 0.3, BEHIND + 0.47, 0.05),
            ("concrete", -0.5, BEHIND - 0.33, -0.05, 0.5, BEHIND + 0.67, 0.01),
        ],
    },
    "SignsPlusGantry": {
        "desc": "Portal gantry (kutu kiriş), 16 m açıklık, 6,0 × 2,5 ve 4,0 × 2,5 m paneller",
        "panels": [("A", -3.3, 5.6, 6.0, 2.5), ("B", 2.0, 5.6, 4.0, 2.5)],
        "parts": [
            ("box", -8.2, BEHIND + 0.05, 0.0, -7.8, BEHIND + 0.45, 7.7),           # sol ayak
            ("box", 7.8, BEHIND + 0.05, 0.0, 8.2, BEHIND + 0.45, 7.7),             # sağ ayak
            ("box", -8.3, BEHIND + 0.06, 7.1, 8.3, BEHIND + 0.44, 7.69),            # üst kiriş
            ("box", -8.3, BEHIND + 0.10, 6.0, 8.3, BEHIND + 0.30, 6.2),            # alt bağlantı kirişi
            ("box", -8.35, BEHIND - 0.1, 0.01, -7.65, BEHIND + 0.6, 0.05),
            ("box", 7.65, BEHIND - 0.1, 0.01, 8.35, BEHIND + 0.6, 0.05),
            ("concrete", -8.6, BEHIND - 0.35, -0.05, -7.4, BEHIND + 0.85, 0.01),
            ("concrete", 7.4, BEHIND - 0.35, -0.05, 8.6, BEHIND + 0.85, 0.01),
        ],
    },
    "SignsPlusIsaretDirek": {
        "desc": "Standart trafik işareti direği, 90 cm işaret + 90 × 30 cm ek levha",
        "panels": [("A", 0.0, 2.15, 0.9, 0.9, "std"), ("B", 0.0, 1.78, 0.9, 0.3, "plate")],
        "parts": [
            ("cyl", 0.0, 0.075, 0.0, 3.1, 0.038),
            ("box", -0.11, 0.004, 2.33, 0.11, 0.045, 2.37),       # işaret kelepçeleri
            ("box", -0.11, 0.004, 2.83, 0.11, 0.045, 2.87),
            ("box", -0.11, 0.004, 1.91, 0.11, 0.045, 1.95),       # ek levha kelepçesi
            ("box", -0.012, 0.025, 3.1, 0.012, 0.125, 3.12),      # direk kapağı
            ("concrete", -0.15, -0.075, -0.05, 0.15, 0.225, 0.02),
        ],
    },
}
