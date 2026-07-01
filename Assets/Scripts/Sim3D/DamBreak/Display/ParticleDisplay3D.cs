using UnityEngine;

public class ParticleDisplay3D : MonoBehaviour
{

    public Shader shader;
    public float scale;
    Mesh mesh;
    public Color col;
    Material mat;

    ComputeBuffer argsBuffer;
    Bounds bounds;

    public Gradient colourMap;
    public int gradientResolution;
    public float velocityDisplayMax;
    Texture2D gradientTexture;
    bool needsUpdate;

    public int meshResolution;
    public int debug_MeshTriCount;
    Simulation3D simRef;

    [Range(0, 1)] public float particleAlpha = 0.95f;

    public void SetAlpha(float alpha)
    {
        particleAlpha = Mathf.Clamp01(alpha);
        if (colourMap != null)
        {
            var alphaKeys = colourMap.alphaKeys;
            for (int i = 0; i < alphaKeys.Length; i++)
            {
                alphaKeys[i].alpha = particleAlpha;
            }
            colourMap.alphaKeys = alphaKeys;
            needsUpdate = true;
        }
    }

    public void Init(Simulation3D sim)
    {
        ReleaseGraphicsResources();
        simRef = sim;
        if (shader == null) shader = Shader.Find("Instanced/Particle3D");
        if (shader == null)
        {
            Debug.LogError("[DamBreak] Particle shader not found: Instanced/Particle3D");
            enabled = false;
            return;
        }
        if (scale <= 0.001f) scale = 0.065f;
        if (velocityDisplayMax <= 0.001f) velocityDisplayMax = 20f;
        if (gradientResolution < 16) gradientResolution = 256;
        if (meshResolution < 1) meshResolution = 2;
        if (col.a <= 0.001f) col = new Color(0.85f, 0.95f, 1f, 1f);
        if (colourMap == null)
        {
            colourMap = new Gradient();
            colourMap.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.18f, 0.55f, 1.00f), 0f),
                    new GradientColorKey(new Color(0.00f, 0.95f, 0.95f), 0.5f),
                    new GradientColorKey(new Color(0.95f, 0.95f, 1.00f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.9f, 0f),
                    new GradientAlphaKey(1f, 1f)
                }
            );
        }

        mat = new Material(shader);
        mat.SetBuffer("Positions", sim.positionBuffer);
        mat.SetBuffer("Velocities", sim.velocityBuffer);
        mat.enableInstancing = true;

        mesh = SebStuff.SphereGenerator.GenerateSphereMesh(meshResolution);
        debug_MeshTriCount = mesh.triangles.Length / 3;
        argsBuffer = ComputeHelper.CreateArgsBuffer(mesh, sim.positionBuffer.count);
        bounds = new Bounds(Vector3.zero, Vector3.one * 10000);
        needsUpdate = true;
    }

    void LateUpdate()
    {
        if (mat == null || mesh == null || argsBuffer == null) return;
        
        UpdateSettings();
        Graphics.DrawMeshInstancedIndirect(mesh, 0, mat, bounds, argsBuffer);
    }

    void UpdateSettings()
    {
        if (simRef != null)
        {
            bounds = new Bounds(simRef.transform.position, simRef.transform.localScale * 2.5f);
        }
        if (needsUpdate)
        {
            needsUpdate = false;
            TextureFromGradient(ref gradientTexture, gradientResolution, colourMap);
            mat.SetTexture("ColourMap", gradientTexture);
        }
        mat.SetFloat("scale", scale);
        mat.SetColor("colour", col);
        mat.SetFloat("velocityMax", velocityDisplayMax);

        Vector3 s = transform.localScale;
        transform.localScale = Vector3.one;
        var localToWorld = transform.localToWorldMatrix;
        transform.localScale = s;

        mat.SetMatrix("localToWorld", localToWorld);
    }

    private void OnValidate()
    {
        needsUpdate = true;
    }

    public void ReleaseBuffers()
    {
        ReleaseGraphicsResources();
    }

    void OnDestroy()
    {
        ReleaseGraphicsResources();
    }

    void ReleaseGraphicsResources()
    {
        ComputeHelper.Release(argsBuffer);
        argsBuffer = null;
        if (mat != null)
        {
            if (Application.isPlaying) Object.Destroy(mat);
            else Object.DestroyImmediate(mat);
            mat = null;
        }
        if (gradientTexture != null)
        {
            if (Application.isPlaying) Object.Destroy(gradientTexture);
            else Object.DestroyImmediate(gradientTexture);
            gradientTexture = null;
        }
    }

    private static void TextureFromGradient(ref Texture2D texture, int width, Gradient gradient, FilterMode filterMode = FilterMode.Bilinear)
    {
        width = Mathf.Max(2, width);
        if (texture == null)
        {
            texture = new Texture2D(width, 1);
        }
        else if (texture.width != width)
        {
            texture.Reinitialize(width, 1);
        }
        if (gradient == null)
        {
            gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(Color.black, 0), new GradientColorKey(Color.black, 1) },
                new GradientAlphaKey[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) }
            );
        }
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = filterMode;

        Color[] cols = new Color[width];
        for (int i = 0; i < cols.Length; i++)
        {
            float t = cols.Length > 1 ? i / (cols.Length - 1f) : 0f;
            cols[i] = gradient.Evaluate(t);
        }
        texture.SetPixels(cols);
        texture.Apply();
    }
}
