using System.Collections.Generic;
using AeroFlow.Core;
using AeroFlow.Sim3D.PipeFlow;
using AeroFlow.Sim3D.RotatingMachinery;
using UnityEngine;
using UnityEngine.Rendering;
using AeroFlow.Rendering;

namespace AeroFlow.Visualization
{
    public class InternalFlowFieldRenderer : MonoBehaviour
    {
        [Header("References")]
        public PipeFlowSimulation3D pipeSimulation;
        public RotatingMachinerySimulation3D machinerySimulation;

        [Header("Quality")]
        [Range(12, 220)] public int lineCount = 96;
        [Range(32, 320)] public int pointsPerLine = 220;
        [Range(0.01f, 0.25f)] public float stepLength = 0.028f;
        [Range(0.03f, 0.30f)] public float updateInterval = 0.10f;
        [Range(0.002f, 0.08f)] public float lineWidth = 0.038f;

        [Header("Machinery Wake")]
        [Range(0f, 2f)] public float machineryWakeSwirl = 1.15f;
        [Range(0f, 2f)] public float machineryWakeTightness = 0.85f;
        [Range(0f, 2f)] public float machineryWakeSpread = 0.42f;

        [Header("Markers")]
        [Range(0.02f, 0.18f)] public float markerThickness = 0.035f;
        [Range(0.04f, 0.25f)] public float markerOpacity = 0.14f;

        private readonly List<LineRenderer> lines = new List<LineRenderer>(128);
        private Gradient velocityGradient;
        private Material lineMaterial;
        private Material markerMaterial;
        private GameObject inletMarker;
        private GameObject outletMarker;
        private float nextUpdateTime;
        private uint rngState = 2166136261u;
        private string activeMode = string.Empty;
        private GameObject cachedMachineryModel;
        private Collider[] machineryColliders;

        private void Awake()
        {
            if (pipeSimulation == null) pipeSimulation = FindAnyObjectByType<PipeFlowSimulation3D>();
            if (machinerySimulation == null) machinerySimulation = FindAnyObjectByType<RotatingMachinerySimulation3D>();
            EnsureGradient();
            EnsureMaterials();
            EnsureMarkers();
        }

        private void OnEnable()
        {
            EnsureMarkers();
            SetLinesVisible(false);
            SetMarkersVisible(false);
        }

        private void Update()
        {
            if (pipeSimulation == null) pipeSimulation = FindAnyObjectByType<PipeFlowSimulation3D>();
            if (machinerySimulation == null) machinerySimulation = FindAnyObjectByType<RotatingMachinerySimulation3D>();

            bool pipeActive = pipeSimulation != null && pipeSimulation.isActiveAndEnabled;
            bool machineryActive = machinerySimulation != null && machinerySimulation.isActiveAndEnabled;
            if (!pipeActive && !machineryActive)
            {
                activeMode = string.Empty;
                SetLinesVisible(false);
                SetMarkersVisible(false);
                return;
            }

            bool renderPipe = pipeActive && !machineryActive;
            string mode = renderPipe ? "pipe" : "machinery";
            if (!string.Equals(activeMode, mode, System.StringComparison.Ordinal))
            {
                ClearLines();
                activeMode = mode;
            }

            if (Time.time < nextUpdateTime)
                return;

            nextUpdateTime = Time.time + Mathf.Max(0.03f, updateInterval);
            if (renderPipe)
            {
                DrawPipeFlow(pipeSimulation);
            }
            else
            {
                DrawMachineryFlow(machinerySimulation);
            }
        }

