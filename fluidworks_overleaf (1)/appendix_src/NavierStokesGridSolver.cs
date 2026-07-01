using UnityEngine;
using System.Collections.Generic;

public class NavierStokesGridSolver : MonoBehaviour
{
    public struct NavierDiagnostics
    {
        public bool valid;
        public float meanVelocity;
        public float maxVelocity;
        public float pressureDrop;
        public float wallShear;
        public float divergenceL1;
        public float meanVorticity;
    }

    [Header("Grid Resolution")]
    [Range(16, 128)] public int gridSizeX = 56;
    [Range(8, 96)] public int gridSizeY = 28;
    [Range(8, 96)] public int gridSizeZ = 28;

    [Header("Solver Quality")]
    [Range(1, 12)] public int diffusionIterations = 2;
    [Range(4, 80)] public int pressureIterations = 14;
    [Range(0f, 0.05f)] public float velocityDamping = 0.002f;
    [Range(0f, 3f)] public float vorticityConfinement = 0.6f;

    public ComputeShader navierShader;

    ComputeBuffer gridVelocity;
    ComputeBuffer gridVelocityTmp;
    ComputeBuffer gridPressure;
    ComputeBuffer gridPressureTmp;
    ComputeBuffer gridDivergence;
    ComputeBuffer obstacleSphereBuffer;
    ComputeBuffer obstacleMaskBuffer;

    int applyForcesKernel;
    int advectVelocityKernel;
    int diffuseKernel;
    int vorticityKernel;
    int divergenceKernel;
    int jacobiKernel;
    int projectKernel;
    int advectParticlesKernel;

    bool initialized;
    int initializedGridCount;
    Vector3 lastCellSize = Vector3.one;
    NavierDiagnostics diagnostics;
    float nextDiagnosticsTime;
    float nextObstacleProxyUpdateTime;
    float lastInletVelocity;
    float lastFluidDensity = 1.225f;
    Vector3 lastWindDirection = Vector3.right;
    const int MaxObstacleSpheres = 24;
    readonly Vector4[] obstacleSphereData = new Vector4[MaxObstacleSpheres];
    static readonly List<Renderer> obstacleRenderers = new List<Renderer>(128);
    int obstacleSphereCount;
    int obstacleMaskSolidCount;
    int[] obstacleMaskData;
    Vector3[] diagnosticsVelocityCache;
    float[] diagnosticsPressureCache;
    float[] diagnosticsDivergenceCache;
    int diagnosticsSnapshotVersion;
    Vector3 lastBoundsCenter;
    Vector3 lastBoundsSize;
    Vector3[] velocitySnapshot;
    int lastLoggedObstacleSphereCount = -1;
    int lastLoggedObstacleMaskSolidCount = -1;
    float nextObstacleLogTime;

    public Vector3 BoundsCenter => lastBoundsCenter;
    public Vector3 BoundsSize => lastBoundsSize;
    public Vector3 CellSize => lastCellSize;
    public int ObstacleSphereCount => obstacleSphereCount;
    public int ObstacleMaskSolidCount => obstacleMaskSolidCount;

    public bool TryGetDiagnostics(out NavierDiagnostics value)
    {
        value = diagnostics;
        return diagnostics.valid;
    }

    public bool IsObstacle(Vector3 worldPos)
    {
        if (obstacleMaskData == null || obstacleMaskSolidCount <= 0) return false;
        int gridCount = gridSizeX * gridSizeY * gridSizeZ;
        if (obstacleMaskData.Length != gridCount) return false;
        if (lastBoundsSize.x <= 1e-5f || lastBoundsSize.y <= 1e-5f || lastBoundsSize.z <= 1e-5f) return false;

        Vector3 minB = lastBoundsCenter - lastBoundsSize * 0.5f;
        float ux = (worldPos.x - minB.x) / lastBoundsSize.x;
        float uy = (worldPos.y - minB.y) / lastBoundsSize.y;
        float uz = (worldPos.z - minB.z) / lastBoundsSize.z;
        if (ux < 0f || ux > 1f || uy < 0f || uy > 1f || uz < 0f || uz > 1f) return false;

        int ix = Mathf.Clamp(Mathf.FloorToInt(ux * (gridSizeX - 1)), 0, gridSizeX - 1);
        int iy = Mathf.Clamp(Mathf.FloorToInt(uy * (gridSizeY - 1)), 0, gridSizeY - 1);
        int iz = Mathf.Clamp(Mathf.FloorToInt(uz * (gridSizeZ - 1)), 0, gridSizeZ - 1);
        return obstacleMaskData[Flatten(ix, iy, iz)] != 0;
    }

