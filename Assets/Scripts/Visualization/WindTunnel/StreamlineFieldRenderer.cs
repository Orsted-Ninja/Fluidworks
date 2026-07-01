using System.Collections.Generic;
using AeroFlow.Physics;
using UnityEngine;
using UnityEngine.Rendering;
using AeroFlow.Core;
using AeroFlow.Rendering;

namespace AeroFlow.Visualization
{
    /// <summary>
    /// High-quality streamline renderer for the wind tunnel.
    /// Solver velocity is preferred, with an analytical body/wake fallback that
    /// keeps the flow readable around the loaded model.
    /// </summary>
    public class StreamlineFieldRenderer : MonoBehaviour
    {
        [Header("References")]
        public WindTunnelSimulation3D windTunnel;
        public NavierStokesGridSolver navier;
        public Transform inletPlane;
        public Transform outletPlane;

        [Header("Quality")]
        [Range(8, 300)] public int maxLineCount = 160;
        [Range(32, 800)] public int pointsPerLine = 500;
        [Range(0.005f, 0.5f)] public float stepLength = 0.028f;
        [Range(0.02f, 0.3f)] public float updateInterval = 0.08f;
        [Range(0.002f, 0.08f)] public float lineWidth = 0.038f;
        [Range(0.1f, 3f)] public float colorVelocityRangeScale = 1.1f;

        [Header("Flow Animation")]
        [Range(0f, 5f)] public float flowSpeed = 2.2f;
        [Range(0f, 0.4f)] public float seedJitter = 0.06f;

        [Header("Obstacle Interaction")]
        [Tooltip("Leave false to show solver streamlines directly, including intersections with the windshield.")]
        public bool enforceStreamlineClearance = false;

        [Header("Style")]
        public Gradient velocityGradient;

        private readonly List<LineRenderer> lines = new List<LineRenderer>(200);
        private readonly Vector3[] obstacleProbeDirections = new Vector3[12];
        private float nextUpdateTime;
        private float flowPhase;
        private uint rngState = 12345u;
        private Material lineMaterial;
        private string lastVisualizationMode;

        private Vector3[] velCache;
        private int cachedSX;
        private int cachedSY;
        private int cachedSZ;
        private Vector3 cachedOrigin;
        private Vector3 cachedCellSize;
        private bool snapshotReady;

        private Bounds modelBoundsCache;
        private bool hasModelBounds;
        private float nextModelBoundsRefreshTime = -999f;

        private void Awake()
        {
            if (windTunnel == null) windTunnel = FindAnyObjectByType<WindTunnelSimulation3D>();
            FindPortals();
            EnsureGradient();
            EnsureMaterial();

            var legacy = GetComponent<WindTunnelStreamlineRenderer>();
            if (legacy != null && legacy.enabled)
            {
                legacy.enabled = false;
            }
        }

        private void OnEnable()
        {
            EnsureMaterial();
            SetAllLinesVisible(false);
        }

        private void OnDisable()
        {
            SetAllLinesVisible(false);
        }

