using UnityEngine;
using AeroFlow.Core;

namespace AeroFlow.Sim3D.RotatingMachinery
{
    public enum RotatoryApplicationType
    {
        Windmill,
        Fan,
        Propeller,
        AxialTurbine,
        RadialTurbine
    }

    public enum RotatoryRotationDirection
    {
        Clockwise,
        CounterClockwise
    }

    public enum RotatoryMotionMode
    {
        ConstantSpeed,
        FluidDrivenAdaptive,
        TorqueCoupledFuture
    }

    [System.Serializable]
    public class RotatingMachinerySettings
    {
        public RotatoryApplicationType applicationType = RotatoryApplicationType.Windmill;
        public RotatoryRotationDirection rotationDirection = RotatoryRotationDirection.CounterClockwise;
        public RotatoryMotionMode motionMode = RotatoryMotionMode.ConstantSpeed;
        public float inletVelocity = 20f;
        public float fluidDensity = 1.225f;
        public float dynamicViscosity = 1.81e-5f;
        public float turbulenceIntensity = 2.0f;
        public float angularVelocityRPM = 1500f;
        public Vector3 rotationAxis = Vector3.up;
        public float rotatingZoneRadius = 1.0f;
        public float rotatingZoneHalfHeight = 0.5f;
        public float rotatingZoneAxisOffset = 0f;
        public float tipSpeedRatio = 0f;
        public string visualizationMode = WindTunnelSimulation3D.VisualizationStreamlines;
        public float timeScale = 1.0f;
        public int iterationsPerFrame = 4;
        public WindTunnelVehicleProperties vehicle = new WindTunnelVehicleProperties();
    }

    [System.Serializable]
    public struct RotatingMachineryDiagnostics
    {
        public bool valid;
        public float meanVelocity;
        public float maxVelocity;
        public float pressureDrop;
        public float wallShear;
        public float divergenceL1;
        public float torque;
        public float power;
        public float efficiency;
        public float angularVelocityRadS;
        public float machineReynolds;
        public float meanSwirl;
        public float tipSpeedRatio;
        public float wakeVelocityDeficit;
        public string energyDirection;
        public string applicationLabel;
    }

    public class RotatingMachinerySimulation3D : MonoBehaviour
    {
        public event System.Action OnSimulationCompleted;
        public RotatingMachinerySettings settings = new RotatingMachinerySettings();

        [Header("Grid Resolution")]
        [Range(16, 128)] public int gridSizeX = 48;
        [Range(8, 96)]  public int gridSizeY = 48;
        [Range(8, 96)]  public int gridSizeZ = 48;

        [Header("Solver Quality")]
        [Range(1, 12)] public int diffusionIterations = 3;
        [Range(4, 80)] public int pressureIterations = 24;
        [Range(0f, 0.05f)] public float velocityDamping = 0.002f;
        [Range(0f, 3f)]    public float vorticityConfinement = 0.8f;

        [Header("Flow Alignment")]
        public WindTunnelFlowAxis flowAxis = WindTunnelFlowAxis.LocalX;
        public bool showPreviewGizmos = true;

        bool isPaused = true;
        bool isInitialized;
        float currentStepTime = 1f / 60f;
        RotatingMachineryDiagnostics diagnostics;
        float nextDiagnosticsTime;

        ComputeShader machineryShader;
        ComputeBuffer gridVelocity, gridVelocityTmp;
        ComputeBuffer gridPressure, gridPressureTmp;
        ComputeBuffer gridDivergence;
        ComputeBuffer obstacleMaskBuffer;
        ComputeBuffer obstacleVelocityBuffer;

        int applyForcesKernel, advectVelocityKernel, diffuseKernel, vorticityKernel;
        int divergenceKernel, jacobiKernel, projectKernel, advectParticlesKernel;
        int initializedGridCount;
        int[] obstacleMaskCache;
        int obstacleMaskSignature;
        bool hasObstacleMaskSignature;
        int lastLoggedObstacleMaskSolidCount = -1;
        float nextObstacleLogTime;

        private Vector3[] obstacleVelocityCache;
        private PartRegistry cachedPartRegistry;
        private Transform cachedPartRegistryRoot;
        private string lastVisualizationMode = "";

