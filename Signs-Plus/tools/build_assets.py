"""Signs+ assetlerini üretir (Blender 5 Python modülü, bpy).

Her asset için <çıktı>/<Asset>/ klasörüne:
  <Asset>.fbx, <Asset>_LOD1.fbx, <Asset>_BaseColor.png, <Asset>_MaskMap.png, <Asset>_Normal.png
CS2 Asset Importer'da "Project Root" olarak <çıktı> klasörü seçilir.

Kullanım: python build_assets.py <çıktı klasörü> [yalnızca bu assetler ...]
"""
import math, os, sys, random
import bpy, bmesh
from PIL import Image, ImageFilter

sys.path.insert(0, os.path.dirname(__file__))
from assets_spec import ASSETS, PANEL_T, BEHIND

TEX = 512
# Doku atlası bölgeleri (u0, v0, u1, v1)
R_FACE = (0.0, 0.5, 0.5, 1.0)       # levha ön yüzü (mod yazıyı bunun önüne çizer)
R_METAL = (0.5, 0.5, 1.0, 1.0)      # galvaniz çelik
R_BACK = (0.0, 0.0, 0.5, 0.5)       # panel arkası alüminyum
R_CONC = (0.5, 0.0, 1.0, 0.5)       # beton


def make_textures(folder, name):
    rnd = random.Random(42)
    base = Image.new("RGB", (TEX, TEX))
    mask = Image.new("RGBA", (TEX, TEX))
    regions = [
        (R_FACE, (236, 236, 230), 10, (0, 0, 0, 80)),       # beyaz yansıtıcı folyo
        (R_METAL, (168, 171, 170), 40, (150, 0, 0, 120)),   # galvaniz: yarı metalik, mat
        (R_BACK, (150, 153, 154), 18, (200, 0, 0, 110)),    # alüminyum
        (R_CONC, (148, 145, 136), 32, (0, 0, 0, 25)),       # beton
    ]
    for (u0, v0, u1, v1), col, noise, mk in regions:
        x0, x1 = int(u0 * TEX), int(u1 * TEX)
        y0, y1 = int((1 - v1) * TEX), int((1 - v0) * TEX)   # görüntü üstten aşağı
        tile = Image.new("RGB", (x1 - x0, y1 - y0))
        px = tile.load()
        for y in range(y1 - y0):
            for x in range(x1 - x0):
                n = rnd.randint(-noise, noise) // 4
                px[x, y] = tuple(max(0, min(255, c + n)) for c in col)
        tile = tile.filter(ImageFilter.GaussianBlur(0.7))
        base.paste(tile, (x0, y0))
        mask.paste(Image.new("RGBA", (x1 - x0, y1 - y0), mk), (x0, y0))
    base.save(os.path.join(folder, f"{name}_BaseColor.png"))
    mask.save(os.path.join(folder, f"{name}_MaskMap.png"))
    Image.new("RGB", (TEX, TEX), (128, 128, 255)).save(os.path.join(folder, f"{name}_Normal.png"))


def map_region(bm, faces, region, inset=0.02):
    """Yüzlerin UV'lerini bölgenin içine sıkıştırır (düz renkli bölgeler; esneme önemsiz)."""
    uv = bm.loops.layers.uv.verify()
    u0, v0, u1, v1 = region
    du, dv = (u1 - u0) * inset, (v1 - v0) * inset
    for f in faces:
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        a, b = [(1, 2), (0, 2), (0, 1)][ax]
        cs = [l.vert.co for l in f.loops]
        amin, amax = min(c[a] for c in cs), max(c[a] for c in cs)
        bmin, bmax = min(c[b] for c in cs), max(c[b] for c in cs)
        for l in f.loops:
            s = (l.vert.co[a] - amin) / max(1e-6, amax - amin)
            t = (l.vert.co[b] - bmin) / max(1e-6, bmax - bmin)
            l[uv].uv = (u0 + du + s * (u1 - u0 - 2 * du), v0 + dv + t * (v1 - v0 - 2 * dv))


