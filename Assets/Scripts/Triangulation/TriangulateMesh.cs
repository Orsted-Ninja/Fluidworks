using UnityEngine;
using System.Collections.Generic;
using Unity.VisualScripting;
public class TriangulateMesh : MonoBehaviour
{

    public string objectNameOrPath;
    public Material triangleMaterial;
    void Start()
    {
        GameObject targetObject = GameObject.Find(objectNameOrPath);

        if (targetObject != null)
        {
            MeshFilter meshFilter = targetObject.GetComponent<MeshFilter>();
            if (meshFilter != null)
            {
                Mesh originalMesh = meshFilter.mesh;

                Mesh reTriangulatedMesh = CreateTriangleMesh(originalMesh);

                //for quads
                Mesh quadMesh = CreateQuadMesh(originalMesh);
                

                // Create a quad mesh
                //Mesh quadMesh = CreateQuadMesh();

                // Create a new GameObject to display the new mesh
                GameObject newMeshObject = new GameObject("NewMeshObject");
                newMeshObject.transform.position = targetObject.transform.position;
                newMeshObject.transform.rotation = targetObject.transform.rotation;
                newMeshObject.transform.localScale = targetObject.transform.localScale;

                MeshFilter newMeshFilter = newMeshObject.AddComponent<MeshFilter>();
                newMeshFilter.mesh = reTriangulatedMesh;

                // for quads
                //newMeshFilter.mesh = quadMesh;

                MeshRenderer newMeshRenderer = newMeshObject.AddComponent<MeshRenderer>();
                if (triangleMaterial != null)
                {
                    newMeshRenderer.material = triangleMaterial;
                }
                else
                {
                    Shader s = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Hidden/InternalErrorShader");
                    newMeshRenderer.material = new Material(s);
                }

                Destroy(targetObject);

                Debug.Log("New mesh has been created. Original object destroyed.");
            }
            else
            {
                Debug.LogError("No MeshFilter found on the target object.");
            }
        }
        else
        {
            Debug.LogError("Target object not found. Please check the name or path.");
        }
    }

