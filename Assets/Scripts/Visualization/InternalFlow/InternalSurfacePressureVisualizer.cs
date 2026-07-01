using AeroFlow.Rendering;
using AeroFlow.Sim3D.PipeFlow;
using UnityEngine;
using UnityEngine.Rendering;

namespace AeroFlow.Visualization
{
    public class InternalSurfacePressureVisualizer : MonoBehaviour
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

        private MeshFilter meshFilter;
        private Renderer targetRenderer;
        private Mesh workingMesh;
        private Vector3[] vertices;
        private Vector3[] normals;
        private Color[] colors;
        private readonly System.Collections.Generic.List<Vector4> scalarChannelData = new System.Collections.Generic.List<Vector4>(1024);
        private Material[] originalMaterials;
        private Material pressureMaterial;
        private PipeFlowSimulation3D pipeSimulation;
        private AeroFlow.Sim3D.RotatingMachinery.RotatingMachinerySimulation3D machinerySimulation;
        private bool subscribed;
        private bool pressureMaterialApplied;

        public Gradient pressureGradient;
        public Gradient frictionGradient;
        public SurfaceMode surfaceMode = SurfaceMode.Pressure;

        public void SetSurfaceMode(SurfaceMode mode)
        {
            surfaceMode = mode;
            if (isActiveAndEnabled)
            {
                UpdateSurfaceColors();
            }
        }

