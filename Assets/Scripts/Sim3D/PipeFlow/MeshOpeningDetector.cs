using System.Collections.Generic;
using UnityEngine;

namespace AeroFlow.Sim3D.PipeFlow
{
    /// <summary>
    /// Represents a detected opening (hole) in a mesh.
    /// </summary>
    public class DetectedOpening
    {
        public Vector3 centroid;
        public Vector3 normal;
        public float radius;
        public int vertexCount;
        public List<Vector3> boundaryPositions;
    }

    /// <summary>
    /// Static utility that finds circular openings in a loaded mesh by detecting
    /// boundary edges (edges used by exactly one triangle), grouping them into
    /// connected loops, and fitting a circle to each loop.
    /// </summary>
    public static class MeshOpeningDetector
    {
        /// <summary>
        /// Minimum boundary-loop vertex count to be considered an opening.
        /// Very small loops (< 6 verts) are cracks or noise, not real openings.
        /// </summary>
        private const int MinLoopVertices = 6;

        /// <summary>
        /// Maximum eccentricity ratio (max-deviation / radius). Loops that deviate
        /// too much from a circle are discarded.
        /// </summary>
        private const float MaxEccentricity = 0.65f;

        public static List<DetectedOpening> DetectOpenings(GameObject model)
        {
            var result = new List<DetectedOpening>();
            if (model == null) return result;

            // Collect all mesh data from the model hierarchy
            var meshFilters = model.GetComponentsInChildren<MeshFilter>(true);
            if (meshFilters.Length == 0) return result;

            // Merge all triangles into a single soup (world space)
            var allVertices = new List<Vector3>();
            var allTriangles = new List<int>();

            foreach (var mf in meshFilters)
            {
                Mesh mesh = mf.sharedMesh;
                if (mesh == null) continue;

                Vector3[] localVerts = mesh.vertices;
                Transform t = mf.transform;
                int baseIndex = allVertices.Count;

                for (int i = 0; i < localVerts.Length; i++)
                {
                    allVertices.Add(t.TransformPoint(localVerts[i]));
                }

                for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                {
                    if (mesh.GetTopology(subMesh) != MeshTopology.Triangles)
                        continue;

                    int[] tris = mesh.GetTriangles(subMesh);
                    if (tris == null || tris.Length < 3)
                        continue;

                    for (int i = 0; i < tris.Length; i++)
                    {
                        allTriangles.Add(tris[i] + baseIndex);
                    }
                }
            }

            if (allVertices.Count == 0 || allTriangles.Count < 3)
                return result;

            // Weld nearby vertices to handle floating-point mismatches at seams
            float weldThreshold = ComputeWeldThreshold(allVertices);
            int[] remap = WeldVertices(allVertices, weldThreshold, out List<Vector3> welded);

            // Build edge map: edge → triangle count
            // An edge used by exactly 1 triangle is a boundary edge (open edge)
            var edgeCount = new Dictionary<long, int>();
            var edgeToVerts = new Dictionary<long, (int a, int b)>();

            int triCount = allTriangles.Count / 3;
            for (int t = 0; t < triCount; t++)
            {
                int a = remap[allTriangles[t * 3 + 0]];
                int b = remap[allTriangles[t * 3 + 1]];
                int c = remap[allTriangles[t * 3 + 2]];

                AddEdge(edgeCount, edgeToVerts, a, b);
                AddEdge(edgeCount, edgeToVerts, b, c);
                AddEdge(edgeCount, edgeToVerts, c, a);
            }

            // Collect boundary edges (used by exactly 1 triangle)
            var boundaryAdj = new Dictionary<int, List<int>>();
            foreach (var kvp in edgeCount)
            {
                if (kvp.Value != 1) continue;

                var (va, vb) = edgeToVerts[kvp.Key];
                if (!boundaryAdj.ContainsKey(va)) boundaryAdj[va] = new List<int>(4);
                if (!boundaryAdj.ContainsKey(vb)) boundaryAdj[vb] = new List<int>(4);
                boundaryAdj[va].Add(vb);
                boundaryAdj[vb].Add(va);
            }

            if (boundaryAdj.Count == 0)
            {
                Debug.Log("[MeshOpeningDetector] No boundary edges found — mesh is watertight.");
                return result;
            }

            // Group connected boundary vertices into loops
            var visited = new HashSet<int>();
            var loops = new List<List<int>>();

            foreach (int start in boundaryAdj.Keys)
            {
                if (visited.Contains(start)) continue;

                var loop = new List<int>();
                var stack = new Stack<int>();
                stack.Push(start);

                while (stack.Count > 0)
                {
                    int v = stack.Pop();
                    if (visited.Contains(v)) continue;
                    visited.Add(v);
                    loop.Add(v);

                    if (boundaryAdj.TryGetValue(v, out var neighbors))
                    {
                        foreach (int n in neighbors)
                        {
                            if (!visited.Contains(n))
                                stack.Push(n);
                        }
                    }
                }

                if (loop.Count >= MinLoopVertices)
                    loops.Add(loop);
            }

            Debug.Log($"[MeshOpeningDetector] Found {loops.Count} boundary loops (>= {MinLoopVertices} verts).");

            // Fit a circle to each loop and filter by eccentricity
            foreach (var loop in loops)
            {
                // Compute centroid
                Vector3 centroid = Vector3.zero;
                var positions = new List<Vector3>(loop.Count);
                foreach (int vi in loop)
                {
                    positions.Add(welded[vi]);
                    centroid += welded[vi];
                }
                centroid /= loop.Count;

                // Fit normal via covariance-based PCA (smallest eigenvector = normal)
                Vector3 normal = FitPlaneNormal(positions, centroid);

                // Compute radius: average distance from centroid projected onto the plane
                float radiusSum = 0f;
                float maxDeviation = 0f;
                foreach (var pos in positions)
                {
                    Vector3 diff = pos - centroid;
                    Vector3 projected = diff - normal * Vector3.Dot(diff, normal);
                    float dist = projected.magnitude;
                    radiusSum += dist;
                    float heightDev = Mathf.Abs(Vector3.Dot(diff, normal));
                    maxDeviation = Mathf.Max(maxDeviation, heightDev);
                }
                float radius = radiusSum / loop.Count;

                // Check eccentricity: how "flat" and "circular" the loop is
                float eccentricity = radius > 1e-4f ? maxDeviation / radius : 999f;
                if (eccentricity > MaxEccentricity)
                {
                    continue; // Too non-planar to be a pipe opening
                }

                // Also check that the loop isn't too elongated (max dist vs min dist from centroid)
                float minR = float.MaxValue, maxR = 0f;
                foreach (var pos in positions)
                {
                    Vector3 diff = pos - centroid;
                    Vector3 projected = diff - normal * Vector3.Dot(diff, normal);
                    float dist = projected.magnitude;
                    minR = Mathf.Min(minR, dist);
                    maxR = Mathf.Max(maxR, dist);
                }
                float roundness = minR / Mathf.Max(maxR, 1e-5f);
                if (roundness < 0.4f)
                {
                    continue; // Too elongated, not circular
                }

                result.Add(new DetectedOpening
                {
                    centroid = centroid,
                    normal = normal,
                    radius = radius,
                    vertexCount = loop.Count,
                    boundaryPositions = positions
                });
            }

            // Sort by distance from model center (most distant first = more likely pipe ends)
            if (result.Count > 1)
            {
                Vector3 modelCenter = ComputeModelCenter(model);
                result.Sort((a, b) =>
                {
                    float da = Vector3.Distance(a.centroid, modelCenter);
                    float db = Vector3.Distance(b.centroid, modelCenter);
                    return db.CompareTo(da);
                });
            }

            Debug.Log($"[MeshOpeningDetector] Detected {result.Count} circular openings.");
            return result;
        }

