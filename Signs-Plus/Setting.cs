using Colossal.IO.AssetDatabase;
using Game.Input;
using Game.Modding;
using Game.Settings;
using SignsPlus.Data;

namespace SignsPlus
{
    [FileLocation(nameof(SignsPlus))]
    [SettingsUIGroupOrder(kSignGroup, kKeyGroup, kDebugGroup)]
    [SettingsUIShowGroupName(kSignGroup, kKeyGroup, kDebugGroup)]
    [SettingsUIKeyboardAction(kToggleAction, ActionType.Button, usages: new[] { Usages.kDefaultUsage }, interactions: new[] { "UIButton" })]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";
        public const string kSignGroup = "Sign";
        public const string kKeyGroup = "Keys";
        public const string kDebugGroup = "Debug";
        public const string kToggleAction = "SignsPlus.ToggleEditor";

        public enum ShaderMode { Auto, Lit, Unlit }
        public enum TabMode { OwnTab, ExistingTab }

        // ModSetting'in kurucusu SetDefaults'u çağırmaz (Transport+ 0.1'deki hata); burada çağrılır.
        public Setting(IMod mod) : base(mod) { SetDefaults(); }

        [SettingsUISection(kSection, kSignGroup)]
        public bool Enabled { get; set; }

        /// <summary>Henüz düzenlenmemiş levhaların zemin rengi.</summary>
        [SettingsUISection(kSection, kSignGroup)]
        public SignCategory DefaultCategory { get; set; }

        /// <summary>
        /// Levhaların araç çubuğundaki yeri: Yollar menüsünde kendi Signs+ sekmesi ya da menünün mevcut bir sekmesi
        /// (yol hizmetleri). Değişiklik oyun yeniden başlatılınca geçerli olur.
        /// </summary>
        [SettingsUISection(kSection, kSignGroup)]
        public TabMode ToolbarTab { get; set; }

        /// <summary>
        /// Açıkken Signs+ levhaları yolların, kaldırımların ve diğer nesnelerin üstüne hata vermeden konur; yol yapılırken
        /// de levhalar engel olmaz ve silinmez. Yalnızca Signs+ levhalarını etkiler (Anarchy'nin genel ayarlarına dokunmaz).
        /// </summary>
        [SettingsUISection(kSection, kSignGroup)]
        public bool FreePlacement { get; set; }

        /// <summary>Editörü açıp kapatan tuş. Varsayılan: Shift+L.</summary>
        [SettingsUISection(kSection, kKeyGroup)]
        [SettingsUIKeyboardBinding(BindingKeyboard.L, kToggleAction, shift: true)]
        public ProxyBinding ToggleEditorBinding { get; set; }

        [SettingsUISection(kSection, kKeyGroup)]
        public bool ResetBindings
        {
            set { ResetKeyBindings(); }
        }

        /// <summary>Levha yüzü ters tarafta ya da ayna görüntüsü çıkarsa açılır (asset dışa aktarım ekseni farkı).</summary>
        [SettingsUISection(kSection, kDebugGroup)]
        public bool FlipFace { get; set; }

        /// <summary>Paneller sağ-sol yer değiştirmiş çıkarsa açılır.</summary>
        [SettingsUISection(kSection, kDebugGroup)]
        public bool MirrorX { get; set; }

        /// <summary>Yazı katmanının panel yüzünden uzaklığı (mm). Yazı titriyorsa artırılır.</summary>
        [SettingsUISection(kSection, kDebugGroup)]
        [SettingsUISlider(min = 1, max = 100, step = 1)]
        public int FaceOffsetMm { get; set; }

        /// <summary>Metre başına piksel. Yazı bulanıksa artırılır (bellek kullanımı da artar).</summary>
        [SettingsUISection(kSection, kDebugGroup)]
        [SettingsUISlider(min = 64, max = 384, step = 32)]
        public int PixelsPerMeter { get; set; }

        [SettingsUISection(kSection, kDebugGroup)]
        public ShaderMode Shader { get; set; }

        /// <summary>Şehirdeki adında "sign", "levha" vb. geçen nesne prefablarının adlarını günlüğe yazar.</summary>
        [SettingsUISection(kSection, kDebugGroup)]
        public bool LogPrefabNames { get; set; }

        [SettingsUIHidden]
        public int SettingsVersion { get; set; }

        public override void SetDefaults()
        {
            Enabled = true;
            DefaultCategory = SignCategory.Normal;
            ToolbarTab = TabMode.OwnTab;
            FreePlacement = true;
            FlipFace = false;
            MirrorX = false;
            FaceOffsetMm = 8;
            PixelsPerMeter = 160;
            Shader = ShaderMode.Auto;
            LogPrefabNames = true;
            SettingsVersion = 2;
        }
    }
}