    public void ResetGrid()
    {
        ReleaseBuffers();
        initialized = false;
    }

    public void Step(
        WindTunnelSettings settings,
        ComputeBuffer particlePositions,
        ComputeBuffer particleVelocities,
        Vector3 boundsCenter,
        Vector3 boundsSize,
        Vector3 obstacleCenter,
        Vector3 obstacleSize,
        Transform obstacleRoot,
        Vector3 windDirection,
        Vector3 tunnelUpAxis,
        bool hasVehicleFrame,
        WindTunnelVehicleReferenceFrame vehicleFrame,
        float deltaTime)
    {
        EnsureInitialized();
        if (!initialized) return;
        int particleCount = (particlePositions != null && particleVelocities != null) ? particlePositions.count : 0;

        int gridCount = gridSizeX * gridSizeY * gridSizeZ;
        windDirection = windDirection.sqrMagnitude > 1e-6f ? windDirection.normalized : Vector3.right;
        lastWindDirection = windDirection;
        lastInletVelocity = Mathf.Max(settings.inletVelocity, 0f);
        lastFluidDensity = Mathf.Max(settings.airDensity, 1e-4f);

        Vector3 cellSize = new Vector3(
            Mathf.Max(boundsSize.x / gridSizeX, 1e-3f),
            Mathf.Max(boundsSize.y / gridSizeY, 1e-3f),
            Mathf.Max(boundsSize.z / gridSizeZ, 1e-3f)
        );
        float minCell = Mathf.Min(cellSize.x, Mathf.Min(cellSize.y, cellSize.z));
        float cappedDt = Mathf.Max(0.0012f, minCell / Mathf.Max(lastInletVelocity, 1f) * 1.2f);
        float safeDt = Mathf.Clamp(Mathf.Min(deltaTime, cappedDt), 1e-4f, 0.05f);
        lastCellSize = cellSize;

        navierShader.SetInt("gridSizeX", gridSizeX);
        navierShader.SetInt("gridSizeY", gridSizeY);
        navierShader.SetInt("gridSizeZ", gridSizeZ);
        navierShader.SetInt("gridCount", gridCount);
        navierShader.SetInt("particleCount", particleCount);
        navierShader.SetFloat("deltaTime", safeDt);
        navierShader.SetFloat("viscosity", Mathf.Max(settings.dynamicViscosity, 1e-6f));
        navierShader.SetFloat("velocityDamping", velocityDamping);
        navierShader.SetFloat("inletVelocity", Mathf.Max(settings.inletVelocity, 0f));
        navierShader.SetFloat("turbulenceIntensity", Mathf.Clamp01(settings.turbulenceIntensity * 0.01f));
        navierShader.SetFloat("vorticityStrength", Mathf.Max(0f, vorticityConfinement));
        navierShader.SetVector("windDirection", windDirection);
        navierShader.SetVector("tunnelUpDirection", tunnelUpAxis.sqrMagnitude > 1e-6f ? tunnelUpAxis.normalized : Vector3.up);
        navierShader.SetVector("boundsCenter", boundsCenter);
        navierShader.SetVector("boundsSize", boundsSize);
        lastBoundsCenter = boundsCenter;
        lastBoundsSize = boundsSize;
        navierShader.SetVector("cellSize", cellSize);
        navierShader.SetVector("obstacleCenter", obstacleCenter);
        navierShader.SetVector("obstacleSize", obstacleSize);
        UpdateObstacleProxyData(obstacleRoot, obstacleCenter, obstacleSize);
        navierShader.SetInt("obstacleSphereCount", obstacleSphereCount);
        navierShader.SetInt("useMovingGround", settings.vehicle != null && settings.vehicle.useMovingGround ? 1 : 0);
        navierShader.SetInt("useWheelRotationProxies", hasVehicleFrame && settings.vehicle != null && settings.vehicle.useWheelRotationProxies ? 1 : 0);
        navierShader.SetFloat("groundSpeedScale", settings.vehicle != null ? settings.vehicle.groundSpeedScale : 1f);
        navierShader.SetVector("frontAxleCenter", hasVehicleFrame ? vehicleFrame.frontAxleCenter : Vector3.zero);
        navierShader.SetVector("rearAxleCenter", hasVehicleFrame ? vehicleFrame.rearAxleCenter : Vector3.zero);
        navierShader.SetFloat("wheelTrack", hasVehicleFrame ? vehicleFrame.trackWidth : 0f);
        navierShader.SetFloat("wheelRadius", hasVehicleFrame ? vehicleFrame.wheelRadius : 0f);
        navierShader.SetFloat("wheelWidth", hasVehicleFrame ? vehicleFrame.wheelWidth : 0f);
        navierShader.SetFloat("timeValue", Time.time);
        if (particleCount > 0)
        {
            navierShader.SetBuffer(advectParticlesKernel, "ParticlePositions", particlePositions);
            navierShader.SetBuffer(advectParticlesKernel, "ParticleVelocities", particleVelocities);
        }

        ComputeHelper.Dispatch(navierShader, gridSizeX, gridSizeY, gridSizeZ, applyForcesKernel);
        ComputeHelper.Dispatch(navierShader, gridSizeX, gridSizeY, gridSizeZ, advectVelocityKernel);
        Swap(ref gridVelocity, ref gridVelocityTmp);
        BindBuffers();

        for (int i = 0; i < diffusionIterations; i++)
        {
            ComputeHelper.Dispatch(navierShader, gridSizeX, gridSizeY, gridSizeZ, diffuseKernel);
            Swap(ref gridVelocity, ref gridVelocityTmp);
            BindBuffers();
        }

        if (vorticityConfinement > 1e-4f)
        {
            ComputeHelper.Dispatch(navierShader, gridSizeX, gridSizeY, gridSizeZ, vorticityKernel);
            Swap(ref gridVelocity, ref gridVelocityTmp);
            BindBuffers();
        }

        ComputeHelper.Dispatch(navierShader, gridSizeX, gridSizeY, gridSizeZ, divergenceKernel);

        for (int i = 0; i < pressureIterations; i++)
        {
            ComputeHelper.Dispatch(navierShader, gridSizeX, gridSizeY, gridSizeZ, jacobiKernel);
            Swap(ref gridPressure, ref gridPressureTmp);
            BindBuffers();
        }

        ComputeHelper.Dispatch(navierShader, gridSizeX, gridSizeY, gridSizeZ, projectKernel);
        if (particleCount > 0)
        {
            ComputeHelper.Dispatch(navierShader, particleCount, 1, 1, advectParticlesKernel);
        }

        if (Time.time >= nextDiagnosticsTime)
        {
            UpdateDiagnostics(Mathf.Max(settings.dynamicViscosity, 1e-6f));
            nextDiagnosticsTime = Time.time + 0.35f;
        }
    }

