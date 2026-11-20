// Bu dosya tools/gen_faces_cs.py tarafından tools/assets_spec.py'den üretilir. Elle düzenlemeyin.
using System.Collections.Generic;

namespace SignsPlus.Data
{
    /// <summary>Panel: asset'teki levhanın önüne yazı. Standard: şekilli trafik işareti (ön + gri arka). Plate: ek levha, boşsa çizilmez.</summary>
    public enum FaceKind { Panel, Standard, Plate }

    /// <summary>Bir asset üzerindeki levha yüzü, nesnenin yerel koordinatlarında (metre). Yüz +Z yönüne bakar.</summary>
    public readonly struct FaceDef
    {
        public readonly string Id;
        public readonly float X, Y, Z, Width, Height;
        public readonly FaceKind Kind;
        public FaceDef(string id, float x, float y, float z, float w, float h, FaceKind kind = FaceKind.Panel) { Id = id; X = x; Y = y; Z = z; Width = w; Height = h; Kind = kind; }
    }

    public static class Faces
    {
        public static readonly Dictionary<string, FaceDef[]> ByAsset = new Dictionary<string, FaceDef[]>
        {
            { "SignsPlusYerLevhasi", new[] { new FaceDef("A", -0.000f, 3.200f, 0f, 3.000f, 2.000f, FaceKind.Panel) } }, // Yer levhası, iki yuvarlak direk, 3,0 × 2,0 m panel
            { "SignsPlusLBayrak", new[] { new FaceDef("A", -4.000f, 6.850f, 0f, 4.000f, 2.500f, FaceKind.Panel) } }, // L tipi bayrak direk (kutu profil), 4,0 × 2,5 m panel
            { "SignsPlusTDirek", new[] { new FaceDef("A", 3.450f, 6.850f, 0f, 3.500f, 2.500f, FaceKind.Panel), new FaceDef("B", -3.450f, 6.850f, 0f, 3.500f, 2.500f, FaceKind.Panel) } }, // T tipi çift kollu direk (kutu profil), iki adet 3,5 × 2,5 m panel
            { "SignsPlusGantry", new[] { new FaceDef("A", 3.300f, 6.850f, 0f, 6.000f, 2.500f, FaceKind.Panel), new FaceDef("B", -2.000f, 6.850f, 0f, 4.000f, 2.500f, FaceKind.Panel) } }, // Portal gantry (kutu kiriş), 16 m açıklık, 6,0 × 2,5 ve 4,0 × 2,5 m paneller
            { "SignsPlusIsaretDirek", new[] { new FaceDef("A", -0.000f, 2.600f, 0f, 0.900f, 0.900f, FaceKind.Standard), new FaceDef("B", -0.000f, 1.930f, 0f, 0.900f, 0.300f, FaceKind.Plate) } }, // Standart trafik işareti direği, 90 cm işaret + 90 × 30 cm ek levha
        };
    }
}
