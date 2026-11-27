using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Colossal.UI.Binding;
using Game;
using Game.City;
using Game.Common;
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
    /// Taksi ve şehirlerarası otobüs ayarları ("Taksi" sekmesi). Hepsi oyunun kodunu değiştirmeden, verisi üzerinden yapılır:
    ///
    /// - Şehir dışı taksiler: dış bağlantı prefablarındaki taksi deposunun araç kapasitesi (TransportDepotData.m_VehicleCapacity)
    ///   0 yapılır; oyunun TransportDepotAISystem'i kapasiteyi her turda prefabtan okuduğu için yeni taksi çıkmaz. Açınca eski değer geri yazılır.
    /// - Açılış ücreti: oyunun şehir politikası (TaxiStartingFee). Yolcuların taksi seçerken baktığı ücret de bu.
    /// - Km başı ücret ve gece tarifesi: TaxiFareSystem (yolculuk bitince ücreti yeniden hesaplar).
    /// - Taksi modelleri ve şehirlerarası otobüs modelleri: oyunun araç seçimi (VehicleSelectRequirementData) bir prefabı, şehrin
    ///   temasında olmayan bir tema şartı taşıyorsa rastgele seçimde atlar, ama bir hatta açıkça seçilmişse temayı yok sayar. Kapatılan
    ///   araçlara böyle bir şart (kendi grubumuzda) eklenir; açınca kaldırılır. Prefablar kayda yazılmadığı için kayıt dosyası değişmez.
    /// - Şehir/şehirlerarası ayrımı: şehir dışından geçen otobüsler (PublicTransportFlags.DummyTraffic) ve modeli seçilmemiş hatlar aynı
    ///   havuzdan seçer. Ayrım açıkken havuzda yalnızca şehirlerarası modeller kalır; modeli seçilmemiş şehir hatlarına şehir modelleri yazılır.
    /// </summary>
    public partial class TaxiCoachSystem : UISystemBase
    {
        private const string kGroup = "transportPlusTaxi";
        private const ushort kOurGroup = 64999;   // eklediğimiz şart grubunun numarası (kaldırırken tanımak için)

        public override GameMode gameMode => GameMode.Game;

        private PrefabSystem m_PrefabSystem;
        private PoliciesUISystem m_Policies;
        private ImageSystem m_Images;
        private CitySystem m_CitySystem;
        private CityConfigurationSystem m_CityConfig;
        private TimeSystem m_Time;
        private NameSystem m_Names;
        private TransitMapUISystem m_Map;
        private EntityQuery m_VehiclePrefabQuery, m_OutsideDepotPrefabQuery, m_ThemeQuery, m_PolicyQuery, m_TaxiQuery, m_StandQuery, m_LineQuery, m_PtQuery;

        private ValueBinding<string> m_State;
        private readonly Queue<Action> m_Pending = new Queue<Action>();
        private bool m_Open;
        private int m_Frames, m_LineFrames;
        private string m_ListJson = "";

        private readonly Dictionary<Entity, int> m_OriginalCapacity = new Dictionary<Entity, int>();
        private readonly HashSet<Entity> m_Excluded = new HashSet<Entity>();
        private Entity m_ExcludeTheme = Entity.Null;
        private Entity m_FeePolicy = Entity.Null;
        private bool m_FeePolicySearched;
        private bool m_Applied;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Policies = World.GetOrCreateSystemManaged<PoliciesUISystem>();
            m_Images = World.GetOrCreateSystemManaged<ImageSystem>();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_CityConfig = World.GetOrCreateSystemManaged<CityConfigurationSystem>();
            m_Time = World.GetOrCreateSystemManaged<TimeSystem>();
            m_Names = World.GetOrCreateSystemManaged<NameSystem>();
            m_Map = World.GetOrCreateSystemManaged<TransitMapUISystem>();
            m_VehiclePrefabQuery = GetEntityQuery(ComponentType.ReadOnly<VehicleData>(), ComponentType.ReadOnly<PrefabData>());
            m_OutsideDepotPrefabQuery = GetEntityQuery(ComponentType.ReadOnly<TransportDepotData>(), ComponentType.ReadOnly<OutsideConnectionData>(), ComponentType.ReadOnly<PrefabData>());
            m_ThemeQuery = GetEntityQuery(ComponentType.ReadOnly<ThemeData>(), ComponentType.ReadOnly<PrefabData>());
            m_PolicyQuery = GetEntityQuery(ComponentType.ReadOnly<PolicyData>(), ComponentType.ReadOnly<CityModifierData>());
            m_TaxiQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Vehicles.Taxi>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() }
            });
            m_StandQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<TaxiStand>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() }
            });
            m_LineQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<TransportLine>(), ComponentType.ReadOnly<VehicleModel>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() }
            });
            m_PtQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Vehicles.PublicTransport>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() }
            });

            AddBinding(m_State = new ValueBinding<string>(kGroup, "state", "{}"));
            AddBinding(new TriggerBinding<bool>(kGroup, "setOpen", on => Queue(() => { m_Open = on; m_Frames = 1000; if (on) m_ListJson = ""; })));
            AddBinding(new TriggerBinding<bool>(kGroup, "setOutside", on => Queue(() => { S.OutsideTaxis = on; Save(); ApplyOutsideTaxis(); })));
            AddBinding(new TriggerBinding<int>(kGroup, "setOpening", v => Queue(() => SetOpeningFee(v))));
            AddBinding(new TriggerBinding<bool>(kGroup, "setTariff", on => Queue(() => { S.TaxiTariff = on; Save(); })));
            AddBinding(new TriggerBinding<int>(kGroup, "setPerKm", v => Queue(() => { S.TaxiPerKm = math.clamp(v, 0, 500); Save(); })));
            AddBinding(new TriggerBinding<int>(kGroup, "setNight", v => Queue(() => { S.TaxiNightPercent = math.clamp(v, 50, 400); Save(); })));
            AddBinding(new TriggerBinding<string>(kGroup, "toggleTaxi", k => Queue(() => ToggleName(k, list => S.TaxiModelsOff = list, () => S.TaxiModelsOff, true))));
            AddBinding(new TriggerBinding<bool>(kGroup, "setCoachMode", on => Queue(() => { S.CoachMode = on; Save(); ApplyModels(); })));
            AddBinding(new TriggerBinding<string>(kGroup, "toggleCoach", k => Queue(() => ToggleName(k, list => S.CoachModels = list, () => S.CoachModels, false))));
            AddBinding(new TriggerBinding<string>(kGroup, "toggleCity", k => Queue(() => ToggleName(k, list => S.CityBusModels = list, () => S.CityBusModels, false))));
            AddBinding(new TriggerBinding<float, float, float>(kGroup, "jumpTo", (x, y, z) => Queue(() => m_Map.JumpToPosition(new float3(x, y, z)))));
        }

        private static Setting S => Mod.Settings;
        private static void Save() { try { Mod.Settings?.ApplyAndSave(); } catch (Exception e) { Mod.log.Warn(e, "Could not save settings"); } }
        private void Queue(Action a) { lock (m_Pending) m_Pending.Enqueue(a); }

        protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_FeePolicySearched = false;
            m_Applied = false;
            m_ListJson = "";
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            if (S == null) return;
            try
            {
                if (!m_Applied)
                {
                    m_Applied = true;
                    ApplyOutsideTaxis();
                    ApplyModels();
                }
                while (true)
                {
                    Action a;
                    lock (m_Pending) { if (m_Pending.Count == 0) break; a = m_Pending.Dequeue(); }
                    a();
                    m_Frames = 1000;
                }
                // Ayrım açıkken yeni kurulan otobüs hatlarına şehir modellerini yaz
                if (S.CoachMode && ++m_LineFrames >= 120) { m_LineFrames = 0; FillCityLines(); }
                if (m_Open && ++m_Frames >= 45) { m_Frames = 0; PushState(); }
            }
            catch (Exception e)
            {
                Mod.log.Error(e, "Taxi / coach update failed");
                lock (m_Pending) m_Pending.Clear();
            }
        }

        // ---------------------------------------------------------------- şehir dışı taksiler

        private void ApplyOutsideTaxis()
        {
            NativeArray<Entity> prefabs = m_OutsideDepotPrefabQuery.ToEntityArray(Allocator.Temp);
            int n = 0;
            try
            {
                foreach (Entity p in prefabs)
                {
                    TransportDepotData d = EntityManager.GetComponentData<TransportDepotData>(p);
                    if (d.m_TransportType != TransportType.Taxi) continue;
                    if (!m_OriginalCapacity.TryGetValue(p, out int original)) m_OriginalCapacity[p] = original = d.m_VehicleCapacity;
                    int want = S.OutsideTaxis ? original : 0;
                    if (d.m_VehicleCapacity != want) { d.m_VehicleCapacity = want; EntityManager.SetComponentData(p, d); }
                    n++;
                }
            }
            finally { prefabs.Dispose(); }
            Mod.log.Info($"Outside taxis {(S.OutsideTaxis ? "on" : "off")} ({n} outside connection taxi depots)");
        }

        // ---------------------------------------------------------------- açılış ücreti (şehir politikası)

        private Entity FeePolicy()
        {
            if (m_FeePolicySearched) return m_FeePolicy;
            m_FeePolicySearched = true;
            m_FeePolicy = Entity.Null;
            NativeArray<Entity> policies = m_PolicyQuery.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (Entity p in policies)
                {
                    DynamicBuffer<CityModifierData> mods = EntityManager.GetBuffer<CityModifierData>(p, true);
                    for (int i = 0; i < mods.Length; i++)
                        if (mods[i].m_Type == CityModifierType.TaxiStartingFee) { m_FeePolicy = p; break; }
                    if (m_FeePolicy != Entity.Null) break;
                }
            }
            finally { policies.Dispose(); }
            if (m_FeePolicy == Entity.Null) Mod.log.Warn("Taxi starting fee policy not found");
            return m_FeePolicy;
        }

        private void SetOpeningFee(int value)
        {
            Entity policy = FeePolicy();
            Entity city = m_CitySystem.City;
            if (policy == Entity.Null || city == Entity.Null) return;
            float v = value;
            if (EntityManager.HasComponent<PolicySliderData>(policy))
            {
                PolicySliderData s = EntityManager.GetComponentData<PolicySliderData>(policy);
                v = math.clamp(v, s.m_Range.min, s.m_Range.max);
            }
            m_Policies.SetPolicy(city, policy, v > 0f, v);
            Mod.log.Info($"Taxi starting fee {v}");
        }

        private float OpeningFee(out bool active)
        {
            active = false;
            Entity policy = FeePolicy(), city = m_CitySystem.City;
            if (policy == Entity.Null || city == Entity.Null || !EntityManager.HasBuffer<Policy>(city)) return 0f;
            DynamicBuffer<Policy> ps = EntityManager.GetBuffer<Policy>(city, true);
            for (int i = 0; i < ps.Length; i++)
                if (ps[i].m_Policy == policy) { active = (ps[i].m_Flags & PolicyFlags.Active) != 0; return active ? ps[i].m_Adjustment : 0f; }
            return 0f;
        }

        // ---------------------------------------------------------------- model seçimi (tema şartı)

        private static HashSet<string> Names(string s)
        {
            var set = new HashSet<string>();
            if (!string.IsNullOrEmpty(s)) foreach (string x in s.Split('|')) if (x.Length > 0) set.Add(x);
            return set;
        }

        private void ToggleName(string key, Action<string> set, Func<string> get, bool taxi)
        {
            Entity p = VehicleCatalog.Parse(key);
            if (p == Entity.Null || !EntityManager.Exists(p)) return;
            string name = m_PrefabSystem.GetPrefabName(p);
            HashSet<string> names = Names(get());
            if (!names.Remove(name)) names.Add(name);
            if (taxi)
            {
                // En az bir taksi modeli açık kalmalı
                int enabled = 0;
                foreach (VehicleCatalog.Item it in VehicleCatalog.Taxis(EntityManager, m_VehiclePrefabQuery))
                    if (!it.Locked && !names.Contains(m_PrefabSystem.GetPrefabName(it.Prefab))) enabled++;
                if (enabled == 0) { Mod.log.Info("At least one taxi model must stay enabled"); return; }
            }
            set(string.Join("|", names));
            Save();
            ApplyModels();
        }

        /// <summary>Rastgele seçimden çıkarılacak prefabları hesaplar ve tema şartını ekler/kaldırır.</summary>
        private void ApplyModels()
        {
            var want = new HashSet<Entity>();
            HashSet<string> taxiOff = Names(S.TaxiModelsOff);
            if (taxiOff.Count > 0)
                foreach (VehicleCatalog.Item it in VehicleCatalog.Taxis(EntityManager, m_VehiclePrefabQuery))
                    if (taxiOff.Contains(m_PrefabSystem.GetPrefabName(it.Prefab))) want.Add(it.Prefab);
            HashSet<string> coach = Names(S.CoachModels);
            if (S.CoachMode && coach.Count > 0)
            {
                bool anyCoach = false;
                List<VehicleCatalog.Item> buses = VehicleCatalog.Buses(EntityManager, m_VehiclePrefabQuery);
                foreach (VehicleCatalog.Item it in buses) if (coach.Contains(m_PrefabSystem.GetPrefabName(it.Prefab))) anyCoach = true;
                if (anyCoach)
                    foreach (VehicleCatalog.Item it in buses)
                        if (!coach.Contains(m_PrefabSystem.GetPrefabName(it.Prefab))) want.Add(it.Prefab);
            }

            Entity theme = ExcludeTheme();
            if (theme == Entity.Null && want.Count > 0) { Mod.log.Warn("No second theme found, vehicle model filters cannot be applied"); want.Clear(); }
            var drop = new List<Entity>();
            foreach (Entity p in m_Excluded) if (!want.Contains(p)) drop.Add(p);
            foreach (Entity p in drop) { RemoveRequirement(p); m_Excluded.Remove(p); }
            foreach (Entity p in want) if (!m_Excluded.Contains(p) && AddRequirement(p, theme)) m_Excluded.Add(p);
            Mod.log.Info($"Vehicle model filters: {m_Excluded.Count} models left out of random choice (coach mode {(S.CoachMode ? "on" : "off")})");
            if (S.CoachMode) FillCityLines();
        }

        /// <summary>Şehrin varsayılan teması dışındaki herhangi bir tema (bölge) prefabı.</summary>
        private Entity ExcludeTheme()
        {
            Entity def = m_CityConfig.defaultTheme;
            if (m_ExcludeTheme != Entity.Null && m_ExcludeTheme != def && EntityManager.Exists(m_ExcludeTheme)) return m_ExcludeTheme;
            m_ExcludeTheme = Entity.Null;
            NativeArray<Entity> themes = m_ThemeQuery.ToEntityArray(Allocator.Temp);
            try { foreach (Entity t in themes) if (t != def) { m_ExcludeTheme = t; break; } }
            finally { themes.Dispose(); }
            // Tema değiştiyse eklenmiş şartları yenile
            if (m_ExcludeTheme != Entity.Null)
                foreach (Entity p in m_Excluded) { RemoveRequirement(p); AddRequirement(p, m_ExcludeTheme); }
            return m_ExcludeTheme;
        }

        private bool AddRequirement(Entity prefab, Entity theme)
        {
            if (!EntityManager.Exists(prefab)) return false;
            DynamicBuffer<ObjectRequirementElement> reqs = EntityManager.HasBuffer<ObjectRequirementElement>(prefab)
                ? EntityManager.GetBuffer<ObjectRequirementElement>(prefab)
                : EntityManager.AddBuffer<ObjectRequirementElement>(prefab);
            for (int i = 0; i < reqs.Length; i++) if (reqs[i].m_Group == kOurGroup) return true;
            reqs.Add(new ObjectRequirementElement(theme, kOurGroup, default(ObjectRequirementType)));
            return true;
        }

        private void RemoveRequirement(Entity prefab)
        {
            if (!EntityManager.Exists(prefab) || !EntityManager.HasBuffer<ObjectRequirementElement>(prefab)) return;
            DynamicBuffer<ObjectRequirementElement> reqs = EntityManager.GetBuffer<ObjectRequirementElement>(prefab);
            for (int i = reqs.Length - 1; i >= 0; i--) if (reqs[i].m_Group == kOurGroup) reqs.RemoveAt(i);
        }

        /// <summary>Ayrım açıkken, modeli seçilmemiş otobüs hatlarına şehir otobüsü modellerini yazar (oyunun paneliyle aynı tampon).</summary>
        private void FillCityLines()
        {
            HashSet<string> coach = Names(S.CoachModels), city = Names(S.CityBusModels);
            if (coach.Count == 0) return;
            List<VehicleCatalog.Item> buses = VehicleCatalog.Buses(EntityManager, m_VehiclePrefabQuery);
            NativeArray<Entity> lines = m_LineQuery.ToEntityArray(Allocator.Temp);
            int filled = 0;
            try
            {
                foreach (Entity line in lines)
                {
                    Entity lp = EntityManager.GetComponentData<PrefabRef>(line).m_Prefab;
                    if (!EntityManager.HasComponent<TransportLineData>(lp)) continue;
                    TransportLineData data = EntityManager.GetComponentData<TransportLineData>(lp);
                    if (data.m_TransportType != TransportType.Bus) continue;
                    DynamicBuffer<VehicleModel> models = EntityManager.GetBuffer<VehicleModel>(line);
                    bool empty = true;
                    for (int i = 0; i < models.Length; i++) if (models[i].m_PrimaryPrefab != Entity.Null) empty = false;
                    if (!empty) continue;
                    int added = 0;
                    foreach (VehicleCatalog.Item it in buses)
                    {
                        if (it.Locked || it.Size != data.m_SizeClass) continue;
                        string name = m_PrefabSystem.GetPrefabName(it.Prefab);
                        if (coach.Contains(name) || (city.Count > 0 && !city.Contains(name))) continue;
                        models.Add(new VehicleModel { m_PrimaryPrefab = it.Prefab });
                        added++;
                    }
                    if (added > 0) filled++;
                }
            }
            finally { lines.Dispose(); }
            if (filled > 0) Mod.log.Info($"Coach mode: city bus models written to {filled} lines");
        }

        // ---------------------------------------------------------------- durum JSON

        private void PushState()
        {
            var sb = new StringBuilder(8192);
            float t = m_Time.normalizedTime;
            bool night = t < 0.25f || t >= 11f / 12f;
            sb.Append("{\"outside\":").Append(B(S.OutsideTaxis))
              .Append(",\"tariff\":").Append(B(S.TaxiTariff))
              .Append(",\"perKm\":").Append(S.TaxiPerKm)
              .Append(",\"nightPct\":").Append(S.TaxiNightPercent)
              .Append(",\"night\":").Append(B(night))
              .Append(",\"coachMode\":").Append(B(S.CoachMode));
            Entity policy = FeePolicy();
            if (policy != Entity.Null && EntityManager.HasComponent<PolicySliderData>(policy))
            {
                PolicySliderData s = EntityManager.GetComponentData<PolicySliderData>(policy);
                float v = OpeningFee(out bool _);
                sb.Append(",\"opening\":{\"v\":").Append(Num(v)).Append(",\"min\":").Append(Num(s.m_Range.min)).Append(",\"max\":").Append(Num(s.m_Range.max))
                  .Append(",\"step\":").Append(Num(s.m_Step <= 0f ? 1f : s.m_Step)).Append('}');
            }

            // Taksiler
            int total = 0, outside = 0, busy = 0, waiting = 0, returning = 0, dispatched = 0;
            NativeArray<Entity> taxis = m_TaxiQuery.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (Entity e in taxis)
                {
                    TaxiFlags f = EntityManager.GetComponentData<Game.Vehicles.Taxi>(e).m_State;
                    if (!EntityManager.HasComponent<Game.Objects.Moving>(e)) continue;   // park halindekiler sayılmaz
                    total++;
                    if ((f & TaxiFlags.FromOutside) != 0) outside++;
                    if ((f & TaxiFlags.Transporting) != 0) busy++;
                    else if ((f & TaxiFlags.Returning) != 0) returning++;
                    else if ((f & TaxiFlags.Dispatched) != 0) dispatched++;
                    else waiting++;
                }
            }
            finally { taxis.Dispose(); }
            sb.Append(",\"taxis\":{\"total\":").Append(total).Append(",\"outside\":").Append(outside).Append(",\"busy\":").Append(busy)
              .Append(",\"free\":").Append(waiting).Append(",\"returning\":").Append(returning).Append(",\"dispatched\":").Append(dispatched).Append('}');

            // Duraklar: en çok yolcu bekleyenler
            var stands = new List<(Entity e, int w)>();
            int standWaiting = 0;
            NativeArray<Entity> st = m_StandQuery.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (Entity e in st)
                {
                    int w = EntityManager.HasComponent<WaitingPassengers>(e) ? EntityManager.GetComponentData<WaitingPassengers>(e).m_Count : 0;
                    standWaiting += w;
                    stands.Add((e, w));
                }
            }
            finally { st.Dispose(); }
            stands.Sort((a, b) => b.w.CompareTo(a.w));
            var ex = new TransitExporter(EntityManager, m_Names);
            sb.Append(",\"stands\":{\"count\":").Append(stands.Count).Append(",\"waiting\":").Append(standWaiting).Append(",\"top\":[");
            for (int i = 0; i < stands.Count && i < 8; i++)
            {
                Entity e = stands[i].e;
                string name = "";
                try { name = ex.StationName(ex.TopBuilding(e), e); } catch (Exception) { }
                float3 p = EntityManager.HasComponent<Game.Objects.Transform>(e) ? EntityManager.GetComponentData<Game.Objects.Transform>(e).m_Position : default;
                if (i > 0) sb.Append(',');
                sb.Append("{\"n\":").Append(TransitExporter.Str(name ?? "")).Append(",\"w\":").Append(stands[i].w)
                  .Append(",\"x\":").Append(Num(p.x)).Append(",\"y\":").Append(Num(p.y)).Append(",\"z\":").Append(Num(p.z)).Append('}');
            }
            sb.Append("]}");

            // Şehir dışından geçen otobüsler
            int coaches = 0;
            NativeArray<Entity> pts = m_PtQuery.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (Entity e in pts)
                {
                    if ((EntityManager.GetComponentData<Game.Vehicles.PublicTransport>(e).m_State & PublicTransportFlags.DummyTraffic) == 0) continue;
                    Entity pf = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (EntityManager.HasComponent<PublicTransportVehicleData>(pf) && EntityManager.GetComponentData<PublicTransportVehicleData>(pf).m_TransportType == TransportType.Bus) coaches++;
                }
            }
            finally { pts.Dispose(); }
            sb.Append(",\"coaches\":").Append(coaches);

            // Model listeleri: sekme açılınca bir kez kurulur, seçim durumu her seferinde
            if (m_ListJson.Length == 0)
            {
                var lb = new StringBuilder(16384);
                lb.Append(",\"taxiList\":");
                VehicleCatalog.Write(lb, EntityManager, m_PrefabSystem, m_Images, VehicleCatalog.Taxis(EntityManager, m_VehiclePrefabQuery), Game.Vehicles.EnergyTypes.None);
                lb.Append(",\"busList\":");
                VehicleCatalog.Write(lb, EntityManager, m_PrefabSystem, m_Images, VehicleCatalog.Buses(EntityManager, m_VehiclePrefabQuery), Game.Vehicles.EnergyTypes.None);
                m_ListJson = lb.ToString();
            }
            sb.Append(m_ListJson);
            AppendNames(sb, "taxiOff", S.TaxiModelsOff);
            AppendNames(sb, "coach", S.CoachModels);
            AppendNames(sb, "city", S.CityBusModels);
            sb.Append('}');
            m_State.Update(sb.ToString());
        }

        private static void AppendNames(StringBuilder sb, string key, string names)
        {
            sb.Append(",\"").Append(key).Append("\":[");
            int n = 0;
            foreach (string x in Names(names)) { if (n++ > 0) sb.Append(','); sb.Append(TransitExporter.Str(x)); }
            sb.Append(']');
        }

        private static string B(bool b) => b ? "true" : "false";
        private static string Num(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "0" : v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
