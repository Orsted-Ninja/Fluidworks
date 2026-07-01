using System;
using System.Collections.Generic;
using UnityEngine;

namespace AeroFlow.Core
{
    public struct MeshValidationReport
    {
        public bool valid;
        public int meshCount;
        public int vertexCount;
        public int triangleCount;
        public int duplicateVertexCount;
        public int degenerateTriangleCount;
        public int nonManifoldEdgeCount;
        public int flippedNormalHintCount;
        public string summary;
        public string suggestions;
    }

    public static class MeshValidationUtility
    {
        private const float VertexQuantizeScale = 100000f;

        public static MeshValidationReport Validate(GameObject root)
        {
            MeshValidationReport report = new MeshValidationReport
            {
                valid = true,
                summary = "No visible meshes found.",
                suggestions = "Import a renderable mesh to run validation."
            };

            if (root == null)
            {
                return report;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return report;
            }

            var edgeUseCounts = new Dictionary<ulong, int>(4096);
            var uniqueVertices = new HashSet<long>(4096);
            int meshCount = 0;
            int totalVertices = 0;
            int totalTriangles = 0;
            int duplicateVertices = 0;
            int degenerateTriangles = 0;
            int flippedNormals = 0;

            for (int r = 0; r < renderers.Length; r++)
            {
                var renderer = renderers[r];
                if (renderer == null || renderer.GetComponentInParent<RuntimeSimulationProxy>() != null)
                {
                    continue;
                }

                Mesh mesh = GetMesh(renderer);
                if (mesh == null)
                {
                    continue;
                }

                meshCount++;
                var vertices = mesh.vertices;
                var normals = mesh.normals;
                var triangles = mesh.triangles;

                totalVertices += vertices.Length;
                totalTriangles += triangles.Length / 3;

                for (int i = 0; i < vertices.Length; i++)
                {
                    long key = QuantizeVertex(vertices[i]);
                    if (!uniqueVertices.Add(key))
                    {
                        duplicateVertices++;
                    }
                }

                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    int a = triangles[i];
                    int b = triangles[i + 1];
                    int c = triangles[i + 2];
                    if (a < 0 || b < 0 || c < 0 || a >= vertices.Length || b >= vertices.Length || c >= vertices.Length)
                    {
                        continue;
                    }

                    Vector3 v0 = vertices[a];
                    Vector3 v1 = vertices[b];
                    Vector3 v2 = vertices[c];
                    Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0);
                    if (normal.sqrMagnitude <= 1e-10f)
                    {
                        degenerateTriangles++;
                        continue;
                    }

                    RegisterEdge(edgeUseCounts, a, b);
                    RegisterEdge(edgeUseCounts, b, c);
                    RegisterEdge(edgeUseCounts, c, a);

                    if (normals != null && normals.Length == vertices.Length)
                    {
                        Vector3 faceNormal = normal.normalized;
                        Vector3 vertexNormal = (normals[a] + normals[b] + normals[c]) / 3f;
                        if (Vector3.Dot(faceNormal, vertexNormal.normalized) < -0.15f)
                        {
                            flippedNormals++;
                        }
                    }
                }
            }

            int nonManifold = 0;
            foreach (var kv in edgeUseCounts)
            {
                if (kv.Value > 2)
                {
                    nonManifold++;
                }
            }

            report.valid = meshCount > 0;
            report.meshCount = meshCount;
            report.vertexCount = totalVertices;
            report.triangleCount = totalTriangles;
            report.duplicateVertexCount = duplicateVertices;
            report.degenerateTriangleCount = degenerateTriangles;
            report.nonManifoldEdgeCount = nonManifold;
            report.flippedNormalHintCount = flippedNormals;
            report.summary = $"Meshes: {meshCount}, Vertices: {totalVertices}, Triangles: {totalTriangles}";

            var suggestions = new List<string>();
            if (degenerateTriangles > 0) suggestions.Add("re-run the mesh with degenerate triangles removed");
            if (nonManifold > 0) suggestions.Add("repair non-manifold edges before export");
            if (duplicateVertices > 0) suggestions.Add("merge duplicate vertices");
            if (flippedNormals > 0) suggestions.Add("recalculate or flip normals");
            report.suggestions = suggestions.Count > 0
                ? "Suggested repairs: " + string.Join(", ", suggestions)
                : "Mesh validation looks clean.";

            if (degenerateTriangles > 0 || nonManifold > 0 || flippedNormals > 0)
            {
                report.valid = false;
            }

            return report;
        }

        private static Mesh GetMesh(Renderer renderer)
        {
            if (renderer is MeshRenderer)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                return filter != null ? filter.sharedMesh : null;
            }

            if (renderer is SkinnedMeshRenderer skinned)
            {
                return skinned.sharedMesh;
            }

            return null;
        }

        private static long QuantizeVertex(Vector3 v)
        {
            long x = (long)Mathf.Round(v.x * VertexQuantizeScale);
            long y = (long)Mathf.Round(v.y * VertexQuantizeScale);
            long z = (long)Mathf.Round(v.z * VertexQuantizeScale);
            return (x * 73856093L) ^ (y * 19349663L) ^ (z * 83492791L);
        }

        private static void RegisterEdge(Dictionary<ulong, int> edgeUseCounts, int a, int b)
        {
            uint lo = (uint)Mathf.Min(a, b);
            uint hi = (uint)Mathf.Max(a, b);
            ulong key = ((ulong)lo << 32) | hi;
            edgeUseCounts.TryGetValue(key, out int count);
            edgeUseCounts[key] = count + 1;
        }
    }
}