    //--- Not used (make every triangle a game object)
    void CreateTriangleObjects(Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        Vector2[] uvs = mesh.uv;
        Vector3[] normals = mesh.normals;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            GameObject triangleObject = new GameObject("Triangle " + (i / 3));

            
            triangleObject.transform.position = transform.position;
            triangleObject.transform.rotation = transform.rotation;
            triangleObject.transform.localScale = transform.localScale;

            MeshFilter mf = triangleObject.AddComponent<MeshFilter>();
            MeshRenderer mr = triangleObject.AddComponent<MeshRenderer>();
            if (triangleMaterial != null)
            {
                mr.material = triangleMaterial;
            }
            else
            {
                // Fallback in case no material is assigned in the Inspector
                Shader s = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Hidden/InternalErrorShader");
                mr.material = new Material(s);
                mr.material.color = Color.red;
                Debug.Log("Fallback");
            }

            //mr.material = new Material(Shader.Find("Standard")); // You can assign any material here

            Mesh triangleMesh = new Mesh();
            Vector3[] triangleVertices = new Vector3[3];
            int[] triangleIndices = new int[3];

            triangleVertices[0] = vertices[triangles[i]];
            triangleVertices[1] = vertices[triangles[i + 1]];
            triangleVertices[2] = vertices[triangles[i + 2]];

            triangleIndices[0] = 0;
            triangleIndices[1] = 1;
            triangleIndices[2] = 2;

            triangleMesh.vertices = triangleVertices;
            triangleMesh.triangles = triangleIndices;

            if (uvs != null && uvs.Length > 0)
            {
                Vector2[] triangleUVs = new Vector2[3];
                triangleUVs[0] = uvs[triangles[i]];
                triangleUVs[1] = uvs[triangles[i + 1]];
                triangleUVs[2] = uvs[triangles[i + 2]];
                triangleMesh.uv = triangleUVs;
            }

            if (normals != null && normals.Length > 0)
            {
                Vector3[] triangleNormals = new Vector3[3];
                triangleNormals[0] = normals[triangles[i]];
                triangleNormals[1] = normals[triangles[i + 1]];
                triangleNormals[2] = normals[triangles[i + 2]];
                triangleMesh.normals = triangleNormals;
            }

            triangleMesh.RecalculateNormals();
            triangleMesh.RecalculateBounds();

            mf.mesh = triangleMesh;
        }
    }

    Mesh CreateTriangleMesh(Mesh originalMesh)
    {
        Vector3[] originalVertices = originalMesh.vertices;
        int[] originalTriangles = originalMesh.triangles;
        Vector2[] originalUVs = originalMesh.uv;
        Vector3[] originalNormals = originalMesh.normals;

        // New mesh data
        List<Vector3> newVertices = new List<Vector3>();
        List<int> newTriangles = new List<int>();
        List<Vector2> newUVs = new List<Vector2>();
        List<Color> newColors = new List<Color>();
        List<Vector3> newNormals = new List<Vector3>();

        for (int i = 0; i < originalTriangles.Length; i += 3)
        {
            // Duplicate vertices for each triangle
            Vector3 vertex1 = originalVertices[originalTriangles[i]];
            Vector3 vertex2 = originalVertices[originalTriangles[i + 1]];
            Vector3 vertex3 = originalVertices[originalTriangles[i + 2]];

            newVertices.Add(vertex1);
            newVertices.Add(vertex2);
            newVertices.Add(vertex3);

            // Add indices for new triangles
            int baseIndex = newVertices.Count - 3;
            newTriangles.Add(baseIndex);
            newTriangles.Add(baseIndex + 1);
            newTriangles.Add(baseIndex + 2);

            // Add UVs
            if (originalUVs != null && originalUVs.Length > 0)
            {
                newUVs.Add(originalUVs[originalTriangles[i]]);
                newUVs.Add(originalUVs[originalTriangles[i + 1]]);
                newUVs.Add(originalUVs[originalTriangles[i + 2]]);
            }

            // Add normals
            if (originalNormals != null && originalNormals.Length > 0)
            {
                newNormals.Add(originalNormals[originalTriangles[i]]);
                newNormals.Add(originalNormals[originalTriangles[i + 1]]);
                newNormals.Add(originalNormals[originalTriangles[i + 2]]);
            }

            // Assign a distinct color to each triangle
            Color color = Random.ColorHSV();
            newColors.Add(color);
            newColors.Add(color);
            newColors.Add(color);
        }

        Mesh newMesh = new Mesh();
        newMesh.vertices = newVertices.ToArray();
        newMesh.triangles = newTriangles.ToArray();
        newMesh.uv = newUVs.ToArray();
        newMesh.normals = newNormals.ToArray();
        newMesh.colors = newColors.ToArray();

        newMesh.RecalculateNormals();
        newMesh.RecalculateBounds();

        return newMesh;
    }

    // --- for making quads out of the original triangles (for testing)
    Mesh CreateQuadMesh(Mesh originalMesh)
{
    Vector3[] originalVertices = originalMesh.vertices;
    int[] originalTriangles = originalMesh.triangles;
    Vector2[] originalUVs = originalMesh.uv;
    Vector3[] originalNormals = originalMesh.normals;

    // New mesh data
    List<Vector3> newVertices = new List<Vector3>();
    List<int> newTriangles = new List<int>();
    List<Vector2> newUVs = new List<Vector2>();
    List<Color> newColors = new List<Color>();
    List<Vector3> newNormals = new List<Vector3>();

    for (int i = 0; i < originalTriangles.Length; i += 6) // Process two triangles (forming a quad) at a time
    {
        // Duplicate vertices for each quad
        Vector3 vertex1 = originalVertices[originalTriangles[i]];
        Vector3 vertex2 = originalVertices[originalTriangles[i + 1]];
        Vector3 vertex3 = originalVertices[originalTriangles[i + 2]];
        Vector3 vertex4 = originalVertices[originalTriangles[i + 3]]; // Second triangle's third vertex

        newVertices.Add(vertex1);
        newVertices.Add(vertex2);
        newVertices.Add(vertex3);
        newVertices.Add(vertex4);

        // Add indices for new quad
        int baseIndex = newVertices.Count - 4;
        newTriangles.Add(baseIndex);
        newTriangles.Add(baseIndex + 1);
        newTriangles.Add(baseIndex + 2);
        newTriangles.Add(baseIndex);
        newTriangles.Add(baseIndex + 2);
        newTriangles.Add(baseIndex + 3);

        // Add UVs
        if (originalUVs != null && originalUVs.Length > 0)
        {
            newUVs.Add(originalUVs[originalTriangles[i]]);
            newUVs.Add(originalUVs[originalTriangles[i + 1]]);
            newUVs.Add(originalUVs[originalTriangles[i + 2]]);
            newUVs.Add(originalUVs[originalTriangles[i + 3]]);
        }

        // Add normals
        if (originalNormals != null && originalNormals.Length > 0)
        {
            newNormals.Add(originalNormals[originalTriangles[i]]);
            newNormals.Add(originalNormals[originalTriangles[i + 1]]);
            newNormals.Add(originalNormals[originalTriangles[i + 2]]);
            newNormals.Add(originalNormals[originalTriangles[i + 3]]);
        }

        // Assign a distinct color to each quad
        Color color = Random.ColorHSV();
        newColors.Add(color);
        newColors.Add(color);
        newColors.Add(color);
        newColors.Add(color);
    }

    Mesh newMesh = new Mesh();
    newMesh.vertices = newVertices.ToArray();
    newMesh.triangles = newTriangles.ToArray();
    newMesh.uv = newUVs.ToArray();
    newMesh.normals = newNormals.ToArray();
    newMesh.colors = newColors.ToArray();

    newMesh.RecalculateNormals();
    newMesh.RecalculateBounds();

    return newMesh;
}


