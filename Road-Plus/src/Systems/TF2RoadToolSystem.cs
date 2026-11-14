// Road+ tool. Three modes:
//   Draw    - TF2 style drawing: click start, move, click to build; the chain continues.
//   Connect - Network Multitool style: pick two existing nodes / road points and join them
//             (straight, smooth, arc or spiral) between their exact heights.
//   Slope   - Move It style: pick two nodes on an existing road and re-grade everything between them.
// Curve and height math lives in Core/ (unit tested); this file talks to the game.
//
// Draw and Connect build through the game's own pipeline (CreationDefinition + NetCourse), so the result is a
// vanilla road and the game's error checks apply (Anarchy can switch them off, see AnarchyCompat).

using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Game.Tools;
using TF2YolAraci.Core;
using TF2YolAraci.UI;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.InputSystem;
using UnityEngine.Scripting;

namespace TF2YolAraci.Systems
{
    public enum ToolMode { Draw, Connect, Slope }

    public partial class TF2RoadToolSystem : ToolBaseSystem
    {
        public const string kToolID = "TF2 Road Tool";
        public override string toolID => kToolID;

        private ToolOutputBarrier m_ToolOutputBarrier;
        private TerrainSystem m_TerrainSystem;
        private OverlayRenderSystem m_OverlayRenderSystem;
        private EntityQuery m_NodeQuery;
        private NetGeometryPrefab m_Prefab;

        // Draw
        private bool m_HasStart;
        private SnapPoint m_Start;
        private Vec3? m_StartDir;
        private float m_HeightOffset;
        private int m_ChainLookupFrames;     // > 0: look for the node the game just built at the chain start

        // Connect / Slope: first pick
        private bool m_HasA;
        private SnapPoint m_A;

        // Slope
        private readonly SlopeEditor m_SlopeEditor = new SlopeEditor();
        private Entity m_CachedPathEnd;
        private SlopePath m_CachedPath;

        public static TF2RoadToolSystem Instance { get; private set; }

        public ToolMode Tool { get; private set; } = ToolMode.Draw;
        public HeightMode Mode { get; private set; } = HeightMode.Terrain;
        public SmoothingLevel Smoothing { get; private set; } = SmoothingLevel.Medium;
        public double Grade { get; private set; } = 0.05;
        public bool TangentLock { get; private set; } = true;
        public ConnectShape Shape { get; private set; } = ConnectShape.Smooth;
        public EvenHeight ConnectHeight { get; private set; } = EvenHeight.Even;
        public EvenHeight SlopeHeight { get; private set; } = EvenHeight.Even;
        public float SpiralRadius { get; private set; } = -1f; // < 0: take the default from the settings
        public bool SpiralFlip { get; private set; }

        public void SetTool(ToolMode tool) { if (Tool != tool) { Tool = tool; ResetAll(); } }
        public void SetMode(HeightMode mode) => Mode = mode;
        public void SetSmoothing(SmoothingLevel level) => Smoothing = level;
        public void SetGrade(double grade) => Grade = grade;
        public void AdjustEndHeight(float delta) => m_HeightOffset += delta;
        public void ResetEndHeight() => m_HeightOffset = 0;
        public void SetShape(ConnectShape shape) => Shape = shape;
        public void SetConnectHeight(EvenHeight h) => ConnectHeight = h == EvenHeight.Level ? EvenHeight.Even : h;
        public void SetSlopeHeight(EvenHeight h) => SlopeHeight = h;
        public void AdjustSpiralRadius(float delta) => SpiralRadius = math.clamp(CurrentSpiralRadius + delta, 15f, 300f);
        public void FlipSpiral() => SpiralFlip = !SpiralFlip;
        public float CurrentSpiralRadius => SpiralRadius > 0 ? SpiralRadius : (Mod.Settings?.SpiralRadiusMeters ?? 60);
        public RoadClass CurrentClass => ClassOf(m_Prefab);
        public bool CanUndo => m_SlopeEditor.CanUndo;
        public void RequestUndo() => m_UndoRequested = true;
        private bool m_UndoRequested;

