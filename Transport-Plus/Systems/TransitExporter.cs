using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Colossal.Mathematics;
using Game.Buildings;
using Game.Common;
using Game.Prefabs;
using Game.Routes;
using Game.UI;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace TransportPlus.Systems
{
    /// <summary>
    /// Oyundaki yolcu hatlarını (metro, tren, tramvay, vapur, otobüs) okuyup arayüzün beklediği JSON'a çevirir.
    /// Export: şematik ağ haritası { city, stations[], lines[], transfers[], water[], diag }.
    /// ExportRoutes: rota sekmesi { city, lines[] (durak sırası + güzergâh), roads[], water[] }.
    /// </summary>
    internal sealed class TransitExporter
    {
        private sealed class Station
        {
            public string Id;
            public string Name;
            public float3 Pos;
            public bool Outside;
        }

        /// <summary>Bir hattın durağa bağlı yol noktası ve o durak.</summary>
        private struct StopRef
        {
            public Entity Waypoint, Stop;
        }

        /// <summary>Kaç hat bulundu, kaçı neden gösterilmedi. Arayüz bunu lejantın altında yazar; hata bildirimlerinde işe yarar.</summary>
        private sealed class Diag
        {
            public int Routes, Cargo, Other, FewStops, FerryOff;
            public readonly Dictionary<string, int> Shown = new Dictionary<string, int>();

            public void Count(string type)
            {
                int n;
                Shown.TryGetValue(type, out n);
                Shown[type] = n + 1;
            }

            public string ToJson()
            {
                var sb = new StringBuilder("{\"routes\":").Append(Routes).Append(",\"shown\":{");
                bool first = true;
                foreach (KeyValuePair<string, int> kv in Shown)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('"').Append(kv.Key).Append("\":").Append(kv.Value);
                }
                return sb.Append("},\"cargo\":").Append(Cargo).Append(",\"other\":").Append(Other)
                    .Append(",\"few\":").Append(FewStops).Append(",\"ferryOff\":").Append(FerryOff).Append('}').ToString();
            }

            public override string ToString()
            {
                var sb = new StringBuilder();
                foreach (KeyValuePair<string, int> kv in Shown) sb.Append(kv.Key).Append(' ').Append(kv.Value).Append(", ");
                return $"{Routes} route entities; shown: {sb}skipped: {Cargo} cargo, {Other} other types, {FewStops} with fewer than 2 stops, {FerryOff} ferries (turned off)";
            }
        }

        private sealed class LineOut
        {
            public string Id, Type, Name, Color;
            public List<Station> Stops;
            public bool Loop;
        }

        private readonly EntityManager m_EM;
        private readonly NameSystem m_Names;

        public TransitExporter(EntityManager em, NameSystem names)
        {
            m_EM = em;
            m_Names = names;
        }

        /// <param name="waterDepth">Dünya konumundaki su derinliği (metre). null ise su çizilmez.</param>
        public string Export(EntityQuery lineQuery, string cityName, float mergeDistance, bool includeFerries, Func<float3, float> waterDepth = null, float transferDistance = 0f)
        {
            var stations = new List<Station>();
            var byEntity = new Dictionary<Entity, Station>();
            var lines = new List<LineOut>();
            var diag = new Diag();

            NativeArray<Entity> entities = lineQuery.ToEntityArray(Allocator.Temp);
            try
            {
                diag.Routes = entities.Length;
                foreach (Entity line in entities)
                {
                    string type;
                    if (!TryGetType(line, out type, diag)) continue;
                    if (type == "ship" && !includeFerries) { diag.FerryOff++; continue; }

                    // Rota, yol noktalarından oluşan kapalı bir döngü. Durağa bağlı olanlar gerçek duraklar.
                    List<StopRef> refs = StopsOf(line);
                    var seq = new List<Station>();
                    foreach (StopRef r in refs)
                    {
                        Station st = GetStation(r.Stop, r.Waypoint, stations, byEntity, mergeDistance);
                        if (st == null) continue;
                        if (seq.Count == 0 || seq[seq.Count - 1] != st) seq.Add(st);
                    }
                    if (seq.Count > 1 && seq[0] == seq[seq.Count - 1]) seq.RemoveAt(seq.Count - 1);
                    if (seq.Count < 2)
                    {
                        diag.FewStops++;
                        Mod.log.Info($"Line skipped ({type}, entity {line.Index}): {refs.Count} stop waypoints, {seq.Count} separate stations");
                        continue;
                    }
                    diag.Count(type);

                    bool loop;
                    List<Station> path = Unfold(seq, out loop);

                    lines.Add(new LineOut
                    {
                        Id = LineCode(line, type),
                        Type = type,
                        Name = CustomName(line), // boşsa arayüz "ilk durak – son durak" yazar
                        Color = LineColor(line),
                        Stops = path,
                        Loop = loop
                    });
                }
            }
            finally
            {
                entities.Dispose();
            }

            List<WaterContours.Loop> water = null;
            if (waterDepth != null && lines.Count > 0)
            {
                try { water = BuildWater(lines, waterDepth); }
                catch (Exception e) { Mod.log.Warn(e, "Water outline could not be built"); }
            }
            List<Transfer> transfers = FindTransfers(lines, mergeDistance, transferDistance);
            Mod.log.Info("Network map: " + diag);
            return ToJson(cityName, stations, lines, water, transfers, diag);
        }

        /// <summary>Hattın durağa bağlı yol noktaları, hat sırasıyla. Taksi durakları sayılmaz (oyunun kendi sayımı gibi).</summary>
        private List<StopRef> StopsOf(Entity line)
        {
            var list = new List<StopRef>();
            if (!m_EM.HasBuffer<RouteWaypoint>(line)) return list;
            DynamicBuffer<RouteWaypoint> waypoints = m_EM.GetBuffer<RouteWaypoint>(line, true);
            for (int i = 0; i < waypoints.Length; i++)
            {
                Entity wp = waypoints[i].m_Waypoint;
                if (wp == Entity.Null || !m_EM.HasComponent<Connected>(wp)) continue;
                Entity stop = m_EM.GetComponentData<Connected>(wp).m_Connected;
                if (stop == Entity.Null || !m_EM.Exists(stop)) continue;
                if (m_EM.HasComponent<Game.Routes.TaxiStand>(stop)) continue;
                list.Add(new StopRef { Waypoint = wp, Stop = stop });
            }
            return list;
        }

        // ---------------------------------------------------------------- hat türü

        private bool TryGetType(Entity line, out string type, Diag diag)
        {
            type = null;
            if (!m_EM.HasComponent<PrefabRef>(line)) { diag.Other++; return false; }
            Entity prefab = m_EM.GetComponentData<PrefabRef>(line).m_Prefab;
            if (!m_EM.HasComponent<TransportLineData>(prefab)) { diag.Other++; return false; }
            TransportLineData data = m_EM.GetComponentData<TransportLineData>(prefab);
            if (!data.m_PassengerTransport) { diag.Cargo++; return false; } // yük trenleri ve yük gemileri haritaya girmez

            // Sayı değeriyle değil adıyla eşlenir: oyun güncellemesi ya da bir DLC enum'a yeni tür eklerse
            // (ör. Ferry) eski derlenmiş mod yanlış türü okumaz.
            switch (data.m_TransportType.ToString())
            {
                case "Subway": type = "subway"; return true;
                case "Train": type = "train"; return true;
                case "Tram": type = "tram"; return true;
                case "Ship":
                case "Ferry": type = "ship"; return true;
                case "Bus": type = "bus"; return true;
                default: diag.Other++; return false; // taksi, uçak, helikopter vb.
            }
        }

        internal string LineCode(Entity line, string type)
        {
            // Oyuncu hatta "M1" gibi kısa bir ad verdiyse onu rozet olarak kullan
            string name = CustomName(line);
            if (!string.IsNullOrEmpty(name) && name.Length <= 4 && name.IndexOf(' ') < 0) return name;
            // "93B Topkapı Hacıosman" gibi adlarda rakam içeren ilk kelime hat kodu (LED tabelalarla aynı kural)
            string parsed = LineNames.Parse(name).Code;
            if (parsed.Length > 0) return parsed;

            int number = m_EM.HasComponent<RouteNumber>(line) ? m_EM.GetComponentData<RouteNumber>(line).m_Number : 0;
            // Otobüs hatları gerçek haritalardaki gibi yalnızca numarayla anılır
            string prefix = type == "subway" ? "M" : type == "train" ? "R" : type == "tram" ? "T" : type == "bus" ? "" : "F";
            return prefix + number.ToString(CultureInfo.InvariantCulture);
        }

        private string LineColor(Entity line)
        {
            if (!m_EM.HasComponent<Color>(line)) return "#888888";
            UnityEngine.Color32 c = m_EM.GetComponentData<Color>(line).m_Color;
            return "#" + c.r.ToString("X2") + c.g.ToString("X2") + c.b.ToString("X2");
        }

        private string SafeName(Entity e)
        {
            try { return m_Names.GetRenderedLabelName(e) ?? string.Empty; }
            catch { return string.Empty; }
        }

        /// <summary>Yalnızca oyuncunun verdiği ad. Ad verilmemişse boş döner (varsayılan ad prefab adıdır, işe yaramaz).</summary>
        internal string CustomName(Entity e)
        {
            try { return m_Names.TryGetCustomName(e, out string custom) ? custom ?? string.Empty : string.Empty; }
            catch { return string.Empty; }
        }

        /// <summary>
        /// İstasyonun haritadaki adı: oyuncunun verdiği ad, yoksa bulunduğu caddenin adı, o da yoksa oyunun gösterdiği ad.
        /// </summary>
        internal string StationName(Entity building, Entity stop)
        {
            string name = CustomName(building);
            if (name.Length == 0 && building != stop) name = CustomName(stop);
            if (name.Length == 0) name = StreetName(building);
            if (name.Length == 0 && building != stop) name = StreetName(stop);
            if (name.Length == 0) name = SafeName(building);
            return name;
        }

        private string StreetName(Entity e)
        {
            Entity edge = Entity.Null;
            if (m_EM.HasComponent<Building>(e)) edge = m_EM.GetComponentData<Building>(e).m_RoadEdge;
            else if (m_EM.HasComponent<Game.Objects.Attached>(e)) edge = m_EM.GetComponentData<Game.Objects.Attached>(e).m_Parent;
            if (edge == Entity.Null || !m_EM.HasComponent<Game.Net.Aggregated>(edge)) return string.Empty;
            Entity street = m_EM.GetComponentData<Game.Net.Aggregated>(edge).m_Aggregate;
            return street == Entity.Null ? string.Empty : SafeName(street);
        }

        // ---------------------------------------------------------------- istasyonlar

        private Station GetStation(Entity stop, Entity waypoint, List<Station> stations, Dictionary<Entity, Station> byEntity, float mergeDistance)
        {
            Entity key = TopBuilding(stop);
            Station found;
            if (byEntity.TryGetValue(key, out found)) return found;

            // Bina → durağın kendisi → hattın yol noktası. Sonuncusu her hatta vardır; böylece konumu okunamayan
            // bir durak (ör. başka bir modun eklediği istasyon) yüzünden hat haritadan düşmez.
            float3 pos;
            if (!TryGetPosition(key, out pos) && !TryGetPosition(stop, out pos) && !TryGetWaypointPosition(waypoint, out pos)) return null;
            bool outside = IsOutside(stop);

            // Yakındaki bir istasyonla birleştir (caddenin iki yanındaki duraklar, istasyon önündeki tramvay durağı)
            if (mergeDistance > 0f && !outside)
            {
                foreach (Station other in stations)
                {
                    if (!other.Outside && math.distance(other.Pos.xz, pos.xz) <= mergeDistance)
                    {
                        byEntity[key] = other;
                        return other;
                    }
                }
            }

            string name = StationName(key, stop);
            var st = new Station
            {
                Id = "s" + stations.Count.ToString(CultureInfo.InvariantCulture),
                Name = string.IsNullOrEmpty(name) ? "Stop " + (stations.Count + 1) : name,
                Pos = pos,
                Outside = outside
            };
            stations.Add(st);
            byEntity[key] = st;
            return st;
        }

        /// <summary>Durağın içinde bulunduğu en üstteki binayı bulur. Yol kenarı duraklarında durağın kendisi döner.</summary>
        internal Entity TopBuilding(Entity stop)
        {
            Entity cur = stop, best = Entity.Null;
            for (int guard = 0; guard < 8 && m_EM.HasComponent<Owner>(cur); guard++)
            {
                cur = m_EM.GetComponentData<Owner>(cur).m_Owner;
                if (cur == Entity.Null) break;
                if (m_EM.HasComponent<Building>(cur)) best = cur;
            }
            return best != Entity.Null ? best : stop;
        }

        private bool TryGetPosition(Entity e, out float3 pos)
        {
            if (m_EM.HasComponent<Game.Objects.Transform>(e))
            {
                pos = m_EM.GetComponentData<Game.Objects.Transform>(e).m_Position;
                return true;
            }
            pos = default;
            return false;
        }

        private bool TryGetWaypointPosition(Entity waypoint, out float3 pos)
        {
            if (waypoint != Entity.Null && m_EM.HasComponent<Position>(waypoint))
            {
                pos = m_EM.GetComponentData<Position>(waypoint).m_Position;
                return true;
            }
            pos = default;
            return false;
        }

        /// <summary>Durak ya da sahiplerinden biri şehir dışı bağlantısı mı (harita kenarındaki tren, gemi, otobüs çıkışı).</summary>
        private bool IsOutside(Entity stop)
        {
            Entity cur = stop;
            for (int guard = 0; guard < 6 && cur != Entity.Null; guard++)
            {
                if (m_EM.HasComponent<Game.Objects.OutsideConnection>(cur) || m_EM.HasComponent<Game.Net.OutsideConnection>(cur)) return true;
                if (!m_EM.HasComponent<Owner>(cur)) break;
                cur = m_EM.GetComponentData<Owner>(cur).m_Owner;
            }
            return false;
        }

        // ---------------------------------------------------------------- gidiş-dönüş → tek yön

        /// <summary>
        /// Oyunda iki yönlü bir hat A B C D C B şeklinde tek döngüdür. Dönüş noktalarını (öncesi ve sonrası aynı istasyon)
        /// bulup A B C D'ye indirger. Gerçek ring hatlarda dönüş noktası yoktur; onlar loop olarak kalır.
        /// </summary>
        private static List<Station> Unfold(List<Station> seq, out bool loop)
        {
            int n = seq.Count;
            if (n == 2) { loop = false; return new List<Station>(seq); }

            var turns = new List<int>();
            for (int i = 0; i < n; i++)
                if (seq[(i - 1 + n) % n] == seq[(i + 1) % n]) turns.Add(i);

            if (turns.Count == 2)
            {
                var path = new List<Station>();
                for (int i = turns[0]; ; i = (i + 1) % n)
                {
                    path.Add(seq[i]);
                    if (i == turns[1]) break;
                }
                loop = false;
                return path;
            }

            loop = true;
            return new List<Station>(seq);
        }

        // ---------------------------------------------------------------- aktarmalar

        private struct Transfer { public Station A, B; public float Meters; }

        /// <summary>
        /// Ortak hattı olmayan, birbirine transferDistance'tan yakın istasyon çiftleri (ör. tren istasyonu + yanındaki metro).
        /// Her istasyon yalnızca en yakın birkaç komşusuna bağlanır.
        /// </summary>
        private static List<Transfer> FindTransfers(List<LineOut> lines, float mergeDistance, float transferDistance)
        {
            var result = new List<Transfer>();
            if (transferDistance <= mergeDistance) return result;
            var linesOf = new Dictionary<Station, HashSet<LineOut>>();
            foreach (LineOut l in lines)
                foreach (Station s in l.Stops)
                {
                    HashSet<LineOut> set;
                    if (!linesOf.TryGetValue(s, out set)) linesOf[s] = set = new HashSet<LineOut>();
                    set.Add(l);
                }
            var list = new List<Station>(linesOf.Keys);
            var count = new Dictionary<Station, int>();
            var pairs = new List<Transfer>();
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                {
                    float d = math.distance(list[i].Pos.xz, list[j].Pos.xz);
                    if (d > transferDistance) continue;
                    if (linesOf[list[i]].Overlaps(linesOf[list[j]])) continue; // aynı hattın komşu durakları aktarma değildir
                    pairs.Add(new Transfer { A = list[i], B = list[j], Meters = d });
                }
            pairs.Sort((x, y) => x.Meters.CompareTo(y.Meters));
            foreach (Transfer t in pairs)
            {
                int ca, cb;
                count.TryGetValue(t.A, out ca); count.TryGetValue(t.B, out cb);
                if (ca >= 3 || cb >= 3) continue;
                count[t.A] = ca + 1; count[t.B] = cb + 1;
                result.Add(t);
            }
            return result;
        }

        // ---------------------------------------------------------------- su

        /// <summary>Hatların çevresindeki alanı örnekleyip kıyı çizgilerini çıkarır.</summary>
        private static List<WaterContours.Loop> BuildWater(List<LineOut> lines, Func<float3, float> waterDepth)
        {
            float2 mn = new float2(float.MaxValue), mx = new float2(float.MinValue);
            float y = 0f;
            int count = 0;
            foreach (LineOut l in lines)
                foreach (Station s in l.Stops)
                {
                    if (s.Outside) continue; // harita kenarındaki çıkışlar su alanını gereksiz büyütür
                    mn = math.min(mn, s.Pos.xz); mx = math.max(mx, s.Pos.xz);
                    y += s.Pos.y; count++;
                }
            if (count == 0) return new List<WaterContours.Loop>();
            y /= math.max(1, count);
            float span = math.cmax(mx - mn);
            float margin = math.max(1500f, span * 0.35f);
            mn -= margin; mx += margin;
            float cell = math.max(20f, math.cmax(mx - mn) / 180f);
            const float kMinDepth = 1f; // bundan sığ su (ıslak kıyı, su birikintisi) kara sayılır
            List<WaterContours.Loop> loops = WaterContours.Build(mn, mx, cell, p => waterDepth(new float3(p.x, y, p.y)) - kMinDepth);
            Mod.log.Info($"Water outline: {loops.Count} shapes, cell {cell:0} m");
            return loops;
        }

        // ---------------------------------------------------------------- rota sekmesi

        /// <summary>
        /// Rota sekmesinin verisi: her yolcu hattı için durakların tam sırası (gidiş ve dönüş ayrı ayrı), aracın izlediği
        /// güzergâh, uzunluk ve araç sayısı; arkasına çizilecek yol ağı ve su. Koordinatlar metre, tam sayı.
        /// </summary>
        public string ExportRoutes(EntityQuery lineQuery, EntityQuery roadQuery, string cityName, bool includeFerries, Func<float3, float> waterDepth)
        {
            var sb = new StringBuilder(1 << 16);
            var names = new Dictionary<Entity, string>();
            var diag = new Diag();
            float2 mn = new float2(float.MaxValue), mx = new float2(float.MinValue);
            float ySum = 0f;
            int yCount = 0, lineCount = 0;

            sb.Append("{\"city\":").Append(Str(cityName)).Append(",\"lines\":[");
            NativeArray<Entity> entities = lineQuery.ToEntityArray(Allocator.Temp);
            try
            {
                diag.Routes = entities.Length;
                foreach (Entity line in entities)
                {
                    string type;
                    if (!TryGetType(line, out type, diag)) continue;
                    if (type == "ship" && !includeFerries) { diag.FerryOff++; continue; }
                    List<StopRef> refs = StopsOf(line);
                    if (refs.Count < 2) { diag.FewStops++; continue; }
                    diag.Count(type);

                    if (lineCount++ > 0) sb.Append(',');
                    sb.Append("{\"id\":\"L").Append(line.Index).Append('"')
                      .Append(",\"code\":").Append(Str(LineCode(line, type)))
                      .Append(",\"type\":").Append(Str(type))
                      .Append(",\"name\":").Append(Str(CustomName(line)))
                      .Append(",\"color\":").Append(Str(LineColor(line)));
                    AppendLineStats(sb, line);

                    sb.Append(",\"stops\":[");
                    var stopPos = new List<float2>(refs.Count);
                    for (int i = 0; i < refs.Count; i++)
                    {
                        StopRef r = refs[i];
                        float3 pos;
                        if (!TryGetPosition(r.Stop, out pos) && !TryGetWaypointPosition(r.Waypoint, out pos))
                            TryGetPosition(TopBuilding(r.Stop), out pos);
                        string name;
                        if (!names.TryGetValue(r.Stop, out name))
                        {
                            name = StationName(TopBuilding(r.Stop), r.Stop);
                            names[r.Stop] = name;
                        }
                        int waiting = m_EM.HasComponent<WaitingPassengers>(r.Waypoint) ? m_EM.GetComponentData<WaitingPassengers>(r.Waypoint).m_Count : 0;
                        if (i > 0) sb.Append(',');
                        sb.Append("{\"n\":").Append(Str(string.IsNullOrEmpty(name) ? "Stop " + (i + 1) : name))
                          .Append(",\"x\":").Append(Int(pos.x)).Append(",\"y\":").Append(Int(pos.y)).Append(",\"z\":").Append(Int(pos.z))
                          .Append(",\"w\":").Append(waiting);
                        if (IsOutside(r.Stop)) sb.Append(",\"out\":true");
                        else
                        {
                            mn = math.min(mn, pos.xz); mx = math.max(mx, pos.xz);
                            ySum += pos.y; yCount++;
                        }
                        sb.Append('}');
                        stopPos.Add(pos.xz);
                    }
                    sb.Append(']');

                    // Güzergâh: oyunun hat çizgisi için tuttuğu eğriler. Yoksa duraklar düz çizgiyle bağlanır.
                    var path = new List<float2>();
                    try { CollectPath(line, path); }
                    catch (Exception e) { Mod.log.Warn(e, "Route curve could not be read, using straight segments"); path.Clear(); }
                    if (path.Count < 2)
                    {
                        path.Clear();
                        path.AddRange(stopPos);
                        path.Add(stopPos[0]);
                    }
                    sb.Append(",\"path\":[");
                    for (int i = 0; i < path.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(Int(path[i].x)).Append(',').Append(Int(path[i].y));
                    }
                    sb.Append("]}");
                }
            }
            finally
            {
                entities.Dispose();
            }
            sb.Append(']');

            bool clip = yCount > 0;
            if (clip)
            {
                float margin = math.max(600f, math.cmax(mx - mn) * 0.15f);
                mn -= margin; mx += margin;
            }
            try { AppendRoads(sb, roadQuery, clip, mn, mx); }
            catch (Exception e) { Mod.log.Warn(e, "Road network could not be read, routes are drawn without roads"); }

            if (waterDepth != null && clip)
            {
                try
                {
                    float y = ySum / yCount;
                    float cell = math.max(25f, math.cmax(mx - mn) / 170f);
                    List<WaterContours.Loop> loops = WaterContours.Build(mn, mx, cell, p => waterDepth(new float3(p.x, y, p.y)) - 1f);
                    AppendWater(sb, loops);
                }
                catch (Exception e) { Mod.log.Warn(e, "Water outline could not be built for the route map"); }
            }
            sb.Append(",\"diag\":").Append(diag.ToJson()).Append('}');
            Mod.log.Info("Route list: " + diag);
            return sb.ToString();
        }

        /// <summary>Uzunluk (m), araç sayısı, açık/kapalı, gündüz/gece. Hepsi oyunun hat panelindeki değerlerle aynı kaynaktan.</summary>
        private void AppendLineStats(StringBuilder sb, Entity line)
        {
            float length = 0f;
            if (m_EM.HasBuffer<RouteSegment>(line))
            {
                DynamicBuffer<RouteSegment> segs = m_EM.GetBuffer<RouteSegment>(line, true);
                for (int i = 0; i < segs.Length; i++)
                {
                    Entity seg = segs[i].m_Segment;
                    if (seg != Entity.Null && m_EM.HasComponent<Game.Pathfind.PathInformation>(seg))
                        length += m_EM.GetComponentData<Game.Pathfind.PathInformation>(seg).m_Distance;
                }
            }
            int vehicles = m_EM.HasBuffer<RouteVehicle>(line) ? m_EM.GetBuffer<RouteVehicle>(line, true).Length : 0;
            bool active = true;
            int schedule = 0; // 0 = gündüz ve gece, 1 = yalnızca gündüz, 2 = yalnızca gece
            if (m_EM.HasComponent<Game.Routes.Route>(line))
            {
                Game.Routes.Route route = m_EM.GetComponentData<Game.Routes.Route>(line);
                active = !RouteUtils.CheckOption(route, RouteOption.Inactive);
                if (RouteUtils.CheckOption(route, RouteOption.Day)) schedule = 1;
                else if (RouteUtils.CheckOption(route, RouteOption.Night)) schedule = 2;
            }
            sb.Append(",\"len\":").Append(Int(length)).Append(",\"veh\":").Append(vehicles)
              .Append(",\"active\":").Append(active ? "true" : "false").Append(",\"sched\":").Append(schedule);
        }

        private void CollectPath(Entity line, List<float2> pts)
        {
            if (!m_EM.HasBuffer<RouteSegment>(line)) return;
            DynamicBuffer<RouteSegment> segs = m_EM.GetBuffer<RouteSegment>(line, true);
            for (int i = 0; i < segs.Length; i++)
            {
                Entity seg = segs[i].m_Segment;
                if (seg == Entity.Null || !m_EM.HasBuffer<CurveElement>(seg)) continue;
                DynamicBuffer<CurveElement> curves = m_EM.GetBuffer<CurveElement>(seg, true);
                for (int k = 0; k < curves.Length; k++) AddBezier(pts, curves[k].m_Curve, 8f);
            }
        }

        /// <summary>Eğriyi birkaç noktayla örnekler. Düz parçalar iki uçla geçilir, öncekine minStep'ten yakın noktalar atlanır.</summary>
        private static void AddBezier(List<float2> pts, Bezier4x3 b, float minStep)
        {
            float2 a = b.a.xz, d = b.d.xz;
            float chord = math.distance(a, d);
            float bend = math.max(DistToSegment(b.b.xz, a, d), DistToSegment(b.c.xz, a, d));
            int steps = bend < 1.5f ? 1 : math.clamp((int)math.ceil(chord / 35f) + 1, 2, 6);
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps, u = 1f - t;
                float3 p = b.a * (u * u * u) + b.b * (3f * u * u * t) + b.c * (3f * u * t * t) + b.d * (t * t * t);
                if (pts.Count > 0 && i < steps && math.distance(pts[pts.Count - 1], p.xz) < minStep) continue;
                if (pts.Count > 0 && math.distance(pts[pts.Count - 1], p.xz) < 0.5f) continue;
                pts.Add(p.xz);
            }
        }

        private static float DistToSegment(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float len2 = math.lengthsq(ab);
            float t = len2 > 1e-6f ? math.saturate(math.dot(p - a, ab) / len2) : 0f;
            return math.distance(p, a + ab * t);
        }

        /// <summary>
        /// Yol ağı, üç sınıfa ayrılmış çoklu çizgiler olarak: 0 = otoyol, 1 = geniş cadde, 2 = sokak.
        /// Biçim: "roads":[[[x,z,x,z,...],...],[...],[...]].
        /// </summary>
        private void AppendRoads(StringBuilder sb, EntityQuery roadQuery, bool clip, float2 mn, float2 mx)
        {
            const int kMaxEdges = 40000;
            var parts = new[] { new StringBuilder(), new StringBuilder(4096), new StringBuilder(1 << 15) };
            var classOf = new Dictionary<Entity, int>();
            var pts = new List<float2>(8);
            int count = 0;

            NativeArray<Entity> edges = roadQuery.ToEntityArray(Allocator.Temp);
            int total = edges.Length;
            try
            {
                foreach (Entity edge in edges)
                {
                    Bezier4x3 b = m_EM.GetComponentData<Game.Net.Curve>(edge).m_Bezier;
                    if (clip)
                    {
                        float2 lo = math.min(math.min(b.a.xz, b.b.xz), math.min(b.c.xz, b.d.xz));
                        float2 hi = math.max(math.max(b.a.xz, b.b.xz), math.max(b.c.xz, b.d.xz));
                        if (hi.x < mn.x || lo.x > mx.x || hi.y < mn.y || lo.y > mx.y) continue;
                    }
                    Entity prefab = m_EM.GetComponentData<PrefabRef>(edge).m_Prefab;
                    int cls;
                    if (!classOf.TryGetValue(prefab, out cls))
                    {
                        cls = RoadClass(prefab);
                        classOf[prefab] = cls;
                    }
                    if (count >= kMaxEdges && cls == 2) continue; // çok büyük şehirlerde önce sokaklardan vazgeç

                    pts.Clear();
                    AddBezier(pts, b, 0f);
                    if (pts.Count < 2) continue;
                    StringBuilder part = parts[cls];
                    if (part.Length > 0) part.Append(',');
                    part.Append('[');
                    for (int i = 0; i < pts.Count; i++)
                    {
                        if (i > 0) part.Append(',');
                        part.Append(Int(pts[i].x)).Append(',').Append(Int(pts[i].y));
                    }
                    part.Append(']');
                    count++;
                }
            }
            finally
            {
                edges.Dispose();
            }
            sb.Append(",\"roads\":[[").Append(parts[0]).Append("],[").Append(parts[1]).Append("],[").Append(parts[2]).Append("]]");
            Mod.log.Info($"Road network: {count} segments of {total}");
        }

        private int RoadClass(Entity prefab)
        {
            if (m_EM.HasComponent<RoadData>(prefab)
                && (m_EM.GetComponentData<RoadData>(prefab).m_Flags & Game.Prefabs.RoadFlags.UseHighwayRules) != 0) return 0;
            float width = m_EM.HasComponent<NetGeometryData>(prefab) ? m_EM.GetComponentData<NetGeometryData>(prefab).m_DefaultWidth : 8f;
            return width >= 22f ? 1 : 2;
        }

        // ---------------------------------------------------------------- JSON

        private static string ToJson(string city, List<Station> stations, List<LineOut> lines, List<WaterContours.Loop> water, List<Transfer> transfers, Diag diag)
        {
            var used = new HashSet<Station>();
            foreach (LineOut l in lines) foreach (Station s in l.Stops) used.Add(s);

            var sb = new StringBuilder(4096);
            sb.Append("{\"city\":").Append(Str(city)).Append(",\"stations\":[");
            bool first = true;
            foreach (Station s in stations)
            {
                if (!used.Contains(s)) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"id\":").Append(Str(s.Id))
                  .Append(",\"name\":").Append(Str(s.Name))
                  .Append(",\"x\":").Append(Num(s.Pos.x))
                  .Append(",\"y\":").Append(Num(s.Pos.y))
                  .Append(",\"z\":").Append(Num(s.Pos.z));
                if (s.Outside) sb.Append(",\"out\":true");
                sb.Append('}');
            }
            sb.Append("],\"lines\":[");
            for (int i = 0; i < lines.Count; i++)
            {
                LineOut l = lines[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":").Append(Str(l.Id))
                  .Append(",\"type\":").Append(Str(l.Type))
                  .Append(",\"name\":").Append(Str(l.Name))
                  .Append(",\"color\":").Append(Str(l.Color))
                  .Append(",\"loop\":").Append(l.Loop ? "true" : "false")
                  .Append(",\"stops\":[");
                for (int k = 0; k < l.Stops.Count; k++)
                {
                    if (k > 0) sb.Append(',');
                    sb.Append(Str(l.Stops[k].Id));
                }
                sb.Append("]}");
            }
            sb.Append("]");
            if (transfers != null && transfers.Count > 0)
            {
                sb.Append(",\"transfers\":[");
                for (int i = 0; i < transfers.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"a\":").Append(Str(transfers[i].A.Id)).Append(",\"b\":").Append(Str(transfers[i].B.Id))
                      .Append(",\"m\":").Append(Num(transfers[i].Meters)).Append('}');
                }
                sb.Append(']');
            }
            if (water != null && water.Count > 0)
            {
                sb.Append(",\"water\":[");
                for (int i = 0; i < water.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"d\":").Append(water[i].Depth).Append(",\"p\":[");
                    List<float2> pts = water[i].Points;
                    for (int k = 0; k < pts.Count; k++)
                    {
                        if (k > 0) sb.Append(',');
                        sb.Append('[').Append(Num(pts[k].x)).Append(',').Append(Num(pts[k].y)).Append(']');
                    }
                    sb.Append("]}");
                }
                sb.Append(']');
            }
            if (diag != null) sb.Append(",\"diag\":").Append(diag.ToJson());
            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendWater(StringBuilder sb, List<WaterContours.Loop> water)
        {
            if (water == null || water.Count == 0) return;
            sb.Append(",\"water\":[");
            for (int i = 0; i < water.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"d\":").Append(water[i].Depth).Append(",\"p\":[");
                List<float2> pts = water[i].Points;
                for (int k = 0; k < pts.Count; k++)
                {
                    if (k > 0) sb.Append(',');
                    sb.Append('[').Append(Num(pts[k].x)).Append(',').Append(Num(pts[k].y)).Append(']');
                }
                sb.Append("]}");
            }
            sb.Append(']');
        }

        private static string Int(float v) => ((int)math.round(v)).ToString(CultureInfo.InvariantCulture);

        private static string Num(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        internal static string Str(string s)
        {
            if (s == null) return "\"\"";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