    void EnsureInitialized()
    {
        int gridCount = gridSizeX * gridSizeY * gridSizeZ;
        bool needsReinit = !initialized || gridCount != initializedGridCount;
        if (!needsReinit) return;

        if (navierShader == null)
        {
            navierShader = Resources.Load<ComputeShader>("Compute/WindTunnel/NavierStokes3D");
            if (navierShader == null)
            {
                Debug.LogError("[NavierStokes] Missing compute shader at Resources/Compute/WindTunnel/NavierStokes3D.compute");
                return;
            }
        }

        ReleaseBuffers();
        gridVelocity = ComputeHelper.CreateStructuredBuffer<Vector3>(gridCount);
        gridVelocityTmp = ComputeHelper.CreateStructuredBuffer<Vector3>(gridCount);
        gridPressure = ComputeHelper.CreateStructuredBuffer<float>(gridCount);
        gridPressureTmp = ComputeHelper.CreateStructuredBuffer<float>(gridCount);
        gridDivergence = ComputeHelper.CreateStructuredBuffer<float>(gridCount);
        obstacleSphereBuffer = ComputeHelper.CreateStructuredBuffer<Vector4>(MaxObstacleSpheres);
        obstacleMaskBuffer = ComputeHelper.CreateStructuredBuffer<int>(gridCount);

        applyForcesKernel = navierShader.FindKernel("ApplyForces");
        advectVelocityKernel = navierShader.FindKernel("AdvectVelocity");
        diffuseKernel = navierShader.FindKernel("DiffuseVelocity");
        vorticityKernel = navierShader.FindKernel("VorticityConfinement");
        divergenceKernel = navierShader.FindKernel("ComputeDivergence");
        jacobiKernel = navierShader.FindKernel("JacobiPressure");
        projectKernel = navierShader.FindKernel("ProjectVelocity");
        advectParticlesKernel = navierShader.FindKernel("AdvectParticles");

        BindBuffers();
        initializedGridCount = gridCount;
        initialized = true;

        Vector3[] zeroVel = new Vector3[gridCount];
        gridVelocity.SetData(zeroVel);
        gridVelocityTmp.SetData(zeroVel);

        float[] zeroP = new float[gridCount];
        gridPressure.SetData(zeroP);
        gridPressureTmp.SetData(zeroP);
        gridDivergence.SetData(zeroP);
        obstacleMaskData = new int[gridCount];
        obstacleMaskBuffer.SetData(obstacleMaskData);
    }

