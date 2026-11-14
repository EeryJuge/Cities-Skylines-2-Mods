// Anarchy support without a hard dependency. Anarchy offers a public bridge for other mods
// (Anarchy.Bridge.AnarchyBridge.TryAddToolSystem); once our tool is registered there, Anarchy's
// "disable error checks" toggle also works while drawing with Road+. Without Anarchy nothing happens.
using System;
using System.Reflection;
using Game.Tools;

namespace TF2YolAraci.Systems
{
    public static class AnarchyCompat
    {
        private static bool s_Done;

        public static void TryRegister(ToolBaseSystem tool)
        {
            if (s_Done || tool == null) return;
            s_Done = true;
            try
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type bridge = asm.GetType("Anarchy.Bridge.AnarchyBridge", false);
                    if (bridge == null) continue;
                    MethodInfo add = bridge.GetMethod("TryAddToolSystem", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(ToolBaseSystem) }, null);
                    if (add == null)
                    {
                        Mod.log.Info("Anarchy found, but its bridge has no TryAddToolSystem; skipping.");
                        return;
                    }
                    object ok = add.Invoke(null, new object[] { tool });
                    Mod.log.Info("Anarchy found: Road+ tool registered (" + ok + ").");
                    return;
                }
                Mod.log.Info("Anarchy not installed.");
            }
            catch (Exception e)
            {
                Mod.log.Warn("Could not register with Anarchy: " + e.Message);
            }
        }
    }
}
