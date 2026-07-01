using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using AeroFlow.Core;

namespace AeroFlow.Sim3D.PipeFlow
{
    public enum BoundaryType { Unassigned, Inlet, Outlet }

    /// <summary>
    /// Assignment mode that is entered when user clicks "Assign Inlet" or "Assign Outlet".
    /// While active the cursor changes and the next cap click assigns that opening.
    /// </summary>
    public enum AssignmentMode { None, PickingInlet, PickingOutlet }

    [System.Serializable]
    public class BoundaryAssignment
    {
        public int openingIndex;
        public BoundaryType type = BoundaryType.Unassigned;
        public DetectedOpening opening;
        [System.NonSerialized] public GameObject capObject;
    }

    public class BoundaryConditionManager : MonoBehaviour
    {
        public event System.Action OnAssignmentsChanged;
        public event System.Action<AssignmentMode> OnAssignmentModeChanged;

        [Header("Cap Colors")]
        public Color unassignedColor  = new Color(0.25f, 0.55f, 1.0f, 0.32f);    // blue
        public Color inletColor       = new Color(0.15f, 0.90f, 0.35f, 0.40f);    // green
        public Color outletColor      = new Color(1.00f, 0.22f, 0.18f, 0.40f);    // red

        public Color unassignedRing   = new Color(0.35f, 0.65f, 1.0f, 0.65f);
        public Color inletRing        = new Color(0.20f, 1.0f, 0.45f, 0.75f);
        public Color outletRing       = new Color(1.00f, 0.35f, 0.25f, 0.75f);

        [Range(0.02f, 0.15f)] public float ringThickness = 0.04f;
        [Range(16, 64)] public int discSegments = 32;

        private List<BoundaryAssignment> assignments = new List<BoundaryAssignment>();
        private Material[] capMats = new Material[3];
        private Material[] ringMats = new Material[3];
        private bool matsCreated;

        // Assignment picking state
        private AssignmentMode currentMode = AssignmentMode.None;
        private Texture2D pickCursor;
        private Camera mainCam;

        public IReadOnlyList<BoundaryAssignment> Assignments => assignments;
        public int OpeningCount => assignments.Count;
        public AssignmentMode CurrentMode => currentMode;

        // ───── Detection ─────

        public void DetectOpenings()
        {
            ClearCaps();
            assignments.Clear();
            CancelPicking();

            var model = RuntimeModelLookup.GetLoadedModel();
            if (model == null)
            {
                Debug.LogWarning("[BoundaryConditionManager] No loaded model found.");
                OnAssignmentsChanged?.Invoke();
                return;
            }

            var openings = MeshOpeningDetector.DetectOpenings(model);
            for (int i = 0; i < openings.Count; i++)
            {
                assignments.Add(new BoundaryAssignment
                {
                    openingIndex = i,
                    type = BoundaryType.Unassigned,
                    opening = openings[i]
                });
            }

            Debug.Log($"[BoundaryConditionManager] {assignments.Count} openings detected.");
            RefreshCaps();
            OnAssignmentsChanged?.Invoke();
        }

        // ───── Assignment ─────

        public void SetBoundaryType(int index, BoundaryType type)
        {
            if (index < 0 || index >= assignments.Count) return;
            assignments[index].type = type;
            RefreshCaps();
            OnAssignmentsChanged?.Invoke();
        }

        public bool HasManualAssignments()
        {
            foreach (var a in assignments)
                if (a.type != BoundaryType.Unassigned) return true;
            return false;
        }

        public List<BoundaryAssignment> GetInlets()
        {
            var r = new List<BoundaryAssignment>();
            foreach (var a in assignments) if (a.type == BoundaryType.Inlet) r.Add(a);
            return r;
        }

        public List<BoundaryAssignment> GetOutlets()
        {
            var r = new List<BoundaryAssignment>();
            foreach (var a in assignments) if (a.type == BoundaryType.Outlet) r.Add(a);
            return r;
        }

        public bool HasAssignedInlet()
        {
            foreach (var a in assignments)
                if (a.type == BoundaryType.Inlet) return true;
            return false;
        }

        public bool HasAssignedOutlet()
        {
            foreach (var a in assignments)
                if (a.type == BoundaryType.Outlet) return true;
            return false;
        }

        public bool HasValidFlowAssignments()
        {
            return HasAssignedInlet() && HasAssignedOutlet();
        }

        // ───── Click-to-assign picking ─────

