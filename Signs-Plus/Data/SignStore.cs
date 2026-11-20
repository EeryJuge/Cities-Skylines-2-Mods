using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Mathematics;

namespace SignsPlus.Data
{
    /// <summary>Bir levha yüzünün içeriği: zemin kategorisi + prototip yazı biçimi (satırlar | ile ayrılır).</summary>
    public struct FaceContent
    {
        public SignCategory Category;
        public string Spec;
    }

    /// <summary>
    /// Levha yazılarının deposu. 0.0.2'de şehir başına bir metin dosyasında tutulur
    /// (LocalLow\Colossal Order\Cities Skylines II\ModsData\SignsPlus\&lt;şehir&gt;.txt). Kayıt dosyasına bileşen
    /// eklenmez; mod kaldırılınca şehir dosyası etkilenmez.
    ///
    /// Anahtar: asset adı + konum (5 cm ızgara). Levha taşınırsa yazısı kaybolur (bilinen sınır).
    /// </summary>
    public sealed class SignStore
    {
        private const string kHeader = "SPS1";
        private readonly Dictionary<string, Dictionary<string, FaceContent>> m_Signs = new Dictionary<string, Dictionary<string, FaceContent>>();
        private string m_Path;
        private bool m_Dirty;
        private float m_SaveAt = -1f;

        /// <summary>Her değişiklikte artar; çizim sistemi buna bakıp yüzleri yeniler.</summary>
        public int Version { get; private set; }
        public string CityName { get; private set; }

        public static string Folder => Path.Combine(UnityEngine.Application.persistentDataPath, "ModsData", "SignsPlus");

        public static string MakeKey(string asset, float3 position)
        {
            int x = (int)math.round(position.x * 20f), y = (int)math.round(position.y * 20f), z = (int)math.round(position.z * 20f);
            return asset + "@" + x.ToString(CultureInfo.InvariantCulture) + "," + y.ToString(CultureInfo.InvariantCulture) + "," + z.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Şehir değişince eski şehrin bekleyen değişiklikleri kaydedilir, yenisi okunur.</summary>
        public void Open(string cityName)
        {
            if (string.IsNullOrEmpty(cityName)) cityName = "_editor";
            if (cityName == CityName && m_Path != null) return;
            Flush();
            CityName = cityName;
            m_Signs.Clear();
            m_Path = Path.Combine(Folder, Sanitize(cityName) + ".txt");
            try
            {
                if (File.Exists(m_Path))
                {
                    var lines = File.ReadAllLines(m_Path, Encoding.UTF8);
                    for (int i = 1; i < lines.Length; i++)
                    {
                        var p = lines[i].Split('\t');
                        if (p.Length < 4) continue;
                        int cat;
                        if (!int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out cat)) continue;
                        Set(p[0], p[1], new FaceContent { Category = (SignCategory)cat, Spec = p[3] }, false);
                    }
                }
                Mod.log.Info($"Signs+ levha yazıları: \"{cityName}\" için {m_Signs.Count} levha okundu ({m_Path})");
            }
            catch (Exception e)
            {
                Mod.log.Warn(e, $"Signs+ levha yazıları okunamadı: {m_Path}");
            }
            Version++;
        }

        public bool TryGet(string key, string face, out FaceContent content)
        {
            content = default;
            return m_Signs.TryGetValue(key, out var faces) && faces.TryGetValue(face, out content);
        }

        public void Set(string key, string face, FaceContent content, bool save = true)
        {
            content.Spec = Clean(content.Spec);
            if (!m_Signs.TryGetValue(key, out var faces)) m_Signs[key] = faces = new Dictionary<string, FaceContent>();
            faces[face] = content;
            Version++;
            if (save) { m_Dirty = true; m_SaveAt = UnityEngine.Time.realtimeSinceStartup + 1.5f; }
        }

        /// <summary>Her karede çağrılır; değişiklikten 1,5 sn sonra diske yazar (yazarken her tuşta kaydetmemek için).</summary>
        public void Tick()
        {
            if (m_Dirty && m_SaveAt > 0f && UnityEngine.Time.realtimeSinceStartup >= m_SaveAt) Flush();
        }

        public void Flush()
        {
            if (!m_Dirty || m_Path == null) return;
            m_Dirty = false;
            m_SaveAt = -1f;
            try
            {
                Directory.CreateDirectory(Folder);
                var sb = new StringBuilder();
                sb.Append(kHeader).Append('\n');
                foreach (var kv in m_Signs)
                    foreach (var f in kv.Value)
                        sb.Append(kv.Key).Append('\t').Append(f.Key).Append('\t')
                          .Append(((int)f.Value.Category).ToString(CultureInfo.InvariantCulture)).Append('\t')
                          .Append(f.Value.Spec).Append('\n');
                string tmp = m_Path + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(m_Path)) File.Delete(m_Path);
                File.Move(tmp, m_Path);
            }
            catch (Exception e)
            {
                Mod.log.Warn(e, $"Signs+ levha yazıları kaydedilemedi: {m_Path}");
            }
        }

        private static string Clean(string s) => string.IsNullOrEmpty(s) ? string.Empty : s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

        private static string Sanitize(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s) sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return sb.ToString().Trim();
        }
    }
}
