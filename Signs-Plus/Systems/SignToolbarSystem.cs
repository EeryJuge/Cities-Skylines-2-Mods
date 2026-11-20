using System;
using System.Collections.Generic;
using System.Text;
using Colossal.Serialization.Entities;
using Game;
using Game.Prefabs;
using Game.Objects;
using Game.SceneFlow;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace SignsPlus.Systems
{
    /// <summary>
    /// Signs+ levhalarını şehirde yerleştirilebilir yapar: Yollar menüsüne "Signs+" sekmesi ekler (ya da ayara göre
    /// Yollar menüsündeki mevcut bir sekmeyi kullanır) ve levha prefablarına UIObject (sekmede görünsün) +
    /// PlaceableObject (yerleştirilebilsin) bileşenlerini verir. Prefablar yüklendikten sonra bir kez çalışır.
    ///
    /// Oyun yeni bir sekmeyi "kilitli" sayabiliyor (kilit durumu şehir yüklenince yeniden kuruluyor); bu yüzden her şehir
    /// yüklemesinden sonra sekmenin ve levhaların kilidi birkaç kez açılır ve durum günlüğe yazılır.
    /// </summary>
    public partial class SignToolbarSystem : GameSystemBase
    {
        private static readonly string[] kPreferredMenus = { "Roads", "Landscaping", "Transportation" };
        public const string kCategoryName = "SignsPlusCategory";
        public const string kCategoryIcon = "coui://ui-mods/SignsPlus-icon.svg";

        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_MenuQuery;
        private EntityQuery m_CategoryQuery;
        private EntityQuery m_ObjectPrefabQuery;
        private UIAssetMenuPrefab m_Menu;
        private UIGroupPrefab m_Group;
        private readonly HashSet<string> m_Done = new HashSet<string>();
        private readonly List<PrefabBase> m_SignPrefabs = new List<PrefabBase>();

        /// <summary>Oyunda bulunan Signs+ levha prefabları (SignPlacementSystem kullanır).</summary>
        public IReadOnlyList<PrefabBase> SignPrefabs => m_SignPrefabs;
        private int m_Frame;
        private int m_Attempts;
        private bool m_Finished;
        private int m_UnlockPasses;
        private bool m_Diagnose;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_MenuQuery = GetEntityQuery(ComponentType.ReadOnly<UIAssetMenuData>(), ComponentType.ReadOnly<PrefabData>());
            m_CategoryQuery = GetEntityQuery(ComponentType.ReadOnly<UIAssetCategoryData>(), ComponentType.ReadOnly<PrefabData>());
            m_ObjectPrefabQuery = GetEntityQuery(ComponentType.ReadOnly<ObjectData>(), ComponentType.ReadOnly<PrefabData>());
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            // Şehir yüklenince kilit durumları yeniden kurulur; birkaç saniye boyunca kilidi açmayı tekrarla
            if (mode == GameMode.Game) { m_UnlockPasses = 20; m_Diagnose = true; }
        }

        protected override void OnUpdate()
        {
            try
            {
                m_Frame++;
                if (m_UnlockPasses > 0 && m_Frame % 30 == 0) { Unlock(); m_UnlockPasses--; }
                if (m_Finished || m_Frame % 120 != 0) return;
                m_Attempts++;
                if (m_Group == null) FindOrCreateGroup();
                if (m_Group == null) { GiveUpAfter(20, "araç çubuğu menüsü bulunamadı"); return; }
                PatchSignPrefabs();
                if (m_Done.Count >= SignsPlus.Data.Faces.ByAsset.Count) { m_Finished = true; m_UnlockPasses = 5; }
                else GiveUpAfter(60, $"{SignsPlus.Data.Faces.ByAsset.Count - m_Done.Count} Signs+ asseti bulunamadı (Asset Importer ile kaydedildi mi?)");
            }
            catch (Exception e)
            {
                m_Finished = true;
                Mod.log.Error(e, "Signs+ araç çubuğu sekmesi eklenemedi; levhalar editörden yerleştirilebilir");
            }
        }

        private void GiveUpAfter(int attempts, string why)
        {
            if (m_Attempts < attempts) return;
            m_Finished = true;
            Mod.log.Warn($"Signs+ araç çubuğu: {why}. {m_Done.Count} levha sekmede.");
        }

        private void FindOrCreateGroup()
        {
            var menus = new Dictionary<string, UIAssetMenuPrefab>();
            var entities = m_MenuQuery.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (var e in entities)
                    if (m_PrefabSystem.TryGetPrefab(e, out UIAssetMenuPrefab menu) && menu != null) menus[menu.name] = menu;
            }
            finally { entities.Dispose(); }
            if (menus.Count == 0) return;
            Mod.log.Info("Araç çubuğu menüleri: " + string.Join(", ", menus.Keys));

            foreach (var n in kPreferredMenus)
                if (menus.TryGetValue(n, out m_Menu)) break;
            if (m_Menu == null) foreach (var m in menus.Values) { m_Menu = m; break; }

            // Seçilen menünün mevcut sekmeleri (günlük ve "mevcut sekme" ayarı için)
            var cats = new List<UIAssetCategoryPrefab>();
            var ce = m_CategoryQuery.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (var e in ce)
                    if (m_PrefabSystem.TryGetPrefab(e, out UIAssetCategoryPrefab c) && c != null && c.m_Menu == m_Menu) cats.Add(c);
            }
            finally { ce.Dispose(); }
            var names = new List<string>();
            foreach (var c in cats) names.Add(c.name);
            Mod.log.Info($"\"{m_Menu.name}\" menüsünün sekmeleri: {string.Join(", ", names)}");

            if (Mod.Settings != null && Mod.Settings.ToolbarTab == Setting.TabMode.ExistingTab && cats.Count > 0)
            {
                UIAssetCategoryPrefab pick = null;
                foreach (var c in cats)
                    if (c.name.IndexOf("Service", StringComparison.OrdinalIgnoreCase) >= 0) pick = c;
                m_Group = pick ?? cats[cats.Count - 1];
                Mod.log.Info($"Signs+ levhaları mevcut \"{m_Group.name}\" sekmesine konacak");
                return;
            }

            var cat = ScriptableObject.CreateInstance<UIAssetCategoryPrefab>();
            cat.name = kCategoryName;
            cat.m_Menu = m_Menu;
            var ui = cat.AddComponent<UIObject>();
            ui.m_Icon = kCategoryIcon;
            ui.m_Priority = 9000;
            ui.m_Group = m_Menu;   // oyunun kendi sekmeleri gibi: sekme menünün öğesi
            m_PrefabSystem.AddPrefab(cat);
            m_Group = cat;
            Mod.log.Info($"Signs+ sekmesi \"{m_Menu.name}\" menüsüne eklendi");
        }

        private void PatchSignPrefabs()
        {
            var en = new AssetNameSource();
            var tr = new AssetNameSource();
            var entities = m_ObjectPrefabQuery.ToEntityArray(Allocator.Temp);
            try
            {
                int priority = 0;
                foreach (var e in entities)
                {
                    string name = m_PrefabSystem.GetPrefabName(e);
                    if (string.IsNullOrEmpty(name)) continue;
                    string asset = SignRenderSystem.MatchAsset(name);
                    if (asset == null || m_Done.Contains(asset)) continue;
                    if (!m_PrefabSystem.TryGetPrefab(e, out PrefabBase pb) || pb == null) continue;
                    // Yalnızca yerleştirilecek nesne; "Mesh" (render prefab) olanlar değil
                    if (!(pb is StaticObjectPrefab)) continue;

                    if (!pb.TryGet(out UIObject ui)) ui = pb.AddComponent<UIObject>();
                    ui.m_Group = m_Group;
                    ui.m_Priority = (m_Group != null && m_Group.name == kCategoryName ? 100 : 100000) + priority++;
                    ui.m_Icon = $"coui://ui-mods/SignsPlus-thumb-{asset}.png";
                    if (!pb.TryGet(out PlaceableObject _))
                    {
                        var placeable = pb.AddComponent<PlaceableObject>();
                        placeable.m_ConstructionCost = 0;
                        placeable.m_XPReward = 0;
                    }
                    m_PrefabSystem.UpdatePrefab(pb);
                    m_Done.Add(asset);
                    m_SignPrefabs.Add(pb);
                    if (UiStrings.AssetNamesEN.TryGetValue(asset, out var ne)) { en.Add($"Assets.NAME[{name}]", ne[0]); en.Add($"Assets.DESCRIPTION[{name}]", ne[1]); }
                    if (UiStrings.AssetNamesTR.TryGetValue(asset, out var nt)) { tr.Add($"Assets.NAME[{name}]", nt[0]); tr.Add($"Assets.DESCRIPTION[{name}]", nt[1]); }
                    Mod.log.Info($"Signs+ sekmesine eklendi: {name}");
                }
            }
            finally { entities.Dispose(); }
            try
            {
                var lm = GameManager.instance.localizationManager;
                lm.AddSource("en-US", en);
                lm.AddSource("tr-TR", tr);
            }
            catch (Exception e) { Mod.log.Warn(e, "Signs+ levha adları eklenemedi"); }
        }

        /// <summary>
        /// Oyun sonradan eklenen prefabları tam kurmuyor (UIObject/UIAssetCategory bağlantıları yazılmıyor, sekme menünün
        /// listesine girmiyor). Bu yüzden bağlantılar burada elle kurulur, kilit açılır ve ilk geçişte durum günlüğe yazılır.
        /// Prefab varlıkları UpdatePrefab ile yeniden oluşturulabildiği için her seferinde güncel varlık sorulur.
        /// </summary>
        private void Unlock()
        {
            try
            {
                Entity menuEntity = Entity.Null, groupEntity = Entity.Null;
                if (m_Menu != null) m_PrefabSystem.TryGetEntity(m_Menu, out menuEntity);
                if (m_Group != null) m_PrefabSystem.TryGetEntity(m_Group, out groupEntity);
                bool ownTab = m_Group != null && m_Group.name == kCategoryName;

                // Sekme → menü
                if (ownTab && groupEntity != Entity.Null && menuEntity != Entity.Null)
                {
                    SetUIObject(groupEntity, menuEntity, 9000);
                    if (EntityManager.HasComponent<UIAssetCategoryData>(groupEntity))
                        EntityManager.SetComponentData(groupEntity, new UIAssetCategoryData { m_Menu = menuEntity });
                    AddElement(menuEntity, groupEntity);
                    Open(groupEntity);
                }

                // Levhalar → sekme
                int priority = 0;
                var signs = new List<Entity>();
                foreach (var pb in m_SignPrefabs)
                {
                    if (!m_PrefabSystem.TryGetEntity(pb, out var e) || e == Entity.Null) continue;
                    signs.Add(e);
                    if (groupEntity != Entity.Null)
                    {
                        SetUIObject(e, groupEntity, (ownTab ? 100 : 100000) + priority++);
                        AddElement(groupEntity, e);
                    }
                    if (!EntityManager.HasComponent<PlaceableObjectData>(e))
                        EntityManager.AddComponentData(e, new PlaceableObjectData { m_ConstructionCost = 0, m_XPReward = 0, m_Flags = PlacementFlags.OnGround });
                    Open(e);
                }

                if (m_Diagnose)
                {
                    m_Diagnose = false;
                    var sb = new StringBuilder("Signs+ sekme durumu: ");
                    var all = new List<Entity>(signs);
                    if (groupEntity != Entity.Null) all.Add(groupEntity);
                    foreach (var e in all)
                    {
                        sb.Append(m_PrefabSystem.GetPrefabName(e)).Append(" [");
                        sb.Append(EntityManager.HasComponent<Locked>(e) ? (EntityManager.IsComponentEnabled<Locked>(e) ? "kilitli" : "açık") : "kilitsiz");
                        if (EntityManager.HasComponent<UIObjectData>(e))
                        {
                            var d = EntityManager.GetComponentData<UIObjectData>(e);
                            sb.Append(", grup=").Append(d.m_Group == Entity.Null ? "yok" : m_PrefabSystem.GetPrefabName(d.m_Group));
                        }
                        else sb.Append(", UIObjectData yok");
                        sb.Append(EntityManager.HasComponent<PlaceableObjectData>(e) ? ", yerleştirilebilir" : "");
                        sb.Append("] ");
                    }
                    if (menuEntity != Entity.Null && EntityManager.HasBuffer<UIGroupElement>(menuEntity))
                    {
                        var buf = EntityManager.GetBuffer<UIGroupElement>(menuEntity, true);
                        bool listed = false;
                        for (int i = 0; i < buf.Length; i++) listed |= buf[i].m_Prefab == groupEntity;
                        sb.Append($"| menüde {buf.Length} öğe, Signs+ sekmesi {(listed ? "listede" : "listede DEĞİL")}");
                    }
                    if (groupEntity != Entity.Null && EntityManager.HasBuffer<UIGroupElement>(groupEntity))
                        sb.Append($", sekmede {EntityManager.GetBuffer<UIGroupElement>(groupEntity, true).Length} levha");
                    Mod.log.Info(sb.ToString());
                }
            }
            catch (Exception e)
            {
                m_UnlockPasses = 0;
                Mod.log.Warn(e, "Signs+ sekme bağlantısı kurulamadı");
            }
        }

        private void SetUIObject(Entity e, Entity group, int priority)
        {
            var data = new UIObjectData { m_Group = group, m_Priority = priority };
            if (EntityManager.HasComponent<UIObjectData>(e)) EntityManager.SetComponentData(e, data);
            else EntityManager.AddComponentData(e, data);
        }

        private void AddElement(Entity group, Entity element)
        {
            if (!EntityManager.HasBuffer<UIGroupElement>(group)) return;
            var buf = EntityManager.GetBuffer<UIGroupElement>(group, false);
            for (int i = 0; i < buf.Length; i++) if (buf[i].m_Prefab == element) return;
            buf.Add(new UIGroupElement { m_Prefab = element });
        }

        private void Open(Entity e)
        {
            if (EntityManager.HasComponent<Locked>(e) && EntityManager.IsComponentEnabled<Locked>(e))
                EntityManager.SetComponentEnabled<Locked>(e, false);
        }
    }
}
