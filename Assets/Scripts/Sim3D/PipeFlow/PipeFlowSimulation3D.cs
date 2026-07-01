using UnityEngine;
using AeroFlow.Core;
using System.Collections.Generic;
using System;
using UnityEngine.Rendering;
using AeroFlow.Visualization;

namespace AeroFlow.Sim3D.PipeFlow
{
    [System.Serializable]
    public class PipeFlowSettings
    {
        public string visualizationMode = PipeFlowSimulation3D.VisualizationStreamlines;
        public float inletVelocity = 2f;
        public float fluidDensity = 998f;         // water by default
        public float dynamicViscosity = 1.003e-3f; // water at 20°C
        public float turbulenceIntensity = 1.0f;
        public float timeScale = 1.0f;
        public int iterationsPerFrame = 4;
        public WindTunnelVehicleProperties vehicle = new WindTunnelVehicleProperties();
    }

    [System.Serializable]
    public struct OpeningInfo
    {
        public int id;
        public Vector3 position;   // world-space centroid
        public Vector3 normal;     // outward normal
        public float radius;       // approximate radius
        public int cellCount;      // how many grid cells in this opening
    }

    [System.Serializable]
    public struct PipeFlowDiagnostics
    {
        public bool valid;
        public float meanVelocity;
        public float maxVelocity;
        public float pressureDrop;
        public float wallShear;
        public float divergenceL1;
        public float flowRate;
        public int fluidCellCount;
        public int openingCount;
        public float pipeReynolds;
    }

    public class PipeFlowSimulation3D : MonoBehaviour
    {
        public const string VisualizationStreamlines = "Streamlines";
        public const string VisualizationSurfacePressure = "Surface Pressure";
        public const string VisualizationSurfaceFriction = "Surface Friction";

        public PipeFlowSettings settings = new PipeFlowSettings();
        public BoundaryConditionManager boundaryManager;
        public event Action OnSimulationCompleted;

        [Header("Grid Resolution")]
        [Range(16, 128)] public int gridSizeX = 64;
        [Range(8, 96)]  public int gridSizeY = 32;
        [Range(8, 96)]  public int gridSizeZ = 32;

        [Header("Solver Quality")]
        [Range(1, 12)] public int diffusionIterations = 3;
        [Range(4, 80)] public int pressureIterations = 24;
        [Range(0f, 0.05f)] public float velocityDamping = 0.002f;
        [Range(0f, 3f)]    public float vorticityConfinement = 0.4f;

        [Header("Flow Alignment")]
        public WindTunnelFlowAxis flowAxis = WindTunnelFlowAxis.LocalX;

        bool isPaused = true;
        bool isInitialized;
        float currentStepTime = 1f / 60f;
        PipeFlowDiagnostics diagnostics;
        float nextDiagnosticsTime;

        ComputeShader pipeShader;
        ComputeBuffer gridVelocity, gridVelocityTmp;
        ComputeBuffer gridPressure, gridPressureTmp;
        ComputeBuffer gridDivergence;
        ComputeBuffer obstacleMaskBuffer;
        ComputeBuffer openingMaskBuffer;
        ComputeBuffer openingTypesBuffer; // 0=unassigned, 1=inlet, 2=outlet

        int applyForcesKernel, advectVelocityKernel, diffuseKernel, vorticityKernel;
        int divergenceKernel, jacobiKernel, projectKernel, advectParticlesKernel;
        int initializedGridCount;
        int[] obstacleMaskCache;
        int[] openingMaskCache;
        int obstacleMaskSignature;
        bool hasObstacleMaskSignature;
        Vector3 lastCellSize = Vector3.one;
        Vector3 lastBoundsCenter, lastBoundsSize;
        Vector3[] diagnosticsVelocityCache;
        float[] diagnosticsPressureCache;
        float[] diagnosticsDivergenceCache;

        int cachedFluidCellCount;
        int cachedOpeningCount;
        List<OpeningInfo> detectedOpenings = new List<OpeningInfo>();

        public bool TryGetDiagnostics(out PipeFlowDiagnostics value)
        {
            value = diagnostics;
            return diagnostics.valid;
        }

        public List<OpeningInfo> GetDetectedOpenings() => detectedOpenings;
        public int FluidCellCount => cachedFluidCellCount;

        public bool IsPaused => isPaused;

        public bool HasValidBoundaryAssignments()
        {
            if (boundaryManager == null)
                boundaryManager = FindAnyObjectByType<BoundaryConditionManager>();
            return boundaryManager != null && boundaryManager.HasValidFlowAssignments();
        }

        public static string NormalizeVisualizationMode(string mode)
        {
            if (string.Equals(mode, VisualizationStreamlines, StringComparison.OrdinalIgnoreCase))
                return VisualizationStreamlines;
            if (string.Equals(mode, VisualizationSurfacePressure, StringComparison.OrdinalIgnoreCase))
                return VisualizationSurfacePressure;
            if (string.Equals(mode, VisualizationSurfaceFriction, StringComparison.OrdinalIgnoreCase))
                return VisualizationSurfaceFriction;
            return VisualizationStreamlines;
        }

        public void SetVisualizationMode(string mode)
        {
            settings.visualizationMode = NormalizeVisualizationMode(mode);
            ApplyVisualizationState();
        }

        public void Play()
        {
            InitializeIfNeeded();
            isPaused = false;
        }

