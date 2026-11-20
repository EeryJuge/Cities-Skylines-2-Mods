"""Araç çubuğu küçük resimleri: her asset tek başına, örnek yazıyla, saydam arka planda (256×256).

Kullanım: python render_thumbs.py <Assets klasörü> <çıktı klasörü> <asset>=<yüz1.png>[,<yüz2.png>] ...
"""
import sys, os, math, bpy
from mathutils import Vector

assets_dir, out_dir = sys.argv[1], sys.argv[2]
faces = {}
for arg in sys.argv[3:]:
    k, v = arg.split("=", 1)
    faces[k] = v.split(",")

bpy.ops.wm.open_mainfile(filepath=os.path.join(assets_dir, "SignsPlus_assets.blend"))
sc = bpy.context.scene
sc.render.engine = "CYCLES"; sc.cycles.samples = 24; sc.cycles.device = "CPU"
sc.render.film_transparent = True
sc.render.resolution_x = sc.render.resolution_y = 256
world = bpy.data.worlds.new("w"); sc.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.75, 0.82, 0.92, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 0.9
sun = bpy.data.lights.new("sun", "SUN"); sun.energy = 3.5
so = bpy.data.objects.new("sun", sun); sc.collection.objects.link(so); so.rotation_euler = (math.radians(55), 0, math.radians(-35))
cam = bpy.data.cameras.new("cam"); cam.type = "ORTHO"
co = bpy.data.objects.new("cam", cam); sc.collection.objects.link(co); sc.camera = co
os.makedirs(out_dir, exist_ok=True)

for name, pngs in faces.items():
    for o in sc.objects:
        if o.type == "MESH": o.hide_render = o.name != name
    o = sc.objects[name]
    me = o.data
    base = me.materials[0]; base.use_nodes = True
    nt = base.node_tree; bsdf = nt.nodes.get("Principled BSDF")
    t = nt.nodes.new("ShaderNodeTexImage"); t.image = bpy.data.images.load(os.path.join(assets_dir, name, name + "_BaseColor.png"))
    nt.links.new(t.outputs[0], bsdf.inputs["Base Color"]); bsdf.inputs["Roughness"].default_value = 0.6
    fronts = sorted([p for p in me.polygons if p.normal.y < -0.9 and abs(p.center.y) < 0.001], key=lambda p: p.center.x)
    uv = me.uv_layers.active.data
    for p, png in zip(fronts, pngs):
        m2 = bpy.data.materials.new(name + png); m2.use_nodes = True
        t2 = m2.node_tree.nodes.new("ShaderNodeTexImage"); t2.image = bpy.data.images.load(png)
        b2 = m2.node_tree.nodes.get("Principled BSDF"); m2.node_tree.links.new(t2.outputs[0], b2.inputs["Base Color"])
        b2.inputs["Roughness"].default_value = 0.5
        me.materials.append(m2); p.material_index = len(me.materials) - 1
        xs = [me.vertices[me.loops[l].vertex_index].co.x for l in p.loop_indices]
        zs = [me.vertices[me.loops[l].vertex_index].co.z for l in p.loop_indices]
        for l in p.loop_indices:
            c = me.vertices[me.loops[l].vertex_index].co
            uv[l].uv = ((c.x - min(xs)) / (max(xs) - min(xs)), (c.z - min(zs)) / (max(zs) - min(zs)))
    # kamerayı nesneye sığdır: önden, biraz soldan ve yukarıdan
    xs = [v.co.x for v in me.vertices]; zs = [v.co.z for v in me.vertices]
    cx, cz = (min(xs) + max(xs)) / 2, (min(zs) + max(zs)) / 2
    size = max(max(xs) - min(xs), max(zs) - min(zs)) * 1.08
    cam.ortho_scale = size
    d = Vector((-0.35, -1.0, 0.18)).normalized()
    co.location = Vector((cx, 0, cz)) + d * 60
    co.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    sc.render.filepath = os.path.join(out_dir, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("küçük resim:", name)
