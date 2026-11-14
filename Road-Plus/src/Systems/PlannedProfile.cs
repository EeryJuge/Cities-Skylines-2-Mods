// Aracın o karede çizdiği yolun yükseklik profili. HeightOverrideSystem, oyunun böldüğü yol parçalarının
// yüksekliklerini bu profile göre yeniden yazar.
using System.Collections.Generic;
using TF2YolAraci.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace TF2YolAraci.Systems
{
    public sealed class PlannedProfile
    {
        /// <summary>Şu anki önizlemenin profili; önizleme yoksa null.</summary>
        public static PlannedProfile Current;

        public readonly Entity prefab;
        public readonly float earthworkMax;   // bu farkın altında yol arazi seviyesinde sayılır, arazi eğimle düzeltilir
        private readonly float2[] m_XZ;
        private readonly float[] m_Y;

        public PlannedProfile(Entity prefab, List<HeightSample> samples, float earthworkMax)
        {
            this.prefab = prefab;
            this.earthworkMax = earthworkMax;
            m_XZ = new float2[samples.Count];
            m_Y = new float[samples.Count];
            for (int i = 0; i < samples.Count; i++)
            {
                m_XZ[i] = new float2((float)samples[i].position.x, (float)samples[i].position.z);
                m_Y[i] = (float)samples[i].position.y;
            }
        }

        /// <summary>
        /// Height of the profile point closest to the hint. The hint's own height counts too, so a spiral whose
        /// loops lie above each other picks the right loop (the game keeps our heights at each piece's ends).
        /// </summary>
        public float HeightAt(float3 hint)
        {
            if (m_XZ.Length == 1) return m_Y[0];
            float best = float.MaxValue, height = m_Y[0];
            for (int i = 1; i < m_XZ.Length; i++)
            {
                float2 a = m_XZ[i - 1], ab = m_XZ[i] - a;
                float len2 = math.lengthsq(ab);
                float t = len2 < 1e-6f ? 0f : math.saturate(math.dot(hint.xz - a, ab) / len2);
                float y = math.lerp(m_Y[i - 1], m_Y[i], t);
                float dy = y - hint.y;
                float d = math.distancesq(a + ab * t, hint.xz) + dy * dy;
                if (d < best)
                {
                    best = d;
                    height = y;
                }
            }
            return height;
        }
    }
}
