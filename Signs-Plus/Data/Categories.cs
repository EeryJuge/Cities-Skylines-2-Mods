using UnityEngine;

namespace SignsPlus.Data
{
    /// <summary>Türkiye paketi levha kategorileri. Kategori bir stildir: levhanın tamamına ya da içindeki bloğa uygulanır.</summary>
    public enum SignCategory
    {
        Otoyol,     // yeşil zemin, beyaz yazı
        Karayolu,   // mavi zemin, beyaz yazı
        Normal,     // beyaz zemin, siyah yazı
        Turistik,   // kahverengi zemin, beyaz yazı
        SinirDisi,  // sarı zemin, siyah yazı
        KGM         // turuncu zemin, siyah yazı
    }

    public struct CategoryStyle
    {
        public Color32 Background;
        public Color32 Text;
        public Color32 Border;
    }

    public static class Categories
    {
        // Geçici renkler (RAL trafik renklerine yakın). Kesin değerler KGM standardından alınacak.
        private static readonly Color32 kWhite = new Color32(242, 242, 236, 255);
        private static readonly Color32 kBlack = new Color32(28, 28, 28, 255);

        public static CategoryStyle Get(SignCategory c)
        {
            switch (c)
            {
                case SignCategory.Otoyol: return new CategoryStyle { Background = new Color32(0, 122, 77, 255), Text = kWhite, Border = kWhite };
                case SignCategory.Karayolu: return new CategoryStyle { Background = new Color32(14, 82, 160, 255), Text = kWhite, Border = kWhite };
                case SignCategory.Turistik: return new CategoryStyle { Background = new Color32(110, 52, 40, 255), Text = kWhite, Border = kWhite };
                case SignCategory.SinirDisi: return new CategoryStyle { Background = new Color32(246, 190, 0, 255), Text = kBlack, Border = kBlack };
                case SignCategory.KGM: return new CategoryStyle { Background = new Color32(232, 92, 40, 255), Text = kBlack, Border = kBlack };
                default: return new CategoryStyle { Background = kWhite, Text = kBlack, Border = kBlack };
            }
        }

        /// <summary>Prototip yazısındaki [etiket] → kategori. Türkçe karakterli ve karaktersiz yazımlar kabul edilir.</summary>
        public static bool TryParse(string tag, out SignCategory c)
        {
            switch (tag.Trim().ToLowerInvariant())
            {
                case "otoyol": case "yesil": case "yeşil": c = SignCategory.Otoyol; return true;
                case "karayolu": case "mavi": c = SignCategory.Karayolu; return true;
                case "normal": case "beyaz": c = SignCategory.Normal; return true;
                case "turistik": case "kahverengi": c = SignCategory.Turistik; return true;
                case "sinirdisi": case "sınırdışı": case "sari": case "sarı": c = SignCategory.SinirDisi; return true;
                case "kgm": case "turuncu": c = SignCategory.KGM; return true;
            }
            c = SignCategory.Normal;
            return false;
        }
    }
}
