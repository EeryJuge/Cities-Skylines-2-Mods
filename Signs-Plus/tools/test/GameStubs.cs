// Yalnızca sözdizimi/tür denetimi için kaba taklitler (gerçek oyun API'si değildir).
using System; using System.Collections.Generic;
namespace Colossal.Logging { public interface ILog { void Info(object m); void Warn(object m); void Warn(Exception e, object m); void Error(object m); void Error(Exception e, object m); ILog SetShowsErrorsInUI(bool b); } public static class LogManager { public static ILog GetLogger(string n) => null; } }
namespace Colossal.IO.AssetDatabase { public class FileLocationAttribute : Attribute { public FileLocationAttribute(string s) { } } public class AssetDatabase { public static AssetDatabase global; public void LoadSettings(string n, object s, object d) { } } }
namespace Colossal { public interface IDictionaryEntryError { } public interface IDictionarySource { IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> e, Dictionary<string, int> i); void Unload(); } }
namespace Colossal.Serialization.Entities { public enum Purpose { LoadGame } }
namespace Colossal.UI.Binding {
  public interface IBinding { }
  public class ValueBinding<T> : IBinding { public ValueBinding(string g, string n, T v) { value = v; } public T value; public void Update(T v) { value = v; } }
  public class TriggerBinding : IBinding { public TriggerBinding(string g, string n, Action a) { } }
  public class TriggerBinding<T> : IBinding { public TriggerBinding(string g, string n, Action<T> a) { } }
  public class TriggerBinding<T1, T2, T3> : IBinding { public TriggerBinding(string g, string n, Action<T1, T2, T3> a) { } } }
namespace Game { [Flags] public enum GameMode { None = 0, MainMenu = 1, Game = 2, Editor = 4, GameOrEditor = 6, All = 7 }
  public enum SystemUpdatePhase { Rendering, UIUpdate, MainLoop, Modification1 } public class UpdateSystem { public void UpdateAt<T>(SystemUpdatePhase p) { } }
  public abstract class GameSystemBase : Unity.Entities.SystemBase { protected virtual void OnCreate() { } protected abstract void OnUpdate(); protected virtual void OnDestroy() { }
    protected virtual void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose p, GameMode m) { } } }
namespace Game.UI { public abstract class UISystemBase : Game.GameSystemBase { public virtual Game.GameMode gameMode => Game.GameMode.All; protected void AddBinding(Colossal.UI.Binding.IBinding b) { } protected override void OnUpdate() { } } }
namespace Game.Input { public enum ActionType { Button } public static class Usages { public const string kDefaultUsage = ""; } public enum BindingKeyboard { L, Y }
  public class ProxyBinding { } public class ProxyAction { public bool shouldBeEnabled; public bool WasPerformedThisFrame() => false; } }
namespace Game.Modding { public interface IMod { void OnLoad(Game.UpdateSystem u); void OnDispose(); }
  public abstract class ModSetting { protected ModSetting(IMod m) { } public abstract void SetDefaults(); public void RegisterInOptionsUI() { } public void UnregisterInOptionsUI() { } public void RegisterKeyBindings() { } public void ResetKeyBindings() { }
   public Game.Input.ProxyAction GetAction(string n) => null;
   public string GetSettingsLocaleID() => ""; public string GetOptionTabLocaleID(string s) => ""; public string GetOptionGroupLocaleID(string s) => ""; public string GetOptionLabelLocaleID(string s) => ""; public string GetOptionDescLocaleID(string s) => ""; public string GetEnumValueLocaleID<T>(T v) => ""; public string GetBindingKeyLocaleID(string a) => ""; public string GetBindingMapLocaleID() => ""; } }
namespace Game.Settings { public class SettingsUIGroupOrderAttribute : Attribute { public SettingsUIGroupOrderAttribute(params string[] s) { } } public class SettingsUIShowGroupNameAttribute : Attribute { public SettingsUIShowGroupNameAttribute(params string[] s) { } }
  public class SettingsUISectionAttribute : Attribute { public SettingsUISectionAttribute(string a, string b) { } } public class SettingsUITextInputAttribute : Attribute { } public class SettingsUIHiddenAttribute : Attribute { }
  public class SettingsUISliderAttribute : Attribute { public float min, max, step; }
  public class SettingsUIKeyboardActionAttribute : Attribute { public SettingsUIKeyboardActionAttribute(string n, Game.Input.ActionType t, string[] usages = null, string[] interactions = null) { } }
  public class SettingsUIKeyboardBindingAttribute : Attribute { public SettingsUIKeyboardBindingAttribute(Game.Input.BindingKeyboard k, string a, bool shift = false) { } } }
