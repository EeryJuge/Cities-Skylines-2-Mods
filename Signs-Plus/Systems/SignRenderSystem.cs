using System;
using System.Collections.Generic;
using Game;
using Game.City;
using Game.Common;
using Game.Prefabs;
using SignsPlus.Data;
using SignsPlus.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using ObjectTransform = Game.Objects.Transform;

namespace SignsPlus.Systems
{
    /// <summary>
    /// Şehirdeki Signs+ assetlerini bulur ve her panel yüzünün hemen önüne o levhanın kendi yazısını çizer.
    /// Oyunun nesne çizimine dokunmaz; yazı katmanı Graphics.RenderMesh ile ayrıca gönderilir. Harmony yok.
    /// </summary>
    public partial class SignRenderSystem : GameSystemBase
    {
        private const int kRescanFrames = 30;
        private const int kEvictFrames = 600;

        public sealed class Instance
        {
            public Entity Entity;
            public string Asset;
            public string Key;
            public float3 Position;
            public quaternion Rotation;
            public FaceDef[] Faces;
            public string[] ContentKeys;
            public string[] ShapeIds;      // Standard/Plate yüzlerin ağ şekli; Panel için null
            public bool[] Hidden;          // boş ek levha çizilmez
        }

        private sealed class AssetInfo
        {
            public string Asset;
            public FaceDef[] Faces;
        }

        private sealed class TexEntry
        {
            public Texture2D Texture;
            public MaterialPropertyBlock Block;
            public int LastFrame;
        }

        private PrefabSystem m_PrefabSystem;
        private CityConfigurationSystem m_CityConfig;
        private EntityQuery m_Query;
        private readonly Dictionary<Entity, AssetInfo> m_PrefabInfo = new Dictionary<Entity, AssetInfo>();
        private readonly List<Instance> m_Instances = new List<Instance>();
        private readonly Dictionary<Entity, Instance> m_ByEntity = new Dictionary<Entity, Instance>();
        private readonly Dictionary<string, TexEntry> m_Textures = new Dictionary<string, TexEntry>();
        private int m_Frame, m_Clock, m_LastCount = -1, m_KeysVersion = -1;
        private string m_KeysSettings;
        private Mesh m_Quad;
        private readonly Dictionary<string, Mesh[]> m_ShapeMeshes = new Dictionary<string, Mesh[]>();   // [ön, arka]
        private Texture2D m_BackTexture;
        private MaterialPropertyBlock m_BackBlock;
        private Material m_Material;
        private Setting.ShaderMode m_MaterialMode;
        private bool m_Failed;