        private void DrawPipeFlow(PipeFlowSimulation3D simulation)
        {
            if (simulation == null)
                return;
            if (!string.Equals(PipeFlowSimulation3D.NormalizeVisualizationMode(simulation.settings.visualizationMode), PipeFlowSimulation3D.VisualizationStreamlines, System.StringComparison.OrdinalIgnoreCase))
            {
                SetLinesVisible(false);
                SetMarkersVisible(false);
                return;
            }

            Bounds domain = simulation.GetDomainBounds();
            Vector3 flowDir = SafeNormalize(simulation.ResolveFlowDirection(), Vector3.right);
            BuildFrame(flowDir, out Vector3 side, out Vector3 up);
            
            // Try to get a specific inlet from the simulation's detected openings + assignments
            var detected = simulation.GetDetectedOpenings();
            var boundaryManager = simulation.boundaryManager;
            if (boundaryManager == null) boundaryManager = FindAnyObjectByType<BoundaryConditionManager>();

            Vector3 seedCenter = Vector3.zero;
            float seedRadius = 0.4f;
            bool foundInlet = false;

            if (boundaryManager != null && detected != null)
            {
                for (int i = 0; i < Mathf.Min(detected.Count, boundaryManager.Assignments.Count); i++)
                {
                    if (boundaryManager.Assignments[i].type == Sim3D.PipeFlow.BoundaryType.Inlet)
                    {
                        seedCenter = detected[i].position;
                        seedRadius = detected[i].radius;
                        foundInlet = true;
                        break;
                    }
                }
            }

            if (!foundInlet && detected != null && detected.Count > 0)
            {
                seedCenter = detected[0].position;
                seedRadius = detected[0].radius;
                foundInlet = true;
            }

            if (!foundInlet)
            {
                float length = Vector3.Dot(domain.size, Abs(flowDir));
                seedCenter = domain.center - flowDir * (length * 0.48f);
                seedRadius = Mathf.Min(domain.size.x, Mathf.Min(domain.size.y, domain.size.z)) * 0.4f;
            }

            ConfigureMarkers(domain, flowDir, side, up, true, seedRadius);

            bool hasSnapshot = simulation.TryGetVelocityFieldSnapshot(
                out Vector3[] velocities,
                out Vector3 origin,
                out Vector3 cellSize,
                out int sizeX,
                out int sizeY,
                out int sizeZ);

            EnsurePool(lineCount);
            rngState = 2654435761u;
            float inletSpeed = Mathf.Max(0.2f, simulation.settings.inletVelocity);
            float speedScale = Mathf.Max(0.5f, inletSpeed * 1.1f);

            for (int i = 0; i < lineCount; i++)
            {
                Vector2 disk = SampleDisk(i, lineCount);
                Vector3 seed = seedCenter
                    + side * (disk.x * seedRadius * 0.92f)
                    + up * (disk.y * seedRadius * 0.92f);

                DrawLine(
                    lines[i],
                    domain,
                    seed,
                    flowDir,
                    side,
                    up,
                    inletSpeed,
                    speedScale,
                    pos => SamplePipeVelocity(simulation, pos, flowDir, side, up, seedRadius, hasSnapshot, velocities, origin, cellSize, sizeX, sizeY, sizeZ),
                    pos => IsInsidePipe(simulation, pos, flowDir, seedRadius));
            }

            SetLinesVisible(true);
            SetMarkersVisible(true);
        }

        private void DrawMachineryFlow(RotatingMachinerySimulation3D simulation)
        {
            if (simulation == null)
                return;

            string modeInput = simulation.settings != null
                ? simulation.settings.visualizationMode
                : WindTunnelSimulation3D.VisualizationStreamlines;
            string visualizationMode = WindTunnelSimulation3D.NormalizeVisualizationMode(modeInput);
            bool horizontalMode = string.Equals(visualizationMode, WindTunnelSimulation3D.VisualizationHorizontalStreamlines, System.StringComparison.OrdinalIgnoreCase);
            bool verticalMode = string.Equals(visualizationMode, WindTunnelSimulation3D.VisualizationVerticalStreamlines, System.StringComparison.OrdinalIgnoreCase);
            bool streamlinesMode = string.Equals(visualizationMode, WindTunnelSimulation3D.VisualizationStreamlines, System.StringComparison.OrdinalIgnoreCase);
            if (!streamlinesMode && !horizontalMode && !verticalMode)
            {
                SetLinesVisible(false);
                SetMarkersVisible(false);
                return;
            }

            RefreshMachineryColliders();

            Bounds domain = simulation.GetDomainBounds();
            Bounds rotorBounds = GetLoadedModelBoundsOrFallback(domain);
            Vector3 flowDir = SafeNormalize(simulation.ResolveFlowDirection(), Vector3.right);
            BuildFrame(flowDir, out Vector3 side, out Vector3 up);
            float markerRadius = Mathf.Max(
                Mathf.Min(Vector3.Dot(domain.size, Abs(side)), Vector3.Dot(domain.size, Abs(up))) * 0.24f,
                simulation.settings.rotatingZoneRadius * 1.7f);
            ConfigureMarkers(domain, flowDir, side, up, false, markerRadius);
            SetMarkersVisible(false);

            bool hasSnapshot = simulation.TryGetVelocityFieldSnapshot(
                out Vector3[] velocities,
                out Vector3 origin,
                out Vector3 cellSize,
                out int sizeX,
                out int sizeY,
                out int sizeZ);

            float inletSpeed = Mathf.Max(0.2f, simulation.settings.inletVelocity);
            float speedScale = Mathf.Max(0.5f, inletSpeed * 1.10f);
            float flowExtent = Vector3.Dot(domain.size, Abs(flowDir));
            float rotorThickness = Mathf.Max(ProjectSizeAlong(rotorBounds, flowDir), 0.02f);
            Vector3 wakeCenter = rotorBounds.center + flowDir * simulation.settings.rotatingZoneAxisOffset;
            Vector3 seedCenter = rotorBounds.center - flowDir * Mathf.Clamp(rotorThickness * 1.35f, flowExtent * 0.06f, flowExtent * 0.16f);
            float rotorRadius = Mathf.Max(
                ProjectSizeAlong(rotorBounds, side),
                ProjectSizeAlong(rotorBounds, up)) * 0.5f;
            float wakeRadius = Mathf.Max(simulation.settings.rotatingZoneRadius, rotorRadius, 0.05f) * 1.05f;
            float halfWidth = Mathf.Max(Vector3.Dot(domain.extents, Abs(side)) * 0.48f, wakeRadius * 1.05f);
            float halfHeight = Mathf.Max(Vector3.Dot(domain.extents, Abs(up)) * 0.48f, wakeRadius * 1.05f);
            float signedRpm = simulation.settings.angularVelocityRPM * (simulation.settings.rotationDirection == RotatoryRotationDirection.Clockwise ? 1f : -1f);
            float angularRadS = signedRpm * Mathf.PI / 30f;

            EnsurePool(lineCount);
            rngState = 2246822519u;

            if (horizontalMode || verticalMode)
            {
                Vector3 axisA = side;
                Vector3 axisB = horizontalMode ? up : flowDir;
                DrawMachineryPlaneStreamlines(
                    simulation,
                    domain,
                    axisA,
                    axisB,
                    seedCenter,
                    wakeRadius,
                    inletSpeed,
                    speedScale,
                    hasSnapshot,
                    velocities,
                    origin,
                    cellSize,
                    sizeX,
                    sizeY,
                    sizeZ,
                    flowDir,
                    rotorBounds,
                    wakeCenter);
            }
            else
            {
                for (int i = 0; i < lineCount; i++)
                {
                    if (i >= lines.Count)
                        break;

                    Vector2 disk = SampleDisk(i, lineCount);
                    Vector3 diskOffset = side * (disk.x * wakeRadius * 0.92f) + up * (disk.y * wakeRadius * 0.92f);
                    Vector3 seed = seedCenter + diskOffset;
                    seed += side * (Rand01() * 2f - 1f) * halfWidth * 0.03f;
                    seed += up * (Rand01() * 2f - 1f) * halfHeight * 0.03f;

                    DrawLine(
                        lines[i],
                        domain,
                        seed,
                        flowDir,
                        side,
                        up,
                        inletSpeed,
                        speedScale,
                        pos => SampleMachineryWakeVelocity(
                            simulation,
                            pos,
                            wakeCenter,
                            flowDir,
                            flowDir,
                            wakeRadius,
                            angularRadS,
                            hasSnapshot,
                            velocities,
                            origin,
                            cellSize,
                            sizeX,
                            sizeY,
                            sizeZ),
                        pos => !IsInsideMachineryModel(pos) && domain.Contains(pos) && IsWithinMachineryWakeEnvelope(pos, rotorBounds, flowDir, wakeCenter, wakeRadius * 1.55f));
                }
            }

            SetLinesVisible(true);
            SetMarkersVisible(false);
        }