        // ----- Helpers -----

        static void AddEdge(Dictionary<long, int> counts, Dictionary<long, (int, int)> map, int a, int b)
        {
            long key = EdgeKey(a, b);
            if (counts.ContainsKey(key))
                counts[key]++;
            else
            {
                counts[key] = 1;
                map[key] = (a, b);
            }
        }

        static long EdgeKey(int a, int b)
        {
            int lo = Mathf.Min(a, b);
            int hi = Mathf.Max(a, b);
            return ((long)lo << 32) | (long)(uint)hi;
        }

        static float ComputeWeldThreshold(List<Vector3> verts)
        {
            // Use 0.01% of bounding box diagonal as weld threshold
            if (verts.Count < 2) return 0.001f;
            Vector3 min = verts[0], max = verts[0];
            for (int i = 1; i < verts.Count; i++)
            {
                min = Vector3.Min(min, verts[i]);
                max = Vector3.Max(max, verts[i]);
            }
            float diag = (max - min).magnitude;
            return Mathf.Max(diag * 0.0001f, 0.0001f);
        }

        static int[] WeldVertices(List<Vector3> verts, float threshold, out List<Vector3> welded)
        {
            int n = verts.Count;
            int[] remap = new int[n];
            welded = new List<Vector3>(n);
            float threshSq = threshold * threshold;

            // Simple spatial bucketing for performance
            var buckets = new Dictionary<long, List<int>>();
            float cellSize = Mathf.Max(threshold * 4f, 0.001f);

            for (int i = 0; i < n; i++)
            {
                Vector3 v = verts[i];
                long bx = (long)Mathf.FloorToInt(v.x / cellSize);
                long by = (long)Mathf.FloorToInt(v.y / cellSize);
                long bz = (long)Mathf.FloorToInt(v.z / cellSize);

                int found = -1;

                // Check nearby buckets
                for (long dx = -1; dx <= 1 && found < 0; dx++)
                for (long dy = -1; dy <= 1 && found < 0; dy++)
                for (long dz = -1; dz <= 1 && found < 0; dz++)
                {
                    long key = HashBucket(bx + dx, by + dy, bz + dz);
                    if (buckets.TryGetValue(key, out var bucket))
                    {
                        foreach (int wi in bucket)
                        {
                            if ((welded[wi] - v).sqrMagnitude < threshSq)
                            {
                                found = wi;
                                break;
                            }
                        }
                    }
                }

                if (found >= 0)
                {
                    remap[i] = found;
                }
                else
                {
                    int newIdx = welded.Count;
                    remap[i] = newIdx;
                    welded.Add(v);

                    long bucketKey = HashBucket(bx, by, bz);
                    if (!buckets.ContainsKey(bucketKey))
                        buckets[bucketKey] = new List<int>(4);
                    buckets[bucketKey].Add(newIdx);
                }
            }

            return remap;
        }

