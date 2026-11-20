"""assets_spec.py'deki panel konumlarından modun yüz tablosunu (SignsPlus/Data/Faces.cs) üretir."""
import os, sys
sys.path.insert(0, os.path.dirname(__file__))
from assets_spec import ASSETS

out = os.path.join(os.path.dirname(__file__), "..", "Data", "Faces.cs")
rows = []
for name, spec in ASSETS.items():
    faces = []
    for panel in spec["panels"]:
        pid, cx, z0, w, h = panel[:5]
        kind = {"panel": "FaceKind.Panel", "std": "FaceKind.Standard", "plate": "FaceKind.Plate"}[panel[5] if len(panel) > 5 else "panel"]
        # Blender (x, y, z) → oyun (-x, z, -y); ön yüz y = 0 düzleminde, oyunda +Z'ye bakar
        faces.append(f'new FaceDef("{pid}", {-cx:.3f}f, {z0 + h / 2:.3f}f, 0f, {w:.3f}f, {h:.3f}f, {kind})')
    rows.append(f'            {{ "{name}", new[] {{ {", ".join(faces)} }} }}, // {spec["desc"]}')

code = f'''// Bu dosya tools/gen_faces_cs.py tarafından tools/assets_spec.py'den üretilir. Elle düzenlemeyin.
using System.Collections.Generic;

namespace SignsPlus.Data
{{
    /// <summary>Panel: asset'teki levhanın önüne yazı. Standard: şekilli trafik işareti (ön + gri arka). Plate: ek levha, boşsa çizilmez.</summary>
    public enum FaceKind {{ Panel, Standard, Plate }}

    /// <summary>Bir asset üzerindeki levha yüzü, nesnenin yerel koordinatlarında (metre). Yüz +Z yönüne bakar.</summary>
    public readonly struct FaceDef
    {{
        public readonly string Id;
        public readonly float X, Y, Z, Width, Height;
        public readonly FaceKind Kind;
        public FaceDef(string id, float x, float y, float z, float w, float h, FaceKind kind = FaceKind.Panel) {{ Id = id; X = x; Y = y; Z = z; Width = w; Height = h; Kind = kind; }}
    }}

    public static class Faces
    {{
        public static readonly Dictionary<string, FaceDef[]> ByAsset = new Dictionary<string, FaceDef[]>
        {{
{chr(10).join(rows)}
        }};
    }}
}}
'''
open(out, "w", encoding="utf-8").write(code)
print(code)
