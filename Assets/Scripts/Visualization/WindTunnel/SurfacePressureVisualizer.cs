using UnityEngine;
using AeroFlow.Physics;
using AeroFlow.Rendering;
using System.Collections.Generic;
using System;

namespace AeroFlow.Visualization
{
    /// <summary>
    /// Colors mesh vertices based on approximated Surface Pressure (Cp) or
    /// Surface Friction (wall shear stress τ_w).
    /// Cp = 1 - (V_local / V_freestream)^2
    /// τ_w ≈ μ · V_local / δ  (thin-boundary-layer approximation)
    /// </summary>
    public class SurfacePressureVisualizer : MonoBehaviour
    {
        private const int ScalarUvChannel = 3;
        private static readonly int PressureMinId = Shader.PropertyToID("_PressureMin");
        private static readonly int PressureMaxId = Shader.PropertyToID("_PressureMax");
        private static readonly int UsePressureMapId = Shader.PropertyToID("_UsePressureMap");

        private static int globalScalarFrame = -1;
        private static float globalScalarMin = 0f;
        private static float globalScalarMax = 1f;

        public enum SurfaceMode
        {
            Pressure,
            Friction
        }

        public void SetSurfaceMode(SurfaceMode mode, bool forceRefresh = true)
        {
            bool changed = surfaceMode != mode;
            surfaceMode = mode;
            if (!forceRefresh || (!changed && isActiveAndEnabled == false))
            {
                return;
            }

            nextUpdateTime = 0f;
            if (isActiveAndEnabled)
            {
                UpdateSurfaceColors();
            }
        }

        private MeshFilter meshFilter;
        private Renderer targetRenderer;
        private Mesh originalMesh;
        private Vector3[] vertices;
        private Vector3[] normals;
        private Color[] colors;
        private Material[] originalMaterials;
        private Material pressureMaterial;
        private bool pressureMaterialApplied;
        private readonly List<Vector4> scalarChannelData = new List<Vector4>(1024);

        private WindTunnelSimulation3D wind;
        private NavierStokesGridSolver navier;
        private SurfaceAeroSolver surfaceAeroSolver;
        private float nextUpdateTime;
        private bool isSubscribedToSolver;

        [Header("Visualization")]
        public Gradient pressureGradient; // Blue (Low) -> Red (High)
        public Gradient frictionGradient; // Blue (Low) -> Green -> Yellow -> Red (High)
        public float sensitivity = 1.0f;
        [Range(0.03f, 0.4f)] public float updateInterval = 0.08f;
        public SurfaceMode surfaceMode = SurfaceMode.Pressure;
        [Range(0f, 1f)] public float pressureSignatureShearWeight = 0.22f;
        [Range(0f, 1f)] public float pressureSignatureStagnationWeight = 0.16f;

        void Start()
        {
            meshFilter = GetComponent<MeshFilter>();
            targetRenderer = GetComponent<Renderer>();
            if (meshFilter == null) return;

            CacheOriginalMaterials();
            if (enabled)
            {
                ApplyPressureMaterial();
            }

            originalMesh = meshFilter.mesh; // Clone
            originalMesh.MarkDynamic();
            vertices = originalMesh.vertices;
            normals = originalMesh.normals;
            colors = new Color[vertices.Length];
            EnsureScalarChannelCapacity();
            
            wind = FindAnyObjectByType<WindTunnelSimulation3D>();
            if (wind != null) navier = wind.navierStokesSolver;
            EnsureSolverSubscription();
            
            // Setup default gradient if none
            if (pressureGradient == null)
            {
                pressureGradient = new Gradient();
                pressureGradient.mode = GradientMode.Blend;
                pressureGradient.SetKeys(
                    new GradientColorKey[]
                    {
                        new GradientColorKey(new Color(0.02f, 0.05f, 0.55f), 0.00f),
                        new GradientColorKey(new Color(0.04f, 0.35f, 0.92f), 0.15f),
                        new GradientColorKey(new Color(0.06f, 0.70f, 0.95f), 0.30f),
                        new GradientColorKey(new Color(0.10f, 0.92f, 0.55f), 0.45f),
                        new GradientColorKey(new Color(0.95f, 0.92f, 0.10f), 0.60f),
                        new GradientColorKey(new Color(1.00f, 0.55f, 0.05f), 0.78f),
                        new GradientColorKey(new Color(0.90f, 0.10f, 0.05f), 1.00f)
                    },
                    new GradientAlphaKey[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(1.0f, 1.0f) }
                );
            }

            // AirShaper-style friction gradient: blue -> green -> yellow -> red
            if (frictionGradient == null)
            {
                frictionGradient = new Gradient();
                frictionGradient.SetKeys(
                    new GradientColorKey[]
                    {
                        new GradientColorKey(new Color(0.05f, 0.15f, 0.80f), 0.00f),
                        new GradientColorKey(new Color(0.08f, 0.72f, 0.45f), 0.30f),
                        new GradientColorKey(new Color(0.85f, 0.90f, 0.12f), 0.60f),
                        new GradientColorKey(new Color(1.00f, 0.50f, 0.08f), 0.80f),
                        new GradientColorKey(new Color(0.92f, 0.12f, 0.08f), 1.00f)
                    },
                    new GradientAlphaKey[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(1.0f, 1.0f) }
                );
            }

            SetDefaultGlobalRange();
            UpdateSurfaceColors();
        }

