using Colossal.IO.AssetDatabase;
using Game.Input;
using Game.Modding;
using Game.Settings;
using Game.UI;

namespace TransportPlus
{
    [FileLocation(nameof(TransportPlus))]
    [SettingsUIGroupOrder(kMapGroup, kKeyGroup)]
    [SettingsUIShowGroupName(kMapGroup, kKeyGroup)]
    [SettingsUIKeyboardAction(kToggleAction, ActionType.Button, usages: new[] { Usages.kDefaultUsage }, interactions: new[] { "UIButton" })]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";
        public const string kMapGroup = "Map";
        public const string kKeyGroup = "Keys";
        public const string kToggleAction = "TransportPlus.ToggleMenu";

        // ModSetting'in kurucusu SetDefaults'u çağırmaz. Çağrılmazsa modu ilk kez kuran herkeste
        // birleştirme mesafesi 0, aktarma mesafesi 0 ve vapurlar kapalı gelir (0.1'deki hata).
        public Setting(IMod mod) : base(mod) { SetDefaults(); }

        /// <summary>Bu mesafeden yakın duraklar haritada tek istasyon olur (metre).</summary>
        [SettingsUISection(kSection, kMapGroup)]
        [SettingsUISlider(min = 0, max = 300, step = 10, unit = Unit.kInteger)]
        public int MergeDistance { get; set; }

        /// <summary>Farklı hatlardaki iki istasyon bu mesafeden yakınsa aralarına aktarma bağlantısı çizilir (metre).</summary>
        [SettingsUISection(kSection, kMapGroup)]
        [SettingsUISlider(min = 0, max = 600, step = 25, unit = Unit.kInteger)]
        public int TransferDistance { get; set; }

        [SettingsUISection(kSection, kMapGroup)]
        public bool IncludeFerries { get; set; }

        /// <summary>Otobüs hatları ağ haritasında baştan açık mı gelsin. Kapalıyken de lejanttan açılabilir.</summary>
        [SettingsUISection(kSection, kMapGroup)]
        public bool ShowBusesOnMap { get; set; }

        /// <summary>Şehirde göster açıkken şehri beyaza boyayan bilgi görünümü. Kapalıyken rotalar normal şehrin üstüne çizilir.</summary>
        [SettingsUISection(kSection, kMapGroup)]
        public bool WhiteCity { get; set; }

        /// <summary>Otobüslerde LED hat tabelası (önde kod + yön, arkada kod, yanda kod + yön).</summary>
        [SettingsUISection(kSection, kMapGroup)]
        public bool LedSigns { get; set; }

        /// <summary>LED tabelaların çizildiği en uzak kamera mesafesi (m).</summary>
        [SettingsUIHidden] public int LedSignDistance { get; set; }

        /// <summary>İstasyon adları: 0 = otomatik (uzaktan yalnızca aktarma istasyonları), 1 = hep, 2 = kapalı. Harita penceresinden değişir.</summary>
        [SettingsUIHidden]
        public int StationNames { get; set; }

        /// <summary>Ayar dosyasının sürümü. 0.1'in kaydettiği sıfır değerleri bir kez düzeltmek için (bkz. Mod.Migrate).</summary>
        [SettingsUIHidden]
        public int SettingsVersion { get; set; }

        // Taksi ve şehirlerarası otobüs ayarları (menünün Taksi sekmesinden değişir)
        [SettingsUIHidden] public bool OutsideTaxis { get; set; }
        [SettingsUIHidden] public bool TaxiTariff { get; set; }
        [SettingsUIHidden] public int TaxiPerKm { get; set; }
        [SettingsUIHidden] public int TaxiNightPercent { get; set; }
        /// <summary>Kapatılan taksi modelleri (prefab adları, | ile ayrılmış).</summary>
        [SettingsUIHidden] public string TaxiModelsOff { get; set; }
        [SettingsUIHidden] public bool CoachMode { get; set; }
        /// <summary>Şehir dışından gelen otobüslerin modelleri (prefab adları).</summary>
        [SettingsUIHidden] public string CoachModels { get; set; }
        /// <summary>Ayrım açıkken modeli seçilmemiş şehir hatlarına yazılacak modeller; boşsa şehirlerarası olmayan hepsi.</summary>
        [SettingsUIHidden] public string CityBusModels { get; set; }

        /// <summary>Transport+ menüsünü açıp kapatan tuş. Varsayılan: Shift+T.</summary>
        [SettingsUISection(kSection, kKeyGroup)]
        [SettingsUIKeyboardBinding(BindingKeyboard.T, kToggleAction, shift: true)]
        public ProxyBinding ToggleMenuBinding { get; set; }

        [SettingsUISection(kSection, kKeyGroup)]
        public bool ResetBindings
        {
            set { ResetKeyBindings(); }
        }

        public override void SetDefaults()
        {
            MergeDistance = 80;
            TransferDistance = 250;
            IncludeFerries = true;
            ShowBusesOnMap = false;
            WhiteCity = true;
            LedSigns = true;
            LedSignDistance = 600;
            StationNames = 0;
            SettingsVersion = 0;
            OutsideTaxis = true;
            TaxiTariff = false;
            TaxiPerKm = 30;
            TaxiNightPercent = 150;
            TaxiModelsOff = "";
            CoachMode = false;
            CoachModels = "";
            CityBusModels = "";
        }
    }
}
