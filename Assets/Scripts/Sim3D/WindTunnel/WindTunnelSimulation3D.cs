using UnityEngine;
using AeroFlow.Visualization;
using AeroFlow.UI;
using AeroFlow.Display;
using AeroFlow.Core;

[System.Serializable]
public enum WindTunnelFlowAxis
{
    LocalX,
    LocalY,
    LocalZ
}

[System.Serializable]
public enum WindTunnelGraphicsMode
{
    Fluid,
    Particle,
    Off
}

[System.Serializable]
public class WindTunnelVehicleProperties
{
    public float massKg = 1500f;
    public float referenceArea = 0f;
    public float frontWeightDistribution = 0.55f;
    public float frontAeroBalance = 0.45f;
    public float wheelbaseMeters = 2.6f;
    public float cgHeightMeters = 0.35f;
    public float rideHeightMeters = 0f;
    public float rakeAngleDegrees = 0f;
    public float trackWidthMeters = 0f;
    public float wheelRadiusMeters = 0f;
    public float wheelWidthMeters = 0f;
    public float enginePowerKw = 180f;
    public float drivetrainEfficiency = 0.90f;
    public float rollingResistanceCoeff = 0.015f;
    public bool useMovingGround = true;
    public bool useWheelRotationProxies = true;
    public float groundSpeedScale = 1f;
}

[System.Serializable]
public struct WindTunnelVehicleReferenceFrame
{
    public bool valid;
    public Bounds modelBounds;
    public Vector3 flowAxis;
    public Vector3 sideAxis;
    public Vector3 upAxis;
    public float bodyLength;
    public float bodyWidth;
    public float bodyHeight;
    public float floorCoordinate;
    public float wheelbase;
    public float trackWidth;
    public float wheelRadius;
    public float wheelWidth;
    public Vector3 frontAxleCenter;
    public Vector3 rearAxleCenter;
    public Vector3 centerOfGravity;
}

[System.Serializable]
public class WindTunnelSettings
{
    public string tunnelProfile = "Standard";
    public bool keepPerformanceBudget = true;
    public int targetParticleBudget = 15000;
    public float airDensity = 1.225f;
    public float dynamicViscosity = 0.0000181f;
    public float fluidTemperatureC = 20f;
    public float inletVelocity = 50f;
    public float outletStaticPressurePa = 101325f;
    public float angleOfAttack = 0f;
    public float turbulenceIntensity = 1.0f;
    public bool useCustomInletDirection = false;
    public bool flipVehicleDirection = true;
    public Vector3 inletDirection = new Vector3(1, 0, 0);
    public string inletSource = "Auto";
    public string visualizationMode = "Streamlines";
    public WindTunnelGraphicsMode graphicsMode = WindTunnelGraphicsMode.Fluid;
    public int streamlineDensity = WindTunnelSimulation3D.DefaultStreamlineDensity;
    public float timeScale = 1.0f;
    public int iterationsPerFrame = 3;
    public WindTunnelVehicleProperties vehicle = new WindTunnelVehicleProperties();
}

public class WindTunnelSimulation3D : MonoBehaviour
{
    public const string PrimarySolverName = "Navier-Stokes Grid (GPU)";
    public const string StandardProfileName = "Standard";
    public const string F1BalancedProfileName = "F1 Balanced";
    public const string VisualizationStreamlines = "Streamlines";
    public const string VisualizationVelocity = "Velocity";
    public const string VisualizationPressure = "Pressure";
    public const string VisualizationEffects = "Effects";
    public const string VisualizationSmoke = "Smoke";
    public const string VisualizationOff = "Off";
    public const string VisualizationVerticalStreamlines = "Vertical Streamlines";
    public const string VisualizationHorizontalStreamlines = "Horizontal Streamlines";
    public const string VisualizationSurfaceFriction = "Surface Friction";
    public const string VisualizationSurfacePressure = "Surface Pressure";
    public const int MinStreamlineDensity = 40;
    public const int MaxStreamlineDensity = 280;
    public const int DefaultStreamlineDensity = 140;
    public bool showInstancedParticles { get; set; }
    private AeroFlow.Core.RuntimeModelLoader cachedLoader;
    private AeroFlow.Core.PartRegistry cachedPartRegistry;
    private Transform cachedPartRegistryRoot;
    private Bounds cachedModelBounds;
    private Transform cachedModelRoot;
    public WindTunnelSettings settings = new WindTunnelSettings();
    [Header("Flow Alignment")]
    public WindTunnelFlowAxis flowAxis = WindTunnelFlowAxis.LocalZ;
    [Header("Debug")]
    public bool useRawSolverDataOnly = false;
    [Header("Demo Wind Tunnel Mode")]
    public bool demoMode = false;
    [Range(0f, 10f)] public float windRelaxation = 2.0f;
    [Range(0f, 2f)] public float noiseStrength = 0.05f;
    [Header("Legacy Tuning")]
    public bool fixedTimeStep;
    [Range(0, 1)] public float collisionDamping = 0.05f;
    public float smoothingRadius = 0.2f;
    public float pressureMultiplier = 150f;
    public float nearPressureMultiplier = 2.25f;
    [Header("References")]
    public Transform floorDisplay;
    public WindTunnelStreamlineRenderer streamlineRenderer;
    public AeroFlow.Visualization.StreamlineFieldRenderer streamlineFieldRenderer;
    public AeroFlow.Visualization.FlowFieldSliceRenderer flowFieldSliceRenderer;
    public NavierStokesGridSolver navierStokesSolver;
    public AeroFlow.Physics.SurfaceAeroSolver surfaceAeroSolver;

