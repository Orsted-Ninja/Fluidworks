using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace AeroFlow.Visualization
{
    [RequireComponent(typeof(WindTunnelSimulation3D))]
    public class WindTunnelStreamlineRenderer : MonoBehaviour
    {
        [Range(1, 512)] public int segmentsPerLine = 60;
        [Range(0.01f, 0.5f)] public float integrationStep = 0.04f;
        public float lineWidth = 0.02f;
        public Material lineMaterial;
        public Gradient colorGradient;

        readonly List<LineRenderer> lineRenderers = new();
        readonly List<Vector3[]> lineBuffers = new();
        WindTunnelSimulation3D simulation;
        NavierStokesGridSolver solver;
        StreamlineFieldRenderer modernRenderer;

        public void Initialize(WindTunnelSimulation3D sim)
        {
            simulation = sim;
            solver = sim?.navierStokesSolver;
            modernRenderer = sim != null ? sim.GetComponent<StreamlineFieldRenderer>() : GetComponent<StreamlineFieldRenderer>();
        }

        void Awake()
        {
            if (simulation == null) simulation = GetComponent<WindTunnelSimulation3D>();
            if (solver == null) solver = simulation?.navierStokesSolver;
            if (modernRenderer == null) modernRenderer = GetComponent<StreamlineFieldRenderer>();
            if (lineMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Additive");
                if (shader != null) lineMaterial = new Material(shader);
            }
            if (colorGradient == null)
            {
                colorGradient = new Gradient();
                colorGradient.SetKeys(
                    new[] { new GradientColorKey(new Color(0.1f, 0.75f, 1f), 0f), new GradientColorKey(new Color(0.1f, 0.25f, 0.8f), 1f) },
                    new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.4f, 1f) }
                );
            }

            DisableIfModernRendererPresent();
        }

        void OnEnable()
        {
            DisableIfModernRendererPresent();
        }

        void OnDisable()
        {
            ClearLines();
            SetLinesActive(false);
        }

        void OnDestroy()
        {
            ClearLines();
            foreach (var lr in lineRenderers)
            {
                if (lr != null && lr.gameObject != null)
                {
                    Destroy(lr.gameObject);
                }
            }
            lineRenderers.Clear();
            lineBuffers.Clear();
        }

        public void ApplyVisualizationMode(string mode)
        {
            if (!string.Equals(
                    WindTunnelSimulation3D.NormalizeVisualizationMode(mode),
                    WindTunnelSimulation3D.VisualizationStreamlines,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                ClearLines();
                SetLinesActive(false);
            }
        }


        void Update()
        {
            if (DisableIfModernRendererPresent())
            {
                return;
            }

            if (simulation == null)
                simulation = GetComponent<WindTunnelSimulation3D>();

            if (solver == null && simulation != null)
                solver = simulation.navierStokesSolver;

            if (simulation == null || solver == null) return;

            if (simulation.settings.visualizationMode != "Streamlines")
            {
                SetLinesActive(false);
                return;
            }

            SetLinesActive(true);

            if (!solver.TryGetVelocityFieldSnapshot(out var velocities,
                                                    out var origin,
                                                    out var cellSize,
                                                    out int sizeX,
                                                    out int sizeY,
                                                    out int sizeZ,
                                                    out _))
                return;

            Vector3 boundsCenter = solver.BoundsCenter;
            Vector3 boundsSize = solver.BoundsSize;

            int density = Mathf.Clamp(simulation.settings.streamlineDensity, 8, 256);

            EnsureRenderers(density);

            GenerateStreamlines(
                density,
                velocities,
                origin,
                cellSize,
                sizeX,
                sizeY,
                sizeZ,
                boundsCenter,
                boundsSize
            );
        }

        bool DisableIfModernRendererPresent()
        {
            if (modernRenderer == null)
                modernRenderer = GetComponent<StreamlineFieldRenderer>();

            if (modernRenderer == null)
                return false;

            ClearLines();
            SetLinesActive(false);
            enabled = false;
            return true;
        }

            void EnsureRenderers(int count)
                {
                    while (lineRenderers.Count < count)
                    {
                        GameObject go = new GameObject($"Streamline_{lineRenderers.Count}");
                        go.transform.SetParent(transform, false);

                        var lr = go.AddComponent<LineRenderer>();
                        ConfigureLineRenderer(lr);

                        lineRenderers.Add(lr);
                        lineBuffers.Add(new Vector3[segmentsPerLine]);
                    }

                    for (int i = 0; i < lineRenderers.Count; i++)
                    {
                        lineRenderers[i].gameObject.SetActive(i < count);
                    }
                }
        void SetLinesActive(bool active)
        {
            foreach (var lr in lineRenderers)
            {
                if (lr != null)
                {
                    lr.enabled = active;
                }
            }
        }

        void ClearLines()
        {
            foreach (var lr in lineRenderers)
            {
                if (lr == null) continue;
                lr.positionCount = 0;
                lr.enabled = false;
            }
        }

        void ConfigureLineRenderer(LineRenderer lr)
        {
            lr.widthMultiplier = lineWidth;
            lr.material = lineMaterial;
            lr.useWorldSpace = true;
            lr.colorGradient = colorGradient;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
        }

        void GenerateStreamlines(int count, Vector3[] velocities, Vector3 origin, Vector3 cellSize, int sizeX, int sizeY, int sizeZ, Vector3 boundsCenter, Vector3 boundsSize)
        {
            int rows = Mathf.CeilToInt(Mathf.Sqrt(count));
            int cols = Mathf.CeilToInt((float)count / rows);
            float ySpacing = rows > 1 ? boundsSize.y / (rows - 1) : 0f;
            float zSpacing = cols > 1 ? boundsSize.z / (cols - 1) : 0f;
            Vector3 inlet = boundsCenter - new Vector3(boundsSize.x * 0.5f, 0f, 0f) + new Vector3(0.01f, 0f, 0f);
            int index = 0;
            for (int row = 0; row < rows && index < count; row++)
            {
                for (int col = 0; col < cols && index < count; col++)
                {
                    Vector3 offset = new Vector3(0f, -boundsSize.y * 0.5f + row * ySpacing, -boundsSize.z * 0.5f + col * zSpacing);
                    Vector3 start = inlet + offset;
                    var buffer = lineBuffers[index];
                    int generated = IntegrateStreamline(start, velocities, origin, cellSize, sizeX, sizeY, sizeZ, buffer);
                    var lr = lineRenderers[index];
                    lr.positionCount = Mathf.Max(1, generated);
                    for (int i = 0; i < generated; i++)
                    {
                        lr.SetPosition(i, buffer[i]);
                    }
                    index++;
                }
            }
        }

        int IntegrateStreamline(Vector3 start, Vector3[] velocities, Vector3 origin, Vector3 cellSize, int sizeX, int sizeY, int sizeZ, Vector3[] buffer)
        {
            int count = 0;
            Vector3 pos = start;
            for (int step = 0; step < segmentsPerLine; step++)
            {
                if (solver != null && solver.IsObstacle(pos)) break;
                buffer[count++] = pos;
                if (!SampleVelocity(pos, velocities, origin, cellSize, sizeX, sizeY, sizeZ, out var velocity)) break;
                if (velocity.sqrMagnitude < 1e-4f) break;
                Vector3 next = pos + velocity * integrationStep;
                if (float.IsNaN(next.x) || float.IsInfinity(next.x)) break;
                if (solver != null && solver.IsObstacle(next)) break;
                pos = next;
            }
            return count;
        }

        bool SampleVelocity(Vector3 position, Vector3[] velocities, Vector3 origin, Vector3 cellSize, int sizeX, int sizeY, int sizeZ, out Vector3 velocity)
        {
            velocity = Vector3.zero;
            if (sizeX <= 0 || sizeY <= 0 || sizeZ <= 0) return false;
            Vector3 relative = new Vector3(
                (position.x - origin.x) / cellSize.x,
                (position.y - origin.y) / cellSize.y,
                (position.z - origin.z) / cellSize.z
            );
            int ix = Mathf.FloorToInt(relative.x);
            int iy = Mathf.FloorToInt(relative.y);
            int iz = Mathf.FloorToInt(relative.z);
            if (ix < 0 || ix >= sizeX || iy < 0 || iy >= sizeY || iz < 0 || iz >= sizeZ) return false;
            int idx = ix + sizeX * (iy + sizeY * iz);
            if (idx < 0 || idx >= velocities.Length) return false;
            velocity = velocities[idx];
            return true;
        }
    }
}
