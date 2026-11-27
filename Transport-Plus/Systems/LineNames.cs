using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TransportPlus.Systems
{
    /// <summary>
    /// Hat adından hat kodu ve iki uç durak: "93B Topkapı Hacıosman" → kod 93B, uçlar Topkapı ve Hacıosman.
    ///
    /// Kurallar:
    /// - İlk kelimede rakam varsa (ve en çok 6 harfse) o kelime hat kodudur: 93B, M2, 500T, E-5 değil (E5 olur).
    /// - Kalan ad bir ayraçla bölünmüşse ("Topkapı - Hacıosman", "–", "—", "/", ">", "→", "↔") ayraçtan bölünür;
    ///   böylece çok kelimeli uçlar yazılabilir: "15F Kadıköy - Beykoz Kavağı".
    /// - Ayraç yoksa: iki kelime iki uçtur; daha fazlaysa ortadan bölünür; tek kelimeyse iki yönde de o yazılır.
    /// </summary>
    public static class LineNames
    {
        public struct Parsed
        {
            public string Code;       // boş olabilir
            public string First;      // ad sırasındaki ilk uç (boş olabilir)
            public string Second;     // ikinci uç (boş olabilir)
        }

        private static readonly string[] kSeparators = { " ↔ ", " → ", " > ", " - ", " – ", " — ", " / ", "↔", "→", "–", "—", "/", ">", " -", "- ", "-" };

        public static Parsed Parse(string name)
        {
            var p = new Parsed { Code = "", First = "", Second = "" };
            if (string.IsNullOrWhiteSpace(name)) return p;
            string rest = name.Trim();
            int sp = IndexOfSpace(rest);
            string first = sp < 0 ? rest : rest.Substring(0, sp);
            if (first.Length <= 6 && HasDigit(first))
            {
                p.Code = first;
                rest = sp < 0 ? "" : rest.Substring(sp + 1).Trim();
            }
            if (rest.Length == 0) return p;
            foreach (string sep in kSeparators)
            {
                int i = rest.IndexOf(sep, StringComparison.Ordinal);
                if (i <= 0 || i + sep.Length >= rest.Length) continue;
                p.First = rest.Substring(0, i).Trim();
                p.Second = rest.Substring(i + sep.Length).Trim();
                if (p.First.Length > 0 && p.Second.Length > 0) return p;
            }
            string[] words = rest.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 1) { p.First = p.Second = words[0]; return p; }
            int half = (words.Length + 1) / 2;
            p.First = string.Join(" ", words, 0, half);
            p.Second = string.Join(" ", words, half, words.Length - half);
            return p;
        }

        /// <summary>Tabela için büyük harf; Türkçe şehirde Türkçe kurala göre (ı → I, i → İ).</summary>
        public static string Upper(string s, bool turkish)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return turkish ? s.ToUpper(new CultureInfo("tr-TR")) : s.ToUpperInvariant();
        }

        /// <summary>Durak adı karşılaştırması için: küçük harf, Türkçe harfler sadeleşmiş, harf/rakam dışı atılmış.</summary>
        public static string Key(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c0 in s.ToLowerInvariant())
            {
                char c = c0;
                switch (c)
                {
                    case 'ı': c = 'i'; break;
                    case 'ş': c = 's'; break;
                    case 'ğ': c = 'g'; break;
                    case 'ç': c = 'c'; break;
                    case 'ö': c = 'o'; break;
                    case 'ü': c = 'u'; break;
                    case 'â': c = 'a'; break;
                    case 'î': c = 'i'; break;
                }
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            }
            return sb.ToString();
        }

        private static bool HasDigit(string s)
        {
            foreach (char c in s) if (char.IsDigit(c)) return true;
            return false;
        }

        private static int IndexOfSpace(string s)
        {
            for (int i = 0; i < s.Length; i++) if (char.IsWhiteSpace(s[i])) return i;
            return -1;
        }
    }
}