namespace Game.SceneFlow { public class ModManager { public bool TryGetExecutableAsset(object m, out Asset a) { a = null; return false; } } public class Asset { public string path; } public class LocalizationManager { public void AddSource(string l, Colossal.IDictionarySource s) { } } public class GameManager { public static GameManager instance; public ModManager modManager; public LocalizationManager localizationManager; } }
namespace Game.Common { public struct Deleted { } }
namespace Game.City { public class CityConfigurationSystem { public string cityName; } }
namespace Game.Tools { public class ToolSystem { public Unity.Entities.Entity selected; public ToolBaseSystem activeTool; } public class ToolBaseSystem { } public class ObjectToolSystem : ToolBaseSystem { } public class NetToolSystem : ToolBaseSystem { } public class AreaToolSystem : ToolBaseSystem { } }
namespace Game.Objects { [Flags] public enum GeometryFlags { None = 0, Overridable = 2, Marker = 4, DeleteOverridden = 16 } [Flags] public enum PlacementFlags { None = 0, OnGround = 1 } public struct Object { } public struct Transform { public Unity.Mathematics.float3 m_Position; public Unity.Mathematics.quaternion m_Rotation; } }
namespace Game.Prefabs { public struct ObjectGeometryData { public Game.Objects.GeometryFlags m_Flags; } public struct PrefabRef { public Unity.Entities.Entity m_Prefab; } public struct PrefabData { } public struct ObjectData { } public struct UIAssetMenuData { } public struct UIAssetCategoryData { public Unity.Entities.Entity m_Menu; } public struct UIObjectData { public Unity.Entities.Entity m_Group; public int m_Priority; } public struct PlaceableObjectData { public uint m_ConstructionCost; public int m_XPReward; public Game.Objects.PlacementFlags m_Flags; } public struct UIGroupElement { public Unity.Entities.Entity m_Prefab; } public struct Locked { }
  public class ComponentBase : UnityEngine.ScriptableObject { }
  public class PrefabBase : ComponentBase { public T AddComponent<T>() where T : ComponentBase => null; public bool TryGet<T>(out T c) where T : ComponentBase { c = null; return false; } }
  public class StaticObjectPrefab : PrefabBase { } public abstract class UIGroupPrefab : PrefabBase { } public class UIAssetMenuPrefab : UIGroupPrefab { } public class UIAssetCategoryPrefab : UIGroupPrefab { public UIAssetMenuPrefab m_Menu; }
  public class UIObject : ComponentBase { public UIGroupPrefab m_Group; public int m_Priority; public string m_Icon; } public class PlaceableObject : ComponentBase { public uint m_ConstructionCost; public int m_XPReward; }
  public class PrefabSystem { public bool TryGetPrefab<T>(Unity.Entities.Entity e, out T p) where T : PrefabBase { p = null; return false; } public string GetPrefabName(Unity.Entities.Entity e) => ""; public bool AddPrefab(PrefabBase p, string parentName = null, PrefabBase parentPrefab = null, ComponentBase parentComponent = null) => true;
    public void UpdatePrefab(PrefabBase p, Unity.Entities.Entity src = default) { } public bool TryGetEntity(PrefabBase p, out Unity.Entities.Entity e) { e = default; return false; } } }
