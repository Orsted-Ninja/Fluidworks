using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using AeroFlow.Rendering;

namespace AeroFlow.Core
{
    /// <summary>
    /// Lightweight runtime mesh importer for common CAD-export formats.
    /// Supports OBJ and STL without extra packages.
    /// </summary>
    public static class RuntimeMeshImporter
    {
        static readonly string[] RuntimeMeshExtensions = { ".obj", ".stl" };
        static readonly string[] SimulationReferenceExtensions = { ".obj", ".stl", ".step", ".stp", ".iges", ".igs", ".glb", ".gltf" };

        public static bool SupportsRuntimeMesh(string path)
        {
            string ext = GetExtension(path);
            for (int i = 0; i < RuntimeMeshExtensions.Length; i++)
            {
                if (ext == RuntimeMeshExtensions[i]) return true;
            }
            return false;
        }

        public static bool SupportsSimulationReference(string path)
        {
            string ext = GetExtension(path);
            for (int i = 0; i < SimulationReferenceExtensions.Length; i++)
            {
                if (ext == SimulationReferenceExtensions[i]) return true;
            }
            return false;
        }

        public static GameObject Import(string path, Transform parent, Material material, bool visible, string objectName = null)
        {
            Mesh mesh = LoadMesh(path);
            if (mesh == null)
            {
                return null;
            }

            var go = new GameObject(string.IsNullOrEmpty(objectName) ? Path.GetFileNameWithoutExtension(path) : objectName);
            go.transform.SetParent(parent, false);

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material != null ? material : CreateFallbackMaterial();
            renderer.enabled = visible;
            renderer.shadowCastingMode = visible ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = visible;

            return go;
        }

        static Mesh LoadMesh(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                Debug.LogWarning($"[RuntimeMeshImporter] Missing file: {path}");
                return null;
            }

            switch (GetExtension(path))
            {
                case ".obj":
                    return LoadObj(path);
                case ".stl":
                    return LoadStl(path);
                default:
                    Debug.LogWarning($"[RuntimeMeshImporter] Unsupported runtime mesh format: {path}");
                    return null;
            }
        }

        static Mesh LoadObj(string path)
        {
            var sourcePositions = new List<Vector3>(4096);
            var sourceNormals = new List<Vector3>(4096);
            var meshPositions = new List<Vector3>(8192);
            var meshNormals = new List<Vector3>(8192);
            var triangles = new List<int>(16384);
            var vertexLookup = new Dictionary<string, int>(8192);
            bool hasNormals = false;

            foreach (string rawLine in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(rawLine)) continue;
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                switch (parts[0])
                {
                    case "v":
                        if (parts.Length >= 4)
                        {
                            sourcePositions.Add(new Vector3(
                                ParseFloat(parts[1]),
                                ParseFloat(parts[2]),
                                ParseFloat(parts[3])));
                        }
                        break;
                    case "vn":
                        if (parts.Length >= 4)
                        {
                            sourceNormals.Add(new Vector3(
                                ParseFloat(parts[1]),
                                ParseFloat(parts[2]),
                                ParseFloat(parts[3])));
                        }
                        break;
                    case "f":
                        if (parts.Length < 4) break;

                        int first = ResolveObjVertex(parts[1], sourcePositions, sourceNormals, meshPositions, meshNormals, vertexLookup, ref hasNormals);
                        for (int i = 2; i < parts.Length - 1; i++)
                        {
                            int second = ResolveObjVertex(parts[i], sourcePositions, sourceNormals, meshPositions, meshNormals, vertexLookup, ref hasNormals);
                            int third = ResolveObjVertex(parts[i + 1], sourcePositions, sourceNormals, meshPositions, meshNormals, vertexLookup, ref hasNormals);
                            triangles.Add(first);
                            triangles.Add(second);
                            triangles.Add(third);
                        }
                        break;
                }
            }

            if (meshPositions.Count == 0 || triangles.Count == 0)
            {
                Debug.LogWarning($"[RuntimeMeshImporter] OBJ contains no renderable geometry: {path}");
                return null;
            }

            var mesh = new Mesh
            {
                name = Path.GetFileNameWithoutExtension(path),
                indexFormat = meshPositions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.SetVertices(meshPositions);
            mesh.SetTriangles(triangles, 0, true);
            if (hasNormals)
            {
                mesh.SetNormals(meshNormals);
            }
            else
            {
                mesh.RecalculateNormals();
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        static int ResolveObjVertex(
            string token,
            List<Vector3> sourcePositions,
            List<Vector3> sourceNormals,
            List<Vector3> meshPositions,
            List<Vector3> meshNormals,
            Dictionary<string, int> vertexLookup,
            ref bool hasNormals)
        {
            if (vertexLookup.TryGetValue(token, out int existing))
            {
                return existing;
            }

            string[] indices = token.Split('/');
            int positionIndex = ParseObjIndex(indices.Length > 0 ? indices[0] : null, sourcePositions.Count);
            int normalIndex = ParseObjIndex(indices.Length > 2 ? indices[2] : null, sourceNormals.Count);

            if (positionIndex < 0 || positionIndex >= sourcePositions.Count)
            {
                throw new InvalidDataException($"OBJ vertex index out of range in token '{token}'.");
            }

            meshPositions.Add(sourcePositions[positionIndex]);
            if (normalIndex >= 0 && normalIndex < sourceNormals.Count)
            {
                meshNormals.Add(sourceNormals[normalIndex]);
                hasNormals = true;
            }
            else
            {
                meshNormals.Add(Vector3.zero);
            }

            int created = meshPositions.Count - 1;
            vertexLookup[token] = created;
            return created;
        }

        static int ParseObjIndex(string value, int count)
        {
            if (string.IsNullOrWhiteSpace(value)) return -1;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int raw))
            {
                return -1;
            }
            if (raw > 0) return raw - 1;
            if (raw < 0) return count + raw;
            return -1;
        }

