using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Net;
using Game.Prefabs;
using Game.UI;
using Game.Vehicles;
using Unity.Collections;
using Unity.Entities;

namespace TransportPlus.Systems
{
    /// <summary>
    /// Seçilebilir araç prefablarını listeler. Ölçütler oyunun TransportVehicleSelectData.GetRandomVehicle'ı ile aynı, tek fark:
    /// tema (bölge) şartına bakılmaz, çünkü oyun hatta açıkça seçilen prefab için temayı yok sayar. Böylece bölge paketlerinin ve
    /// Paradox Mods'tan gelen assetlerin araçları, başka bölgeninkiler dahil, listede çıkar.
    ///
    /// Raylılarda oyunun ayrımı: "birincil" = çok birimli tren seti (tramvay, metro, banliyö) ya da yolcu vagonu;
    /// "ikincil" = lokomotif. Lokomotifli trenlerde hat bir vagon ve bir lokomotif listesi tutar.
    /// </summary>
    internal static class VehicleCatalog
    {
        public struct Item
        {
            public Entity Prefab;
            public int Capacity;
            public EnergyTypes Energy;
            public SizeClass Size;
            public bool Locked;
            public int Units;          // tren setindeki birim sayısı (bilgi amaçlı)
        }

        public static TrackTypes TrackOf(TransportType t) =>
            t == TransportType.Train ? TrackTypes.Train : t == TransportType.Tram ? TrackTypes.Tram : t == TransportType.Subway ? TrackTypes.Subway : TrackTypes.None;

        /// <param name="slot">0 = birincil, 1 = ikincil (yalnızca lokomotifli trenler)</param>
        public static List<Item> ForLine(EntityManager em, EntityQuery prefabQuery, TransportType type, SizeClass size, int slot)
        {
            var list = new List<Item>();
            NativeArray<Entity> prefabs = prefabQuery.ToEntityArray(Allocator.Temp);
            try
            {
                TrackTypes track = TrackOf(type);
                foreach (Entity p in prefabs)
                {
                    bool hasPt = em.HasComponent<PublicTransportVehicleData>(p);
                    PublicTransportVehicleData pt = hasPt ? em.GetComponentData<PublicTransportVehicleData>(p) : default;
                    bool forLines = hasPt && (pt.m_PurposeMask & PublicTransportPurpose.TransportLine) != 0;
                    var it = new Item { Prefab = p, Capacity = hasPt ? pt.m_PassengerCapacity : 0, Units = 1, Locked = IsLocked(em, p) };
                    switch (type)
                    {
                        case TransportType.Bus:
                            if (slot != 0 || !forLines || pt.m_TransportType != TransportType.Bus || !em.HasComponent<CarData>(p)) continue;
                            CarData car = em.GetComponentData<CarData>(p);
                            if (car.m_SizeClass != size) continue;
                            it.Energy = car.m_EnergyType; it.Size = car.m_SizeClass;
                            break;
                        case TransportType.Train:
                        case TransportType.Tram:
                        case TransportType.Subway:
                        {
                            if (!em.HasComponent<TrainData>(p)) continue;
                            TrainData train = em.GetComponentData<TrainData>(p);
                            if (train.m_TrackType != track) continue;
                            bool engine = em.HasComponent<TrainEngineData>(p);
                            bool carriage = em.HasComponent<TrainCarriageData>(p);
                            bool mu = em.HasComponent<MultipleUnitTrainData>(p);
                            bool primary = (engine && mu) || (carriage && !mu);
                            bool secondary = engine && !mu;
                            if (slot == 0 ? !(primary && forLines) : !secondary) continue;
                            it.Energy = train.m_EnergyType;
                            if (engine) it.Units = math_max(1, em.GetComponentData<TrainEngineData>(p).m_Count.x);
                            break;
                        }
                        case TransportType.Ship:
                        case TransportType.Ferry:
                        {
                            if (slot != 0 || !forLines || !em.HasComponent<WatercraftData>(p)) continue;
                            if (pt.m_TransportType != TransportType.Ship && pt.m_TransportType != TransportType.Ferry) continue;
                            WatercraftData w = em.GetComponentData<WatercraftData>(p);
                            if (w.m_SizeClass != size) continue;
                            it.Energy = w.m_EnergyType; it.Size = w.m_SizeClass;
                            break;
                        }
                        default:
                            continue;
                    }
                    list.Add(it);
                }
            }
            finally { prefabs.Dispose(); }
            return list;
        }