    bool isPaused = true;
    bool pauseNextFrame;
    bool isInitialized;
    public string initStatus = "Not Initialized";
    float currentStepTime = 1f / 60f;
    bool hasPreviewSignature;
    int lastPreviewSignature;
    float nextPreviewRefreshTime;
    string lastNonPressureVisualizationMode = VisualizationStreamlines;

    public void Play()
    {
        InitializeIfNeeded();
        hasPreviewSignature = false;
        nextPreviewRefreshTime = 0f;
        isPaused = false;
    }

    public void Pause()
    {
        hasPreviewSignature = false;
        nextPreviewRefreshTime = 0f;
        isPaused = true;
    }

    public bool IsPaused => isPaused;

    void Start()
    {
        ClampSettings();
        SetVisualizationMode(settings.visualizationMode);
        InitializeIfNeeded();
        isPaused = true;
        ApplyFloorColor();

        // Auto-attach the tunnel enclosure visuals if not already present.
        if (GetComponent<AeroFlow.Visualization.WindTunnelEnclosure>() == null)
            gameObject.AddComponent<AeroFlow.Visualization.WindTunnelEnclosure>();
    }

    void OnValidate()
    {
        ClampSettings();
        SetVisualizationMode(settings.visualizationMode);
        if (streamlineFieldRenderer == null)
        {
            streamlineFieldRenderer = GetComponent<AeroFlow.Visualization.StreamlineFieldRenderer>();
        }
        if (streamlineRenderer == null)
        {
            streamlineRenderer = GetComponent<WindTunnelStreamlineRenderer>();
        }
        if (flowFieldSliceRenderer == null)
        {
            flowFieldSliceRenderer = GetComponent<AeroFlow.Visualization.FlowFieldSliceRenderer>();
        }
    }

    void FixedUpdate()
    {
        if (fixedTimeStep)
        {
            RunSimulationFrame(Time.fixedDeltaTime);
        }
    }

    void Update()
    {
        if (!isInitialized) return;
        ClampSettings();
        if (!fixedTimeStep && Time.frameCount > 10)
        {
            if (isPaused)
            {
                RefreshPreviewIfNeeded();
            }
            else
            {
                RunSimulationFrame(Time.deltaTime);
            }
        }
        else if (fixedTimeStep && isPaused)
        {
            RefreshPreviewIfNeeded();
        }

        if (pauseNextFrame)
        {
            isPaused = true;
            pauseNextFrame = false;
        }

        ApplyFloorColor();
        HandleInput();
    }

    void RunSimulationFrame(float frameTime)
    {
        if (!isInitialized || isPaused) return;
        settings.iterationsPerFrame = Mathf.Max(1, settings.iterationsPerFrame);
        float clampedFrameTime = Mathf.Min(frameTime, 0.05f);
        float timeStep = clampedFrameTime / settings.iterationsPerFrame * settings.timeScale;
        UpdateSettings(timeStep);
        for (int i = 0; i < settings.iterationsPerFrame; i++)
        {
            RunSimulationStep();
        }
    }

    void RunSimulationStep()
    {
        if (!isInitialized) return;
        if (navierStokesSolver != null)
        {
            GetLoadedModelBounds(out var obstacleCenter, out var obstacleSize);
            Transform obstacleRoot = GetLoadedModelRoot();

            // Use transform position/scale as wind tunnel bounds, but provide sensible defaults
            Bounds tunnelBounds = GetTunnelBounds();
            Vector3 boundsCenter = tunnelBounds.center;
            Vector3 boundsSize = tunnelBounds.size;

            // If bounds are zero or very small, use default wind tunnel dimensions
            if (boundsSize.x < 0.01f || boundsSize.y < 0.01f || boundsSize.z < 0.01f)
            {
                boundsSize = new Vector3(10f, 5f, 5f); // Default wind tunnel size
                boundsCenter = transform.position;
                Debug.Log("[WindTunnel] Using default bounds: " + boundsSize);
            }

            navierStokesSolver.Step(
                settings,
                null,
                null,
                boundsCenter,
                boundsSize,
                obstacleCenter,
                obstacleSize,
                obstacleRoot,
                ResolveWindDirection(),
                ResolveTunnelVerticalAxis(),
                TryGetVehicleReferenceFrame(out WindTunnelVehicleReferenceFrame vehicleFrame),
                vehicleFrame,
                currentStepTime
            );
        }
    }

    void UpdateSettings(float deltaTime)
    {
        if (!isInitialized) return;
        currentStepTime = deltaTime;
    }

    void RefreshPreviewIfNeeded()
    {
        if (!isInitialized) return;
        if (Time.time < nextPreviewRefreshTime) return;

        int previewSignature = ComputePreviewSignature();
        if (hasPreviewSignature && previewSignature == lastPreviewSignature)
        {
            return;
        }

        UpdateSettings((1f / 60f) * Mathf.Max(0.1f, settings.timeScale));
        int previewSteps = Mathf.Clamp(settings.iterationsPerFrame * 3, 8, 16);
        for (int i = 0; i < previewSteps; i++)
        {
            RunSimulationStep();
        }

        lastPreviewSignature = ComputePreviewSignature();
        hasPreviewSignature = true;
        nextPreviewRefreshTime = Time.time + 0.05f;
    }