        static Mesh LoadStl(string path)
        {
            return LooksLikeBinaryStl(path) ? LoadBinaryStl(path) : LoadAsciiStl(path);
        }

        static bool LooksLikeBinaryStl(string path)
        {
            using var stream = File.OpenRead(path);
            if (stream.Length < 84) return false;

            using var reader = new BinaryReader(stream, Encoding.ASCII, true);
            byte[] header = reader.ReadBytes(80);
            uint triangleCount = reader.ReadUInt32();
            long expectedLength = 84L + triangleCount * 50L;
            bool headerHasNull = false;
            for (int i = 0; i < header.Length; i++)
            {
                if (header[i] == 0)
                {
                    headerHasNull = true;
                    break;
                }
            }

            string headerText = Encoding.ASCII.GetString(header).Trim();
            if (expectedLength == stream.Length && (headerHasNull || !headerText.StartsWith("solid", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return expectedLength == stream.Length && headerHasNull;
        }

        static Mesh LoadBinaryStl(string path)
        {
            using var reader = new BinaryReader(File.OpenRead(path));
            reader.ReadBytes(80);
            uint triangleCount = reader.ReadUInt32();

            int vertexCount = checked((int)triangleCount * 3);
            var vertices = new List<Vector3>(vertexCount);
            var normals = new List<Vector3>(vertexCount);
            var triangles = new List<int>(vertexCount);

            for (int i = 0; i < triangleCount; i++)
            {
                Vector3 normal = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                Vector3 v0 = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                Vector3 v1 = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                Vector3 v2 = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                reader.ReadUInt16();

                int baseIndex = vertices.Count;
                vertices.Add(v0);
                vertices.Add(v1);
                vertices.Add(v2);
                normals.Add(normal);
                normals.Add(normal);
                normals.Add(normal);
                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex + 2);
            }

            return BuildTriangleMesh(path, vertices, triangles, normals);
        }

        static Mesh LoadAsciiStl(string path)
        {
            var vertices = new List<Vector3>(8192);
            var normals = new List<Vector3>(8192);
            var triangles = new List<int>(8192);
            Vector3 currentNormal = Vector3.zero;
            var facetVertices = new List<Vector3>(3);

            foreach (string rawLine in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(rawLine)) continue;
                string line = rawLine.Trim();
                if (line.StartsWith("facet normal", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 5)
                    {
                        currentNormal = new Vector3(ParseFloat(parts[2]), ParseFloat(parts[3]), ParseFloat(parts[4]));
                    }
                    facetVertices.Clear();
                }
                else if (line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4)
                    {
                        facetVertices.Add(new Vector3(ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3])));
                    }
                }
                else if (line.StartsWith("endfacet", StringComparison.OrdinalIgnoreCase) && facetVertices.Count >= 3)
                {
                    int baseIndex = vertices.Count;
                    vertices.Add(facetVertices[0]);
                    vertices.Add(facetVertices[1]);
                    vertices.Add(facetVertices[2]);
                    normals.Add(currentNormal);
                    normals.Add(currentNormal);
                    normals.Add(currentNormal);
                    triangles.Add(baseIndex);
                    triangles.Add(baseIndex + 1);
                    triangles.Add(baseIndex + 2);
                    facetVertices.Clear();
                }
            }

            return BuildTriangleMesh(path, vertices, triangles, normals);
        }

        static Mesh BuildTriangleMesh(string path, List<Vector3> vertices, List<int> triangles, List<Vector3> normals)
        {
            if (vertices.Count == 0 || triangles.Count == 0)
            {
                Debug.LogWarning($"[RuntimeMeshImporter] STL contains no renderable geometry: {path}");
                return null;
            }

            var mesh = new Mesh
            {
                name = Path.GetFileNameWithoutExtension(path),
                indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0, true);

            bool validNormals = true;
            for (int i = 0; i < normals.Count; i++)
            {
                if (normals[i].sqrMagnitude <= 1e-8f)
                {
                    validNormals = false;
                    break;
                }
            }

            if (validNormals)
            {
                mesh.SetNormals(normals);
            }
            else
            {
                mesh.RecalculateNormals();
            }

            mesh.RecalculateBounds();
            return mesh;
        }

        static Material CreateFallbackMaterial()
        {
            Shader shader = RuntimeShaderResolver.FindSectionableShader();
            if (shader == null) return null;

            var material = new Material(shader);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", new Color(0.12f, 0.16f, 0.20f));
            if (material.HasProperty("_Color")) material.SetColor("_Color", new Color(0.12f, 0.16f, 0.20f));
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.85f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.35f);
            return material;
        }

        static float ParseFloat(string value)
        {
            if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float parsed))
            {
                return parsed;
            }
            return 0f;
        }

        static string GetExtension(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetExtension(path).ToLowerInvariant();
        }
    }
}