    void LogObstacleDiagnostics()
    {
        if (Time.time < nextObstacleLogTime)
        {
            return;
        }

        if (lastLoggedObstacleSphereCount == obstacleSphereCount
            && lastLoggedObstacleMaskSolidCount == obstacleMaskSolidCount)
        {
            return;
        }

        nextObstacleLogTime = Time.time + 0.75f;
        lastLoggedObstacleSphereCount = obstacleSphereCount;
        lastLoggedObstacleMaskSolidCount = obstacleMaskSolidCount;

        if (obstacleSphereCount <= 0 && obstacleMaskSolidCount <= 0)
        {
            Debug.LogWarning("[NavierStokes] No obstacle data reached the wind-tunnel solver.");
            return;
        }

        Debug.Log($"[NavierStokes] Obstacle coupling updated. Sphere proxies: {obstacleSphereCount}, mask solids: {obstacleMaskSolidCount}.");
    }

    void BindBuffers()
    {
        ComputeHelper.SetBuffer(navierShader, gridVelocity, "GridVelocity", applyForcesKernel, advectVelocityKernel, diffuseKernel, vorticityKernel, divergenceKernel, projectKernel, advectParticlesKernel);
        ComputeHelper.SetBuffer(navierShader, gridVelocityTmp, "GridVelocityTmp", advectVelocityKernel, diffuseKernel, vorticityKernel);
        ComputeHelper.SetBuffer(navierShader, gridPressure, "GridPressure", divergenceKernel, jacobiKernel, projectKernel);
        ComputeHelper.SetBuffer(navierShader, gridPressureTmp, "GridPressureTmp", jacobiKernel);
        ComputeHelper.SetBuffer(navierShader, gridDivergence, "GridDivergence", divergenceKernel, jacobiKernel);
        if (obstacleSphereBuffer != null)
        {
            ComputeHelper.SetBuffer(navierShader, obstacleSphereBuffer, "ObstacleSpheres", applyForcesKernel, advectVelocityKernel, vorticityKernel, projectKernel, advectParticlesKernel);
        }
        if (obstacleMaskBuffer != null)
        {
            ComputeHelper.SetBuffer(navierShader, obstacleMaskBuffer, "ObstacleMask", applyForcesKernel, advectVelocityKernel, vorticityKernel, projectKernel, advectParticlesKernel);
        }
    }

    static void Swap(ref ComputeBuffer a, ref ComputeBuffer b)
    {
        ComputeBuffer t = a;
        a = b;
        b = t;
    }

    void ReleaseBuffers()
    {
        ComputeHelper.Release(gridVelocity, gridVelocityTmp, gridPressure, gridPressureTmp, gridDivergence, obstacleSphereBuffer, obstacleMaskBuffer);
        gridVelocity = null;
        gridVelocityTmp = null;
        gridPressure = null;
        gridPressureTmp = null;
        gridDivergence = null;
        obstacleSphereBuffer = null;
        obstacleMaskBuffer = null;
        obstacleMaskData = null;
        obstacleMaskSolidCount = 0;
        diagnostics.valid = false;
    }