        private void RefreshMachineryColliders()
        {
            GameObject model = RuntimeModelLookup.GetLoadedModel();
            if (model == cachedMachineryModel && machineryColliders != null)
            {
                return;
            }

            cachedMachineryModel = model;
            machineryColliders = null;

            if (model == null) return;

            var colliders = new List<Collider>();
            colliders.AddRange(model.GetComponentsInChildren<Collider>(true));
            if (colliders.Count == 0)
            {
                var filters = model.GetComponentsInChildren<MeshFilter>(true);
                foreach (var filter in filters)
                {
                    if (filter == null || filter.sharedMesh == null) continue;
                    var existing = filter.GetComponent<Collider>();
                    if (existing != null)
                    {
                        colliders.Add(existing);
                        continue;
                    }
                    var meshCollider = filter.gameObject.AddComponent<MeshCollider>();
                    meshCollider.sharedMesh = filter.sharedMesh;
                    meshCollider.convex = false;
                    meshCollider.isTrigger = true;
                    colliders.Add(meshCollider);
                }
            }

            if (colliders.Count == 0) return;
            machineryColliders = colliders.ToArray();
        }

        private bool IsInsideMachineryModel(Vector3 position)
        {
            if (machineryColliders == null) return false;
            for (int i = 0; i < machineryColliders.Length; i++)
            {
                Collider collider = machineryColliders[i];
                if (collider == null || !collider.enabled) continue;
                Vector3 closest = collider.ClosestPoint(position);
                if ((closest - position).sqrMagnitude < 1e-6f)
                {
                    return true;
                }
            }
            return false;
        }

