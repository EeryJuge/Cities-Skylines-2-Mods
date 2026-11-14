// The game re-samples the inner heights of ground-level roads from the terrain (CourseSplitSystem).
// After the game has split the course and before it creates edges (before GenerateNodesSystem, Modification1),
// this system writes the heights of the pieces drawn by Road+ back to our profile.
//
// Yükseklik farkı "eğimli arazi sınırı"nın altındaysa yükseklik farkı 0 bildirilir: oyun normal yol yapar ve
// altındaki araziyi yola göre eğimle (dolgu / yarma) kendisi düzeltir. Sınırın üstünde gerçek fark bildirilir:
// oyun istinat duvarı, köprü ya da tünel yapar.
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace TF2YolAraci.Systems
{
    public partial class HeightOverrideSystem : GameSystemBase
    {
        private EntityQuery m_CourseQuery;
        private TerrainSystem m_TerrainSystem;
        private Entity m_LoggedPrefab;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_CourseQuery = GetEntityQuery(ComponentType.ReadOnly<CreationDefinition>(), ComponentType.ReadWrite<NetCourse>(), ComponentType.ReadOnly<Updated>());
            RequireForUpdate(m_CourseQuery);
        }

        [Preserve]
        protected override void OnUpdate()
        {
            PlannedProfile plan = PlannedProfile.Current;
            if (plan == null || !TF2YolAraci.UI.ToolStatus.active) return; // only while Road+ is the active tool

            NativeArray<Entity> entities = m_CourseQuery.ToEntityArray(Allocator.Temp);
            TerrainHeightData heights = m_TerrainSystem.GetHeightData();

            for (int i = 0; i < entities.Length; i++)
            {
                Entity e = entities[i];
                CreationDefinition def = EntityManager.GetComponentData<CreationDefinition>(e);
                if (def.m_Prefab != plan.prefab || def.m_Owner != Entity.Null || (def.m_Flags & CreationFlags.Delete) != 0) continue;
                if (EntityManager.HasComponent<OwnerDefinition>(e)) continue;
                if (!EntityManager.HasComponent<NetGeometryData>(def.m_Prefab)) continue;

                NetGeometryData geo = EntityManager.GetComponentData<NetGeometryData>(def.m_Prefab);
                NetCourse course = EntityManager.GetComponentData<NetCourse>(e);
                Override(ref course, plan, geo, ref heights);
                EntityManager.SetComponentData(e, course);

                if (m_LoggedPrefab != def.m_Prefab)
                {
                    m_LoggedPrefab = def.m_Prefab;
                    Mod.log.Info($"Road data: elevation limit {geo.m_ElevationLimit:0.##} m, max slope {geo.m_MaxSlopeSteepness:P0}");
                }
            }
            entities.Dispose();
        }

        private void Override(ref NetCourse course, PlannedProfile plan, NetGeometryData geo, ref TerrainHeightData heights)
        {
            // Parçayı kendi aralığına kes; oyun da yükseklik yazarken bunu yapar.
            float2 range = new float2(course.m_StartPosition.m_CourseDelta, course.m_EndPosition.m_CourseDelta);
            Bezier4x3 curve = MathUtils.Cut(course.m_Curve, range);

            float3 p0 = curve.a, p3 = curve.d;
            float3 p13 = MathUtils.Position(curve, 1f / 3f), p23 = MathUtils.Position(curve, 2f / 3f);

            // Mevcut bir düğüme bağlanan uç, o düğümün yüksekliğinde kalır.
            // Each piece's ends still carry our heights; they are the hint for which spiral loop a point belongs to.
            float h0 = course.m_StartPosition.m_Position.y, h3 = course.m_EndPosition.m_Position.y;
            float y0 = IsExistingNode(course.m_StartPosition.m_Entity) ? h0 : plan.HeightAt(new float3(p0.x, h0, p0.z));
            float y3 = IsExistingNode(course.m_EndPosition.m_Entity) ? h3 : plan.HeightAt(new float3(p3.x, h3, p3.z));
            float y13 = plan.HeightAt(new float3(p13.x, math.lerp(y0, y3, 1f / 3f), p13.z));
            float y23 = plan.HeightAt(new float3(p23.x, math.lerp(y0, y3, 2f / 3f), p23.z));

            // Bezier'in y kontrol noktaları: eğri t = 0, 1/3, 2/3, 1'de bu yüksekliklerden geçer.
            float r1 = 27f * y13 - 8f * y0 - y3;
            float r2 = 27f * y23 - y0 - 8f * y3;
            curve.a.y = y0;
            curve.b.y = (2f * r1 - r2) / 18f;
            curve.c.y = (2f * r2 - r1) / 18f;
            curve.d.y = y3;

            course.m_Curve = curve;
            course.m_StartPosition.m_CourseDelta = 0f;
            course.m_EndPosition.m_CourseDelta = 1f;
            course.m_StartPosition.m_Position.y = y0;
            course.m_EndPosition.m_Position.y = y3;
            course.m_Length = MathUtils.Length(curve);

            // Arazi seviyesi mi, istinat duvarı / köprü mü? Oyunun kendi zorunlu yükseltme bayraklarına (ör. su üstü) dokunma.
            float threshold = math.max(plan.earthworkMax, geo.m_ElevationLimit);
            bool forcedStart = (course.m_StartPosition.m_Flags & (CoursePosFlags.ForceElevatedNode | CoursePosFlags.ForceElevatedEdge)) != 0;
            bool forcedEnd = (course.m_EndPosition.m_Flags & (CoursePosFlags.ForceElevatedNode | CoursePosFlags.ForceElevatedEdge)) != 0;
            if (!forcedStart)
                course.m_StartPosition.m_Elevation = ElevationAt(curve.a, threshold, ref heights);
            if (!forcedEnd)
                course.m_EndPosition.m_Elevation = ElevationAt(curve.d, threshold, ref heights);
            if (!forcedStart && !forcedEnd)
                course.m_Elevation = ElevationAt(MathUtils.Position(curve, 0.5f), threshold, ref heights);
        }

        private static float2 ElevationAt(float3 pos, float threshold, ref TerrainHeightData heights)
        {
            float e = pos.y - TerrainUtils.SampleHeight(ref heights, pos);
            return math.abs(e) < threshold ? default(float2) : new float2(e);
        }

        private bool IsExistingNode(Entity entity) =>
            entity != Entity.Null && EntityManager.HasComponent<Node>(entity) && !EntityManager.HasComponent<Temp>(entity);
    }
}