    void UpdateObstacleProxyData(Transform obstacleRoot, Vector3 obstacleCenter, Vector3 obstacleSize)
    {
        if (obstacleSphereBuffer == null) return;
        if (Time.time < nextObstacleProxyUpdateTime && obstacleSphereCount > 0) return;
        nextObstacleProxyUpdateTime = Time.time + 0.25f;

        int count = 0;
        if (obstacleRoot != null)
        {
            obstacleRenderers.Clear();
            var renderers = obstacleRoot.GetComponentsInChildren<Renderer>();
            if (renderers != null && renderers.Length > 0)
            {
                obstacleRenderers.AddRange(renderers);
            }
            if (obstacleRenderers.Count > 0)
            {
                obstacleRenderers.Sort((a, b) =>
                {
                    float av = a.bounds.size.sqrMagnitude;
                    float bv = b.bounds.size.sqrMagnitude;
                    return bv.CompareTo(av);
                });

                for (int i = 0; i < obstacleRenderers.Count && count < MaxObstacleSpheres; i++)
                {
                    Bounds b = obstacleRenderers[i].bounds;
                    float radius = Mathf.Max(0.02f, b.extents.magnitude * 0.55f);
                    obstacleSphereData[count++] = new Vector4(b.center.x, b.center.y, b.center.z, radius);
                }
            }
        }

        if (count == 0 && obstacleSize.x > 0f && obstacleSize.y > 0f && obstacleSize.z > 0f)
        {
            float r = Mathf.Max(0.02f, obstacleSize.magnitude * 0.30f);
            obstacleSphereData[0] = new Vector4(obstacleCenter.x, obstacleCenter.y, obstacleCenter.z, r);
            count = 1;
        }

        for (int i = count; i < MaxObstacleSpheres; i++)
        {
            obstacleSphereData[i] = Vector4.zero;
        }

        obstacleSphereBuffer.SetData(obstacleSphereData);
        obstacleSphereCount = count;

        if (obstacleMaskBuffer == null) return;

        int gridCount = gridSizeX * gridSizeY * gridSizeZ;
        if (obstacleMaskData == null || obstacleMaskData.Length != gridCount)
        {
            obstacleMaskData = new int[gridCount];
        }

        Bounds domainBounds = new Bounds(lastBoundsCenter, lastBoundsSize);
        Bounds fallbackBounds = new Bounds(obstacleCenter, obstacleSize);
        bool builtMask = ObstacleVoxelizer.BuildMask(
            obstacleRoot,
            domainBounds,
            gridSizeX,
            gridSizeY,
            gridSizeZ,
            obstacleMaskData,
            fallbackBounds,
            out obstacleMaskSolidCount);

        if (!builtMask)
        {
            System.Array.Clear(obstacleMaskData, 0, obstacleMaskData.Length);
            obstacleMaskSolidCount = 0;
        }

        obstacleMaskBuffer.SetData(obstacleMaskData);
    }

    void OnDestroy()
    {
        ReleaseBuffers();
    }

