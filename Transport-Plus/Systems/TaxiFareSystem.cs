using Game;
using Game.Common;
using Game.Simulation;
using Game.Tools;
using Game.Vehicles;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace TransportPlus.Systems
{
    /// <summary>
    /// Transport+ taksi tarifesi: km başı ücret ve gece çarpanı.
    ///
    /// Oyunda taksi ücreti = açılış ücreti (şehir politikası) + yolculuk mesafesi × 0,03 / m (30 / km, oyunun kodunda sabit).
    /// Mesafe kısmı taksi yolcu indirmeye başlarken (TaxiFlags.Disembarking) Taxi.m_CurrentFee'ye eklenir; yolcular inerken bu
    /// ücreti öder (ResidentAISystem). Bu sistem TaxiAISystem'den hemen sonra çalışır ve inişe başlayan taksinin ücretini
    /// (açılış + mesafe × kendi km ücretimiz) × gece çarpanı olarak yeniden yazar. Her yolculuk bir kez düzeltilir.
    /// Tarife kapalıyken hiçbir şey yapmaz; oyunun hesabı aynen kalır.
    /// </summary>
    public partial class TaxiFareSystem : GameSystemBase
    {
        private const float kVanillaPerMeter = 0.03f;

        private EntityQuery m_TaxiQuery;
        private TimeSystem m_Time;
        private NativeParallelHashMap<Entity, ushort> m_Done;
        private JobHandle m_Last;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Time = World.GetOrCreateSystemManaged<TimeSystem>();
            m_TaxiQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadWrite<Game.Vehicles.Taxi>(), ComponentType.ReadOnly<Odometer>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() }
            });
            m_Done = new NativeParallelHashMap<Entity, ushort>(256, Allocator.Persistent);
            RequireForUpdate(m_TaxiQuery);
        }

        protected override void OnDestroy()
        {
            m_Last.Complete();
            if (m_Done.IsCreated) m_Done.Dispose();
            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            Setting s = Mod.Settings;
            if (s == null || !s.TaxiTariff) return;
            float t = m_Time.normalizedTime;
            bool night = t < 0.25f || t >= 11f / 12f;   // oyunun toplu taşıma gecesi: 22.00–06.00
            m_Last.Complete();
            if (m_Done.Count() > 4096) m_Done.Clear();

            NativeList<Entity> taxis = m_TaxiQuery.ToEntityListAsync(Allocator.TempJob, out JobHandle listDeps);
            var job = new FareJob
            {
                m_Entities = taxis.AsDeferredJobArray(),
                m_Taxis = GetComponentLookup<Game.Vehicles.Taxi>(false),
                m_Odometers = GetComponentLookup<Odometer>(true),
                m_Done = m_Done,
                m_PerMeter = math.max(0, s.TaxiPerKm) / 1000f,
                m_Multiplier = night ? math.max(0.5f, s.TaxiNightPercent / 100f) : 1f
            };
            m_Last = job.Schedule(JobHandle.CombineDependencies(Dependency, listDeps));
            taxis.Dispose(m_Last);
            Dependency = m_Last;
        }

        private struct FareJob : IJob
        {
            [ReadOnly] public NativeArray<Entity> m_Entities;
            public ComponentLookup<Game.Vehicles.Taxi> m_Taxis;
            [ReadOnly] public ComponentLookup<Odometer> m_Odometers;
            public NativeParallelHashMap<Entity, ushort> m_Done;
            public float m_PerMeter, m_Multiplier;

            public void Execute()
            {
                for (int i = 0; i < m_Entities.Length; i++)
                {
                    Entity e = m_Entities[i];
                    Game.Vehicles.Taxi taxi = m_Taxis[e];
                    if ((taxi.m_State & TaxiFlags.Disembarking) == 0)
                    {
                        m_Done.Remove(e);
                        continue;
                    }
                    if (m_Done.TryGetValue(e, out ushort done) && done == taxi.m_CurrentFee) continue;
                    float distance = m_Odometers.TryGetComponent(e, out Odometer odo) ? math.max(0f, odo.m_Distance - taxi.m_StartDistance) : 0f;
                    int vanilla = (int)math.round(distance * kVanillaPerMeter);
                    int opening = math.max(0, taxi.m_CurrentFee - vanilla);
                    int fee = (int)math.round((opening + distance * m_PerMeter) * m_Multiplier);
                    taxi.m_CurrentFee = (ushort)math.clamp(fee, 0, 65535);
                    m_Taxis[e] = taxi;
                    m_Done[e] = taxi.m_CurrentFee;
                }
            }
        }
    }
}
