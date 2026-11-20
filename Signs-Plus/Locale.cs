using System.Collections.Generic;
using Colossal;
using SignsPlus.Data;

namespace SignsPlus
{
    internal static class UiStrings
    {
        // Editör penceresinin metinleri. Arayüz bunları "SignsPlus.UI.<anahtar>" olarak okur.
        public static readonly Dictionary<string, string> EN = new Dictionary<string, string>
        {
            { "Title", "Signs+" }, { "NoSign", "No sign selected" },
            { "NoSignHint", "Click a Signs+ sign in the city, or hover the mouse over one and press Shift+L." },
            { "PickCenter", "Pick the sign in the middle of the screen" },
            { "Face", "Panel" }, { "FaceLeft", "left" }, { "FaceRight", "right" },
            { "Background", "Sign color" }, { "Lines", "Lines" }, { "AddLine", "+ Add line" },
            { "LinePlaceholder", "Place name" }, { "Block", "Block" }, { "NoBlock", "No block" },
            { "Arrow", "Arrow" }, { "NoArrow", "None" },
            { "CopyToOther", "Copy to the other panel" }, { "Clear", "Clear" },
            { "MoveUp", "Move up" }, { "MoveDown", "Move down" }, { "Delete", "Delete" },
            { "Saved", "Changes show on the sign right away and are saved for this city." },
            { "Cat0", "Motorway" }, { "Cat1", "Highway" }, { "Cat2", "Normal" }, { "Cat3", "Tourist" }, { "Cat4", "Abroad" }, { "Cat5", "KGM" },
            { "BigArrow", "Large arrow" }, { "Align", "Alignment (click to change)" }, { "AlignAll", "Align all" }, { "Templates", "Templates" }, { "Undo", "Undo" },
            { "Tpl.Motorway", "Motorway direction sign" }, { "Tpl.Junction", "Junction advance sign" }, { "Tpl.Distance", "Distance sign" },
            { "Tpl.City", "City direction sign" }, { "Tpl.Tourist", "Tourist sign" }, { "Tpl.Border", "Border crossing" },
            { "Size", "Text size (click to change)" }, { "RightPlaceholder", "25 km" }, { "RightHint", "Right-aligned text, e.g. distance" },
            { "AddItem", "+ Symbol / number / arrow" }, { "SlotPre", "start" }, { "SlotPost", "after" }, { "SlotRight", "right" },
            { "SlotPreLong", "Before the text" }, { "SlotPostLong", "After the text" }, { "SlotRightLong", "Right edge" },
            { "Boxed", "In a box" }, { "TabSymbol", "Symbol" }, { "TabNumber", "Number box" }, { "TabArrow", "Arrow" }, { "Add", "Add" },
            { "NumberHint", "O and K: orange, E: green, D: white." },
            { "Sym.ucak", "Airport" },
            { "Sym.kopru", "Bridge" },
            { "Sym.feribot", "Ferry" },
            { "Sym.liman", "Port" },
            { "Sym.tren", "Train station" },
            { "Sym.otobus", "Bus terminal" },
            { "Sym.araba", "Car" },
            { "Sym.kamyon", "Truck" },
            { "Sym.otoyol", "Motorway" },
            { "Sym.cikis", "Exit" },
            { "Sym.tunel", "Tunnel" },
            { "Sym.otopark", "Parking" },
            { "Sym.benzin", "Fuel" },
            { "Sym.dinlenme", "Rest area" },
            { "Sym.merkez", "City centre" },
            { "Sym.hastane", "Hospital" },
            { "Sym.ilkyardim", "First aid" },
            { "Sym.sanayi", "Industry" },
            { "Sym.stadyum", "Stadium" },
            { "Sym.cami", "Mosque" },
            { "Sym.plaj", "Beach" },
            { "Sym.yaris", "Race track" },
            { "Sym.kamera", "Camera" },
            { "Asset.SignsPlusYerLevhasi", "Ground sign" }, { "Asset.SignsPlusLBayrak", "L cantilever" },
            { "Asset.SignsPlusTDirek", "T pole" }, { "Asset.SignsPlusGantry", "Gantry" }, { "Asset.SignsPlusIsaretDirek", "Traffic sign" },
            { "StdSign", "Sign" }, { "StdValue", "Value" }, { "StdValueHint", "e.g. 30, 50, 70, 90, 120" }, { "Plate", "Additional plate" },
            { "PlateHint", "Text under the sign, e.g. 200 m. Empty: no plate." }, { "PlateLine1", "Line 1" }, { "PlateLine2", "Line 2" },
            { "GroupWarn", "Warning" }, { "GroupReg", "Regulatory" }, { "GroupInfo", "Information" },
        };