        private void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            targetRenderer = GetComponent<Renderer>();
            pipeSimulation = FindAnyObjectByType<PipeFlowSimulation3D>();
            machinerySimulation = FindAnyObjectByType<AeroFlow.Sim3D.RotatingMachinery.RotatingMachinerySimulation3D>();
            EnsureGradients();
            CacheMesh();
        }

        private void OnEnable()
        {
            ResolveSimulation();
            Subscribe();
            UpdateSurfaceColors();
        }

        private void OnDisable()
        {
            Unsubscribe();
            RestoreOriginalMaterials();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            RestoreOriginalMaterials();
        }

        private void ResolveSimulation()
        {
            if (pipeSimulation == null)
            {
                pipeSimulation = FindAnyObjectByType<PipeFlowSimulation3D>();
            }
            if (machinerySimulation == null)
            {
                machinerySimulation = FindAnyObjectByType<AeroFlow.Sim3D.RotatingMachinery.RotatingMachinerySimulation3D>();
            }
        }

        private void Subscribe()
        {
            if (subscribed)
                return;

            if (pipeSimulation != null)
            {
                pipeSimulation.OnSimulationCompleted += UpdateSurfaceColors;
                subscribed = true;
                return;
            }

            if (machinerySimulation != null)
            {
                machinerySimulation.OnSimulationCompleted += UpdateSurfaceColors;
                subscribed = true;
            }
        }

        private void Unsubscribe()
        {
            if (!subscribed)
                return;

            if (pipeSimulation != null)
            {
                pipeSimulation.OnSimulationCompleted -= UpdateSurfaceColors;
            }
            if (machinerySimulation != null)
            {
                machinerySimulation.OnSimulationCompleted -= UpdateSurfaceColors;
            }
            subscribed = false;
        }

        private void EnsureGradients()
        {
            if (pressureGradient == null)
            {
                pressureGradient = new Gradient();
                pressureGradient.mode = GradientMode.Blend;
                pressureGradient.SetKeys(
                    new[]
                    {
                        new GradientColorKey(new Color(0.04f, 0.10f, 0.72f), 0.00f),
                        new GradientColorKey(new Color(0.22f, 0.58f, 0.98f), 0.24f),
                        new GradientColorKey(new Color(0.88f, 0.94f, 0.98f), 0.50f),
                        new GradientColorKey(new Color(0.98f, 0.56f, 0.28f), 0.76f),
                        new GradientColorKey(new Color(0.82f, 0.12f, 0.08f), 1.00f)
                    },
                    new[]
                    {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(1f, 1f)
                    });
            }

            if (frictionGradient == null)
            {
                frictionGradient = new Gradient();
                frictionGradient.mode = GradientMode.Blend;
                frictionGradient.SetKeys(
                    new[]
                    {
                        new GradientColorKey(new Color(0.18f, 0.00f, 0.36f), 0.00f),
                        new GradientColorKey(new Color(0.36f, 0.05f, 0.68f), 0.18f),
                        new GradientColorKey(new Color(0.06f, 0.82f, 0.92f), 0.46f),
                        new GradientColorKey(new Color(0.12f, 0.92f, 0.42f), 0.68f),
                        new GradientColorKey(new Color(0.96f, 0.96f, 0.12f), 0.86f),
                        new GradientColorKey(new Color(1.00f, 0.58f, 0.10f), 1.00f)
                    },
                    new[]
                    {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(1f, 1f)
                    });
            }
        }

        private void CacheMesh()
        {
            if (meshFilter == null)
                return;

            workingMesh = meshFilter.mesh;
            if (workingMesh == null)
                return;

            workingMesh.MarkDynamic();
            vertices = workingMesh.vertices;
            normals = workingMesh.normals;
            if (normals == null || normals.Length != vertices.Length)
            {
                TryRecalculateNormals(workingMesh);
                normals = workingMesh.normals;
            }

            if (normals == null || normals.Length != vertices.Length)
            {
                normals = new Vector3[vertices.Length];
                for (int i = 0; i < normals.Length; i++)
                {
                    normals[i] = Vector3.up;
                }
            }

            colors = new Color[vertices.Length];
            EnsureScalarChannelCapacity();
        }

        private void ApplyPressureMaterial()
        {
            if (targetRenderer == null)
                return;

            originalMaterials ??= targetRenderer.sharedMaterials;
            if (pressureMaterial == null)
            {
                Shader shader = RuntimeShaderResolver.FindFirstSupported(
                    "AeroFlow/VertexColorLitURP",
                    "AeroFlow/VertexColorURP",
                    "AeroFlow/VertexColorBuiltin",
                    "AeroFlow/SectionableURP",
                    "AeroFlow/SectionableBuiltin",
                    "Universal Render Pipeline/Lit",
                    "Standard");
                if (shader != null)
                {
                    pressureMaterial = new Material(shader) { name = "InternalSurfaceHeatmap" };
                }
            }

            if (pressureMaterial == null)
                return;

            pressureMaterial.SetFloat(UsePressureMapId, 1f);

            var materials = targetRenderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
            {
                materials = new Material[1];
            }

            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = pressureMaterial;
            }

            targetRenderer.sharedMaterials = materials;
            pressureMaterialApplied = true;
        }

        private void RestoreOriginalMaterials()
        {
            if (!pressureMaterialApplied || targetRenderer == null || originalMaterials == null || originalMaterials.Length == 0)
                return;

            targetRenderer.sharedMaterials = originalMaterials;
            pressureMaterialApplied = false;
        }

        public void UpdateSurfaceColors()
        {
            ResolveSimulation();
            if (!isActiveAndEnabled || meshFilter == null || targetRenderer == null)
                return;
            bool usingPipe = pipeSimulation != null && pipeSimulation.isActiveAndEnabled;
            bool usingMachinery = machinerySimulation != null && machinerySimulation.isActiveAndEnabled;
            if (!usingPipe && !usingMachinery)
            {
                RestoreOriginalMaterials();
                return;
            }
            if (usingPipe && !usingMachinery && !pipeSimulation.HasValidBoundaryAssignments())
            {
                RestoreOriginalMaterials();
                return;
            }
            if (usingPipe && !usingMachinery && !pipeSimulation.TryGetDiagnostics(out _))
            {
                RestoreOriginalMaterials();
                return;
            }

            CacheMesh();
            if (workingMesh == null || vertices == null || vertices.Length == 0)
            {
                RestoreOriginalMaterials();
                return;
            }

            EnsureScalarChannelCapacity();

            float minValue = 0f;
            float maxValue = 0f;
            bool hasValue = false;

            Matrix4x4 localToWorld = transform.localToWorldMatrix;
            Matrix4x4 normalMatrix = localToWorld.inverse.transpose;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 worldPosition = localToWorld.MultiplyPoint3x4(vertices[i]);
                Vector3 worldNormal = normalMatrix.MultiplyVector(normals[i]).normalized;
                bool ok;
                float value;
                if (surfaceMode == SurfaceMode.Friction)
                {
                    ok = TrySampleSmoothedFriction(worldPosition, worldNormal, out value);
                }
                else
                {
                    ok = TrySampleSurfacePressure(worldPosition, worldNormal, out value);
                }

                if (!ok || !float.IsFinite(value))
                {
                    scalarChannelData[i] = new Vector4(float.NaN, 0f, 0f, 0f);
                    continue;
                }

                if (!hasValue)
                {
                    minValue = value;
                    maxValue = value;
                    hasValue = true;
                }
                else
                {
                    if (value < minValue) minValue = value;
                    if (value > maxValue) maxValue = value;
                }

                scalarChannelData[i] = new Vector4(value, 0f, 0f, 0f);
            }

            if (!hasValue)
            {
                RestoreOriginalMaterials();
                return;
            }

            ApplyPressureMaterial();

            if (surfaceMode == SurfaceMode.Pressure && TryGetPressureRange(out float globalMinPressure, out float globalMaxPressure))
            {
                float symmetric = Mathf.Max(Mathf.Abs(globalMinPressure), Mathf.Abs(globalMaxPressure));
                if (symmetric > 1e-6f)
                {
                    minValue = -symmetric;
                    maxValue = symmetric;
                }
            }
            else if (surfaceMode == SurfaceMode.Friction && TryGetWallShearRange(out float globalMinShear, out float globalMaxShear))
            {
                minValue = Mathf.Min(minValue, globalMinShear);
                maxValue = Mathf.Max(maxValue, globalMaxShear);

                // Friction fields tend to collapse into the bottom end of the range.
                // Lift the floor so visible variation reaches the mid/high colors.
                float shearRange = maxValue - minValue;
                if (shearRange > 1e-6f)
                {
                    minValue = Mathf.Lerp(minValue, maxValue, 0.10f);
                }
            }

            if (Mathf.Approximately(minValue, maxValue))
            {
                maxValue = minValue + 0.001f;
            }

            Gradient gradient = surfaceMode == SurfaceMode.Friction ? frictionGradient : pressureGradient;
            float range = Mathf.Max(maxValue - minValue, 0.001f);
            for (int i = 0; i < colors.Length; i++)
            {
                float raw = scalarChannelData[i].x;
                if (!float.IsFinite(raw))
                {
                    colors[i] = new Color(0.62f, 0.68f, 0.72f, 1f);
                    continue;
                }

                float t = Mathf.Clamp01((raw - minValue) / range);
                if (surfaceMode == SurfaceMode.Friction)
                {
                    t = Mathf.Pow(t, 0.52f);
                    t = Mathf.Clamp01(t * 1.18f);
                }
                colors[i] = gradient != null ? gradient.Evaluate(t) : EvaluateTurbo(t);
            }

            workingMesh.colors = colors;
            workingMesh.SetUVs(ScalarUvChannel, scalarChannelData);
            RegisterGlobalRange(minValue, maxValue);
            if (pressureMaterial != null)
            {
                pressureMaterial.SetFloat(UsePressureMapId, 1f);
            }
        }

        private void EnsureScalarChannelCapacity()
        {
            if (vertices == null)
                return;

            int targetCount = vertices.Length;
            if (scalarChannelData.Count == targetCount)
                return;

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

        private bool TrySampleSmoothedFriction(Vector3 worldPosition, Vector3 worldNormal, out float wallShear)
        {
            wallShear = 0f;
            if (pipeSimulation == null && machinerySimulation == null)
                return false;

            Vector3 normal = worldNormal.sqrMagnitude > 1e-8f ? worldNormal.normalized : Vector3.up;
            Vector3 tangentA = Vector3.Cross(normal, Vector3.up);
            if (tangentA.sqrMagnitude < 1e-8f)
            {
                tangentA = Vector3.Cross(normal, Vector3.right);
            }
            tangentA.Normalize();
            Vector3 tangentB = Vector3.Cross(normal, tangentA).normalized;

            float offset = EstimateSurfaceSampleOffset();
            Vector3[] sampleOffsets =
            {
                Vector3.zero,
                tangentA * offset,
                -tangentA * offset,
                tangentB * offset,
                -tangentB * offset,
                (tangentA + tangentB) * (offset * 0.7f),
                (tangentA - tangentB) * (offset * 0.7f),
                (-tangentA + tangentB) * (offset * 0.7f),
                (-tangentA - tangentB) * (offset * 0.7f)
            };

            float weightedSum = 0f;
            float totalWeight = 0f;
            float peakShear = 0f;
            bool found = false;

            for (int i = 0; i < sampleOffsets.Length; i++)
            {
                Vector3 samplePosition = worldPosition + sampleOffsets[i];
                if (!TrySampleSurfaceFriction(samplePosition, normal, out float sampledShear) || !float.IsFinite(sampledShear))
                    continue;

                float distance = sampleOffsets[i].magnitude;
                float weight = i == 0 ? 1.35f : 1f / (1f + distance / Mathf.Max(offset, 1e-4f));
                weightedSum += sampledShear * weight;
                totalWeight += weight;
                peakShear = Mathf.Max(peakShear, sampledShear);
                found = true;
            }

            if (!found || totalWeight <= 1e-6f)
                return false;

            float averagedShear = weightedSum / totalWeight;
            wallShear = Mathf.Lerp(averagedShear, peakShear, 0.28f);
            return float.IsFinite(wallShear);
        }

        private float EstimateSurfaceSampleOffset()
        {
            if (pipeSimulation != null &&
                pipeSimulation.TryGetVelocityFieldSnapshot(out _, out _, out Vector3 cellSize, out _, out _, out _))
            {
                float minCell = Mathf.Min(cellSize.x, Mathf.Min(cellSize.y, cellSize.z));
                return Mathf.Max(minCell * 0.45f, 0.0025f);
            }

            if (machinerySimulation != null &&
                machinerySimulation.TryGetVelocityFieldSnapshot(out _, out _, out Vector3 machineryCellSize, out _, out _, out _))
            {
                float minCell = Mathf.Min(machineryCellSize.x, Mathf.Min(machineryCellSize.y, machineryCellSize.z));
                return Mathf.Max(minCell * 0.45f, 0.0025f);
            }

            if (targetRenderer != null)
            {
                Bounds bounds = targetRenderer.bounds;
                float size = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                return Mathf.Max(size * 0.002f, 0.0025f);
            }

            return 0.005f;
        }

        private bool TrySampleSurfacePressure(Vector3 worldPosition, Vector3 worldNormal, out float pressure)
        {
            pressure = 0f;
            if (pipeSimulation != null && pipeSimulation.TrySampleSurfaceScalars(worldPosition, worldNormal, out float pipePressure, out _))
            {
                pressure = pipePressure;
                return true;
            }

            if (machinerySimulation != null && machinerySimulation.TrySampleSurfaceScalars(worldPosition, worldNormal, out float machineryPressure, out _))
            {
                pressure = machineryPressure;
                return true;
            }

            return false;
        }

        private bool TrySampleSurfaceFriction(Vector3 worldPosition, Vector3 worldNormal, out float wallShear)
        {
            wallShear = 0f;
            if (pipeSimulation != null && pipeSimulation.TrySampleSurfaceScalars(worldPosition, worldNormal, out _, out float pipeShear))
            {
                wallShear = pipeShear;
                return true;
            }

            if (machinerySimulation != null && machinerySimulation.TrySampleSurfaceScalars(worldPosition, worldNormal, out _, out float machineryShear))
            {
                wallShear = machineryShear;
                return true;
            }

            return false;
        }

        private bool TryGetPressureRange(out float minPressure, out float maxPressure)
        {
            minPressure = 0f;
            maxPressure = 0f;
            if (pipeSimulation != null && pipeSimulation.TryGetPressureRange(out minPressure, out maxPressure))
            {
                return true;
            }

            if (machinerySimulation != null && machinerySimulation.TryGetPressureRange(out minPressure, out maxPressure))
            {
                return true;
            }

            return false;
        }

        private bool TryGetWallShearRange(out float minWallShear, out float maxWallShear)
        {
            minWallShear = 0f;
            maxWallShear = 0f;
            if (pipeSimulation != null && pipeSimulation.TryGetWallShearRange(out minWallShear, out maxWallShear))
            {
                return true;
            }

            if (machinerySimulation != null && machinerySimulation.TryGetWallShearRange(out minWallShear, out maxWallShear))
            {
                return true;
            }

            return false;
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

        private static void TryRecalculateNormals(Mesh mesh)
        {
            if (mesh == null)
                return;

            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                if (mesh.GetTopology(i) == MeshTopology.Triangles)
                {
                    mesh.RecalculateNormals();
                    return;
                }
            }
        }
    }
}