        void OnEnable()
        {
            ApplyPressureMaterial();
            ResolveSimulationReferences();
            EnsureSolverSubscription();
            UpdateSurfaceColors();
        }

        void OnDisable()
        {
            RemoveSolverSubscription();
            RestoreOriginalMaterials();
        }

        private void CacheOriginalMaterials()
        {
            if (targetRenderer == null || originalMaterials != null) return;
            originalMaterials = targetRenderer.sharedMaterials;
        }

        private void ApplyPressureMaterial()
        {
            if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
            if (targetRenderer == null) return;
            CacheOriginalMaterials();

            if (pressureMaterial == null)
            {
                Shader shader = RuntimeShaderResolver.FindPressureShader();
                if (shader != null)
                {
                    pressureMaterial = new Material(shader);
                    pressureMaterial.name = "SurfacePressureHeatmap";
                }
            }

            if (pressureMaterial == null) return;
            pressureMaterial.SetFloat(UsePressureMapId, 1f);

            var mats = targetRenderer.sharedMaterials;
            if (mats == null || mats.Length == 0)
            {
                mats = new Material[1];
            }
            for (int i = 0; i < mats.Length; i++)
            {
                mats[i] = pressureMaterial;
            }
            targetRenderer.sharedMaterials = mats;
            pressureMaterialApplied = true;
        }

        private void RestoreOriginalMaterials()
        {
            if (!pressureMaterialApplied || targetRenderer == null || originalMaterials == null || originalMaterials.Length == 0)
            {
                return;
            }

            targetRenderer.sharedMaterials = originalMaterials;
            pressureMaterialApplied = false;
        }