        private void DrawLine(
            LineRenderer line,
            Bounds domain,
            Vector3 seed,
            Vector3 flowDir,
            Vector3 side,
            Vector3 up,
            float inletSpeed,
            float speedScale,
            System.Func<Vector3, Vector3> sampleVelocity,
            System.Func<Vector3, bool> contains)
        {
            if (line == null)
                return;

            line.positionCount = pointsPerLine;
            Vector3 position = seed;
            float speedSum = 0f;
            int used = 0;
            float maxTravel = Vector3.Dot(domain.size, Abs(flowDir)) * 0.98f;
            float travelled = 0f;

            for (int step = 0; step < pointsPerLine; step++)
            {
                if (!contains(position) || !domain.Contains(position))
                    break;

                line.SetPosition(used, position);
                used++;

                Vector3 k1 = sampleVelocity(position);
                float speed = k1.magnitude;
                speedSum += speed;
                if (speed < inletSpeed * 0.03f && used > 12)
                    break;

                float dt = stepLength / Mathf.Max(speed, 0.1f);
                Vector3 midpoint = position + k1 * (dt * 0.5f);
                Vector3 k2 = sampleVelocity(midpoint);
                Vector3 next = position + k2 * dt;
                if (!float.IsFinite(next.x) || !float.IsFinite(next.y) || !float.IsFinite(next.z))
                    break;

                travelled += Vector3.Distance(position, next);
                position = next;
                if (travelled >= maxTravel)
                    break;
                if (used >= pointsPerLine)
                    break;
            }

            if (used < 2)
            {
                line.positionCount = 0;
                line.enabled = false;
                return;
            }

            line.enabled = true;
            line.positionCount = used;
            float avgSpeed = speedSum / Mathf.Max(1, used);
            float speedT = Mathf.Clamp01(avgSpeed / Mathf.Max(0.5f, speedScale));
            Color baseColor = velocityGradient.Evaluate(speedT);
            line.startColor = new Color(baseColor.r, baseColor.g, baseColor.b, 0.92f);
            line.endColor = new Color(baseColor.r * 0.78f, baseColor.g * 0.84f, baseColor.b * 0.90f, 0.20f);
            line.widthMultiplier = Mathf.Lerp(lineWidth * 0.90f, lineWidth * 1.18f, speedT);
        }

        private Vector3 SamplePipeVelocity(
            PipeFlowSimulation3D simulation,
            Vector3 position,
            Vector3 flowDir,
            Vector3 side,
            Vector3 up,
            float pipeRadius,
            bool hasSnapshot,
            Vector3[] velocities,
            Vector3 origin,
            Vector3 cellSize,
            int sizeX,
            int sizeY,
            int sizeZ)
        {
            if (hasSnapshot && TrySampleGrid(position, velocities, origin, cellSize, sizeX, sizeY, sizeZ, out Vector3 sampled)
                && sampled.sqrMagnitude > 1e-6f)
            {
                return sampled;
            }

            Vector3 rel = position - simulation.GetDomainBounds().center;
            float radial = new Vector2(Vector3.Dot(rel, side), Vector3.Dot(rel, up)).magnitude;
            float normalized = Mathf.Clamp01(radial / Mathf.Max(pipeRadius, 1e-4f));
            // Flow regime determined by solver Reynolds — use turbulent profile as default fallback
            float axialSpeed = simulation.settings.inletVelocity * Mathf.Lerp(1.0f, 0.65f, normalized);
            return flowDir * Mathf.Max(0.08f, axialSpeed);
        }

        private Vector3 SampleMachineryVelocity(
            RotatingMachinerySimulation3D simulation,
            Vector3 position,
            Vector3 flowDir,
            Vector3 side,
            Vector3 up,
            bool hasSnapshot,
            Vector3[] velocities,
            Vector3 origin,
            Vector3 cellSize,
            int sizeX,
            int sizeY,
            int sizeZ)
        {
            if (hasSnapshot && TrySampleGrid(position, velocities, origin, cellSize, sizeX, sizeY, sizeZ, out Vector3 sampled)
                && sampled.sqrMagnitude > 1e-6f)
            {
                return sampled;
            }

            Bounds domain = simulation.GetDomainBounds();
            Vector3 rotAxis = simulation.settings.rotationAxis.sqrMagnitude > 1e-6f
                ? simulation.settings.rotationAxis.normalized
                : Vector3.up;
            Vector3 rel = position - domain.center;
            Vector3 radial = rel - rotAxis * Vector3.Dot(rel, rotAxis);
            float radialDistance = radial.magnitude;
            float zoneT = Mathf.Clamp01(1f - radialDistance / Mathf.Max(simulation.settings.rotatingZoneRadius, 0.05f));
            Vector3 swirl = Vector3.zero;
            if (radialDistance > 1e-4f)
            {
                Vector3 tangent = Vector3.Cross(rotAxis, radial).normalized;
                float angularRadS = simulation.settings.angularVelocityRPM * Mathf.PI / 30f;
                swirl = tangent * angularRadS * radialDistance * zoneT * 0.08f;
            }

            Vector3 axial = flowDir * Mathf.Max(0.12f, simulation.settings.inletVelocity);
            return axial + swirl;
        }

