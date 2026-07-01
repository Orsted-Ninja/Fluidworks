using System;
using System.Collections.Generic;
using UnityEngine;
using AeroFlow.Rendering;
using AeroFlow.Display;

namespace AeroFlow.Core
{
    public static class MeshSegmentationUtility
    {
        private const float BladeScoreThreshold = 0.55f;
        private const float FallbackRotatingFraction = 0.35f;

        private struct TriangleSample
        {
            public int triangleIndex;
            public float score;
        }

        public static bool TryAutoSegmentSingleMesh(GameObject modelRoot, out int createdParts)
        {
            createdParts = 0;
            if (modelRoot == null)
            {
                return false;
            }

            var renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return false;
            }

            Renderer renderer = FindPrimaryRenderableRenderer(renderers);
            if (renderer == null || renderer.GetComponentInParent<RuntimeSimulationProxy>() != null)
            {
                return false;
            }

            Mesh sourceMesh = GetMesh(renderer);
            if (sourceMesh == null)
            {
                return false;
            }

            if (sourceMesh.vertexCount < 12 || sourceMesh.triangles == null || sourceMesh.triangles.Length < 36)
            {
                return false;
            }

            Transform sourceTransform = renderer.transform;
            Material[] sourceMaterials = renderer.sharedMaterials;
            Material sourceMaterial = sourceMaterials != null && sourceMaterials.Length > 0 ? sourceMaterials[0] : null;

            var triangles = sourceMesh.triangles;
            var vertices = sourceMesh.vertices;
            var normals = sourceMesh.normals;
            Matrix4x4 localToWorld = sourceTransform.localToWorldMatrix;
            Bounds localBounds = sourceMesh.bounds;
            Vector3 centerWorld = sourceTransform.TransformPoint(localBounds.center);

            Vector3 axis = EstimateBestAxis(localToWorld, vertices, triangles, centerWorld);
            var samples = BuildTriangleSamples(localToWorld, vertices, normals, triangles, centerWorld, axis);
            if (samples.Count == 0)
            {
                return false;
            }

            var rotating = new List<int>(samples.Count);
            var staticParts = new List<int>(samples.Count);

            for (int i = 0; i < samples.Count; i++)
            {
                if (samples[i].score >= BladeScoreThreshold)
                {
                    rotating.Add(samples[i].triangleIndex);
                }
                else
                {
                    staticParts.Add(samples[i].triangleIndex);
                }
            }

            if ((rotating.Count == 0 || staticParts.Count == 0) && samples.Count >= 6)
            {
                samples.Sort((a, b) => b.score.CompareTo(a.score));
                rotating.Clear();
                staticParts.Clear();

                int rotatingCount = Mathf.Clamp(Mathf.RoundToInt(samples.Count * FallbackRotatingFraction), 1, samples.Count - 1);
                for (int i = 0; i < samples.Count; i++)
                {
                    if (i < rotatingCount) rotating.Add(samples[i].triangleIndex);
                    else staticParts.Add(samples[i].triangleIndex);
                }
            }

            if (rotating.Count == 0 || staticParts.Count == 0)
            {
                return false;
            }

            var created = new List<GameObject>(2);
            CreateChildSegment(sourceTransform, sourceMesh, sourceMaterial, sourceMaterials, rotating, "RotatingBlade_Auto", PartSegmentationCollection.RotatingBlade, created);
            CreateChildSegment(sourceTransform, sourceMesh, sourceMaterial, sourceMaterials, staticParts, "StaticStructure_Auto", PartSegmentationCollection.StaticStructure, created);

            if (created.Count == 0)
            {
                return false;
            }

            // Hide the original welded mesh so the new segments become the visible parts.
            if (renderer is MeshRenderer meshRenderer)
            {
                var filter = sourceTransform.GetComponent<MeshFilter>();
                if (filter != null)
                {
                    filter.sharedMesh = null;
                }
                meshRenderer.enabled = false;
            }
            else if (renderer is SkinnedMeshRenderer skinned)
            {
                skinned.sharedMesh = null;
                skinned.enabled = false;
            }

            createdParts = created.Count;
            return true;
        }

        private static Renderer FindPrimaryRenderableRenderer(Renderer[] renderers)
        {
            Renderer bestRenderer = null;
            int bestTriangleCount = -1;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                if (renderer.GetComponentInParent<RuntimeSimulationProxy>() != null) continue;

                Mesh mesh = GetMesh(renderer);
                if (mesh == null || mesh.triangles == null || mesh.triangles.Length < 3)
                {
                    continue;
                }

                int triangleCount = mesh.triangles.Length;
                if (triangleCount > bestTriangleCount)
                {
                    bestTriangleCount = triangleCount;
                    bestRenderer = renderer;
                }
            }