/*
    Mesh Triangulate(Mesh mesh)
    {
        Mesh newMesh = new Mesh();

    Vector3[] vertices = mesh.vertices;
    int[] triangles = mesh.triangles;
    Vector2[] uvs = mesh.uv;
    Vector3[] normals = mesh.normals;

    // Since the mesh is already triangulated, we just copy the triangles
    int[] newTriangles = new int[triangles.Length];
    for (int i = 0; i < triangles.Length; i++)
    {
        Debug.Log("triangles:" + triangles[i]);
        newTriangles[i] = triangles[i];
    }

    newMesh.vertices = vertices;
    newMesh.triangles = newTriangles;
    newMesh.uv = uvs;
    newMesh.normals = normals;

    // Clear vertex and edge data
    newMesh.uv = null;
    newMesh.colors = null;

    // Recalculate normals and bounds
    newMesh.RecalculateNormals();
    newMesh.RecalculateBounds();

    return newMesh;
    }
    */

/*
    bool CompareMeshes(Mesh mesh1, Mesh mesh2)
    {
        if (mesh1.vertices.Length != mesh2.vertices.Length || mesh1.triangles.Length != mesh2.triangles.Length)
            return false;

        for (int i = 0; i < mesh1.vertices.Length; i++)
        {
            if (mesh1.vertices[i] != mesh2.vertices[i])
                return false;
        }

        for (int i = 0; i < mesh1.triangles.Length; i++)
        {
            if (mesh1.triangles[i] != mesh2.triangles[i])
                return false;
        }

        return true;
    }


    Mesh DeTriangulate(Mesh mesh)
    {
        Mesh newMesh = new Mesh();
        List<Vector3> vertices = new List<Vector3>(mesh.vertices);
        List<int> newTriangles = new List<int>();

        Debug.Log("Vertices:");
            foreach (Vector3 vertex in vertices)
            {
                Debug.Log(vertex);
            }

        // Naive approach: Convert every two triangles into a quad if possible (ignoring edge cases)
        for (int i = 0; i < mesh.triangles.Length; i += 6)
        {
            if (i + 5 < mesh.triangles.Length)
            {
                newTriangles.Add(mesh.triangles[i]);
                newTriangles.Add(mesh.triangles[i + 1]);
                newTriangles.Add(mesh.triangles[i + 2]);
                newTriangles.Add(mesh.triangles[i + 3]);
            }
        }

        newMesh.vertices = vertices.ToArray();
        newMesh.triangles = newTriangles.ToArray();

        newMesh.RecalculateNormals();
        newMesh.RecalculateBounds();

        return newMesh;
    }
*/
    
}