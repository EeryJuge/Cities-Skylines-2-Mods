using System.Collections.Generic;
using Game;
using Game.Objects;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace SignsPlus.Systems
{
    /// <summary>
    /// Serbest yerleştirme: Signs+ levhaları yolların, kaldırımların ve diğer nesnelerin üstüne hata vermeden konur.
    ///
    /// Oyun bir nesneyi yerleştirirken (Game.Objects.ValidationHelpers) ve yol yaparken (Game.Net.ValidationHelpers)
    /// çakışmayı nesnenin "çarpışma maskesine" bakarak denetler; maskesi boş olan nesne hiçbir şeyle çakışmaz.
    /// Maske, prefabın ObjectGeometryData bayraklarında Marker varken oyun kipinde boştur (ObjectUtils.GetCollisionMask).
    /// Ama Marker'lı nesneye fareyle tıklanamaz (seçim ve buldozer ışını onları atlar). Bu yüzden Marker yalnızca
    /// yerleştirme araçları (nesne, yol, alan) açıkken Signs+ prefablarına konur; seçim ve buldozerde kalkar.
    ///
    /// Ayrıca levhaların "Overridable" bayrağı kaldırılır: oyunun OverrideSystem'i yalnızca bu bayraklı nesneleri
    /// yolların altında gizler, yani levha yol yapılınca kaybolmaz.
    ///
    /// Yalnızca Signs+ prefablarının kendi verisi değişir. Anarchy'nin açıp kapattığı genel hata ayarlarına
    /// (ToolErrorData) ve başka hiçbir nesneye dokunulmaz; iki mod birlikte sorunsuz çalışır.
    /// </summary>
    public partial class SignPlacementSystem : GameSystemBase
    {
        private PrefabSystem m_PrefabSystem;
        private ToolSystem m_ToolSystem;
        private EntityQuery m_PrefabQuery;
        private readonly Dictionary<Entity, bool> m_IsSign = new Dictionary<Entity, bool>();
        private readonly Dictionary<Entity, GeometryFlags> m_Original = new Dictionary<Entity, GeometryFlags>();
        private readonly List<Entity> m_Signs = new List<Entity>();
        private bool m_Applied, m_MarkerOn, m_FreeOn;
        private int m_Frame, m_LastPrefabCount = -1;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_PrefabQuery = GetEntityQuery(ComponentType.ReadOnly<ObjectGeometryData>(), ComponentType.ReadOnly<PrefabData>());
        }

        protected override void OnUpdate()
        {
            var settings = Mod.Settings;
            if (settings == null) return;
            var tool = m_ToolSystem.activeTool;
            bool free = settings.FreePlacement;
            bool placing = tool is ObjectToolSystem || tool is NetToolSystem || tool is AreaToolSystem;
            bool marker = free && placing;

            // Prefab varlıkları UpdatePrefab ile yenilenebilir; ara sıra listeyi tazele ve bayrakları yeniden uygula
            bool refresh = ++m_Frame % 120 == 0;
            if (!refresh && m_Applied && marker == m_MarkerOn && free == m_FreeOn) return;
            if (refresh || m_Signs.Count == 0) FindSigns();
            Apply(marker, free);
            m_Applied = true;
            m_MarkerOn = marker;
            m_FreeOn = free;
        }

        private void FindSigns()
        {
            var entities = m_PrefabQuery.ToEntityArray(Allocator.Temp);
            try
            {
                if (entities.Length == m_LastPrefabCount && m_Signs.Count > 0) return;
                m_LastPrefabCount = entities.Length;
                m_Signs.Clear();
                foreach (var e in entities)
                {
                    if (!m_IsSign.TryGetValue(e, out bool isSign))
                    {
                        isSign = m_PrefabSystem.TryGetPrefab(e, out PrefabBase pb) && pb != null && SignRenderSystem.MatchAsset(pb.name) != null;
                        m_IsSign[e] = isSign;
                    }
                    if (isSign) m_Signs.Add(e);
                }
            }
            finally { entities.Dispose(); }
        }

        private void Apply(bool marker, bool free)
        {
            foreach (var e in m_Signs)
            {
                if (!EntityManager.Exists(e) || !EntityManager.HasComponent<ObjectGeometryData>(e)) continue;
                var data = EntityManager.GetComponentData<ObjectGeometryData>(e);
                if (!m_Original.TryGetValue(e, out var original))
                {
                    original = data.m_Flags & ~GeometryFlags.Marker;
                    m_Original[e] = original;
                    Mod.log.Info($"Serbest yerleştirme: {(m_PrefabSystem.TryGetPrefab(e, out PrefabBase pb) ? pb.name : e.ToString())} bayrakları {original}");
                }
                var flags = original;
                if (free) flags &= ~(GeometryFlags.Overridable | GeometryFlags.DeleteOverridden);
                if (marker) flags |= GeometryFlags.Marker;
                if (flags == data.m_Flags) continue;
                data.m_Flags = flags;
                EntityManager.SetComponentData(e, data);
            }
        }

        protected override void OnDestroy()
        {
            // Mod kapanırken prefabları ilk hallerine döndür
            try { foreach (var kv in m_Original) if (EntityManager.Exists(kv.Key) && EntityManager.HasComponent<ObjectGeometryData>(kv.Key)) { var d = EntityManager.GetComponentData<ObjectGeometryData>(kv.Key); d.m_Flags = kv.Value; EntityManager.SetComponentData(kv.Key, d); } }
            catch { /* dünya kapanıyorsa önemsiz */ }
            base.OnDestroy();
        }
    }
}