def add_box(bm, x0, y0, z0, x1, y1, z1):
    vs = [bm.verts.new(p) for p in [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
                                     (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]]
    idx = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    return [bm.faces.new([vs[i] for i in q]) for q in idx]


def add_cyl(bm, x, y, z0, z1, r, seg):
    bot, top, faces = [], [], []
    for i in range(seg):
        a = 2 * math.pi * i / seg
        bot.append(bm.verts.new((x + r * math.cos(a), y + r * math.sin(a), z0)))
        top.append(bm.verts.new((x + r * math.cos(a), y + r * math.sin(a), z1)))
    for i in range(seg):
        j = (i + 1) % seg
        f = bm.faces.new([bot[i], bot[j], top[j], top[i]]); f.smooth = True; faces.append(f)
    faces.append(bm.faces.new(list(reversed(bot))))
    faces.append(bm.faces.new(top))
    return faces


def build_mesh(name, spec, lod):
    bm = bmesh.new()
    metal, back, conc, front = [], [], [], []
    for panel in spec["panels"]:
        pid, cx, z0, w, h = panel[:5]
        if len(panel) > 5 and panel[5] != "panel":
            continue   # standart işaret / ek levha: yüzeyi mod çizer
        x0, x1 = cx - w / 2, cx + w / 2
        fs = add_box(bm, x0, 0.0, z0, x1, PANEL_T, z0 + h)
        bm.normal_update()
        for f in fs:
            (front if f.normal.y < -0.9 else back).append(f)
        if lod == 0:   # arkadaki iki yatay kuşaklama profili
            for k in (0.22, 0.78):
                zc = z0 + h * k
                back += add_box(bm, x0 + 0.06, PANEL_T, zc - 0.03, x1 - 0.06, BEHIND + 0.02, zc + 0.03)
    for part in spec["parts"]:
        kind = part[0]
        if kind == "box":
            if lod > 0 and abs(part[6] - part[3]) < 0.06:   # LOD1: ince plakalar yok
                continue
            metal += add_box(bm, *part[1:])
        elif kind == "cyl":
            x, y, z0, z1, r = part[1:]
            metal += add_cyl(bm, x, y, z0, z1, r, 16 if lod == 0 else 6)
        elif kind == "concrete":
            conc += add_box(bm, *part[1:])
    bm.normal_update()
    map_region(bm, metal, R_METAL)
    map_region(bm, back, R_BACK)
    map_region(bm, conc, R_CONC)
    map_region(bm, front, R_FACE, inset=0.0)
    # Ön yüz: u soldan sağa (önden bakınca), v aşağıdan yukarı — map_region (X,Z) projeksiyonu zaten böyle

    oname = name if lod == 0 else f"{name}_LOD{lod}"
    me = bpy.data.meshes.new(oname)
    bm.to_mesh(me)
    bm.free()
    mat = bpy.data.materials.get(oname) or bpy.data.materials.new(oname)
    me.materials.append(mat)
    obj = bpy.data.objects.new(oname, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def export(obj, path):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH"},
        axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_NONE", global_scale=1.0,
        mesh_smooth_type="FACE", use_tspace=True, use_triangles=True,
        use_mesh_modifiers=True, add_leaf_bones=False, path_mode="STRIP", embed_textures=False)


def main(out, only=None):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0
    for name, spec in ASSETS.items():
        if only and name not in only:
            continue
        folder = os.path.join(out, name)
        os.makedirs(folder, exist_ok=True)
        for lod in (0, 1):
            obj = build_mesh(name, spec, lod)
            export(obj, os.path.join(folder, obj.name + ".fbx"))
            tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
            print(f"{obj.name}: {tris} üçgen")
        make_textures(folder, name)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, "SignsPlus_assets.blend" if not only else "SignsPlus_assets_" + "_".join(only) + ".blend"))


if __name__ == "__main__":
    # python build_assets.py <çıktı> [asset adı ...]  (ad verilirse yalnızca o assetler)
    main(sys.argv[1], sys.argv[2:] or None)
