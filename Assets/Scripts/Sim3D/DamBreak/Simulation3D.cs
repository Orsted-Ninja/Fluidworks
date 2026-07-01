using UnityEngine;
using Unity.Mathematics;
using AeroFlow.UI;

[System.Serializable]
public class DamBreakSettings
{
    // Initial Conditions (Reset Required)
    public int particleCount = 42875; // 35^3
    public float waterFillRatio = 0.5f;

    // Fluid Properties (Live)
    public float density = 1000f;
    public float viscosity = 0.001f;

    // Simulation Controls (Live)
    public float gravity = -10f;
    public float timeScale = 0.9f;
    public int iterationsPerFrame = 4;
    public float maxVelocity = 50f;
    public WindTunnelVehicleProperties vehicle = new WindTunnelVehicleProperties();
}

public class Simulation3D : MonoBehaviour
{
    public const string PrimarySolverName = "SPH Particle Solver (GPU)";
    public event System.Action SimulationStepCompleted;

    [Header("Settings")]
    public DamBreakSettings settings = new DamBreakSettings();

    // Internal SPH Stability variables
    public bool fixedTimeStep;
    [Range(0, 1)] public float collisionDamping = 0.95f;
    public float smoothingRadius = 0.2f;
    public float pressureMultiplier = 150f;
    public float nearPressureMultiplier = 2.25f;

    [Header("References")]
    public ComputeShader compute;
    public Spawner3D spawner;
    public ParticleDisplay3D display;
    public Transform floorDisplay;

    // Buffers
    public ComputeBuffer positionBuffer { get; private set; }
    public ComputeBuffer velocityBuffer { get; private set; }
    public ComputeBuffer densityBuffer { get; private set; }
    public ComputeBuffer predictedPositionsBuffer;
    ComputeBuffer spatialIndices;
    ComputeBuffer spatialOffsets;

    // Kernel IDs
    const int externalForcesKernel = 0;
    const int spatialHashKernel = 1;
    const int densityKernel = 2;
    const int pressureKernel = 3;
    const int viscosityKernel = 4;
    const int updatePositionsKernel = 5;

    GPUSort gpuSort;

    // State
    bool isPaused = true;
    bool pauseNextFrame;
    Spawner3D.SpawnData spawnData;
    bool tankCollidersBuilt;
    float groundedStillTime;
    bool missingStateLogged;

    public bool IsPaused => isPaused;