        /// <summary>Enter picking mode — cursor changes, next cap click assigns.</summary>
        public void BeginPicking(AssignmentMode mode)
        {
            currentMode = mode;
            EnsurePickCursor();
            Cursor.SetCursor(pickCursor, new Vector2(16, 16), CursorMode.Auto);
            OnAssignmentModeChanged?.Invoke(currentMode);
        }

        public void CancelPicking()
        {
            if (currentMode == AssignmentMode.None) return;
            currentMode = AssignmentMode.None;
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            OnAssignmentModeChanged?.Invoke(currentMode);
        }

        private void Update()
        {
            if (currentMode == AssignmentMode.None) return;

            // Escape key cancels picking
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CancelPicking();
                return;
            }

            // Right click cancels
            if (Input.GetMouseButtonDown(1))
            {
                CancelPicking();
                return;
            }

            // Left click = try to pick a cap
            if (Input.GetMouseButtonDown(0))
            {
                TryPickCap();
            }
        }

        void TryPickCap()
        {
            if (mainCam == null) mainCam = Camera.main;
            if (mainCam == null) return;

            Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            if (!UnityEngine.Physics.Raycast(ray, out hit, 500f)) return;

            // Find which assignment owns the clicked cap
            GameObject hitGo = hit.collider.gameObject;
            for (int i = 0; i < assignments.Count; i++)
            {
                var a = assignments[i];
                if (a.capObject == null) continue;

                // Cap is the root, collider is on the root
                if (hitGo == a.capObject || hitGo.transform.IsChildOf(a.capObject.transform))
                {
                    BoundaryType assignType = currentMode == AssignmentMode.PickingInlet
                        ? BoundaryType.Inlet
                        : BoundaryType.Outlet;

                    SetBoundaryType(i, assignType);
                    CancelPicking();
                    return;
                }
            }
        }

        // ───── Flash ─────

        public void FlashOpening(int index, float duration = 1.0f)
        {
            if (index < 0 || index >= assignments.Count) return;
            if (assignments[index].capObject == null) return;
            StartCoroutine(FlashCap(assignments[index].capObject, duration));
        }

        System.Collections.IEnumerator FlashCap(GameObject cap, float dur)
        {
            if (cap == null) yield break;
            var mr = cap.GetComponentInChildren<MeshRenderer>();
            if (mr == null) yield break;

            var mat = mr.material;
            Color orig = Color.white;
            if (mat.HasProperty("_BaseColor")) orig = mat.GetColor("_BaseColor");
            else if (mat.HasProperty("_Color")) orig = mat.GetColor("_Color");

            Color flash = new Color(1f, 1f, 1f, 0.75f);
            float t = 0f;
            while (t < dur)
            {
                float p = Mathf.PingPong(t * 5f, 1f);
                Color c = Color.Lerp(orig, flash, p);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                else if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
                t += Time.deltaTime;
                yield return null;
            }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", orig);
            else if (mat.HasProperty("_Color")) mat.SetColor("_Color", orig);
        }

        // ───── Cap mesh rendering ─────

        public void RefreshCaps()
        {
            ClearCaps();
            EnsureMaterials();

            for (int i = 0; i < assignments.Count; i++)
            {
                var a = assignments[i];
                if (a.opening == null) continue;
                int mi = (int)a.type;

                GameObject cap = CreateDiscMesh(
                    $"OpeningCap_{i}", a.opening.centroid, a.opening.normal,
                    a.opening.radius, capMats[mi]);
                cap.transform.SetParent(transform);

                var capRenderer = cap.GetComponent<MeshRenderer>();
                if (capRenderer != null)
                {
                    capRenderer.enabled = false;
                }

                // Add a collider so raycasts can pick it
                var mc = cap.AddComponent<MeshCollider>();
                mc.sharedMesh = cap.GetComponent<MeshFilter>().sharedMesh;
                mc.convex = true;

                a.capObject = cap;

                GameObject ring = CreateRingMesh(
                    $"OpeningRing_{i}", a.opening.centroid, a.opening.normal,
                    a.opening.radius, ringThickness, ringMats[mi]);
                ring.transform.SetParent(cap.transform);
            }
        }