    int ComputePreviewSignature()
    {
        GetLoadedModelBounds(out var obstacleCenter, out var obstacleSize);
        Transform obstacleRoot = GetLoadedModelRoot();
        Vector3 windDirection = ResolveWindDirection();

        int hash = 17;
        hash = hash * 31 + Quantize(settings.inletVelocity, 100f);
        hash = hash * 31 + Quantize(settings.angleOfAttack, 100f);
        hash = hash * 31 + Quantize(settings.turbulenceIntensity, 100f);
        hash = hash * 31 + Quantize(settings.airDensity, 100f);
        hash = hash * 31 + Quantize(settings.dynamicViscosity, 100000f);
        if (settings.vehicle != null)
        {
            hash = hash * 31 + Quantize(settings.vehicle.rideHeightMeters, 1000f);
            hash = hash * 31 + Quantize(settings.vehicle.rakeAngleDegrees, 100f);
            hash = hash * 31 + Quantize(settings.vehicle.wheelbaseMeters, 100f);
            hash = hash * 31 + Quantize(settings.vehicle.trackWidthMeters, 100f);
            hash = hash * 31 + Quantize(settings.vehicle.wheelRadiusMeters, 100f);
            hash = hash * 31 + Quantize(settings.vehicle.wheelWidthMeters, 100f);
            hash = hash * 31 + Quantize(settings.vehicle.cgHeightMeters, 100f);
            hash = hash * 31 + Quantize(settings.vehicle.groundSpeedScale, 100f);
            hash = hash * 31 + (settings.vehicle.useMovingGround ? 1 : 0);
            hash = hash * 31 + (settings.vehicle.useWheelRotationProxies ? 1 : 0);
        }
        hash = hash * 31 + Quantize(windDirection.x, 1000f);
        hash = hash * 31 + Quantize(windDirection.y, 1000f);
        hash = hash * 31 + Quantize(windDirection.z, 1000f);
        hash = hash * 31 + Quantize(obstacleCenter.x, 100f);
        hash = hash * 31 + Quantize(obstacleCenter.y, 100f);
        hash = hash * 31 + Quantize(obstacleCenter.z, 100f);
        hash = hash * 31 + Quantize(obstacleSize.x, 100f);
        hash = hash * 31 + Quantize(obstacleSize.y, 100f);
        hash = hash * 31 + Quantize(obstacleSize.z, 100f);

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

    void ApplyFloorColor()
    {
        if (floorDisplay == null) return;
        var renderer = floorDisplay.GetComponent<Renderer>();
        if (renderer == null) return;
        Material mat = renderer.material;
        if (mat == null)
        {
            var fallbackShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (fallbackShader != null)
            {
                mat = new Material(fallbackShader);
                renderer.material = mat;
            }
        }
        if (mat == null) return;
        if (mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader")
        {
            var fallbackShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (fallbackShader != null) mat.shader = fallbackShader;
        }
        if (mat.mainTexture != null) return;
        var target = new Color(0.03773582f, 0.03773582f, 0.03773582f, 1f);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", target);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", target);

        if (floorDisplay != null)
        {
            floorDisplay.transform.localScale = new Vector3(1, 1 / transform.localScale.y * 0.1f, 1);
        }
    }

    void HandleInput()
    {
        if (UIFocusUtility.IsTextInputFocused())
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            isPaused = !isPaused;
        }

        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            isPaused = false;
            pauseNextFrame = true;
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            isPaused = true;
        }
    }

    public void InitializeIfNeeded()
    {
        ClampSettings();
        if (isInitialized) return;

        initStatus = "Initializing...";
        if (navierStokesSolver == null) navierStokesSolver = GetComponent<NavierStokesGridSolver>() ?? gameObject.AddComponent<NavierStokesGridSolver>();
        if (navierStokesSolver == null)
        {
            Debug.LogError("[WindTunnel] NavierStokesGridSolver missing.");
            initStatus = "Error: Navier solver missing";
            return;
        }
        if (surfaceAeroSolver == null)
        {
            surfaceAeroSolver = GetComponent<AeroFlow.Physics.SurfaceAeroSolver>() ?? gameObject.AddComponent<AeroFlow.Physics.SurfaceAeroSolver>();
        }

        if (streamlineRenderer == null)
        {
            streamlineRenderer = GetComponent<WindTunnelStreamlineRenderer>() ?? gameObject.AddComponent<WindTunnelStreamlineRenderer>();
        }
        streamlineRenderer?.Initialize(this);

        if (streamlineFieldRenderer == null)
        {
            streamlineFieldRenderer = GetComponent<AeroFlow.Visualization.StreamlineFieldRenderer>()
                ?? gameObject.AddComponent<AeroFlow.Visualization.StreamlineFieldRenderer>();
        }
        if (streamlineFieldRenderer != null)
        {
            streamlineFieldRenderer.windTunnel = this;
            streamlineFieldRenderer.navier = navierStokesSolver;
            streamlineFieldRenderer.maxLineCount = GetClampedStreamlineDensity();
        }
        if (flowFieldSliceRenderer == null)
        {
            flowFieldSliceRenderer = GetComponent<AeroFlow.Visualization.FlowFieldSliceRenderer>()
                ?? gameObject.AddComponent<AeroFlow.Visualization.FlowFieldSliceRenderer>();
        }
        if (flowFieldSliceRenderer != null)
        {
            flowFieldSliceRenderer.windTunnel = this;
            flowFieldSliceRenderer.navier = navierStokesSolver;
        }
        if (streamlineRenderer != null)
        {
            streamlineRenderer.enabled = false;
        }

        isInitialized = true;
        initStatus = "Initialized";
        Debug.Log($"[WindTunnel] Initialized. Solver: {PrimarySolverName}.");
    }

    void GetLoadedModelBounds(out Vector3 center, out Vector3 size)
    {
        center = Vector3.zero;
        size = Vector3.zero;

        if (cachedLoader == null)
            cachedLoader = FindAnyObjectByType<AeroFlow.Core.RuntimeModelLoader>();
        
        if (cachedLoader != null && cachedLoader.TryGetSimulationBounds(out var simulationBounds))
        {
            center = simulationBounds.center;
            size = simulationBounds.size;
            return;
        }

        Transform root = GetLoadedModelRoot();
        if (root != cachedPartRegistryRoot)
        {
            cachedPartRegistryRoot = root;
            cachedPartRegistry = root != null ? root.GetComponent<AeroFlow.Core.PartRegistry>() : null;
        }

        if (cachedPartRegistry != null && cachedPartRegistry.TryGetCombinedBounds(out var partBounds))
        {
            center = partBounds.center;
            size = partBounds.size;
            return;
        }

        GameObject loadedModel = AeroFlow.Core.RuntimeModelLookup.GetLoadedModel();
        if (loadedModel == null) return;

        // Use RuntimeModelLookup cached renderers instead of GetComponentsInChildren
        var renderers = AeroFlow.Core.RuntimeModelLookup.GetLoadedModelRenderers();
        if (renderers == null || renderers.Length == 0) return;

        if (cachedModelRoot != loadedModel.transform)
        {
            cachedModelRoot = loadedModel.transform;
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                if (renderers[i] != null) b.Encapsulate(renderers[i].bounds);
            }
            cachedModelBounds = b;
        }

        center = cachedModelBounds.center;
        size = cachedModelBounds.size;
    }

    public bool TryGetLoadedModelBounds(out Bounds bounds)
    {
        GetLoadedModelBounds(out Vector3 center, out Vector3 size);
        if (size.x <= 1e-5f || size.y <= 1e-5f || size.z <= 1e-5f)
        {
            bounds = default;
            return false;
        }

        bounds = new Bounds(center, size);
        return true;
    }

    Transform GetLoadedModelRoot()
    {
        if (cachedLoader == null)
            cachedLoader = FindAnyObjectByType<AeroFlow.Core.RuntimeModelLoader>();

        if (cachedLoader != null && cachedLoader.CurrentSimulationGeometryRoot != null)
        {
            return cachedLoader.CurrentSimulationGeometryRoot;
        }

        if (cachedPartRegistry != null) return cachedPartRegistry.transform;
        
        GameObject loadedModel = AeroFlow.Core.RuntimeModelLookup.GetLoadedModel();
        return loadedModel != null ? loadedModel.transform : null;
    }

    private void EnsureSurfacePressureVisualizers()
    {
        var model = AeroFlow.Core.RuntimeModelLookup.GetLoadedModel();
        if (model == null) return;

        var renderers = model.GetComponentsInChildren<MeshRenderer>(true);
        if (renderers == null || renderers.Length == 0) return;

        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null) continue;
            if (renderer.GetComponent<SurfacePressureVisualizer>() != null) continue;
            if (renderer.GetComponent<RuntimeSimulationProxy>() != null) continue;
            var meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null) continue;

            bool hasTriangleTopology = false;
            Mesh sharedMesh = meshFilter.sharedMesh;
            for (int subMesh = 0; subMesh < sharedMesh.subMeshCount; subMesh++)
            {
                if (sharedMesh.GetTopology(subMesh) == MeshTopology.Triangles)
                {
                    hasTriangleTopology = true;
                    break;
                }
            }

            if (!hasTriangleTopology) continue;
            renderer.gameObject.AddComponent<SurfacePressureVisualizer>();
        }
    }

    public Bounds GetTunnelBounds()
    {
        return new Bounds(transform.position, GetTunnelSize());
    }

    public Vector3 GetTunnelSize()
    {
        Vector3 scale = transform.lossyScale;
        Vector3 size = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        var enclosure = GetComponent<AeroFlow.Visualization.WindTunnelEnclosure>();
        if (enclosure == null)
        {
            return size;
        }

        float lengthScale = Mathf.Clamp(enclosure.enclosureLengthScale, 0.4f, 1.0f);
        switch (flowAxis)
        {
            case WindTunnelFlowAxis.LocalX:
                size.x *= lengthScale;
                break;
            case WindTunnelFlowAxis.LocalY:
                size.y *= lengthScale;
                break;
            case WindTunnelFlowAxis.LocalZ:
            default:
                size.z *= lengthScale;
                break;
        }

        return size;
    }

    public Vector3 ResolveTunnelLongAxis()
    {
        return ResolveConfiguredAxis(flowAxis);
    }

    public Vector3 ResolveTunnelVerticalAxis()
    {
        Vector3 up = transform.up;
        if (up.sqrMagnitude < 1e-6f)
        {
            up = Vector3.up;
        }

        up.Normalize();
        Vector3 longAxis = ResolveTunnelLongAxis();
        if (Mathf.Abs(Vector3.Dot(up, longAxis)) > 0.92f)
        {
            Vector3 fallback = transform.forward;
            if (fallback.sqrMagnitude < 1e-6f || Mathf.Abs(Vector3.Dot(fallback.normalized, longAxis)) > 0.92f)
            {
                fallback = transform.right;
            }

            if (fallback.sqrMagnitude > 1e-6f)
            {
                up = fallback.normalized;
            }
        }

        return up;
    }

    public Vector3 ResolveTunnelSideAxis()
    {
        Vector3 side = Vector3.Cross(ResolveTunnelVerticalAxis(), ResolveTunnelLongAxis()).normalized;
        if (side.sqrMagnitude < 1e-6f)
        {
            side = Vector3.Cross(Vector3.up, ResolveTunnelLongAxis()).normalized;
            if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
        }
        return side;
    }

    public Quaternion ResolveVehicleRakeRotation()
    {
        float rakeAngle = settings.vehicle != null ? settings.vehicle.rakeAngleDegrees : 0f;
        if (Mathf.Abs(rakeAngle) <= 1e-4f)
        {
            return Quaternion.identity;
        }

        return Quaternion.AngleAxis(rakeAngle, ResolveTunnelSideAxis());
    }

    public bool TryGetVehicleReferenceFrame(out WindTunnelVehicleReferenceFrame frame)
    {
        frame = default;
        if (!TryGetLoadedModelBounds(out Bounds modelBounds))
        {
            return false;
        }

        WindTunnelVehicleProperties vehicle = settings.vehicle ?? new WindTunnelVehicleProperties();
        Vector3 flowAxis = ResolveWindDirection();
        if (flowAxis.sqrMagnitude < 1e-6f)
        {
            flowAxis = ResolveTunnelLongAxis();
        }

        flowAxis.Normalize();
        Vector3 upAxis = ResolveTunnelVerticalAxis();
        if (upAxis.sqrMagnitude < 1e-6f)
        {
            upAxis = Vector3.up;
        }
        upAxis.Normalize();

        Vector3 sideAxis = Vector3.Cross(upAxis, flowAxis).normalized;
        if (sideAxis.sqrMagnitude < 1e-6f)
        {
            sideAxis = ResolveTunnelSideAxis();
        }

        float bodyLength = ProjectSizeAlong(modelBounds, flowAxis);
        float bodyWidth = ProjectSizeAlong(modelBounds, sideAxis);
        float bodyHeight = ProjectSizeAlong(modelBounds, upAxis);
        if (bodyLength <= 1e-5f || bodyWidth <= 1e-5f || bodyHeight <= 1e-5f)
        {
            return false;
        }

        float autoWheelbase = Mathf.Clamp(bodyLength * 0.62f, bodyLength * 0.42f, bodyLength * 0.86f);
        float wheelbase = vehicle.wheelbaseMeters > 0.01f
            ? Mathf.Clamp(vehicle.wheelbaseMeters, bodyLength * 0.35f, bodyLength * 0.95f)
            : autoWheelbase;
        float trackWidth = vehicle.trackWidthMeters > 0.01f
            ? Mathf.Clamp(vehicle.trackWidthMeters, 0.4f, bodyWidth * 0.98f)
            : Mathf.Clamp(bodyWidth * 0.78f, bodyWidth * 0.45f, bodyWidth * 0.92f);
        float wheelRadius = vehicle.wheelRadiusMeters > 0.01f
            ? vehicle.wheelRadiusMeters
            : Mathf.Clamp(bodyHeight * 0.20f + Mathf.Max(vehicle.rideHeightMeters, 0f) * 0.15f, 0.18f, bodyHeight * 0.34f);
        float wheelWidth = vehicle.wheelWidthMeters > 0.01f
            ? vehicle.wheelWidthMeters
            : Mathf.Clamp(trackWidth * 0.12f, 0.12f, 0.42f);

        Bounds tunnelBounds = GetTunnelBounds();
        float floorCoordinate = Vector3.Dot(tunnelBounds.center, upAxis) - ProjectHalfExtent(tunnelBounds, upAxis);
        float axleHeightCoordinate = floorCoordinate + wheelRadius;

        Vector3 centerlinePoint = modelBounds.center;
        centerlinePoint += upAxis * (axleHeightCoordinate - Vector3.Dot(centerlinePoint, upAxis));
        centerlinePoint += sideAxis * (Vector3.Dot(modelBounds.center, sideAxis) - Vector3.Dot(centerlinePoint, sideAxis));

        Vector3 frontAxleCenter = centerlinePoint - flowAxis * (wheelbase * 0.5f);
        Vector3 rearAxleCenter = centerlinePoint + flowAxis * (wheelbase * 0.5f);

        float frontToCgDistance = wheelbase * (1f - Mathf.Clamp01(vehicle.frontWeightDistribution));
        Vector3 centerOfGravity = frontAxleCenter + flowAxis * frontToCgDistance;
        float cgHeightCoordinate = floorCoordinate + Mathf.Max(vehicle.cgHeightMeters, wheelRadius * 0.55f);
        centerOfGravity += upAxis * (cgHeightCoordinate - Vector3.Dot(centerOfGravity, upAxis));
        centerOfGravity += sideAxis * (Vector3.Dot(modelBounds.center, sideAxis) - Vector3.Dot(centerOfGravity, sideAxis));

        frame.valid = true;
        frame.modelBounds = modelBounds;
        frame.flowAxis = flowAxis;
        frame.sideAxis = sideAxis;
        frame.upAxis = upAxis;
        frame.bodyLength = bodyLength;
        frame.bodyWidth = bodyWidth;
        frame.bodyHeight = bodyHeight;
        frame.floorCoordinate = floorCoordinate;
        frame.wheelbase = wheelbase;
        frame.trackWidth = trackWidth;
        frame.wheelRadius = wheelRadius;
        frame.wheelWidth = wheelWidth;
        frame.frontAxleCenter = frontAxleCenter;
        frame.rearAxleCenter = rearAxleCenter;
        frame.centerOfGravity = centerOfGravity;
        return true;
    }

    public Vector3 ResolveWindDirection()
    {
        if (settings.useCustomInletDirection && settings.inletDirection.sqrMagnitude > 1e-6f)
        {
            return settings.inletDirection.normalized;
        }

        Vector3 fallback = GetDirectionFromInletSource(settings.inletSource);
        if (fallback != Vector3.zero)
        {
            return fallback.normalized;
        }

        return (Quaternion.AngleAxis(settings.angleOfAttack, ResolveTunnelVerticalAxis()) * ResolveTunnelLongAxis()).normalized;
    }

    static float ProjectSizeAlong(Bounds bounds, Vector3 axis)
    {
        axis = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.right;
        Vector3 extents = bounds.extents;
        Vector3 absAxis = new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
        return 2f * Vector3.Dot(extents, absAxis);
    }

    static float ProjectHalfExtent(Bounds bounds, Vector3 axis)
    {
        return ProjectSizeAlong(bounds, axis) * 0.5f;
    }

    public static string GetFlowAxisLabel(WindTunnelFlowAxis axis)
    {
        switch (axis)
        {
            case WindTunnelFlowAxis.LocalY:
                return "Y Axis";
            case WindTunnelFlowAxis.LocalZ:
                return "Z Axis";
            default:
                return "X Axis";
        }
    }

    public static WindTunnelFlowAxis ParseFlowAxisLabel(string label)
    {
        if (string.Equals(label, "Y Axis", System.StringComparison.OrdinalIgnoreCase))
        {
            return WindTunnelFlowAxis.LocalY;
        }
        if (string.Equals(label, "Z Axis", System.StringComparison.OrdinalIgnoreCase))
        {
            return WindTunnelFlowAxis.LocalZ;
        }
        return WindTunnelFlowAxis.LocalX;
    }

    Vector3 GetDirectionFromInletSource(string inletSource)
    {
        Vector3 longAxis = ResolveTunnelLongAxis();
        Vector3 sideAxis = ResolveTunnelSideAxis();
        Vector3 upAxis = ResolveTunnelVerticalAxis();

        switch (inletSource)
        {
            case "Left": return sideAxis;
            case "Right": return -sideAxis;
            case "Front": return longAxis;
            case "Back": return -longAxis;
            case "Top": return -upAxis;
            case "Bottom": return upAxis;
            default: return Vector3.zero;
        }
    }

    Vector3 ResolveConfiguredAxis(WindTunnelFlowAxis axis)
    {
        Vector3 resolved;
        switch (axis)
        {
            case WindTunnelFlowAxis.LocalY:
                resolved = transform.up;
                break;
            case WindTunnelFlowAxis.LocalZ:
                resolved = transform.forward;
                break;
            default:
                resolved = transform.right;
                break;
        }

        if (resolved.sqrMagnitude < 1e-6f)
        {
            return Vector3.right;
        }

        return resolved.normalized;
    }

    public void SetVisualizationMode(string mode)
    {
        string normalized = NormalizeVisualizationMode(mode);
        settings.visualizationMode = normalized;
        if (!string.Equals(normalized, VisualizationPressure, System.StringComparison.OrdinalIgnoreCase)
            && !string.Equals(normalized, VisualizationSurfacePressure, System.StringComparison.OrdinalIgnoreCase)
            && !string.Equals(normalized, VisualizationSurfaceFriction, System.StringComparison.OrdinalIgnoreCase))
        {
            lastNonPressureVisualizationMode = normalized;
        }
        showInstancedParticles = false;
        ApplyVisualizationState(normalized);
    }

    public void SetGraphicsMode(WindTunnelGraphicsMode mode)
    {
        settings.graphicsMode = mode;
        ApplyVisualizationState(settings.visualizationMode);
    }

    public string GetFallbackVisualizationMode()
    {
        return NormalizeVisualizationMode(lastNonPressureVisualizationMode);
    }

    public static string GetGraphicsModeLabel(WindTunnelGraphicsMode mode)
    {
        switch (mode)
        {
            case WindTunnelGraphicsMode.Particle:
                return "Particles";
            case WindTunnelGraphicsMode.Off:
                return "Off";
            default:
                return "Fluid";
        }
    }

    public static string NormalizeVisualizationMode(string mode)
    {
        if (string.Equals(mode, VisualizationVelocity, System.StringComparison.OrdinalIgnoreCase))
        {
            return VisualizationVelocity;
        }
        if (string.Equals(mode, VisualizationPressure, System.StringComparison.OrdinalIgnoreCase))
        {
            return VisualizationPressure;
        }
        if (string.Equals(mode, VisualizationEffects, System.StringComparison.OrdinalIgnoreCase))
        {
            return VisualizationEffects;
        }
        if (string.Equals(mode, VisualizationSmoke, System.StringComparison.OrdinalIgnoreCase))
        {
            return VisualizationSmoke;
        }
        if (string.Equals(mode, VisualizationOff, System.StringComparison.OrdinalIgnoreCase))
        {
            return VisualizationOff;
        }
        if (string.Equals(mode, VisualizationVerticalStreamlines, System.StringComparison.OrdinalIgnoreCase))
        {
            return VisualizationVerticalStreamlines;
        }
        if (string.Equals(mode, VisualizationHorizontalStreamlines, System.StringComparison.OrdinalIgnoreCase))
        {
            return VisualizationHorizontalStreamlines;
        }
        if (string.Equals(mode, VisualizationSurfacePressure, System.StringComparison.OrdinalIgnoreCase))
        {
            return VisualizationSurfacePressure;
        }
        if (string.Equals(mode, VisualizationSurfaceFriction, System.StringComparison.OrdinalIgnoreCase))
        {
            return VisualizationSurfaceFriction;
        }
        return VisualizationStreamlines;
    }

    public void ApplyStandardProfile(bool enforcePerformanceBudget = true)
    {
        settings.tunnelProfile = StandardProfileName;
        settings.keepPerformanceBudget = enforcePerformanceBudget && !VisualsBootstrapper.ForceMaxPerformanceGlobal;
        settings.targetParticleBudget = Mathf.Clamp(settings.targetParticleBudget, 5000, 30000);
        settings.inletVelocity = 50f;
        settings.angleOfAttack = 0f;
        settings.turbulenceIntensity = 1f;
        SetVisualizationMode(VisualizationStreamlines);
        settings.streamlineDensity = DefaultStreamlineDensity;
        ClampSettings();
    }

    public void ApplyF1BalancedProfile(bool enforcePerformanceBudget = true)
    {
        settings.tunnelProfile = F1BalancedProfileName;
        settings.keepPerformanceBudget = enforcePerformanceBudget && !VisualsBootstrapper.ForceMaxPerformanceGlobal;
        settings.targetParticleBudget = Mathf.Clamp(settings.targetParticleBudget, 5000, 30000);
        settings.inletVelocity = 72f;
        settings.turbulenceIntensity = 0.6f;
        SetVisualizationMode(VisualizationStreamlines);
        settings.streamlineDensity = 180;
        ClampSettings();
    }

    public void ResetSimulation()
    {
        if (navierStokesSolver != null)
        {
            navierStokesSolver.ResetGrid();
        }
        if (surfaceAeroSolver != null)
        {
            surfaceAeroSolver.ResetData();
        }
        hasPreviewSignature = false;
        nextPreviewRefreshTime = 0f;
        InitializeIfNeeded();
        isPaused = true;
    }

    public int GetClampedStreamlineDensity()
    {
        return Mathf.Clamp(settings.streamlineDensity, MinStreamlineDensity, MaxStreamlineDensity);
    }

    void ClampSettings()
    {
        settings.inletVelocity = Mathf.Clamp(settings.inletVelocity, 0f, 150f);
        settings.airDensity = Mathf.Clamp(settings.airDensity, 0.5f, 1500f);
        settings.dynamicViscosity = Mathf.Clamp(settings.dynamicViscosity, 0.000001f, 10f);
        settings.fluidTemperatureC = Mathf.Clamp(settings.fluidTemperatureC, -50f, 150f);
        settings.outletStaticPressurePa = Mathf.Clamp(settings.outletStaticPressurePa, 50000f, 150000f);
        settings.turbulenceIntensity = Mathf.Clamp(settings.turbulenceIntensity, 0f, 100f);
        settings.angleOfAttack = Mathf.Clamp(settings.angleOfAttack, -45f, 45f);
        settings.timeScale = Mathf.Clamp(settings.timeScale, 0.1f, 5f);
        settings.iterationsPerFrame = Mathf.Clamp(settings.iterationsPerFrame, 1, 10);
        settings.streamlineDensity = GetClampedStreamlineDensity();
        settings.visualizationMode = NormalizeVisualizationMode(settings.visualizationMode);
        if (settings.vehicle == null)
        {
            settings.vehicle = new WindTunnelVehicleProperties();
        }
        settings.vehicle.massKg = Mathf.Clamp(settings.vehicle.massKg, 250f, 6000f);
        settings.vehicle.referenceArea = Mathf.Max(0f, settings.vehicle.referenceArea);
        settings.vehicle.frontWeightDistribution = Mathf.Clamp01(settings.vehicle.frontWeightDistribution);
        settings.vehicle.frontAeroBalance = Mathf.Clamp01(settings.vehicle.frontAeroBalance);
        settings.vehicle.wheelbaseMeters = Mathf.Clamp(settings.vehicle.wheelbaseMeters, 1.5f, 6f);
        settings.vehicle.cgHeightMeters = Mathf.Clamp(settings.vehicle.cgHeightMeters, 0.15f, 1.5f);
        settings.vehicle.rideHeightMeters = Mathf.Clamp(settings.vehicle.rideHeightMeters, 0f, 0.5f);
        settings.vehicle.rakeAngleDegrees = Mathf.Clamp(settings.vehicle.rakeAngleDegrees, -8f, 8f);
        settings.vehicle.trackWidthMeters = Mathf.Max(0f, settings.vehicle.trackWidthMeters);
        settings.vehicle.wheelRadiusMeters = Mathf.Max(0f, settings.vehicle.wheelRadiusMeters);
        settings.vehicle.wheelWidthMeters = Mathf.Max(0f, settings.vehicle.wheelWidthMeters);
        settings.vehicle.enginePowerKw = Mathf.Clamp(settings.vehicle.enginePowerKw, 0f, 2000f);
        settings.vehicle.drivetrainEfficiency = Mathf.Clamp(settings.vehicle.drivetrainEfficiency, 0.5f, 1f);
        settings.vehicle.rollingResistanceCoeff = Mathf.Clamp(settings.vehicle.rollingResistanceCoeff, 0.001f, 0.08f);
        settings.vehicle.groundSpeedScale = Mathf.Clamp(settings.vehicle.groundSpeedScale, 0f, 1.5f);
        if (!string.Equals(settings.visualizationMode, VisualizationPressure, System.StringComparison.OrdinalIgnoreCase)
            && !string.Equals(settings.visualizationMode, VisualizationSurfacePressure, System.StringComparison.OrdinalIgnoreCase)
            && !string.Equals(settings.visualizationMode, VisualizationSurfaceFriction, System.StringComparison.OrdinalIgnoreCase))
        {
            lastNonPressureVisualizationMode = settings.visualizationMode;
        }
        showInstancedParticles = false;

        if (streamlineFieldRenderer == null)
        {
            streamlineFieldRenderer = GetComponent<AeroFlow.Visualization.StreamlineFieldRenderer>();
        }
        if (streamlineFieldRenderer != null)
        {
            streamlineFieldRenderer.maxLineCount = settings.streamlineDensity;
        }
    }

    void ApplyVisualizationState(string mode)
    {
        string normalized = NormalizeVisualizationMode(mode);

        if (streamlineRenderer == null)
        {
            streamlineRenderer = GetComponent<WindTunnelStreamlineRenderer>();
        }
        if (streamlineFieldRenderer == null)
        {
            streamlineFieldRenderer = GetComponent<AeroFlow.Visualization.StreamlineFieldRenderer>();
        }
        if (flowFieldSliceRenderer == null)
        {
            flowFieldSliceRenderer = GetComponent<AeroFlow.Visualization.FlowFieldSliceRenderer>();
        }

        FlowParticleSystem flowParticles = GetComponentInChildren<FlowParticleSystem>(true);
        if (flowParticles == null)
        {
            flowParticles = FindAnyObjectByType<FlowParticleSystem>();
        }
        bool graphicsOff = settings.graphicsMode == WindTunnelGraphicsMode.Off;
        bool particleMode = settings.graphicsMode == WindTunnelGraphicsMode.Particle;
        bool surfacePressureMode = !graphicsOff && string.Equals(normalized, VisualizationSurfacePressure, System.StringComparison.OrdinalIgnoreCase);

        if (graphicsOff)
        {
            showInstancedParticles = false;
            if (streamlineRenderer != null) streamlineRenderer.enabled = false;
            if (streamlineFieldRenderer != null)
            {
                streamlineFieldRenderer.ApplyVisualizationMode(VisualizationOff);
                streamlineFieldRenderer.enabled = false;
            }
            if (flowFieldSliceRenderer != null)
            {
                flowFieldSliceRenderer.ApplyVisualizationMode(VisualizationOff);
                flowFieldSliceRenderer.enabled = false;
            }
            if (flowParticles != null)
            {
                flowParticles.enabled = false;
                flowParticles.gameObject.SetActive(false);
                flowParticles.ApplyVisualizationMode(VisualizationOff);
            }
        }
        else if (particleMode)
        {
            showInstancedParticles = false;
            if (streamlineRenderer != null) streamlineRenderer.enabled = false;
            if (streamlineFieldRenderer != null)
            {
                streamlineFieldRenderer.ApplyVisualizationMode(VisualizationOff);
                streamlineFieldRenderer.enabled = false;
            }
            if (flowFieldSliceRenderer != null)
            {
                flowFieldSliceRenderer.ApplyVisualizationMode(VisualizationOff);
                flowFieldSliceRenderer.enabled = false;
            }
            if (flowParticles != null)
            {
                flowParticles.windTunnel = this;
                flowParticles.gameObject.SetActive(true);
                flowParticles.enabled = true;
            }
        }
        else if (surfacePressureMode)
        {
            showInstancedParticles = false;
            if (streamlineRenderer != null) streamlineRenderer.enabled = false;
            if (streamlineFieldRenderer != null)
            {
                streamlineFieldRenderer.ApplyVisualizationMode(VisualizationOff);
                streamlineFieldRenderer.enabled = false;
            }
            if (flowFieldSliceRenderer != null)
            {
                flowFieldSliceRenderer.ApplyVisualizationMode(VisualizationOff);
                flowFieldSliceRenderer.enabled = false;
            }
            if (flowParticles != null)
            {
                flowParticles.windTunnel = this;
                flowParticles.gameObject.SetActive(true);
                flowParticles.enabled = false;
                flowParticles.ApplyVisualizationMode(VisualizationOff);
            }
        }
        else
        {
            showInstancedParticles = false;
            if (streamlineRenderer != null) streamlineRenderer.enabled = false;
            if (streamlineFieldRenderer != null)
            {
                streamlineFieldRenderer.ApplyVisualizationMode(normalized);
                streamlineFieldRenderer.enabled = true;
            }
            if (flowFieldSliceRenderer != null)
            {
                flowFieldSliceRenderer.ApplyVisualizationMode(normalized);
                flowFieldSliceRenderer.enabled = true;
            }
            if (flowParticles != null)
            {
                flowParticles.windTunnel = this;
                flowParticles.gameObject.SetActive(true);
                flowParticles.enabled = false;
                flowParticles.ApplyVisualizationMode(VisualizationOff);
            }
        }

        // Surface-only modes: set surfaceMode on all SurfacePressureVisualizers.
        bool frictionMode = !graphicsOff && string.Equals(normalized, VisualizationSurfaceFriction, System.StringComparison.OrdinalIgnoreCase);
        bool pressureMode = !graphicsOff && 
            (string.Equals(normalized, VisualizationPressure, System.StringComparison.OrdinalIgnoreCase)
             || string.Equals(normalized, VisualizationSurfacePressure, System.StringComparison.OrdinalIgnoreCase));
        EnsureSurfacePressureVisualizers();
        var surfaceVisualizers = FindObjectsByType<AeroFlow.Visualization.SurfacePressureVisualizer>(FindObjectsSortMode.None);
        for (int i = 0; i < surfaceVisualizers.Length; i++)
        {
            if (surfaceVisualizers[i] == null) continue;
            if (graphicsOff)
            {
                surfaceVisualizers[i].enabled = false;
            }
            else if (frictionMode)
            {
                surfaceVisualizers[i].enabled = true;
                surfaceVisualizers[i].SetSurfaceMode(AeroFlow.Visualization.SurfacePressureVisualizer.SurfaceMode.Friction);
            }
            else if (pressureMode)
            {
                surfaceVisualizers[i].enabled = true;
                surfaceVisualizers[i].SetSurfaceMode(AeroFlow.Visualization.SurfacePressureVisualizer.SurfaceMode.Pressure);
            }
            else
            {
                surfaceVisualizers[i].enabled = false;
            }
        }
    }
}