            return bestRenderer;
        }

        private static void CreateChildSegment(
            Transform parent,
            Mesh sourceMesh,
            Material sourceMaterial,
            Material[] sourceMaterials,
            List<int> triangleIndices,
            string name,
            PartSegmentationCollection collection,
            List<GameObject> created)
        {
            if (parent == null || sourceMesh == null || triangleIndices == null || triangleIndices.Count == 0)
            {
                return;
            }

            Mesh mesh = BuildSubsetMesh(sourceMesh, triangleIndices);
            if (mesh == null)
            {
                return;
            }

            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = Vector3.zero;
            child.transform.localRotation = Quaternion.identity;
            child.transform.localScale = Vector3.one;

            var filter = child.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = sourceMaterial != null ? new Material(sourceMaterial) : CreateFallbackMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;

            var state = child.AddComponent<RenderModeState>();
            state.CacheOriginal(renderer);
            created.Add(child);
        }

        private static List<TriangleSample> BuildTriangleSamples(
            Matrix4x4 localToWorld,
            Vector3[] vertices,
            Vector3[] normals,
            int[] triangles,
            Vector3 centerWorld,
            Vector3 axisWorld)
        {
            var samples = new List<TriangleSample>(triangles.Length / 3);
            float maxRadius = 1e-5f;

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                if (a < 0 || b < 0 || c < 0 || a >= vertices.Length || b >= vertices.Length || c >= vertices.Length)
                {
                    continue;
                }

                Vector3 w0 = localToWorld.MultiplyPoint3x4(vertices[a]);
                Vector3 w1 = localToWorld.MultiplyPoint3x4(vertices[b]);
                Vector3 w2 = localToWorld.MultiplyPoint3x4(vertices[c]);
                Vector3 centroid = (w0 + w1 + w2) / 3f;
                Vector3 normal = Vector3.Cross(w1 - w0, w2 - w0);
                if (normal.sqrMagnitude <= 1e-10f)
                {
                    continue;
                }

                Vector3 radial = centroid - centerWorld;
                radial -= axisWorld * Vector3.Dot(radial, axisWorld);
                maxRadius = Mathf.Max(maxRadius, radial.magnitude);
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

                Vector3 w0 = localToWorld.MultiplyPoint3x4(vertices[a]);
                Vector3 w1 = localToWorld.MultiplyPoint3x4(vertices[b]);
                Vector3 w2 = localToWorld.MultiplyPoint3x4(vertices[c]);
                Vector3 centroid = (w0 + w1 + w2) / 3f;
                Vector3 normal = Vector3.Cross(w1 - w0, w2 - w0);
                float area = normal.magnitude * 0.5f;
                if (normal.sqrMagnitude <= 1e-10f)
                {
                    continue;
                }

                Vector3 axis = axisWorld.sqrMagnitude > 1e-6f ? axisWorld.normalized : Vector3.up;
                Vector3 radial = centroid - centerWorld;
                radial -= axis * Vector3.Dot(radial, axis);
                float radialScore = Mathf.Clamp01(radial.magnitude / Mathf.Max(maxRadius, 1e-5f));
                float normalScore = 1f - Mathf.Abs(Vector3.Dot(normal.normalized, axis));
                float areaScore = Mathf.Clamp01(area / Mathf.Max(maxRadius * maxRadius, 1e-4f));
                float score = Mathf.Clamp01(0.58f * radialScore + 0.32f * normalScore + 0.10f * areaScore);
                samples.Add(new TriangleSample { triangleIndex = i / 3, score = score });
            }

            return samples;
        }

        private static Vector3 EstimateBestAxis(Matrix4x4 localToWorld, Vector3[] vertices, int[] triangles, Vector3 centerWorld)
        {
            Vector3[] candidates = { Vector3.right, Vector3.up, Vector3.forward };
            Vector3 bestAxis = Vector3.up;
            float bestScore = float.MinValue;

            foreach (var candidate in candidates)
            {
                float score = ScoreAxis(localToWorld, vertices, triangles, centerWorld, candidate);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestAxis = candidate;
                }
            }

            return bestAxis;
        }

        private static float ScoreAxis(Matrix4x4 localToWorld, Vector3[] vertices, int[] triangles, Vector3 centerWorld, Vector3 axisWorld)
        {
            float maxRadius = 1e-5f;
            float sum = 0f;
            int count = 0;

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                if (a < 0 || b < 0 || c < 0 || a >= vertices.Length || b >= vertices.Length || c >= vertices.Length)
                {
                    continue;
                }

                Vector3 w0 = localToWorld.MultiplyPoint3x4(vertices[a]);
                Vector3 w1 = localToWorld.MultiplyPoint3x4(vertices[b]);
                Vector3 w2 = localToWorld.MultiplyPoint3x4(vertices[c]);
                Vector3 centroid = (w0 + w1 + w2) / 3f;
                Vector3 normal = Vector3.Cross(w1 - w0, w2 - w0);
                if (normal.sqrMagnitude <= 1e-10f)
                {
                    continue;
                }

                Vector3 axis = axisWorld.normalized;
                Vector3 radial = centroid - centerWorld;
                radial -= axis * Vector3.Dot(radial, axis);
                maxRadius = Mathf.Max(maxRadius, radial.magnitude);
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

                Vector3 w0 = localToWorld.MultiplyPoint3x4(vertices[a]);
                Vector3 w1 = localToWorld.MultiplyPoint3x4(vertices[b]);
                Vector3 w2 = localToWorld.MultiplyPoint3x4(vertices[c]);
                Vector3 centroid = (w0 + w1 + w2) / 3f;
                Vector3 normal = Vector3.Cross(w1 - w0, w2 - w0);
                if (normal.sqrMagnitude <= 1e-10f)
                {
                    continue;
                }

                Vector3 axis = axisWorld.normalized;
                Vector3 radial = centroid - centerWorld;
                radial -= axis * Vector3.Dot(radial, axis);
                float radialScore = Mathf.Clamp01(radial.magnitude / Mathf.Max(maxRadius, 1e-5f));
                float normalScore = 1f - Mathf.Abs(Vector3.Dot(normal.normalized, axis));
                sum += 0.58f * radialScore + 0.42f * normalScore;
                count++;
            }

            return count > 0 ? sum / count : float.MinValue;
        }

        private static Mesh BuildSubsetMesh(Mesh source, List<int> triangleIndices)
        {
            if (source == null || triangleIndices == null || triangleIndices.Count == 0)
            {
                return null;
            }

            var sourceTriangles = source.triangles;
            if (sourceTriangles == null || sourceTriangles.Length == 0)
            {
                return null;
            }

            var sourceVertices = source.vertices;
            var sourceNormals = source.normals;
            var sourceUV = source.uv;
            var sourceColors = source.colors;
            var sourceTangents = source.tangents;

            var remap = new Dictionary<int, int>(triangleIndices.Count * 3);
            var vertices = new List<Vector3>(triangleIndices.Count * 3);
            var normals = new List<Vector3>(triangleIndices.Count * 3);
            var uvs = new List<Vector2>(triangleIndices.Count * 3);
            var colors = new List<Color>(triangleIndices.Count * 3);
            var tangents = new List<Vector4>(triangleIndices.Count * 3);
            var triangles = new List<int>(triangleIndices.Count * 3);

            bool hasNormals = sourceNormals != null && sourceNormals.Length == sourceVertices.Length;
            bool hasUV = sourceUV != null && sourceUV.Length == sourceVertices.Length;
            bool hasColors = sourceColors != null && sourceColors.Length == sourceVertices.Length;
            bool hasTangents = sourceTangents != null && sourceTangents.Length == sourceVertices.Length;

            for (int i = 0; i < triangleIndices.Count; i++)
            {
                int tri = triangleIndices[i];
                int baseIndex = tri * 3;
                if (baseIndex + 2 >= sourceTriangles.Length)
                {
                    continue;
                }

                for (int j = 0; j < 3; j++)
                {
                    int srcIndex = sourceTriangles[baseIndex + j];
                    if (!remap.TryGetValue(srcIndex, out int dstIndex))
                    {
                        dstIndex = vertices.Count;
                        remap[srcIndex] = dstIndex;
                        vertices.Add(sourceVertices[srcIndex]);
                        normals.Add(hasNormals ? sourceNormals[srcIndex] : Vector3.zero);
                        uvs.Add(hasUV ? sourceUV[srcIndex] : Vector2.zero);
                        colors.Add(hasColors ? sourceColors[srcIndex] : Color.white);
                        tangents.Add(hasTangents ? sourceTangents[srcIndex] : Vector4.zero);
                    }

                    triangles.Add(dstIndex);
                }
            }

            if (triangles.Count < 3 || vertices.Count == 0)
            {
                return null;
            }

            var mesh = new Mesh
            {
                name = source.name + "_Seg"
            };
            mesh.indexFormat = vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0, true);
            if (hasNormals)
            {
                mesh.SetNormals(normals);
            }
            else
            {
                mesh.RecalculateNormals();
            }
            if (hasUV) mesh.SetUVs(0, uvs);
            if (hasColors) mesh.SetColors(colors);
            if (hasTangents) mesh.SetTangents(tangents);
            mesh.RecalculateBounds();
            return mesh;
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

        private static Material CreateFallbackMaterial()
        {
            Shader shader = RuntimeShaderResolver.FindSectionableShader() ?? RuntimeShaderResolver.FindLitShader();
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                return null;
            }

            var material = new Material(shader);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", new Color(0.18f, 0.28f, 0.40f, 1f));
            if (material.HasProperty("_Color")) material.SetColor("_Color", new Color(0.18f, 0.28f, 0.40f, 1f));
            return material;
        }
    }
}
