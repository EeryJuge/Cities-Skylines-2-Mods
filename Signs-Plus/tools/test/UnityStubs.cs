// Yalnızca masaüstü testi için: Signs+ çizim kodunun kullandığı Unity türlerinin küçük taklitleri.
using System;
using System.IO;
namespace UnityEngine
{
    public struct Color32 { public byte r, g, b, a; public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; } }
    public struct Vector2 { public float x, y; public static Vector2 operator +(Vector2 a, Vector2 b) => default; public Vector2(float x, float y) { this.x = x; this.y = y; } public static Vector2 operator *(Vector2 v, float f) => new Vector2(v.x * f, v.y * f); }
    public static class Mathf { public const float Deg2Rad = (float)(Math.PI / 180.0); public static float Cos(float a) => (float)Math.Cos(a); public static float Sin(float a) => (float)Math.Sin(a); public static float Max(float a, float b) => Math.Max(a, b); }
    public enum TextureFormat { RGBA32 }
    public enum TextureWrapMode { Clamp }
    public enum FilterMode { Trilinear }
    public class Object { public string name; public static void Destroy(Object o) { } }
    public class Texture2D : Object
    {
        public int width, height; public Color32[] px; public TextureWrapMode wrapMode; public FilterMode filterMode; public int anisoLevel;
        public Texture2D(int w, int h, TextureFormat f, bool mip) { width = w; height = h; px = new Color32[w * h]; }
        public Texture2D(int w, int h, TextureFormat f, bool mip, bool linear) : this(w, h, f, mip) { }
        public void SetPixels32(Color32[] p) { px = p; }
        public Color32[] GetPixels32() => px;
        public void Apply(bool a, bool b) { }
    }
    public static class ImageConversion
    {
        // Test: PNG yerine Python'un hazırladığı ham RGBA dosyası okunur: $SP_RAW_DIR/<png bayt sayısı>.raw
        public static bool LoadImage(Texture2D t, byte[] data, bool nonReadable)
        {
            var raw = File.ReadAllBytes(Path.Combine(Environment.GetEnvironmentVariable("SP_RAW_DIR"), data.Length + ".raw"));
            int w = BitConverter.ToInt32(raw, 0), h = BitConverter.ToInt32(raw, 4);
            t.width = w; t.height = h; t.px = new Color32[w * h];
            for (int i = 0; i < w * h; i++) t.px[i] = new Color32(raw[8 + i * 4], raw[9 + i * 4], raw[10 + i * 4], raw[11 + i * 4]);
            return true;
        }
    }
}