        private static readonly double[] kGradeSteps = { -0.10, -0.05, 0.05, 0.10 };

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            Instance = this;
            m_ToolOutputBarrier = World.GetOrCreateSystemManaged<ToolOutputBarrier>();
            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_OverlayRenderSystem = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            m_NodeQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Node>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            Enabled = false;
        }

        public override PrefabBase GetPrefab() => m_Prefab;

        public override bool TrySetPrefab(PrefabBase prefab)
        {
            if (IsSupported(prefab)) { m_Prefab = (NetGeometryPrefab)prefab; return true; }
            return false;
        }

        /// <summary>Roads (including Road Builder and asset roads), train / tram / subway tracks and pathways.</summary>
        public static bool IsSupported(PrefabBase prefab) => prefab is RoadPrefab || prefab is TrackPrefab || prefab is PathwayPrefab;

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();
            m_ToolRaycastSystem.typeMask = TypeMask.Terrain | TypeMask.Net;
            m_ToolRaycastSystem.netLayerMask = Tool == ToolMode.Slope ? AllLayers : LayersFor(m_Prefab);
            m_ToolRaycastSystem.collisionMask = CollisionMask.OnGround | CollisionMask.Overground;
        }

        private const Layer AllLayers = Layer.Road | Layer.PublicTransportRoad | Layer.TrainTrack | Layer.TramTrack | Layer.SubwayTrack | Layer.Pathway;

        private static Layer LayersFor(NetGeometryPrefab prefab)
        {
            if (prefab is TrackPrefab track)
            {
                if ((track.m_TrackType & TrackTypes.Tram) != 0) return Layer.TramTrack | Layer.Road;
                if ((track.m_TrackType & TrackTypes.Subway) != 0) return Layer.SubwayTrack;
                return Layer.TrainTrack;
            }
            if (prefab is PathwayPrefab) return Layer.Pathway | Layer.Road;
            return Layer.Road | Layer.PublicTransportRoad;
        }

        [Preserve]
        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            ResetAll();
            applyAction.shouldBeEnabled = true;
            cancelAction.shouldBeEnabled = true;
            ToolStatus.active = true;
            AnarchyCompat.TryRegister(this);
        }

        [Preserve]
        protected override void OnStopRunning()
        {
            applyAction.shouldBeEnabled = false;
            cancelAction.shouldBeEnabled = false;
            ToolStatus.active = false;
            PlannedProfile.Current = null; // never let a stale profile reshape roads drawn with the game's own tool
            base.OnStopRunning();
        }

        [Preserve]
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            HandleKeys();
            applyMode = ApplyMode.Clear; // the preview is rebuilt every frame
            ToolStatus.ClearPlan();
            PlannedProfile.Current = null;
            PublishStatus();

            if (m_UndoRequested)
            {
                m_UndoRequested = false;
                if (m_SlopeEditor.Undo(EntityManager)) Mod.log.Info("Slope undone");
            }
            if (m_Prefab == null) return inputDeps;

            // Right click only ends the current chain / pick; the tool stays on (Shift + R turns it off).
            if (cancelAction.WasPressedThisFrame() && !PointerOverPanel())
            {
                ResetAll();
                return inputDeps;
            }

            JobHandle result;
            switch (Tool)
            {
                case ToolMode.Connect: result = UpdateConnect(inputDeps); break;
                case ToolMode.Slope: result = UpdateSlope(inputDeps); break;
                default: result = UpdateDraw(inputDeps); break;
            }
            PublishStatus();
            return result;
        }

        private bool Clicked() => applyAction.WasPressedThisFrame() && !PointerOverPanel();

        // ---- Draw -----------------------------------------------------------------------------------------------

        private JobHandle UpdateDraw(JobHandle inputDeps)
        {
            if (!GetRaycastResult(out ControlPoint hit))
            {
                DrawMarkers(null, null);
                return inputDeps;
            }
            SnapPoint hover = NetSnap.Resolve(EntityManager, hit, false);

            if (!m_HasStart)
            {
                DrawMarkers(null, hover);
                ToolStatus.message = hover.valid ? (hover.hasOutward ? "Continue this road (its direction is kept)." : "Start on this road.") : "";
                ToolStatus.canBuild = true;
                if (Clicked())
                {
                    m_HasStart = true;
                    m_Start = hover;
                    m_StartDir = hover.hasOutward ? V(hover.outward) : (Vec3?)null;
                    m_HeightOffset = 0;
                    m_ChainLookupFrames = 0;
                }
                return inputDeps;
            }

            ResolveChainNode();

            // Do not connect the end back onto the start node.
            if (hover.valid && hover.entity == m_Start.entity) hover = SnapPoint.Free(hover.position);

            Plan plan = MakeDrawPlan(hover);
            if (plan == null)
            {
                DrawMarkers(m_Start, SnapPoint.Free(hover.position + new float3(0f, m_HeightOffset, 0f)));
                ToolStatus.message = "Turn too sharp. Place a point closer first, or hold Ctrl to release the tangent lock.";
                return inputDeps;
            }

            SnapPoint endMarker = plan.end;
            endMarker.position = F(plan.segments[plan.segments.Count - 1].curve.d);
            DrawMarkers(m_Start, endMarker);

            JobHandle handle = WriteDefinitions(plan, inputDeps);
            bool allowed = CheckErrors(plan);

            if (Clicked() && plan.canBuild && allowed)
            {
                applyMode = ApplyMode.Apply;
                // The chain continues from this end, in this direction.
                if (plan.end.valid && plan.end.isNode) { m_Start = plan.end; m_ChainLookupFrames = 0; }
                else { m_Start = SnapPoint.Free(endMarker.position); m_ChainLookupFrames = 60; }
                m_Start.hasOutward = false;
                m_StartDir = plan.endDirection;
                m_HeightOffset = 0;
            }
            return handle;
        }

        /// <summary>After a piece is built the game creates its end node a frame or two later; link the chain to it.</summary>
        private void ResolveChainNode()
        {
            if (m_ChainLookupFrames <= 0 || m_Start.valid) return;
            m_ChainLookupFrames--;
            Entity node = NetSnap.FindNodeAt(EntityManager, m_NodeQuery, m_Start.position, 0.5f);
            if (node == Entity.Null) return;
            m_Start = new SnapPoint { valid = true, isNode = true, entity = node, position = EntityManager.GetComponentData<Node>(node).m_Position };
            m_ChainLookupFrames = 0;
        }

        private Plan MakeDrawPlan(SnapPoint end)
        {
            RoadSettings settings = Settings();
            RoadClass roadClass = ClassOf(m_Prefab);

            Vec3 start = V(m_Start.position);
            Vec3 target = V(end.position);
            Vec3? dir = TangentLock ? m_StartDir : null; // Ctrl held: sharp corner allowed
            if (!RoadMath.BuildCurve(start, dir, target, settings, out Bezier curve, out Vec3 endDir)) return null;
            var path = new PolyCurve(curve);

            System.Func<double, double, double> terrainAt = TerrainSampler();
            double grade = Mode == HeightMode.Graded ? Grade : 0;
            if (Mode == HeightMode.Graded && !RoadMath.IsGradeAllowed(grade, roadClass, settings))
                grade = System.Math.Sign(grade) * settings.SlopeLimit(roadClass);

            // Ending on an existing road: the end height is that road's height, in every mode.
            double? endHeight = end.valid ? end.position.y : (double?)null;
            List<HeightSample> profile = RoadMath.BuildProfile(path, terrainAt, start.y, endHeight, Mode, grade, Smoothing, roadClass, settings,
                end.valid ? 0 : m_HeightOffset);
            if (end.valid && Mode != HeightMode.Terrain)
            {
                double fix = end.position.y - profile[profile.Count - 1].position.y;
                profile = RoadMath.BuildProfile(path, terrainAt, start.y, endHeight, Mode, grade, Smoothing, roadClass, settings, fix);
            }

            Plan plan = FinishPlan(path, profile, roadClass, settings, m_Start, end);
            plan.endDirection = endDir;
            return plan;
        }

        // ---- Connect --------------------------------------------------------------------------------------------

        private JobHandle UpdateConnect(JobHandle inputDeps)
        {
            if (!GetRaycastResult(out ControlPoint hit))
            {
                DrawMarkers(m_HasA ? (SnapPoint?)m_A : null, null);
                return inputDeps;
            }
            SnapPoint hover = NetSnap.Resolve(EntityManager, hit, false);

            if (!m_HasA)
            {
                DrawMarkers(null, hover);
                ToolStatus.message = hover.valid ? "Click to pick the first end." : "Point at an existing road end or road.";
                ToolStatus.canBuild = hover.valid;
                if (Clicked() && hover.valid) { m_HasA = true; m_A = hover; }
                return inputDeps;
            }

            if (!hover.valid || hover.entity == m_A.entity)
            {
                DrawMarkers(m_A, hover);
                ToolStatus.message = "Point at the second road end or road.";
                return inputDeps;
            }

            RoadSettings settings = Settings();
            RoadClass roadClass = ClassOf(m_Prefab);
            Vec3? startDir = m_A.hasOutward ? V(m_A.outward) : (Vec3?)null;
            Vec3? endDir = hover.hasOutward ? V(-hover.outward) : (Vec3?)null; // arrive against the road that ends there
            double diff = hover.position.y - m_A.position.y;
            double clearance = Mod.Settings?.SpiralClearanceMeters ?? 8;

            if (!ConnectMath.Build(Shape, V(m_A.position), startDir, V(hover.position), endDir, diff, settings.SlopeLimit(roadClass),
                    CurrentSpiralRadius, SpiralFlip, clearance, out PolyCurve path, out SpiralInfo spiral, out string error))
            {
                DrawMarkers(m_A, hover);
                ToolStatus.message = error;
                return inputDeps;
            }

            List<HeightSample> profile = ConnectMath.EvenProfile(path, TerrainSampler(), m_A.position.y, hover.position.y,
                ConnectHeight, Smoothing, roadClass, settings);
            Plan plan = FinishPlan(path, profile, roadClass, settings, m_A, hover);
            if (spiral != null)
            {
                ToolStatus.spiralTurns = spiral.turns;
                if (plan.canBuild)
                    ToolStatus.message = spiral.turns == 0 ? "No loop needed: arc and straight."
                        : spiral.turns + (spiral.turns == 1 ? " loop, " : " loops, ") + spiral.risePerTurn.ToString("0.0", Inv) + " m between loops.";
            }
            else if (!plan.canBuild && Shape != ConnectShape.Spiral)
            {
                ToolStatus.message = "Too steep for this road. Try Spiral, or Terrain height.";
            }

            DrawMarkers(m_A, hover);
            JobHandle handle = WriteDefinitions(plan, inputDeps);
            bool allowed = CheckErrors(plan);
            if (Clicked() && plan.canBuild && allowed)
            {
                applyMode = ApplyMode.Apply;
                m_HasA = false;
            }
            return handle;
        }

        // ---- Slope ----------------------------------------------------------------------------------------------

        private JobHandle UpdateSlope(JobHandle inputDeps)
        {
            OverlayRenderSystem.Buffer overlay = BeginOverlay();
            try
            {
                if (!GetRaycastResult(out ControlPoint hit))
                {
                    if (m_HasA) Circle(overlay, m_A.position, true);
                    ToolStatus.message = m_HasA ? "Point at the last node of the stretch." : "Point at a road.";
                    return inputDeps;
                }
                SnapPoint hover = NetSnap.Resolve(EntityManager, hit, true);

                if (!m_HasA)
                {
                    if (hover.valid) Circle(overlay, hover.position, false);
                    ToolStatus.message = hover.valid ? "Click the first node of the stretch to re-grade." : "Point at a road.";
                    ToolStatus.canBuild = hover.valid;
                    if (Clicked() && hover.valid) { m_HasA = true; m_A = hover; m_CachedPath = null; }
                    return inputDeps;
                }

                Circle(overlay, m_A.position, true);
                if (!hover.valid || hover.entity == m_A.entity)
                {
                    ToolStatus.message = "Point at the last node of the stretch.";
                    return inputDeps;
                }
                Circle(overlay, hover.position, false);

                if (m_CachedPath == null || m_CachedPathEnd != hover.entity)
                {
                    m_CachedPath = SlopeEditor.FindPath(EntityManager, m_A.entity, hover.entity);
                    m_CachedPathEnd = hover.entity;
                }
                SlopePath path = m_CachedPath;
                if (path == null)
                {
                    ToolStatus.message = "These nodes are not connected by roads.";
                    return inputDeps;
                }

                RoadSettings settings = Settings();
                RoadClass roadClass = ClassOfEdge(path.edges[0]);
                float yA = EntityManager.GetComponentData<Node>(m_A.entity).m_Position.y;
                float yB = EntityManager.GetComponentData<Node>(hover.entity).m_Position.y;
                List<HeightSample> profile = ConnectMath.EvenProfile(path.path, TerrainSampler(), yA, yB, SlopeHeight, Smoothing, roadClass, settings);
                SlopeEditor.Plan(path, profile, out List<Bezier4x3> curves, out List<float> heights);

                var gray = new UnityEngine.Color(1f, 1f, 1f, 0.35f);
                var cyan = new UnityEngine.Color(0.35f, 0.85f, 1f, 0.9f);
                for (int i = 0; i < path.edges.Count; i++)
                {
                    overlay.DrawCurve(gray, EntityManager.GetComponentData<Curve>(path.edges[i]).m_Bezier, 2f);
                    overlay.DrawCurve(cyan, curves[i], 1.2f);
                }

                double maxSlope = 0;
                foreach (HeightSample h in profile) maxSlope = System.Math.Max(maxSlope, System.Math.Abs(h.slope));
                ToolStatus.hasPlan = true;
                ToolStatus.length = path.path.Length;
                ToolStatus.maxSlope = maxSlope;
                ToolStatus.slopeLimit = settings.SlopeLimit(roadClass);
                ToolStatus.canBuild = true;
                ToolStatus.message = path.edges.Count + (path.edges.Count == 1 ? " segment" : " segments")
                    + (maxSlope > ToolStatus.slopeLimit + 1e-6 ? ". Steeper than the limit for this road, but allowed here." : ". Click to apply, Ctrl + Z to undo.");
                if (maxSlope > ToolStatus.slopeLimit + 1e-6) ToolStatus.canBuild = false;

                if (Clicked())
                {
                    m_SlopeEditor.Apply(EntityManager, path, curves, heights);
                    Mod.log.Info($"Slope applied to {path.edges.Count} segments ({SlopeHeight})");
                    m_HasA = false;
                    m_CachedPath = null;
                }
                return inputDeps;
            }
            finally
            {
                EndOverlay();
            }
        }

        // ---- Plans ----------------------------------------------------------------------------------------------

        private class Plan
        {
            public List<RoadMath.Segment> segments;
            public Vec3 endDirection;
            public bool canBuild;
            public SnapPoint start, end;
        }

        private Plan FinishPlan(PolyCurve path, List<HeightSample> profile, RoadClass roadClass, RoadSettings settings, SnapPoint start, SnapPoint end)
        {
            List<RoadMath.Segment> segments = RoadMath.Segmentize(path, profile, settings);

            // Only slopes over the limit block building. Below the terrain the game builds a retaining wall,
            // above it a raised road or bridge; those are shown as hints.
            SampleIssue issues = SampleIssue.None;
            double maxSlope = 0, maxAbove = 0, maxBelow = 0;
            foreach (HeightSample p in profile)
            {
                issues |= p.issues;
                maxSlope = System.Math.Max(maxSlope, System.Math.Abs(p.slope));
                double diff = p.position.y - p.terrain;
                maxAbove = System.Math.Max(maxAbove, diff);
                maxBelow = System.Math.Max(maxBelow, -diff);
            }
            bool canBuild = (issues & SampleIssue.TooSteep) == 0;

            ToolStatus.hasPlan = true;
            ToolStatus.length = path.Length;
            ToolStatus.maxSlope = maxSlope;
            ToolStatus.slopeLimit = settings.SlopeLimit(roadClass);
            ToolStatus.canBuild = canBuild;
            double earth = settings.earthworkMax;
            ToolStatus.message =
                !canBuild ? "Too steep to build. Increase smoothing or make the road longer."
                : maxAbove > earth ? "Road is up to " + maxAbove.ToString("0", Inv) + " m above the terrain: the game builds a raised road or bridge."
                : maxBelow > earth ? "Road is up to " + maxBelow.ToString("0", Inv) + " m below the terrain: the game builds a retaining wall or tunnel."
                : (maxAbove > 0.5 || maxBelow > 0.5) ? "The terrain will be sloped to fit the road (fill / cut)."
                : "Ready";

            PlannedProfile.Current = new PlannedProfile(m_PrefabSystem.GetEntity(m_Prefab), profile, (float)settings.earthworkMax);
            return new Plan { segments = segments, canBuild = canBuild, start = start, end = end };
        }

        /// <summary>The game marks problems (overlaps, too close to buildings ...) on the preview; respect them like its own tool.</summary>
        private bool CheckErrors(Plan plan)
        {
            if (GetAllowApply()) return true;
            ToolStatus.canBuild = false;
            if (plan.canBuild)
                ToolStatus.message = "The game reports a problem here (red preview). Anarchy can turn these checks off.";
            return false;
        }

        private RoadSettings Settings() => Mod.Settings?.ToRoadSettings() ?? new RoadSettings();

        private System.Func<double, double, double> TerrainSampler()
        {
            TerrainHeightData heights = m_TerrainSystem.GetHeightData();
            return (x, z) => TerrainUtils.SampleHeight(ref heights, new float3((float)x, 0f, (float)z));
        }

        private void PublishStatus()
        {
            ToolStatus.tool = Tool;
            ToolStatus.roadName = m_Prefab != null ? m_Prefab.name : "";
            ToolStatus.roadClass = ClassOf(m_Prefab);
            RoadSettings rs = Settings();
            ToolStatus.allowedGrades = rs.AllowedGrades(ToolStatus.roadClass);
            ToolStatus.slopeLimitForClass = rs.SlopeLimit(ToolStatus.roadClass);
            ToolStatus.mode = Mode;
            ToolStatus.smoothing = Smoothing;
            ToolStatus.grade = Grade;
            ToolStatus.heightOffset = m_HeightOffset;
            ToolStatus.tangentLock = TangentLock;
            ToolStatus.hasStart = Tool == ToolMode.Draw ? m_HasStart : m_HasA;
            ToolStatus.shape = Shape;
            ToolStatus.connectHeight = ConnectHeight;
            ToolStatus.slopeHeight = SlopeHeight;
            ToolStatus.spiralRadius = CurrentSpiralRadius;
            ToolStatus.spiralFlip = SpiralFlip;
            ToolStatus.canUndo = m_SlopeEditor.CanUndo;
        }

        // ---- Overlay --------------------------------------------------------------------------------------------

        private OverlayRenderSystem.Buffer BeginOverlay()
        {
            OverlayRenderSystem.Buffer buffer = m_OverlayRenderSystem.GetBuffer(out JobHandle deps);
            deps.Complete();
            return buffer;
        }

        private void EndOverlay() => m_OverlayRenderSystem.AddBufferWriter(default(JobHandle));

        private static void Circle(OverlayRenderSystem.Buffer buffer, float3 pos, bool start)
        {
            var orange = new UnityEngine.Color(1f, 0.6f, 0.1f, 1f);
            var orangeFill = new UnityEngine.Color(1f, 0.6f, 0.1f, 0.35f);
            var white = new UnityEngine.Color(1f, 1f, 1f, 0.9f);
            var whiteFill = new UnityEngine.Color(1f, 1f, 1f, 0.15f);
            if (start) buffer.DrawCircle(orange, orangeFill, 0.6f, 0, new float2(0f, 1f), pos, 8f);
            else buffer.DrawCircle(white, whiteFill, 0.4f, 0, new float2(0f, 1f), pos, 5f);
        }

        /// <summary>Orange circle at the start, white circle at the cursor; a snapped cursor gets a cyan ring.</summary>
        private void DrawMarkers(SnapPoint? start, SnapPoint? hover)
        {
            OverlayRenderSystem.Buffer buffer = BeginOverlay();
            if (start.HasValue) Circle(buffer, start.Value.position, true);
            if (hover.HasValue)
            {
                Circle(buffer, hover.Value.position, false);
                if (hover.Value.valid)
                {
                    var cyan = new UnityEngine.Color(0.35f, 0.85f, 1f, 1f);
                    buffer.DrawCircle(cyan, new UnityEngine.Color(0f, 0f, 0f, 0f), 0.5f, 0, new float2(0f, 1f), hover.Value.position, 10f);
                }
            }
            EndOverlay();
        }

        // ---- Road classes ---------------------------------------------------------------------------------------

        /// <summary>Highway, ramp (single-lane one-way highway or "ramp" in the name), rail, or other.</summary>
        public static RoadClass ClassOf(NetGeometryPrefab prefab)
        {
            if (prefab is TrackPrefab track) return (track.m_TrackType & TrackTypes.Tram) != 0 ? RoadClass.Other : RoadClass.Rail;
            if (!(prefab is RoadPrefab road) || !road.m_HighwayRules) return RoadClass.Other;
            string name = road.name.ToLowerInvariant();
            if (name.Contains("ramp") || (name.Contains("oneway") && name.Contains("1 lane"))) return RoadClass.Ramp;
            return RoadClass.Highway;
        }

        private RoadClass ClassOfEdge(Entity edge)
        {
            if (!EntityManager.HasComponent<PrefabRef>(edge)) return RoadClass.Other;
            Entity prefabEntity = EntityManager.GetComponentData<PrefabRef>(edge).m_Prefab;
            return m_PrefabSystem.TryGetPrefab(prefabEntity, out NetGeometryPrefab prefab) ? ClassOf(prefab) : RoadClass.Other;
        }

        private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        private static bool PointerOverPanel()
        {
            if (ToolStatus.pointerOverPanel) return true;
            Mouse mouse = Mouse.current;
            if (mouse == null || ToolStatus.panelRect.width <= 0) return false;
            UnityEngine.Vector2 p = mouse.position.ReadValue();
            return ToolStatus.panelRect.Contains(new UnityEngine.Vector2(p.x, UnityEngine.Screen.height - p.y));
        }

        // ---- Definitions for the game's preview / build pipeline ------------------------------------------------

        private JobHandle WriteDefinitions(Plan plan, JobHandle inputDeps)
        {
            Entity prefabEntity = m_PrefabSystem.GetEntity(m_Prefab);
            EntityCommandBuffer ecb = m_ToolOutputBarrier.CreateCommandBuffer();
            TerrainHeightData heights = m_TerrainSystem.GetHeightData();

            for (int i = 0; i < plan.segments.Count; i++)
            {
                Bezier4x3 curve = ToGame(plan.segments[i].curve);
                bool first = i == 0, last = i == plan.segments.Count - 1;
                if (first && plan.start.valid) curve.a = plan.start.position;
                if (last && plan.end.valid) curve.d = plan.end.position;

                CoursePos startPos = MakeCoursePos(curve, 0f, first ? CoursePosFlags.IsFirst : 0, ref heights);
                CoursePos endPos = MakeCoursePos(curve, 1f, last ? CoursePosFlags.IsLast : 0, ref heights);
                if (first && plan.start.valid) { startPos.m_Entity = plan.start.entity; startPos.m_SplitPosition = plan.start.split; }
                if (last && plan.end.valid) { endPos.m_Entity = plan.end.entity; endPos.m_SplitPosition = plan.end.split; }

                Entity e = ecb.CreateEntity();
                ecb.AddComponent(e, new CreationDefinition
                {
                    m_Prefab = prefabEntity,
                    m_RandomSeed = UnityEngine.Random.Range(0, int.MaxValue),
                });
                ecb.AddComponent(e, new NetCourse
                {
                    m_Curve = curve,
                    m_StartPosition = startPos,
                    m_EndPosition = endPos,
                    m_Length = MathUtils.Length(curve),
                    m_FixedIndex = -1,
                });
                ecb.AddComponent(e, default(Updated));
            }

            m_ToolOutputBarrier.AddJobHandleForProducer(inputDeps);
            return inputDeps;
        }

        private static CoursePos MakeCoursePos(Bezier4x3 curve, float t, CoursePosFlags flags, ref TerrainHeightData heights)
        {
            float3 pos = MathUtils.Position(curve, t);
            float3 tangent = MathUtils.Tangent(curve, t);
            float elevation = pos.y - TerrainUtils.SampleHeight(ref heights, pos);
            return new CoursePos
            {
                m_Position = pos,
                m_Rotation = NetUtils.GetNodeRotation(tangent),
                m_CourseDelta = t,
                m_Elevation = new float2(elevation),
                m_Flags = flags,
                m_ParentMesh = -1,
            };
        }

        private static Bezier4x3 ToGame(Bezier b) => new Bezier4x3(F(b.a), F(b.b), F(b.c), F(b.d));
        private static float3 F(Vec3 v) => new float3((float)v.x, (float)v.y, (float)v.z);
        private static Vec3 V(float3 f) => new Vec3(f.x, f.y, f.z);

        // ---- Keys -----------------------------------------------------------------------------------------------

        private void HandleKeys()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            bool shift = kb.shiftKey.isPressed, ctrl = kb.ctrlKey.isPressed;

            if (kb.tabKey.wasPressedThisFrame)
            {
                switch (Tool)
                {
                    case ToolMode.Draw:
                        if (shift) Smoothing = (SmoothingLevel)(((int)Smoothing + 1) % 3);
                        else Mode = (HeightMode)(((int)Mode + 1) % 3);
                        break;
                    case ToolMode.Connect:
                        if (shift) ConnectHeight = ConnectHeight == EvenHeight.Even ? EvenHeight.Terrain : EvenHeight.Even;
                        else Shape = (ConnectShape)(((int)Shape + 1) % 4);
                        break;
                    default:
                        SlopeHeight = (EvenHeight)(((int)SlopeHeight + 1) % 3);
                        break;
                }
            }

            if (Tool == ToolMode.Draw)
            {
                if (kb.rightBracketKey.wasPressedThisFrame) StepGrade(+1);
                if (kb.leftBracketKey.wasPressedThisFrame) StepGrade(-1);
            }

            float step = shift ? 0.25f : 1f;
            if (Tool == ToolMode.Draw)
            {
                if (kb.pageUpKey.wasPressedThisFrame) m_HeightOffset += step;
                if (kb.pageDownKey.wasPressedThisFrame) m_HeightOffset -= step;
            }
            else if (Tool == ToolMode.Connect && Shape == ConnectShape.Spiral)
            {
                if (kb.pageUpKey.wasPressedThisFrame) AdjustSpiralRadius(5f);
                if (kb.pageDownKey.wasPressedThisFrame) AdjustSpiralRadius(-5f);
            }

            if (ctrl && kb.zKey.wasPressedThisFrame) m_UndoRequested = true;
            TangentLock = !ctrl;
        }

        private void StepGrade(int direction)
        {
            int i = System.Array.IndexOf(kGradeSteps, Grade);
            if (i < 0) i = 2;
            i = math.clamp(i + direction, 0, kGradeSteps.Length - 1);
            Grade = kGradeSteps[i];
        }

        private void ResetAll()
        {
            m_HasStart = false;
            m_StartDir = null;
            m_HeightOffset = 0;
            m_ChainLookupFrames = 0;
            m_HasA = false;
            m_CachedPath = null;
        }
    }
}