        static long HashBucket(long x, long y, long z)
        {
            unchecked
            {
                long h = x * 73856093L ^ y * 19349663L ^ z * 83492791L;
                return h;
            }
        }

        static Vector3 FitPlaneNormal(List<Vector3> points, Vector3 centroid)
        {
            // Covariance matrix approach
            float xx = 0, xy = 0, xz = 0;
            float yy = 0, yz = 0, zz = 0;

            foreach (var p in points)
            {
                Vector3 d = p - centroid;
                xx += d.x * d.x;
                xy += d.x * d.y;
                xz += d.x * d.z;
                yy += d.y * d.y;
                yz += d.y * d.z;
                zz += d.z * d.z;
            }

            // Find the eigenvector with the smallest eigenvalue
            // For a plane fit, this is the normal direction
            // Use the cross-product of two largest eigenvectors (Newell's method as approximation)
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < points.Count; i++)
            {
                int j = (i + 1) % points.Count;
                Vector3 vi = points[i] - centroid;
                Vector3 vj = points[j] - centroid;
                normal.x += (vi.y - vj.y) * (vi.z + vj.z);
                normal.y += (vi.z - vj.z) * (vi.x + vj.x);
                normal.z += (vi.x - vj.x) * (vi.y + vj.y);
            }

            if (normal.sqrMagnitude < 1e-10f)
            {
                // Fallback: use covariance approach
                float detX = yy * zz - yz * yz;
                float detY = xx * zz - xz * xz;
                float detZ = xx * yy - xy * xy;

                if (detX >= detY && detX >= detZ)
                    normal = new Vector3(detX, xz * yz - xy * zz, xy * yz - xz * yy);
                else if (detY >= detX && detY >= detZ)
                    normal = new Vector3(xz * yz - xy * zz, detY, xy * xz - yz * xx);
                else
                    normal = new Vector3(xy * yz - xz * yy, xy * xz - yz * xx, detZ);
            }

            if (normal.sqrMagnitude < 1e-10f)
                return Vector3.up;

            return normal.normalized;
        }

        static Vector3 ComputeModelCenter(GameObject model)
        {
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return model.transform.position;

            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                b.Encapsulate(renderers[i].bounds);
            return b.center;
        }
    }
}