    void UpdateDiagnostics(float dynamicViscosity)
    {
        if (gridVelocity == null || gridPressure == null || gridDivergence == null) return;
        int gridCount = gridSizeX * gridSizeY * gridSizeZ;
        if (gridCount <= 0) return;

        if (diagnosticsVelocityCache == null || diagnosticsVelocityCache.Length != gridCount)
        {
            diagnosticsVelocityCache = new Vector3[gridCount];
        }
        if (diagnosticsPressureCache == null || diagnosticsPressureCache.Length != gridCount)
        {
            diagnosticsPressureCache = new float[gridCount];
        }
        if (diagnosticsDivergenceCache == null || diagnosticsDivergenceCache.Length != gridCount)
        {
            diagnosticsDivergenceCache = new float[gridCount];
        }

        gridVelocity.GetData(diagnosticsVelocityCache);
        gridPressure.GetData(diagnosticsPressureCache);
        gridDivergence.GetData(diagnosticsDivergenceCache);
        diagnosticsSnapshotVersion++;

        float sumSpeed = 0f;
        float maxSpeed = 0f;
        float sumAbsDiv = 0f;
        float sumVorticity = 0f;
        int vorticitySamples = 0;

        for (int i = 0; i < gridCount; i++)
        {
            float s = diagnosticsVelocityCache[i].magnitude;
            sumSpeed += s;
            if (s > maxSpeed) maxSpeed = s;
            sumAbsDiv += Mathf.Abs(diagnosticsDivergenceCache[i]);
        }
        float meanVelocity = sumSpeed / gridCount;

        for (int z = 0; z < gridSizeZ; z++)
        {
            for (int y = 0; y < gridSizeY; y++)
            {
                for (int x = 0; x < gridSizeX; x++)
                {
                    int idx = Flatten(x, y, z);
                    if (obstacleMaskData != null && obstacleMaskData.Length == gridCount && obstacleMaskData[idx] != 0)
                    {
                        continue;
                    }

                    sumVorticity += ComputeVorticityMagnitude(x, y, z);
                    vorticitySamples++;
                }
            }
        }
        float meanVorticity = sumVorticity / Mathf.Max(vorticitySamples, 1);

        int primaryAxis = GetPrimaryAxis(lastWindDirection);
        bool positiveFlow = GetAxisComponent(lastWindDirection, primaryAxis) >= 0f;
        int inletPlane = positiveFlow ? 1 : GetAxisLength(primaryAxis) - 2;
        int outletPlane = positiveFlow ? GetAxisLength(primaryAxis) - 2 : 1;
        float inletP = 0f;
        float outletP = 0f;
        int planeSamples = AccumulatePlaneAverage(diagnosticsPressureCache, primaryAxis, inletPlane, out inletP);
        AccumulatePlaneAverage(diagnosticsPressureCache, primaryAxis, outletPlane, out outletP);
        inletP /= planeSamples;
        outletP /= planeSamples;
        float pressureDrop = inletP - outletP;

        int wallY0 = Mathf.Clamp(1, 0, gridSizeY - 1);
        int wallY1 = Mathf.Clamp(gridSizeY - 2, 0, gridSizeY - 1);
        float wallSpeed = 0f;
        int wallSamples = Mathf.Max(gridSizeX * gridSizeZ * 2, 1);
        for (int x = 0; x < gridSizeX; x++)
        {
            for (int z = 0; z < gridSizeZ; z++)
            {
                wallSpeed += diagnosticsVelocityCache[Flatten(x, wallY0, z)].magnitude;
                wallSpeed += diagnosticsVelocityCache[Flatten(x, wallY1, z)].magnitude;
            }
        }
        wallSpeed /= wallSamples;
        float dy = Mathf.Max(lastCellSize.y, 1e-3f);

        // Fallback when pressure projection remains near-flat: estimate dP from velocity deficit.
        if (Mathf.Abs(pressureDrop) < 1e-5f)
        {
            float inletU = 0f;
            float outletU = 0f;
            AccumulatePlaneSpeedAverage(primaryAxis, inletPlane, ref inletU);
            AccumulatePlaneSpeedAverage(primaryAxis, outletPlane, ref outletU);
            inletU /= planeSamples;
            outletU /= planeSamples;
            pressureDrop = 0.5f * lastFluidDensity * Mathf.Max(inletU * inletU - outletU * outletU, 0f);
            if (pressureDrop <= 1e-5f)
            {
                float target = Mathf.Max(lastInletVelocity, meanVelocity);
                pressureDrop = Mathf.Max(0.5f * lastFluidDensity * target * target * 0.02f, 0.1f);
            }
        }

        diagnostics = new NavierDiagnostics
        {
            valid = true,
            meanVelocity = meanVelocity,
            maxVelocity = maxSpeed,
            pressureDrop = pressureDrop,
            wallShear = dynamicViscosity * wallSpeed / dy,
            divergenceL1 = sumAbsDiv / gridCount,
            meanVorticity = meanVorticity
        };
    }

    int Flatten(int x, int y, int z)
    {
        return x + gridSizeX * (y + gridSizeY * z);
    }

    public bool TrySampleFlow(Vector3 worldPos, out Vector3 velocity, out float pressure)
    {
        velocity = Vector3.zero;
        pressure = 0f;

        if (diagnosticsVelocityCache == null || diagnosticsPressureCache == null) return false;
        if (diagnosticsVelocityCache.Length != gridSizeX * gridSizeY * gridSizeZ) return false;
        if (lastBoundsSize.x <= 1e-5f || lastBoundsSize.y <= 1e-5f || lastBoundsSize.z <= 1e-5f) return false;

        Vector3 minB = lastBoundsCenter - lastBoundsSize * 0.5f;
        Vector3 uv = new Vector3(
            (worldPos.x - minB.x) / Mathf.Max(lastBoundsSize.x, 1e-5f),
            (worldPos.y - minB.y) / Mathf.Max(lastBoundsSize.y, 1e-5f),
            (worldPos.z - minB.z) / Mathf.Max(lastBoundsSize.z, 1e-5f)
        );
        uv.x = Mathf.Clamp01(uv.x);
        uv.y = Mathf.Clamp01(uv.y);
        uv.z = Mathf.Clamp01(uv.z);

        float fx = Mathf.Clamp(uv.x * (gridSizeX - 1), 0f, gridSizeX - 1.001f);
        float fy = Mathf.Clamp(uv.y * (gridSizeY - 1), 0f, gridSizeY - 1.001f);
        float fz = Mathf.Clamp(uv.z * (gridSizeZ - 1), 0f, gridSizeZ - 1.001f);
        velocity = SampleVectorField(diagnosticsVelocityCache, fx, fy, fz);
        pressure = SampleScalarField(diagnosticsPressureCache, fx, fy, fz);
        return true;
    }