        private UnityEngine.Rendering.AsyncGPUReadbackRequest velReq;
        private UnityEngine.Rendering.AsyncGPUReadbackRequest pReq;
        private UnityEngine.Rendering.AsyncGPUReadbackRequest divReq;
        private bool diagnosticsPending = false;
        Vector3 lastCellSize = Vector3.one;
        Vector3 lastBoundsCenter, lastBoundsSize;
        Vector3[] diagnosticsVelocityCache;
        float[] diagnosticsPressureCache;
        float[] diagnosticsDivergenceCache;

        public bool TryGetDiagnostics(out RotatingMachineryDiagnostics value)
        {
            value = diagnostics;
            return diagnostics.valid;
        }

        public bool IsPaused => isPaused;

        public void Play()
        {
            InitializeIfNeeded();
            isPaused = false;
        }

        public void Pause() { isPaused = true; }

        public Bounds GetDomainBounds()
        {
            Vector3 size = transform.localScale;
            if (size.x < 0.01f || size.y < 0.01f || size.z < 0.01f)
                size = new Vector3(6f, 4f, 4f);
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
            if (!isPaused && Time.frameCount > 10)
                RunSimulationFrame(Time.deltaTime);
        }

        public void InitializeIfNeeded()
        {
            int gridCount = gridSizeX * gridSizeY * gridSizeZ;
            if (isInitialized && gridCount == initializedGridCount) return;

            if (machineryShader == null)
            {
                machineryShader = Resources.Load<ComputeShader>("Compute/RotatingMachinery/RotatingMachinery3D");
                if (machineryShader == null)
                {
                    Debug.LogError("[RotatingMachinery] Missing compute shader at Resources/Compute/RotatingMachinery/RotatingMachinery3D.compute");
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
            obstacleVelocityBuffer = ComputeHelper.CreateStructuredBuffer<Vector3>(gridCount);

            applyForcesKernel    = machineryShader.FindKernel("ApplyForces");
            advectVelocityKernel = machineryShader.FindKernel("AdvectVelocity");
            diffuseKernel        = machineryShader.FindKernel("DiffuseVelocity");
            vorticityKernel      = machineryShader.FindKernel("VorticityConfinement");
            divergenceKernel     = machineryShader.FindKernel("ComputeDivergence");
            jacobiKernel         = machineryShader.FindKernel("JacobiPressure");
            projectKernel        = machineryShader.FindKernel("ProjectVelocity");
            advectParticlesKernel = machineryShader.FindKernel("AdvectParticles");

            BindBuffers();
            initializedGridCount = gridCount;
            isInitialized = true;

            gridVelocity.SetData(new Vector3[gridCount]);
            gridVelocityTmp.SetData(new Vector3[gridCount]);
            gridPressure.SetData(new float[gridCount]);
            gridPressureTmp.SetData(new float[gridCount]);
            gridDivergence.SetData(new float[gridCount]);
            obstacleMaskBuffer.SetData(new int[gridCount]);
            obstacleVelocityBuffer.SetData(new Vector3[gridCount]);
        }

        void BindBuffers()
        {
            ComputeHelper.SetBuffer(machineryShader, gridVelocity, "GridVelocity",
                applyForcesKernel, advectVelocityKernel, diffuseKernel, vorticityKernel,
                divergenceKernel, projectKernel, advectParticlesKernel);
            ComputeHelper.SetBuffer(machineryShader, gridVelocityTmp, "GridVelocityTmp",
                advectVelocityKernel, diffuseKernel, vorticityKernel);
            ComputeHelper.SetBuffer(machineryShader, gridPressure, "GridPressure",
                divergenceKernel, jacobiKernel, projectKernel);
            ComputeHelper.SetBuffer(machineryShader, gridPressureTmp, "GridPressureTmp",
                jacobiKernel);
            ComputeHelper.SetBuffer(machineryShader, gridDivergence, "GridDivergence",
                divergenceKernel, jacobiKernel);
            if (obstacleMaskBuffer != null)
            {
                ComputeHelper.SetBuffer(machineryShader, obstacleMaskBuffer, "ObstacleMask",
                    applyForcesKernel, advectVelocityKernel, vorticityKernel, projectKernel, advectParticlesKernel);
                ComputeHelper.SetBuffer(machineryShader, obstacleVelocityBuffer, "ObstacleVelocity",
                    applyForcesKernel, advectVelocityKernel, vorticityKernel, projectKernel);
            }
        }

        void RunSimulationFrame(float frameTime)
        {
            if (!isInitialized || isPaused) return;
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
            float signedRpm = settings.angularVelocityRPM * (settings.rotationDirection == RotatoryRotationDirection.Clockwise ? 1f : -1f);
            float angularVelocityRadS = signedRpm * Mathf.PI / 30f;
            int gridCount = gridSizeX * gridSizeY * gridSizeZ;

            machineryShader.SetInt("gridSizeX", gridSizeX);
            machineryShader.SetInt("gridSizeY", gridSizeY);
            machineryShader.SetInt("gridSizeZ", gridSizeZ);
            machineryShader.SetInt("gridCount", gridCount);
            machineryShader.SetInt("particleCount", 0);
            machineryShader.SetFloat("deltaTime", safeDt);
            machineryShader.SetFloat("viscosity", Mathf.Max(settings.dynamicViscosity, 1e-6f));
            machineryShader.SetFloat("velocityDamping", velocityDamping);
            machineryShader.SetFloat("inletVelocity", Mathf.Max(settings.inletVelocity, 0f));
            machineryShader.SetFloat("turbulenceIntensity", Mathf.Clamp01(settings.turbulenceIntensity * 0.01f));
            machineryShader.SetFloat("vorticityStrength", Mathf.Max(0f, vorticityConfinement));
            machineryShader.SetVector("flowDirection", flowDir);
            machineryShader.SetVector("boundsCenter", boundsCenter);
            machineryShader.SetVector("boundsSize", boundsSize);
            machineryShader.SetVector("cellSize", cellSize);
            machineryShader.SetFloat("timeValue", Time.time);

            // MRF-specific
            Vector3 rotAxis = settings.rotationAxis.sqrMagnitude > 1e-6f
                ? settings.rotationAxis.normalized : Vector3.up;
            machineryShader.SetVector("rotationAxis", rotAxis);
            machineryShader.SetFloat("angularVelocity", angularVelocityRadS);
            machineryShader.SetVector("rotatingZoneCenter", boundsCenter + rotAxis * settings.rotatingZoneAxisOffset);
            machineryShader.SetFloat("rotatingZoneRadius", Mathf.Max(settings.rotatingZoneRadius, 0.01f));
            machineryShader.SetFloat("rotatingZoneHalfHeight", Mathf.Max(settings.rotatingZoneHalfHeight, 0.01f));

            // Obstacle mask for loaded rotor/blade geometry
            UpdateObstacleMaskIfNeeded(GetLoadedModelRoot(), domain, gridCount);

            // Dispatch solver
            ComputeHelper.Dispatch(machineryShader, gridSizeX, gridSizeY, gridSizeZ, applyForcesKernel);
            ComputeHelper.Dispatch(machineryShader, gridSizeX, gridSizeY, gridSizeZ, advectVelocityKernel);
            Swap(ref gridVelocity, ref gridVelocityTmp); BindBuffers();

            for (int i = 0; i < diffusionIterations; i++)
            {
                ComputeHelper.Dispatch(machineryShader, gridSizeX, gridSizeY, gridSizeZ, diffuseKernel);
                Swap(ref gridVelocity, ref gridVelocityTmp); BindBuffers();
            }

            if (vorticityConfinement > 1e-4f)
            {
                ComputeHelper.Dispatch(machineryShader, gridSizeX, gridSizeY, gridSizeZ, vorticityKernel);
                Swap(ref gridVelocity, ref gridVelocityTmp); BindBuffers();
            }

            ComputeHelper.Dispatch(machineryShader, gridSizeX, gridSizeY, gridSizeZ, divergenceKernel);

            for (int i = 0; i < pressureIterations; i++)
            {
                ComputeHelper.Dispatch(machineryShader, gridSizeX, gridSizeY, gridSizeZ, jacobiKernel);
                Swap(ref gridPressure, ref gridPressureTmp); BindBuffers();
            }

            ComputeHelper.Dispatch(machineryShader, gridSizeX, gridSizeY, gridSizeZ, projectKernel);

            if (Time.time >= nextDiagnosticsTime)
            {
                UpdateDiagnostics();
                nextDiagnosticsTime = Time.time + 0.35f;
            }

            ApplyVisualizationState();
            OnSimulationCompleted?.Invoke();
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

        PartRegistry CurrentPartRegistry 
        {
            get {
                var root = GetLoadedModelRoot();
                if (root != cachedPartRegistryRoot)
                {
                    cachedPartRegistryRoot = root;
                    cachedPartRegistry = root != null ? root.GetComponent<PartRegistry>() : null;
                }
                return cachedPartRegistry;
            }
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

            int index = x + gridSizeX * (y + gridSizeY * z);
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
            Vector3 normalizedDirection = direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector3.up;
            for (int i = 0; i < offsets.Length; i++)
            {
                float offset = sampleStep * offsets[i];
                Vector3 samplePosition = worldPosition + normalizedDirection * offset;
                if (!TrySampleField(samplePosition, out Vector3 velocity, out float sampledPressure))
                    continue;

                Vector3 tangentVelocity = Vector3.ProjectOnPlane(velocity, normalizedDirection);
                float tangentialSpeed = tangentVelocity.magnitude;
                if (tangentialSpeed <= bestSpeed)
                    continue;

                bestSpeed = tangentialSpeed;
                pressure = sampledPressure;
                wallShear = Mathf.Max(settings.dynamicViscosity, 1e-6f) * tangentialSpeed / Mathf.Max(offset, 1e-4f);
                found = true;
            }
        }

        void UpdateObstacleMaskIfNeeded(Transform obstacleRoot, Bounds domain, int gridCount)
        {
            if (obstacleMaskBuffer == null || gridCount <= 0)
                return;

            if (obstacleMaskCache == null || obstacleMaskCache.Length != gridCount)
            {
                obstacleMaskCache = new int[gridCount];
                hasObstacleMaskSignature = false;
            }

            var registry = CurrentPartRegistry;
            int signature = ComputeObstacleMaskSignature(obstacleRoot, domain, registry);
            if (hasObstacleMaskSignature && signature == obstacleMaskSignature)
                return;

            obstacleMaskSignature = signature;
            hasObstacleMaskSignature = true;
            System.Array.Clear(obstacleMaskCache, 0, obstacleMaskCache.Length);

            if (obstacleRoot != null)
            {
                Bounds fallback = new Bounds(domain.center, Vector3.zero);
                ObstacleVoxelizer.BuildMask(
                    obstacleRoot,
                    domain,
                    gridSizeX,
                    gridSizeY,
                    gridSizeZ,
                    obstacleMaskCache,
                    fallback,
                    out _);
            }
            
            obstacleMaskBuffer.SetData(obstacleMaskCache);

            // Update velocity field from segmented parts
            if (registry != null)
            {
                if (obstacleVelocityCache == null || obstacleVelocityCache.Length != gridCount)
                {
                    obstacleVelocityCache = new Vector3[gridCount];
                }
                ObstacleVoxelizer.BuildVelocityField(registry, domain, gridSizeX, gridSizeY, gridSizeZ, obstacleMaskCache, obstacleVelocityCache);
                obstacleVelocityBuffer.SetData(obstacleVelocityCache);
            }
            else
            {
                if (obstacleVelocityCache == null || obstacleVelocityCache.Length != gridCount)
                {
                    obstacleVelocityCache = new Vector3[gridCount];
                }
                System.Array.Clear(obstacleVelocityCache, 0, obstacleVelocityCache.Length);
                obstacleVelocityBuffer.SetData(obstacleVelocityCache);
            }
        }

        int ComputeObstacleMaskSignature(Transform obstacleRoot, Bounds domain, PartRegistry registry)
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

            if (registry != null)
            {
                var parts = registry.Parts;
                for (int i = 0; i < parts.Count; i++)
                {
                    var part = parts[i];
                    if (part == null || part.partTransform == null || part.motionSettings == null) continue;

                    Vector3 pos = part.partTransform.position;
                    Vector3 scale = part.partTransform.lossyScale;
                    Vector3 euler = part.partTransform.rotation.eulerAngles;
                    hash = hash * 31 + part.partTransform.GetInstanceID();
                    hash = hash * 31 + Quantize(pos.x, 100f);
                    hash = hash * 31 + Quantize(pos.y, 100f);
                    hash = hash * 31 + Quantize(pos.z, 100f);
                    hash = hash * 31 + Quantize(scale.x, 100f);
                    hash = hash * 31 + Quantize(scale.y, 100f);
                    hash = hash * 31 + Quantize(scale.z, 100f);
                    hash = hash * 31 + Quantize(euler.x, 10f);
                    hash = hash * 31 + Quantize(euler.y, 10f);
                    hash = hash * 31 + Quantize(euler.z, 10f);
                    hash = hash * 31 + (int)part.motionSettings.motionType;
                    hash = hash * 31 + Quantize(part.motionSettings.axis.x, 100f);
                    hash = hash * 31 + Quantize(part.motionSettings.axis.y, 100f);
                    hash = hash * 31 + Quantize(part.motionSettings.axis.z, 100f);
                }
            }

            return hash;
        }

        static int Quantize(float value, float scale)
        {
            if (!float.IsFinite(value)) return 0;
            return Mathf.RoundToInt(value * scale);
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
            if (normalized == lastVisualizationMode) return;
            lastVisualizationMode = normalized;

            bool surfaceMode = string.Equals(normalized, WindTunnelSimulation3D.VisualizationSurfacePressure, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, WindTunnelSimulation3D.VisualizationSurfaceFriction, System.StringComparison.OrdinalIgnoreCase);

            if (surfaceMode)
            {
                EnsureSurfaceVisualizers();
            }

            var visualizers = FindObjectsByType<AeroFlow.Visualization.InternalSurfacePressureVisualizer>(FindObjectsSortMode.None);
            for (int i = 0; i < visualizers.Length; i++)
            {
                var visualizer = visualizers[i];
                if (visualizer == null)
                    continue;

                if (string.Equals(normalized, WindTunnelSimulation3D.VisualizationSurfaceFriction, System.StringComparison.OrdinalIgnoreCase))
                {
                    visualizer.enabled = true;
                    visualizer.SetSurfaceMode(AeroFlow.Visualization.InternalSurfacePressureVisualizer.SurfaceMode.Friction);
                    visualizer.UpdateSurfaceColors();
                }
                else if (string.Equals(normalized, WindTunnelSimulation3D.VisualizationSurfacePressure, System.StringComparison.OrdinalIgnoreCase))
                {
                    visualizer.enabled = true;
                    visualizer.SetSurfaceMode(AeroFlow.Visualization.InternalSurfacePressureVisualizer.SurfaceMode.Pressure);
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

                if (renderer.GetComponent<AeroFlow.Visualization.InternalSurfacePressureVisualizer>() == null)
                {
                    renderer.gameObject.AddComponent<AeroFlow.Visualization.InternalSurfacePressureVisualizer>();
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

        private static string NormalizeVisualizationMode(string value)
        {
            if (string.Equals(value, WindTunnelSimulation3D.VisualizationSurfacePressure, System.StringComparison.OrdinalIgnoreCase))
                return WindTunnelSimulation3D.VisualizationSurfacePressure;
            if (string.Equals(value, WindTunnelSimulation3D.VisualizationSurfaceFriction, System.StringComparison.OrdinalIgnoreCase))
                return WindTunnelSimulation3D.VisualizationSurfaceFriction;
            if (string.Equals(value, WindTunnelSimulation3D.VisualizationVerticalStreamlines, System.StringComparison.OrdinalIgnoreCase))
                return WindTunnelSimulation3D.VisualizationVerticalStreamlines;
            if (string.Equals(value, WindTunnelSimulation3D.VisualizationHorizontalStreamlines, System.StringComparison.OrdinalIgnoreCase))
                return WindTunnelSimulation3D.VisualizationHorizontalStreamlines;
            return WindTunnelSimulation3D.VisualizationStreamlines;
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

            if (!diagnosticsPending)
            {
                velReq = UnityEngine.Rendering.AsyncGPUReadback.Request(gridVelocity);
                pReq = UnityEngine.Rendering.AsyncGPUReadback.Request(gridPressure);
                divReq = UnityEngine.Rendering.AsyncGPUReadback.Request(gridDivergence);
                diagnosticsPending = true;
                return;
            }

            if (!velReq.done || !pReq.done || !divReq.done)
            {
                return;
            }

            diagnosticsPending = false;
            
            if (velReq.hasError || pReq.hasError || divReq.hasError) return;

            velReq.GetData<Vector3>().CopyTo(diagnosticsVelocityCache);
            pReq.GetData<float>().CopyTo(diagnosticsPressureCache);
            divReq.GetData<float>().CopyTo(diagnosticsDivergenceCache);

            float sumSpeed = 0f, maxSpeed = 0f, sumAbsDiv = 0f;
            float sumSwirl = 0f;
            int swirlCount = 0;
            Vector3 rotAxis = settings.rotationAxis.sqrMagnitude > 1e-6f
                ? settings.rotationAxis.normalized : Vector3.up;
            Vector3 zoneCenter = lastBoundsCenter + rotAxis * settings.rotatingZoneAxisOffset;

            for (int z = 0; z < gridSizeZ; z++)
            {
                for (int y = 0; y < gridSizeY; y++)
                {
                    for (int x = 0; x < gridSizeX; x++)
                    {
                        int idx = x + gridSizeX * (y + gridSizeY * z);
                        Vector3 v = diagnosticsVelocityCache[idx];
                        float s = v.magnitude;
                        sumSpeed += s;
                        if (s > maxSpeed) maxSpeed = s;
                        sumAbsDiv += Mathf.Abs(diagnosticsDivergenceCache[idx]);

                        // Measure swirl (tangential velocity component) in rotating zone
                        Vector3 cellWorld = CellCenterWorld(x, y, z);
                        Vector3 rel = cellWorld - zoneCenter;
                        Vector3 radialVec = rel - rotAxis * Vector3.Dot(rel, rotAxis);
                        float rDist = radialVec.magnitude;
                        if (rDist > 0.01f && rDist < settings.rotatingZoneRadius)
                        {
                            Vector3 tangent = Vector3.Cross(rotAxis, radialVec).normalized;
                            float swirlSpeed = Vector3.Dot(v, tangent);
                            sumSwirl += Mathf.Abs(swirlSpeed);
                            swirlCount++;
                        }
                    }
                }
            }

            float meanVelocity = sumSpeed / Mathf.Max(gridCount, 1);
            float meanSwirl = swirlCount > 0 ? sumSwirl / swirlCount : 0f;

            // Pressure drop
            Vector3 flowDir = ResolveFlowDirection();
            int primaryAxis = GetPrimaryAxis(flowDir);
            bool positiveFlow = GetAxisComponent(flowDir, primaryAxis) >= 0f;
            int inletPlane = positiveFlow ? 1 : GetAxisLength(primaryAxis) - 2;
            int outletPlane = positiveFlow ? GetAxisLength(primaryAxis) - 2 : 1;
            float inletP = AveragePressureOnPlane(primaryAxis, inletPlane);
            float outletP = AveragePressureOnPlane(primaryAxis, outletPlane);
            float pressureDrop = inletP - outletP;
            if (Mathf.Abs(pressureDrop) < 1e-5f)
                pressureDrop = Mathf.Max(0.5f * settings.fluidDensity * meanVelocity * meanVelocity * 0.02f, 0.1f);

            // Wall shear (approximate)
            float wallShear = Mathf.Max(settings.dynamicViscosity, 1e-6f) * meanVelocity * 0.5f
                            / Mathf.Max(Mathf.Min(lastCellSize.x, Mathf.Min(lastCellSize.y, lastCellSize.z)), 1e-3f);

            // Angular velocity
            float angularVelocityRadS = settings.angularVelocityRPM * Mathf.PI / 30f;

            // Torque estimate: T = ΔP * Q / ω
            // Where Q ≈ V_mean * A_inlet
            float inletArea = lastBoundsSize.y * lastBoundsSize.z;
            float flowRate = meanVelocity * inletArea;
            float torque = Mathf.Abs(angularVelocityRadS) > 1e-3f
                ? Mathf.Abs(pressureDrop) * flowRate / Mathf.Abs(angularVelocityRadS)
                : 0f;

            // Power: P = T * ω
            float power = torque * Mathf.Abs(angularVelocityRadS);
            float tipSpeedRatio = Mathf.Abs(settings.inletVelocity) > 1e-3f
                ? Mathf.Abs(angularVelocityRadS) * settings.rotatingZoneRadius / Mathf.Max(settings.inletVelocity, 1e-3f)
                : 0f;
            float wakeVelocityDeficit = Mathf.Clamp01(
                Mathf.Max(0f, settings.inletVelocity - meanVelocity) / Mathf.Max(settings.inletVelocity, 1e-3f));
            bool extractsEnergy = IsEnergyExtractionPreset(settings.applicationType);
            string energyDirection = extractsEnergy ? "Energy extracted from fluid" : "Energy transferred to fluid";
            string applicationLabel = GetApplicationLabel(settings.applicationType);

            // Efficiency: η = (ΔP * Q) / (T * ω) — for turbines this is useful
            // For a fan/impeller, η = ΔP * Q / P_input
            // We'll use: η = useful work / total kinetic energy transfer
            float kineticPower = 0.5f * settings.fluidDensity * flowRate * meanVelocity * meanVelocity;
            float efficiency = kineticPower > 1e-6f ? Mathf.Clamp01(Mathf.Abs(pressureDrop) * Mathf.Abs(flowRate) / Mathf.Max(power, 1e-6f)) : 0f;

            // Machine Reynolds: Re = ω * R² / ν
            float machineRe = Mathf.Abs(angularVelocityRadS) * settings.rotatingZoneRadius * settings.rotatingZoneRadius
                            / Mathf.Max(settings.dynamicViscosity / Mathf.Max(settings.fluidDensity, 1e-4f), 1e-8f);

            diagnostics = new RotatingMachineryDiagnostics
            {
                valid = true,
                meanVelocity = meanVelocity,
                maxVelocity = maxSpeed,
                pressureDrop = pressureDrop,
                wallShear = wallShear,
                divergenceL1 = sumAbsDiv / Mathf.Max(gridCount, 1),
                torque = torque,
                power = power,
                efficiency = efficiency,
                angularVelocityRadS = angularVelocityRadS,
                machineReynolds = machineRe,
                meanSwirl = meanSwirl,
                tipSpeedRatio = tipSpeedRatio,
                wakeVelocityDeficit = wakeVelocityDeficit,
                energyDirection = energyDirection,
                applicationLabel = applicationLabel
            };
        }

        public void ApplyPreset(RotatoryApplicationType preset)
        {
            settings.applicationType = preset;
            settings.rotatingZoneAxisOffset = 0f;

            switch (preset)
            {
                case RotatoryApplicationType.Windmill:
                    settings.rotationDirection = RotatoryRotationDirection.CounterClockwise;
                    settings.motionMode = RotatoryMotionMode.ConstantSpeed;
                    settings.inletVelocity = 12f;
                    settings.angularVelocityRPM = 180f;
                    settings.rotatingZoneRadius = 1.6f;
                    settings.rotatingZoneHalfHeight = 0.6f;
                    settings.tipSpeedRatio = 6f;
                    break;
                case RotatoryApplicationType.Fan:
                    settings.rotationDirection = RotatoryRotationDirection.Clockwise;
                    settings.motionMode = RotatoryMotionMode.ConstantSpeed;
                    settings.inletVelocity = 2f;
                    settings.angularVelocityRPM = 1200f;
                    settings.rotatingZoneRadius = 0.8f;
                    settings.rotatingZoneHalfHeight = 0.4f;
                    settings.tipSpeedRatio = 3.5f;
                    break;
                case RotatoryApplicationType.Propeller:
                    settings.rotationDirection = RotatoryRotationDirection.Clockwise;
                    settings.motionMode = RotatoryMotionMode.ConstantSpeed;
                    settings.inletVelocity = 15f;
                    settings.angularVelocityRPM = 2200f;
                    settings.rotatingZoneRadius = 1.1f;
                    settings.rotatingZoneHalfHeight = 0.45f;
                    settings.tipSpeedRatio = 4.5f;
                    break;
                case RotatoryApplicationType.AxialTurbine:
                    settings.rotationDirection = RotatoryRotationDirection.CounterClockwise;
                    settings.motionMode = RotatoryMotionMode.ConstantSpeed;
                    settings.inletVelocity = 18f;
                    settings.angularVelocityRPM = 900f;
                    settings.rotatingZoneRadius = 1.0f;
                    settings.rotatingZoneHalfHeight = 0.5f;
                    settings.tipSpeedRatio = 5f;
                    break;
                case RotatoryApplicationType.RadialTurbine:
                    settings.rotationDirection = RotatoryRotationDirection.Clockwise;
                    settings.motionMode = RotatoryMotionMode.ConstantSpeed;
                    settings.inletVelocity = 16f;
                    settings.angularVelocityRPM = 1400f;
                    settings.rotatingZoneRadius = 0.9f;
                    settings.rotatingZoneHalfHeight = 0.5f;
                    settings.tipSpeedRatio = 4f;
                    break;
            }
        }

        void OnDrawGizmos()
        {
            if (!showPreviewGizmos)
            {
                return;
            }

            DrawPreviewGizmos();
        }

        private void DrawPreviewGizmos()
        {
            Vector3 axis = settings.rotationAxis.sqrMagnitude > 1e-6f ? settings.rotationAxis.normalized : Vector3.up;
            Vector3 center = GetPreviewZoneCenter(axis);
            float radius = Mathf.Max(settings.rotatingZoneRadius, 0.01f);
            float halfHeight = Mathf.Max(settings.rotatingZoneHalfHeight, 0.01f);

            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, axis);
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;

            Gizmos.matrix = Matrix4x4.TRS(center, rotation, Vector3.one);

            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.35f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(radius * 2f, halfHeight * 2f, radius * 2f));

            Gizmos.color = new Color(0.35f, 1f, 0.9f, 0.95f);
            Gizmos.DrawLine(Vector3.down * halfHeight, Vector3.up * halfHeight);
            DrawArrowHead(Vector3.up * halfHeight, Vector3.up, radius);

            Gizmos.color = new Color(1f, 0.92f, 0.18f, 0.95f);
            Gizmos.DrawSphere(Vector3.zero, Mathf.Max(0.03f, radius * 0.08f));

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }

        private Vector3 GetPreviewZoneCenter(Vector3 axis)
        {
            Bounds domain = GetDomainBounds();
            return domain.center + axis * settings.rotatingZoneAxisOffset;
        }

        private static void DrawArrowHead(Vector3 tip, Vector3 direction, float radius)
        {
            Vector3 dir = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.up;
            Vector3 side = Vector3.Cross(dir, Vector3.right);
            if (side.sqrMagnitude < 1e-5f)
            {
                side = Vector3.Cross(dir, Vector3.forward);
            }
            side.Normalize();
            Vector3 back = tip - dir * Mathf.Max(radius * 0.22f, 0.05f);
            Gizmos.DrawLine(tip, back + side * radius * 0.12f);
            Gizmos.DrawLine(tip, back - side * radius * 0.12f);
        }

        private static bool IsEnergyExtractionPreset(RotatoryApplicationType type)
        {
            return type == RotatoryApplicationType.Windmill
                || type == RotatoryApplicationType.AxialTurbine
                || type == RotatoryApplicationType.RadialTurbine;
        }

        private static string GetApplicationLabel(RotatoryApplicationType type)
        {
            switch (type)
            {
                case RotatoryApplicationType.Windmill: return "Windmill";
                case RotatoryApplicationType.Fan: return "Fan";
                case RotatoryApplicationType.Propeller: return "Propeller";
                case RotatoryApplicationType.AxialTurbine: return "Axial Turbine";
                case RotatoryApplicationType.RadialTurbine: return "Radial Turbine";
                default: return "Rotatory";
            }
        }

        Vector3 CellCenterWorld(int x, int y, int z)
        {
            Vector3 uv = new Vector3(
                (x + 0.5f) / gridSizeX,
                (y + 0.5f) / gridSizeY,
                (z + 0.5f) / gridSizeZ);
            return lastBoundsCenter + Vector3.Scale(uv - Vector3.one * 0.5f, lastBoundsSize);
        }

        float AveragePressureOnPlane(int axis, int planeIndex)
        {
            float sum = 0f;
            int count = 0;
            for (int a = 0; a < GetAxisLength((axis + 1) % 3); a++)
            {
                for (int b = 0; b < GetAxisLength((axis + 2) % 3); b++)
                {
                    int x, y, z;
                    if (axis == 0) { x = planeIndex; y = a; z = b; }
                    else if (axis == 1) { x = b; y = planeIndex; z = a; }
                    else { x = a; y = b; z = planeIndex; }
                    int idx = x + gridSizeX * (y + gridSizeY * z);
                    if (idx >= 0 && idx < diagnosticsPressureCache.Length)
                    {
                        sum += diagnosticsPressureCache[idx];
                        count++;
                    }
                }
            }
            return count > 0 ? sum / count : 0f;
        }

        int GetPrimaryAxis(Vector3 dir)
        {
            Vector3 a = new Vector3(Mathf.Abs(dir.x), Mathf.Abs(dir.y), Mathf.Abs(dir.z));
            if (a.y >= a.x && a.y >= a.z) return 1;
            return a.z >= a.x ? 2 : 0;
        }

        float GetAxisComponent(Vector3 value, int axis)
        {
            return axis == 1 ? value.y : axis == 2 ? value.z : value.x;
        }

        int GetAxisLength(int axis)
        {
            return axis == 1 ? gridSizeY : axis == 2 ? gridSizeZ : gridSizeX;
        }

        static void Swap(ref ComputeBuffer a, ref ComputeBuffer b)
        {
            (a, b) = (b, a);
        }

        void ReleaseBuffers()
        {
            ComputeHelper.Release(gridVelocity, gridVelocityTmp, gridPressure, gridPressureTmp,
                gridDivergence, obstacleMaskBuffer, obstacleVelocityBuffer);
            gridVelocity = gridVelocityTmp = null;
            gridPressure = gridPressureTmp = null;
            gridDivergence = null;
            obstacleMaskBuffer = null;
            obstacleMaskCache = null;
            hasObstacleMaskSignature = false;
            isInitialized = false;
        }

        void OnDestroy() { ReleaseBuffers(); }
    }
}
