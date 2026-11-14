// TF2YolAraci - giriş noktası. Oyun mod yükleyicisi IMod'u bulup OnLoad'u çağırır.
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using TF2YolAraci.Systems;

namespace TF2YolAraci
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(TF2YolAraci)}.{nameof(Mod)}").SetShowsErrorsInUI(false);
        public static Setting Settings { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            Settings = new Setting(this);
            Settings.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Settings));
            AssetDatabase.global.LoadSettings(nameof(TF2YolAraci), Settings, new Setting(this));

            // Araç, ToolUpdate aşamasında çalışır. Tuş dinleyici her karede çalışan UIUpdate aşamasında.
            updateSystem.UpdateAt<TF2RoadToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<ToolActivatorSystem>(SystemUpdatePhase.UIUpdate);

            // Oyun yolu böldükten sonra, kenarları oluşturmadan önce yükseklikleri TF2 profiline göre yeniden yazar.
            updateSystem.UpdateBefore<HeightOverrideSystem, Game.Tools.GenerateNodesSystem>(SystemUpdatePhase.Modification1);

            // Araç açıkken ekranda gösterilen bilgi paneli
            UI.TF2Hud.Create();
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
