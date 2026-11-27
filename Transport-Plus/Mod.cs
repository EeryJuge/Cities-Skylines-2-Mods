using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using TransportPlus.Systems;

namespace TransportPlus
{
    public class Mod : IMod
    {
        public static readonly ILog log = LogManager.GetLogger($"{nameof(TransportPlus)}.{nameof(Mod)}").SetShowsErrorsInUI(false);
        public static Setting Settings { get; private set; }

        public const string Version = "0.3.1";

        /// <summary>
        /// Her adım ayrı korunur: çok modlu kurulumlarda bir adım hata verirse (0.3'te bildirilen "menü açılmıyor" sorunu)
        /// geri kalanı yine yüklenir. Sıra önemli: önce ayarlar ve kısayol, sonra menü, en son yeni özellikler.
        /// </summary>
        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info($"OnLoad (Transport+ {Version})");

            Step("mod asset", () =>
            {
                if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                    log.Info($"Current mod asset at {asset.path}");
            });

            // 0.2 ile aynı sıra: ayar nesnesi, seçenekler, kısayol, metinler, en son kayıtlı ayarların okunması
            Step("settings", () => Settings = new Setting(this));
            Step("options UI", () => Settings?.RegisterInOptionsUI());
            Step("key bindings", () => Settings?.RegisterKeyBindings());
            Step("English texts", () => GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Settings)));
            Step("Turkish texts", () => GameManager.instance.localizationManager.AddSource("tr-TR", new LocaleTR(Settings)));
            Step("saved settings", () =>
            {
                if (Settings == null) return;
                AssetDatabase.global.LoadSettings(nameof(TransportPlus), Settings, new Setting(this));
                Migrate(Settings);
            });

            // Menü (harita, Hatlar sekmesi, kısayol) her şeyden önce
            Step("menu", () => updateSystem.UpdateAt<TransitMapUISystem>(SystemUpdatePhase.UIUpdate));
            // Şehirdeki rota görünümü: oyunun çizim sistemleriyle aynı aşamada, çizimi yapan OverlayRenderSystem'den önce
            Step("route overlay", () => updateSystem.UpdateBefore<RouteOverlaySystem, Game.Rendering.OverlayRenderSystem>(SystemUpdatePhase.Rendering));
            // Hat yönetimi ve durak istatistikleri (Hatlar sekmesinin ayrıntı paneli)
            Step("line manager", () => updateSystem.UpdateAt<LineManagerSystem>(SystemUpdatePhase.UIUpdate));
            // Taksi ve şehirlerarası otobüs ayarları; taksi tarifesi oyunun taksi yapay zekâsından hemen sonra
            Step("taxi tab", () => updateSystem.UpdateAt<TaxiCoachSystem>(SystemUpdatePhase.UIUpdate));
            // Otobüslerin LED hat tabelaları: oyunun araç konumlarını ara değerlediği sistemden hemen sonra
            Step("LED signs", () => updateSystem.UpdateAfter<DestinationSignSystem, Game.Rendering.ObjectInterpolateSystem>(SystemUpdatePhase.Rendering));
            Step("taxi fares", () => updateSystem.UpdateAfter<TaxiFareSystem, Game.Simulation.TaxiAISystem>(SystemUpdatePhase.GameSimulation));

            log.Info(s_Failed == 0 ? "OnLoad finished" : $"OnLoad finished with {s_Failed} failed step(s); see the errors above");
        }

        private static int s_Failed;

        private static void Step(string name, System.Action action)
        {
            try { action(); }
            catch (System.Exception e)
            {
                s_Failed++;
                log.Error(e, $"Loading step '{name}' failed; Transport+ continues without it");
            }
        }

        /// <summary>
        /// 0.1 varsayılanları hiç uygulamıyordu; o sürümde seçeneklere dokunanların dosyasında birleştirme ve aktarma
        /// mesafesi 0 kalmış olabilir (her iki yanlı durak ayrı istasyon, üst üste adlar). İkisi birden 0 ise bir kez düzelt.
        /// </summary>
        private static void Migrate(Setting s)
        {
            try
            {
                if (s.SettingsVersion >= 2) return;
                if (s.MergeDistance == 0 && s.TransferDistance == 0)
                {
                    log.Info("Settings from 0.1 had zero distances, restoring the defaults");
                    s.MergeDistance = 80;
                    s.TransferDistance = 250;
                    s.IncludeFerries = true;
                }
                s.SettingsVersion = 2;
                s.ApplyAndSave();
            }
            catch (System.Exception e)
            {
                log.Warn(e, "Settings migration failed");
            }
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
            if (Settings != null)
            {
                Settings.UnregisterInOptionsUI();
                Settings = null;
            }
        }
    }
}