namespace Unity.Mathematics { public struct float3 { public float x, y, z; public static implicit operator UnityEngine.Vector3(float3 v) => default; } public struct quaternion { public static implicit operator UnityEngine.Quaternion(quaternion q) => default; } public static class math { public static float round(float f) => f; } }
namespace Unity.Collections { public enum Allocator { Temp } public struct NativeArray<T> : IDisposable, IEnumerable<T> { public int Length => 0; public T this[int i] => default; public void Dispose() { } public IEnumerator<T> GetEnumerator() { yield break; } System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { yield break; } } }
namespace Unity.Entities { public struct Entity : IEquatable<Entity> { public static Entity Null; public bool Equals(Entity o) => true; public static bool operator ==(Entity a, Entity b) => true; public static bool operator !=(Entity a, Entity b) => false; public override bool Equals(object o) => true; public override int GetHashCode() => 0; }
  public struct ComponentType { public static ComponentType ReadOnly<T>() => default; }
  public class EntityQueryDesc { public ComponentType[] All, None; } public struct EntityQuery { public Unity.Collections.NativeArray<T> ToComponentDataArray<T>(Unity.Collections.Allocator a) => default; public Unity.Collections.NativeArray<Entity> ToEntityArray(Unity.Collections.Allocator a) => default; }
  public class World { public T GetOrCreateSystemManaged<T>() where T : new() => new T(); }
  public struct DynamicBuffer<T> { public int Length => 0; public T this[int i] => default; public int Add(T v) => 0; } public class EntityManager { public bool Exists(Entity e) => true; public bool HasComponent<T>(Entity e) => false; public void SetComponentEnabled<T>(Entity e, bool b) { } public bool IsComponentEnabled<T>(Entity e) => false; public T GetComponentData<T>(Entity e) => default; public void SetComponentData<T>(Entity e, T v) { } public bool AddComponentData<T>(Entity e, T v) => true; public bool HasBuffer<T>(Entity e) => false; public DynamicBuffer<T> GetBuffer<T>(Entity e, bool ro = false) => default; }
  public abstract class SystemBase { public World World; public EntityManager EntityManager; protected EntityQuery GetEntityQuery(EntityQueryDesc d) => default; protected EntityQuery GetEntityQuery(params ComponentType[] t) => default; } }
namespace UnityEngine.Rendering { public enum ShadowCastingMode { Off } }
namespace UnityEngine.InputSystem { public class Vec2Control { public UnityEngine.Vector2 ReadValue() => default; } public class Mouse { public static Mouse current; public Vec2Control position; } }
namespace UnityEngine {
  public class ScriptableObject : Object { public static T CreateInstance<T>() where T : ScriptableObject, new() => new T(); }
  public static class Application { public static string persistentDataPath = "/tmp"; } public static class Time { public static float realtimeSinceStartup; }
  public static class Screen { public static int width, height; }
  public struct Ray { public Vector3 origin, direction; }
  public class Camera { public static Camera main; public Ray ScreenPointToRay(Vector3 p) => default; }
  public struct Vector3 { public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } public static Vector3 one, forward, back; public float magnitude => 0;
    public static Vector3 operator -(Vector3 a, Vector3 b) => default; public static Vector3 operator *(Vector3 a, float b) => default; public static float Dot(Vector3 a, Vector3 b) => 0; }
  public struct Vector4 { public Vector4(float x, float y, float z, float w) { } }
  public struct Quaternion { public static Quaternion identity; public static Quaternion Euler(float x, float y, float z) => default; }
  public struct Matrix4x4 { public static Matrix4x4 TRS(Vector3 p, Quaternion q, Vector3 s) => default; public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b) => default; public Vector3 MultiplyPoint3x4(Vector3 v) => v; }
  public struct Color { public static Color white; }
  public class Shader : Object { public static Shader Find(string n) => null; }
  public class Material : Object { public Material(Shader s) { } public bool enableInstancing; public bool HasProperty(string n) => true; public void SetColor(string n, Color c) { } public void SetFloat(string n, float f) { } }
  public class MaterialPropertyBlock { public void SetTexture(string n, Texture2D t) { } }
  public class Mesh : Object { public Vector3[] vertices, normals; public Vector2[] uv; public Vector4[] tangents; public int[] triangles; public void RecalculateBounds() { } }
  public struct RenderParams { public RenderParams(Material m) { shadowCastingMode = 0; receiveShadows = false; layer = 0; matProps = null; } public Rendering.ShadowCastingMode shadowCastingMode; public bool receiveShadows; public int layer; public MaterialPropertyBlock matProps; }
  public static class Graphics { public static void RenderMesh(in RenderParams rp, Mesh m, int sub, Matrix4x4 mat) { } }
}
