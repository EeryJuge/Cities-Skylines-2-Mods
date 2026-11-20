using System;
using System.Globalization;
using System.Text;
using Colossal.UI.Binding;
using Game;
using Game.Input;
using Game.Tools;
using Game.UI;
using SignsPlus.Data;
using Unity.Entities;
using UnityEngine;

namespace SignsPlus.Systems
{
    /// <summary>
    /// Editör penceresinin köprüsü (Transport+ / Demand+ ile aynı düzen, sol üstte buton yok).
    /// Pencere üç yoldan açılır: bir Signs+ levhasına tıklamak, kısayol (Shift+L; imlecin altındaki levha seçilir)
    /// ve UIPlus gibi başka bir modun çağırabileceği "signsPlus.toggle" / "signsPlus.setOpen" tetikleyicileri.
    /// </summary>
    public partial class SignsPlusUISystem : UISystemBase
    {
        private const string kGroup = "signsPlus";

        public override GameMode gameMode => GameMode.GameOrEditor;

        private ValueBinding<bool> m_IsOpen;
        private ValueBinding<string> m_State;
        private SignRenderSystem m_Render;
        private ToolSystem m_Tool;
        private ProxyAction m_ToggleAction;
        private Entity m_Selected = Entity.Null;
        private Entity m_LastToolSelected = Entity.Null;
        private bool m_Typing;
        private bool m_Dirty;
        private int m_Frames;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Render = World.GetOrCreateSystemManaged<SignRenderSystem>();
            m_Tool = World.GetOrCreateSystemManaged<ToolSystem>();

            AddBinding(m_IsOpen = new ValueBinding<bool>(kGroup, "isOpen", false));
            AddBinding(m_State = new ValueBinding<string>(kGroup, "state", "{}"));
            AddBinding(new TriggerBinding<bool>(kGroup, "setOpen", SetOpen));
            AddBinding(new TriggerBinding(kGroup, "toggle", Toggle));
            AddBinding(new TriggerBinding(kGroup, "pickCenter", PickCenter));
            AddBinding(new TriggerBinding<string, int, string>(kGroup, "setFace", SetFace));
            AddBinding(new TriggerBinding<bool>(kGroup, "setTyping", typing => m_Typing = typing));

            if (Mod.Settings != null)
            {
                m_ToggleAction = Mod.Settings.GetAction(Setting.kToggleAction);
                if (m_ToggleAction != null) m_ToggleAction.shouldBeEnabled = true;
            }
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            try
            {
                // Şehirde bir Signs+ levhasına tıklanınca editör o levhayla açılır
                var toolSel = m_Tool != null ? m_Tool.selected : Entity.Null;
                if (toolSel != m_LastToolSelected)
                {
                    m_LastToolSelected = toolSel;
                    if (toolSel != Entity.Null && m_Render.TryGetInstance(toolSel, out _))
                    {
                        m_Selected = toolSel;
                        m_Dirty = true;
                        SetOpen(true);
                    }
                }

                if (!m_Typing && m_ToggleAction != null && m_ToggleAction.WasPerformedThisFrame()) Toggle();

                if (m_IsOpen.value && (m_Dirty || ++m_Frames >= 15))
                {
                    m_Frames = 0;
                    m_Dirty = false;
                    PushState();
                }
            }
            catch (Exception e)
            {
                Mod.log.Error(e, "Signs+ arayüz güncellemesi başarısız");
            }
        }

        protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Selected = Entity.Null;
            m_LastToolSelected = Entity.Null;
            m_Typing = false;
            SetOpen(false);
            m_State.Update("{}");
            if (m_ToggleAction != null) m_ToggleAction.shouldBeEnabled = (mode & GameMode.GameOrEditor) != 0;
        }

        /* ------------------------------------------------------------------ açma / seçme */

        private void SetOpen(bool open)
        {
            if (!open) m_Typing = false;
            if (m_IsOpen.value == open) return;
            m_IsOpen.Update(open);
            if (open) { m_Frames = 0; PushState(); }
        }

        private void Toggle()
        {
            if (m_IsOpen.value) { SetOpen(false); return; }
            // Seçili bir Signs+ levhası yoksa imlecin altındaki (yoksa ekranın ortasındaki) levha seçilir
            var toolSel = m_Tool != null ? m_Tool.selected : Entity.Null;
            if (toolSel != Entity.Null && m_Render.TryGetInstance(toolSel, out _)) m_Selected = toolSel;
            else
            {
                var picked = m_Render.PickAtScreen(MousePosition());
                if (picked != Entity.Null) m_Selected = picked;
            }
            SetOpen(true);
        }

        private void PickCenter()
        {
            var picked = m_Render.PickAtScreen(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            if (picked != Entity.Null) m_Selected = picked;
            m_Dirty = true;
        }

        private static Vector2 MousePosition()
        {
            try
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse != null) return mouse.position.ReadValue();
            }
            catch (Exception) { /* yedek: ekranın ortası */ }
            return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        }

        /* ------------------------------------------------------------------ değişiklikler */

        private void SetFace(string face, int category, string spec)
        {
            if (!m_Render.TryGetInstance(m_Selected, out var inst)) return;
            bool known = false;
            foreach (var f in inst.Faces) known |= f.Id == face;
            if (!known) return;
            category = Math.Max(0, Math.Min(5, category));
            m_Render.Store.Set(inst.Key, face, new FaceContent { Category = (SignCategory)category, Spec = spec ?? string.Empty });
            m_Dirty = true;
        }

        /* ------------------------------------------------------------------ durum */

        private void PushState()
        {
            var sb = new StringBuilder(512);
            sb.Append("{\"city\":");
            Str(sb, m_Render.Store.CityName ?? string.Empty);
            sb.Append(",\"count\":").Append(m_Render.Instances.Count.ToString(CultureInfo.InvariantCulture));
            if (m_Render.TryGetInstance(m_Selected, out var inst))
            {
                sb.Append(",\"sel\":{\"asset\":");
                Str(sb, inst.Asset);
                sb.Append(",\"key\":");
                Str(sb, inst.Key);
                sb.Append(",\"faces\":[");
                for (int i = 0; i < inst.Faces.Length; i++)
                {
                    var f = inst.Faces[i];
                    var c = m_Render.GetContent(inst, f.Id);
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"id\":");
                    Str(sb, f.Id);
                    sb.Append(",\"w\":").Append(f.Width.ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(",\"h\":").Append(f.Height.ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(",\"cat\":").Append(((int)c.Category).ToString(CultureInfo.InvariantCulture));
                    sb.Append(",\"kind\":").Append(((int)f.Kind).ToString(CultureInfo.InvariantCulture));
                    sb.Append(",\"spec\":");
                    Str(sb, c.Spec ?? string.Empty);
                    sb.Append('}');
                }
                sb.Append("]}");
            }
            else sb.Append(",\"sel\":null");
            sb.Append('}');
            string json = sb.ToString();
            if (json != m_State.value) m_State.Update(json);
        }

        private static void Str(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case '\t': sb.Append(' '); break;
                    default:
                        if (c < 0x20) sb.Append(' ');
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
