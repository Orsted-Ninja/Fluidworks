using UnityEngine;

public class MeshInfo : MonoBehaviour
{
    public float moveUpDistance = 100.0f;
    void Start()
    {

        MeshFilter meshFilter = GetComponent<MeshFilter>();
        
        if (meshFilter != null)
        {
            Mesh mesh = meshFilter.mesh;

            int[] triangles = mesh.triangles;
            Vector3[] vertices = mesh.vertices;

            for (int i = 0; i < triangles.Length; i += 3)
            {

                Vector3 vertex1 = mesh.vertices[triangles[i]];
                Vector3 vertex2 = mesh.vertices[triangles[i + 1]];
                Vector3 vertex3 = mesh.vertices[triangles[i + 2]];

                Debug.Log("Triangle " + i / 3 + ":");
                Debug.Log("Vertex 1: " + vertex1);
                Debug.Log("Vertex 2: " + vertex2);
                Debug.Log("Vertex 3: " + vertex3);

                int vertexIndex1 = triangles[i];
                int vertexIndex2 = triangles[i + 1];
                int vertexIndex3 = triangles[i + 2];

                vertices[vertexIndex1] += Vector3.up * moveUpDistance;
                vertices[vertexIndex1] += Vector3.right * moveUpDistance;
                vertices[vertexIndex2] += Vector3.up * moveUpDistance;
                vertices[vertexIndex2] += Vector3.right * moveUpDistance;
                vertices[vertexIndex3] += Vector3.up * moveUpDistance;
                vertices[vertexIndex3] += Vector3.right * moveUpDistance;

            mesh.vertices = vertices;

            // Recalculate bounds and normals for the mesh to update the changes
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
                
            }
        }
        else
        {
            Debug.LogError("MeshFilter component not found!");
        }
    }
}