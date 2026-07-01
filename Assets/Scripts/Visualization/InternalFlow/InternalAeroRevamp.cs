using UnityEngine;
using AeroFlow.Rendering;
using System.Collections.Generic;
using AeroFlow.Sim3D.PipeFlow;

namespace AeroFlow.Visualization
{
    public enum OpeningType { Inlet, Outlet }

    [System.Serializable]
    public class FlowOpening
    {
        public string id;
        public OpeningType type;
        public Vector3 position;
        public Vector3 normal;
        public float radius = 0.4f;
        public Color color = Color.cyan;

        [HideInInspector] public GameObject markerObj;
    }

    public class InternalAeroRevamp : MonoBehaviour
    {
        [Header("Flow Visualization")]
        public int streamlineCount = 40;
        public int pointsPerLine = 120;
        public float lineWidth = 0.02f;
        public float flowSpeed = 1.5f;
        public Material streamlineMaterial;

        [Header("Opening Markers")]
        public float markerScale = 1.0f;

        [Header("Diagnostics")]
        public List<FlowOpening> openings = new List<FlowOpening>();

        private List<LineRenderer> _lines = new List<LineRenderer>();
        private PipeFlowSimulation3D _sim;
        private Vector3[] _velocitySnapshot;
        private Vector3 _fieldOrigin;
        private Vector3 _fieldCellSize;
        private int _fieldSizeX, _fieldSizeY, _fieldSizeZ;
        private bool _hasFieldData;
        private float _lastFieldUpdateTime;

        private void Awake()
        {
            ClearLegacyObjects();
            _sim = FindAnyObjectByType<PipeFlowSimulation3D>();
        }

        private void ClearLegacyObjects()
        {
            var oldLines = GameObject.FindObjectsByType<LineRenderer>(FindObjectsSortMode.None);
            foreach (var lr in oldLines)
            {
                if (lr != null && lr.gameObject != null &&
                    (lr.name.StartsWith("InternalFlowLine_") || lr.name.StartsWith("Streamline_")))
                {
                    Destroy(lr.gameObject);
                }
            }

            var oldInlet = GameObject.Find("InternalFlow_Inlet");
            if (oldInlet != null) Destroy(oldInlet);
            var oldOutlet = GameObject.Find("InternalFlow_Outlet");
            if (oldOutlet != null) Destroy(oldOutlet);

            // Clear old opening markers
            foreach (var op in openings)
            {
                if (op != null && op.markerObj != null) Destroy(op.markerObj);
            }
        }

        private void Start()
        {
            EnsureLinePool();
        }

        private void OnEnable()
        {
            _sim = FindAnyObjectByType<PipeFlowSimulation3D>();
        }

        private void Update()
        {
            UpdateFieldSnapshot();
            SyncOpeningsFromSolver();
            UpdateFlowVisualization();
        }

        private void OnDisable()
        {
            foreach (var lr in _lines)
            {
                if (lr != null) lr.positionCount = 0;
            }
        }

        private void OnDestroy()
        {
            foreach (var lr in _lines)
            {
                if (lr != null && lr.gameObject != null)
                    Destroy(lr.gameObject);
            }
            _lines.Clear();

            foreach (var op in openings)
            {
                if (op != null && op.markerObj != null)
                    Destroy(op.markerObj);
            }
            openings.Clear();
        }

        /// <summary>
        /// Fetch velocity field from the solver periodically (not every frame for perf).
        /// </summary>
        private void UpdateFieldSnapshot()
        {
            if (_sim == null || !_sim.isActiveAndEnabled) { _hasFieldData = false; return; }
            if (Time.time - _lastFieldUpdateTime < 0.1f && _hasFieldData) return;

            _hasFieldData = _sim.TryGetVelocityFieldSnapshot(
                out _velocitySnapshot,
                out _fieldOrigin,
                out _fieldCellSize,
                out _fieldSizeX, out _fieldSizeY, out _fieldSizeZ);
            _lastFieldUpdateTime = Time.time;
        }

        /// <summary>
        /// Sync openings from the solver's auto-detected openings.
        /// </summary>
        private void SyncOpeningsFromSolver()
        {
            if (_sim == null) return;
            var boundaryManager = _sim.boundaryManager;
            if (boundaryManager == null) boundaryManager = FindAnyObjectByType<BoundaryConditionManager>();

            var detected = _sim.GetDetectedOpenings();
            if (detected == null || detected.Count == 0) return;

            // Always rebuild if count doesn't match or assignments changed
            bool needsRebuild = detected.Count != openings.Count;
            if (boundaryManager != null && boundaryManager.Assignments.Count != openings.Count) needsRebuild = true;

            if (!needsRebuild) return;

            // Clear old markers
            foreach (var op in openings)
            {
                if (op != null && op.markerObj != null) Destroy(op.markerObj);
            }
            openings.Clear();

            for (int i = 0; i < detected.Count; i++)
            {
                var d = detected[i];
                var type = OpeningType.Outlet; // Default
                Color col = new Color(0.1f, 1.0f, 0.4f); // green = outlet

                if (boundaryManager != null && i < boundaryManager.Assignments.Count)
                {
                    var assignment = boundaryManager.Assignments[i];
                    if (assignment.type == Sim3D.PipeFlow.BoundaryType.Inlet)
                    {
                        type = OpeningType.Inlet;
                        col = new Color(0.1f, 0.6f, 1.0f); // blue = inlet
                    }
                }
                else if (i == 0) // Fallback for first opening
                {
                    type = OpeningType.Inlet;
                    col = new Color(0.1f, 0.6f, 1.0f);
                }

                var op = new FlowOpening
                {
                    id = "o-" + d.id,
                    type = type,
                    position = d.position,
                    normal = d.normal,
                    radius = d.radius,
                    color = col
                };
                openings.Add(op);
            }

            RefreshOpeningMarkers();
        }