        public SignStore Store { get; } = new SignStore();
        public IReadOnlyList<Instance> Instances => m_Instances;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_CityConfig = World.GetOrCreateSystemManaged<CityConfigurationSystem>();
            m_Query = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Objects.Object>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<ObjectTransform>(),
                },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
            Mod.log.Info($"{nameof(SignRenderSystem)} hazır. Tanınan assetler: {string.Join(", ", Faces.ByAsset.Keys)}");
        }

        protected override void OnUpdate()
        {
            var settings = Mod.Settings;
            if (settings == null || m_Failed) return;
            try
            {
                m_Clock++;
                Store.Tick();
                if (++m_Frame >= kRescanFrames || m_LastCount < 0)
                {
                    m_Frame = 0;
                    Store.Open(m_CityConfig != null ? m_CityConfig.cityName : null);
                    Rescan(settings);
                }
                if (!settings.Enabled || m_Instances.Count == 0) return;

                string settingsKey = $"{settings.PixelsPerMeter}#{(int)settings.DefaultCategory}";
                if (m_KeysVersion != Store.Version || m_KeysSettings != settingsKey)
                {
                    foreach (var inst in m_Instances) UpdateContentKeys(inst, settings);
                    m_KeysVersion = Store.Version;
                    m_KeysSettings = settingsKey;
                }

                EnsureMaterial(settings);
                var rp = new RenderParams(m_Material)
                {
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = true,
                    layer = 0,
                };
                float offset = settings.FaceOffsetMm / 1000f;
                foreach (var inst in m_Instances)
                {
                    var obj = Matrix4x4.TRS(inst.Position, inst.Rotation, Vector3.one);
                    for (int i = 0; i < inst.Faces.Length; i++)
                    {
                        var face = inst.Faces[i];
                        if (inst.Hidden[i]) continue;
                        rp.matProps = GetBlock(inst, i, face, settings);
                        float x = settings.MirrorX ? -face.X : face.X;
                        var rot = settings.FlipFace ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
                        float dz = settings.FlipFace ? -offset : offset;
                        var scale = new Vector3(face.Width, face.Height, 1f);
                        Matrix4x4 local = Matrix4x4.TRS(new Vector3(x, face.Y, face.Z + dz), rot, scale);
                        if (face.Kind == FaceKind.Panel)
                        {
                            Graphics.RenderMesh(rp, m_Quad, 0, obj * local);
                            continue;
                        }
                        // Standart işaret ve ek levha: arkasında panel yok; şekilli ön yüz + gri arka
                        var meshes = GetShapeMeshes(inst.ShapeIds[i]);
                        Graphics.RenderMesh(rp, meshes[0], 0, obj * local);
                        rp.matProps = m_BackBlock;
                        Graphics.RenderMesh(rp, meshes[1], 0, obj * Matrix4x4.TRS(new Vector3(x, face.Y, face.Z), rot, scale));
                    }
                }

                if (m_Clock % 300 == 0) EvictTextures();
            }
            catch (Exception e)
            {
                // Her karede aynı hatayı yazmamak için çizim kapatılır; ayrıntı günlükte
                m_Failed = true;
                Mod.log.Error(e, "Signs+ çizimi durduruldu");
            }
        }

        /* ------------------------------------------------------------------ levhaları bulma */

        private void Rescan(Setting settings)
        {
            m_Instances.Clear();
            m_ByEntity.Clear();
            var entities = m_Query.ToEntityArray(Allocator.Temp);
            var refs = m_Query.ToComponentDataArray<PrefabRef>(Allocator.Temp);
            var transforms = m_Query.ToComponentDataArray<ObjectTransform>(Allocator.Temp);
            try
            {
                for (int i = 0; i < refs.Length; i++)
                {
                    var info = GetInfo(refs[i].m_Prefab, settings);
                    if (info == null) continue;
                    var inst = new Instance
                    {
                        Entity = entities[i],
                        Asset = info.Asset,
                        Key = SignStore.MakeKey(info.Asset, transforms[i].m_Position),
                        Position = transforms[i].m_Position,
                        Rotation = transforms[i].m_Rotation,
                        Faces = info.Faces,
                        ContentKeys = new string[info.Faces.Length],
                        ShapeIds = new string[info.Faces.Length],
                        Hidden = new bool[info.Faces.Length],
                    };
                    UpdateContentKeys(inst, settings);
                    m_Instances.Add(inst);
                    m_ByEntity[inst.Entity] = inst;
                }
            }
            finally
            {
                entities.Dispose();
                refs.Dispose();
                transforms.Dispose();
            }
            if (m_Instances.Count != m_LastCount)
            {
                // Yerleştirme önizlemesi de sayıldığı için sayı sık değişebilir; yalnızca ilk sefer ve büyük farkta yaz
                if (m_LastCount < 0 || Math.Abs(m_Instances.Count - m_LastCount) > 1)
                    Mod.log.Info($"Signs+ levha sayısı: {m_Instances.Count}");
                m_LastCount = m_Instances.Count;
            }
        }

        private AssetInfo GetInfo(Entity prefab, Setting settings)
        {
            if (m_PrefabInfo.TryGetValue(prefab, out var cached)) return cached;
            AssetInfo info = null;
            string name = null;
            if (m_PrefabSystem.TryGetPrefab(prefab, out PrefabBase pb) && pb != null) name = pb.name;
            if (name != null)
            {
                string asset = MatchAsset(name);
                if (asset != null) info = new AssetInfo { Asset = asset, Faces = Faces.ByAsset[asset] };
                string n = Normalize(name);
                if (settings.LogPrefabNames && (info != null || n.Contains("sign") || n.Contains("levha") || n.Contains("gantry") || n.Contains("direk")))
                    Mod.log.Info($"Prefab \"{name}\" ({pb.GetType().Name}) → {(info != null ? info.Faces.Length + " levha yüzü" : "Signs+ assetiyle eşleşmedi")}");
            }
            m_PrefabInfo[prefab] = info;
            return info;
        }

        /// <summary>Prefab adı Signs+ assetlerinden hangisini içeriyor (harf/rakam dışı karakterler yok sayılır).</summary>
        public static string MatchAsset(string prefabName)
        {
            string n = Normalize(prefabName);
            foreach (var key in Faces.ByAsset.Keys)
                if (n.Contains(Normalize(key))) return key;
            return null;
        }

        public static string Normalize(string s)
        {
            var chars = new List<char>(s.Length);
            foreach (var c in s.ToLowerInvariant())
                if (char.IsLetterOrDigit(c)) chars.Add(c);
            return new string(chars.ToArray());
        }

        /* ------------------------------------------------------------------ arayüz için */

        public bool TryGetInstance(Entity e, out Instance inst) => m_ByEntity.TryGetValue(e, out inst);

        public FaceContent GetContent(Instance inst, string faceId)
        {
            if (Store.TryGet(inst.Key, faceId, out var c)) return c;
            return new FaceContent { Category = Mod.Settings != null ? Mod.Settings.DefaultCategory : SignCategory.Normal, Spec = string.Empty };
        }

        /// <summary>Yeni yerleştirilen bir standart işaret direğinin ilk görünümü.</summary>
        public static string DefaultStandardSpec => StandardSigns.DefaultId;

        /// <summary>Ekrandaki bir noktaya (piksel) en yakın Signs+ levhası; yoksa Entity.Null.</summary>
        public Entity PickAtScreen(Vector2 screen)
        {
            var cam = Camera.main;
            if (cam == null) return Entity.Null;
            var ray = cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f));
            Entity best = Entity.Null;
            float bestScore = float.MaxValue;
            foreach (var inst in m_Instances)
            {
                var obj = Matrix4x4.TRS(inst.Position, inst.Rotation, Vector3.one);
                foreach (var f in inst.Faces)
                {
                    Vector3 c = obj.MultiplyPoint3x4(new Vector3(f.X, f.Y, f.Z));
                    Vector3 d = c - ray.origin;
                    float along = Vector3.Dot(d, ray.direction);
                    if (along <= 1f) continue;
                    float off = (d - ray.direction * along).magnitude;
                    float radius = Mathf.Max(f.Width, f.Height) * 0.75f + along * 0.01f;
                    if (off > radius) continue;
                    float score = off / radius + along * 0.0005f;
                    if (score < bestScore) { bestScore = score; best = inst.Entity; }
                }
            }
            return best;
        }

        /* ------------------------------------------------------------------ dokular */

        private void UpdateContentKeys(Instance inst, Setting settings)
        {
            for (int i = 0; i < inst.Faces.Length; i++)
            {
                var f = inst.Faces[i];
                var c = GetContent(inst, f.Id);
                switch (f.Kind)
                {
                    case FaceKind.Standard:
                    {
                        var sign = StandardSigns.Resolve(c.Spec, out var param);
                        inst.ShapeIds[i] = sign.Id;
                        inst.Hidden[i] = false;
                        inst.ContentKeys[i] = $"std|{f.Width:0.###}|{settings.PixelsPerMeter}|{sign.Id}:{param}";
                        break;
                    }
                    case FaceKind.Plate:
                        inst.ShapeIds[i] = "#plate";
                        inst.Hidden[i] = string.IsNullOrWhiteSpace(c.Spec);
                        inst.ContentKeys[i] = $"plate|{f.Width:0.###}x{f.Height:0.###}|{settings.PixelsPerMeter}|{c.Spec}";
                        break;
                    default:
                        inst.ShapeIds[i] = null;
                        inst.Hidden[i] = false;
                        inst.ContentKeys[i] = $"{f.Width:0.###}x{f.Height:0.###}|{(int)c.Category}|{settings.PixelsPerMeter}|{c.Spec}";
                        break;
                }
            }
        }

        private MaterialPropertyBlock GetBlock(Instance inst, int faceIndex, FaceDef face, Setting settings)
        {
            string key = inst.ContentKeys[faceIndex];
            if (key != null && m_Textures.TryGetValue(key, out var entry))
            {
                entry.LastFrame = m_Clock;
                return entry.Block;
            }
            var c = GetContent(inst, face.Id);
            string texName = "SignsPlus " + inst.Asset + " " + face.Id;
            Texture2D tex;
            switch (face.Kind)
            {
                case FaceKind.Standard: tex = StandardSigns.Compose(c.Spec, face.Width, settings.PixelsPerMeter, texName); break;
                case FaceKind.Plate: tex = SignComposer.Compose(c.Spec, SignCategory.Normal, face.Width, face.Height, settings.PixelsPerMeter, texName); break;
                default: tex = SignComposer.Compose(c.Spec, c.Category, face.Width, face.Height, settings.PixelsPerMeter, texName); break;
            }
            var block = new MaterialPropertyBlock();
            block.SetTexture("_BaseColorMap", tex);   // HDRP/Lit
            block.SetTexture("_UnlitColorMap", tex);  // HDRP/Unlit
            block.SetTexture("_MainTex", tex);        // yedek gölgelendiriciler
            m_Textures[key ?? string.Empty] = new TexEntry { Texture = tex, Block = block, LastFrame = m_Clock };
            return block;
        }

        private void EvictTextures()
        {
            List<string> old = null;
            foreach (var kv in m_Textures)
                if (m_Clock - kv.Value.LastFrame > kEvictFrames) (old ?? (old = new List<string>())).Add(kv.Key);
            if (old == null) return;
            foreach (var k in old)
            {
                UnityEngine.Object.Destroy(m_Textures[k].Texture);
                m_Textures.Remove(k);
            }
        }

        /* ------------------------------------------------------------------ şekilli yüzler */

        private static readonly Vector2[] kPlateOutline = { new Vector2(0f, 0f), new Vector2(100f, 0f), new Vector2(100f, 100f), new Vector2(0f, 100f) };

        private Mesh[] GetShapeMeshes(string shapeId)
        {
            if (m_ShapeMeshes.TryGetValue(shapeId, out var m)) return m;
            Vector2[] outline = kPlateOutline;
            if (shapeId != "#plate" && StandardSigns.TryGet(shapeId, out var sign) && sign.Outline != null && sign.Outline.Length >= 3) outline = sign.Outline;
            m = new[] { BuildShape(outline, true, "SignsPlus " + shapeId), BuildShape(outline, false, "SignsPlus " + shapeId + " arka") };
            m_ShapeMeshes[shapeId] = m;
            return m;
        }

        /// <summary>
        /// Dışbükey dış hattan (100'lük kutu, y aşağı) tek yüzlü ağ: ön yüz +Z'ye, arka yüz -Z'ye bakar.
        /// Yerel koordinat: x = 0,5 - px/100 (önden bakınca sağ = -X), y = 0,5 - py/100. UV dokuyla aynı (u = px/100).
        /// </summary>
        private static Mesh BuildShape(Vector2[] outline, bool front, string name)
        {
            int n = outline.Length;
            var verts = new Vector3[n + 1];
            var uv = new Vector2[n + 1];
            float cx = 0f, cy = 0f;
            for (int i = 0; i < n; i++)
            {
                verts[i] = new Vector3(0.5f - outline[i].x / 100f, 0.5f - outline[i].y / 100f, 0f);
                uv[i] = new Vector2(outline[i].x / 100f, 1f - outline[i].y / 100f);
                cx += verts[i].x; cy += verts[i].y;
            }
            verts[n] = new Vector3(cx / n, cy / n, 0f);
            uv[n] = new Vector2(0.5f - verts[n].x, verts[n].y + 0.5f);
            var tris = new int[n * 3];
            for (int i = 0; i < n; i++)
            {
                int a = n, b = i, c = (i + 1) % n;
                // Üçgenin yüzü cross(b-a, c-a) yönüne bakar (Unity'nin kendi dörtgeniyle aynı kural)
                float z = (verts[b].x - verts[a].x) * (verts[c].y - verts[a].y) - (verts[b].y - verts[a].y) * (verts[c].x - verts[a].x);
                bool facesPlusZ = z > 0f;
                if (facesPlusZ != front) { int t = b; b = c; c = t; }
                tris[3 * i] = a; tris[3 * i + 1] = b; tris[3 * i + 2] = c;
            }
            var normal = front ? Vector3.forward : Vector3.back;
            var normals = new Vector3[n + 1];
            var tangents = new Vector4[n + 1];
            for (int i = 0; i <= n; i++) { normals[i] = normal; tangents[i] = new Vector4(-1f, 0f, 0f, 1f); }
            var m = new Mesh { name = name, vertices = verts, uv = uv, normals = normals, tangents = tangents, triangles = tris };
            m.RecalculateBounds();
            return m;
        }

        private void EnsureMaterial(Setting settings)
        {
            if (m_Quad == null) m_Quad = BuildQuad();
            if (m_BackBlock == null)
            {
                // Alüminyum levha arkası
                m_BackTexture = new Texture2D(4, 4, TextureFormat.RGBA32, false, false) { name = "SignsPlus Back" };
                var px = new Color32[16];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(140, 143, 145, 255);
                m_BackTexture.SetPixels32(px);
                m_BackTexture.Apply(false, true);
                m_BackBlock = new MaterialPropertyBlock();
                m_BackBlock.SetTexture("_BaseColorMap", m_BackTexture);
                m_BackBlock.SetTexture("_UnlitColorMap", m_BackTexture);
                m_BackBlock.SetTexture("_MainTex", m_BackTexture);
            }
            if (m_Material != null && m_MaterialMode == settings.Shader) return;
            if (m_Material != null) UnityEngine.Object.Destroy(m_Material);
            m_MaterialMode = settings.Shader;

            string[] order;
            switch (settings.Shader)
            {
                case Setting.ShaderMode.Lit: order = new[] { "HDRP/Lit" }; break;
                case Setting.ShaderMode.Unlit: order = new[] { "HDRP/Unlit" }; break;
                default: order = new[] { "HDRP/Lit", "HDRP/Unlit", "Unlit/Texture", "Standard" }; break;
            }
            Shader shader = null;
            foreach (var n in order)
            {
                shader = Shader.Find(n);
                if (shader != null) { Mod.log.Info($"Levha yüzü gölgelendiricisi: {n}"); break; }
                Mod.log.Warn($"Gölgelendirici bulunamadı: {n}");
            }
            if (shader == null) throw new InvalidOperationException("Kullanılabilir gölgelendirici yok");
            m_Material = new Material(shader) { name = "SignsPlus Face", enableInstancing = false };
            if (m_Material.HasProperty("_BaseColor")) m_Material.SetColor("_BaseColor", Color.white);
            if (m_Material.HasProperty("_UnlitColor")) m_Material.SetColor("_UnlitColor", Color.white);
            if (m_Material.HasProperty("_Smoothness")) m_Material.SetFloat("_Smoothness", 0.35f);
            if (m_Material.HasProperty("_Metallic")) m_Material.SetFloat("_Metallic", 0f);
        }

        /// <summary>1×1 m dörtgen, +Z'ye bakar. Önden bakınca sağ taraf -X'tir (Blender → oyun eksen dönüşümü, oyunda doğrulandı).</summary>
        private static Mesh BuildQuad()
        {
            var m = new Mesh { name = "SignsPlus Face Quad" };
            m.vertices = new[] { new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            m.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            m.tangents = new[] { new Vector4(-1f, 0f, 0f, 1f), new Vector4(-1f, 0f, 0f, 1f), new Vector4(-1f, 0f, 0f, 1f), new Vector4(-1f, 0f, 0f, 1f) };
            // İki sarım yönü de eklenir; arkası panelle örtülür
            m.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            return m;
        }

        protected override void OnDestroy()
        {
            Store.Flush();
            foreach (var t in m_Textures.Values) UnityEngine.Object.Destroy(t.Texture);
            m_Textures.Clear();
            if (m_Material != null) UnityEngine.Object.Destroy(m_Material);
            if (m_Quad != null) UnityEngine.Object.Destroy(m_Quad);
            foreach (var m in m_ShapeMeshes.Values) { UnityEngine.Object.Destroy(m[0]); UnityEngine.Object.Destroy(m[1]); }
            if (m_BackTexture != null) UnityEngine.Object.Destroy(m_BackTexture);
            base.OnDestroy();
        }
    }
}