        public void UpdateSurfaceColors()
        {
            if (!isActiveAndEnabled || !EnsureMeshDataIsValid())
            {
                return;
            }

            ResolveSimulationReferences();
            EnsureSolverSubscription();
            if (wind == null || navier == null)
            {
                return;
            }

            if (Time.time < nextUpdateTime)
            {
                return;
            }

            nextUpdateTime = Time.time + Mathf.Max(0.03f, updateInterval);
            EnsureScalarChannelCapacity();

            if (surfaceAeroSolver != null &&
                surfaceAeroSolver.TryGetSurfaceField(
                    targetRenderer,
                    surfaceMode == SurfaceMode.Friction ? SurfaceAeroSolver.SurfaceFieldMode.Friction : SurfaceAeroSolver.SurfaceFieldMode.Pressure,
                    out float[] surfaceValues,
                    out float solverMin,
                    out float solverMax) &&
                surfaceValues != null &&
                surfaceValues.Length == vertices.Length)
            {
                for (int i = 0; i < surfaceValues.Length; i++)
                {
                    scalarChannelData[i] = new Vector4(surfaceValues[i], 0f, 0f, 0f);
                }

                if (Mathf.Approximately(solverMin, solverMax))
                {
                    solverMax = solverMin + 0.001f;
                }

                if (pressureMaterial != null)
                {
                    pressureMaterial.SetFloat(UsePressureMapId, 1f);
                }

                ApplyVertexColorsFromScalars(surfaceValues, solverMin, solverMax);
                originalMesh.SetUVs(ScalarUvChannel, scalarChannelData);
                RegisterGlobalRange(solverMin, solverMax);
                return;
            }

            float vFree = Mathf.Max(0.1f, wind.settings.inletVelocity);
            float rho = Mathf.Max(wind.settings.airDensity, 0.01f);
            float mu = Mathf.Max(wind.settings.dynamicViscosity, 1e-6f);
            float qInf = 0.5f * rho * vFree * vFree;
            Vector3 flowDir = wind.ResolveWindDirection().normalized;
            if (flowDir.sqrMagnitude < 1e-6f) flowDir = Vector3.right;

            Matrix4x4 localToWorld = transform.localToWorldMatrix;
            bool isFrictionMode = surfaceMode == SurfaceMode.Friction;
            float localMin = float.PositiveInfinity;
            float localMax = float.NegativeInfinity;

            if (isFrictionMode)
            {
                float delta = 0.02f;
                if (navier.TrySampleFlow(transform.position, out _, out _))
                {
                    Bounds tunnelBounds = wind.GetTunnelBounds();
                    float tunnelLength = Mathf.Max(tunnelBounds.size.x, Mathf.Max(tunnelBounds.size.y, tunnelBounds.size.z));
                    delta = Mathf.Max(0.005f, tunnelLength / 64f);
                }

                int vertexCount = vertices.Length;
                int normalCount = normals != null ? normals.Length : 0;
                int count = Mathf.Min(vertexCount, normalCount > 0 ? normalCount : vertexCount);
                for (int i = 0; i < count; i++)
                {
                    Vector3 worldPos = localToWorld.MultiplyPoint3x4(vertices[i]);
                    Vector3 worldNormal = localToWorld.MultiplyVector(normals[i]).normalized;

                    Vector3 velocityAtPoint = flowDir * vFree;
                    bool sampled = navier.TrySampleFlow(worldPos, out velocityAtPoint, out _);
                    if (!float.IsFinite(velocityAtPoint.x) || !float.IsFinite(velocityAtPoint.y) || !float.IsFinite(velocityAtPoint.z))
                    {
                        velocityAtPoint = flowDir * vFree;
                        sampled = false;
                    }

                    Vector3 vTangential = velocityAtPoint - worldNormal * Vector3.Dot(velocityAtPoint, worldNormal);
                    float tau = mu * vTangential.magnitude / delta;
                    if (!float.IsFinite(tau)) tau = 0f;

                    float facing = Mathf.Abs(Vector3.Dot(worldNormal, flowDir));
                    float geometricTau = mu * vFree * (1f - facing * 0.6f) / delta;
                    float blendWeight = sampled ? 0.72f : 0.25f;
                    float displayTau = Mathf.Lerp(geometricTau, tau, blendWeight);
                    if (!float.IsFinite(displayTau))
                    {
                        displayTau = 0f;
                    }

                    localMin = Mathf.Min(localMin, displayTau);
                    localMax = Mathf.Max(localMax, displayTau);
                    scalarChannelData[i] = new Vector4(displayTau, 0f, 0f, 0f);
                }
            }
            else
            {
                float safeSensitivity = Mathf.Max(0.0001f, Mathf.Abs(sensitivity));
                float minCp = -2.0f * safeSensitivity;
                float maxCp = 1.2f * safeSensitivity;
                if (!float.IsFinite(minCp) || !float.IsFinite(maxCp) || Mathf.Abs(maxCp - minCp) < 1e-6f)
                {
                    minCp = -2.0f;
                    maxCp = 1.2f;
                }

                Bounds tunnelBounds = wind.GetTunnelBounds();
                float tunnelLength = Mathf.Max(tunnelBounds.size.x, Mathf.Max(tunnelBounds.size.y, tunnelBounds.size.z));
                float delta = Mathf.Max(0.005f, tunnelLength / 64f);
                float maxTau = Mathf.Max(mu * vFree / delta * 2.5f, 1e-6f);
                float shearWeight = Mathf.Clamp01(pressureSignatureShearWeight);
                float stagnationWeight = Mathf.Clamp01(pressureSignatureStagnationWeight);

                int vertexCount = vertices.Length;
                int normalCount = normals != null ? normals.Length : 0;
                int count = Mathf.Min(vertexCount, normalCount > 0 ? normalCount : vertexCount);
                for (int i = 0; i < count; i++)
                {
                    Vector3 worldPos = localToWorld.MultiplyPoint3x4(vertices[i]);
                    Vector3 worldNormal = localToWorld.MultiplyVector(normals[i]).normalized;
                    Vector3 velocityAtPoint = Vector3.zero;
                    float sampledPressure = 0f;
                    bool sampled = navier.TrySampleFlow(worldPos, out velocityAtPoint, out sampledPressure);
                    if (!sampled)
                    {
                        velocityAtPoint = wind.ResolveWindDirection() * vFree;
                    }
                    if (!float.IsFinite(velocityAtPoint.x) || !float.IsFinite(velocityAtPoint.y) || !float.IsFinite(velocityAtPoint.z))
                    {
                        velocityAtPoint = Vector3.zero;
                    }
                    if (!float.IsFinite(sampledPressure))
                    {
                        sampledPressure = 0f;
                    }

                    float localSpeed = velocityAtPoint.magnitude;
                    if (!float.IsFinite(localSpeed)) localSpeed = 0f;

                    float solverCp = sampled && qInf > 1e-5f
                        ? sampledPressure / qInf
                        : 1.0f - Mathf.Pow(localSpeed / vFree, 2f);
                    if (!float.IsFinite(solverCp))
                    {
                        solverCp = 0f;
                    }

                    float facing = Mathf.Clamp(Vector3.Dot(worldNormal, -flowDir), -1f, 1f);
                    float geometricCp = facing >= 0f
                        ? Mathf.Pow(facing, 1.6f)
                        : -0.60f * Mathf.Pow(-facing, 1.15f);

                    Vector3 tangentialVelocity = velocityAtPoint - worldNormal * Vector3.Dot(velocityAtPoint, worldNormal);
                    float tauProxy = mu * tangentialVelocity.magnitude / delta;
                    if (!float.IsFinite(tauProxy))
                    {
                        tauProxy = 0f;
                    }

                    float shearSignature = Mathf.Clamp01(tauProxy / maxTau);
                    float stagnationSignature = facing > 0f ? Mathf.Pow(facing, 1.35f) : 0f;
                    float solverBlend = sampled ? 0.72f : 0.26f;
                    float cpDisplay = Mathf.Lerp(geometricCp, solverCp, solverBlend);
                    cpDisplay += shearSignature * shearWeight;
                    cpDisplay += stagnationSignature * stagnationWeight;
                    cpDisplay = Mathf.Clamp(cpDisplay, minCp, maxCp);
                    if (!float.IsFinite(cpDisplay))
                    {
                        cpDisplay = 0f;
                    }

                    localMin = Mathf.Min(localMin, cpDisplay);
                    localMax = Mathf.Max(localMax, cpDisplay);
                    scalarChannelData[i] = new Vector4(cpDisplay, 0f, 0f, 0f);
                }
            }

            if (!float.IsFinite(localMin) || !float.IsFinite(localMax))
            {
                localMin = 0f;
                localMax = 1f;
            }

            float shaderMin = localMin;
            float shaderMax = localMax;

            if (Mathf.Approximately(shaderMin, shaderMax))
            {
                shaderMax = shaderMin + 0.001f;
            }

            if (pressureMaterial != null)
            {
                pressureMaterial.SetFloat(UsePressureMapId, 1f);
            }

            ApplyVertexColorsFromCurrentChannel(shaderMin, shaderMax);
            originalMesh.SetUVs(ScalarUvChannel, scalarChannelData);
            RegisterGlobalRange(shaderMin, shaderMax);
        }

