using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Colossal.UI.Binding;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Pathfind;
using Game.Policies;
using Game.Prefabs;
using Game.Routes;
using Game.Simulation;
using Game.Tools;
using Game.UI;
using Game.UI.InGame;
using Game.Vehicles;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace TransportPlus.Systems
{
    /// <summary>
    /// Hat yönetimi ve istatistikler (Better Transit Overview tarzı).
    ///
    /// Ayarlar oyunun kendi hat panelinin kullandığı yollarla uygulanır, böylece oyunun paneli ve diğer modlar aynı veriyi görür:
    /// - bilet ücreti, araç sayısı, gündüz/gece hattı, servis dışı → PoliciesUISystem.SetPolicy (UITransportConfigurationPrefab'daki politikalar)
    /// - araç sayısı ↔ politika değeri dönüşümü oyunun VehicleCountSection hesabıyla birebir aynı (TransportLineSystem ve
    ///   RouteModifierRefreshData yardımcılarıyla)
    /// - araç modelleri → hattın VehicleModel listesi (oyunun SelectVehiclesSection'ı gibi, birden çok model seçilebilir). Liste prefablardan
    ///   kurulduğu için bölge paketlerinin ve Paradox Mods'tan gelen araç assetleri de seçilebilir.
    ///
    /// Durak istatistikleri: her hattın araçlarının yolcu listesi (Passenger) belirli aralıkla izlenir. İki örnek arasında listeye
    /// eklenen yolcu o durakta binmiş, çıkan yolcu inmiş sayılır. Sayımlar oturum boyunca birikir (kayda yazılmaz).
    /// </summary>
    public partial class LineManagerSystem : UISystemBase
    {
        private const string kGroup = "transportPlusLines";
        private const int kSampleFrames = 20;

        public override GameMode gameMode => GameMode.Game;

        private PrefabSystem m_PrefabSystem;
        private PoliciesUISystem m_Policies;
        private ImageSystem m_Images;
        private NameSystem m_Names;
        private RouteOverlaySystem m_Overlay;
        private EntityQuery m_ConfigQuery, m_LineQuery, m_VehiclePrefabQuery, m_DepotQuery;

        private ValueBinding<string> m_Detail;
        private Entity m_Selected = Entity.Null;
        private TransitMapUISystem m_Map;
        private Entity m_ModelListLine = Entity.Null;
        private EnergyTypes m_ModelListEnergy;
        private string m_ModelListJson = "";
        private readonly Queue<Action> m_Pending = new Queue<Action>();
        private int m_Frames, m_DetailFrames;

        private Entity m_TicketPolicy, m_VehiclePolicy, m_DayPolicy, m_NightPolicy, m_OutOfServicePolicy;
        private bool m_PoliciesFound;

        // Yolcu izleme
        private sealed class VehicleState { public HashSet<Entity> Pax = new HashSet<Entity>(); public Entity Target; }
        private readonly Dictionary<Entity, VehicleState> m_Vehicles = new Dictionary<Entity, VehicleState>();
        private readonly Dictionary<Entity, int2> m_StopFlow = new Dictionary<Entity, int2>();   // yol noktası → (binen, inen)
        private double m_SampleStart;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Policies = World.GetOrCreateSystemManaged<PoliciesUISystem>();
            m_Images = World.GetOrCreateSystemManaged<ImageSystem>();
            m_Names = World.GetOrCreateSystemManaged<NameSystem>();
            m_Overlay = World.GetOrCreateSystemManaged<RouteOverlaySystem>();
            m_Map = World.GetOrCreateSystemManaged<TransitMapUISystem>();
            m_ConfigQuery = GetEntityQuery(ComponentType.ReadOnly<UITransportConfigurationData>());
            m_LineQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<TransportLine>(), ComponentType.ReadOnly<RouteWaypoint>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() }
            });
            m_VehiclePrefabQuery = GetEntityQuery(ComponentType.ReadOnly<VehicleData>(), ComponentType.ReadOnly<PrefabData>());
            m_DepotQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Buildings.TransportDepot>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() }
            });

            AddBinding(m_Detail = new ValueBinding<string>(kGroup, "detail", "{}"));
            AddBinding(new TriggerBinding<string>(kGroup, "select", id => Queue(() => Select(id))));
            AddBinding(new TriggerBinding<int>(kGroup, "setTicket", v => Queue(() => SetTicket(v))));
            AddBinding(new TriggerBinding<int>(kGroup, "setVehicles", v => Queue(() => SetVehicles(v))));
            AddBinding(new TriggerBinding<int>(kGroup, "setSchedule", v => Queue(() => SetSchedule(v))));
            AddBinding(new TriggerBinding<bool>(kGroup, "setActive", v => Queue(() => SetActive(v))));
            AddBinding(new TriggerBinding<string, int>(kGroup, "setModel", (v, slot) => Queue(() => SetModel(v, slot))));
            AddBinding(new TriggerBinding<bool>(kGroup, "showVehicles", on => Queue(() => m_Overlay.SetShowVehicles(on))));
            AddBinding(new TriggerBinding<string>(kGroup, "focusVehicle", k => Queue(() => FocusVehicle(k))));
            AddBinding(new TriggerBinding<string>(kGroup, "followVehicle", k => Queue(() => FollowVehicle(k))));
            AddBinding(new TriggerBinding(kGroup, "resetStats", () => Queue(ResetStats)));
        }

        protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Selected = Entity.Null;
            m_PoliciesFound = false;
            ResetStats();
            m_Detail.Update("{}");
        }

        private void Queue(Action a) { lock (m_Pending) m_Pending.Enqueue(a); }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            try
            {
                while (true)
                {
                    Action a;
                    lock (m_Pending) { if (m_Pending.Count == 0) break; a = m_Pending.Dequeue(); }
                    a();
                    m_DetailFrames = 1000;   // ayardan hemen sonra ayrıntıyı tazele
                }
                // Yolcu sayımı yalnızca menü açıkken: kapalıyken oyunun işlerini beklemeye zorlamasın (büyük şehirlerde FPS)
                bool open = m_Map != null && m_Map.IsOpen;
                if (!open) { if (m_Vehicles.Count > 0) m_Vehicles.Clear(); }
                else if (++m_Frames >= kSampleFrames) { m_Frames = 0; Sample(); }
                if (m_Selected != Entity.Null && ++m_DetailFrames >= 30)
                {
                    m_DetailFrames = 0;
                    PushDetail();
                }
            }
            catch (Exception e)
            {
                Mod.log.Error(e, "Line manager update failed");
                m_Pending.Clear();
            }
        }

        // ---------------------------------------------------------------- seçim ve ayarlar

        private void Select(string id)
        {
            m_Selected = Find(id);
            m_ModelListLine = Entity.Null;
            m_Overlay.SetFocusVehicle(Entity.Null);
            m_DetailFrames = 1000;
            if (m_Selected == Entity.Null) m_Detail.Update("{}");
        }

        private Entity Find(string id)
        {
            if (string.IsNullOrEmpty(id) || id[0] != 'L' || !int.TryParse(id.Substring(1), out int index)) return Entity.Null;
            NativeArray<Entity> lines = m_LineQuery.ToEntityArray(Allocator.Temp);
            try { foreach (Entity e in lines) if (e.Index == index) return e; }
            finally { lines.Dispose(); }
            return Entity.Null;
        }

        private bool Valid => m_Selected != Entity.Null && EntityManager.Exists(m_Selected) && EntityManager.HasComponent<TransportLine>(m_Selected);

        private bool FindPolicies()
        {
            if (m_PoliciesFound) return true;
            if (m_ConfigQuery.IsEmptyIgnoreFilter) return false;
            var cfg = m_PrefabSystem.GetSingletonPrefab<UITransportConfigurationPrefab>(m_ConfigQuery);
            m_TicketPolicy = cfg.m_TicketPricePolicy != null ? m_PrefabSystem.GetEntity(cfg.m_TicketPricePolicy) : Entity.Null;
            m_VehiclePolicy = cfg.m_VehicleCountPolicy != null ? m_PrefabSystem.GetEntity(cfg.m_VehicleCountPolicy) : Entity.Null;
            m_DayPolicy = cfg.m_DayRoutePolicy != null ? m_PrefabSystem.GetEntity(cfg.m_DayRoutePolicy) : Entity.Null;
            m_NightPolicy = cfg.m_NightRoutePolicy != null ? m_PrefabSystem.GetEntity(cfg.m_NightRoutePolicy) : Entity.Null;
            m_OutOfServicePolicy = cfg.m_OutOfServicePolicy != null ? m_PrefabSystem.GetEntity(cfg.m_OutOfServicePolicy) : Entity.Null;
            m_PoliciesFound = true;
            return true;
        }

        private void SetTicket(int price)
        {
            if (!Valid || !FindPolicies() || m_TicketPolicy == Entity.Null) return;
            PolicySliderData slider = EntityManager.GetComponentData<PolicySliderData>(m_TicketPolicy);
            float v = math.clamp(price, slider.m_Range.min, slider.m_Range.max);
            m_Policies.SetPolicy(m_Selected, m_TicketPolicy, v > 0f, v);
            Mod.log.Info($"Line {m_Selected.Index}: ticket price {v}");
        }

        private void SetVehicles(int count)
        {
            if (!Valid || !FindPolicies() || m_VehiclePolicy == Entity.Null) return;
            TransportLineData data = LineData(m_Selected);
            float duration = StableDuration(m_Selected, data);
            float adjustment = AdjustmentFromCount(math.max(1, count), data.m_DefaultVehicleInterval, duration);
            if (adjustment < 0f) { Mod.log.Warn("Vehicle count policy has no interval modifier"); return; }
            m_Policies.SetPolicy(m_Selected, m_VehiclePolicy, true, adjustment);
            Mod.log.Info($"Line {m_Selected.Index}: vehicles {count} (adjustment {adjustment:0.###})");
        }

        /// <summary>0 = gündüz ve gece, 1 = yalnızca gündüz, 2 = yalnızca gece.</summary>
        private void SetSchedule(int schedule)
        {
            if (!Valid || !FindPolicies()) return;
            if (m_DayPolicy != Entity.Null) m_Policies.SetPolicy(m_Selected, m_DayPolicy, schedule == 1, 0f);
            if (m_NightPolicy != Entity.Null) m_Policies.SetPolicy(m_Selected, m_NightPolicy, schedule == 2, 0f);
        }

        private void SetActive(bool active)
        {
            if (!Valid || !FindPolicies() || m_OutOfServicePolicy == Entity.Null) return;
            m_Policies.SetPolicy(m_Selected, m_OutOfServicePolicy, !active, 0f);
        }

        /// <summary>
        /// Hattın araç modelleri oyunda bir listedir (VehicleModel tamponu: her öğede birincil ve ikincil prefab; boşsa oyun uygun
        /// araçlardan rastgele seçer). Oyunun kendi paneli (SelectVehiclesSection) de tamponu doğrudan değiştirir:
        /// yeni seçim boş kalan ilk yuvaya yazılır, yoksa yeni öğe eklenir. Burada da aynısı; seçili olan yeniden seçilirse çıkarılır.
        /// Boş anahtar listeyi temizler. slot: 0 = birincil (otobüs, tren seti, vagon, gemi), 1 = ikincil (lokomotif).
        /// </summary>
        private void SetModel(string key, int slot)
        {
            if (!Valid || !EntityManager.HasBuffer<VehicleModel>(m_Selected)) return;
            DynamicBuffer<VehicleModel> models = EntityManager.GetBuffer<VehicleModel>(m_Selected);
            Entity prefab = ParseEntity(key);
            if (prefab == Entity.Null)
            {
                if (slot < 0) models.Clear();
                else
                {
                    for (int i = models.Length - 1; i >= 0; i--)
                    {
                        VehicleModel m = models[i];
                        if (slot == 0) m.m_PrimaryPrefab = Entity.Null; else m.m_SecondaryPrefab = Entity.Null;
                        if (m.m_PrimaryPrefab == Entity.Null && m.m_SecondaryPrefab == Entity.Null) models.RemoveAt(i); else models[i] = m;
                    }
                }
                Mod.log.Info($"Line {m_Selected.Index}: vehicle models cleared (slot {slot})");
                return;
            }
            if (!EntityManager.Exists(prefab) || !EntityManager.HasComponent<VehicleData>(prefab)) return;
            string name = m_PrefabSystem.GetPrefabName(prefab);
            for (int i = 0; i < models.Length; i++)
            {
                VehicleModel m = models[i];
                if ((slot == 0 ? m.m_PrimaryPrefab : m.m_SecondaryPrefab) != prefab) continue;
                if (slot == 0) m.m_PrimaryPrefab = Entity.Null; else m.m_SecondaryPrefab = Entity.Null;
                if (m.m_PrimaryPrefab == Entity.Null && m.m_SecondaryPrefab == Entity.Null) models.RemoveAt(i); else models[i] = m;
                Mod.log.Info($"Line {m_Selected.Index}: vehicle model removed {name}");
                return;
            }
            for (int i = 0; i < models.Length; i++)
            {
                VehicleModel m = models[i];
                if ((slot == 0 ? m.m_PrimaryPrefab : m.m_SecondaryPrefab) != Entity.Null) continue;
                if (slot == 0) m.m_PrimaryPrefab = prefab; else m.m_SecondaryPrefab = prefab;
                models[i] = m;
                Mod.log.Info($"Line {m_Selected.Index}: vehicle model added {name}");
                return;
            }
            models.Add(slot == 0 ? new VehicleModel { m_PrimaryPrefab = prefab } : new VehicleModel { m_SecondaryPrefab = prefab });
            Mod.log.Info($"Line {m_Selected.Index}: vehicle model added {name}");
        }

        /// <summary>Panelde seçilen taşıt şehirde büyük ve yanıp sönen işaretle gösterilir; kamera üstüne gider.</summary>
        private void FocusVehicle(string key)
        {
            Entity v = ParseEntity(key);
            if (v != Entity.Null && !EntityManager.Exists(v)) v = Entity.Null;
            m_Overlay.SetFocusVehicle(v);
            if (v == Entity.Null) return;
            if (TryPosition(v, out float3 p)) m_Map.JumpToPosition(p);
        }

        /// <summary>Oyunun kendi araç panelini açar ve kamera aracı takip eder.</summary>
        private void FollowVehicle(string key)
        {
            Entity v = ParseEntity(key);
            if (v == Entity.Null || !EntityManager.Exists(v)) return;
            try
            {
                SelectedInfoUISystem info = World.GetOrCreateSystemManaged<SelectedInfoUISystem>();
                info.SetSelection(v);
                info.Focus(v);
            }
            catch (Exception e) { Mod.log.Warn(e, "Could not follow the vehicle"); }
        }

        private bool TryPosition(Entity v, out float3 p)
        {
            if (EntityManager.HasComponent<Game.Objects.Transform>(v)) { p = EntityManager.GetComponentData<Game.Objects.Transform>(v).m_Position; return true; }
            p = default; return false;
        }

        private static Entity ParseEntity(string key) => VehicleCatalog.Parse(key);

        // ---------------------------------------------------------------- araç sayısı (oyunun VehicleCountSection hesabı)

        private TransportLineData LineData(Entity line) =>
            EntityManager.GetComponentData<TransportLineData>(EntityManager.GetComponentData<PrefabRef>(line).m_Prefab);

        /// <summary>Bir tam turun süresi: parçaların yol süreleri + durak başına bekleme.</summary>
        private float StableDuration(Entity line, TransportLineData data)
        {
            float d = 0f;
            DynamicBuffer<RouteWaypoint> wps = EntityManager.GetBuffer<RouteWaypoint>(line, true);
            for (int i = 0; i < wps.Length; i++)
                if (EntityManager.HasComponent<VehicleTiming>(wps[i].m_Waypoint)) d += data.m_StopDuration;
            if (EntityManager.HasBuffer<RouteSegment>(line))
            {
                DynamicBuffer<RouteSegment> segs = EntityManager.GetBuffer<RouteSegment>(line, true);
                for (int i = 0; i < segs.Length; i++)
                    if (EntityManager.HasComponent<PathInformation>(segs[i].m_Segment))
                        d += EntityManager.GetComponentData<PathInformation>(segs[i].m_Segment).m_Duration;
            }
            return d;
        }

        private float CurrentInterval(Entity line, TransportLineData data)
        {
            float interval = data.m_DefaultVehicleInterval;
            if (EntityManager.HasBuffer<RouteModifier>(line))
                RouteUtils.ApplyModifier(ref interval, EntityManager.GetBuffer<RouteModifier>(line, true), RouteModifierType.VehicleInterval);
            return interval;
        }

        private int CountFromAdjustment(float adjustment, float interval, float duration)
        {
            var mod = default(RouteModifier);
            DynamicBuffer<RouteModifierData> datas = EntityManager.GetBuffer<RouteModifierData>(m_VehiclePolicy, true);
            ComponentLookup<PolicySliderData> sliders = GetComponentLookup<PolicySliderData>(true);
            for (int i = 0; i < datas.Length; i++)
            {
                if (datas[i].m_Type != RouteModifierType.VehicleInterval) continue;
                float delta = RouteModifierInitializeSystem.RouteModifierRefreshData.GetModifierDelta(datas[i], adjustment, m_VehiclePolicy, sliders);
                RouteModifierInitializeSystem.RouteModifierRefreshData.AddModifierData(ref mod, datas[i], delta);
                break;
            }
            interval += mod.m_Delta.x;
            interval += interval * mod.m_Delta.y;
            return TransportLineSystem.CalculateVehicleCount(interval, duration);
        }

        private float AdjustmentFromCount(int count, float originalInterval, float duration)
        {
            float interval = TransportLineSystem.CalculateVehicleInterval(duration, count);
            DynamicBuffer<RouteModifierData> datas = EntityManager.GetBuffer<RouteModifierData>(m_VehiclePolicy, true);
            PolicySliderData slider = EntityManager.GetComponentData<PolicySliderData>(m_VehiclePolicy);
            for (int i = 0; i < datas.Length; i++)
            {
                if (datas[i].m_Type != RouteModifierType.VehicleInterval) continue;
                var mod = default(RouteModifier);
                if ((int)datas[i].m_Mode == 1) mod.m_Delta.x = interval - originalInterval;
                else mod.m_Delta.y = (interval - originalInterval) / originalInterval;
                float delta = RouteModifierInitializeSystem.RouteModifierRefreshData.GetDeltaFromModifier(mod, datas[i]);
                return RouteModifierInitializeSystem.RouteModifierRefreshData.GetPolicyAdjustmentFromModifierDelta(datas[i], delta, slider);
            }
            return -1f;
        }

        // ---------------------------------------------------------------- yolcu izleme

        private void ResetStats()
        {
            m_Vehicles.Clear();
            m_StopFlow.Clear();
            m_SampleStart = UnityEngine.Time.realtimeSinceStartupAsDouble;
        }

        /// <summary>
        /// Her araç için yolcu listesini öncekiyle karşılaştırır. Değişim o anki hedef durağa (araç durakta yolcu alıyorsa)
        /// ya da bir önceki hedefe (araç duraktan yeni ayrıldıysa) yazılır.
        /// </summary>
        private void Sample()
        {
            var seen = new HashSet<Entity>();
            var pax = new HashSet<Entity>();
            NativeArray<Entity> lines = m_LineQuery.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (Entity line in lines)
                {
                    if (!EntityManager.HasBuffer<RouteVehicle>(line)) continue;
                    DynamicBuffer<RouteVehicle> vehicles = EntityManager.GetBuffer<RouteVehicle>(line, true);
                    for (int i = 0; i < vehicles.Length; i++)
                    {
                        Entity v = vehicles[i].m_Vehicle;
                        if (v == Entity.Null || !EntityManager.Exists(v)) continue;
                        seen.Add(v);
                        pax.Clear();
                        CollectPassengers(v, pax);
                        Entity target = EntityManager.HasComponent<Target>(v) ? EntityManager.GetComponentData<Target>(v).m_Target : Entity.Null;
                        bool boarding = EntityManager.HasComponent<Game.Vehicles.PublicTransport>(v)
                            && (EntityManager.GetComponentData<Game.Vehicles.PublicTransport>(v).m_State & PublicTransportFlags.Boarding) != 0;

                        if (!m_Vehicles.TryGetValue(v, out VehicleState st))
                        {
                            st = new VehicleState { Target = target };
                            st.Pax.UnionWith(pax);
                            m_Vehicles[v] = st;
                            continue;
                        }
                        Entity stop = boarding ? target : st.Target;
                        if (stop != Entity.Null)
                        {
                            int on = 0, off = 0;
                            foreach (Entity p in pax) if (!st.Pax.Contains(p)) on++;
                            foreach (Entity p in st.Pax) if (!pax.Contains(p)) off++;
                            if (on > 0 || off > 0)
                            {
                                m_StopFlow.TryGetValue(stop, out int2 f);
                                m_StopFlow[stop] = f + new int2(on, off);
                            }
                        }
                        st.Pax.Clear();
                        st.Pax.UnionWith(pax);
                        st.Target = target;
                    }
                }
            }
            finally { lines.Dispose(); }
            if (seen.Count != m_Vehicles.Count)
            {
                var gone = new List<Entity>();
                foreach (Entity v in m_Vehicles.Keys) if (!seen.Contains(v)) gone.Add(v);
                foreach (Entity v in gone) m_Vehicles.Remove(v);
            }
        }

        private void CollectPassengers(Entity vehicle, HashSet<Entity> into)
        {
            if (EntityManager.HasBuffer<LayoutElement>(vehicle))
            {
                DynamicBuffer<LayoutElement> layout = EntityManager.GetBuffer<LayoutElement>(vehicle, true);
                if (layout.Length > 0)
                {
                    for (int i = 0; i < layout.Length; i++) AddPassengers(layout[i].m_Vehicle, into);
                    return;
                }
            }
            AddPassengers(vehicle, into);
        }

        private void AddPassengers(Entity car, HashSet<Entity> into)
        {
            if (car == Entity.Null || !EntityManager.HasBuffer<Passenger>(car)) return;
            DynamicBuffer<Passenger> p = EntityManager.GetBuffer<Passenger>(car, true);
            for (int i = 0; i < p.Length; i++) into.Add(p[i].m_Passenger);
        }

        private int Capacity(Entity vehicle)
        {
            int cap = 0;
            if (EntityManager.HasBuffer<LayoutElement>(vehicle))
            {
                DynamicBuffer<LayoutElement> layout = EntityManager.GetBuffer<LayoutElement>(vehicle, true);
                for (int i = 0; i < layout.Length; i++) cap += CarCapacity(layout[i].m_Vehicle);
                if (layout.Length > 0) return cap;
            }
            return CarCapacity(vehicle);
        }

        private int CarCapacity(Entity car)
        {
            if (car == Entity.Null || !EntityManager.HasComponent<PrefabRef>(car)) return 0;
            Entity prefab = EntityManager.GetComponentData<PrefabRef>(car).m_Prefab;
            return EntityManager.HasComponent<PublicTransportVehicleData>(prefab) ? EntityManager.GetComponentData<PublicTransportVehicleData>(prefab).m_PassengerCapacity : 0;
        }

        // ---------------------------------------------------------------- ayrıntı JSON

        private void PushDetail()
        {
            if (!Valid) { m_Selected = Entity.Null; m_Detail.Update("{}"); return; }
            FindPolicies();
            Entity line = m_Selected;
            TransportLineData data = LineData(line);
            var ex = new TransitExporter(EntityManager, m_Names);
            string type = LineTypes.Of(EntityManager, line) ?? "bus";
            var sb = new StringBuilder(4096);
            sb.Append("{\"id\":\"L").Append(line.Index).Append('"')
              .Append(",\"type\":").Append(TransitExporter.Str(type))
              .Append(",\"code\":").Append(TransitExporter.Str(ex.LineCode(line, type)))
              .Append(",\"name\":").Append(TransitExporter.Str(ex.CustomName(line)));

            // Durum ve tarife
            Route route = EntityManager.GetComponentData<Route>(line);
            bool active = !RouteUtils.CheckOption(route, RouteOption.Inactive);
            int sched = RouteUtils.CheckOption(route, RouteOption.Day) ? 1 : RouteUtils.CheckOption(route, RouteOption.Night) ? 2 : 0;
            sb.Append(",\"active\":").Append(active ? "true" : "false").Append(",\"sched\":").Append(sched)
              .Append(",\"hasActive\":").Append(m_OutOfServicePolicy != Entity.Null ? "true" : "false")
              .Append(",\"hasSched\":").Append(m_DayPolicy != Entity.Null && m_NightPolicy != Entity.Null ? "true" : "false");

            // Bilet ücreti
            if (m_TicketPolicy != Entity.Null && EntityManager.HasComponent<PolicySliderData>(m_TicketPolicy))
            {
                PolicySliderData s = EntityManager.GetComponentData<PolicySliderData>(m_TicketPolicy);
                float v = PolicyValue(line, m_TicketPolicy, 0f);
                sb.Append(",\"ticket\":{\"v\":").Append(Num(v)).Append(",\"min\":").Append(Num(s.m_Range.min)).Append(",\"max\":").Append(Num(s.m_Range.max))
                  .Append(",\"step\":").Append(Num(s.m_Step <= 0f ? 1f : s.m_Step)).Append(",\"unit\":").Append(s.m_Unit).Append('}');
            }

            // Araçlar
            float duration = StableDuration(line, data);
            float interval = CurrentInterval(line, data);
            int count = TransportLineSystem.CalculateVehicleCount(interval, duration);
            DynamicBuffer<RouteVehicle> rv = EntityManager.GetBuffer<RouteVehicle>(line, true);
            int onBoard = 0, capacity = 0;
            var tmp = new HashSet<Entity>();
            for (int i = 0; i < rv.Length; i++)
            {
                Entity v = rv[i].m_Vehicle;
                if (v == Entity.Null || !EntityManager.Exists(v)) continue;
                tmp.Clear(); CollectPassengers(v, tmp); onBoard += tmp.Count;
                capacity += Capacity(v);
            }
            sb.Append(",\"veh\":{\"count\":").Append(count).Append(",\"running\":").Append(rv.Length)
              .Append(",\"duration\":").Append(Num(duration)).Append(",\"interval\":").Append(Num(count > 0 ? duration / count : interval));
            if (m_VehiclePolicy != Entity.Null && EntityManager.HasComponent<PolicySliderData>(m_VehiclePolicy))
            {
                PolicySliderData s = EntityManager.GetComponentData<PolicySliderData>(m_VehiclePolicy);
                int a = CountFromAdjustment(s.m_Range.min, data.m_DefaultVehicleInterval, duration);
                int b = CountFromAdjustment(s.m_Range.max, data.m_DefaultVehicleInterval, duration);
                sb.Append(",\"min\":").Append(math.max(1, math.min(a, b))).Append(",\"max\":").Append(math.max(a, b));
            }
            sb.Append(",\"onBoard\":").Append(onBoard).Append(",\"capacity\":").Append(capacity).Append('}');

            // Araç modelleri (bütün türler) ve hattaki taşıtlar
            if (EntityManager.HasBuffer<VehicleModel>(line)) AppendModels(sb, line, data);
            AppendVehicles(sb, line, ex);

            // Duraklar
            AppendStops(sb, line, ex);
            sb.Append(",\"since\":").Append(Num((float)((UnityEngine.Time.realtimeSinceStartupAsDouble - m_SampleStart) / 60.0)));
            sb.Append('}');
            m_Detail.Update(sb.ToString());
        }

        private float PolicyValue(Entity line, Entity policy, float fallback)
        {
            if (!EntityManager.HasBuffer<Policy>(line)) return fallback;
            DynamicBuffer<Policy> ps = EntityManager.GetBuffer<Policy>(line, true);
            for (int i = 0; i < ps.Length; i++)
                if (ps[i].m_Policy == policy) return (ps[i].m_Flags & PolicyFlags.Active) != 0 ? ps[i].m_Adjustment : fallback;
            return fallback;
        }

        private void AppendModels(StringBuilder sb, Entity line, TransportLineData data)
        {
            // Hangi enerji türleri kullanılabilir: şehirdeki bu türden depoların (ve yükseltmelerinin) enerji türleri
            EnergyTypes energy = EnergyTypes.None;
            NativeArray<Entity> depots = m_DepotQuery.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (Entity d in depots)
                {
                    Entity pf = EntityManager.GetComponentData<PrefabRef>(d).m_Prefab;
                    if (EntityManager.HasComponent<TransportDepotData>(pf))
                    {
                        TransportDepotData td = EntityManager.GetComponentData<TransportDepotData>(pf);
                        if (td.m_TransportType == data.m_TransportType) energy |= td.m_EnergyTypes;
                    }
                    if (!EntityManager.HasBuffer<InstalledUpgrade>(d)) continue;
                    DynamicBuffer<InstalledUpgrade> ups = EntityManager.GetBuffer<InstalledUpgrade>(d, true);
                    for (int i = 0; i < ups.Length; i++)
                    {
                        Entity up = ups[i].m_Upgrade;
                        if (up == Entity.Null || !EntityManager.HasComponent<PrefabRef>(up)) continue;
                        Entity upf = EntityManager.GetComponentData<PrefabRef>(up).m_Prefab;
                        if (EntityManager.HasComponent<TransportDepotData>(upf))
                        {
                            TransportDepotData td = EntityManager.GetComponentData<TransportDepotData>(upf);
                            if (td.m_TransportType == data.m_TransportType || td.m_TransportType == TransportType.None) energy |= td.m_EnergyTypes;
                        }
                    }
                }
            }
            finally { depots.Dispose(); }

            DynamicBuffer<VehicleModel> chosen = EntityManager.GetBuffer<VehicleModel>(line, true);
            bool loco = data.m_TransportType == TransportType.Train;
            sb.Append(",\"model\":{\"sel\":[");
            AppendChosen(sb, chosen, 0);
            sb.Append("],\"sel2\":[");
            AppendChosen(sb, chosen, 1);
            sb.Append("],\"energy\":").Append((int)energy).Append(",\"loco\":").Append(loco ? "true" : "false");
            // Liste pahalı (bütün prefablar + küçük resimler): hat seçilince bir kez kurulur
            if (m_ModelListLine != line || m_ModelListEnergy != energy)
            {
                var lb = new StringBuilder(8192);
                lb.Append(",\"list\":");
                VehicleCatalog.Write(lb, EntityManager, m_PrefabSystem, m_Images, VehicleCatalog.ForLine(EntityManager, m_VehiclePrefabQuery, data.m_TransportType, data.m_SizeClass, 0), energy);
                if (loco)
                {
                    lb.Append(",\"list2\":");
                    VehicleCatalog.Write(lb, EntityManager, m_PrefabSystem, m_Images, VehicleCatalog.ForLine(EntityManager, m_VehiclePrefabQuery, data.m_TransportType, data.m_SizeClass, 1), energy);
                }
                m_ModelListJson = lb.ToString();
                m_ModelListLine = line;
                m_ModelListEnergy = energy;
            }
            sb.Append(m_ModelListJson).Append('}');
        }

        private static void AppendChosen(StringBuilder sb, DynamicBuffer<VehicleModel> chosen, int slot)
        {
            for (int i = 0, k = 0; i < chosen.Length; i++)
            {
                Entity e = slot == 0 ? chosen[i].m_PrimaryPrefab : chosen[i].m_SecondaryPrefab;
                if (e == Entity.Null) continue;
                if (k++ > 0) sb.Append(',');
                sb.Append(TransitExporter.Str(Key(e)));
            }
        }

        /// <summary>
        /// Hattaki taşıtlar: model, yolcu / kapasite, durum, sıradaki durak, hız, kilometre ve konum.
        /// </summary>
        private void AppendVehicles(StringBuilder sb, Entity line, TransitExporter ex)
        {
            sb.Append(",\"vehicles\":[");
            if (EntityManager.HasBuffer<RouteVehicle>(line))
            {
                DynamicBuffer<RouteVehicle> vs = EntityManager.GetBuffer<RouteVehicle>(line, true);
                var tmp = new HashSet<Entity>();
                int n = 0;
                for (int i = 0; i < vs.Length; i++)
                {
                    Entity v = vs[i].m_Vehicle;
                    if (v == Entity.Null || !EntityManager.Exists(v) || !EntityManager.HasComponent<PrefabRef>(v)) continue;
                    tmp.Clear(); CollectPassengers(v, tmp);
                    int cars = EntityManager.HasBuffer<LayoutElement>(v) ? math.max(1, EntityManager.GetBuffer<LayoutElement>(v, true).Length) : 1;
                    string state = "road";
                    if (EntityManager.HasComponent<Game.Vehicles.PublicTransport>(v))
                    {
                        PublicTransportFlags f = EntityManager.GetComponentData<Game.Vehicles.PublicTransport>(v).m_State;
                        if ((f & PublicTransportFlags.Boarding) != 0) state = "boarding";
                        else if ((f & PublicTransportFlags.Returning) != 0) state = "depot";
                        else if ((f & PublicTransportFlags.RequiresMaintenance) != 0) state = "maint";
                        else if ((f & PublicTransportFlags.Refueling) != 0) state = "fuel";
                        else if ((f & PublicTransportFlags.EnRoute) == 0) state = "toLine";
                        if ((f & PublicTransportFlags.Full) != 0) state += "+full";
                    }
                    string next = "";
                    if (EntityManager.HasComponent<Target>(v))
                    {
                        Entity wp = EntityManager.GetComponentData<Target>(v).m_Target;
                        if (wp != Entity.Null && EntityManager.HasComponent<Connected>(wp))
                        {
                            Entity stop = EntityManager.GetComponentData<Connected>(wp).m_Connected;
                            if (stop != Entity.Null && EntityManager.Exists(stop)) next = ex.StationName(ex.TopBuilding(stop), stop);
                        }
                    }
                    float speed = EntityManager.HasComponent<Game.Objects.Moving>(v) ? math.length(EntityManager.GetComponentData<Game.Objects.Moving>(v).m_Velocity) : 0f;
                    float odo = EntityManager.HasComponent<Odometer>(v) ? EntityManager.GetComponentData<Odometer>(v).m_Distance : 0f;
                    TryPosition(v, out float3 pos);
                    Entity prefab = EntityManager.GetComponentData<PrefabRef>(v).m_Prefab;
                    if (n++ > 0) sb.Append(',');
                    sb.Append("{\"e\":").Append(TransitExporter.Str(Key(v)))
                      .Append(",\"id\":").Append(TransitExporter.Str(m_PrefabSystem.GetPrefabName(prefab)))
                      .Append(",\"pax\":").Append(tmp.Count).Append(",\"cap\":").Append(Capacity(v)).Append(",\"cars\":").Append(cars)
                      .Append(",\"st\":").Append(TransitExporter.Str(state)).Append(",\"next\":").Append(TransitExporter.Str(next ?? ""))
                      .Append(",\"kmh\":").Append(Num(speed * 3.6f)).Append(",\"km\":").Append(Num(odo / 1000f))
                      .Append(",\"x\":").Append(Num(pos.x)).Append(",\"y\":").Append(Num(pos.y)).Append(",\"z\":").Append(Num(pos.z)).Append('}');
                }
            }
            sb.Append(']');
            sb.Append(",\"showVeh\":").Append(m_Overlay.ShowVehicles ? "true" : "false");
        }

        private void AppendStops(StringBuilder sb, Entity line, TransitExporter ex)
        {
            sb.Append(",\"stops\":[");
            DynamicBuffer<RouteWaypoint> wps = EntityManager.GetBuffer<RouteWaypoint>(line, true);
            int n = 0;
            for (int i = 0; i < wps.Length; i++)
            {
                Entity wp = wps[i].m_Waypoint;
                if (wp == Entity.Null || !EntityManager.HasComponent<Connected>(wp)) continue;
                Entity stop = EntityManager.GetComponentData<Connected>(wp).m_Connected;
                if (stop == Entity.Null || !EntityManager.Exists(stop)) continue;
                string name = ex.StationName(ex.TopBuilding(stop), stop);
                int waiting = 0, avgWait = 0;
                if (EntityManager.HasComponent<WaitingPassengers>(wp))
                {
                    WaitingPassengers w = EntityManager.GetComponentData<WaitingPassengers>(wp);
                    waiting = w.m_Count; avgWait = w.m_AverageWaitingTime;
                }
                m_StopFlow.TryGetValue(wp, out int2 flow);
                if (n++ > 0) sb.Append(',');
                sb.Append("{\"n\":").Append(TransitExporter.Str(string.IsNullOrEmpty(name) ? "Stop " + n : name))
                  .Append(",\"w\":").Append(waiting).Append(",\"wt\":").Append(avgWait)
                  .Append(",\"on\":").Append(flow.x).Append(",\"off\":").Append(flow.y).Append('}');
            }
            sb.Append(']');
        }

        private static string Key(Entity e) => VehicleCatalog.Key(e);
        private static string Num(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "0" : v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