        private void EnsureLinePool()
        {
            if (streamlineMaterial == null)
            {
                Shader s = Shader.Find("AeroFlow/VertexColorURP");
                if (s == null)
                {
                    Debug.LogError("[InternalAero] Streamline shader not found.");
                    streamlineMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
                }
                else
                {
                    streamlineMaterial = new Material(s);
                }
            }

            while (_lines.Count < streamlineCount)
            {
                GameObject go = new GameObject("PipeStreamline_" + _lines.Count);
                go.transform.SetParent(this.transform);
                var lr = go.AddComponent<LineRenderer>();
                lr.startWidth = lr.endWidth = lineWidth;
                lr.material = streamlineMaterial;
                lr.positionCount = 0;
                lr.useWorldSpace = true;
                _lines.Add(lr);
            }
        }

        /// <summary>
        /// Draws streamlines through the pipe by integrating the solver's velocity field.
        /// Seeds lines at detected openings and integrates forward through fluid cells.
        /// </summary>
        private void UpdateFlowVisualization()
        {
            if (_sim != null && !string.Equals(PipeFlowSimulation3D.NormalizeVisualizationMode(_sim.settings.visualizationMode), PipeFlowSimulation3D.VisualizationStreamlines, System.StringComparison.OrdinalIgnoreCase))
            {
                foreach (var l in _lines)
                    if (l != null) l.positionCount = 0;
                return;
            }

            if (_sim == null || !_sim.HasValidBoundaryAssignments())
            {
                foreach (var l in _lines) if (l != null) l.positionCount = 0;
                return;
            }

            if (!_hasFieldData || _velocitySnapshot == null || openings.Count == 0)
            {
                foreach (var l in _lines) if (l != null) l.positionCount = 0;
                return;
            }

            EnsureLinePool();

            // Seed streamlines from inlet opening(s)
            var inlets = new List<FlowOpening>();
            foreach (var op in openings)
                if (op.type == OpeningType.Inlet) inlets.Add(op);
            if (inlets.Count == 0)
            {
                foreach (var l in _lines) if (l != null) l.positionCount = 0;
                return;
            }

            for (int i = 0; i < streamlineCount; i++)
            {
                if (i >= _lines.Count) break;
                var lr = _lines[i];
                if (lr == null) continue;

                // Pick an inlet to seed from
                var inlet = inlets[i % inlets.Count];

                // Seed position: jitter around the inlet center within radius
                float angle = (i * 137.5f + Time.time * flowSpeed * 20f) * Mathf.Deg2Rad;
                float rFrac = Mathf.Sqrt((float)(i + 1) / streamlineCount) * 0.85f;
                Vector3 right = Vector3.Cross(inlet.normal, Vector3.up).normalized;
                if (right.sqrMagnitude < 0.01f)
                    right = Vector3.Cross(inlet.normal, Vector3.forward).normalized;
                Vector3 up = Vector3.Cross(right, inlet.normal).normalized;
                Vector3 seedOffset = (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * inlet.radius * rFrac;
                Vector3 seedPos = inlet.position + seedOffset;

                // Integrate the streamline through the velocity field
                List<Vector3> points = new List<Vector3>(pointsPerLine);
                Vector3 pos = seedPos;
                float stepLen = Mathf.Max(_fieldCellSize.magnitude * 0.5f, 0.02f);
                float maxTravel = 50f; // world units
                float traveled = 0f;

                for (int p = 0; p < pointsPerLine; p++)
                {
                    points.Add(pos);

                    Vector3 vel = SampleVelocity(pos);
                    float speed = vel.magnitude;
                    if (speed < 0.001f) break;
                    if (traveled > maxTravel) break;

                    Vector3 dir = vel / speed;
                    pos += dir * stepLen;
                    traveled += stepLen;

                    // Check if we've left the fluid domain
                    if (!IsInFluidDomain(pos)) break;
                }

                lr.positionCount = points.Count;
                lr.SetPositions(points.ToArray());

                // Color by speed (blue→cyan→green→yellow→red)
                float maxV = Mathf.Max(_sim.settings.inletVelocity * 1.5f, 1f);
                Gradient grad = MakeVelocityGradient();
                lr.colorGradient = grad;
                lr.startWidth = lineWidth;
                lr.endWidth = lineWidth * 0.4f;
            }
        }

        private Vector3 SampleVelocity(Vector3 worldPos)
        {
            if (_velocitySnapshot == null || !_hasFieldData) return Vector3.zero;

            Vector3 uv = new Vector3(
                (worldPos.x - _fieldOrigin.x) / Mathf.Max(_fieldCellSize.x * _fieldSizeX, 1e-3f),
                (worldPos.y - _fieldOrigin.y) / Mathf.Max(_fieldCellSize.y * _fieldSizeY, 1e-3f),
                (worldPos.z - _fieldOrigin.z) / Mathf.Max(_fieldCellSize.z * _fieldSizeZ, 1e-3f));

            uv.x = Mathf.Clamp01(uv.x);
            uv.y = Mathf.Clamp01(uv.y);
            uv.z = Mathf.Clamp01(uv.z);

            float fx = uv.x * (_fieldSizeX - 1);
            float fy = uv.y * (_fieldSizeY - 1);
            float fz = uv.z * (_fieldSizeZ - 1);

            int x0 = Mathf.FloorToInt(fx); int y0 = Mathf.FloorToInt(fy); int z0 = Mathf.FloorToInt(fz);
            int x1 = Mathf.Min(x0 + 1, _fieldSizeX - 1);
            int y1 = Mathf.Min(y0 + 1, _fieldSizeY - 1);
            int z1 = Mathf.Min(z0 + 1, _fieldSizeZ - 1);
            x0 = Mathf.Clamp(x0, 0, _fieldSizeX - 1);
            y0 = Mathf.Clamp(y0, 0, _fieldSizeY - 1);
            z0 = Mathf.Clamp(z0, 0, _fieldSizeZ - 1);

            float tx = fx - x0; float ty = fy - y0; float tz = fz - z0;

            int Flat(int x, int y, int z) => x + _fieldSizeX * (y + _fieldSizeY * z);

            Vector3 v000 = _velocitySnapshot[Flat(x0, y0, z0)];
            Vector3 v100 = _velocitySnapshot[Flat(x1, y0, z0)];
            Vector3 v010 = _velocitySnapshot[Flat(x0, y1, z0)];
            Vector3 v110 = _velocitySnapshot[Flat(x1, y1, z0)];
            Vector3 v001 = _velocitySnapshot[Flat(x0, y0, z1)];
            Vector3 v101 = _velocitySnapshot[Flat(x1, y0, z1)];
            Vector3 v011 = _velocitySnapshot[Flat(x0, y1, z1)];
            Vector3 v111 = _velocitySnapshot[Flat(x1, y1, z1)];

            Vector3 va = Vector3.Lerp(Vector3.Lerp(v000, v100, tx), Vector3.Lerp(v010, v110, tx), ty);
            Vector3 vb = Vector3.Lerp(Vector3.Lerp(v001, v101, tx), Vector3.Lerp(v011, v111, tx), ty);
            return Vector3.Lerp(va, vb, tz);
        }

        private bool IsInFluidDomain(Vector3 worldPos)
        {
            if (_sim == null) return false;
            var mask = _sim.GetObstacleMask();
            if (mask == null) return true;

            Vector3 uv = new Vector3(
                (worldPos.x - _fieldOrigin.x) / Mathf.Max(_fieldCellSize.x * _fieldSizeX, 1e-3f),
                (worldPos.y - _fieldOrigin.y) / Mathf.Max(_fieldCellSize.y * _fieldSizeY, 1e-3f),
                (worldPos.z - _fieldOrigin.z) / Mathf.Max(_fieldCellSize.z * _fieldSizeZ, 1e-3f));

            int x = Mathf.Clamp(Mathf.FloorToInt(uv.x * _fieldSizeX), 0, _fieldSizeX - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(uv.y * _fieldSizeY), 0, _fieldSizeY - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt(uv.z * _fieldSizeZ), 0, _fieldSizeZ - 1);
            int idx = x + _fieldSizeX * (y + _fieldSizeY * z);

            return idx >= 0 && idx < mask.Length && mask[idx] == 0;
        }

        private Gradient MakeVelocityGradient()
        {
            Gradient g = new Gradient();
            g.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(new Color(0.05f, 0.10f, 0.80f), 0.0f),  // deep blue
                    new GradientColorKey(new Color(0.05f, 0.70f, 0.85f), 0.2f),  // cyan
                    new GradientColorKey(new Color(0.10f, 0.85f, 0.25f), 0.4f),  // green
                    new GradientColorKey(new Color(0.95f, 0.90f, 0.10f), 0.6f),  // yellow
                    new GradientColorKey(new Color(0.95f, 0.50f, 0.05f), 0.8f),  // orange
                    new GradientColorKey(new Color(0.90f, 0.10f, 0.05f), 1.0f),  // red
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0.9f, 0.0f),
                    new GradientAlphaKey(1.0f, 0.3f),
                    new GradientAlphaKey(0.8f, 0.7f),
                    new GradientAlphaKey(0.3f, 1.0f),
                });
            return g;
        }

        public void RefreshOpeningMarkers()
        {
            foreach (var op in openings)
            {
                if (op.markerObj != null)
                {
                    Destroy(op.markerObj);
                    op.markerObj = null;
                }
            }
        }
    }
}