        private void EnsureScalarChannelCapacity()
        {
            if (vertices == null)
            {
                return;
            }

            int targetCount = vertices.Length;
            if (scalarChannelData.Count == targetCount)
            {
                return;
            }

            scalarChannelData.Clear();
            for (int i = 0; i < targetCount; i++)
            {
                scalarChannelData.Add(Vector4.zero);
            }
        }

        private static void RegisterGlobalRange(float localMin, float localMax)
        {
            int frame = Time.frameCount;
            if (globalScalarFrame != frame)
            {
                globalScalarFrame = frame;
                globalScalarMin = localMin;
                globalScalarMax = localMax;
            }
            else
            {
                globalScalarMin = Mathf.Min(globalScalarMin, localMin);
                globalScalarMax = Mathf.Max(globalScalarMax, localMax);
            }

            if (Mathf.Abs(globalScalarMax - globalScalarMin) < 1e-5f)
            {
                globalScalarMax = globalScalarMin + 1e-5f;
            }

            Shader.SetGlobalFloat(PressureMinId, globalScalarMin);
            Shader.SetGlobalFloat(PressureMaxId, globalScalarMax);
        }

        private static void SetDefaultGlobalRange()
        {
            Shader.SetGlobalFloat(PressureMinId, 0f);
            Shader.SetGlobalFloat(PressureMaxId, 1f);
        }

        private void ResolveSimulationReferences()
        {
            if (wind == null)
            {
                wind = FindAnyObjectByType<WindTunnelSimulation3D>();
            }

            if (navier == null && wind != null)
            {
                navier = wind.navierStokesSolver;
            }
            if (surfaceAeroSolver == null && wind != null)
            {
                surfaceAeroSolver = wind.surfaceAeroSolver != null
                    ? wind.surfaceAeroSolver
                    : wind.GetComponent<SurfaceAeroSolver>();
            }
        }

