// Turns Road+ on and off and keeps it on.
//
// Shift + R: toggle Road+.
// While on, whatever road, track or path is picked from the game's menu continues in the Road+ tool
// (Road Builder roads and asset roads included). Right click does not close it.
using Game;
using Game.Prefabs;
using Game.Tools;
using TF2YolAraci.UI;
using UnityEngine.InputSystem;
using UnityEngine.Scripting;

namespace TF2YolAraci.Systems
{
    public partial class ToolActivatorSystem : GameSystemBase
    {
        /// <summary>TF2 modu açık mı (Shift + R ile değişir).</summary>
        public static bool StickyOn { get; private set; }

        private ToolSystem m_ToolSystem;
        private NetToolSystem m_NetToolSystem;
        private DefaultToolSystem m_DefaultToolSystem;
        private TF2RoadToolSystem m_TF2Tool;

        private ToolBaseSystem m_PrevActive;
        private int m_FramesSinceRightClick = 999;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_NetToolSystem = World.GetOrCreateSystemManaged<NetToolSystem>();
            m_DefaultToolSystem = World.GetOrCreateSystemManaged<DefaultToolSystem>();
            m_TF2Tool = World.GetOrCreateSystemManaged<TF2RoadToolSystem>();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            ToolBaseSystem active = m_ToolSystem.activeTool;

            if (mouse != null && (mouse.rightButton.wasPressedThisFrame || mouse.rightButton.wasReleasedThisFrame))
                m_FramesSinceRightClick = 0;
            else if (m_FramesSinceRightClick < 999)
                m_FramesSinceRightClick++;

            if (ToolStatus.closeRequested)
            {
                ToolStatus.closeRequested = false;
                if (StickyOn) Toggle(active);
            }
            else if (kb != null && kb.shiftKey.isPressed && kb.rKey.wasPressedThisFrame)
            {
                Toggle(active);
            }
            else if (StickyOn)
            {
                // Menüden başka bir yol seçildi: oyun kendi yol aracını açtı, TF2'ye geri al.
                if (active == m_NetToolSystem && TF2RoadToolSystem.IsSupported(m_NetToolSystem.prefab))
                {
                    Activate((NetGeometryPrefab)m_NetToolSystem.prefab);
                }
                // Sağ tıktan hemen sonra oyun aracı kapattıysa geri aç.
                else if (active == m_DefaultToolSystem && m_PrevActive == m_TF2Tool && m_FramesSinceRightClick <= 3
                         && m_TF2Tool.GetPrefab() is NetGeometryPrefab last)
                {
                    Activate(last);
                }
            }

            m_PrevActive = m_ToolSystem.activeTool;
        }

        private void Toggle(ToolBaseSystem active)
        {
            if (StickyOn)
            {
                StickyOn = false;
                if (active == m_TF2Tool && m_TF2Tool.GetPrefab() is NetGeometryPrefab current)
                {
                    m_NetToolSystem.TrySetPrefab(current);
                    m_ToolSystem.activeTool = m_NetToolSystem;
                }
                Mod.log.Info("Road+ disabled");
                return;
            }

            if (active == m_NetToolSystem && TF2RoadToolSystem.IsSupported(m_NetToolSystem.prefab))
            {
                StickyOn = true;
                Activate((NetGeometryPrefab)m_NetToolSystem.prefab);
                Mod.log.Info("Road+ enabled");
            }
            else
            {
                Mod.log.Info("Not enabled: select a road, track or path in the game's menu first");
            }
        }

        private void Activate(NetGeometryPrefab road)
        {
            m_TF2Tool.TrySetPrefab(road);
            m_ToolSystem.activeTool = m_TF2Tool;
            Mod.log.Info($"Road+ tool: {road.name}");
        }
    }
}