        public static readonly Dictionary<string, string> TR = new Dictionary<string, string>
        {
            { "Title", "Signs+" }, { "NoSign", "Levha seçili değil" },
            { "NoSignHint", "Şehirde bir Signs+ levhasına tıkla ya da imleci bir levhanın üstüne getirip Shift+L'ye bas." },
            { "PickCenter", "Ekranın ortasındaki levhayı seç" },
            { "Face", "Panel" }, { "FaceLeft", "sol" }, { "FaceRight", "sağ" },
            { "Background", "Levha rengi" }, { "Lines", "Satırlar" }, { "AddLine", "+ Satır ekle" },
            { "LinePlaceholder", "Yer adı" }, { "Block", "Blok" }, { "NoBlock", "Blok yok" },
            { "Arrow", "Ok" }, { "NoArrow", "Yok" },
            { "CopyToOther", "Diğer panele kopyala" }, { "Clear", "Temizle" },
            { "MoveUp", "Yukarı taşı" }, { "MoveDown", "Aşağı taşı" }, { "Delete", "Sil" },
            { "Saved", "Değişiklikler levhada hemen görünür ve bu şehir için kaydedilir." },
            { "Cat0", "Otoyol" }, { "Cat1", "Karayolu" }, { "Cat2", "Normal" }, { "Cat3", "Turistik" }, { "Cat4", "Sınır dışı" }, { "Cat5", "KGM" },
            { "BigArrow", "Büyük ok" }, { "Align", "Hiza (değiştirmek için tıkla)" }, { "AlignAll", "Hepsini hizala" }, { "Templates", "Şablonlar" }, { "Undo", "Geri al" },
            { "Tpl.Motorway", "Otoyol yön levhası" }, { "Tpl.Junction", "Kavşak ön bilgi" }, { "Tpl.Distance", "Mesafe levhası" },
            { "Tpl.City", "Şehir içi yön" }, { "Tpl.Tourist", "Turistik yer" }, { "Tpl.Border", "Sınır kapısı" },
            { "Size", "Yazı boyu (değiştirmek için tıkla)" }, { "RightPlaceholder", "25 km" }, { "RightHint", "Sağa yaslı yazı, ör. mesafe" },
            { "AddItem", "+ Sembol / numara / ok" }, { "SlotPre", "başta" }, { "SlotPost", "sonda" }, { "SlotRight", "sağda" },
            { "SlotPreLong", "Yazının önünde" }, { "SlotPostLong", "Yazının arkasında" }, { "SlotRightLong", "Sağ kenarda" },
            { "Boxed", "Kutu içinde" }, { "TabSymbol", "Sembol" }, { "TabNumber", "Numara kutusu" }, { "TabArrow", "Ok" }, { "Add", "Ekle" },
            { "NumberHint", "O ve K turuncu, E yeşil, D beyaz." },
            { "Sym.ucak", "Havalimanı" },
            { "Sym.kopru", "Köprü" },
            { "Sym.feribot", "Feribot" },
            { "Sym.liman", "Liman" },
            { "Sym.tren", "Tren / gar" },
            { "Sym.otobus", "Otobüs terminali" },
            { "Sym.araba", "Araba" },
            { "Sym.kamyon", "Kamyon" },
            { "Sym.otoyol", "Otoyol" },
            { "Sym.cikis", "Çıkış" },
            { "Sym.tunel", "Tünel" },
            { "Sym.otopark", "Otopark" },
            { "Sym.benzin", "Akaryakıt" },
            { "Sym.dinlenme", "Dinlenme tesisi" },
            { "Sym.merkez", "Şehir merkezi" },
            { "Sym.hastane", "Hastane" },
            { "Sym.ilkyardim", "İlk yardım" },
            { "Sym.sanayi", "Sanayi" },
            { "Sym.stadyum", "Stadyum" },
            { "Sym.cami", "Cami" },
            { "Sym.plaj", "Plaj" },
            { "Sym.yaris", "Yarış pisti" },
            { "Sym.kamera", "Kamera" },
            { "Asset.SignsPlusYerLevhasi", "Yer levhası" }, { "Asset.SignsPlusLBayrak", "L bayrak direk" },
            { "Asset.SignsPlusTDirek", "T direk" }, { "Asset.SignsPlusGantry", "Gantry" }, { "Asset.SignsPlusIsaretDirek", "Trafik işareti" },
            { "StdSign", "İşaret" }, { "StdValue", "Değer" }, { "StdValueHint", "ör. 30, 50, 70, 90, 120" }, { "Plate", "Ek levha" },
            { "PlateHint", "İşaretin altındaki yazı, ör. 200 m. Boşsa ek levha yok." }, { "PlateLine1", "1. satır" }, { "PlateLine2", "2. satır" },
            { "GroupWarn", "Tehlike uyarı" }, { "GroupReg", "Tanzim" }, { "GroupInfo", "Bilgi" },
        };

