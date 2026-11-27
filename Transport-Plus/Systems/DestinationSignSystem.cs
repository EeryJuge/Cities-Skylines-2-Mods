using System;
using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Rendering;
using Game.Routes;
using Game.SceneFlow;
using Game.Tools;
using Game.UI;
using Game.Vehicles;
using TransportPlus.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace TransportPlus.Systems
{
    /// <summary>
    /// Otobüslerin hat tabelaları (LED): önde "93B  HACIOSMAN", dönüşte "93B  TOPKAPI"; arkada hat kodu, sağ yanda kod + yön.
    ///
    /// - Yazı hat adından gelir (LineNames.Parse): "93B Topkapı Hacıosman" → kod 93B, uçlar Topkapı ve Hacıosman.
    ///   Ad verilmemişse kod hat numarası, uçlar ilk durak ve ondan en uzak durak.
    /// - Yön: hattın ilk durağından en uzak durak dönüş noktasıdır. Aracın hedef durağı ilk duraktan sonra ve dönüş
    ///   noktasına kadarsa ikinci uca, değilse birinci uca gidiyordur. Garaja dönen araç "SERVİS DIŞI" yazar.
    /// - Çizim oyunun araç çizimine dokunmaz: her tabela aracın ara konumuna (InterpolatedTransform) göre yerleştirilen
    ///   ayrı bir dörtgen olarak Graphics.RenderMesh ile gönderilir (Signs+ ile aynı yöntem). Kayda hiçbir şey yazılmaz.
    /// - Dokular metne göre paylaşılır; kullanılmayan doku 600 kare sonra silinir. Yalnızca kameraya yakın araçlar çizilir.
    /// </summary>
    public partial class DestinationSignSystem : GameSystemBase
    {
        private const int kRefreshFrames = 30;
        private const int kEvictFrames = 600;

        private static readonly Color32 kBack = new Color32(10, 10, 10, 255);
        private static readonly Color32 kLed = new Color32(255, 170, 24, 255);

        private struct Face { public Vector3 Pos; public Quaternion Rot; public Vector2 Size; public int Kind; }   // Kind 0 ön, 1 arka, 2 yan

        private sealed class VehicleSign
        {
            public string Front, Rear, Side;   // doku anahtarları
            public Face[] Faces;
        }

        private sealed class LineInfo
        {
            public string Code, First, Second;
            public Dictionary<Entity, int> Index;   // yol noktası → sıra
            public int Turn;                         // dönüş noktasının sırası
            public int Version;
        }

        private sealed class TexEntry { public Texture2D Texture; public MaterialPropertyBlock Block; public int LastFrame; }

        private EntityQuery m_Query;
        private NameSystem m_Names;
        private readonly Dictionary<Entity, VehicleSign> m_Signs = new Dictionary<Entity, VehicleSign>();
        private readonly Dictionary<Entity, LineInfo> m_Lines = new Dictionary<Entity, LineInfo>();
        private readonly Dictionary<Entity, Face[]> m_PrefabFaces = new Dictionary<Entity, Face[]>();
        private readonly Dictionary<string, TexEntry> m_Textures = new Dictionary<string, TexEntry>();
        private Mesh m_Quad;
        private Material m_Material;
        private int m_Frame, m_Clock, m_LineVersion;
        private bool m_Failed, m_Logged;
        private PrefabSystem m_PrefabSystem;
        private int m_MeasureBudget;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Names = World.GetOrCreateSystemManaged<NameSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Query = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Vehicles.PublicTransport>(),
                    ComponentType.ReadOnly<CurrentRoute>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<InterpolatedTransform>(),
                },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() }
            });
        }

        protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Signs.Clear();
            m_Lines.Clear();
            m_PrefabFaces.Clear();
            m_Failed = false;
        }

        protected override void OnUpdate()
        {
            Setting s = Mod.Settings;
            if (m_Failed || s == null || !s.LedSigns) return;
            if (m_Query.IsEmptyIgnoreFilter) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            try
            {
                m_Clock++;
                bool refresh = ++m_Frame >= kRefreshFrames;
                if (refresh)
                {
                    m_Frame = 0;
                    m_LineVersion++;   // hat adları ve duraklar 30 karede bir yeniden okunur
                    Refresh();
                }
                if (m_Signs.Count == 0) return;
                EnsureMaterial();

                Vector3 camPos = cam.transform.position;
                float maxDist = Mathf.Clamp(s.LedSignDistance, 100, 2000);
                float maxSq = maxDist * maxDist;
                var rp = new RenderParams(m_Material) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, layer = 0 };

                NativeArray<Entity> entities = m_Query.ToEntityArray(Allocator.Temp);
                NativeArray<InterpolatedTransform> transforms = m_Query.ToComponentDataArray<InterpolatedTransform>(Allocator.Temp);
                try
                {
                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (!m_Signs.TryGetValue(entities[i], out VehicleSign sign)) continue;
                        InterpolatedTransform t = transforms[i];
                        Vector3 p = t.m_Position;
                        if ((p - camPos).sqrMagnitude > maxSq) continue;
                        var world = Matrix4x4.TRS(p, t.m_Rotation, Vector3.one);
                        foreach (Face f in sign.Faces)
                        {
                            string key = f.Kind == 0 ? sign.Front : f.Kind == 1 ? sign.Rear : sign.Side;
                            if (key == null) continue;
                            rp.matProps = GetBlock(key, f);
                            Graphics.RenderMesh(rp, m_Quad, 0, world * Matrix4x4.TRS(f.Pos, f.Rot, new Vector3(f.Size.x, f.Size.y, 1f)));
                        }
                    }
                }
                finally { entities.Dispose(); transforms.Dispose(); }

                if (m_Clock % 300 == 0) Evict();
            }
            catch (Exception e)
            {
                // Her karede aynı hatayı yazmamak için tabelalar kapatılır (şehir yeniden yüklenince yeniden denenir)
                m_Failed = true;
                Mod.log.Error(e, "LED destination signs stopped");
            }
        }

        // ---------------------------------------------------------------- araç başına yazı

        private void Refresh()
        {
            bool turkish = IsTurkish();
            m_MeasureBudget = 2;
            var ex = new TransitExporter(EntityManager, m_Names);
            var seen = new HashSet<Entity>();
            NativeArray<Entity> entities = m_Query.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (Entity v in entities)
                {
                    Entity prefab = EntityManager.GetComponentData<PrefabRef>(v).m_Prefab;
                    Face[] faces = FacesFor(prefab);
                    if (faces == null) continue;   // otobüs değil
                    Entity line = EntityManager.GetComponentData<CurrentRoute>(v).m_Route;
                    if (line == Entity.Null || !EntityManager.Exists(line) || !EntityManager.HasBuffer<RouteWaypoint>(line)) continue;
                    LineInfo info = Line(line, ex);
                    seen.Add(v);

                    string dest;
                    PublicTransportFlags state = EntityManager.GetComponentData<Game.Vehicles.PublicTransport>(v).m_State;
                    if ((state & (PublicTransportFlags.Returning | PublicTransportFlags.RequiresMaintenance)) != 0 || (state & PublicTransportFlags.EnRoute) == 0)
                        dest = turkish ? "SERVİS DIŞI" : "NOT IN SERVICE";
                    else
                    {
                        int target = -1;
                        if (EntityManager.HasComponent<Target>(v))
                        {
                            Entity wp = EntityManager.GetComponentData<Target>(v).m_Target;
                            if (wp != Entity.Null && info.Index.TryGetValue(wp, out int ti)) target = ti;
                        }
                        bool toSecond = target > 0 && target <= info.Turn;
                        dest = toSecond ? info.Second : info.First;
                        if (string.IsNullOrEmpty(dest)) dest = toSecond ? info.First : info.Second;
                    }
                    dest = LineNames.Upper(dest, turkish);
                    string code = info.Code;

                    if (!m_Signs.TryGetValue(v, out VehicleSign sign)) m_Signs[v] = sign = new VehicleSign();
                    sign.Faces = faces;
                    sign.Front = "F|" + code + "|" + dest;
                    sign.Side = "S|" + code + "|" + dest;
                    sign.Rear = string.IsNullOrEmpty(code) ? null : "R|" + code;
                }
            }
            finally { entities.Dispose(); }
            if (seen.Count != m_Signs.Count)
            {
                var gone = new List<Entity>();
                foreach (Entity e in m_Signs.Keys) if (!seen.Contains(e)) gone.Add(e);
                foreach (Entity e in gone) m_Signs.Remove(e);
            }
            if (!m_Logged && m_Signs.Count > 0)
            {
                m_Logged = true;
                Mod.log.Info($"LED destination signs: {m_Signs.Count} buses");
            }
        }

        private LineInfo Line(Entity line, TransitExporter ex)
        {
            if (m_Lines.TryGetValue(line, out LineInfo info) && info.Version == m_LineVersion) return info;
            if (info == null) m_Lines[line] = info = new LineInfo();
            info.Version = m_LineVersion;

            DynamicBuffer<RouteWaypoint> wps = EntityManager.GetBuffer<RouteWaypoint>(line, true);
            info.Index = new Dictionary<Entity, int>(wps.Length);
            float3 start = default;
            float best = -1f;
            int turn = wps.Length / 2;
            var stopNames = new List<(int index, string name)>();
            for (int i = 0; i < wps.Length; i++)
            {
                Entity wp = wps[i].m_Waypoint;
                info.Index[wp] = i;
                if (!EntityManager.HasComponent<Game.Routes.Position>(wp)) continue;
                float3 p = EntityManager.GetComponentData<Game.Routes.Position>(wp).m_Position;
                if (i == 0) start = p;
                float d = math.distancesq(p.xz, start.xz);
                if (d > best) { best = d; turn = i; }
                if (EntityManager.HasComponent<Connected>(wp))
                {
                    Entity stop = EntityManager.GetComponentData<Connected>(wp).m_Connected;
                    string n = "";
                    try { if (stop != Entity.Null && EntityManager.Exists(stop)) n = ex.StationName(ex.TopBuilding(stop), stop); } catch (Exception) { }
                    stopNames.Add((i, n ?? ""));
                }
            }
            info.Turn = turn;

            string custom = ex.CustomName(line);
            LineNames.Parsed parsed = LineNames.Parse(custom);
            int number = EntityManager.HasComponent<RouteNumber>(line) ? EntityManager.GetComponentData<RouteNumber>(line).m_Number : 0;
            info.Code = parsed.Code.Length > 0 ? parsed.Code : number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string firstStop = stopNames.Count > 0 ? stopNames[0].name : "";
            string turnStop = firstStop;
            foreach (var sn in stopNames) if (sn.index <= turn) turnStop = sn.name;
            if (parsed.First.Length == 0 && parsed.Second.Length == 0)
            {
                // Ad yoksa ya da yalnızca koddan oluşuyorsa uçlar durak adlarından
                info.First = firstStop;
                info.Second = turnStop;
            }
            else
            {
                info.First = parsed.First;
                info.Second = parsed.Second;
                // Ad sırası durak sırasının tersiyse (ilk durak ikinci uçta) değiştir
                string kf = LineNames.Key(parsed.First), ks = LineNames.Key(parsed.Second);
                string s0 = LineNames.Key(firstStop), st = LineNames.Key(turnStop);
                bool reversed = ks.Length > 0 && kf.Length > 0 && s0.Contains(ks) && !s0.Contains(kf) && (st.Length == 0 || !st.Contains(ks));
                if (reversed) { info.First = parsed.Second; info.Second = parsed.First; }
            }
            return info;
        }

        private static bool IsTurkish()
        {
            try
            {
                string id = GameManager.instance?.localizationManager?.activeLocaleId;
                return id != null && id.StartsWith("tr", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        // ---------------------------------------------------------------- tabelanın araç üstündeki yeri

        /// <summary>
        /// Otobüs prefabı için tabela yüzleri (aracın yerel ekseninde: ön +Z, yukarı +Y, sağ +X). Otobüs değilse null.
        /// Yer, modelin kendi köşe noktalarından ölçülür: prefab sınır kutusu aynaları ve tavandaki klimayı da içerdiği için
        /// kutuya göre konan tabela aracın önünde ve üstünde havada kalıyordu. Ölçüm her model için bir kez yapılır
        /// (bir yenilemede en çok iki model); model okunamazsa kutudan tahmin edilir.
        /// </summary>
        private Face[] FacesFor(Entity prefab)
        {
            if (m_PrefabFaces.TryGetValue(prefab, out Face[] faces)) return faces;
            if (!(EntityManager.HasComponent<PublicTransportVehicleData>(prefab) && EntityManager.HasComponent<CarData>(prefab)
                && EntityManager.GetComponentData<PublicTransportVehicleData>(prefab).m_TransportType == TransportType.Bus
                && EntityManager.HasComponent<ObjectGeometryData>(prefab)))
            {
                m_PrefabFaces[prefab] = null;
                return null;
            }
            Colossal.Mathematics.Bounds3 b = EntityManager.GetComponentData<ObjectGeometryData>(prefab).m_Bounds;
            if (b.max.x - b.min.x < 1f || b.max.y - b.min.y < 1.5f || b.max.z - b.min.z < 4f)
            {
                m_PrefabFaces[prefab] = null;
                return null;
            }
            if (m_MeasureBudget <= 0) return null;   // sonraki yenilemede ölçülür
            m_MeasureBudget--;
            faces = null;
            try { faces = MeasureFaces(prefab, b); }
            catch (Exception e) { Mod.log.Warn(e, "LED sign: could not read the bus model, using its bounds"); }
            if (faces == null) faces = BoundsFaces(b);
            m_PrefabFaces[prefab] = faces;
            return faces;
        }

        /// <summary>Model okunamazsa: sınır kutusundan, aynalar ve tavan klimasının payı düşülerek.</summary>
        private static Face[] BoundsFaces(Colossal.Mathematics.Bounds3 b)
        {
            float height = b.max.y - b.min.y, length = b.max.z - b.min.z;
            float hw = Mathf.Min(b.max.x, 1.28f);
            float frontW = Mathf.Min(hw * 2f * 0.78f, 2.0f);
            float top = b.max.y - Mathf.Clamp(height * 0.14f, 0.38f, 0.55f);
            float front = b.max.z - 0.2f;
            return new[]
            {
                new Face { Kind = 0, Pos = new Vector3(0f, top, front), Rot = Quaternion.identity, Size = new Vector2(frontW, frontW / 8f) },
                new Face { Kind = 1, Pos = new Vector3(0f, top, b.min.z + 0.02f), Rot = Quaternion.Euler(0f, 180f, 0f), Size = new Vector2(0.62f, 0.31f) },
                new Face { Kind = 2, Pos = new Vector3(hw + 0.02f, top - 0.1f, front - Mathf.Min(2.2f, length * 0.2f)), Rot = Quaternion.Euler(0f, 90f, 0f), Size = new Vector2(1.3f, 1.3f / 8f) },
            };
        }

        /// <summary>
        /// Modeli ölçer. Uç noktalar (gövde genişliği, tavan kenarı, ön/arka uç) köşe noktalarından; ön camın ve arka
        /// camın yüzeyi ise üçgenlere ışın atılarak bulunur (büyük düz yüzeylerde köşe noktası seyrek olduğu için
        /// yalnız köşe noktasına bakınca tabela gövdenin içinde kalıyordu).
        /// </summary>
        private Face[] MeasureFaces(Entity prefab, Colossal.Mathematics.Bounds3 b)
        {
            if (m_PrefabSystem == null || !EntityManager.HasBuffer<Game.Prefabs.SubMesh>(prefab)) return null;
            var subs = new List<Game.Prefabs.SubMesh>();
            foreach (Game.Prefabs.SubMesh sm in EntityManager.GetBuffer<Game.Prefabs.SubMesh>(prefab, true)) subs.Add(sm);

            var tri = new List<Vector3>(30000);
            foreach (Game.Prefabs.SubMesh sm in subs)
            {
                if (!m_PrefabSystem.TryGetPrefab(sm.m_SubMesh, out RenderPrefab render) || render == null) continue;
                Mesh[] meshes = render.ObtainMeshes();
                try
                {
                    if (meshes == null) continue;
                    Quaternion q = sm.m_Rotation;
                    Vector3 off = sm.m_Position;
                    foreach (Mesh m in meshes)
                    {
                        if (m == null || !m.isReadable) continue;
                        Vector3[] v = m.vertices;
                        int[] idx = m.triangles;
                        if (idx.Length > 900000) continue;
                        for (int i = 0; i + 2 < idx.Length; i += 3)
                        {
                            tri.Add(q * v[idx[i]] + off);
                            tri.Add(q * v[idx[i + 1]] + off);
                            tri.Add(q * v[idx[i + 2]] + off);
                        }
                    }
                }
                finally { render.ReleaseMeshes(); }
            }
            if (tri.Count < 150) return null;

            Vector3 mn = tri[0], mx = tri[0];
            foreach (Vector3 p in tri) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
            float L = mx.z - mn.z, H = mx.y - mn.y;
            if (Mathf.Abs(L - (b.max.z - b.min.z)) > 0.25f * (b.max.z - b.min.z) || Mathf.Abs(mx.y - b.max.y) > 0.3f * (b.max.y - b.min.y)) return null;

            // Gövde yarı genişliği ve tavan kenarı: aracın orta %40'ı (aynalar ve tamponlar dışarıda)
            float hw = 0f;
            foreach (Vector3 p in tri) if (p.z > mn.z + 0.3f * L && p.z < mx.z - 0.3f * L && p.y > mn.y + 0.3f * H) hw = Mathf.Max(hw, Mathf.Abs(p.x));
            if (hw < 0.5f) return null;
            float roofEdge = mn.y;
            foreach (Vector3 p in tri) if (p.z > mn.z + 0.3f * L && p.z < mx.z - 0.3f * L && Mathf.Abs(p.x) > hw - 0.15f) roofEdge = Mathf.Max(roofEdge, p.y);

            float fw = Mathf.Min(hw * 2f * 0.78f, 2.0f), fh = fw / 8f;
            float yLow = mn.y + 0.35f * H;

            // Ön yüz: aracın ortasından öne doğru profil; ön yüzün dik kısmının bittiği yer (tavana döndüğü yer) camın üstü
            // Işınlar yalnızca o uca yakın üçgenlere atılır (hız için)
            var frontTri = new List<Vector3>(); var rearTri = new List<Vector3>(); var midTri = new List<Vector3>();
            for (int i = 0; i + 2 < tri.Count; i += 3)
            {
                float zmax = Mathf.Max(tri[i].z, Mathf.Max(tri[i + 1].z, tri[i + 2].z));
                float zmin = Mathf.Min(tri[i].z, Mathf.Min(tri[i + 1].z, tri[i + 2].z));
                List<Vector3> dst = zmax > mx.z - 1.5f ? frontTri : null;
                if (dst != null) { dst.Add(tri[i]); dst.Add(tri[i + 1]); dst.Add(tri[i + 2]); }
                if (zmin < mn.z + 1.5f) { rearTri.Add(tri[i]); rearTri.Add(tri[i + 1]); rearTri.Add(tri[i + 2]); }
                if (zmax > mn.z + 0.5f && zmin < mx.z - 0.5f) { midTri.Add(tri[i]); midTri.Add(tri[i + 1]); midTri.Add(tri[i + 2]); }
            }
            Face front = FaceOnProfile(frontTri, +1, yLow, mx.y, fw, fh, 0f, out float frontTopY, out float frontPitch);
            // Arka yüz
            Face rear = FaceOnProfile(rearTri, -1, yLow, mx.y, 0.62f, 0.31f, 0f, out float rearTopY, out _);
            if (front.Size.x <= 0f) return null;
            if (rear.Size.x <= 0f) rear = new Face { Kind = 1, Pos = new Vector3(0f, roofEdge - 0.3f, mn.z - 0.02f), Rot = Quaternion.Euler(0f, 180f, 0f), Size = new Vector2(0.62f, 0.31f) };
            front.Kind = 0;
            rear.Kind = 1;

            // Yan tabela: sağ yanda, ön kapının arkasındaki ilk camın üst kenarı, gövde yüzeyine ışınla
            float sw = 1.3f, sh = sw / 8f;
            float zS = front.Pos.z - Mathf.Clamp(L * 0.2f, 1.2f, 2.2f);
            float yS = roofEdge - 0.2f - sh * 0.5f;
            float xS = float.MinValue;
            for (int k = -1; k <= 1; k++)
                for (int j = -1; j <= 1; j += 2)
                    if (Cast(midTri, 0, zS + k * sw * 0.45f, yS + j * sh * 0.5f, true, out float hx)) xS = Mathf.Max(xS, hx);
            if (xS < 0.3f || xS > hw + 0.1f) xS = hw;

            Mod.log.Info($"LED sign geometry {m_PrefabSystem.GetPrefabName(prefab)}: {tri.Count / 3} triangles, half width {hw:0.00}, roof {roofEdge:0.00}, " +
                $"front {front.Pos.z:0.00} @ {front.Pos.y:0.00} (face top {frontTopY:0.00}, pitch {frontPitch:0}), rear {rear.Pos.z:0.00} @ {rear.Pos.y:0.00}, side {xS:0.00}, bounds z {b.min.z:0.00}..{b.max.z:0.00} y {b.max.y:0.00}");
            return new[]
            {
                front,
                rear,
                new Face { Kind = 2, Pos = new Vector3(xS + 0.025f, yS, zS), Rot = Quaternion.Euler(0f, 90f, 0f), Size = new Vector2(sw, sh) },
            };
        }

        /// <summary>
        /// Ön (dir +1) ya da arka (dir -1) yüzün x = 0 hattındaki profilini 2 cm aralıkla çıkarır, yüzün dik kısmının en
        /// üstünü bulur ve tabelayı onun hemen altına, yüzeyin eğimine göre yatırarak koyar. Bulunamazsa Size sıfır.
        /// </summary>
        private static Face FaceOnProfile(List<Vector3> tri, int dir, float yLow, float yHigh, float w, float h, float x0, out float faceTop, out float pitch)
        {
            faceTop = 0f; pitch = 0f;
            const float kStep = 0.02f;
            int n = Mathf.Max(2, Mathf.CeilToInt((yHigh - yLow) / kStep) + 1);
            var zs = new float[n];
            var hit = new bool[n];
            float extreme = dir > 0 ? float.MinValue : float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                float y = yLow + i * kStep;
                float best = dir > 0 ? float.MinValue : float.MaxValue;
                bool any = false;
                // Ortada ve iki yanda; en önde (arkada) olan alınır ki tabela kavisli camın içinde kalmasın
                for (int k = -1; k <= 1; k++)
                {
                    if (!Cast(tri, 2, x0 + k * w * 0.35f, y, dir > 0, out float z)) continue;
                    any = true;
                    best = dir > 0 ? Mathf.Max(best, z) : Mathf.Min(best, z);
                }
                hit[i] = any;
                zs[i] = best;
                if (any) extreme = dir > 0 ? Mathf.Max(extreme, best) : Mathf.Min(extreme, best);
            }
            if (extreme == float.MinValue || extreme == float.MaxValue) return default;

            // Yüzün dik kısımları: en uç noktaya 0,7 m'den yakın, 2 cm yükselirken en çok 1,2 cm geri çekilen (yaklaşık 30°)
            // ve 5 cm'den fazla öne çıkmayan ardışık örnekler. Tampon, kaput ve tavan kıvrımı bu dizileri böler; en uzun
            // dik dizi ön cam (arkada arka cam) sayılır ve tabela onun üst kenarına konur.
            int top = -1, bestLen = -1, runStart = -1;
            for (int i = 0; i < n; i++)
            {
                bool ok = hit[i] && Mathf.Abs(zs[i] - extreme) < 0.7f;
                bool cont = ok && runStart >= 0 && hit[i - 1]
                    && (zs[i - 1] - zs[i]) * dir <= 0.012f && (zs[i] - zs[i - 1]) * dir <= 0.05f;
                if (!cont) runStart = ok ? i : -1;
                if (runStart < 0) continue;
                int len = i - runStart;
                if (len >= bestLen) { bestLen = len; top = i; }
            }
            if (bestLen < 5) top = -1;   // 10 cm'den kısa dik yüz yok
            if (top < 0) return default;
            faceTop = yLow + top * kStep;
            float yc = faceTop - 0.04f - h * 0.5f;
            int iUp = Mathf.Clamp(Mathf.RoundToInt((yc + h * 0.5f - yLow) / kStep), 0, n - 1);
            int iDn = Mathf.Clamp(Mathf.RoundToInt((yc - h * 0.5f - yLow) / kStep), 0, n - 1);
            float zUp = hit[iUp] ? zs[iUp] : extreme, zDn = hit[iDn] ? zs[iDn] : zUp;
            // Eğim: üst kenar alttan ne kadar geride; tabela yüzeye paralel durur
            float back = (zDn - zUp) * dir;
            pitch = Mathf.Clamp(Mathf.Atan2(back, h) * Mathf.Rad2Deg, -10f, 45f);
            float zc = (zUp + zDn) * 0.5f + 0.03f * dir;
            Quaternion rot = dir > 0 ? Quaternion.Euler(-pitch, 0f, 0f) : Quaternion.Euler(-pitch, 180f, 0f);
            return new Face { Pos = new Vector3(x0, yc, zc), Rot = rot, Size = new Vector2(w, h) };
        }

        /// <summary>
        /// Eksene paralel bir doğrunun üçgenlerle kesişimi. axis 0: x boyunca (u = z, v = y); 1: y boyunca (u = x, v = z);
        /// 2: z boyunca (u = x, v = y). En büyük (max) ya da en küçük kesişim değeri.
        /// </summary>
        private static bool Cast(List<Vector3> tri, int axis, float pu, float pv, bool max, out float result)
        {
            result = max ? float.MinValue : float.MaxValue;
            bool any = false;
            for (int i = 0; i + 2 < tri.Count; i += 3)
            {
                Vector3 a = tri[i], b = tri[i + 1], c = tri[i + 2];
                float au, av, aw, bu, bv, bw, cu, cv, cw;
                if (axis == 0) { au = a.z; av = a.y; aw = a.x; bu = b.z; bv = b.y; bw = b.x; cu = c.z; cv = c.y; cw = c.x; }
                else if (axis == 1) { au = a.x; av = a.z; aw = a.y; bu = b.x; bv = b.z; bw = b.y; cu = c.x; cv = c.z; cw = c.y; }
                else { au = a.x; av = a.y; aw = a.z; bu = b.x; bv = b.y; bw = b.z; cu = c.x; cv = c.y; cw = c.z; }
                if (pu < Mathf.Min(au, Mathf.Min(bu, cu)) || pu > Mathf.Max(au, Mathf.Max(bu, cu))) continue;
                if (pv < Mathf.Min(av, Mathf.Min(bv, cv)) || pv > Mathf.Max(av, Mathf.Max(bv, cv))) continue;
                float d = (bv - cv) * (au - cu) + (cu - bu) * (av - cv);
                if (Mathf.Abs(d) < 1e-9f) continue;
                float l1 = ((bv - cv) * (pu - cu) + (cu - bu) * (pv - cv)) / d;
                float l2 = ((cv - av) * (pu - cu) + (au - cu) * (pv - cv)) / d;
                float l3 = 1f - l1 - l2;
                if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) continue;
                float w = l1 * aw + l2 * bw + l3 * cw;
                result = max ? Mathf.Max(result, w) : Mathf.Min(result, w);
                any = true;
            }
            return any;
        }

        // ---------------------------------------------------------------- dokular

        private MaterialPropertyBlock GetBlock(string key, Face face)
        {
            if (m_Textures.TryGetValue(key, out TexEntry e)) { e.LastFrame = m_Clock; return e.Block; }
            Texture2D tex = Compose(key);
            var block = new MaterialPropertyBlock();
            block.SetTexture("_UnlitColorMap", tex);   // HDRP/Unlit
            block.SetTexture("_BaseColorMap", tex);    // HDRP/Lit
            block.SetTexture("_MainTex", tex);         // yedek
            m_Textures[key] = new TexEntry { Texture = tex, Block = block, LastFrame = m_Clock };
            return block;
        }

        /// <summary>"F|kod|yön" (ön, 8:1), "S|kod|yön" (yan, 8:1), "R|kod" (arka, 2:1).</summary>
        private static Texture2D Compose(string key)
        {
            string[] p = key.Split('|');
            FontAtlas font = FontAtlas.Bold;
            bool rear = p[0] == "R";
            int w = rear ? 192 : 768, h = 96;
            var r = new LedRasterizer(w, h);
            r.Clear(kBack);
            float pad = h * 0.12f;
            if (rear)
            {
                DrawFit(r, font, p.Length > 1 ? p[1] : "", pad, w - pad, h, h * 0.66f, true);
            }
            else
            {
                string code = p.Length > 1 ? p[1] : "", dest = p.Length > 2 ? p[2] : "";
                float x = pad;
                if (code.Length > 0)
                {
                    float cw = DrawFit(r, font, code, x, Math.Min(w * 0.32f, x + w * 0.32f), h, h * 0.66f, false);
                    x += cw + h * 0.35f;
                }
                DrawFit(r, font, dest, x, w - pad, h, h * 0.52f, true);
            }
            r.LedDots(4, kBack, 0.35f);
            return r.ToTexture("Transport+ LED " + key);
        }

        /// <summary>Metni [x0, x1] aralığına sığdırarak dikeyde ortalı yazar; yazılan genişliği döndürür.</summary>
        private static float DrawFit(LedRasterizer r, FontAtlas font, string text, float x0, float x1, int h, float capPx, bool center)
        {
            if (string.IsNullOrEmpty(text) || x1 <= x0) return 0f;
            const float track = 0.04f;
            float scale = capPx / font.CapHeight;
            float tw = LedRasterizer.TextWidth(font, text, scale, track);
            if (tw > x1 - x0) { scale *= (x1 - x0) / tw; tw = x1 - x0; }
            float cap = font.CapHeight * scale;
            float baseline = h * 0.5f + cap * 0.5f;
            float x = center ? x0 + (x1 - x0 - tw) * 0.5f : x0;
            r.DrawText(font, text, x, baseline, scale, track, kLed);
            return tw;
        }

        private void Evict()
        {
            List<string> old = null;
            foreach (var kv in m_Textures)
                if (m_Clock - kv.Value.LastFrame > kEvictFrames) (old ?? (old = new List<string>())).Add(kv.Key);
            if (old == null) return;
            foreach (string k in old) { UnityEngine.Object.Destroy(m_Textures[k].Texture); m_Textures.Remove(k); }
        }

        /// <summary>
        /// Kendinden ışıklı görünsün diye önce HDRP/Unlit (gece de okunur), yoksa HDRP/Lit (Signs+'ta oyunda doğrulandı).
        /// </summary>
        private void EnsureMaterial()
        {
            if (m_Quad == null) m_Quad = BuildQuad();
            if (m_Material != null) return;
            Shader shader = null;
            foreach (string n in new[] { "HDRP/Unlit", "HDRP/Lit", "Unlit/Texture" })
            {
                shader = Shader.Find(n);
                if (shader != null) { Mod.log.Info($"LED sign shader: {n}"); break; }
            }
            if (shader == null) throw new InvalidOperationException("No usable shader for LED signs");
            m_Material = new Material(shader) { name = "Transport+ LED sign", enableInstancing = false };
            if (m_Material.HasProperty("_UnlitColor")) m_Material.SetColor("_UnlitColor", UnityEngine.Color.white);
            if (m_Material.HasProperty("_BaseColor")) m_Material.SetColor("_BaseColor", UnityEngine.Color.white);
            if (m_Material.HasProperty("_Smoothness")) m_Material.SetFloat("_Smoothness", 0.6f);
            if (m_Material.HasProperty("_Metallic")) m_Material.SetFloat("_Metallic", 0f);
        }

        /// <summary>1×1 m dörtgen, +Z'ye bakar; önden bakınca sağ taraf -X (Signs+ ile aynı, oyunda doğrulandı).</summary>
        private static Mesh BuildQuad()
        {
            var m = new Mesh { name = "Transport+ LED quad" };
            m.vertices = new[] { new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            m.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            m.tangents = new[] { new Vector4(-1f, 0f, 0f, 1f), new Vector4(-1f, 0f, 0f, 1f), new Vector4(-1f, 0f, 0f, 1f), new Vector4(-1f, 0f, 0f, 1f) };
            // İki sarım yönü de eklenir (hangi yönün ön sayıldığından bağımsız görünsün); arka yüz aracın gövdesiyle örtülür
            m.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            return m;
        }

        protected override void OnDestroy()
        {
            foreach (TexEntry t in m_Textures.Values) UnityEngine.Object.Destroy(t.Texture);
            m_Textures.Clear();
            if (m_Material != null) UnityEngine.Object.Destroy(m_Material);
            if (m_Quad != null) UnityEngine.Object.Destroy(m_Quad);
            base.OnDestroy();
        }
    }
}