        private void OnDestroy()
        {
            ClearAllLines();
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i] != null && lines[i].gameObject != null)
                {
                    Destroy(lines[i].gameObject);
                }
            }
            lines.Clear();
        }

        public void ApplyVisualizationMode(string mode)
        {
            string normalized = WindTunnelSimulation3D.NormalizeVisualizationMode(mode);
            bool show = string.Equals(normalized, WindTunnelSimulation3D.VisualizationStreamlines, System.StringComparison.OrdinalIgnoreCase)
                     || string.Equals(normalized, WindTunnelSimulation3D.VisualizationVelocity, System.StringComparison.OrdinalIgnoreCase)
                     || string.Equals(normalized, WindTunnelSimulation3D.VisualizationEffects, System.StringComparison.OrdinalIgnoreCase)
                     || string.Equals(normalized, "Smoke", System.StringComparison.OrdinalIgnoreCase)
                     || string.Equals(normalized, "Impact Lines", System.StringComparison.OrdinalIgnoreCase)
                     || string.Equals(normalized, WindTunnelSimulation3D.VisualizationVerticalStreamlines, System.StringComparison.OrdinalIgnoreCase)
                     || string.Equals(normalized, WindTunnelSimulation3D.VisualizationHorizontalStreamlines, System.StringComparison.OrdinalIgnoreCase);
            if (!show)
            {
                ClearAllLines();
                SetAllLinesVisible(false);
            }
        }

        private void Update()
        {
            if (windTunnel == null) windTunnel = FindAnyObjectByType<WindTunnelSimulation3D>();
            if (windTunnel == null)
            {
                SetAllLinesVisible(false);
                return;
            }

            if (navier == null) navier = windTunnel.navierStokesSolver;
            if (inletPlane == null || outletPlane == null)
            {
                FindPortals();
            }

            if (windTunnel.IsPaused)
            {
                SetAllLinesVisible(false);
                return;
            }

            RefreshVelocitySnapshot();
            RefreshModelBounds();

            string viz = windTunnel.settings.visualizationMode;
            if (!string.Equals(lastVisualizationMode, viz, System.StringComparison.Ordinal))
            {
                ClearAllLines();
                lastVisualizationMode = viz;
            }
            bool effectsMode = string.Equals(viz, WindTunnelSimulation3D.VisualizationEffects, System.StringComparison.OrdinalIgnoreCase);
            bool verticalMode = string.Equals(viz, WindTunnelSimulation3D.VisualizationVerticalStreamlines, System.StringComparison.OrdinalIgnoreCase);
            bool horizontalMode = string.Equals(viz, WindTunnelSimulation3D.VisualizationHorizontalStreamlines, System.StringComparison.OrdinalIgnoreCase);
            bool smokeMode = string.Equals(viz, "Smoke", System.StringComparison.OrdinalIgnoreCase);
            bool impactMode = string.Equals(viz, "Impact Lines", System.StringComparison.OrdinalIgnoreCase);
            bool show = string.Equals(viz, "Streamlines", System.StringComparison.OrdinalIgnoreCase)
                     || string.Equals(viz, "Velocity", System.StringComparison.OrdinalIgnoreCase)
                     || effectsMode
                     || smokeMode
                     || impactMode
                     || verticalMode
                     || horizontalMode;
            if (!show)
            {
                SetAllLinesVisible(false);
                return;
            }

            float effectiveUpdateInterval = GetEffectiveUpdateInterval(viz, effectsMode, verticalMode || horizontalMode);
            if (Time.time < nextUpdateTime) return;
            nextUpdateTime = Time.time + effectiveUpdateInterval;

            flowPhase += flowSpeed * effectiveUpdateInterval;

            int desiredLines = hasModelBounds
                ? windTunnel.GetClampedStreamlineDensity()
                : Mathf.Min(windTunnel.GetClampedStreamlineDensity(), 48);
            if (effectsMode)
            {
                desiredLines = hasModelBounds
                    ? Mathf.Clamp(Mathf.RoundToInt(windTunnel.GetClampedStreamlineDensity() * 0.55f), 56, 140)
                    : 0;
            }
            else if (smokeMode || impactMode)
            {
                desiredLines = hasModelBounds
                    ? Mathf.Clamp(Mathf.RoundToInt(windTunnel.GetClampedStreamlineDensity() * 2.2f), 80, 480)
                    : 0;
            }
            else if (verticalMode || horizontalMode)
            {
                desiredLines = hasModelBounds
                    ? Mathf.Clamp(Mathf.RoundToInt(windTunnel.GetClampedStreamlineDensity() * 0.65f), 40, 200)
                    : Mathf.Min(windTunnel.GetClampedStreamlineDensity(), 64);
            }
            else if (string.Equals(viz, "Velocity", System.StringComparison.OrdinalIgnoreCase))
            {
                desiredLines = hasModelBounds
                    ? Mathf.Min(desiredLines, 160)
                    : Mathf.Min(desiredLines, 48);
            }

            if ((effectsMode || smokeMode || impactMode) && !hasModelBounds)
            {
                SetAllLinesVisible(false);
                return;
            }

            desiredLines = GetBudgetedLineCount(desiredLines, viz, effectsMode, verticalMode || horizontalMode);
            maxLineCount = Mathf.Max(maxLineCount, desiredLines);

            EnsurePool(desiredLines);
            if (effectsMode)
            {
                DrawStylizedEffects(desiredLines);
            }
            else if (smokeMode || impactMode)
            {
                DrawStreamlines(desiredLines, smokeMode, impactMode);
            }
            else if (verticalMode)
            {
                DrawPlaneStreamlines(desiredLines, true);
            }
            else if (horizontalMode)
            {
                DrawPlaneStreamlines(desiredLines, false);
            }
            else
            {
                DrawStreamlines(desiredLines, false, false);
            }
        }

        private void RefreshVelocitySnapshot()
        {
            snapshotReady = false;
            if (navier == null) return;
            if (!navier.TryGetVelocityFieldSnapshot(
                    out var v, out var origin, out var cellSize,
                    out int sx, out int sy, out int sz, out _))
            {
                return;
            }

            int gridCount = sx * sy * sz;
            if (gridCount <= 0) return;
            velCache = v;
            cachedSX = sx;
            cachedSY = sy;
            cachedSZ = sz;
            cachedOrigin = origin;
            cachedCellSize = cellSize;
            snapshotReady = true;
        }

        private float GetEffectiveUpdateInterval(string viz, bool effectsMode, bool planeMode)
        {
            float interval = Mathf.Max(0.03f, updateInterval);
            if (windTunnel == null || !windTunnel.settings.keepPerformanceBudget)
            {
                return interval;
            }

            if (effectsMode)
            {
                return Mathf.Max(interval, 0.12f);
            }

            if (planeMode || string.Equals(viz, WindTunnelSimulation3D.VisualizationVelocity, System.StringComparison.OrdinalIgnoreCase))
            {
                return Mathf.Max(interval, 0.10f);
            }

            return Mathf.Max(interval, 0.09f);
        }

        private int GetBudgetedLineCount(int desiredLines, string viz, bool effectsMode, bool planeMode)
        {
            if (windTunnel == null || !windTunnel.settings.keepPerformanceBudget)
            {
                return desiredLines;
            }

            int budgetCap;
            if (effectsMode)
            {
                budgetCap = 96;
            }
            else if (string.Equals(viz, "Smoke", System.StringComparison.OrdinalIgnoreCase) || string.Equals(viz, "Impact Lines", System.StringComparison.OrdinalIgnoreCase))
            {
                budgetCap = 280;
            }
            else if (planeMode)
            {
                budgetCap = 120;
            }
            else if (string.Equals(viz, WindTunnelSimulation3D.VisualizationVelocity, System.StringComparison.OrdinalIgnoreCase))
            {
                budgetCap = 110;
            }
            else
            {
                budgetCap = 128;
            }

            return Mathf.Max(24, Mathf.Min(desiredLines, budgetCap));
        }

        private void RefreshModelBounds()
        {
            if (Time.time < nextModelBoundsRefreshTime) return;
            nextModelBoundsRefreshTime = Time.time + 0.35f;

            if (windTunnel != null && windTunnel.TryGetLoadedModelBounds(out Bounds bounds))
            {
                modelBoundsCache = bounds;
                hasModelBounds = true;
                return;
            }

            hasModelBounds = false;
            GameObject model = RuntimeModelLookup.GetLoadedModel();
            if (model == null) return;

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                if (renderer.GetComponentInParent<AeroFlow.Core.RuntimeSimulationProxy>() != null) continue;

                if (!hasModelBounds)
                {
                    modelBoundsCache = renderer.bounds;
                    hasModelBounds = true;
                }
                else
                {
                    modelBoundsCache.Encapsulate(renderer.bounds);
                }
            }
        }

        private float Rand01()
        {
            rngState ^= rngState << 13;
            rngState ^= rngState >> 17;
            rngState ^= rngState << 5;
            return (rngState & 0xFFFFu) / 65535f;
        }

        private void DrawStreamlines(int lineCount, bool smokeMode = false, bool impactMode = false)
        {
            // Stable seed — avoids per-frame flicker by keeping seed positions coherent.
            rngState = 2654435761u;

            Bounds tunnelBounds = windTunnel.GetTunnelBounds();
            if (tunnelBounds.size.x < 0.1f || tunnelBounds.size.y < 0.1f || tunnelBounds.size.z < 0.1f)
            {
                tunnelBounds = new Bounds(windTunnel.transform.position, new Vector3(10f, 4f, 5f));
            }

            Vector3 dir = ResolveWindDir().normalized;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.right;
            BuildFlowFrame(dir, out Vector3 side, out Vector3 up);
            bool pressureMode = string.Equals(windTunnel.settings.visualizationMode, "Pressure", System.StringComparison.OrdinalIgnoreCase);

            float vinf = Mathf.Max(0.5f, windTunnel.settings.inletVelocity);
            float speedNormDenom = Mathf.Max(0.5f, vinf * Mathf.Max(0.1f, colorVelocityRangeScale));

            float tunnelLength = Vector3.Dot(tunnelBounds.size, Abs(dir));
            float tunnelHalfSide = Vector3.Dot(tunnelBounds.extents, Abs(side)) * 0.92f;
            float tunnelHalfHeight = Vector3.Dot(tunnelBounds.extents, Abs(up)) * 0.90f;
            Vector3 inletCenter = GetInletCenter(tunnelBounds, dir, tunnelLength);

            float centerSide = 0f;
            float centerVertical = 0f;
            float bodyHalfLength = Mathf.Max(tunnelLength * 0.10f, 0.20f);
            float bodyHalfSide = tunnelHalfSide * 0.40f;
            float bodyHalfHeight = tunnelHalfHeight * 0.35f;
            if (hasModelBounds)
            {
                centerSide = Vector3.Dot(modelBoundsCache.center - tunnelBounds.center, side);
                centerVertical = Vector3.Dot(modelBoundsCache.center - tunnelBounds.center, up);
                bodyHalfLength = Mathf.Clamp(ProjectHalfExtent(modelBoundsCache, dir) * 1.4f, tunnelLength * 0.05f, tunnelLength * 0.22f);
                bodyHalfSide = Mathf.Clamp(ProjectHalfExtent(modelBoundsCache, side) * 2.1f, tunnelHalfSide * 0.14f, tunnelHalfSide * 0.70f);
                bodyHalfHeight = Mathf.Clamp(ProjectHalfExtent(modelBoundsCache, up) * 1.9f, tunnelHalfHeight * 0.14f, tunnelHalfHeight * 0.70f);
            }

            Vector3 bodySeedOrigin = inletCenter;
            if (hasModelBounds)
            {
                float upstreamOffset = Mathf.Clamp(bodyHalfLength * 1.55f, tunnelLength * 0.06f, tunnelLength * 0.18f);
                bodySeedOrigin = modelBoundsCache.center - dir * upstreamOffset;
            }

            int focusCount = hasModelBounds && !smokeMode
                ? (pressureMode ? lineCount : Mathf.Clamp(Mathf.RoundToInt(lineCount * 0.35f), 0, lineCount))
                : (smokeMode ? lineCount : 0);
            int rows = Mathf.Max(2, Mathf.CeilToInt(Mathf.Sqrt(lineCount)));
            int cols = Mathf.Max(2, Mathf.CeilToInt(lineCount / (float)rows));

            for (int i = 0; i < lineCount; i++)
            {
                int row = i / cols;
                int col = i % cols;
                float u = rows > 1 ? row / (float)(rows - 1) : 0.5f;
                float v = cols > 1 ? col / (float)(cols - 1) : 0.5f;
                float normalizedY = u * 2f - 1f;
                float normalizedZ = v * 2f - 1f;

                float spanSide;
                float spanUp;
                float seedSideCenter;
                float seedUpCenter;

                if (i < focusCount)
                {
                    spanSide = pressureMode ? bodyHalfSide * 0.72f : bodyHalfSide;
                    spanUp = pressureMode ? bodyHalfHeight * 0.68f : bodyHalfHeight;
                    seedSideCenter = centerSide;
                    seedUpCenter = centerVertical;
                }
                else if (hasModelBounds)
                {
                    spanSide = tunnelHalfSide * 0.88f;
                    spanUp = tunnelHalfHeight * 0.85f;
                    seedSideCenter = 0f;
                    seedUpCenter = 0f;
                }
                else
                {
                    spanSide = tunnelHalfSide;
                    spanUp = tunnelHalfHeight;
                    seedSideCenter = 0f;
                    seedUpCenter = 0f;
                }
                
                if (smokeMode)
                {
                    spanSide = bodyHalfSide * 3.5f;
                    spanUp = bodyHalfHeight * 3.5f;
                }

                float jitterSide = (Rand01() * 2f - 1f) * seedJitter * spanSide;
                float jitterUp = (Rand01() * 2f - 1f) * seedJitter * spanUp;
                Vector3 seedBase = i < focusCount ? bodySeedOrigin : inletCenter;
                Vector3 seed = seedBase
                             + side * (seedSideCenter + normalizedZ * spanSide + jitterSide)
                             + up * (seedUpCenter + normalizedY * spanUp + jitterUp);

                if (enforceStreamlineClearance && TryProjectOutsideBody(ref seed, dir))
                {
                    seed += dir * (stepLength * 3f);
                }

                LineRenderer lr = lines[i];
                lr.enabled = true;
                IntegrateLine(
                    seed,
                    dir,
                    side,
                    up,
                    vinf,
                    tunnelBounds,
                    lr,
                    out float avgSpeed,
                    out float minDistToBody,
                    false,
                    bodySeedOrigin,
                    bodyHalfLength,
                    bodyHalfSide,
                    bodyHalfHeight);

                if (impactMode && hasModelBounds)
                {
                    float impactThreshold = Mathf.Max(bodyHalfLength, Mathf.Max(bodyHalfSide, bodyHalfHeight)) * 0.6f;
                    if (minDistToBody > impactThreshold)
                    {
                        lr.positionCount = 0;
                        lr.enabled = false;
                        continue;
                    }
                }

                float speedT = Mathf.Clamp01(avgSpeed / speedNormDenom);
                Color baseColor = velocityGradient != null
                    ? velocityGradient.Evaluate(speedT)
                    : new Color(0.55f, 0.90f, 1f, 1f);
                float linePhase = i / (float)Mathf.Max(lineCount, 1);
                float wave = Mathf.Repeat(flowPhase * 0.8f - linePhase, 1f);
                float brightness = Mathf.SmoothStep(0.45f, 1.0f, 1f - Mathf.Abs(wave - 0.5f) * 2f);
                float alpha = Mathf.Lerp(0.55f, 1.0f, brightness);
                
                if (smokeMode)
                {
                    lr.startColor = new Color(0.85f, 0.86f, 0.87f, 0.35f);
                    lr.endColor = new Color(0.85f, 0.86f, 0.87f, 0.05f);
                    lr.widthMultiplier = lineWidth * 4.5f;
                }
                else
                {
                    lr.startColor = new Color(baseColor.r * (0.9f + 0.1f * brightness), baseColor.g * (0.9f + 0.1f * brightness), baseColor.b, alpha);
                    lr.endColor = new Color(baseColor.r * 0.72f, baseColor.g * 0.72f, baseColor.b * 0.72f, 0.18f);
                    lr.widthMultiplier = Mathf.Lerp(lineWidth * 0.88f, lineWidth * 1.5f, speedT);
                }
            }

            for (int i = lineCount; i < lines.Count; i++)
            {
                if (lines[i] != null) lines[i].enabled = false;
            }
        }

        private void DrawStylizedEffects(int lineCount)
        {
            rngState = 3141592653u;

            Bounds tunnelBounds = windTunnel.GetTunnelBounds();
            if (tunnelBounds.size.x < 0.1f || tunnelBounds.size.y < 0.1f || tunnelBounds.size.z < 0.1f)
            {
                tunnelBounds = new Bounds(windTunnel.transform.position, new Vector3(10f, 4f, 5f));
            }

            Vector3 dir = ResolveWindDir().normalized;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.right;
            BuildFlowFrame(dir, out Vector3 side, out Vector3 up);
            float vinf = Mathf.Max(0.5f, windTunnel.settings.inletVelocity);
            float speedNormDenom = Mathf.Max(0.5f, vinf * 1.15f);

            Vector3 bodyCenter = modelBoundsCache.center;
            float bodyHalfLength = Mathf.Max(ProjectHalfExtent(modelBoundsCache, dir), 0.18f);
            float bodyHalfSide = Mathf.Max(ProjectHalfExtent(modelBoundsCache, side), 0.09f);
            float bodyHalfHeight = Mathf.Max(ProjectHalfExtent(modelBoundsCache, up), 0.09f);
            float tunnelLength = Vector3.Dot(tunnelBounds.size, Abs(dir));
            float tunnelHalfSide = Vector3.Dot(tunnelBounds.extents, Abs(side)) * 0.88f;
            float tunnelHalfHeight = Vector3.Dot(tunnelBounds.extents, Abs(up)) * 0.86f;
            float upstreamOffset = Mathf.Clamp(bodyHalfLength * 1.65f, tunnelLength * 0.05f, tunnelLength * 0.20f);
            Vector3 seedOrigin = bodyCenter - dir * upstreamOffset;
            Vector3 inletCenter = GetInletCenter(tunnelBounds, dir, tunnelLength);

            int carrierCount = Mathf.Clamp(Mathf.RoundToInt(lineCount * 0.24f), 10, Mathf.Max(10, lineCount / 3));
            int wakeLineCount = Mathf.Max(0, lineCount - carrierCount);
            int topCount = wakeLineCount > 0 ? Mathf.Max(8, Mathf.RoundToInt(wakeLineCount * 0.48f)) : 0;
            int flankCount = wakeLineCount > 0 ? Mathf.Max(8, Mathf.RoundToInt(wakeLineCount * 0.34f)) : 0;
            int centerCount = Mathf.Max(0, wakeLineCount - topCount - flankCount);

            for (int i = 0; i < lineCount; i++)
            {
                bool carrierLine = i < carrierCount;
                Vector3 seed;
                if (carrierLine)
                {
                    int carrierIndex = i;
                    int carrierRows = Mathf.Max(2, Mathf.CeilToInt(Mathf.Sqrt(carrierCount)));
                    int carrierCols = Mathf.Max(2, Mathf.CeilToInt(carrierCount / (float)carrierRows));
                    int row = carrierIndex / carrierCols;
                    int col = carrierIndex % carrierCols;
                    float u = carrierRows > 1 ? row / (float)(carrierRows - 1) : 0.5f;
                    float v = carrierCols > 1 ? col / (float)(carrierCols - 1) : 0.5f;
                    float sidePos = (v * 2f - 1f) * tunnelHalfSide;
                    float upPos = (u * 2f - 1f) * tunnelHalfHeight;
                    float jitterSide = (Rand01() * 2f - 1f) * seedJitter * tunnelHalfSide * 0.55f;
                    float jitterUp = (Rand01() * 2f - 1f) * seedJitter * tunnelHalfHeight * 0.55f;
                    seed = inletCenter + side * (sidePos + jitterSide) + up * (upPos + jitterUp);
                }
                else
                {
                    int wakeIndex = i - carrierCount;
                    seed = ComputeEffectsSeed(
                        wakeIndex,
                        topCount,
                        flankCount,
                        centerCount,
                        seedOrigin,
                        bodyCenter,
                        side,
                        up,
                        bodyHalfSide,
                        bodyHalfHeight);
                }

                if (enforceStreamlineClearance && TryProjectOutsideBody(ref seed, dir))
                {
                    seed += dir * (stepLength * 1.5f);
                }

                LineRenderer lr = lines[i];
                lr.enabled = true;
                IntegrateLine(
                    seed,
                    dir,
                    side,
                    up,
                    vinf,
                    tunnelBounds,
                    lr,
                    out float avgSpeed,
                    out float minDistToBody,
                    !carrierLine,
                    bodyCenter,
                    bodyHalfLength,
                    bodyHalfSide,
                    bodyHalfHeight);

                float speedT = Mathf.Clamp01(avgSpeed / speedNormDenom);
                if (carrierLine)
                {
                    Color carrierColor = velocityGradient != null
                        ? velocityGradient.Evaluate(Mathf.Clamp01(0.12f + speedT * 0.72f))
                        : new Color(0.15f, 0.78f, 1f, 1f);
                    lr.startColor = new Color(carrierColor.r, carrierColor.g, carrierColor.b, 0.72f);
                    lr.endColor = new Color(carrierColor.r * 0.82f, carrierColor.g * 0.82f, carrierColor.b * 0.82f, 0.22f);
                    lr.widthMultiplier = Mathf.Lerp(lineWidth * 0.34f, lineWidth * 0.52f, speedT);
                }
                else
                {
                    float sideCoord = Vector3.Dot(seed - bodyCenter, side);
                    float upCoord = Vector3.Dot(seed - bodyCenter, up);
                    float sideT = Mathf.InverseLerp(-bodyHalfSide * 1.4f, bodyHalfSide * 1.4f, sideCoord);
                    float upT = Mathf.InverseLerp(-bodyHalfHeight * 0.15f, bodyHalfHeight * 1.1f, upCoord);
                    float colorT = Mathf.Clamp01(0.06f + speedT * 0.58f + upT * 0.18f + (1f - Mathf.Abs(sideT * 2f - 1f)) * 0.18f);

                    Color baseColor = velocityGradient != null
                        ? velocityGradient.Evaluate(colorT)
                        : new Color(0.18f, 0.95f, 0.30f, 1f);
                    float alpha = Mathf.Lerp(0.76f, 0.98f, upT);
                    lr.startColor = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
                    lr.endColor = new Color(baseColor.r * 0.86f, baseColor.g * 0.86f, baseColor.b * 0.86f, 0.12f);
                    lr.widthMultiplier = Mathf.Lerp(lineWidth * 0.42f, lineWidth * 0.72f, Mathf.Clamp01(0.35f + speedT * 0.65f));
                }
            }

            for (int i = lineCount; i < lines.Count; i++)
            {
                if (lines[i] != null) lines[i].enabled = false;
            }
        }

        /// <summary>
        /// AirShaper-style vertical or horizontal streamlines.
        /// Seeds from a cross-section plane through the model center.
        /// </summary>
        private void DrawPlaneStreamlines(int lineCount, bool vertical)
        {
            rngState = vertical ? 1928374650u : 3847562910u;

            Bounds tunnelBounds = windTunnel.GetTunnelBounds();
            if (tunnelBounds.size.x < 0.1f || tunnelBounds.size.y < 0.1f || tunnelBounds.size.z < 0.1f)
            {
                tunnelBounds = new Bounds(windTunnel.transform.position, new Vector3(10f, 4f, 5f));
            }

            Vector3 dir = ResolveWindDir().normalized;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.right;
            BuildFlowFrame(dir, out Vector3 side, out Vector3 up);

            float vinf = Mathf.Max(0.5f, windTunnel.settings.inletVelocity);
            float speedNormDenom = Mathf.Max(0.5f, vinf * Mathf.Max(0.1f, colorVelocityRangeScale));
            float tunnelLength = Vector3.Dot(tunnelBounds.size, Abs(dir));

            // Plane axes: vertical = (dir, up), horizontal = (dir, side)
            Vector3 planeAxis1 = dir;
            Vector3 planeAxis2 = vertical ? up : side;
            float planeHalf1 = tunnelLength * 0.46f;
            float planeHalf2 = vertical
                ? Vector3.Dot(tunnelBounds.extents, Abs(up)) * 0.88f
                : Vector3.Dot(tunnelBounds.extents, Abs(side)) * 0.88f;

            Vector3 planeCenter = hasModelBounds ? modelBoundsCache.center : tunnelBounds.center;
            // For vertical: center on model, offset zero on side axis
            // For horizontal: center on model, at a specific height (mid-body)
            if (hasModelBounds)
            {
                if (vertical)
                {
                    // Seed on the symmetry plane (side=0)
                    planeCenter = modelBoundsCache.center;
                }
                else
                {
                    // Seed at mid-height of model
                    float modelHalfHeight = ProjectHalfExtent(modelBoundsCache, up);
                    planeCenter = modelBoundsCache.center + up * (modelHalfHeight * 0.15f);
                }
            }

            // Seed upstream of model center
            Vector3 seedOrigin = GetInletCenter(tunnelBounds, dir, tunnelLength);

            int rows = Mathf.Max(2, Mathf.CeilToInt(Mathf.Sqrt(lineCount * 0.6f)));
            int cols = Mathf.Max(2, Mathf.CeilToInt(lineCount / (float)rows));

            for (int i = 0; i < lineCount; i++)
            {
                int row = i / cols;
                int col = i % cols;
                float u = rows > 1 ? row / (float)(rows - 1) : 0.5f;
                float v = cols > 1 ? col / (float)(cols - 1) : 0.5f;

                // Spread seeds across the plane
                float coord2 = (u * 2f - 1f) * planeHalf2;
                float coordFlow = (v * 2f - 1f) * planeHalf1 * 0.15f; // slight spread along flow

                float jitter2 = (Rand01() * 2f - 1f) * seedJitter * planeHalf2;
                float jitterFlow = (Rand01() * 2f - 1f) * seedJitter * planeHalf1 * 0.08f;

                Vector3 seed = seedOrigin
                    + planeAxis2 * (coord2 + jitter2)
                    + planeAxis1 * (coordFlow + jitterFlow);

                if (enforceStreamlineClearance && TryProjectOutsideBody(ref seed, dir))
                {
                    seed += dir * (stepLength * 2f);
                }

                LineRenderer lr = lines[i];
                lr.enabled = true;
                IntegrateLine(
                    seed,
                    dir,
                    side,
                    up,
                    vinf,
                    tunnelBounds,
                    lr,
                    out float avgSpeed,
                    out float minDistToBody,
                    false,
                    planeCenter,
                    planeHalf1,
                    planeHalf2,
                    planeHalf2);

                float speedT = Mathf.Clamp01(avgSpeed / speedNormDenom);
                Color baseColor = velocityGradient != null
                    ? velocityGradient.Evaluate(speedT)
                    : new Color(0.55f, 0.90f, 1f, 1f);
                float linePhase = i / (float)Mathf.Max(lineCount, 1);
                float wave = Mathf.Repeat(flowPhase * 0.5f - linePhase, 1f);
                float brightness = Mathf.SmoothStep(0.55f, 1.0f, 1f - Mathf.Abs(wave - 0.5f) * 2f);
                float alpha = Mathf.Lerp(0.65f, 1.0f, brightness);

                lr.startColor = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
                lr.endColor = new Color(baseColor.r * 0.75f, baseColor.g * 0.75f, baseColor.b * 0.75f, 0.18f);
                lr.widthMultiplier = Mathf.Lerp(lineWidth * 0.70f, lineWidth * 1.3f, speedT);
            }

            for (int i = lineCount; i < lines.Count; i++)
            {
                if (lines[i] != null) lines[i].enabled = false;
            }
        }

        private Vector3 ComputeEffectsSeed(
            int index,
            int topCount,
            int flankCount,
            int centerCount,
            Vector3 seedOrigin,
            Vector3 bodyCenter,
            Vector3 side,
            Vector3 up,
            float bodyHalfSide,
            float bodyHalfHeight)
        {
            float sideJitter = (Rand01() * 2f - 1f) * bodyHalfSide * 0.045f;
            float upJitter = (Rand01() * 2f - 1f) * bodyHalfHeight * 0.04f;

            if (index < topCount)
            {
                float t = topCount > 1 ? index / (float)(topCount - 1) : 0.5f;
                float sidePos = Mathf.Lerp(-bodyHalfSide * 1.05f, bodyHalfSide * 1.05f, t);
                float arch = Mathf.Sin(t * Mathf.PI);
                float upPos = Mathf.Lerp(bodyHalfHeight * 0.24f, bodyHalfHeight * 0.78f, arch);
                return seedOrigin + side * (sidePos + sideJitter) + up * (upPos + upJitter);
            }

            if (index < topCount + flankCount)
            {
                int flankIndex = index - topCount;
                int flankPairCount = Mathf.Max(1, flankCount / 2);
                float layer = flankPairCount > 1 ? (flankIndex / 2) / (float)(flankPairCount - 1) : 0.5f;
                float sign = flankIndex % 2 == 0 ? -1f : 1f;
                float sidePos = sign * Mathf.Lerp(bodyHalfSide * 0.74f, bodyHalfSide * 1.02f, layer);
                float upPos = Mathf.Lerp(bodyHalfHeight * 0.06f, bodyHalfHeight * 0.58f, layer);
                return seedOrigin + side * (sidePos + sideJitter) + up * (upPos + upJitter);
            }

            int centerIndex = index - topCount - flankCount;
            float centerT = centerCount > 1 ? centerIndex / (float)(centerCount - 1) : 0.5f;
            float sidePosCenter = Mathf.Lerp(-bodyHalfSide * 0.34f, bodyHalfSide * 0.34f, centerT);
            float upPosCenter = Mathf.Lerp(bodyHalfHeight * 0.12f, bodyHalfHeight * 0.34f, Mathf.PingPong(centerT * 1.5f, 1f));
            return seedOrigin + side * (sidePosCenter + sideJitter) + up * (upPosCenter + upJitter);
        }

        private void IntegrateLine(
            Vector3 seed,
            Vector3 dir,
            Vector3 side,
            Vector3 up,
            float vinf,
            Bounds tunnelBounds,
            LineRenderer lr,
            out float avgSpeed,
            out float minDistToBody,
            bool stylizedEffects = false,
            Vector3 effectsCenter = default,
            float effectsHalfLength = 0f,
            float effectsHalfSide = 0f,
            float effectsHalfHeight = 0f)
        {
            minDistToBody = float.MaxValue;
            lr.positionCount = pointsPerLine;

            Vector3 position = seed;
            float speedSum = 0f;
            int used = 0;
            int maxPoints = stylizedEffects ? Mathf.Min(pointsPerLine, 120) : pointsPerLine;
            float speedFactor = Mathf.InverseLerp(0.5f, 70f, vinf);
            float tunnelLength = Vector3.Dot(tunnelBounds.size, Abs(dir));
            float tunnelHalfSide = Vector3.Dot(tunnelBounds.extents, Abs(side));
            float tunnelHalfHeight = Vector3.Dot(tunnelBounds.extents, Abs(up));
            float outletCoordinate = GetOutletCoordinate(tunnelBounds, dir, tunnelLength);
            float totalTravel = 0f;
            float outletDistanceFromSeed = Mathf.Max(0f, outletCoordinate - Vector3.Dot(seed, dir));
            float maxTravel = stylizedEffects
                ? Mathf.Max(stepLength * 30f, Mathf.Lerp(effectsHalfLength * 2.4f, effectsHalfLength * 5.8f, speedFactor))
                : Mathf.Max(tunnelLength * 1.05f, outletDistanceFromSeed + tunnelLength * 0.15f);

            // Adaptive step: body radius for proximity-based scaling
            float bodyRadius = hasModelBounds ? modelBoundsCache.extents.magnitude : 0f;
            Vector3 bodyCenter = hasModelBounds ? modelBoundsCache.center : tunnelBounds.center;

            for (int i = 0; i < maxPoints; i++)
            {
                if (hasModelBounds)
                {
                    float dist = Vector3.Distance(position, bodyCenter);
                    if (dist < minDistToBody) minDistToBody = dist;
                }

                if (!float.IsFinite(position.x))
                {
                    break;
                }

                if (!stylizedEffects)
                {
                    Vector3 relToCenter = position - tunnelBounds.center;
                    float axial = Vector3.Dot(relToCenter, dir);
                    float lateral = Mathf.Abs(Vector3.Dot(relToCenter, side));
                    float vertical = Mathf.Abs(Vector3.Dot(relToCenter, up));
                    if (axial < -tunnelLength * 0.55f
                        || axial > tunnelLength * 0.55f
                        || lateral > tunnelHalfSide * 1.06f
                        || vertical > tunnelHalfHeight * 1.06f)
                    {
                        break;
                    }
                }

                if (stylizedEffects && used > 10)
                {
                    Vector3 rel = position - effectsCenter;
                    float axial = Vector3.Dot(rel, dir);
                    float lateral = Mathf.Abs(Vector3.Dot(rel, side));
                    float vertical = Mathf.Abs(Vector3.Dot(rel, up));
                    if (axial > effectsHalfLength * 4.0f
                        || axial < -effectsHalfLength * 1.8f
                        || lateral > effectsHalfSide * 2.8f
                        || vertical > effectsHalfHeight * 2.4f)
                    {
                        break;
                    }
                }

                if (enforceStreamlineClearance && TryProjectOutsideBody(ref position, dir))
                {
                    position += dir * (stepLength * 0.5f);
                }

                // Check obstacle mask — try deflecting around the obstacle instead of breaking
                if (enforceStreamlineClearance && navier != null && navier.IsObstacle(position))
                {
                    if (!TryDeflectAroundObstacle(ref position, dir, side, up, vinf))
                    {
                        break;
                    }
                }

                lr.SetPosition(used, position);
                used++;

                Vector3 k1 = SampleVelocity(position, dir, side, up, vinf);
                float speed = k1.magnitude;
                speedSum += speed;

                if (speed < vinf * (stylizedEffects ? 0.010f : 0.0008f) && used > 18)
                {
                    break;
                }

                // Adaptive step: fine near body, coarse in freestream
                float adaptiveStep = stepLength;
                if (!stylizedEffects && bodyRadius > 0.01f)
                {
                    float distToBody = Vector3.Distance(position, bodyCenter);
                    float proximity = Mathf.Clamp01(distToBody / (bodyRadius * 2.5f));
                    adaptiveStep = Mathf.Lerp(stepLength, stepLength * 3.5f, proximity * proximity);
                }

                float dt = adaptiveStep / Mathf.Max(speed, 0.1f);
                dt = Mathf.Min(dt, adaptiveStep * (stylizedEffects ? 2.8f : 4f));

                Vector3 midpoint = position + k1 * (dt * 0.5f);
                if (!float.IsFinite(midpoint.x))
                {
                    break;
                }

                // Sub-step obstacle check: also test the midpoint
                if (enforceStreamlineClearance && navier != null && navier.IsObstacle(midpoint))
                {
                    if (!TryDeflectAroundObstacle(ref midpoint, dir, side, up, vinf))
                    {
                        break;
                    }
                }

                Vector3 k2 = SampleVelocity(midpoint, dir, side, up, vinf);
                Vector3 nextPosition = position + k2 * dt;
                totalTravel += Vector3.Distance(position, nextPosition);
                float currentOutletDistance = Vector3.Dot(position, dir) - outletCoordinate;
                float nextOutletDistance = Vector3.Dot(nextPosition, dir) - outletCoordinate;
                if (currentOutletDistance <= 0f && nextOutletDistance >= 0f)
                {
                    float denom = nextOutletDistance - currentOutletDistance;
                    float t = Mathf.Abs(denom) > 1e-6f ? Mathf.Clamp01(-currentOutletDistance / denom) : 1f;
                    position = Vector3.Lerp(position, nextPosition, t);
                    lr.SetPosition(used - 1, position);
                    break;
                }

                // Check destination — deflect or break
                if (enforceStreamlineClearance && navier != null && navier.IsObstacle(nextPosition))
                {
                    if (!TryDeflectAroundObstacle(ref nextPosition, dir, side, up, vinf))
                    {
                        break;
                    }
                }

                position = nextPosition;

                if (totalTravel >= maxTravel)
                {
                    break;
                }
            }

            int finalCount = Mathf.Max(used, 1);
            lr.positionCount = finalCount;
            avgSpeed = used > 0 ? speedSum / used : 0f;
        }

        private void FindPortals()
        {
            if (windTunnel == null)
            {
                return;
            }

            Transform root = windTunnel.transform;
            if (inletPlane == null)
            {
                inletPlane = root.Find("InletPlane") ?? FindDescendant(root, "InletPlane");
            }

            if (outletPlane == null)
            {
                outletPlane = root.Find("OutletPlane") ?? FindDescendant(root, "OutletPlane");
            }
        }

        private static Transform FindDescendant(Transform parent, string targetName)
        {
            if (parent == null)
            {
                return null;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == targetName)
                {
                    return child;
                }

                Transform found = FindDescendant(child, targetName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private Vector3 GetInletCenter(Bounds tunnelBounds, Vector3 dir, float tunnelLength)
        {
            if (inletPlane != null)
            {
                return inletPlane.position;
            }

            return tunnelBounds.center - dir * (tunnelLength * 0.5f);
        }

        private float GetOutletCoordinate(Bounds tunnelBounds, Vector3 dir, float tunnelLength)
        {
            if (outletPlane != null)
            {
                return Vector3.Dot(outletPlane.position, dir);
            }

            return Vector3.Dot(tunnelBounds.center, dir) + tunnelLength * 0.5f;
        }

        private Vector3 SampleVelocity(Vector3 worldPos, Vector3 dir, Vector3 side, Vector3 up, float vinf)
        {
            Vector3 presentation = ComputeAnalyticalFlow(worldPos, dir, side, up, vinf);
            string viz = windTunnel != null ? windTunnel.settings.visualizationMode : "Streamlines";
            bool effectsMode = string.Equals(viz, WindTunnelSimulation3D.VisualizationEffects, System.StringComparison.OrdinalIgnoreCase);
            bool rawSolverOnly = windTunnel != null && windTunnel.useRawSolverDataOnly;
            // Favor solver data heavily — the analytical model is only a coarse fallback.
            float solverWeight = hasModelBounds
                ? 1f
                : (effectsMode ? 0.50f : 0.88f);
            solverWeight = Mathf.Clamp01(solverWeight);

            if (snapshotReady)
            {
                Vector3 v = SampleGrid(worldPos);
                if (float.IsFinite(v.x) && v.sqrMagnitude > 1e-6f)
                {
                    return rawSolverOnly ? v : Vector3.Lerp(presentation, v, solverWeight);
                }
            }

            if (navier != null && navier.TrySampleFlow(worldPos, out var sampled, out _))
            {
                if (float.IsFinite(sampled.x) && sampled.sqrMagnitude > 1e-6f)
                {
                    return rawSolverOnly ? sampled : Vector3.Lerp(presentation, sampled, solverWeight);
                }
            }

            return rawSolverOnly ? Vector3.zero : presentation;
        }

        private Vector3 SampleGrid(Vector3 worldPos)
        {
            if (velCache == null || cachedSX <= 0 || cachedSY <= 0 || cachedSZ <= 0)
            {
                return Vector3.zero;
            }

            float lx = (worldPos.x - cachedOrigin.x) / Mathf.Max(cachedCellSize.x, 1e-5f);
            float ly = (worldPos.y - cachedOrigin.y) / Mathf.Max(cachedCellSize.y, 1e-5f);
            float lz = (worldPos.z - cachedOrigin.z) / Mathf.Max(cachedCellSize.z, 1e-5f);

            lx = Mathf.Clamp(lx, 0f, cachedSX - 1.001f);
            ly = Mathf.Clamp(ly, 0f, cachedSY - 1.001f);
            lz = Mathf.Clamp(lz, 0f, cachedSZ - 1.001f);

            int x0 = Mathf.FloorToInt(lx);
            int y0 = Mathf.FloorToInt(ly);
            int z0 = Mathf.FloorToInt(lz);
            int x1 = Mathf.Min(x0 + 1, cachedSX - 1);
            int y1 = Mathf.Min(y0 + 1, cachedSY - 1);
            int z1 = Mathf.Min(z0 + 1, cachedSZ - 1);

            float tx = lx - x0;
            float ty = ly - y0;
            float tz = lz - z0;

            Vector3 v000 = velCache[Idx(x0, y0, z0)];
            Vector3 v100 = velCache[Idx(x1, y0, z0)];
            Vector3 v010 = velCache[Idx(x0, y1, z0)];
            Vector3 v110 = velCache[Idx(x1, y1, z0)];
            Vector3 v001 = velCache[Idx(x0, y0, z1)];
            Vector3 v101 = velCache[Idx(x1, y0, z1)];
            Vector3 v011 = velCache[Idx(x0, y1, z1)];
            Vector3 v111 = velCache[Idx(x1, y1, z1)];

            return Vector3.Lerp(
                Vector3.Lerp(Vector3.Lerp(v000, v100, tx), Vector3.Lerp(v010, v110, tx), ty),
                Vector3.Lerp(Vector3.Lerp(v001, v101, tx), Vector3.Lerp(v011, v111, tx), ty),
                tz);
        }

        private int Idx(int x, int y, int z)
        {
            return x + cachedSX * (y + cachedSY * z);
        }

        private Vector3 ComputeAnalyticalFlow(Vector3 pos, Vector3 dir, Vector3 side, Vector3 up, float vinf)
        {
            Vector3 freestream = dir * vinf;
            if (!hasModelBounds)
            {
                return freestream;
            }

            Vector3 rel = pos - modelBoundsCache.center;
            float axial = Vector3.Dot(rel, dir);
            float lateral = Vector3.Dot(rel, side);
            float vertical = Vector3.Dot(rel, up);

            float halfLength = Mathf.Max(ProjectHalfExtent(modelBoundsCache, dir) * 1.05f, 0.12f);
            float halfWidth = Mathf.Max(ProjectHalfExtent(modelBoundsCache, side) * 0.92f, 0.08f);
            float halfHeight = Mathf.Max(ProjectHalfExtent(modelBoundsCache, up) * 0.92f, 0.08f);

            float ellipsoid = axial * axial / Mathf.Max(halfLength * halfLength, 1e-4f)
                            + lateral * lateral / Mathf.Max(halfWidth * halfWidth, 1e-4f)
                            + vertical * vertical / Mathf.Max(halfHeight * halfHeight, 1e-4f);

            Vector3 lateralDir = side * (lateral / Mathf.Max(halfWidth * halfWidth, 1e-4f))
                               + up * (vertical / Mathf.Max(halfHeight * halfHeight, 1e-4f));
            if (lateralDir.sqrMagnitude > 1e-6f)
            {
                lateralDir.Normalize();
            }

            if (ellipsoid < 1f)
            {
                Vector3 normal = (dir * (axial / Mathf.Max(halfLength * halfLength, 1e-4f)) + lateralDir).normalized;
                Vector3 tangent = Vector3.ProjectOnPlane(dir, normal).normalized;
                if (tangent.sqrMagnitude < 1e-6f) tangent = dir;
                return tangent * (vinf * 0.45f) + normal * (vinf * 0.08f);
            }

            float shell = Mathf.Sqrt(Mathf.Max(ellipsoid, 1e-4f));
            float influence = Mathf.Clamp01(1.4f - shell);
            float throat = Mathf.Clamp01(1.15f - Mathf.Abs(axial) / (halfLength * 1.7f));
            float crossRadius = Mathf.Sqrt(
                lateral * lateral / Mathf.Max(halfWidth * halfWidth, 1e-4f)
              + vertical * vertical / Mathf.Max(halfHeight * halfHeight, 1e-4f));
            float nearSurface = Mathf.Clamp01(1.55f - crossRadius);

            Vector3 sidewash = lateralDir * (vinf * 0.55f * influence);
            Vector3 acceleratedCore = dir * (vinf * 0.22f * throat * nearSurface);

            Vector3 wake = Vector3.zero;
            if (axial > 0f)
            {
                float wakeLength = halfLength * 6.5f;
                float wakeAxial = Mathf.Clamp01(1f - axial / Mathf.Max(wakeLength, 1e-4f));
                float wakeCore = wakeAxial * Mathf.Clamp01(1.2f - crossRadius);
                wake -= dir * (vinf * 0.62f * wakeCore);

                Vector3 swirlDir = Vector3.Cross(dir, lateralDir).normalized;
                if (swirlDir.sqrMagnitude > 1e-6f)
                {
                    wake += swirlDir * (vinf * 0.11f * wakeCore * Mathf.Sign(vertical == 0f ? 1f : vertical));
                }
            }

            Vector3 result = freestream + sidewash + acceleratedCore + wake;
            float forward = Vector3.Dot(result, dir);
            if (forward < vinf * 0.06f)
            {
                result += dir * (vinf * 0.06f - forward);
            }
            return result;
        }

        private bool TryProjectOutsideBody(ref Vector3 position, Vector3 flowDirection)
        {
            if (!enforceStreamlineClearance)
            {
                return false;
            }

            if (!hasModelBounds)
            {
                return false;
            }

            // Use a tighter inner bounds — shrink the AABB by 40% to better
            // approximate the actual model surface and reduce the gap.
            Bounds innerBounds = modelBoundsCache;
            Vector3 shrinkAmount = innerBounds.size * 0.08f;
            innerBounds.Expand(-Mathf.Min(shrinkAmount.x, Mathf.Min(shrinkAmount.y, shrinkAmount.z)));

            if (!innerBounds.Contains(position))
            {
                return false;
            }

            // If inside the obstacle voxel mask, use that for a tighter check
            if (navier != null && !navier.IsObstacle(position))
            {
                return false;
            }

            Vector3 min = innerBounds.min;
            Vector3 max = innerBounds.max;
            float dxMin = Mathf.Abs(position.x - min.x);
            float dxMax = Mathf.Abs(max.x - position.x);
            float dyMin = Mathf.Abs(position.y - min.y);
            float dyMax = Mathf.Abs(max.y - position.y);
            float dzMin = Mathf.Abs(position.z - min.z);
            float dzMax = Mathf.Abs(max.z - position.z);

            float best = dxMin;
            Vector3 projected = new Vector3(min.x, position.y, position.z);

            if (dxMax < best) { best = dxMax; projected = new Vector3(max.x, position.y, position.z); }
            if (dyMin < best) { best = dyMin; projected = new Vector3(position.x, min.y, position.z); }
            if (dyMax < best) { best = dyMax; projected = new Vector3(position.x, max.y, position.z); }
            if (dzMin < best) { best = dzMin; projected = new Vector3(position.x, position.y, min.z); }
            if (dzMax < best) { projected = new Vector3(position.x, position.y, max.z); }

            Vector3 outward = (projected - modelBoundsCache.center).normalized;
            if (outward.sqrMagnitude < 1e-6f)
            {
                outward = Vector3.up;
            }

            position = projected + outward * 0.01f + flowDirection * 0.005f;
            return true;
        }

        /// <summary>
        /// Attempts to push a streamline point laterally out of the obstacle mask.
        /// Probes 8 cross-flow directions (±side, ±up, and diagonals) at increasing
        /// distances.  Returns true if a clear cell was found, false if the point
        /// is deeply embedded and the streamline should terminate.
        /// </summary>
        private bool TryDeflectAroundObstacle(ref Vector3 position, Vector3 dir, Vector3 side, Vector3 up, float vinf)
        {
            if (navier == null) return false;

            // Estimate cell size from the solver grid so our probe steps are
            // commensurate with the voxel resolution.
            float cellSize = stepLength * 1.2f;
            if (windTunnel != null)
            {
                Bounds tb = windTunnel.GetTunnelBounds();
                float maxDim = Mathf.Max(tb.size.x, Mathf.Max(tb.size.y, tb.size.z));
                cellSize = Mathf.Max(maxDim / 64f, stepLength);
            }

            // 12 probe directions: 8 cross-flow + 4 flow-aligned diagonals
            obstacleProbeDirections[0] = side;
            obstacleProbeDirections[1] = -side;
            obstacleProbeDirections[2] = up;
            obstacleProbeDirections[3] = -up;
            obstacleProbeDirections[4] = (side + up).normalized;
            obstacleProbeDirections[5] = (side - up).normalized;
            obstacleProbeDirections[6] = (-side + up).normalized;
            obstacleProbeDirections[7] = (-side - up).normalized;
            obstacleProbeDirections[8] = (dir + up).normalized;
            obstacleProbeDirections[9] = (dir - up).normalized;
            obstacleProbeDirections[10] = (dir + side).normalized;
            obstacleProbeDirections[11] = (dir - side).normalized;

            // Probe at 5 increasing radii for better reach on coarse grids
            for (int radius = 1; radius <= 5; radius++)
            {
                float dist = cellSize * radius;
                for (int d = 0; d < obstacleProbeDirections.Length; d++)
                {
                    Vector3 candidate = position + obstacleProbeDirections[d] * dist;
                    if (!navier.IsObstacle(candidate))
                    {
                        // Nudge slightly further in the clear direction + a tiny
                        // downstream push so the streamline keeps flowing.
                        position = candidate + obstacleProbeDirections[d] * (cellSize * 0.15f) + dir * (stepLength * 0.2f);
                        return true;
                    }
                }
            }

            return false; // deeply embedded — terminate this streamline
        }

        private Vector3 ResolveWindDir()
        {
            return windTunnel != null ? windTunnel.ResolveWindDirection() : Vector3.right;
        }

        private void BuildFlowFrame(Vector3 dir, out Vector3 side, out Vector3 up)
        {
            Vector3 referenceUp = windTunnel != null ? windTunnel.ResolveTunnelVerticalAxis() : Vector3.up;
            if (Mathf.Abs(Vector3.Dot(referenceUp.normalized, dir.normalized)) > 0.92f)
            {
                referenceUp = windTunnel != null ? windTunnel.ResolveTunnelLongAxis() : Vector3.forward;
            }

            side = Vector3.Cross(referenceUp, dir).normalized;
            if (side.sqrMagnitude < 1e-6f)
            {
                side = Vector3.Cross(Vector3.up, dir).normalized;
            }
            if (side.sqrMagnitude < 1e-6f)
            {
                side = Vector3.right;
            }

            up = Vector3.Cross(dir, side).normalized;
            if (up.sqrMagnitude < 1e-6f)
            {
                up = windTunnel != null ? windTunnel.ResolveTunnelVerticalAxis() : Vector3.up;
            }
        }

        private void EnsurePool(int desiredCount)
        {
            EnsureMaterial();
            while (lines.Count < desiredCount)
            {
                var go = new GameObject($"Streamline_{lines.Count:000}");
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.shadowCastingMode = ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                lr.textureMode = LineTextureMode.Stretch;
                lr.alignment = LineAlignment.View;
                lr.useWorldSpace = true;
                lr.numCornerVertices = 6;
                lr.numCapVertices = 3;
                lr.material = lineMaterial;
                lr.startWidth = lineWidth;
                lr.endWidth = lineWidth * 0.45f;
                lr.positionCount = 0;
                lr.enabled = false;
                lines.Add(lr);
            }

            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i] == null) continue;
                lines[i].startWidth = lineWidth;
                lines[i].endWidth = lineWidth * 0.45f;
            }
        }

        private void EnsureMaterial()
        {
            if (lineMaterial != null) return;

            Shader shader = RuntimeShaderResolver.FindLineShader();
            if (shader == null) return;

            lineMaterial = new Material(shader);
            if (!lineMaterial.HasProperty("_DashCount") || !lineMaterial.HasProperty("_FlowSpeed"))
            {
                Debug.LogWarning($"[Streamlines] Falling back to non-animated line shader '{shader.name}'. Build visuals may render as solid lines.");
            }

            var baseColor = new Color(0.78f, 1f, 0.88f, 0.95f);
            if (lineMaterial.HasProperty("_BaseColor")) lineMaterial.SetColor("_BaseColor", baseColor);
            if (lineMaterial.HasProperty("_Color")) lineMaterial.SetColor("_Color", baseColor);
            if (lineMaterial.HasProperty("_Surface")) lineMaterial.SetFloat("_Surface", 1f);
            if (lineMaterial.HasProperty("_Blend")) lineMaterial.SetFloat("_Blend", 0f);
            if (lineMaterial.HasProperty("_AlphaClip")) lineMaterial.SetFloat("_AlphaClip", 0f);
            if (lineMaterial.HasProperty("_Cull")) lineMaterial.SetFloat("_Cull", (float)CullMode.Off);
            if (lineMaterial.HasProperty("_ZWrite")) lineMaterial.SetFloat("_ZWrite", 0f);
            if (lineMaterial.HasProperty("_SrcBlend")) lineMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (lineMaterial.HasProperty("_DstBlend")) lineMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            // Animated streamline shader properties
            if (lineMaterial.HasProperty("_DashCount")) lineMaterial.SetFloat("_DashCount", 28f);
            if (lineMaterial.HasProperty("_DashRatio")) lineMaterial.SetFloat("_DashRatio", 0.52f);
            if (lineMaterial.HasProperty("_FlowSpeed")) lineMaterial.SetFloat("_FlowSpeed", 2.8f);
            if (lineMaterial.HasProperty("_GlowIntensity")) lineMaterial.SetFloat("_GlowIntensity", 0.9f);
            if (lineMaterial.HasProperty("_FadeStart")) lineMaterial.SetFloat("_FadeStart", 0.55f);
            if (lineMaterial.HasProperty("_FadeEnd")) lineMaterial.SetFloat("_FadeEnd", 0.04f);
            lineMaterial.renderQueue = (int)RenderQueue.Transparent + 12;
        }

        private void EnsureGradient()
        {
            if (velocityGradient != null) return;

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

        private void SetAllLinesVisible(bool visible)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i] != null) lines[i].enabled = visible;
            }
        }

        private void ClearAllLines()
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i] == null)
                {
                    continue;
                }

                lines[i].enabled = false;
                lines[i].positionCount = 0;
            }
        }

        private static float ProjectHalfExtent(Bounds bounds, Vector3 axis)
        {
            return Vector3.Dot(bounds.extents, Abs(axis.normalized));
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }
    }
}