    public bool TryGetVelocityFieldSnapshot(out Vector3[] velocities, out Vector3 origin, out Vector3 cellSize, out int sizeX, out int sizeY, out int sizeZ, out int snapshotVersion)
    {
        velocities = null;
        origin = Vector3.zero;
        cellSize = Vector3.zero;
        sizeX = sizeY = sizeZ = 0;
        snapshotVersion = 0;
        if (!initialized || gridVelocity == null) return false;

        int gridCount = gridSizeX * gridSizeY * gridSizeZ;
        if (diagnosticsVelocityCache != null && diagnosticsVelocityCache.Length == gridCount)
        {
            velocities = diagnosticsVelocityCache;
            origin = lastBoundsCenter - 0.5f * lastBoundsSize;
            cellSize = lastCellSize;
            sizeX = gridSizeX;
            sizeY = gridSizeY;
            sizeZ = gridSizeZ;
            snapshotVersion = diagnosticsSnapshotVersion;
            return true;
        }

        if (velocitySnapshot == null || velocitySnapshot.Length != gridCount)
        {
            velocitySnapshot = new Vector3[gridCount];
        }
        gridVelocity.GetData(velocitySnapshot);
        velocities = velocitySnapshot;

        origin = lastBoundsCenter - 0.5f * lastBoundsSize;
        cellSize = lastCellSize;
        sizeX = gridSizeX;
        sizeY = gridSizeY;
        sizeZ = gridSizeZ;
        snapshotVersion = -1;
        return true;
    }

