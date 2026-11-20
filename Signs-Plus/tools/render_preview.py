"""Önizleme: assetleri sırayla dizer, panellere örnek levha dokusu koyar ve render alır."""
import sys, os, math, bpy
out, signs = sys.argv[1], sys.argv[2:]
bpy.ops.wm.open_mainfile(filepath=os.path.join(out, "SignsPlus_assets.blend"))
sc = bpy.context.scene
objs = [o for o in sc.objects if not o.name.endswith("_LOD1")]
for o in sc.objects:
    if o.name.endswith("_LOD1"): o.hide_render = True
order = ["SignsPlusYerLevhasi", "SignsPlusLBayrak", "SignsPlusTDirek", "SignsPlusGantry"]
x = 0.0; si = 0
for name in order:
    o = sc.objects[name]
    xs = [v.co.x for v in o.data.vertices]
    o.location.x = x - min(xs); x += max(xs) - min(xs) + 3.0
    # malzeme: atlas doku + ön yüz için ayrı malzeme (örnek levha)
    mat = o.data.materials[0]; mat.use_nodes = True
    nt = mat.node_tree; bsdf = nt.nodes.get("Principled BSDF")
    tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = bpy.data.images.load(os.path.join(out, name, name + "_BaseColor.png"))
    nt.links.new(tex.outputs[0], bsdf.inputs["Base Color"]); bsdf.inputs["Roughness"].default_value = 0.6
    import bmesh
    me = o.data; fm = []
    for p in me.polygons:
        if p.normal.y < -0.9 and abs(p.center.y) < 0.001: fm.append(p)
    # her panel ön yüzüne sırayla bir örnek levha
    for p in sorted(fm, key=lambda p: p.center.x):
        if si >= len(signs): break
        m2 = bpy.data.materials.new(f"face{si}"); m2.use_nodes = True
        t2 = m2.node_tree.nodes.new("ShaderNodeTexImage"); t2.image = bpy.data.images.load(signs[si])
        b2 = m2.node_tree.nodes.get("Principled BSDF"); m2.node_tree.links.new(t2.outputs[0], b2.inputs["Base Color"])
        b2.inputs["Roughness"].default_value = 0.5
        me.materials.append(m2); p.material_index = len(me.materials) - 1
        # UV'yi 0-1'e aç (önizleme için)
        uv = me.uv_layers.active.data
        xs2 = [me.vertices[me.loops[l].vertex_index].co.x for l in p.loop_indices]
        zs2 = [me.vertices[me.loops[l].vertex_index].co.z for l in p.loop_indices]
        for l in p.loop_indices:
            co = me.vertices[me.loops[l].vertex_index].co
            uv[l].uv = ((co.x - min(xs2)) / (max(xs2) - min(xs2)), (co.z - min(zs2)) / (max(zs2) - min(zs2)))
        si += 1
# zemin
bpy.ops.mesh.primitive_plane_add(size=200, location=(x / 2, 10, 0))
g = bpy.context.object; gm = bpy.data.materials.new("ground"); gm.use_nodes = True
gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.25, 0.3, 0.2, 1); g.data.materials.append(gm)
# ışık, gökyüzü, kamera
sun = bpy.data.lights.new("sun", "SUN"); sun.energy = 4; so = bpy.data.objects.new("sun", sun); sc.collection.objects.link(so)
so.rotation_euler = (math.radians(50), 0, math.radians(-30))
world = bpy.data.worlds.new("w"); sc.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.7, 0.9, 1); world.node_tree.nodes["Background"].inputs[1].default_value = 0.8
cam = bpy.data.cameras.new("cam"); cam.lens = 30; co = bpy.data.objects.new("cam", cam); sc.collection.objects.link(co); sc.camera = co
co.location = (x / 2 - 1.5, -44, 4.8); co.rotation_euler = (math.radians(88), 0, 0)
sc.render.resolution_x, sc.render.resolution_y = 2000, 760
sc.render.engine = sys.argv[-1] if False else "CYCLES"
sc.cycles.samples = 24; sc.cycles.device = "CPU"
sc.render.filepath = os.path.join(out, "preview.png")
bpy.ops.render.render(write_still=True)
print("ok")
