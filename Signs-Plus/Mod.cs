using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using SignsPlus.Systems;

namespace SignsPlus
{
    public class Mod : IMod
    {
        public static readonly ILog log = LogManager.GetLogger($"{nameof(SignsPlus)}.{nameof(Mod)}").SetShowsErrorsInUI(false);
        public static Setting Settings { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                log.Info($"Current mod asset at {asset.path}");

            Settings = new Setting(this);
            Settings.RegisterInOptionsUI();
            Settings.RegisterKeyBindings();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Settings));
            GameManager.instance.localizationManager.AddSource("tr-TR", new LocaleTR(Settings));
            AssetDatabase.global.LoadSettings(nameof(SignsPlus), Settings, new Setting(this));

            // Levha yüzleri her karede, oyunun çizim aşamasında gönderilir; editör penceresi arayüz aşamasında çalışır;
            // araç çubuğu sekmesi prefablar yüklenince bir kez eklenir
            updateSystem.UpdateAt<SignRenderSystem>(SystemUpdatePhase.Rendering);
            updateSystem.UpdateAt<SignsPlusUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<SignToolbarSystem>(SystemUpdatePhase.MainLoop);
            // Serbest yerleştirme: araçlar güncellendikten sonra, oyunun çakışma denetiminden (ModificationEnd) önce
            updateSystem.UpdateAt<SignPlacementSystem>(SystemUpdatePhase.Modification1);
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