        // Araç çubuğundaki levha adları ve açıklamaları (prefab adı oyunda belli olunca eklenir)
        public static readonly Dictionary<string, string[]> AssetNamesEN = new Dictionary<string, string[]>
        {
            { "SignsPlusYerLevhasi", new[] { "Ground sign (3 × 2 m)", "Direction sign on two posts. Click it to edit the text." } },
            { "SignsPlusLBayrak", new[] { "L cantilever sign (4 × 2.5 m)", "Single arm over the road. Click it to edit the text." } },
            { "SignsPlusTDirek", new[] { "T pole sign (2 × 3.5 × 2.5 m)", "Two panels on a T pole. Click it to edit the text." } },
            { "SignsPlusGantry", new[] { "Gantry (16 m)", "Portal gantry with a 6 m and a 4 m panel. Click it to edit the text." } },
            { "SignsPlusIsaretDirek", new[] { "Traffic sign", "Standard traffic sign on a pole, with an optional additional plate. Click it to choose the sign." } },
        };

        public static readonly Dictionary<string, string[]> AssetNamesTR = new Dictionary<string, string[]>
        {
            { "SignsPlusYerLevhasi", new[] { "Yer levhası (3 × 2 m)", "İki direkli yön levhası. Yazısını düzenlemek için üstüne tıkla." } },
            { "SignsPlusLBayrak", new[] { "L bayrak direk (4 × 2,5 m)", "Yolun üstüne uzanan tek kollu levha. Yazısını düzenlemek için üstüne tıkla." } },
            { "SignsPlusTDirek", new[] { "T direk (2 × 3,5 × 2,5 m)", "T direkte iki levha. Yazısını düzenlemek için üstüne tıkla." } },
            { "SignsPlusGantry", new[] { "Gantry (16 m)", "6 m ve 4 m panelli portal gantry. Yazısını düzenlemek için üstüne tıkla." } },
            { "SignsPlusIsaretDirek", new[] { "Trafik işareti", "Direkte standart trafik işareti, isteğe bağlı ek levhayla. İşareti seçmek için üstüne tıkla." } },
        };
    }