        /// <summary>Şehir otobüsü / şehirlerarası seçimi için: hat amaçlı bütün otobüsler, boyuttan bağımsız.</summary>
        public static List<Item> Buses(EntityManager em, EntityQuery prefabQuery)
        {
            var list = new List<Item>();
            NativeArray<Entity> prefabs = prefabQuery.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (Entity p in prefabs)
                {
                    if (!em.HasComponent<PublicTransportVehicleData>(p) || !em.HasComponent<CarData>(p)) continue;
                    PublicTransportVehicleData pt = em.GetComponentData<PublicTransportVehicleData>(p);
                    if (pt.m_TransportType != TransportType.Bus || (pt.m_PurposeMask & PublicTransportPurpose.TransportLine) == 0) continue;
                    CarData car = em.GetComponentData<CarData>(p);
                    list.Add(new Item { Prefab = p, Capacity = pt.m_PassengerCapacity, Energy = car.m_EnergyType, Size = car.m_SizeClass, Locked = IsLocked(em, p), Units = 1 });
                }
            }
            finally { prefabs.Dispose(); }
            return list;
        }

        public static List<Item> Taxis(EntityManager em, EntityQuery prefabQuery)
        {
            var list = new List<Item>();
            NativeArray<Entity> prefabs = prefabQuery.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (Entity p in prefabs)
                {
                    if (!em.HasComponent<TaxiData>(p) || !em.HasComponent<CarData>(p)) continue;
                    CarData car = em.GetComponentData<CarData>(p);
                    list.Add(new Item { Prefab = p, Capacity = em.GetComponentData<TaxiData>(p).m_PassengerCapacity, Energy = car.m_EnergyType, Size = car.m_SizeClass, Locked = IsLocked(em, p), Units = 1 });
                }
            }
            finally { prefabs.Dispose(); }
            return list;
        }

        public static bool IsLocked(EntityManager em, Entity p) => em.HasComponent<Locked>(p) && em.IsComponentEnabled<Locked>(p);

        /// <summary>Listeyi JSON'a yazar. extra: her öğe için ek alanlar (",\"k\":v" biçiminde) üreten isteğe bağlı işlev.</summary>
        public static void Write(StringBuilder sb, EntityManager em, PrefabSystem prefabs, ImageSystem images, List<Item> items, EnergyTypes available, Func<Item, string> extra = null)
        {
            sb.Append('[');
            for (int i = 0; i < items.Count; i++)
            {
                Item it = items[i];
                string thumb = null;
                try { thumb = images.GetThumbnail(it.Prefab); } catch (Exception) { /* küçük resim yoksa boş */ }
                bool usable = available == EnergyTypes.None || it.Energy == EnergyTypes.None || (it.Energy & available) != 0;
                if (i > 0) sb.Append(',');
                sb.Append("{\"e\":").Append(TransitExporter.Str(Key(it.Prefab)))
                  .Append(",\"id\":").Append(TransitExporter.Str(prefabs.GetPrefabName(it.Prefab)))
                  .Append(",\"thumb\":").Append(TransitExporter.Str(thumb ?? ""))
                  .Append(",\"cap\":").Append(it.Capacity)
                  .Append(",\"units\":").Append(it.Units)
                  .Append(",\"size\":").Append((int)it.Size)
                  .Append(",\"elec\":").Append((it.Energy & EnergyTypes.Electricity) != 0 ? "true" : "false")
                  .Append(",\"locked\":").Append(it.Locked ? "true" : "false")
                  .Append(",\"usable\":").Append(usable ? "true" : "false");
                if (extra != null) sb.Append(extra(it));
                sb.Append('}');
            }
            sb.Append(']');
        }

        public static string Key(Entity e) => e.Index.ToString(CultureInfo.InvariantCulture) + ":" + e.Version.ToString(CultureInfo.InvariantCulture);

        public static Entity Parse(string key)
        {
            if (string.IsNullOrEmpty(key)) return Entity.Null;
            string[] p = key.Split(':');
            if (p.Length != 2 || !int.TryParse(p[0], out int i) || !int.TryParse(p[1], out int v)) return Entity.Null;
            return new Entity { Index = i, Version = v };
        }

        private static int math_max(int a, int b) => a > b ? a : b;
    }
}