        void EnsurePickCursor()
        {
            if (pickCursor != null) return;
            // Procedural crosshair cursor 32×32
            int s = 32;
            pickCursor = new Texture2D(s, s, TextureFormat.RGBA32, false);
            pickCursor.filterMode = FilterMode.Point;
            Color clear = Color.clear;
            Color line = new Color(0.3f, 0.7f, 1f, 1f);
            Color center = Color.white;

            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
                pickCursor.SetPixel(x, y, clear);

            int mid = s / 2;
            // Horizontal line
            for (int x = mid - 8; x <= mid + 8; x++)
            {
                if (x >= 0 && x < s)
                {
                    pickCursor.SetPixel(x, mid, line);
                    pickCursor.SetPixel(x, mid - 1, new Color(0, 0, 0, 0.3f));
                    pickCursor.SetPixel(x, mid + 1, new Color(0, 0, 0, 0.3f));
                }
            }
            // Vertical line
            for (int y = mid - 8; y <= mid + 8; y++)
            {
                if (y >= 0 && y < s)
                {
                    pickCursor.SetPixel(mid, y, line);
                    pickCursor.SetPixel(mid - 1, y, new Color(0, 0, 0, 0.3f));
                    pickCursor.SetPixel(mid + 1, y, new Color(0, 0, 0, 0.3f));
                }
            }
            // Center dot
            pickCursor.SetPixel(mid, mid, center);
            pickCursor.Apply();
        }

        GameObject CreateDiscMesh(string name, Vector3 center, Vector3 normal, float radius, Material mat)
        {
            int seg = Mathf.Max(16, discSegments);
            var mesh = new Mesh { name = name };
            var verts = new Vector3[seg + 1];
            var tris = new int[seg * 3];
            verts[0] = Vector3.zero;
            for (int i = 0; i < seg; i++)
            {
                float a = (float)i / seg * Mathf.PI * 2f;
                verts[i + 1] = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
                int nx = (i + 1) % seg;
                tris[i * 3] = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = nx + 1;
            }
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.position = center;
            go.transform.rotation = Quaternion.LookRotation(normal);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        GameObject CreateRingMesh(string name, Vector3 center, Vector3 normal, float radius, float thick, Material mat)
        {
            int seg = Mathf.Max(16, discSegments);
            var mesh = new Mesh { name = name };
            var verts = new Vector3[seg * 2];
            var tris = new int[seg * 6];
            float innerR = Mathf.Max(0f, radius - thick * 0.5f);
            float outerR = radius + thick * 0.5f;

            for (int i = 0; i < seg; i++)
            {
                float a = (float)i / seg * Mathf.PI * 2f;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                verts[i * 2] = new Vector3(c * innerR, s * innerR, 0f);
                verts[i * 2 + 1] = new Vector3(c * outerR, s * outerR, 0f);
                int nx = (i + 1) % seg;
                int ti = i * 6;
                tris[ti] = i * 2; tris[ti + 1] = i * 2 + 1; tris[ti + 2] = nx * 2 + 1;
                tris[ti + 3] = i * 2; tris[ti + 4] = nx * 2 + 1; tris[ti + 5] = nx * 2;
            }
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.position = center;
            go.transform.rotation = Quaternion.LookRotation(normal);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        void EnsureMaterials()
        {
            if (matsCreated) return;
            matsCreated = true;
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            capMats[0] = MakeMat(sh, unassignedColor);
            capMats[1] = MakeMat(sh, inletColor);
            capMats[2] = MakeMat(sh, outletColor);
            ringMats[0] = MakeMat(sh, unassignedRing);
            ringMats[1] = MakeMat(sh, inletRing);
            ringMats[2] = MakeMat(sh, outletRing);
        }

        Material MakeMat(Shader sh, Color col)
        {
            if (sh == null)
            {
                Debug.LogError("[BoundaryConditionManager] Shader is null for MakeMat.");
                sh = Shader.Find("Hidden/InternalErrorShader");
            }
            var m = new Material(sh);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
            if (m.HasProperty("_Color")) m.SetColor("_Color", col);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", (float)CullMode.Off);
            m.renderQueue = (int)RenderQueue.Transparent + 5;
            return m;
        }

        public void ClearCaps()
        {
            foreach (var a in assignments)
            {
                if (a.capObject != null) { Destroy(a.capObject); a.capObject = null; }
            }
        }

        void OnDestroy()
        {
            CancelPicking();
            ClearCaps();
            foreach (var m in capMats) if (m != null) Destroy(m);
            foreach (var m in ringMats) if (m != null) Destroy(m);
            if (pickCursor != null) Destroy(pickCursor);
        }
    }
}