    /// <summary>Araç çubuğundaki Signs+ sekmesi ve levha adları; prefab adları oyunda bulununca doldurulur.</summary>
    public class AssetNameSource : IDictionarySource
    {
        private readonly Dictionary<string, string> m_Entries = new Dictionary<string, string>();
        public void Add(string key, string value) => m_Entries[key] = value;
        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts) => m_Entries;
        public void Unload() { }
    }

    public class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;
        public LocaleEN(Setting setting) { m_Setting = setting; }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            var d = new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Signs+ [ALPHA]" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "Main" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kSignGroup), "Signs" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kKeyGroup), "Key bindings" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kDebugGroup), "Testing" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Enabled)), "Show sign faces" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Enabled)), "Draws the sign text on every Signs+ sign in the city." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.DefaultCategory)), "Color of new signs" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.DefaultCategory)), "Background color of signs that have not been edited yet." },
                { m_Setting.GetEnumValueLocaleID(SignCategory.Otoyol), "Motorway (green)" },
                { m_Setting.GetEnumValueLocaleID(SignCategory.Karayolu), "Highway (blue)" },
                { m_Setting.GetEnumValueLocaleID(SignCategory.Normal), "Normal (white)" },
                { m_Setting.GetEnumValueLocaleID(SignCategory.Turistik), "Tourist (brown)" },
                { m_Setting.GetEnumValueLocaleID(SignCategory.SinirDisi), "Abroad (yellow)" },
                { m_Setting.GetEnumValueLocaleID(SignCategory.KGM), "KGM (orange)" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ToolbarTab)), "Where the signs are in the toolbar" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ToolbarTab)), "Own tab: a Signs+ tab in the Roads menu. Existing tab: the signs go into the Roads menu's road services tab. Restart the game after changing this." },
                { m_Setting.GetEnumValueLocaleID(Setting.TabMode.OwnTab), "Own Signs+ tab" },
                { m_Setting.GetEnumValueLocaleID(Setting.TabMode.ExistingTab), "Road services tab" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ToggleEditorBinding)), "Open / close the sign editor" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ToggleEditorBinding)), "Opens the editor for the selected sign, or for the sign under the mouse." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ResetBindings)), "Reset key bindings" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ResetBindings)), "Sets the shortcut back to Shift+L." },
                { m_Setting.GetBindingKeyLocaleID(Setting.kToggleAction), "Open / close the sign editor" },
                { m_Setting.GetBindingMapLocaleID(), "Signs+" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.FlipFace)), "Flip face to the other side" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.FlipFace)), "Turn on if the text appears on the back of the panel or hidden inside it." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.MirrorX)), "Swap left and right" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.MirrorX)), "Turn on if the text of the left panel appears on the right panel." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.FaceOffsetMm)), "Text distance from panel (mm)" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.FaceOffsetMm)), "Increase if the text flickers." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.PixelsPerMeter)), "Text sharpness (pixels per meter)" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.PixelsPerMeter)), "Higher is sharper and uses more memory." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Shader)), "Shader" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Shader)), "Auto tries lit first. Use Unlit if the text is black or invisible." },
                { m_Setting.GetEnumValueLocaleID(Setting.ShaderMode.Auto), "Auto" },
                { m_Setting.GetEnumValueLocaleID(Setting.ShaderMode.Lit), "Lit" },
                { m_Setting.GetEnumValueLocaleID(Setting.ShaderMode.Unlit), "Unlit" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.FreePlacement)), "Place signs freely on roads" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.FreePlacement)), "Signs+ signs can be placed on roads, sidewalks and other objects without an overlap error, roads can be built past them, and they are never removed by roads. Only Signs+ signs are affected; it works the same with or without Anarchy." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.LogPrefabNames)), "Write asset names to the log" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.LogPrefabNames)), "Writes the names of placed sign-like assets to SignsPlus.Mod.log." },
                { "SubServices.NAME[SignsPlusCategory]", "Signs+" },
                { "Assets.NAME[SignsPlusCategory]", "Signs+" },
                { "Assets.SUB_SERVICE_DESCRIPTION[SignsPlusCategory]", "Road signs with editable text." },
            };
            foreach (var kv in UiStrings.EN) d["SignsPlus.UI." + kv.Key] = kv.Value;
            foreach (var kv in StdSignNames.EN) d["SignsPlus.UI." + kv.Key] = kv.Value;
            return d;
        }

        public void Unload() { }
    }

    public class LocaleTR : IDictionarySource
    {
        private readonly Setting m_Setting;
        public LocaleTR(Setting setting) { m_Setting = setting; }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            var d = new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Signs+ [ALPHA]" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "Genel" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kSignGroup), "Levhalar" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kKeyGroup), "Kısayol tuşları" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kDebugGroup), "Deneme" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Enabled)), "Levha yüzlerini göster" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Enabled)), "Şehirdeki her Signs+ levhasının paneline yazısını çizer." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.DefaultCategory)), "Yeni levhaların rengi" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.DefaultCategory)), "Henüz düzenlenmemiş levhaların zemin rengi." },
                { m_Setting.GetEnumValueLocaleID(SignCategory.Otoyol), "Otoyol (yeşil)" },
                { m_Setting.GetEnumValueLocaleID(SignCategory.Karayolu), "Karayolu (mavi)" },
                { m_Setting.GetEnumValueLocaleID(SignCategory.Normal), "Normal (beyaz)" },
                { m_Setting.GetEnumValueLocaleID(SignCategory.Turistik), "Turistik (kahverengi)" },
                { m_Setting.GetEnumValueLocaleID(SignCategory.SinirDisi), "Sınır dışı (sarı)" },
                { m_Setting.GetEnumValueLocaleID(SignCategory.KGM), "KGM (turuncu)" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ToolbarTab)), "Levhaların araç çubuğundaki yeri" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ToolbarTab)), "Kendi sekmesi: Yollar menüsünde Signs+ sekmesi. Mevcut sekme: levhalar Yollar menüsünün yol hizmetleri sekmesine konur. Değiştirdikten sonra oyunu yeniden başlat." },
                { m_Setting.GetEnumValueLocaleID(Setting.TabMode.OwnTab), "Kendi Signs+ sekmesi" },
                { m_Setting.GetEnumValueLocaleID(Setting.TabMode.ExistingTab), "Yol hizmetleri sekmesi" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ToggleEditorBinding)), "Levha editörünü aç / kapat" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ToggleEditorBinding)), "Seçili levhanın, yoksa imlecin altındaki levhanın editörünü açar." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ResetBindings)), "Kısayolları sıfırla" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ResetBindings)), "Kısayolu tekrar Shift+L yapar." },
                { m_Setting.GetBindingKeyLocaleID(Setting.kToggleAction), "Levha editörünü aç / kapat" },
                { m_Setting.GetBindingMapLocaleID(), "Signs+" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.FlipFace)), "Yüzü diğer tarafa çevir" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.FlipFace)), "Yazı panelin arkasında çıkıyor ya da panelin içinde kayboluyorsa aç." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.MirrorX)), "Sağ ve solu değiştir" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.MirrorX)), "Sol panelin yazısı sağ panelde çıkıyorsa aç." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.FaceOffsetMm)), "Yazının panelden uzaklığı (mm)" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.FaceOffsetMm)), "Yazı titriyorsa artır." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.PixelsPerMeter)), "Yazı keskinliği (metre başına piksel)" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.PixelsPerMeter)), "Yüksek değer daha keskin, ama daha çok bellek kullanır." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Shader)), "Gölgelendirici" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Shader)), "Otomatik önce ışıklı olanı dener. Yazı siyah ya da görünmezse Işıksız'ı seç." },
                { m_Setting.GetEnumValueLocaleID(Setting.ShaderMode.Auto), "Otomatik" },
                { m_Setting.GetEnumValueLocaleID(Setting.ShaderMode.Lit), "Işıklı" },
                { m_Setting.GetEnumValueLocaleID(Setting.ShaderMode.Unlit), "Işıksız" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.FreePlacement)), "Levhalar yollara serbestçe konsun" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.FreePlacement)), "Signs+ levhaları yolların, kaldırımların ve diğer nesnelerin üstüne üst üste binme hatası olmadan konur; yol yapılırken levhalar engel olmaz ve yol onları silmez. Yalnızca Signs+ levhalarını etkiler; Anarchy açık da kapalı da olsa aynı çalışır." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.LogPrefabNames)), "Asset adlarını günlüğe yaz" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.LogPrefabNames)), "Yerleştirilen levha benzeri assetlerin adlarını SignsPlus.Mod.log'a yazar." },
                { "SubServices.NAME[SignsPlusCategory]", "Signs+" },
                { "Assets.NAME[SignsPlusCategory]", "Signs+" },
                { "Assets.SUB_SERVICE_DESCRIPTION[SignsPlusCategory]", "Yazısı düzenlenebilen yol levhaları." },
            };
            foreach (var kv in UiStrings.TR) d["SignsPlus.UI." + kv.Key] = kv.Value;
            foreach (var kv in StdSignNames.TR) d["SignsPlus.UI." + kv.Key] = kv.Value;
            return d;
        }

        public void Unload() { }
    }
}