        private bool EnsureMeshDataIsValid()
        {
            if (meshFilter == null)
            {
                meshFilter = GetComponent<MeshFilter>();
            }
            if (meshFilter == null)
            {
                return false;
            }

            Mesh currentMesh = meshFilter.mesh;
            if (currentMesh == null)
            {
                return false;
            }

            if (originalMesh != currentMesh)
            {
                originalMesh = currentMesh;
                originalMesh.MarkDynamic();
            }

            vertices = originalMesh.vertices;
            if (vertices == null || vertices.Length == 0)
            {
                return false;
            }

            normals = originalMesh.normals;
            if (normals == null || normals.Length != vertices.Length)
            {
                bool canRecalculateNormals = false;
                for (int subMesh = 0; subMesh < originalMesh.subMeshCount; subMesh++)
                {
                    if (originalMesh.GetTopology(subMesh) == MeshTopology.Triangles)
                    {
                        canRecalculateNormals = true;
                        break;
                    }
                }

                if (canRecalculateNormals)
                {
                    originalMesh.RecalculateNormals();
                    normals = originalMesh.normals;
                }
            }

            if (normals == null || normals.Length != vertices.Length)
            {
                normals = new Vector3[vertices.Length];
                for (int i = 0; i < normals.Length; i++)
                {
                    normals[i] = Vector3.up;
                }
            }

            EnsureColorCapacity();
            EnsureScalarChannelCapacity();
            return true;
        }

        private void ApplyVertexColorsFromCurrentChannel(float minValue, float maxValue)
        {
            if (scalarChannelData == null || scalarChannelData.Count == 0)
            {
                return;
            }

            EnsureColorCapacity();
            float range = Mathf.Max(maxValue - minValue, 0.001f);
            for (int i = 0; i < scalarChannelData.Count; i++)
            {
                float normalized = Mathf.Clamp01((scalarChannelData[i].x - minValue) / range);
                colors[i] = EvaluateTurbo(normalized);
            }
            originalMesh.colors = colors;
        }

        private void ApplyVertexColorsFromScalars(float[] values, float minValue, float maxValue)
        {
            if (values == null || values.Length == 0)
            {
                return;
            }

            EnsureColorCapacity();
            float range = Mathf.Max(maxValue - minValue, 0.001f);
            int count = Mathf.Min(values.Length, colors.Length);
            for (int i = 0; i < count; i++)
            {
                float normalized = Mathf.Clamp01((values[i] - minValue) / range);
                colors[i] = EvaluateTurbo(normalized);
            }
            originalMesh.colors = colors;
        }

        private void EnsureColorCapacity()
        {
            if (vertices == null)
            {
                return;
            }

            if (colors != null && colors.Length == vertices.Length)
            {
                return;
            }

            colors = new Color[vertices.Length];
        }

        private static Color EvaluateTurbo(float t)
        {
            t = Mathf.Clamp01(t);
            float tt = t * t;
            float ttt = tt * t;
            float tttt = tt * tt;
            float ttttt = tttt * t;

            float r = 0.13572138f + 4.61539260f * t - 42.66032258f * tt + 132.13108234f * ttt - 152.94239396f * tttt + 59.28637943f * ttttt;
            float g = 0.09140261f + 2.19418839f * t + 4.84296658f * tt - 14.18503333f * ttt + 4.27729857f * tttt + 2.82956604f * ttttt;
            float b = 0.10667330f + 12.64194608f * t - 60.58204836f * tt + 110.36276771f * ttt - 89.90310912f * tttt + 27.34824973f * ttttt;

            return new Color(
                Mathf.Clamp01(r),
                Mathf.Clamp01(g),
                Mathf.Clamp01(b),
                1f);
        }

        private void EnsureSolverSubscription()
        {
            if (isSubscribedToSolver)
            {
                return;
            }

            ResolveSimulationReferences();
            if (navier == null)
            {
                return;
            }

            navier.OnSimulationCompleted += UpdateSurfaceColors;
            isSubscribedToSolver = true;
        }

        private void RemoveSolverSubscription()
        {
            if (!isSubscribedToSolver || navier == null)
            {
                isSubscribedToSolver = false;
                return;
            }

            navier.OnSimulationCompleted -= UpdateSurfaceColors;
            isSubscribedToSolver = false;
        }
    }
}
