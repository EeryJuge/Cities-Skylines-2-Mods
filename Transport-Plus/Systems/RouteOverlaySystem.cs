using System;
using System.Collections.Generic;
using System.Reflection;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Net;
using Game.Pathfind;
using Game.Prefabs;
using Game.Rendering;
using Game.Routes;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace TransportPlus.Systems
{
    /// <summary>
    /// Rotaları şehrin kendisinde, oyunun trafik görünümü gibi gösterir.
    ///
    /// Beyaz dünya: mod yüklenirken oyuna kendi bilgi görünümümüz ("TransportPlusRoutes") eklenir. Varsayılan rengi açık
    /// olduğu için, görünüm açıkken oyun binaları, yolları ve diğer nesneleri bu renge boyar (ToolSystem.UpdateInfoviewColors
    /// → colossal_InfomodeColors). Bir bilgi görünümü en az bir infomode ister; bizimki otobüs duraklarını koyu renge boyar.
    ///
    /// Rotalar: her hattın gerçekten geçtiği şeritler (RouteSegment → PathElement) okunur ve yol kenarlarına (Edge) çevrilir.
    /// Her hat bir yolda tek bir şerit olarak, yolun orta çizgisine göre yan yana çizilir (iki yön aynı şeritte).
    /// Kavşaklarda iki yolun şerit uçları, yolların kendi teğetleriyle yumuşak bir eğriyle bağlanır; böylece çizgi kesintisiz olur.
    /// Önce bütün beyaz kenarlar, sonra renkler çizilir: birleşme yerlerinde dikiş kalmaz.
    /// </summary>
    public partial class RouteOverlaySystem : GameSystemBase
    {
        public const string kInfoviewName = "TransportPlusRoutes";
        public const string kInfomodeName = "TransportPlusRouteStops";

        private static InfoviewPrefab s_Infoview;

        private PrefabSystem m_PrefabSystem;
        private ToolSystem m_ToolSystem;
        private OverlayRenderSystem m_Overlay;
        private EntityQuery m_LineQuery;

        // Pencereden gelen seçim
        private string m_Type = "bus";
        private int m_Highlight = -1;          // seçili hattın Entity.Index'i, yoksa -1
        private bool m_Dirty = true;
        private ulong m_Signature;
        private int m_Frames;
        private bool m_WasActive;
        private bool m_LoggedOnce;

        // Önceden hesaplanmış çizim verisi (çizim işi bunları okur; yeniden kurmadan önce iş bitirilir)
        private NativeList<Stripe> m_Stripes;
        private NativeList<Joint> m_Joints;
        private NativeList<StopMark> m_Stops;
        private NativeList<LineRef> m_LineRefs;   // çizilen hatlar (taşıt işaretleri için)
        private bool m_ShowVehicles;
        private Entity m_FocusVehicle = Entity.Null;
        private float m_GroundY;
        private JobHandle m_LastJob;

        /// <summary>Çizilen bir hat (taşıt işaretleri için): renk ve kalınlık çarpanı.</summary>
        private struct LineRef { public Entity m_Line; public UnityEngine.Color m_Color; public float m_Scale; }

        /// <summary>Bir hattın bir yol üstündeki parçası: yol eğrisinin kesilmiş hali, yolun soluna doğru sıra.</summary>
        private struct Stripe
        {
            public Bezier4x3 m_Curve;
            public float m_Slot;
            public UnityEngine.Color m_Color;
            public float m_Scale;
        }

        /// <summary>Kavşakta iki yol parçasını birleştiren bağlantı. Teğetler yolculuk yönünde; sıralar yolculuğun soluna göre.</summary>
        private struct Joint
        {
            public float3 m_A, m_TanA, m_B, m_TanB;
            public float2 m_Slot;
            public UnityEngine.Color m_Color;
            public float m_Scale;
        }

        /// <summary>Durak: hattın şeridine oturtulmuş nokta.</summary>
        private struct StopMark
        {
            public Bezier4x3 m_Curve;
            public float m_T, m_Slot;
            public UnityEngine.Color m_Color;
            public float m_Scale;
        }

        /// <summary>Şehirdeki rota görünümü açık mı (UI bunu okur). Beyaz bilgi görünümünden bağımsız.</summary>
        public bool Active => m_On;
        private bool m_On, m_InfoviewSeen, m_InfoviewFailed;
        private int m_ActivateTries;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_Overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            m_LineQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<TransportLine>(),
                    ComponentType.ReadOnly<RouteWaypoint>(),
                    ComponentType.ReadOnly<RouteSegment>(),
                    ComponentType.ReadOnly<PrefabRef>()
                },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() }
            });
            m_Stripes = new NativeList<Stripe>(1024, Allocator.Persistent);
            m_Joints = new NativeList<Joint>(1024, Allocator.Persistent);
            m_Stops = new NativeList<StopMark>(256, Allocator.Persistent);
            m_LineRefs = new NativeList<LineRef>(64, Allocator.Persistent);
            // Bilgi görünümü prefabı artık mod yüklenirken değil, rotalar ilk kez şehirde gösterilirken eklenir:
            // çok modlu kurulumlarda açılışta oyunun bilgi görünümü listesine dokunmamak için (0.3 arayüz sorunu).
        }

        protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_On = false;   // yeni şehirde görünüm kapalı başlar; arayüz sekme açılınca yeniden açar
        }

        protected override void OnDestroy()
        {
            m_LastJob.Complete();
            if (m_Stripes.IsCreated) m_Stripes.Dispose();
            if (m_Joints.IsCreated) m_Joints.Dispose();
            if (m_Stops.IsCreated) m_Stops.Dispose();
            if (m_LineRefs.IsCreated) m_LineRefs.Dispose();
            base.OnDestroy();
        }

        /// <summary>
        /// Beyaz şehir için bilgi görünümü prefabı. İlk kullanımda eklenir; oyun prefabı birkaç kare içinde kurar,
        /// OnUpdate etkinleştirmeyi o sürede yeniden dener. Eklenemezse rotalar beyaz zemin olmadan çizilir.
        /// </summary>
        private void CreateInfoview()
        {
            if (s_Infoview != null || m_InfoviewFailed) return;
            m_InfoviewFailed = true;   // hata olursa bir daha denenmesin; başarıda aşağıda geri alınır
            try
            {
                var stops = ScriptableObject.CreateInstance<TransportStopInfomodePrefab>();
                stops.name = kInfomodeName;
                stops.m_Type = TransportType.Bus;
                stops.m_Color = new UnityEngine.Color(0.30f, 0.33f, 0.38f, 1f);
                stops.m_Priority = 10;

                var view = ScriptableObject.CreateInstance<InfoviewPrefab>();
                view.name = kInfoviewName;
                view.m_DefaultColor = new UnityEngine.Color(0.96f, 0.95f, 0.93f, 1f);
                view.m_SecondaryColor = new UnityEngine.Color(0.86f, 0.85f, 0.82f, 1f);
                view.m_IconPath = "coui://ui-mods/TransportPlus-icon.svg";
                view.m_Priority = 1000;
                view.m_Infomodes = new[] { new InfomodeInfo { m_Mode = stops, m_Priority = 1 } };

                m_PrefabSystem.AddPrefab(stops);
                m_PrefabSystem.AddPrefab(view);
                s_Infoview = view;
                m_InfoviewFailed = false;
                Mod.log.Info("Route infoview added");
            }
            catch (Exception e)
            {
                s_Infoview = null;
                Mod.log.Error(e, "Could not add the route infoview; routes are drawn without the white background");
            }
        }

        // ---------------------------------------------------------------- arayüzden

        /// <summary>Şehirdeki rota görünümünü açar ya da kapatır.</summary>
        public void SetActive(bool on)
        {
            if (m_ToolSystem == null) return;
            if (on)
            {
                if (m_On) return;
                m_On = true;
                m_Dirty = true;
                m_InfoviewSeen = false;
                m_ActivateTries = 0;
                if (WantWhite)
                {
                    try
                    {
                        CreateInfoview();
                        HideInfoviewPanel();
                        if (s_Infoview != null) m_ToolSystem.infoview = s_Infoview;
                    }
                    catch (Exception e) { Mod.log.Warn(e, "White city view could not be turned on"); }
                }
            }
            else
            {
                if (!m_On) return;
                m_On = false;
                try { if (s_Infoview != null && m_ToolSystem.infoview == s_Infoview) m_ToolSystem.infoview = null; }
                catch (Exception e) { Mod.log.Warn(e, "White city view could not be turned off"); }
            }
        }

        private static bool s_PanelHooked;

        /// <summary>
        /// Beyaz şehir oyunun bilgi görünümü olarak açıldığı için oyun kendi "etkin bilgi görünümü" panelini de
        /// gösterir ("TRANSPORT+ ROUTES"). Oyunun arayüzüne giden "infoviews.activeInfoview" değerini yazan fonksiyon
        /// sarılır: etkin görünüm bizimkiyse arayüze boş gider, panel açılmaz; diğer tüm görünümler olduğu gibi kalır.
        /// Yama değil, yalnızca o bağın yazıcısı değiştirilir; bulunamazsa panel eskisi gibi görünür.
        /// </summary>
        private void HideInfoviewPanel()
        {
            if (s_PanelHooked) return;
            s_PanelHooked = true;
            try
            {
                var ui = World.GetExistingSystemManaged<Game.UI.InGame.InfoviewsUISystem>();
                const BindingFlags kFlags = BindingFlags.Instance | BindingFlags.NonPublic;
                var bindingField = typeof(Game.UI.InGame.InfoviewsUISystem).GetField("m_ActiveView", kFlags);
                var writerField = typeof(Colossal.UI.Binding.RawValueBinding).GetField("m_WriterDelegate", kFlags);
                var binding = ui == null ? null : bindingField?.GetValue(ui) as Colossal.UI.Binding.RawValueBinding;
                var original = binding == null ? null : writerField?.GetValue(binding) as Action<Colossal.UI.Binding.IJsonWriter>;
                if (original == null)
                {
                    Mod.log.Warn("Infoview panel binding not found; the game's infoview panel stays visible with the routes");
                    return;
                }
                ToolSystem tool = m_ToolSystem;
                Action<Colossal.UI.Binding.IJsonWriter> writer = w =>
                {
                    if (s_Infoview != null && tool != null && tool.activeInfoview == s_Infoview) w.WriteNull();
                    else original(w);
                };
                writerField.SetValue(binding, writer);
                Mod.log.Info("Infoview panel hidden for the route view");
            }
            catch (Exception e) { Mod.log.Warn(e, "Could not hide the game's infoview panel for the route view"); }
        }

        private static bool WantWhite => Mod.Settings == null || Mod.Settings.WhiteCity;

        /// <summary>
        /// Beyaz görünümün durumunu izler: prefab yeni eklendiyse birkaç kare içinde etkinleştirmeyi yeniden dener;
        /// bir kez etkin olduktan sonra oyuncu oyunun menüsünden başka bir görünüm seçerse rota görünümünü de kapatır.
        /// </summary>
        private bool KeepWhite()
        {
            if (!WantWhite || s_Infoview == null) return true;
            if (m_ToolSystem.activeInfoview == s_Infoview) { m_InfoviewSeen = true; return true; }
            if (m_InfoviewSeen) { m_On = false; return false; }
            if (++m_ActivateTries <= 300 && m_ActivateTries % 10 == 0) m_ToolSystem.infoview = s_Infoview;
            if (m_ActivateTries == 300) Mod.log.Warn("Route infoview did not become active; drawing routes without the white background");
            return true;
        }

        /// <summary>Gösterilecek tür ("bus", "tram"...) ve öne çıkan hat ("L123" ya da boş).</summary>
        public void SetSelection(string type, string lineId)
        {
            int hl = -1;
            if (!string.IsNullOrEmpty(lineId) && lineId.Length > 1 && lineId[0] == 'L') int.TryParse(lineId.Substring(1), out hl);
            if (string.IsNullOrEmpty(type)) type = "bus";
            if (type == m_Type && hl == m_Highlight) return;
            m_Type = type;
            m_Highlight = hl;
            m_Dirty = true;
        }

        /// <summary>Taşıt işaretleri: açıkken çizilen hatların araçları konumlarında halka olarak gösterilir.</summary>
        public void SetShowVehicles(bool on) { m_ShowVehicles = on; }
        public bool ShowVehicles => m_ShowVehicles;
        /// <summary>Panelde seçili taşıt: işareti büyük ve yanıp söner.</summary>
        public void SetFocusVehicle(Entity vehicle) { m_FocusVehicle = vehicle; }

        /// <summary>Hat ayarları değişince (renk vb.) hemen yeniden kur.</summary>
        public void MarkDirty() => m_Dirty = true;

        // ---------------------------------------------------------------- her kare

        protected override void OnUpdate()
        {
            bool active = m_On;
            try { if (active) active = KeepWhite(); }
            catch (Exception e) { Mod.log.Warn(e, "Route infoview check failed"); }
            if (!active)
            {
                m_WasActive = false;
                return;
            }
            try
            {
                if (!m_WasActive) { m_WasActive = true; m_Dirty = true; }
                if (++m_Frames >= 60)
                {
                    m_Frames = 0;
                    ulong sig = Signature();
                    if (sig != m_Signature) { m_Signature = sig; m_Dirty = true; }
                }
                if (m_Dirty)
                {
                    m_Dirty = false;
                    m_LastJob.Complete();
                    Rebuild();
                }
                if (m_Stripes.Length == 0 && m_Joints.Length == 0) return;

                // Kalınlık: kamera yerden ne kadar uzaksa o kadar kalın, ekranda aşağı yukarı aynı görünsün
                float dist = 600f;
                Camera cam = Camera.main;
                if (cam != null) dist = math.max(20f, cam.transform.position.y - m_GroundY);
                float width = math.clamp(dist * 0.0055f, 2.2f, 70f);

                OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle deps);
                var job = new DrawJob
                {
                    m_Stripes = m_Stripes.AsArray(),
                    m_Joints = m_Joints.AsArray(),
                    m_Stops = m_Stops.AsArray(),
                    m_Lines = m_LineRefs.AsArray(),
                    m_ShowVehicles = m_ShowVehicles,
                    m_Focus = m_FocusVehicle,
                    m_Blink = (UnityEngine.Time.realtimeSinceStartup % 1f) < 0.6f,
                    m_RouteVehicles = GetBufferLookup<RouteVehicle>(true),
                    m_Layouts = GetBufferLookup<Game.Vehicles.LayoutElement>(true),
                    m_Interpolated = GetComponentLookup<InterpolatedTransform>(true),
                    m_Transforms = GetComponentLookup<Game.Objects.Transform>(true),
                    m_Width = width,
                    m_Spacing = width * 1.25f,
                    m_Buffer = buffer
                };
                m_LastJob = job.Schedule(JobHandle.CombineDependencies(Dependency, deps));
                m_Overlay.AddBufferWriter(m_LastJob);
                Dependency = m_LastJob;
            }
            catch (Exception e)
            {
                if (!m_LoggedOnce) { m_LoggedOnce = true; Mod.log.Error(e, "Route overlay failed"); }
            }
        }

        private struct DrawJob : IJob
        {
            [ReadOnly] public NativeArray<Stripe> m_Stripes;
            [ReadOnly] public NativeArray<Joint> m_Joints;
            [ReadOnly] public NativeArray<StopMark> m_Stops;
            [ReadOnly] public NativeArray<LineRef> m_Lines;
            [ReadOnly] public BufferLookup<RouteVehicle> m_RouteVehicles;
            [ReadOnly] public BufferLookup<Game.Vehicles.LayoutElement> m_Layouts;
            [ReadOnly] public ComponentLookup<InterpolatedTransform> m_Interpolated;
            [ReadOnly] public ComponentLookup<Game.Objects.Transform> m_Transforms;
            public bool m_ShowVehicles, m_Blink;
            public Entity m_Focus;
            public float m_Width, m_Spacing;
            public OverlayRenderSystem.Buffer m_Buffer;

            public void Execute()
            {
                var white = new UnityEngine.Color(1f, 1f, 1f, 1f);
                float edge = m_Width * 0.35f;
                // 1) beyaz kenarlar, 2) renkler: her renk kendi kenarının üstünde, birleşmelerde dikiş yok
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int i = 0; i < m_Stripes.Length; i++)
                    {
                        Stripe s = m_Stripes[i];
                        Bezier4x3 c = s.m_Slot == 0f ? s.m_Curve : NetUtils.OffsetCurveLeftSmooth(s.m_Curve, new float2(s.m_Slot * m_Spacing));
                        Draw(c, s.m_Color, s.m_Scale, pass, white, edge);
                    }
                    for (int i = 0; i < m_Joints.Length; i++)
                    {
                        Joint j = m_Joints[i];
                        float3 a = j.m_A + Left(j.m_TanA) * (j.m_Slot.x * m_Spacing);
                        float3 b = j.m_B + Left(j.m_TanB) * (j.m_Slot.y * m_Spacing);
                        if (math.distancesq(a.xz, b.xz) < 0.01f) continue;
                        Draw(NetUtils.FitCurve(a, j.m_TanA, j.m_TanB, b), j.m_Color, j.m_Scale, pass, white, edge);
                    }
                }
                float d = m_Width * 1.7f;
                for (int i = 0; i < m_Stops.Length; i++)
                {
                    StopMark s = m_Stops[i];
                    float3 p = MathUtils.Position(s.m_Curve, s.m_T);
                    float3 t = MathUtils.Tangent(s.m_Curve, s.m_T);
                    p += Left(t) * (s.m_Slot * m_Spacing);
                    float dd = d * s.m_Scale;
                    m_Buffer.DrawCircle(s.m_Color, white, dd * 0.25f, OverlayRenderSystem.StyleFlags.Projected, new float2(0f, 1f), p, dd);
                }
                if (m_ShowVehicles || m_Focus != Entity.Null) DrawVehicles(white);
            }

            /// <summary>
            /// Taşıtlar: hattın rengiyle dolu, beyaz kenarlı daire (vagonlu araçlarda her vagon). Seçili taşıt büyük ve yanıp söner.
            /// Konum çizim anındaki ara değerden (InterpolatedTransform) okunur, yoksa simülasyon konumundan.
            /// </summary>
            private void DrawVehicles(UnityEngine.Color white)
            {
                float d = m_Width * 2.3f;
                var dark = new UnityEngine.Color(0.1f, 0.12f, 0.15f, 1f);
                for (int i = 0; i < m_Lines.Length; i++)
                {
                    LineRef l = m_Lines[i];
                    if (!m_RouteVehicles.TryGetBuffer(l.m_Line, out DynamicBuffer<RouteVehicle> vs)) continue;
                    for (int v = 0; v < vs.Length; v++)
                    {
                        Entity veh = vs[v].m_Vehicle;
                        bool focus = veh == m_Focus;
                        if (!m_ShowVehicles && !focus) continue;
                        float size = focus ? d * 1.9f : d * l.m_Scale;
                        UnityEngine.Color fill = focus && m_Blink ? white : l.m_Color;
                        UnityEngine.Color rim = focus ? dark : white;
                        if (m_Layouts.TryGetBuffer(veh, out DynamicBuffer<Game.Vehicles.LayoutElement> layout) && layout.Length > 1)
                        {
                            for (int c = 0; c < layout.Length; c++)
                                if (TryPos(layout[c].m_Vehicle, out float3 cp))
                                    m_Buffer.DrawCircle(rim, fill, size * 0.22f, OverlayRenderSystem.StyleFlags.Projected, new float2(0f, 1f), cp, c == 0 ? size : size * 0.8f);
                        }
                        else if (TryPos(veh, out float3 p))
                            m_Buffer.DrawCircle(rim, fill, size * 0.22f, OverlayRenderSystem.StyleFlags.Projected, new float2(0f, 1f), p, size);
                    }
                }
            }

            private bool TryPos(Entity e, out float3 p)
            {
                if (m_Interpolated.TryGetComponent(e, out InterpolatedTransform it)) { p = it.m_Position; return true; }
                if (m_Transforms.TryGetComponent(e, out Game.Objects.Transform t)) { p = t.m_Position; return true; }
                p = default; return false;
            }

            private void Draw(Bezier4x3 c, UnityEngine.Color color, float scale, int pass, UnityEngine.Color white, float edge)
            {
                float w = m_Width * scale;
                if (pass == 0) { white.a = color.a; m_Buffer.DrawCurve(white, white, 0f, OverlayRenderSystem.StyleFlags.Projected, c, w + edge, new float2(1f, 1f)); }
                else m_Buffer.DrawCurve(color, color, 0f, OverlayRenderSystem.StyleFlags.Projected, c, w, new float2(1f, 1f));
            }

            /// <summary>Yatay düzlemde teğetin solu (NetUtils.OffsetCurveLeftSmooth ile aynı yön).</summary>
            private static float3 Left(float3 tangent)
            {
                float2 t = math.normalizesafe(tangent.xz);
                return new float3(-t.y, 0f, t.x);
            }
        }

        // ---------------------------------------------------------------- geometri

        /// <summary>Bir hattın yol üstündeki bir geçişi (yolculuk sırasıyla).</summary>
        private struct Pass { public Entity Edge; public bool Along; }

        private void Rebuild()
        {
            m_Stripes.Clear();
            m_Joints.Clear();
            m_Stops.Clear();
            m_LineRefs.Clear();
            var lines = new List<Entity>();
            NativeArray<Entity> all = m_LineQuery.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (Entity e in all)
                    if (TypeOf(e) == m_Type) lines.Add(e);
            }
            finally { all.Dispose(); }
            // Sıra sabit kalsın: hat numarasına göre (yan yana dizilişte her yolda aynı düzen)
            lines.Sort((a, b) => Number(a).CompareTo(Number(b)));

            var edgeLines = new Dictionary<Entity, List<int>>();        // yol → üstünden geçen hatlar (sırayla)
            var ranges = new Dictionary<long, float2>();               // (hat, yol) → kullanılan aralık [tmin, tmax]
            var passes = new List<List<Pass>>();                       // hat → yol geçişleri (yolculuk sırası)
            float ySum = 0f; int yCount = 0;

            for (int li = 0; li < lines.Count; li++)
            {
                var seq = new List<Pass>();
                passes.Add(seq);
                DynamicBuffer<RouteSegment> segs = EntityManager.GetBuffer<RouteSegment>(lines[li], true);
                for (int s = 0; s < segs.Length; s++)
                {
                    Entity seg = segs[s].m_Segment;
                    if (seg == Entity.Null || !EntityManager.HasBuffer<PathElement>(seg)) continue;
                    DynamicBuffer<PathElement> path = EntityManager.GetBuffer<PathElement>(seg, true);
                    for (int p = 0; p < path.Length; p++)
                    {
                        Entity lane = path[p].m_Target;
                        float2 t = path[p].m_TargetDelta;
                        if (lane == Entity.Null || !EntityManager.HasComponent<Curve>(lane) || !EntityManager.HasComponent<Owner>(lane)) continue;
                        Entity owner = EntityManager.GetComponentData<Owner>(lane).m_Owner;
                        if (owner == Entity.Null || !EntityManager.HasComponent<Game.Net.Edge>(owner) || !EntityManager.HasComponent<Curve>(owner)) continue;

                        Bezier4x3 laneCurve = EntityManager.GetComponentData<Curve>(lane).m_Bezier;
                        Bezier4x3 edgeCurve = EntityManager.GetComponentData<Curve>(owner).m_Bezier;
                        bool sameDir = math.dot((laneCurve.d - laneCurve.a).xz, (edgeCurve.d - edgeCurve.a).xz) >= 0f;
                        float2 et = sameDir ? t : 1f - t;                  // şerit parametresi → yol parametresi
                        if (math.abs(et.y - et.x) > 1e-4f)
                        {
                            long key = ((long)li << 32) | (uint)owner.Index;
                            float2 r = new float2(math.min(et.x, et.y), math.max(et.x, et.y));
                            if (ranges.TryGetValue(key, out float2 old)) r = new float2(math.min(old.x, r.x), math.max(old.y, r.y));
                            else
                            {
                                if (!edgeLines.TryGetValue(owner, out List<int> list)) edgeLines[owner] = list = new List<int>();
                                list.Add(li);
                            }
                            ranges[key] = r;
                        }
                        // Aynı yolda arka arkaya şerit değiştirmeler tek geçiştir
                        bool along = et.y >= et.x;
                        if (seq.Count == 0 || seq[seq.Count - 1].Edge != owner) seq.Add(new Pass { Edge = owner, Along = along });
                        ySum += edgeCurve.a.y; yCount++;
                    }
                }
            }

            // Yol başına sıra: hatlar yolun orta çizgisinin iki yanına simetrik dizilir
            var slotOf = new Dictionary<long, float>();
            foreach (KeyValuePair<Entity, List<int>> kv in edgeLines)
            {
                int n = kv.Value.Count;
                for (int k = 0; k < n; k++) slotOf[((long)kv.Value[k] << 32) | (uint)kv.Key.Index] = k - (n - 1) * 0.5f;
            }

            bool any = m_Highlight >= 0 && lines.Exists(l => l.Index == m_Highlight);
            var order = new List<int>();
            for (int li = 0; li < lines.Count; li++) if (!(any && lines[li].Index == m_Highlight)) order.Add(li);
            for (int li = 0; li < lines.Count; li++) if (any && lines[li].Index == m_Highlight) order.Add(li);   // seçili hat en üstte

            var edgeCache = new Dictionary<Entity, Bezier4x3>();
            Func<Entity, Bezier4x3> curveOf = e =>
            {
                if (!edgeCache.TryGetValue(e, out Bezier4x3 c)) edgeCache[e] = c = EntityManager.GetComponentData<Curve>(e).m_Bezier;
                return c;
            };

            foreach (int li in order)
            {
                bool hl = any && lines[li].Index == m_Highlight;
                UnityEngine.Color color = LineColor(lines[li]);
                if (any && !hl) color = UnityEngine.Color.Lerp(color, new UnityEngine.Color(0.86f, 0.86f, 0.86f, 1f), 0.6f);
                float scale = hl ? 1.3f : 1f;
                m_LineRefs.Add(new LineRef { m_Line = lines[li], m_Color = color, m_Scale = scale });

                // Yol parçaları
                foreach (KeyValuePair<Entity, List<int>> kv in edgeLines)
                {
                    long key = ((long)li << 32) | (uint)kv.Key.Index;
                    if (!ranges.TryGetValue(key, out float2 r)) continue;
                    m_Stripes.Add(new Stripe { m_Curve = MathUtils.Cut(curveOf(kv.Key), r), m_Slot = slotOf[key], m_Color = color, m_Scale = scale });
                }

                // Kavşak bağlantıları: art arda iki farklı yolun ortak düğümünde (hat döngü olduğu için sondan başa da)
                List<Pass> seq = passes[li];
                var done = new HashSet<long>();
                for (int i = 0; i < seq.Count; i++)
                {
                    Pass p = seq[i], q = seq[(i + 1) % seq.Count];
                    if (p.Edge == q.Edge) continue;
                    Game.Net.Edge ea = EntityManager.GetComponentData<Game.Net.Edge>(p.Edge);
                    Game.Net.Edge eb = EntityManager.GetComponentData<Game.Net.Edge>(q.Edge);
                    Entity node = ea.m_End == eb.m_Start || ea.m_End == eb.m_End ? ea.m_End
                                : ea.m_Start == eb.m_Start || ea.m_Start == eb.m_End ? ea.m_Start : Entity.Null;
                    if (node == Entity.Null) continue;
                    // Aynı bağlantı iki yönde de geçilir; tek kez çiz
                    long jk = ((long)math.min(p.Edge.Index, q.Edge.Index) << 32) | (uint)math.max(p.Edge.Index, q.Edge.Index);
                    if (!done.Add(jk)) continue;

                    Bezier4x3 ca = curveOf(p.Edge), cb = curveOf(q.Edge);
                    bool aEnd = ea.m_End == node, bStart = eb.m_Start == node;
                    // Yolculuk yönü: A'dan düğüme, düğümden B'ye
                    float3 pa = aEnd ? ca.d : ca.a;
                    float3 ta = aEnd ? MathUtils.EndTangent(ca) : -MathUtils.StartTangent(ca);
                    float3 pb = bStart ? cb.a : cb.d;
                    float3 tb = bStart ? MathUtils.StartTangent(cb) : -MathUtils.EndTangent(cb);
                    ta = math.normalizesafe(new float3(ta.x, 0f, ta.z));
                    tb = math.normalizesafe(new float3(tb.x, 0f, tb.z));
                    float sa = slotOf.TryGetValue(((long)li << 32) | (uint)p.Edge.Index, out float va) ? va : 0f;
                    float sb = slotOf.TryGetValue(((long)li << 32) | (uint)q.Edge.Index, out float vb) ? vb : 0f;
                    // Yolun soluna göre sıra → yolculuğun soluna göre: yolun yönünde gidiliyorsa aynı, tersine gidiliyorsa eksi
                    if (!aEnd) sa = -sa;
                    if (!bStart) sb = -sb;
                    m_Joints.Add(new Joint { m_A = pa, m_TanA = ta, m_B = pb, m_TanB = tb, m_Slot = new float2(sa, sb), m_Color = color, m_Scale = scale });
                }

                // Duraklar: hattın kendi şeridindeki en yakın noktaya
                DynamicBuffer<RouteWaypoint> wps = EntityManager.GetBuffer<RouteWaypoint>(lines[li], true);
                for (int i = 0; i < wps.Length; i++)
                {
                    Entity wp = wps[i].m_Waypoint;
                    if (wp == Entity.Null || !EntityManager.HasComponent<Connected>(wp) || !EntityManager.HasComponent<Position>(wp)) continue;
                    float3 pos = EntityManager.GetComponentData<Position>(wp).m_Position;
                    float best = float.MaxValue, bestT = 0f, bestSlot = 0f; Bezier4x3 bestCurve = default;
                    foreach (KeyValuePair<Entity, List<int>> kv in edgeLines)
                    {
                        long key = ((long)li << 32) | (uint)kv.Key.Index;
                        if (!ranges.ContainsKey(key)) continue;
                        Bezier4x3 c = curveOf(kv.Key);
                        float dist = MathUtils.Distance(c.xz, pos.xz, out float t);
                        if (dist < best) { best = dist; bestT = t; bestSlot = slotOf[key]; bestCurve = c; }
                    }
                    if (best > 60f) continue;
                    m_Stops.Add(new StopMark { m_Curve = bestCurve, m_T = bestT, m_Slot = bestSlot, m_Color = color, m_Scale = scale });
                }
            }

            m_GroundY = yCount > 0 ? ySum / yCount : 0f;
            Mod.log.Info($"Route overlay: {lines.Count} {m_Type} lines, {edgeLines.Count} roads, {m_Stripes.Length} stripes, {m_Joints.Length} joints, {m_Stops.Length} stops");
        }

        // ---------------------------------------------------------------- yardımcılar

        private string TypeOf(Entity line) => LineTypes.Of(EntityManager, line);

        private int Number(Entity line) =>
            EntityManager.HasComponent<RouteNumber>(line) ? EntityManager.GetComponentData<RouteNumber>(line).m_Number : line.Index;

        private UnityEngine.Color LineColor(Entity line)
        {
            if (!EntityManager.HasComponent<Game.Routes.Color>(line)) return new UnityEngine.Color(0.5f, 0.5f, 0.5f, 1f);
            Color32 c = EntityManager.GetComponentData<Game.Routes.Color>(line).m_Color;
            return new UnityEngine.Color(c.r / 255f, c.g / 255f, c.b / 255f, 1f);
        }

        /// <summary>Hat eklenince, silinince, durak ya da güzergâh değişince değişen ucuz özet.</summary>
        private ulong Signature()
        {
            ulong h = 1469598103934665603UL;
            NativeArray<Entity> lines = m_LineQuery.ToEntityArray(Allocator.Temp);
            try
            {
                h = (h ^ (ulong)lines.Length) * 1099511628211UL;
                foreach (Entity e in lines)
                {
                    h = (h ^ (ulong)e.Index) * 1099511628211UL;
                    h = (h ^ (ulong)e.Version) * 1099511628211UL;
                    DynamicBuffer<RouteSegment> segs = EntityManager.GetBuffer<RouteSegment>(e, true);
                    for (int i = 0; i < segs.Length; i++)
                    {
                        Entity seg = segs[i].m_Segment;
                        int n = seg != Entity.Null && EntityManager.HasBuffer<PathElement>(seg) ? EntityManager.GetBuffer<PathElement>(seg, true).Length : 0;
                        h = (h ^ (ulong)n) * 1099511628211UL;
                    }
                    if (EntityManager.HasComponent<Game.Routes.Color>(e))
                    {
                        Color32 c = EntityManager.GetComponentData<Game.Routes.Color>(e).m_Color;
                        h = (h ^ (ulong)(c.r | (c.g << 8) | (c.b << 16))) * 1099511628211UL;
                    }
                }
            }
            finally { lines.Dispose(); }
            return h;
        }
    }

    /// <summary>Hat türünü arayüzde kullanılan adla verir ("bus", "tram", ...). Yük hatları ve diğer türler null.</summary>
    internal static class LineTypes
    {
        public static string Of(EntityManager em, Entity line)
        {
            if (!em.HasComponent<PrefabRef>(line)) return null;
            Entity prefab = em.GetComponentData<PrefabRef>(line).m_Prefab;
            if (!em.HasComponent<TransportLineData>(prefab)) return null;
            TransportLineData data = em.GetComponentData<TransportLineData>(prefab);
            if (!data.m_PassengerTransport) return null;
            switch (data.m_TransportType.ToString())
            {
                case "Bus": return "bus";
                case "Tram": return "tram";
                case "Subway": return "subway";
                case "Train": return "train";
                case "Ship":
                case "Ferry": return "ship";
                default: return null;
            }
        }
    }
}