        private Vector3 SampleMachineryWakeVelocity(
            RotatingMachinerySimulation3D simulation,
            Vector3 position,
            Vector3 wakeCenter,
            Vector3 flowDir,
            Vector3 wakeAxis,
            float wakeRadius,
            float angularRadS,
            bool hasSnapshot,
            Vector3[] velocities,
            Vector3 origin,
            Vector3 cellSize,
            int sizeX,
            int sizeY,
            int sizeZ)
        {
            Vector3 baseVelocity = SampleMachineryVelocity(
                simulation,
                position,
                flowDir,
                Vector3.right,
                Vector3.up,
                hasSnapshot,
                velocities,
                origin,
                cellSize,
                sizeX,
                sizeY,
                sizeZ);

            Vector3 rel = position - wakeCenter;
            float downstream = Vector3.Dot(rel, flowDir);
            Vector3 radialVec = rel - flowDir * downstream;
            float radialDistance = radialVec.magnitude;
            Vector3 radialDir = radialDistance > 1e-5f ? radialVec / radialDistance : Vector3.right;
            Vector3 tangent = Vector3.Cross(wakeAxis, radialDir).normalized;
            if (tangent.sqrMagnitude < 1e-6f)
            {
                tangent = Vector3.Cross(flowDir, radialDir).normalized;
            }

            float signedRotation = Mathf.Sign(angularRadS);
            float tipBand = Mathf.Clamp01(1f - Mathf.Abs(radialDistance - wakeRadius * 0.82f) / Mathf.Max(wakeRadius * 0.33f, 1e-3f));
            float coreBand = Mathf.Clamp01(1f - radialDistance / Mathf.Max(wakeRadius * 0.34f, 1e-3f));
            float wakeStart = Mathf.InverseLerp(-wakeRadius * 0.35f, wakeRadius * 0.40f, downstream);
            float downstreamFade = Mathf.Exp(-Mathf.Max(0f, downstream) / Mathf.Max(wakeRadius * (1.6f - 0.45f * machineryWakeTightness), 1e-3f));

            float tipSwirl = Mathf.Abs(angularRadS) * wakeRadius * 0.12f * tipBand * wakeStart * downstreamFade * machineryWakeSwirl;
            float coreSwirl = Mathf.Abs(angularRadS) * wakeRadius * 0.05f * coreBand * wakeStart * downstreamFade * machineryWakeSwirl;
            Vector3 swirl = tangent * (tipSwirl + coreSwirl * 0.65f) * signedRotation;

            float inducedAxial = simulation.settings.inletVelocity * (0.18f * tipBand + 0.10f * coreBand) * wakeStart * Mathf.Exp(-Mathf.Max(0f, downstream) / Mathf.Max(wakeRadius * 2.4f, 1e-3f));
            Vector3 axial = flowDir * inducedAxial;

            float radialPulse = Mathf.Sin(downstream * (2.2f + machineryWakeTightness * 1.8f) / Mathf.Max(wakeRadius, 1e-3f));
            Vector3 radialSpread = radialDir * radialPulse * wakeRadius * 0.035f * tipBand * wakeStart * machineryWakeSpread;

            return baseVelocity + swirl + axial + radialSpread;
        }