        public void Pause() { isPaused = true; }

        public void InvalidateBoundaryState()
        {
            hasObstacleMaskSignature = false;
            cachedOpeningCount = 0;
            detectedOpenings.Clear();
            diagnostics.valid = false;
            nextDiagnosticsTime = 0f;
        }

        public Bounds GetDomainBounds()
        {
            // Domain = loaded model bounding box + padding
            var modelRoot = GetLoadedModelRoot();
            if (modelRoot != null)
            {
                var renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length > 0)
                {
                    Bounds b = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++)
                        b.Encapsulate(renderers[i].bounds);
                    // Add 15% padding so openings at mesh boundaries are inside the domain
                    b.Expand(b.size * 0.15f);
                    return b;
                }
            }

            Vector3 size = transform.localScale;
            if (size.x < 0.01f || size.y < 0.01f || size.z < 0.01f)
                size = new Vector3(5f, 3f, 3f);
            return new Bounds(transform.position, size);
        }

        void Start()
        {
            InitializeIfNeeded();
            settings.visualizationMode = NormalizeVisualizationMode(settings.visualizationMode);
            ApplyVisualizationState();
            isPaused = true;
        }

        void Update()
        {
            if (!isInitialized) return;
            if (!isPaused && !string.Equals(NormalizeVisualizationMode(settings.visualizationMode), VisualizationStreamlines, StringComparison.OrdinalIgnoreCase))
            {
                EnsureSurfaceVisualizers();
            }
            if (!isPaused && Time.frameCount > 10)
            {
                RunSimulationFrame(Time.deltaTime);
            }
        }

        public void InitializeIfNeeded()
        {
            int gridCount = gridSizeX * gridSizeY * gridSizeZ;
            if (isInitialized && gridCount == initializedGridCount) return;

            if (boundaryManager == null)
            {
                boundaryManager = FindAnyObjectByType<BoundaryConditionManager>();
            }

            if (pipeShader == null)
            {
                pipeShader = Resources.Load<ComputeShader>("Compute/PipeFlow/PipeFlow3D");
                if (pipeShader == null)
                {
                    Debug.LogError("[PipeFlow] Missing compute shader at Resources/Compute/PipeFlow/PipeFlow3D.compute");
                    return;
                }
            }

            ReleaseBuffers();
            gridVelocity    = ComputeHelper.CreateStructuredBuffer<Vector3>(gridCount);
            gridVelocityTmp = ComputeHelper.CreateStructuredBuffer<Vector3>(gridCount);
            gridPressure    = ComputeHelper.CreateStructuredBuffer<float>(gridCount);
            gridPressureTmp = ComputeHelper.CreateStructuredBuffer<float>(gridCount);
            gridDivergence  = ComputeHelper.CreateStructuredBuffer<float>(gridCount);
            obstacleMaskBuffer = ComputeHelper.CreateStructuredBuffer<int>(gridCount);
            openingMaskBuffer  = ComputeHelper.CreateStructuredBuffer<int>(gridCount);
            openingTypesBuffer = ComputeHelper.CreateStructuredBuffer<int>(16); // max 16 openings

            applyForcesKernel    = pipeShader.FindKernel("ApplyForces");
            advectVelocityKernel = pipeShader.FindKernel("AdvectVelocity");
            diffuseKernel        = pipeShader.FindKernel("DiffuseVelocity");
            vorticityKernel      = pipeShader.FindKernel("VorticityConfinement");
            divergenceKernel     = pipeShader.FindKernel("ComputeDivergence");
            jacobiKernel         = pipeShader.FindKernel("JacobiPressure");
            projectKernel        = pipeShader.FindKernel("ProjectVelocity");
            advectParticlesKernel = pipeShader.FindKernel("AdvectParticles");

            BindBuffers();
            initializedGridCount = gridCount;
            isInitialized = true;

            gridVelocity.SetData(new Vector3[gridCount]);
            gridVelocityTmp.SetData(new Vector3[gridCount]);
            gridPressure.SetData(new float[gridCount]);
            gridPressureTmp.SetData(new float[gridCount]);
            gridDivergence.SetData(new float[gridCount]);
            obstacleMaskBuffer.SetData(new int[gridCount]);
            openingMaskBuffer.SetData(new int[gridCount]);

            Debug.Log("[PipeFlow] Initialized. Solver: Mesh-driven Navier-Stokes (GPU).");
        }

        void BindBuffers()
        {
            if (pipeShader == null)
                return;

            int gridCount = Mathf.Max(gridSizeX * gridSizeY * gridSizeZ, 1);
            EnsureCoreSolverBuffers(gridCount);

            ComputeHelper.SetBuffer(pipeShader, gridVelocity, "GridVelocity",
                applyForcesKernel, advectVelocityKernel, diffuseKernel, vorticityKernel,
                divergenceKernel, projectKernel, advectParticlesKernel);
            ComputeHelper.SetBuffer(pipeShader, gridVelocityTmp, "GridVelocityTmp",
                advectVelocityKernel, diffuseKernel, vorticityKernel);
            ComputeHelper.SetBuffer(pipeShader, gridPressure, "GridPressure",
                divergenceKernel, jacobiKernel, projectKernel);
            ComputeHelper.SetBuffer(pipeShader, gridPressureTmp, "GridPressureTmp",
                jacobiKernel);
            ComputeHelper.SetBuffer(pipeShader, gridDivergence, "GridDivergence",
                divergenceKernel, jacobiKernel);
            if (obstacleMaskBuffer != null)
            {
                pipeShader.SetBuffer(applyForcesKernel, "ObstacleMask", obstacleMaskBuffer);
                pipeShader.SetBuffer(advectVelocityKernel, "ObstacleMask", obstacleMaskBuffer);
                pipeShader.SetBuffer(diffuseKernel, "ObstacleMask", obstacleMaskBuffer);
                pipeShader.SetBuffer(vorticityKernel, "ObstacleMask", obstacleMaskBuffer);
                pipeShader.SetBuffer(divergenceKernel, "ObstacleMask", obstacleMaskBuffer);
                pipeShader.SetBuffer(jacobiKernel, "ObstacleMask", obstacleMaskBuffer);
                pipeShader.SetBuffer(projectKernel, "ObstacleMask", obstacleMaskBuffer);
                pipeShader.SetBuffer(advectParticlesKernel, "ObstacleMask", obstacleMaskBuffer);
            }
            if (openingMaskBuffer != null)
            {
                ComputeHelper.SetBuffer(pipeShader, openingMaskBuffer, "OpeningMask",
                    applyForcesKernel, advectVelocityKernel, advectParticlesKernel);
            }
            if (openingTypesBuffer != null)
            {
                ComputeHelper.SetBuffer(pipeShader, openingTypesBuffer, "OpeningTypes",
                    applyForcesKernel, advectVelocityKernel);
            }
        }

        void RunSimulationFrame(float frameTime)
        {
            if (!isInitialized || isPaused) return;
            if (!HasValidBoundaryAssignments())
                return;
            float clampedFrameTime = Mathf.Min(frameTime, 0.05f);
            float timeStep = clampedFrameTime / Mathf.Max(1, settings.iterationsPerFrame) * settings.timeScale;
            currentStepTime = timeStep;
            for (int i = 0; i < settings.iterationsPerFrame; i++)
                RunSimulationStep();
        }

        void RunSimulationStep()
        {
            if (!isInitialized) return;

            Bounds domain = GetDomainBounds();
            Vector3 boundsCenter = domain.center;
            Vector3 boundsSize = domain.size;
            lastBoundsCenter = boundsCenter;
            lastBoundsSize = boundsSize;

            Vector3 cellSize = new Vector3(
                Mathf.Max(boundsSize.x / gridSizeX, 1e-3f),
                Mathf.Max(boundsSize.y / gridSizeY, 1e-3f),
                Mathf.Max(boundsSize.z / gridSizeZ, 1e-3f));
            lastCellSize = cellSize;

            float minCell = Mathf.Min(cellSize.x, Mathf.Min(cellSize.y, cellSize.z));
            float safeDt = Mathf.Clamp(
                Mathf.Min(currentStepTime, minCell / Mathf.Max(settings.inletVelocity, 1f) * 1.2f),
                1e-4f, 0.05f);

            Vector3 flowDir = ResolveFlowDirection();
            int gridCount = gridSizeX * gridSizeY * gridSizeZ;

            pipeShader.SetInt("gridSizeX", gridSizeX);
            pipeShader.SetInt("gridSizeY", gridSizeY);
            pipeShader.SetInt("gridSizeZ", gridSizeZ);
            pipeShader.SetInt("gridCount", gridCount);
            pipeShader.SetInt("particleCount", 0);
            pipeShader.SetFloat("deltaTime", safeDt);
            pipeShader.SetFloat("viscosity", Mathf.Max(settings.dynamicViscosity, 1e-6f));
            pipeShader.SetFloat("velocityDamping", velocityDamping);
            pipeShader.SetFloat("inletVelocity", Mathf.Max(settings.inletVelocity, 0f));
            pipeShader.SetFloat("turbulenceIntensity", Mathf.Clamp01(settings.turbulenceIntensity * 0.01f));
            pipeShader.SetFloat("vorticityStrength", Mathf.Max(0f, vorticityConfinement));
            pipeShader.SetVector("flowDirection", flowDir);
            pipeShader.SetVector("boundsCenter", boundsCenter);
            pipeShader.SetVector("boundsSize", boundsSize);
            pipeShader.SetVector("cellSize", cellSize);
            pipeShader.SetFloat("timeValue", Time.time);
            pipeShader.SetInt("openingCount", cachedOpeningCount);

            // Update opening types from BoundaryConditionManager
            UpdateOpeningTypesBuffer();

            // Build inverted obstacle mask from the loaded model
            UpdateObstacleMaskIfNeeded(GetLoadedModelRoot(), domain, gridCount);

            // Dispatch solver
            ComputeHelper.Dispatch(pipeShader, gridSizeX, gridSizeY, gridSizeZ, applyForcesKernel);
            ComputeHelper.Dispatch(pipeShader, gridSizeX, gridSizeY, gridSizeZ, advectVelocityKernel);
            Swap(ref gridVelocity, ref gridVelocityTmp); BindBuffers();

            for (int i = 0; i < diffusionIterations; i++)
            {
                ComputeHelper.Dispatch(pipeShader, gridSizeX, gridSizeY, gridSizeZ, diffuseKernel);
                Swap(ref gridVelocity, ref gridVelocityTmp); BindBuffers();
            }

            if (vorticityConfinement > 1e-4f)
            {
                ComputeHelper.Dispatch(pipeShader, gridSizeX, gridSizeY, gridSizeZ, vorticityKernel);
                Swap(ref gridVelocity, ref gridVelocityTmp); BindBuffers();
            }

            ComputeHelper.Dispatch(pipeShader, gridSizeX, gridSizeY, gridSizeZ, divergenceKernel);

            for (int i = 0; i < pressureIterations; i++)
            {
                ComputeHelper.Dispatch(pipeShader, gridSizeX, gridSizeY, gridSizeZ, jacobiKernel);
                Swap(ref gridPressure, ref gridPressureTmp); BindBuffers();
            }

            ComputeHelper.Dispatch(pipeShader, gridSizeX, gridSizeY, gridSizeZ, projectKernel);

            if (Time.time >= nextDiagnosticsTime)
            {
                UpdateDiagnostics();
                nextDiagnosticsTime = Time.time + 0.35f;
            }
        }

        public Vector3 ResolveFlowDirection()
        {
            switch (flowAxis)
            {
                case WindTunnelFlowAxis.LocalX: return transform.right;
                case WindTunnelFlowAxis.LocalY: return transform.up;
                case WindTunnelFlowAxis.LocalZ: return transform.forward;
                default: return Vector3.right;
            }
        }

        Transform GetLoadedModelRoot()
        {
            var model = RuntimeModelLookup.GetLoadedModel();
            return model != null ? model.transform : null;
        }

        public bool TryGetVelocityFieldSnapshot(
            out Vector3[] velocities,
            out Vector3 origin,
            out Vector3 cellSize,
            out int sizeX,
            out int sizeY,
            out int sizeZ)
        {
            velocities = null;
            sizeX = gridSizeX;
            sizeY = gridSizeY;
            sizeZ = gridSizeZ;

            Bounds domain = GetDomainBounds();
            cellSize = new Vector3(
                Mathf.Max(domain.size.x / Mathf.Max(1, gridSizeX), 1e-3f),
                Mathf.Max(domain.size.y / Mathf.Max(1, gridSizeY), 1e-3f),
                Mathf.Max(domain.size.z / Mathf.Max(1, gridSizeZ), 1e-3f));
            origin = domain.center - domain.extents;

            if (!isInitialized || gridVelocity == null)
                return false;

            int gridCount = gridSizeX * gridSizeY * gridSizeZ;
            if (gridCount <= 0)
                return false;

            if (diagnosticsVelocityCache == null || diagnosticsVelocityCache.Length != gridCount)
                diagnosticsVelocityCache = new Vector3[gridCount];

            gridVelocity.GetData(diagnosticsVelocityCache);
            velocities = diagnosticsVelocityCache;
            return true;
        }

        /// <summary>
        /// Returns the inverted obstacle mask (0 = fluid, 1 = wall) for external use.
        /// </summary>
        public int[] GetObstacleMask() => obstacleMaskCache;

        /// <summary>
        /// Returns the opening mask for external use.
        /// </summary>
        public int[] GetOpeningMask() => openingMaskCache;

        void UpdateObstacleMaskIfNeeded(Transform obstacleRoot, Bounds domain, int gridCount)
        {
            if (obstacleMaskBuffer == null || gridCount <= 0)
                return;

            if (obstacleMaskCache == null || obstacleMaskCache.Length != gridCount)
            {
                obstacleMaskCache = new int[gridCount];
                openingMaskCache = new int[gridCount];
                hasObstacleMaskSignature = false;
            }

            int signature = ComputeObstacleMaskSignature(obstacleRoot, domain);
            if (hasObstacleMaskSignature && signature == obstacleMaskSignature)
                return;

            obstacleMaskSignature = signature;
            hasObstacleMaskSignature = true;
            System.Array.Clear(obstacleMaskCache, 0, obstacleMaskCache.Length);
            System.Array.Clear(openingMaskCache, 0, openingMaskCache.Length);

            if (obstacleRoot != null)
            {
                // Get openings from boundary manager to seed the voxelization
                var detectedOps = new List<DetectedOpening>();
                if (boundaryManager != null)
                {
                    foreach (var a in boundaryManager.Assignments)
                        if (a.opening != null) detectedOps.Add(a.opening);
                }

                int fluidCount, openCount;
                bool ok = ObstacleVoxelizer.BuildInvertedMask(
                    obstacleRoot,
                    domain,
                    gridSizeX, gridSizeY, gridSizeZ,
                    obstacleMaskCache,
                    openingMaskCache,
                    detectedOps,
                    out fluidCount,
                    out openCount);

                cachedFluidCellCount = fluidCount;
                cachedOpeningCount = openCount;

                if (ok)
                {
                    Debug.Log($"[PipeFlow] Inverted voxelization: {fluidCount} fluid cells, {openCount} openings detected.");
                    BuildOpeningInfoList(domain);
                }
                else
                {
                    Debug.LogWarning("[PipeFlow] Inverted voxelization failed — model may not be a closed pipe.");
                }
            }

            obstacleMaskBuffer.SetData(obstacleMaskCache);
            openingMaskBuffer.SetData(openingMaskCache);
            BindBuffers();
        }

        void BuildOpeningInfoList(Bounds domain)
        {
            detectedOpenings.Clear();
            if (openingMaskCache == null) return;

            // Group cells by opening ID and compute centroid + normal
            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < openingMaskCache.Length; i++)
            {
                int oid = openingMaskCache[i];
                if (oid <= 0) continue;
                if (!groups.ContainsKey(oid))
                    groups[oid] = new List<int>();
                groups[oid].Add(i);
            }

            Vector3 minB = domain.center - domain.extents;

            foreach (var kv in groups)
            {
                Vector3 centroid = Vector3.zero;
                Vector3 normal = Vector3.zero;
                foreach (int idx in kv.Value)
                {
                    int x = idx % gridSizeX;
                    int yz = idx / gridSizeX;
                    int y = yz % gridSizeY;
                    int z = yz / gridSizeY;

                    Vector3 cellCenter = minB + new Vector3(
                        (x + 0.5f) * lastCellSize.x,
                        (y + 0.5f) * lastCellSize.y,
                        (z + 0.5f) * lastCellSize.z);
                    centroid += cellCenter;

                    // Estimate normal from domain boundary proximity
                    if (x <= 1) normal += Vector3.left;
                    if (x >= gridSizeX - 2) normal += Vector3.right;
                    if (y <= 1) normal += Vector3.down;
                    if (y >= gridSizeY - 2) normal += Vector3.up;
                    if (z <= 1) normal += Vector3.back;
                    if (z >= gridSizeZ - 2) normal += Vector3.forward;
                }
                centroid /= kv.Value.Count;
                normal = normal.normalized;
                if (normal.sqrMagnitude < 0.01f) normal = Vector3.right;

                // Estimate radius from cell spread
                float maxDist = 0f;
                foreach (int idx in kv.Value)
                {
                    int x = idx % gridSizeX;
                    int yz = idx / gridSizeX;
                    int y = yz % gridSizeY;
                    int z = yz / gridSizeY;
                    Vector3 cellCenter = minB + new Vector3(
                        (x + 0.5f) * lastCellSize.x,
                        (y + 0.5f) * lastCellSize.y,
                        (z + 0.5f) * lastCellSize.z);
                    float d = Vector3.Distance(cellCenter, centroid);
                    if (d > maxDist) maxDist = d;
                }

                detectedOpenings.Add(new OpeningInfo
                {
                    id = kv.Key,
                    position = centroid,
                    normal = normal,
                    radius = Mathf.Max(maxDist, lastCellSize.magnitude),
                    cellCount = kv.Value.Count
                });
            }
        }

        int ComputeObstacleMaskSignature(Transform obstacleRoot, Bounds domain)
        {
            int hash = 17;
            hash = hash * 31 + gridSizeX;
            hash = hash * 31 + gridSizeY;
            hash = hash * 31 + gridSizeZ;
            hash = hash * 31 + Quantize(domain.center.x, 100f);
            hash = hash * 31 + Quantize(domain.center.y, 100f);
            hash = hash * 31 + Quantize(domain.center.z, 100f);
            hash = hash * 31 + Quantize(domain.size.x, 100f);
            hash = hash * 31 + Quantize(domain.size.y, 100f);
            hash = hash * 31 + Quantize(domain.size.z, 100f);

            if (obstacleRoot != null)
            {
                Vector3 pos = obstacleRoot.position;
                Vector3 scale = obstacleRoot.lossyScale;
                Vector3 euler = obstacleRoot.rotation.eulerAngles;
                hash = hash * 31 + obstacleRoot.GetInstanceID();
                hash = hash * 31 + Quantize(pos.x, 100f);
                hash = hash * 31 + Quantize(pos.y, 100f);
                hash = hash * 31 + Quantize(pos.z, 100f);
                hash = hash * 31 + Quantize(scale.x, 100f);
                hash = hash * 31 + Quantize(scale.y, 100f);
                hash = hash * 31 + Quantize(scale.z, 100f);
                hash = hash * 31 + Quantize(euler.x, 10f);
                hash = hash * 31 + Quantize(euler.y, 10f);
                hash = hash * 31 + Quantize(euler.z, 10f);
            }

            return hash;
        }

        static int Quantize(float value, float scale)
        {
            if (!float.IsFinite(value)) return 0;
            return Mathf.RoundToInt(value * scale);
        }

        void UpdateDiagnostics()
        {
            if (gridVelocity == null || gridPressure == null || gridDivergence == null) return;
            int gridCount = gridSizeX * gridSizeY * gridSizeZ;
            if (gridCount <= 0) return;

            if (diagnosticsVelocityCache == null || diagnosticsVelocityCache.Length != gridCount)
                diagnosticsVelocityCache = new Vector3[gridCount];
            if (diagnosticsPressureCache == null || diagnosticsPressureCache.Length != gridCount)
                diagnosticsPressureCache = new float[gridCount];
            if (diagnosticsDivergenceCache == null || diagnosticsDivergenceCache.Length != gridCount)
                diagnosticsDivergenceCache = new float[gridCount];

            gridVelocity.GetData(diagnosticsVelocityCache);
            gridPressure.GetData(diagnosticsPressureCache);
            gridDivergence.GetData(diagnosticsDivergenceCache);

            float sumSpeed = 0f, maxSpeed = 0f, sumAbsDiv = 0f;
            int fluidCellCount = 0;

            for (int i = 0; i < gridCount; i++)
            {
                // Only count fluid cells (not walls)
                if (obstacleMaskCache != null && obstacleMaskCache.Length > i && obstacleMaskCache[i] != 0)
                    continue;

                float s = diagnosticsVelocityCache[i].magnitude;
                sumSpeed += s;
                if (s > maxSpeed) maxSpeed = s;
                fluidCellCount++;
                sumAbsDiv += Mathf.Abs(diagnosticsDivergenceCache[i]);
            }
            float meanVelocity = fluidCellCount > 0 ? sumSpeed / fluidCellCount : 0f;

            // Pressure drop: compare average pressure across detected openings
            float pressureDrop = 0f;
            if (detectedOpenings.Count >= 2)
            {
                float p0 = AveragePressureAtOpening(detectedOpenings[0]);
                float p1 = AveragePressureAtOpening(detectedOpenings[detectedOpenings.Count - 1]);
                pressureDrop = Mathf.Abs(p0 - p1);
            }
            if (pressureDrop < 1e-5f)
            {
                pressureDrop = Mathf.Max(0.5f * settings.fluidDensity * meanVelocity * meanVelocity * 0.02f, 0.1f);
            }

            // Wall shear: average speed of fluid cells adjacent to walls
            float wallShear = EstimateWallShearStress(gridCount);

            // Cross-section flow rate estimate
            float flowRate = 0f;
            if (detectedOpenings.Count > 0)
            {
                float openingArea = Mathf.PI * detectedOpenings[0].radius * detectedOpenings[0].radius;
                flowRate = meanVelocity * openingArea;
            }

            // Reynolds number (using hydraulic diameter estimate)
            float hydraulicDiameter = detectedOpenings.Count > 0 ? 2f * detectedOpenings[0].radius : 0.5f;
            float pipeRe = settings.fluidDensity * meanVelocity * hydraulicDiameter / Mathf.Max(settings.dynamicViscosity, 1e-6f);

            diagnostics = new PipeFlowDiagnostics
            {
                valid = true,
                meanVelocity = meanVelocity,
                maxVelocity = maxSpeed,
                pressureDrop = pressureDrop,
                wallShear = wallShear,
                divergenceL1 = sumAbsDiv / Mathf.Max(gridCount, 1),
                flowRate = flowRate,
                fluidCellCount = fluidCellCount,
                openingCount = cachedOpeningCount,
                pipeReynolds = pipeRe
            };

            OnSimulationCompleted?.Invoke();
        }

        float AveragePressureAtOpening(OpeningInfo opening)
        {
            if (openingMaskCache == null || diagnosticsPressureCache == null) return 0f;
            float sum = 0f;
            int count = 0;
            for (int i = 0; i < openingMaskCache.Length; i++)
            {
                if (openingMaskCache[i] == opening.id)
                {
                    sum += diagnosticsPressureCache[i];
                    count++;
                }
            }
            return count > 0 ? sum / count : 0f;
        }

        float EstimateWallShearStress(int gridCount)
        {
            if (obstacleMaskCache == null || diagnosticsVelocityCache == null) return 0f;
            float sumSpeed = 0f;
            int count = 0;

            for (int i = 0; i < gridCount; i++)
            {
                if (obstacleMaskCache[i] != 0) continue; // skip walls

                int x = i % gridSizeX;
                int yz = i / gridSizeX;
                int y = yz % gridSizeY;
                int z = yz / gridSizeY;

                // Check if adjacent to wall
                bool nearWall = false;
                if (x > 0 && obstacleMaskCache[Flatten(x-1, y, z)] != 0) nearWall = true;
                if (!nearWall && x < gridSizeX-1 && obstacleMaskCache[Flatten(x+1, y, z)] != 0) nearWall = true;
                if (!nearWall && y > 0 && obstacleMaskCache[Flatten(x, y-1, z)] != 0) nearWall = true;
                if (!nearWall && y < gridSizeY-1 && obstacleMaskCache[Flatten(x, y+1, z)] != 0) nearWall = true;
                if (!nearWall && z > 0 && obstacleMaskCache[Flatten(x, y, z-1)] != 0) nearWall = true;
                if (!nearWall && z < gridSizeZ-1 && obstacleMaskCache[Flatten(x, y, z+1)] != 0) nearWall = true;

                if (nearWall)
                {
                    sumSpeed += diagnosticsVelocityCache[i].magnitude;
                    count++;
                }
            }

            float avgNearWallSpeed = count > 0 ? sumSpeed / count : 0f;
            float dy = Mathf.Max(lastCellSize.y, Mathf.Min(lastCellSize.x, lastCellSize.z));
            return Mathf.Max(settings.dynamicViscosity, 1e-6f) * avgNearWallSpeed / Mathf.Max(dy, 1e-3f);
        }

        int Flatten(int x, int y, int z)
        {
            return x + gridSizeX * (y + gridSizeY * z);
        }

        static void Swap(ref ComputeBuffer a, ref ComputeBuffer b)
        {
            (a, b) = (b, a);
        }

        void ReleaseBuffers()
        {
            ComputeHelper.Release(gridVelocity, gridVelocityTmp, gridPressure, gridPressureTmp,
                gridDivergence, obstacleMaskBuffer);
            if (openingMaskBuffer != null)
            {
                openingMaskBuffer.Release();
                openingMaskBuffer = null;
            }
            if (openingTypesBuffer != null)
            {
                openingTypesBuffer.Release();
                openingTypesBuffer = null;
            }
            gridVelocity = gridVelocityTmp = null;
            gridPressure = gridPressureTmp = null;
            gridDivergence = null;
            obstacleMaskBuffer = null;
            obstacleMaskCache = null;
            openingMaskCache = null;
            hasObstacleMaskSignature = false;
            isInitialized = false;
        }

        void UpdateOpeningTypesBuffer()
        {
            if (openingTypesBuffer == null) return;
            if (boundaryManager == null) boundaryManager = FindAnyObjectByType<BoundaryConditionManager>();
            if (boundaryManager == null) return;

            var assignments = boundaryManager.Assignments;
            int[] types = new int[16];
            for (int i = 0; i < Mathf.Min(assignments.Count, 16); i++)
            {
                // Mapping: Unassigned=0, Inlet=1, Outlet=2
                // BoundaryType enum: Unassigned=0, Inlet=1, Outlet=2
                types[i] = (int)assignments[i].type;
            }
            openingTypesBuffer.SetData(types);
        }

        public bool TryGetPressureRange(out float minPressure, out float maxPressure)
        {
            minPressure = 0f;
            maxPressure = 0f;
            if (diagnosticsPressureCache == null || obstacleMaskCache == null)
                return false;

            bool found = false;
            int count = Mathf.Min(diagnosticsPressureCache.Length, obstacleMaskCache.Length);
            for (int i = 0; i < count; i++)
            {
                if (obstacleMaskCache[i] != 0)
                    continue;

                float pressure = diagnosticsPressureCache[i];
                if (!float.IsFinite(pressure))
                    continue;

                if (!found)
                {
                    minPressure = pressure;
                    maxPressure = pressure;
                    found = true;
                }
                else
                {
                    if (pressure < minPressure) minPressure = pressure;
                    if (pressure > maxPressure) maxPressure = pressure;
                }
            }

            return found;
        }

        public bool TryGetWallShearRange(out float minWallShear, out float maxWallShear)
        {
            minWallShear = 0f;
            maxWallShear = 0f;
            if (diagnosticsVelocityCache == null || obstacleMaskCache == null)
                return false;

            float sampleStep = Mathf.Max(Mathf.Min(lastCellSize.x, Mathf.Min(lastCellSize.y, lastCellSize.z)), 1e-3f);
            float viscosity = Mathf.Max(settings.dynamicViscosity, 1e-6f);
            bool found = false;
            int count = Mathf.Min(diagnosticsVelocityCache.Length, obstacleMaskCache.Length);
            for (int i = 0; i < count; i++)
            {
                if (obstacleMaskCache[i] != 0)
                    continue;

                float speed = diagnosticsVelocityCache[i].magnitude;
                if (!float.IsFinite(speed))
                    continue;

                float wallShear = viscosity * speed / Mathf.Max(sampleStep, 1e-4f);
                if (!found)
                {
                    minWallShear = wallShear;
                    maxWallShear = wallShear;
                    found = true;
                }
                else
                {
                    if (wallShear < minWallShear) minWallShear = wallShear;
                    if (wallShear > maxWallShear) maxWallShear = wallShear;
                }
            }

            return found;
        }

        public bool TrySampleSurfaceScalars(Vector3 worldPosition, Vector3 worldNormal, out float pressure, out float wallShear)
        {
            pressure = 0f;
            wallShear = 0f;
            if (diagnosticsVelocityCache == null || diagnosticsPressureCache == null || obstacleMaskCache == null)
                return false;

            float sampleStep = Mathf.Max(Mathf.Min(lastCellSize.x, Mathf.Min(lastCellSize.y, lastCellSize.z)), 1e-3f);
            Vector3 normal = worldNormal.sqrMagnitude > 1e-8f ? worldNormal.normalized : Vector3.up;
            bool found = false;
            float bestSpeed = -1f;

            TrySurfaceDirection(worldPosition, normal, sampleStep, ref found, ref bestSpeed, ref pressure, ref wallShear);
            TrySurfaceDirection(worldPosition, -normal, sampleStep, ref found, ref bestSpeed, ref pressure, ref wallShear);
            return found;
        }

        public bool TrySampleField(Vector3 worldPosition, out Vector3 velocity, out float pressure)
        {
            velocity = Vector3.zero;
            pressure = 0f;

            if (diagnosticsVelocityCache == null || diagnosticsPressureCache == null || obstacleMaskCache == null)
                return false;

            if (!TryGetCellCoordinates(worldPosition, out int x, out int y, out int z))
                return false;

            int index = Flatten(x, y, z);
            if (index < 0 || index >= obstacleMaskCache.Length || obstacleMaskCache[index] != 0)
                return false;

            velocity = diagnosticsVelocityCache[index];
            pressure = diagnosticsPressureCache[index];
            return float.IsFinite(velocity.x) && float.IsFinite(velocity.y) && float.IsFinite(velocity.z) && float.IsFinite(pressure);
        }

        private void TrySurfaceDirection(
            Vector3 worldPosition,
            Vector3 direction,
            float sampleStep,
            ref bool found,
            ref float bestSpeed,
            ref float pressure,
            ref float wallShear)
        {
            float[] offsets = { 0.45f, 0.85f, 1.25f };
            for (int i = 0; i < offsets.Length; i++)
            {
                float offset = sampleStep * offsets[i];
                Vector3 samplePosition = worldPosition + direction * offset;
                if (!TrySampleField(samplePosition, out Vector3 velocity, out float sampledPressure))
                    continue;

                Vector3 tangentVelocity = Vector3.ProjectOnPlane(velocity, direction.normalized);
                float tangentialSpeed = tangentVelocity.magnitude;
                if (tangentialSpeed <= bestSpeed)
                    continue;

                bestSpeed = tangentialSpeed;
                pressure = sampledPressure;
                wallShear = Mathf.Max(settings.dynamicViscosity, 1e-6f) * tangentialSpeed / Mathf.Max(offset, 1e-4f);
                found = true;
            }
        }

        void EnsureCoreSolverBuffers(int gridCount)
        {
            EnsureStructuredBuffer<Vector3>(ref gridVelocity, gridCount);
            EnsureStructuredBuffer<Vector3>(ref gridVelocityTmp, gridCount);
            EnsureStructuredBuffer<float>(ref gridPressure, gridCount);
            EnsureStructuredBuffer<float>(ref gridPressureTmp, gridCount);
            EnsureStructuredBuffer<float>(ref gridDivergence, gridCount);
            EnsureStructuredBuffer<int>(ref obstacleMaskBuffer, gridCount);
            EnsureStructuredBuffer<int>(ref openingMaskBuffer, gridCount);

            if (openingTypesBuffer == null || !openingTypesBuffer.IsValid() || openingTypesBuffer.count != 16)
            {
                if (openingTypesBuffer != null)
                {
                    openingTypesBuffer.Release();
                }
                openingTypesBuffer = ComputeHelper.CreateStructuredBuffer<int>(16);
                openingTypesBuffer.SetData(new int[16]);
            }
        }

        void EnsureStructuredBuffer<T>(ref ComputeBuffer buffer, int count)
        {
            int safeCount = Mathf.Max(count, 1);
            if (buffer != null && buffer.IsValid() && buffer.count == safeCount)
                return;

            ComputeHelper.CreateStructuredBuffer<T>(ref buffer, safeCount);
        }

        private bool TryGetCellCoordinates(Vector3 worldPosition, out int x, out int y, out int z)
        {
            x = y = z = 0;
            if (lastBoundsSize.x <= 0f || lastBoundsSize.y <= 0f || lastBoundsSize.z <= 0f)
                return false;

            Vector3 min = lastBoundsCenter - lastBoundsSize * 0.5f;
            float fx = (worldPosition.x - min.x) / Mathf.Max(lastCellSize.x, 1e-5f);
            float fy = (worldPosition.y - min.y) / Mathf.Max(lastCellSize.y, 1e-5f);
            float fz = (worldPosition.z - min.z) / Mathf.Max(lastCellSize.z, 1e-5f);

            x = Mathf.FloorToInt(fx);
            y = Mathf.FloorToInt(fy);
            z = Mathf.FloorToInt(fz);
            return x >= 0 && x < gridSizeX && y >= 0 && y < gridSizeY && z >= 0 && z < gridSizeZ;
        }

        private void ApplyVisualizationState()
        {
            string normalized = NormalizeVisualizationMode(settings.visualizationMode);
            bool surfaceMode = string.Equals(normalized, VisualizationSurfacePressure, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, VisualizationSurfaceFriction, StringComparison.OrdinalIgnoreCase);

            if (surfaceMode)
            {
                EnsureSurfaceVisualizers();
            }

            var visualizers = FindObjectsByType<InternalSurfacePressureVisualizer>(FindObjectsSortMode.None);
            for (int i = 0; i < visualizers.Length; i++)
            {
                var visualizer = visualizers[i];
                if (visualizer == null)
                    continue;

                if (string.Equals(normalized, VisualizationSurfaceFriction, StringComparison.OrdinalIgnoreCase))
                {
                    visualizer.enabled = true;
                    visualizer.SetSurfaceMode(InternalSurfacePressureVisualizer.SurfaceMode.Friction);
                    visualizer.UpdateSurfaceColors();
                }
                else if (string.Equals(normalized, VisualizationSurfacePressure, StringComparison.OrdinalIgnoreCase))
                {
                    visualizer.enabled = true;
                    visualizer.SetSurfaceMode(InternalSurfacePressureVisualizer.SurfaceMode.Pressure);
                    visualizer.UpdateSurfaceColors();
                }
                else
                {
                    visualizer.enabled = false;
                }
            }
        }

        private void EnsureSurfaceVisualizers()
        {
            Transform modelRoot = GetLoadedModelRoot();
            if (modelRoot == null)
                return;

            var renderers = modelRoot.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null || !HasTriangleSubmesh(mesh))
                    continue;

                if (renderer.GetComponent<InternalSurfacePressureVisualizer>() == null)
                {
                    renderer.gameObject.AddComponent<InternalSurfacePressureVisualizer>();
                }
            }
        }

        private static bool HasTriangleSubmesh(Mesh mesh)
        {
            if (mesh == null)
                return false;

            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                if (mesh.GetTopology(i) == MeshTopology.Triangles)
                    return true;
            }

            return false;
        }

        void OnDestroy() { ReleaseBuffers(); }
    }
}