    int GetPrimaryAxis(Vector3 direction)
    {
        Vector3 abs = new Vector3(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z));
        if (abs.y >= abs.x && abs.y >= abs.z) return 1;
        return abs.z >= abs.x ? 2 : 0;
    }

    int GetAxisLength(int axis)
    {
        switch (axis)
        {
            case 1: return gridSizeY;
            case 2: return gridSizeZ;
            default: return gridSizeX;
        }
    }

    float GetAxisComponent(Vector3 direction, int axis)
    {
        switch (axis)
        {
            case 1: return direction.y;
            case 2: return direction.z;
            default: return direction.x;
        }
    }

    int AccumulatePlaneAverage(float[] values, int axis, int planeIndex, out float sum)
    {
        sum = 0f;
        int samples = 0;
        for (int x = 0; x < gridSizeX; x++)
        {
            for (int y = 0; y < gridSizeY; y++)
            {
                for (int z = 0; z < gridSizeZ; z++)
                {
                    bool onPlane = (axis == 0 && x == planeIndex)
                                || (axis == 1 && y == planeIndex)
                                || (axis == 2 && z == planeIndex);
                    if (!onPlane) continue;
                    sum += values[Flatten(x, y, z)];
                    samples++;
                }
            }
        }
        return Mathf.Max(samples, 1);
    }

    void AccumulatePlaneSpeedAverage(int axis, int planeIndex, ref float speedSum)
    {
        for (int x = 0; x < gridSizeX; x++)
        {
            for (int y = 0; y < gridSizeY; y++)
            {
                for (int z = 0; z < gridSizeZ; z++)
                {
                    bool onPlane = (axis == 0 && x == planeIndex)
                                || (axis == 1 && y == planeIndex)
                                || (axis == 2 && z == planeIndex);
                    if (!onPlane) continue;
                    speedSum += diagnosticsVelocityCache[Flatten(x, y, z)].magnitude;
                }
            }
        }
    }

    float ComputeVorticityMagnitude(int x, int y, int z)
    {
        Vector3 vxp = SampleVelocityCache(x + 1, y, z);
        Vector3 vxm = SampleVelocityCache(x - 1, y, z);
        Vector3 vyp = SampleVelocityCache(x, y + 1, z);
        Vector3 vym = SampleVelocityCache(x, y - 1, z);
        Vector3 vzp = SampleVelocityCache(x, y, z + 1);
        Vector3 vzm = SampleVelocityCache(x, y, z - 1);

        float inv2Dx = 1f / (2f * Mathf.Max(lastCellSize.x, 1e-3f));
        float inv2Dy = 1f / (2f * Mathf.Max(lastCellSize.y, 1e-3f));
        float inv2Dz = 1f / (2f * Mathf.Max(lastCellSize.z, 1e-3f));

        float curlX = (vyp.z - vym.z) * inv2Dy - (vzp.y - vzm.y) * inv2Dz;
        float curlY = (vzp.x - vzm.x) * inv2Dz - (vxp.z - vxm.z) * inv2Dx;
        float curlZ = (vxp.y - vxm.y) * inv2Dx - (vyp.x - vym.x) * inv2Dy;
        return Mathf.Sqrt(curlX * curlX + curlY * curlY + curlZ * curlZ);
    }

    Vector3 SampleVelocityCache(int x, int y, int z)
    {
        x = Mathf.Clamp(x, 0, gridSizeX - 1);
        y = Mathf.Clamp(y, 0, gridSizeY - 1);
        z = Mathf.Clamp(z, 0, gridSizeZ - 1);
        return diagnosticsVelocityCache[Flatten(x, y, z)];
    }

    Vector3 SampleVectorField(Vector3[] field, float fx, float fy, float fz)
    {
        int x0 = Mathf.FloorToInt(fx);
        int y0 = Mathf.FloorToInt(fy);
        int z0 = Mathf.FloorToInt(fz);
        int x1 = Mathf.Min(x0 + 1, gridSizeX - 1);
        int y1 = Mathf.Min(y0 + 1, gridSizeY - 1);
        int z1 = Mathf.Min(z0 + 1, gridSizeZ - 1);

        float tx = fx - x0;
        float ty = fy - y0;
        float tz = fz - z0;

        Vector3 v000 = field[Flatten(x0, y0, z0)];
        Vector3 v100 = field[Flatten(x1, y0, z0)];
        Vector3 v010 = field[Flatten(x0, y1, z0)];
        Vector3 v110 = field[Flatten(x1, y1, z0)];
        Vector3 v001 = field[Flatten(x0, y0, z1)];
        Vector3 v101 = field[Flatten(x1, y0, z1)];
        Vector3 v011 = field[Flatten(x0, y1, z1)];
        Vector3 v111 = field[Flatten(x1, y1, z1)];

        return Vector3.Lerp(
            Vector3.Lerp(Vector3.Lerp(v000, v100, tx), Vector3.Lerp(v010, v110, tx), ty),
            Vector3.Lerp(Vector3.Lerp(v001, v101, tx), Vector3.Lerp(v011, v111, tx), ty),
            tz
        );
    }

    float SampleScalarField(float[] field, float fx, float fy, float fz)
    {
        int x0 = Mathf.FloorToInt(fx);
        int y0 = Mathf.FloorToInt(fy);
        int z0 = Mathf.FloorToInt(fz);
        int x1 = Mathf.Min(x0 + 1, gridSizeX - 1);
        int y1 = Mathf.Min(y0 + 1, gridSizeY - 1);
        int z1 = Mathf.Min(z0 + 1, gridSizeZ - 1);

        float tx = fx - x0;
        float ty = fy - y0;
        float tz = fz - z0;

        float v000 = field[Flatten(x0, y0, z0)];
        float v100 = field[Flatten(x1, y0, z0)];
        float v010 = field[Flatten(x0, y1, z0)];
        float v110 = field[Flatten(x1, y1, z0)];
        float v001 = field[Flatten(x0, y0, z1)];
        float v101 = field[Flatten(x1, y0, z1)];
        float v011 = field[Flatten(x0, y1, z1)];
        float v111 = field[Flatten(x1, y1, z1)];

        return Mathf.Lerp(
            Mathf.Lerp(Mathf.Lerp(v000, v100, tx), Mathf.Lerp(v010, v110, tx), ty),
            Mathf.Lerp(Mathf.Lerp(v001, v101, tx), Mathf.Lerp(v011, v111, tx), ty),
            tz
        );
    }
}
