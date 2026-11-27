using System;
using System.IO;
using System.Text;
using Colossal.UI.Binding;
using Game;
using Game.City;
using Game.Common;
using Game.Input;
using Game.Prefabs;
using Game.Rendering;
using Game.Routes;
using Game.Simulation;
using Game.Tools;
using Game.UI;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace TransportPlus.Systems
{
    /// <summary>
    /// UI ile köprü. Menünün açık/kapalı durumunu tutar (buton ve kısayol tuşu aynı durumu değiştirir),
    /// menü açılınca harita verisini gönderir, "Yenile" ve "Şehirde göster" komutlarını alır.
    /// </summary>
    public partial class TransitMapUISystem : UISystemBase
    {
        private const string kGroup = "transportPlus";

        // Yalnızca şehir yüklüyken çalışır; ana menüde ve harita düzenleyicide devre dışı kalır.
        public override GameMode gameMode => GameMode.Game;

        private ValueBinding<bool> m_IsOpen;
        private ValueBinding<string> m_MapData;
        private ValueBinding<string> m_LastSaved;
        private ValueBinding<string> m_RoutesData;
        private ValueBinding<string> m_Options;
        private ValueBinding<bool> m_WorldView;
        private RouteOverlaySystem m_RouteOverlay;
        private int m_SaveCount;
        private bool m_RefreshRequested;
        private bool m_RoutesRequested;
        // Otomatik yenileme: pencere açıkken hatların özeti birkaç saniyede bir hesaplanır, değişirse veri yeniden gönderilir
        private ulong m_LineSignature;
        private int m_SignatureFrames;
        private bool m_RoutesShown;
        // PNG arka planda çizilir; bitince sonucu ana iş parçacığı arayüze bildirir
        private volatile string m_SaveResult;
        private EntityQuery m_RoadQuery;
        private NameSystem m_NameSystem;

        /// <summary>Kaydedilen resimler: ...\AppData\LocalLow\Colossal Order\Cities Skylines II\Transport+</summary>
        private static string ExportFolder => Path.Combine(UnityEngine.Application.persistentDataPath, "Transport+");
        private CityConfigurationSystem m_CityConfig;
        private CameraUpdateSystem m_Camera;
        private EntityQuery m_LineQuery;
        private ProxyAction m_ToggleAction;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_NameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            m_CityConfig = World.GetOrCreateSystemManaged<CityConfigurationSystem>();
            m_Camera = World.GetOrCreateSystemManaged<CameraUpdateSystem>();

            m_LineQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<TransportLine>(),
                    ComponentType.ReadOnly<Route>(),
                    ComponentType.ReadOnly<RouteWaypoint>(),
                    ComponentType.ReadOnly<PrefabRef>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>()
                }
            });

            m_RoadQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Net.Edge>(),
                    ComponentType.ReadOnly<Game.Net.Road>(),
                    ComponentType.ReadOnly<Game.Net.Curve>(),
                    ComponentType.ReadOnly<PrefabRef>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>()
                }
            });

            AddBinding(m_IsOpen = new ValueBinding<bool>(kGroup, "isOpen", false));
            AddBinding(m_RoutesData = new ValueBinding<string>(kGroup, "routesData", "{}"));
            AddBinding(m_Options = new ValueBinding<string>(kGroup, "options", OptionsJson()));
            AddBinding(new TriggerBinding(kGroup, "refreshRoutes", () => m_RoutesRequested = true));
            AddBinding(new TriggerBinding<string, int>(kGroup, "setOption", SetOption));
            // Şehirdeki rota görünümü (beyaz dünya + yollara çizilmiş rotalar)
            try { m_RouteOverlay = World.GetOrCreateSystemManaged<RouteOverlaySystem>(); }
            catch (Exception e) { m_RouteOverlay = null; Mod.log.Error(e, "Route overlay could not be created; the menu works without the city view"); }
            AddBinding(m_WorldView = new ValueBinding<bool>(kGroup, "worldView", false));
            AddBinding(new TriggerBinding<bool>(kGroup, "setWorldView", on => { try { m_RouteOverlay?.SetActive(on); } catch (Exception e) { Mod.log.Warn(e, "World view toggle failed"); } }));
            AddBinding(new TriggerBinding<string, string>(kGroup, "setWorldSelection", (type, line) => { try { m_RouteOverlay?.SetSelection(type, line); } catch (Exception e) { Mod.log.Warn(e, "World selection failed"); } }));
            AddBinding(m_MapData = new ValueBinding<string>(kGroup, "mapData", "{}"));
            AddBinding(new TriggerBinding<bool>(kGroup, "setOpen", SetOpen));
            AddBinding(new TriggerBinding(kGroup, "toggle", Toggle));
            AddBinding(new TriggerBinding(kGroup, "refresh", () => m_RefreshRequested = true));
            AddBinding(new TriggerBinding<float, float, float>(kGroup, "jumpTo", JumpTo));
            AddBinding(m_LastSaved = new ValueBinding<string>(kGroup, "lastSaved", ""));
            AddBinding(new TriggerBinding<string, string>(kGroup, "saveImage", SaveImage));
            AddBinding(new TriggerBinding(kGroup, "openFolder", OpenFolder));

            if (Mod.Settings != null)
            {
                m_ToggleAction = Mod.Settings.GetAction(Setting.kToggleAction);
                if (m_ToggleAction != null) m_ToggleAction.shouldBeEnabled = true;
            }
        }

        /// <summary>Menü açık mı (diğer sistemler ağır işleri yalnızca açıkken yapsın diye).</summary>
        internal bool IsOpen => m_IsOpen != null && m_IsOpen.value;

        protected override void OnUpdate()
        {
            base.OnUpdate();
            // Buradaki hiçbir hata oyunun arayüz güncellemesine sızmamalı: başka modlarla birlikte çalışırken
            // tek bir istisna tüm UIUpdate aşamasını bozabilir.
            try
            {
                if (m_ToggleAction != null && m_ToggleAction.WasPerformedThisFrame()) Toggle();
                // Dışa aktarma, arayüz geri çağrısının içinde değil sistemin kendi güncellemesinde çalışır
                if (m_RefreshRequested)
                {
                    m_RefreshRequested = false;
                    m_Options.Update(OptionsJson());
                    Refresh();
                }
                if (m_RoutesRequested)
                {
                    m_RoutesRequested = false;
                    m_RoutesShown = true;
                    RefreshRoutes();
                }
                bool world = m_RouteOverlay != null && m_RouteOverlay.Active;
                if (world != m_WorldView.value) m_WorldView.Update(world);
                string saved = m_SaveResult;
                if (saved != null)
                {
                    m_SaveResult = null;
                    m_LastSaved.Update(saved);
                }
                if (m_IsOpen.value && ++m_SignatureFrames >= 120)
                {
                    m_SignatureFrames = 0;
                    ulong sig = LineSignature();
                    if (sig != m_LineSignature)
                    {
                        m_LineSignature = sig;
                        Mod.log.Info("Lines changed, refreshing");
                        m_RefreshRequested = true;
                        if (m_RoutesShown) m_RoutesRequested = true;
                    }
                }
            }
            catch (Exception e)
            {
                m_RefreshRequested = false;
                m_RoutesRequested = false;
                Mod.log.Error(e, "Transport+ update failed");
            }
        }

        private static string OptionsJson()
        {
            Setting s = Mod.Settings;
            return "{\"buses\":" + (s != null && s.ShowBusesOnMap ? "true" : "false")
                + ",\"names\":" + (s != null ? math.clamp(s.StationNames, 0, 2) : 0) + "}";
        }

        /// <summary>Harita penceresinden değişen seçenekler ayarlara yazılır, böylece oyunu kapatıp açınca hatırlanır.</summary>
        private void SetOption(string key, int value)
        {
            try
            {
                Setting s = Mod.Settings;
                if (s == null) return;
                if (key == "names") s.StationNames = math.clamp(value, 0, 2);
                else if (key == "buses") s.ShowBusesOnMap = value != 0;
                else return;
                s.ApplyAndSave();
                m_Options.Update(OptionsJson());
            }
            catch (Exception e)
            {
                Mod.log.Warn(e, "Could not save the option");
            }
        }

        private void RefreshRoutes()
        {
            try
            {
                Setting s = Mod.Settings;
                bool ferries = s == null || s.IncludeFerries;
                string city = m_CityConfig != null ? m_CityConfig.cityName : string.Empty;
                var exporter = new TransitExporter(EntityManager, m_NameSystem);
                string json;
                try
                {
                    var water = World.GetOrCreateSystemManaged<WaterSystem>();
                    var surface = water.GetSurfaceData(out JobHandle deps);
                    deps.Complete();
                    json = exporter.ExportRoutes(m_LineQuery, m_RoadQuery, city, ferries, p => WaterUtils.SampleDepth(ref surface, p));
                }
                catch (Exception e)
                {
                    Mod.log.Warn(e, "Water data unavailable, exporting routes without water");
                    json = new TransitExporter(EntityManager, m_NameSystem).ExportRoutes(m_LineQuery, m_RoadQuery, city, ferries, null);
                }
                Mod.log.Info($"Route export finished: {json.Length} characters");
                m_RoutesData.Update(json);
            }
            catch (Exception e)
            {
                Mod.log.Error(e, "Route export failed");
                m_RoutesData.Update("{\"error\":true}");
            }
        }

        protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            // Ana menüye dönünce ya da yeni şehir yüklenince menüyü kapat, eski şehrin verisini temizle
            SetOpen(false);
            m_MapData.Update("{}");
            m_RoutesData.Update("{}");
            m_RefreshRequested = false;
            m_RoutesRequested = false;
            // Kısayol yalnızca şehir içinde dinlenir; menüde ve düzenleyicide başka modların tuşlarına karışmaz
            if (m_ToggleAction != null) m_ToggleAction.shouldBeEnabled = mode == GameMode.Game;
        }

        private void Toggle() => SetOpen(!m_IsOpen.value);

        private void SetOpen(bool open)
        {
            if (m_IsOpen.value == open) return;
            Mod.log.Info(open ? "Menu opened" : "Menu closed");
            m_IsOpen.Update(open);
            if (open)
            {
                m_RefreshRequested = true;
                m_LineSignature = LineSignature();
                m_SignatureFrames = 0;
            }
            else
            {
                m_RoutesShown = false;
                // Menü kapanınca şehirdeki rota görünümü de kapanır (arayüz bir hatayla kapandıysa bile)
                try { m_RouteOverlay.SetActive(false); } catch (Exception e) { Mod.log.Warn(e, "World view toggle failed"); }
            }
        }

        /// <summary>
        /// Hatların ucuz bir özeti: her hattın kimliği, durak sayısı, rengi, numarası ve açık/kapalı durumu.
        /// Hat eklenince, silinince, durak eklenip çıkarılınca ya da renk değişince değişir.
        /// </summary>
        private ulong LineSignature()
        {
            ulong h = 1469598103934665603UL;
            Action<long> mix = v => { h ^= (ulong)v; h *= 1099511628211UL; };
            Unity.Collections.NativeArray<Entity> lines = m_LineQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            try
            {
                mix(lines.Length);
                foreach (Entity e in lines)
                {
                    mix(e.Index); mix(e.Version);
                    mix(EntityManager.GetBuffer<RouteWaypoint>(e, true).Length);
                    if (EntityManager.HasComponent<Game.Routes.Color>(e))
                    {
                        UnityEngine.Color32 c = EntityManager.GetComponentData<Game.Routes.Color>(e).m_Color;
                        mix(c.r | (c.g << 8) | (c.b << 16));
                    }
                    if (EntityManager.HasComponent<RouteNumber>(e)) mix(EntityManager.GetComponentData<RouteNumber>(e).m_Number);
                    Route route = EntityManager.GetComponentData<Route>(e);
                    mix((long)route.m_Flags); mix(route.m_OptionMask);
                }
            }
            finally
            {
                lines.Dispose();
            }
            return h;
        }

        private void Refresh()
        {
            try
            {
                Setting s = Mod.Settings;
                float merge = s != null ? s.MergeDistance : 80f;
                float transfer = s != null ? s.TransferDistance : 250f;
                bool ferries = s == null || s.IncludeFerries;
                string city = m_CityConfig != null ? m_CityConfig.cityName : string.Empty;

                Mod.log.Info($"Export started ({m_LineQuery.CalculateEntityCount()} route entities)");
                var exporter = new TransitExporter(EntityManager, m_NameSystem);
                string json;
                try
                {
                    // Oyunun simülasyon için tuttuğu su derinliği verisi (CPU kopyası)
                    var water = World.GetOrCreateSystemManaged<WaterSystem>();
                    var surface = water.GetSurfaceData(out JobHandle deps);
                    deps.Complete();
                    json = exporter.Export(m_LineQuery, city, merge, ferries, p => WaterUtils.SampleDepth(ref surface, p), transfer);
                }
                catch (Exception e)
                {
                    Mod.log.Warn(e, "Water data unavailable, exporting without water");
                    json = new TransitExporter(EntityManager, m_NameSystem).Export(m_LineQuery, city, merge, ferries, null, transfer);
                }
                Mod.log.Info($"Export finished: {json.Length} characters, sending to the UI");
                m_MapData.Update(json);
            }
            catch (Exception e)
            {
                Mod.log.Error(e, "Transit map export failed");
                m_MapData.Update("{\"error\":true}");
            }
        }

        /// <param name="format">"svg2png" (data: SVG metni, burada PNG'ye çevrilir), "png" (data: base64) ya da "svg"</param>
        private void SaveImage(string format, string data)
        {
            m_SaveCount++;
            if (format == "svg2png")
            {
                SaveSvgAsPng(data);
                return;
            }
            try
            {
                bool png = format == "png";
                Directory.CreateDirectory(ExportFolder);
                string file = SafeFileName(m_CityConfig != null ? m_CityConfig.cityName : null)
                    + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + (png ? ".png" : ".svg");
                string path = Path.Combine(ExportFolder, file);
                if (png) File.WriteAllBytes(path, Convert.FromBase64String(data));
                else File.WriteAllText(path, data, new UTF8Encoding(false));
                Mod.log.Info($"Transit map saved to {path}");
                m_LastSaved.Update("{\"ok\":true,\"file\":" + Json(file) + ",\"n\":" + m_SaveCount + "}");
            }
            catch (Exception e)
            {
                Mod.log.Error(e, "Could not save the transit map image");
                m_LastSaved.Update("{\"ok\":false,\"error\":" + Json(e.Message) + ",\"n\":" + m_SaveCount + "}");
            }
        }

        /// <summary>
        /// PNG'yi arka planda çizer (büyük haritalarda bir saniyeyi bulabilir, oyun takılmasın).
        /// GDI+ çalışmazsa aynı resmi SVG olarak kaydeder ve bunu arayüze "fallback" ile bildirir.
        /// </summary>
        private void SaveSvgAsPng(string svg)
        {
            int n = m_SaveCount;
            string stem;
            try
            {
                Directory.CreateDirectory(ExportFolder);
                stem = Path.Combine(ExportFolder, SafeFileName(m_CityConfig != null ? m_CityConfig.cityName : null)
                    + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            }
            catch (Exception e)
            {
                Mod.log.Error(e, "Could not prepare the export folder");
                m_LastSaved.Update("{\"ok\":false,\"error\":" + Json(e.Message) + ",\"n\":" + n + "}");
                return;
            }
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    int w, h;
                    string error;
                    if (SvgRaster.TryRender(svg, stem + ".png", out w, out h, out error))
                    {
                        Mod.log.Info($"Transit map saved to {stem}.png ({w}x{h})");
                        m_SaveResult = "{\"ok\":true,\"file\":" + Json(Path.GetFileName(stem) + ".png") + ",\"n\":" + n + "}";
                        return;
                    }
                    Mod.log.Warn("PNG could not be created (" + error + "), saving SVG instead");
                    try { File.Delete(stem + ".png"); } catch (Exception) { /* yarım dosya kalmasın */ }
                    File.WriteAllText(stem + ".svg", svg, new UTF8Encoding(false));
                    m_SaveResult = "{\"ok\":true,\"fallback\":true,\"file\":" + Json(Path.GetFileName(stem) + ".svg") + ",\"n\":" + n + "}";
                }
                catch (Exception e)
                {
                    Mod.log.Error(e, "Could not save the transit map image");
                    m_SaveResult = "{\"ok\":false,\"error\":" + Json(e.Message) + ",\"n\":" + n + "}";
                }
            });
        }

        private void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(ExportFolder);
                UnityEngine.Application.OpenURL("file:///" + ExportFolder.Replace('\\', '/'));
            }
            catch (Exception e)
            {
                Mod.log.Warn(e, "Could not open the export folder");
            }
        }

        private static string SafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Transport+";
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim();
        }

        private static string Json(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? string.Empty)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 0x20) sb.Append(' ');
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        /// <summary>Kamerayı bir noktaya götürür (diğer sistemler için).</summary>
        internal void JumpToPosition(Unity.Mathematics.float3 p) => JumpTo(p.x, p.y, p.z);

        private void JumpTo(float x, float y, float z)
        {
            try
            {
                var orbit = m_Camera?.orbitCameraController;
                if (orbit == null) return;
                orbit.followedEntity = Entity.Null;
                orbit.pivot = new float3(x, y, z);
                if (m_Camera.activeCameraController != orbit) m_Camera.activeCameraController = orbit;
            }
            catch (Exception e)
            {
                Mod.log.Warn(e, "Could not move the camera");
            }
        }
    }
}