    public void Play() 
    { 
        isPaused = false; 
        groundedStillTime = 0f;
        
        // --- Wake Up Rigidbodies to Drop ---
        GameObject loadedModel = GameObject.Find("LoadedModel");
        if (loadedModel != null)
        {
            Rigidbody rb = loadedModel.GetComponent<Rigidbody>();
            if (rb != null)
            {
                Renderer[] renderers = loadedModel.GetComponentsInChildren<Renderer>();
                if (renderers != null && renderers.Length > 0)
                {
                    Bounds modelBounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) modelBounds.Encapsulate(renderers[i].bounds);
                    Bounds tankBounds = new Bounds(transform.position, transform.localScale);

                    // Auto-fit overly large imported models so gravity drop remains stable and realistic.
                    float maxModelDim = Mathf.Max(modelBounds.size.x, Mathf.Max(modelBounds.size.y, modelBounds.size.z));
                    float maxTankDim = Mathf.Min(tankBounds.size.x, Mathf.Min(tankBounds.size.y, tankBounds.size.z)) * 0.55f;
                    if (maxModelDim > maxTankDim && maxModelDim > 1e-5f)
                    {
                        float fitScale = maxTankDim / maxModelDim;
                        loadedModel.transform.localScale *= fitScale;
                        modelBounds = renderers[0].bounds;
                        for (int i = 1; i < renderers.Length; i++) modelBounds.Encapsulate(renderers[i].bounds);
                    }

                    // Start the model just above the initial water level for a better splash experience.
                    float waterTopY = spawner.centre.y + (spawner.size / 2f);
                    float bottomOffset = rb.position.y - modelBounds.min.y;
                    float dropHeightY = waterTopY + bottomOffset + 1.25f; // 1.25m above water
                    
                    Vector3 p = rb.position;
                    // Only snap up if it's currently buried or too low.
                    if (p.y < dropHeightY) p.y = dropHeightY;
                    rb.position = p;
                }
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.constraints = RigidbodyConstraints.FreezeRotation;
                rb.mass = Mathf.Clamp(rb.mass, 5f, 120f);
                rb.linearDamping = 0.15f;
                rb.angularDamping = 0.4f;
                rb.detectCollisions = true;
                rb.WakeUp();
            }
        }
    }
    
    public void Pause() 
    { 
        isPaused = true; 
        groundedStillTime = 0f;
        
        // --- Sleep Rigidbodies ---
        GameObject loadedModel = GameObject.Find("LoadedModel");
        if (loadedModel != null)
        {
            Rigidbody rb = loadedModel.GetComponent<Rigidbody>();
            if (rb != null)
            {
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                rb.constraints = RigidbodyConstraints.None;
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.Sleep();
            }
        }
    }

    void Start()
    {
        Debug.Log("[DamBreak] Controls: Space = Play/Pause, R = Reset");
        Debug.Log("Use transform tool in scene to scale/rotate simulation bounding box.");
        var loadedCompute = Resources.Load<ComputeShader>("Compute/DamBreak/FluidSim3D");
        if (loadedCompute != null)
        {
            compute = loadedCompute;
        }
        if (compute == null)
        {
            Debug.LogError("[DamBreak] FluidSim3D compute shader is missing.");
            enabled = false;
            return;
        }
        Debug.Log($"[DamBreak] Mode: {PrimarySolverName}. Compute: {compute.name}.");

        // --- SPH STABILITY SAFEGUARDS ---
        // Prevent explosive numerical behaviors stemming from un-tuned inspector variables
        settings.iterationsPerFrame = Mathf.Max(4, settings.iterationsPerFrame);
        pressureMultiplier = Mathf.Clamp(pressureMultiplier, 10f, 150f);
        nearPressureMultiplier = Mathf.Clamp(nearPressureMultiplier, 1f, 15f);
        smoothingRadius = Mathf.Max(0.25f, smoothingRadius);
        // --------------------------------

        // --- Generate Unity Physics Tank Boundaries ---
        // Prevents unity rigidbodies from falling through GPU fluid bounds
        BuildTankColliders();
        // --------------------------------

        float deltaTime = 1 / 60f;
        Time.fixedDeltaTime = deltaTime;

        // Execute initial buffer creation
        ApplyAndReset();

        // Ensure floor is always correctly scaled for camera views
        if (floorDisplay != null) floorDisplay.transform.localScale = new Vector3(1, 1 / transform.localScale.y * 0.1f, 1);
    }

    public void ApplyAndReset()
    {
        isPaused = true;
        missingStateLogged = false;

        // Clean up old buffers if they exist
        if (positionBuffer != null)
        {
            ComputeHelper.Release(positionBuffer, predictedPositionsBuffer, velocityBuffer, densityBuffer, spatialIndices, spatialOffsets);
            if (display != null) display.ReleaseBuffers();
        }

        // --- Calculate Initial Conditions from DamBreakSettings ---
        // Convert total particle count to per-axis grid dimensions for the generic spawner
        spawner.numParticlesPerAxis = Mathf.Clamp(Mathf.CeilToInt(Mathf.Pow(settings.particleCount, 1f / 3f)), 2, 100);
        
        // Scale the water's cubic volume according to the tank size and fill percentage
        float tankVolume = transform.localScale.x * transform.localScale.y * transform.localScale.z;
        float waterVolume = tankVolume * settings.waterFillRatio;
        spawner.size = Mathf.Pow(waterVolume, 1f / 3f);
        
        // Spawn the water block floating just slightly above the tank floor
        spawner.centre = transform.position + new Vector3(0, -transform.localScale.y / 2f + (spawner.size / 2f) + 0.1f, 0);

        spawnData = spawner.GetSpawnData();

        // --- Create SPH Compute Buffers ---
        int numParticles = spawnData.points.Length;
        positionBuffer = ComputeHelper.CreateStructuredBuffer<float3>(numParticles);
        predictedPositionsBuffer = ComputeHelper.CreateStructuredBuffer<float3>(numParticles);
        velocityBuffer = ComputeHelper.CreateStructuredBuffer<float3>(numParticles);
        densityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
        spatialIndices = ComputeHelper.CreateStructuredBuffer<uint3>(numParticles);
        spatialOffsets = ComputeHelper.CreateStructuredBuffer<uint>(numParticles);
        
        // --- Initialize Buffers ---
        SetInitialBufferData(spawnData);
        compute.SetInt("numParticles", positionBuffer.count);

        // --- Re-bind GPU Kernels ---
        ComputeHelper.SetBuffer(compute, positionBuffer, "Positions", externalForcesKernel, updatePositionsKernel);
        ComputeHelper.SetBuffer(compute, predictedPositionsBuffer, "PredictedPositions", externalForcesKernel, spatialHashKernel, densityKernel, pressureKernel, viscosityKernel, updatePositionsKernel);
        ComputeHelper.SetBuffer(compute, spatialIndices, "SpatialIndices", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel);
        ComputeHelper.SetBuffer(compute, spatialOffsets, "SpatialOffsets", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel);
        ComputeHelper.SetBuffer(compute, densityBuffer, "Densities", densityKernel, pressureKernel, viscosityKernel);
        ComputeHelper.SetBuffer(compute, velocityBuffer, "Velocities", externalForcesKernel, pressureKernel, viscosityKernel, updatePositionsKernel);

        gpuSort = new GPUSort();
        gpuSort.SetBuffers(spatialIndices, spatialOffsets);

        if (display != null) display.Init(this);
    }

    void FixedUpdate()
    {
        // Run simulation if in fixed timestep mode
        if (fixedTimeStep)
        {
            RunSimulationFrame(Time.fixedDeltaTime);
        }
    }

    void Update()
    {
        // Run simulation if not in fixed timestep mode
        // (skip running for first few frames as timestep can be a lot higher than usual)
        if (!fixedTimeStep && Time.frameCount > 10)
        {
            RunSimulationFrame(Time.deltaTime);
        }

        if (pauseNextFrame)
        {
            isPaused = true;
            pauseNextFrame = false;
        }
        if (floorDisplay != null)
        {
            floorDisplay.transform.localScale = new Vector3(1, 1 / transform.localScale.y * 0.1f, 1);
        }

        HandleInput();
        ConstrainLoadedModelToTank();
    }

    void BuildTankColliders()
    {
        if (tankCollidersBuilt) return;
        GameObject existing = transform.Find("TankColliders") != null ? transform.Find("TankColliders").gameObject : null;
        if (existing != null)
        {
            tankCollidersBuilt = true;
            return;
        }

        GameObject tankColliders = new GameObject("TankColliders");
        tankColliders.transform.SetParent(transform, false);

        // Slightly thicker colliders prevent tunneling for dropped rigidbodies.
        BoxCollider floor = tankColliders.AddComponent<BoxCollider>();
        floor.center = new Vector3(0, -0.5f, 0); floor.size = new Vector3(1, 0.2f, 1);
        BoxCollider wallL = tankColliders.AddComponent<BoxCollider>();
        wallL.center = new Vector3(-0.5f, 0, 0); wallL.size = new Vector3(0.2f, 1, 1);
        BoxCollider wallR = tankColliders.AddComponent<BoxCollider>();
        wallR.center = new Vector3(0.5f, 0, 0); wallR.size = new Vector3(0.2f, 1, 1);
        BoxCollider wallF = tankColliders.AddComponent<BoxCollider>();
        wallF.center = new Vector3(0, 0, 0.5f); wallF.size = new Vector3(1, 1, 0.2f);
        BoxCollider wallB = tankColliders.AddComponent<BoxCollider>();
        wallB.center = new Vector3(0, 0, -0.5f); wallB.size = new Vector3(1, 1, 0.2f);

        tankCollidersBuilt = true;
    }

    void ConstrainLoadedModelToTank()
    {
        GameObject loadedModel = GameObject.Find("LoadedModel");
        if (loadedModel == null) return;
        Rigidbody rb = loadedModel.GetComponent<Rigidbody>();
        if (rb == null || rb.isKinematic) return;

        Renderer[] renderers = loadedModel.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0) return;

        Bounds modelBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) modelBounds.Encapsulate(renderers[i].bounds);
        Bounds tankBounds = new Bounds(transform.position, transform.localScale);

        Vector3 pos = rb.position;
        bool clamped = false;

        float bottomOffset = pos.y - modelBounds.min.y;
        float minY = tankBounds.min.y + bottomOffset + 0.02f;
        if (pos.y < minY)
        {
            pos.y = minY;
            clamped = true;
        }
        if (clamped)
        {
            rb.position = pos;
            if (rb.linearVelocity.y < 0f)
            {
                Vector3 v = rb.linearVelocity;
                v.y = 0f;
                rb.linearVelocity = v;
            }
        }

        // Settle lock: once the model is resting on the floor, stop micro-jitter.
        float floorGap = Mathf.Abs(rb.position.y - minY);
        bool nearFloor = floorGap < 0.03f;
        bool verySlow = rb.linearVelocity.magnitude < 0.18f && rb.angularVelocity.magnitude < 0.35f;
        if (nearFloor && verySlow)
        {
            groundedStillTime += Time.deltaTime;
            if (groundedStillTime > 0.35f)
            {
                Vector3 p = rb.position;
                p.y = minY;
                rb.position = p;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.Sleep();
            }
        }
        else
        {
            groundedStillTime = 0f;
        }
    }

    void RunSimulationFrame(float frameTime)
    {
        if (!HasValidSimulationState())
        {
            isPaused = true;
            return;
        }
        if (!isPaused)
        {
            // Clamp timestep to prevent massive integration steps from FPS lag or UI hangs
            float clampedFrameTime = Mathf.Min(frameTime, 0.05f); // Max 50ms per frame
            settings.iterationsPerFrame = Mathf.Max(1, settings.iterationsPerFrame); // Never divide by zero

            float timeStep = clampedFrameTime / settings.iterationsPerFrame * settings.timeScale;

            UpdateSettings(timeStep);

            for (int i = 0; i < settings.iterationsPerFrame; i++)
            {
                RunSimulationStep();
                SimulationStepCompleted?.Invoke();
            }
        }
    }

    void RunSimulationStep()
    {
        if (!HasValidSimulationState())
        {
            isPaused = true;
            return;
        }
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: externalForcesKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: spatialHashKernel);
        gpuSort.SortAndCalculateOffsets();
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: densityKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: pressureKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: viscosityKernel);
        ComputeHelper.Dispatch(compute, positionBuffer.count, kernelIndex: updatePositionsKernel);

    }

    void UpdateSettings(float deltaTime)
    {
        if (!HasValidSimulationState()) return;
        Vector3 simBoundsSize = transform.localScale;
        Vector3 simBoundsCentre = transform.position;

        compute.SetFloat("deltaTime", deltaTime);
        compute.SetFloat("gravity", settings.gravity);
        compute.SetFloat("collisionDamping", collisionDamping);
        compute.SetFloat("smoothingRadius", smoothingRadius);
        compute.SetFloat("targetDensity", settings.density);
        compute.SetFloat("pressureMultiplier", pressureMultiplier);
        compute.SetFloat("nearPressureMultiplier", nearPressureMultiplier);
        compute.SetFloat("viscosityStrength", settings.viscosity);
        compute.SetFloat("maxVelocity", settings.maxVelocity);
        compute.SetVector("boundsSize", simBoundsSize);
        compute.SetVector("centre", simBoundsCentre);

        compute.SetMatrix("localToWorld", transform.localToWorldMatrix);
        compute.SetMatrix("worldToLocal", transform.worldToLocalMatrix);

        // --- Bounding Box Obstacle for Imported Models ---
        GameObject loadedModel = GameObject.Find("LoadedModel");
        if (loadedModel != null)
        {
            Bounds b = new Bounds();
            Renderer[] renderers = loadedModel.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            }
            
            compute.SetVector("obstacleCenter", b.center);
            compute.SetVector("obstacleSize", b.size);
        }
        else
        {
            compute.SetVector("obstacleCenter", Vector3.zero);
            compute.SetVector("obstacleSize", Vector3.zero);
        }
    }

    void SetInitialBufferData(Spawner3D.SpawnData spawnData)
    {
        float3[] allPoints = new float3[spawnData.points.Length];
        System.Array.Copy(spawnData.points, allPoints, spawnData.points.Length);

        positionBuffer.SetData(allPoints);
        predictedPositionsBuffer.SetData(allPoints);
        velocityBuffer.SetData(spawnData.velocities);
    }

    void HandleInput()
    {
        if (UIFocusUtility.IsTextInputFocused())
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (isPaused) Play();
            else Pause();
        }

        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            isPaused = false;
            pauseNextFrame = true;
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            Pause();
            SetInitialBufferData(spawnData);
        }
    }

    void OnDestroy()
    {
        ComputeHelper.Release(positionBuffer, predictedPositionsBuffer, velocityBuffer, densityBuffer, spatialIndices, spatialOffsets);
    }

    bool HasValidSimulationState()
    {
        bool valid = compute != null
            && positionBuffer != null
            && predictedPositionsBuffer != null
            && velocityBuffer != null
            && densityBuffer != null
            && spatialIndices != null
            && spatialOffsets != null
            && gpuSort != null;

        if (!valid && !missingStateLogged)
        {
            Debug.LogWarning("[DamBreak] Simulation state incomplete; pausing until buffers are initialized/reset.");
            missingStateLogged = true;
        }
        else if (valid)
        {
            missingStateLogged = false;
        }
        return valid;
    }

    void OnDrawGizmos()
    {
        // Draw Bounds
        var m = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0, 1, 0, 0.5f);
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        Gizmos.matrix = m;

    }
}
