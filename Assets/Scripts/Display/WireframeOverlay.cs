using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AeroFlow.Display
{
    public class WireframeOverlay : MonoBehaviour
    {
        private Mesh _lineMesh;
        private MeshRenderer _renderer;
        private MeshFilter _filter;
        private Mesh _sourceMesh;

        public static WireframeOverlay Attach(GameObject host, Mesh sourceMesh, Material mat)
        {
            var go = new GameObject("WireframeOverlay");
            go.transform.SetParent(host.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var overlay = go.AddComponent<WireframeOverlay>();
            overlay.Build(sourceMesh, mat);
            return overlay;
        }

        public void Build(Mesh sourceMesh, Material mat)
        {
            _sourceMesh = sourceMesh;
            _filter = gameObject.AddComponent<MeshFilter>();
            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.sharedMaterial = mat;

            _lineMesh = BuildLineMesh(sourceMesh);
            _filter.sharedMesh = _lineMesh;
        }

        public void SetEnabled(bool enabled)
        {
            if (_renderer != null) _renderer.enabled = enabled;
        }

        private Mesh BuildLineMesh(Mesh mesh)
        {
            var vertices = mesh.vertices;
            var edges = new HashSet<ulong>();
            var lineVerts = new List<Vector3>(Mathf.Max(mesh.vertexCount * 2, 64));
            var lineIndices = new List<int>(Mathf.Max(mesh.vertexCount * 2, 64));

            void AddEdge(int a, int b)
            {
                uint min = (uint)Mathf.Min(a, b);
                uint max = (uint)Mathf.Max(a, b);
                ulong key = ((ulong)min << 32) | max;
                if (edges.Add(key))
                {
                    lineIndices.Add(lineVerts.Count);
                    lineVerts.Add(vertices[min]);
                    lineIndices.Add(lineVerts.Count);
                    lineVerts.Add(vertices[max]);
                }
            }

            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                int[] triangles = mesh.GetIndices(s);
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    AddEdge(triangles[i], triangles[i + 1]);
                    AddEdge(triangles[i + 1], triangles[i + 2]);
                    AddEdge(triangles[i + 2], triangles[i]);
                }
            }

            if (lineVerts.Count == 0)
            {
                lineVerts.Add(Vector3.zero);
                lineVerts.Add(Vector3.zero);
                lineIndices.Add(0);
                lineIndices.Add(1);
            }

            var lineMesh = new Mesh();
            if (lineVerts.Count > 65535)
            {
                lineMesh.indexFormat = IndexFormat.UInt32;
            }
            lineMesh.SetVertices(lineVerts);
            lineMesh.SetIndices(lineIndices, MeshTopology.Lines, 0);
            lineMesh.RecalculateBounds();
            return lineMesh;
        }
    }
}