        private static bool TryGetLoadedModelBounds(out Bounds bounds)
        {
            bounds = default;
            GameObject model = RuntimeModelLookup.GetLoadedModel();
            if (model == null)
                return false;

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            bool initialized = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                if (!initialized)
                {
                    bounds = renderer.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return initialized;
        }

        private static Bounds GetLoadedModelBoundsOrFallback(Bounds fallback)
        {
            return TryGetLoadedModelBounds(out Bounds modelBounds) ? modelBounds : fallback;
        }

        private static bool IsWithinMachineryWakeEnvelope(Vector3 position, Bounds rotorBounds, Vector3 flowDir, Vector3 wakeCenter, float wakeRadius)
        {
            float upstreamAllowance = Mathf.Max(ProjectSizeAlong(rotorBounds, flowDir) * 0.45f, wakeRadius * 0.45f);
            float downstreamAllowance = Mathf.Max(ProjectSizeAlong(rotorBounds, flowDir) * 7.5f, wakeRadius * 7.5f);
            Vector3 rel = position - wakeCenter;
            float downstream = Vector3.Dot(rel, flowDir);
            if (downstream < -upstreamAllowance || downstream > downstreamAllowance)
                return false;

            Vector3 radialVec = rel - flowDir * downstream;
            float radialLimit = Mathf.Lerp(wakeRadius * 1.15f, wakeRadius * 2.45f, Mathf.Clamp01(Mathf.Max(0f, downstream) / Mathf.Max(downstreamAllowance, 1e-3f)));
            return radialVec.magnitude <= radialLimit;
        }

        private static float ProjectSizeAlong(Bounds bounds, Vector3 axis)
        {
            axis = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.right;
            Vector3 extents = bounds.extents;
            Vector3 absAxis = new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
            return 2f * Vector3.Dot(extents, absAxis);
        }

        private bool IsInsidePipe(PipeFlowSimulation3D simulation, Vector3 position, Vector3 flowDir, float pipeRadius)
        {
            Bounds domain = simulation.GetDomainBounds();
            if (!domain.Contains(position))
                return false;

            BuildFrame(flowDir, out Vector3 side, out Vector3 up);
            Vector3 rel = position - domain.center;
            float radial = new Vector2(Vector3.Dot(rel, side), Vector3.Dot(rel, up)).magnitude;
            return radial <= pipeRadius * 1.02f;
        }

        private void ConfigureMarkers(Bounds domain, Vector3 flowDir, Vector3 side, Vector3 up, bool circular, float radiusOrHalfWidth)
        {
            EnsureMarkers();
            if (inletMarker == null || outletMarker == null)
                return;

            float length = Vector3.Dot(domain.size, Abs(flowDir));
            Vector3 inletCenter = domain.center - flowDir * (length * 0.5f - markerThickness * 0.5f);
            Vector3 outletCenter = domain.center + flowDir * (length * 0.5f - markerThickness * 0.5f);
            Quaternion rotation = Quaternion.LookRotation(flowDir, up);

            float width;
            float height;
            if (circular)
            {
                width = radiusOrHalfWidth * 2f;
                height = radiusOrHalfWidth * 2f;
            }
            else
            {
                width = Vector3.Dot(domain.size, Abs(side)) * 0.92f;
                height = Vector3.Dot(domain.size, Abs(up)) * 0.92f;
            }

            inletMarker.transform.SetPositionAndRotation(inletCenter, rotation);
            outletMarker.transform.SetPositionAndRotation(outletCenter, rotation);
            inletMarker.transform.localScale = new Vector3(width, height, markerThickness);
            outletMarker.transform.localScale = new Vector3(width, height, markerThickness);

            Color inletColor = new Color(0.10f, 0.78f, 1.00f, markerOpacity);
            Color outletColor = new Color(1.00f, 0.48f, 0.16f, markerOpacity);
            SetMarkerColor(inletMarker, inletColor);
            SetMarkerColor(outletMarker, outletColor);
        }

        private void EnsurePool(int desiredCount)
        {
            EnsureMaterials();
            while (lines.Count < desiredCount)
            {
                GameObject go = new GameObject($"InternalFlowLine_{lines.Count:000}");
                go.transform.SetParent(transform, false);
                LineRenderer line = go.AddComponent<LineRenderer>();
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                line.textureMode = LineTextureMode.Stretch;
                line.alignment = LineAlignment.View;
                line.useWorldSpace = true;
                line.numCornerVertices = 6;
                line.numCapVertices = 3;
                line.material = lineMaterial;
                line.startWidth = lineWidth;
                line.endWidth = lineWidth * 0.45f;
                line.positionCount = 0;
                line.enabled = false;
                lines.Add(line);
            }

            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i] == null)
                    continue;
                lines[i].startWidth = lineWidth;
                lines[i].endWidth = lineWidth * 0.45f;
            }
        }

        private void DrawMachineryPlaneStreamlines(
            RotatingMachinerySimulation3D simulation,
            Bounds domain,
            Vector3 axisA,
            Vector3 axisB,
            Vector3 seedCenter,
            float radius,
            float inletSpeed,
            float speedScale,
            bool hasSnapshot,
            Vector3[] velocities,
            Vector3 origin,
            Vector3 cellSize,
            int sizeX,
            int sizeY,
            int sizeZ,
            Vector3 flowDir,
            Bounds rotorBounds,
            Vector3 wakeCenter)
        {
            EnsurePool(lineCount);
            rngState = 2654435761u;

            for (int i = 0; i < lineCount; i++)
            {
                if (i >= lines.Count)
                    break;

                Vector2 disk = SampleGrid(i, lineCount);
                Vector3 offset = axisA * (disk.x * radius * 0.92f) + axisB * (disk.y * radius * 0.92f);
                Vector3 seed = seedCenter + offset;

                DrawLine(
                    lines[i],
                    domain,
                    seed,
                    flowDir,
                    axisA,
                    axisB,
                    inletSpeed,
                    speedScale,
                    pos => SampleMachineryWakeVelocity(
                        simulation,
                        pos,
                        wakeCenter,
                        flowDir,
                        flowDir,
                        radius,
                        simulation.settings.angularVelocityRPM * Mathf.PI / 30f,
                        hasSnapshot,
                        velocities,
                        origin,
                        cellSize,
                        sizeX,
                        sizeY,
                        sizeZ),
                    pos => !IsInsideMachineryModel(pos) && domain.Contains(pos) &&
                           IsWithinMachineryWakeEnvelope(pos, rotorBounds, flowDir, wakeCenter, radius * 1.55f));
            }
        }

        private void EnsureMarkers()
        {
            EnsureMaterials();
            inletMarker ??= CreateMarker("InternalFlow_Inlet");
            outletMarker ??= CreateMarker("InternalFlow_Outlet");
        }

        private GameObject CreateMarker(string name)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(transform, false);
            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && markerMaterial != null)
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.sharedMaterial = new Material(markerMaterial);
            }
            else if (renderer != null)
            {
                renderer.sharedMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
            }
            return go;
        }

        private void EnsureMaterials()
        {
            if (lineMaterial == null)
            {
                Shader lineShader = RuntimeShaderResolver.FindLineShader();
                if (lineShader == null)
                {
                    Debug.LogError("[InternalFlow] Line shader not found.");
                    lineMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
                }
                else
                {
                    lineMaterial = new Material(lineShader);
                }

                if (lineMaterial.HasProperty("_Surface")) lineMaterial.SetFloat("_Surface", 1f);
                if (lineMaterial.HasProperty("_Blend")) lineMaterial.SetFloat("_Blend", 0f);
                if (lineMaterial.HasProperty("_Cull")) lineMaterial.SetFloat("_Cull", (float)CullMode.Off);
                if (lineMaterial.HasProperty("_ZWrite")) lineMaterial.SetFloat("_ZWrite", 0f);
                Color baseColor = new Color(0.78f, 1f, 0.88f, 0.95f);
                if (lineMaterial.HasProperty("_BaseColor")) lineMaterial.SetColor("_BaseColor", baseColor);
                if (lineMaterial.HasProperty("_Color")) lineMaterial.SetColor("_Color", baseColor);
                if (lineMaterial.HasProperty("_AlphaClip")) lineMaterial.SetFloat("_AlphaClip", 0f);
                if (lineMaterial.HasProperty("_SrcBlend")) lineMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                if (lineMaterial.HasProperty("_DstBlend")) lineMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                if (lineMaterial.HasProperty("_DashCount")) lineMaterial.SetFloat("_DashCount", 28f);
                if (lineMaterial.HasProperty("_DashRatio")) lineMaterial.SetFloat("_DashRatio", 0.52f);
                if (lineMaterial.HasProperty("_FlowSpeed")) lineMaterial.SetFloat("_FlowSpeed", 2.8f);
                if (lineMaterial.HasProperty("_GlowIntensity")) lineMaterial.SetFloat("_GlowIntensity", 0.9f);
                if (lineMaterial.HasProperty("_FadeStart")) lineMaterial.SetFloat("_FadeStart", 0.55f);
                if (lineMaterial.HasProperty("_FadeEnd")) lineMaterial.SetFloat("_FadeEnd", 0.04f);
                lineMaterial.renderQueue = (int)RenderQueue.Transparent + 12;
            }

            if (markerMaterial == null)
            {
                Shader markerShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                if (markerShader == null)
                {
                    Debug.LogError("[InternalFlow] Marker shader not found.");
                    markerMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
                }
                else
                {
                    markerMaterial = new Material(markerShader);
                }

                if (markerMaterial.HasProperty("_Surface")) markerMaterial.SetFloat("_Surface", 1f);
                if (markerMaterial.HasProperty("_Blend")) markerMaterial.SetFloat("_Blend", 0f);
                if (markerMaterial.HasProperty("_Cull")) markerMaterial.SetFloat("_Cull", (float)CullMode.Off);
                if (markerMaterial.HasProperty("_ZWrite")) markerMaterial.SetFloat("_ZWrite", 0f);
                markerMaterial.renderQueue = (int)RenderQueue.Transparent + 10;
            }
        }

        private void EnsureGradient()
        {
            if (velocityGradient != null)
                return;

            velocityGradient = new Gradient();
            velocityGradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.05f, 0.20f, 0.90f), 0.00f),
                    new GradientColorKey(new Color(0.08f, 0.72f, 1.00f), 0.22f),
                    new GradientColorKey(new Color(0.10f, 0.96f, 0.52f), 0.42f),
                    new GradientColorKey(new Color(0.98f, 0.92f, 0.12f), 0.65f),
                    new GradientColorKey(new Color(1.00f, 0.48f, 0.08f), 0.82f),
                    new GradientColorKey(new Color(0.92f, 0.12f, 0.08f), 1.00f)
                },
                new[]
                {
                    new GradientAlphaKey(0.92f, 0f),
                    new GradientAlphaKey(1.0f, 0.5f),
                    new GradientAlphaKey(0.96f, 1f)
                });
        }

        private bool TrySampleGrid(Vector3 position, Vector3[] velocities, Vector3 origin, Vector3 cellSize, int sizeX, int sizeY, int sizeZ, out Vector3 velocity)
        {
            velocity = Vector3.zero;
            if (velocities == null || sizeX <= 0 || sizeY <= 0 || sizeZ <= 0)
                return false;

            float lx = (position.x - origin.x) / Mathf.Max(cellSize.x, 1e-5f);
            float ly = (position.y - origin.y) / Mathf.Max(cellSize.y, 1e-5f);
            float lz = (position.z - origin.z) / Mathf.Max(cellSize.z, 1e-5f);
            lx = Mathf.Clamp(lx, 0f, sizeX - 1.001f);
            ly = Mathf.Clamp(ly, 0f, sizeY - 1.001f);
            lz = Mathf.Clamp(lz, 0f, sizeZ - 1.001f);

            int x0 = Mathf.FloorToInt(lx);
            int y0 = Mathf.FloorToInt(ly);
            int z0 = Mathf.FloorToInt(lz);
            int x1 = Mathf.Min(x0 + 1, sizeX - 1);
            int y1 = Mathf.Min(y0 + 1, sizeY - 1);
            int z1 = Mathf.Min(z0 + 1, sizeZ - 1);
            float tx = lx - x0;
            float ty = ly - y0;
            float tz = lz - z0;

            Vector3 v000 = velocities[Index(x0, y0, z0, sizeX, sizeY)];
            Vector3 v100 = velocities[Index(x1, y0, z0, sizeX, sizeY)];
            Vector3 v010 = velocities[Index(x0, y1, z0, sizeX, sizeY)];
            Vector3 v110 = velocities[Index(x1, y1, z0, sizeX, sizeY)];
            Vector3 v001 = velocities[Index(x0, y0, z1, sizeX, sizeY)];
            Vector3 v101 = velocities[Index(x1, y0, z1, sizeX, sizeY)];
            Vector3 v011 = velocities[Index(x0, y1, z1, sizeX, sizeY)];
            Vector3 v111 = velocities[Index(x1, y1, z1, sizeX, sizeY)];

            velocity = Vector3.Lerp(
                Vector3.Lerp(Vector3.Lerp(v000, v100, tx), Vector3.Lerp(v010, v110, tx), ty),
                Vector3.Lerp(Vector3.Lerp(v001, v101, tx), Vector3.Lerp(v011, v111, tx), ty),
                tz);
            return float.IsFinite(velocity.x) && float.IsFinite(velocity.y) && float.IsFinite(velocity.z);
        }

        private static int Index(int x, int y, int z, int sizeX, int sizeY)
        {
            return x + sizeX * (y + sizeY * z);
        }

        private Vector2 SampleDisk(int index, int count)
        {
            float golden = 2.39996323f;
            float t = (index + 0.5f) / Mathf.Max(1, count);
            float r = Mathf.Sqrt(t);
            float a = index * golden;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }

        private Vector2 SampleGrid(int index, int count)
        {
            int rows = Mathf.Max(2, Mathf.CeilToInt(Mathf.Sqrt(count)));
            int cols = Mathf.Max(2, Mathf.CeilToInt(count / (float)rows));
            int row = index / cols;
            int col = index % cols;
            float u = rows > 1 ? row / (float)(rows - 1) : 0.5f;
            float v = cols > 1 ? col / (float)(cols - 1) : 0.5f;
            float jitterX = (Rand01() * 2f - 1f) * 0.08f;
            float jitterY = (Rand01() * 2f - 1f) * 0.08f;
            return new Vector2(
                Mathf.Clamp(u * 2f - 1f + jitterX, -1f, 1f),
                Mathf.Clamp(v * 2f - 1f + jitterY, -1f, 1f));
        }

        private float Rand01()
        {
            rngState ^= rngState << 13;
            rngState ^= rngState >> 17;
            rngState ^= rngState << 5;
            return (rngState & 0xFFFFu) / 65535f;
        }

        private void BuildFrame(Vector3 flowDir, out Vector3 side, out Vector3 up)
        {
            Vector3 referenceUp = Mathf.Abs(Vector3.Dot(flowDir, Vector3.up)) > 0.92f ? Vector3.forward : Vector3.up;
            side = Vector3.Cross(referenceUp, flowDir).normalized;
            if (side.sqrMagnitude < 1e-6f)
                side = Vector3.right;
            up = Vector3.Cross(flowDir, side).normalized;
            if (up.sqrMagnitude < 1e-6f)
                up = Vector3.up;
        }

        private static Vector3 SafeNormalize(Vector3 value, Vector3 fallback)
        {
            if (value.sqrMagnitude < 1e-6f)
                return fallback;
            return value.normalized;
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }

        private void SetLinesVisible(bool visible)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i] != null)
                    lines[i].enabled = visible && lines[i].positionCount > 1;
            }
        }

        private void ClearLines()
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i] == null)
                    continue;
                lines[i].positionCount = 0;
                lines[i].enabled = false;
            }
        }

        private void SetMarkersVisible(bool visible)
        {
            if (inletMarker != null) inletMarker.SetActive(visible);
            if (outletMarker != null) outletMarker.SetActive(visible);
        }

        private void SetMarkerColor(GameObject marker, Color color)
        {
            if (marker == null)
                return;

            MeshRenderer renderer = marker.GetComponent<MeshRenderer>();
            if (renderer == null)
                return;

            Material material = renderer.sharedMaterial;
            if (material == null)
                return;

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        }
    }
}
