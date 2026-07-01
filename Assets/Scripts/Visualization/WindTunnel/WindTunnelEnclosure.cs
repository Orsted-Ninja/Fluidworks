using UnityEngine;
using AeroFlow.Rendering;

namespace AeroFlow.Visualization
{
    [ExecuteAlways]
    public class WindTunnelEnclosure : MonoBehaviour
    {
        [Header("Frame")]
        [Range(0.002f, 0.06f)] public float edgeWidthFraction = 0.005f;
        public Color edgeTint = new Color(0.12f, 0.28f, 0.38f, 1f);
        [Range(0f, 2f)] public float edgeBrightness = 0.65f;

        [Header("Surfaces")]
        public bool showGlass = true;
        public Color glassTint = new Color(0.08f, 0.18f, 0.28f, 0.025f);
        public Color inletTint = new Color(0.10f, 0.55f, 0.85f, 0.10f);
        public Color outletTint = new Color(0.90f, 0.38f, 0.15f, 0.08f);
        [Range(0.02f, 0.25f)] public float portalInsetFraction = 0.10f;
        [Range(0.2f, 1.2f)] public float inletPanelWidthScale = 1.0f;
        [Range(0.2f, 1.2f)] public float inletPanelHeightScale = 1.0f;
        [Range(0.2f, 1.2f)] public float outletPanelWidthScale = 1.0f;
        [Range(0.2f, 1.2f)] public float outletPanelHeightScale = 1.0f;
        [Range(0.4f, 1.0f)] public float enclosureLengthScale = 0.82f;
        public bool showFloor = true;
        public Color floorTint = new Color(0.05f, 0.07f, 0.10f, 1f);
        public Color guideTint = new Color(0.08f, 0.35f, 0.50f, 0.06f);
        [Range(0.4f, 1.2f)] public float floorLengthScale = 1.0f;
        [Range(0.001f, 0.05f)] public float floorClearance = 0.012f;

        GameObject root;
        Material edgeMaterial;
        Material glassMaterial;
        Material inletMaterial;
        Material outletMaterial;
        Material floorMaterial;
        Material guideMaterial;
        bool dirty = true;

        void OnEnable() { dirty = true; }
        void OnValidate() { dirty = true; }
        public void MarkDirty() { dirty = true; }

        void LateUpdate()
        {
            if (dirty)
            {
                Rebuild();
                dirty = false;
            }
        }

        void OnDisable()
        {
            DestroyRoot();
        }

        public void Rebuild()
        {
            if (this == null) return;
            Debug.Log("[Enclosure] Rebuilding wind tunnel enclosure...");
            DestroyRoot(); // Changed from Cleanup() to DestroyRoot() to match existing method
            EnsureMaterials();

            // The following block was part of the provided snippet but conflicts with existing logic.
            // It is commented out to maintain the original Rebuild method's structure and functionality.
            // var sim = GetComponent<AeroFlow.Sim3D.WindTunnel.WindTunnelSimulation3D>();
            // if (sim == null)
            // {
            //     Debug.LogError("[Enclosure] FAILED: WindTunnelSimulation3D component not found on object!");
            //     return;
            // }
            // Vector3 size = sim.transform.localScale;
            // Debug.Log($"[Enclosure] Simulation bounds size: {size}");

            root = new GameObject("WindTunnelPresentation") { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(this.transform, false); // Changed from transform to this.transform
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            Vector3 size = GetLocalSize();
            ResolveLocalFrame(out Vector3 flowAxis, out Vector3 sideAxis, out Vector3 upAxis);
            float length = ProjectSize(size, flowAxis);
            float width = ProjectSize(size, sideAxis);
            float height = ProjectSize(size, upAxis);
            length *= enclosureLengthScale;

            if (TryGetFloorDisplayLength(flowAxis, out float floorLength))
            {
                length = Mathf.Clamp(floorLength * 1.02f, floorLength, length);
            }

            float edge = edgeWidthFraction * Mathf.Max(length, Mathf.Max(width, height));

            float halfLength = length * 0.5f;
            float halfWidth = width * 0.5f;
            float halfHeight = height * 0.5f;
            float verticalCenterOffset = 0f;

            if (TryGetFloorDisplaySurfaceCoordinate(upAxis, out float floorSurfaceCoordinate))
            {
                float ceilingCoordinate = halfHeight;
                float adjustedHeight = ceilingCoordinate - floorSurfaceCoordinate;
                if (adjustedHeight > 0.1f)
                {
                    halfHeight = adjustedHeight * 0.5f;
                    verticalCenterOffset = floorSurfaceCoordinate + halfHeight;
                }
            }

            root.transform.localPosition = upAxis * verticalCenterOffset;

            BuildFrame(flowAxis, sideAxis, upAxis, halfLength, halfWidth, halfHeight, edge);

            if (showGlass)
            {
                BuildGlass(flowAxis, sideAxis, upAxis, length, width, height, halfLength, halfWidth, halfHeight);
            }

            if (showFloor)
            {
                float portalInset = Mathf.Clamp(length * portalInsetFraction, length * 0.02f, length * 0.25f);
                float testSectionLength = Mathf.Clamp((length - portalInset * 2f) * floorLengthScale, length * 0.20f, length * 1.20f);
                float floorOffset = floorClearance;
                BuildFloor(flowAxis, sideAxis, upAxis, testSectionLength, width, halfHeight, floorOffset);
                BuildGuides(flowAxis, sideAxis, upAxis, testSectionLength, width, halfHeight, floorOffset);
            }
        }

        void BuildFrame(Vector3 flowAxis, Vector3 sideAxis, Vector3 upAxis, float halfLength, float halfWidth, float halfHeight, float edge)
        {
            for (int lengthSign = -1; lengthSign <= 1; lengthSign += 2)
            {
                for (int heightSign = -1; heightSign <= 1; heightSign += 2)
                {
                    Vector3 pos = flowAxis * (halfLength * lengthSign) + upAxis * (halfHeight * heightSign);
                    CreateBar("FrameSide", pos, sideAxis, halfWidth * 2f, edge);
                }

                for (int widthSign = -1; widthSign <= 1; widthSign += 2)
                {
                    Vector3 pos = flowAxis * (halfLength * lengthSign) + sideAxis * (halfWidth * widthSign);
                    CreateBar("FrameHeight", pos, upAxis, halfHeight * 2f, edge);
                }
            }

            for (int widthSign = -1; widthSign <= 1; widthSign += 2)
            {
                for (int heightSign = -1; heightSign <= 1; heightSign += 2)
                {
                    Vector3 pos = sideAxis * (halfWidth * widthSign) + upAxis * (halfHeight * heightSign);
                    CreateBar("FrameFlow", pos, flowAxis, halfLength * 2f, edge);
                }
            }
        }

        void BuildGlass(Vector3 flowAxis, Vector3 sideAxis, Vector3 upAxis, float length, float width, float height, float halfLength, float halfWidth, float halfHeight)
        {
            float portalInset = Mathf.Clamp(length * portalInsetFraction, length * 0.02f, length * 0.25f);
            CreateQuad("Ceiling", upAxis * halfHeight, upAxis, flowAxis, width, length, glassMaterial);
            CreateQuad("GlassLeft", sideAxis * -halfWidth, -sideAxis, upAxis, length, height, glassMaterial);
            CreateQuad("GlassRight", sideAxis * halfWidth, sideAxis, upAxis, length, height, glassMaterial);
            CreateQuad(
                "InletPlane",
                flowAxis * (-halfLength + portalInset),
                -flowAxis,
                upAxis,
                Mathf.Clamp(width * inletPanelWidthScale, width * 0.2f, width * 1.2f),
                Mathf.Clamp(height * inletPanelHeightScale, height * 0.2f, height * 1.2f),
                inletMaterial);
            CreateQuad(
                "OutletPlane",
                flowAxis * (halfLength - portalInset),
                flowAxis,
                upAxis,
                Mathf.Clamp(width * outletPanelWidthScale, width * 0.2f, width * 1.2f),
                Mathf.Clamp(height * outletPanelHeightScale, height * 0.2f, height * 1.2f),
                outletMaterial);
        }

        void BuildFloor(Vector3 flowAxis, Vector3 sideAxis, Vector3 upAxis, float length, float width, float halfHeight, float floorOffset)
        {
            CreateQuad("Floor", upAxis * (-halfHeight - floorOffset), -upAxis, flowAxis, width, length, floorMaterial);
        }

        void BuildGuides(Vector3 flowAxis, Vector3 sideAxis, Vector3 upAxis, float length, float width, float halfHeight, float floorOffset)
        {
            float[] offsets = { -0.28f, 0.28f };
            float thickness = Mathf.Max(0.02f, width * 0.012f);
            for (int i = 0; i < offsets.Length; i++)
            {
                Vector3 localPos = sideAxis * (width * offsets[i] * 0.5f) + upAxis * (-halfHeight - floorOffset + thickness * 0.5f);
                CreateGuideBar(localPos, flowAxis, length * 0.92f, thickness);
            }
        }

        void CreateBar(string name, Vector3 localCenter, Vector3 localAxis, float length, float edge)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = localCenter;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.right, localAxis.normalized);
            go.transform.localScale = new Vector3(length, edge, edge);
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = edgeMaterial;
            var collider = go.GetComponent<Collider>();
            if (collider != null) DestroyImmediate(collider);
        }

        void CreateQuad(string name, Vector3 localCenter, Vector3 normalAxis, Vector3 planeUp, float width, float height, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = localCenter;
            go.transform.localRotation = Quaternion.LookRotation(normalAxis.normalized, planeUp.normalized);
            go.transform.localScale = new Vector3(width, height, 1f);
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = material;
            var collider = go.GetComponent<Collider>();
            if (collider != null) DestroyImmediate(collider);
        }

        void CreateGuideBar(Vector3 localCenter, Vector3 localAxis, float length, float thickness)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Guide";
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = localCenter;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.right, localAxis.normalized);
            go.transform.localScale = new Vector3(length, thickness, thickness * 0.5f);
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = guideMaterial;
            var collider = go.GetComponent<Collider>();
            if (collider != null) DestroyImmediate(collider);
        }

        void EnsureMaterials()
        {
            edgeMaterial = CreateUnlitMaterial(ScaleColor(edgeTint, edgeBrightness));
            glassMaterial = CreateTransparentMaterial(glassTint, 0.12f);
            inletMaterial = CreatePortalMaterial(inletTint, new Color(0.22f, 0.72f, 0.95f, 0.22f));
            outletMaterial = CreatePortalMaterial(outletTint, new Color(0.95f, 0.55f, 0.25f, 0.18f));
            floorMaterial = CreateFloorMaterial();
            guideMaterial = CreateTransparentMaterial(guideTint, 0f);
        }

        Material CreateUnlitMaterial(Color color)
        {
            var shader = RuntimeShaderResolver.FindSimpleUnlitShader();
            if (shader == null) return new Material(Shader.Find("Hidden/InternalErrorShader"));
            var material = new Material(shader) { hideFlags = HideFlags.DontSave };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            return material;
        }

        Material CreateTransparentMaterial(Color color, float smoothness)
        {
            var shader = RuntimeShaderResolver.FindLitShader();
            if (shader == null) return new Material(Shader.Find("Hidden/InternalErrorShader"));
            var material = new Material(shader) { hideFlags = HideFlags.DontSave };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return material;
        }

        Material CreatePortalMaterial(Color color, Color pixelTint)
        {
            Material material = CreateTransparentMaterial(color, 0.02f);
            Texture2D portalTexture = GenerateGridTexture(
                256,
                256,
                12,
                new Color(color.r * 0.40f, color.g * 0.40f, color.b * 0.40f, color.a * 0.55f),
                pixelTint);
            material.mainTexture = portalTexture;
            material.mainTextureScale = Vector2.one;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", portalTexture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", portalTexture);
            return material;
        }


        Material CreateFloorMaterial()
        {
            var shader = RuntimeShaderResolver.FindLitShader();
            if (shader == null) return new Material(Shader.Find("Hidden/InternalErrorShader"));
            var material = new Material(shader) { hideFlags = HideFlags.DontSave };
            Texture2D grid = GenerateGridTexture(512, 1024, 48, floorTint, new Color(0.10f, 0.18f, 0.25f, 1f));
            material.mainTexture = grid;
            material.mainTextureScale = new Vector2(1f, 2f);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.62f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.22f);
            return material;
        }

        Texture2D GenerateGridTexture(int width, int height, int cellSize, Color baseColor, Color lineColor)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool majorLine = (x % (cellSize * 4) == 0) || (y % (cellSize * 4) == 0);
                    bool line = (x % cellSize == 0) || (y % cellSize == 0);
                    Color color = majorLine ? ScaleColor(lineColor, 1.4f) : line ? lineColor : baseColor;
                    texture.SetPixel(x, y, color);
                }
            }

            texture.Apply();
            return texture;
        }

        Vector3 GetLocalSize()
        {
            if (TryGetSolverLocalBounds(out Vector3 localCenter, out Vector3 localSize))
            {
                return localSize;
            }

            Vector3 scale = transform.localScale;
            return new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        }

        bool TryGetSolverLocalBounds(out Vector3 localCenter, out Vector3 localSize)
        {
            localCenter = Vector3.zero;
            localSize = Vector3.zero;

            var wind = GetComponent<WindTunnelSimulation3D>();
            if (wind == null || wind.navierStokesSolver == null)
            {
                return false;
            }

            Vector3 solverSizeWorld = wind.navierStokesSolver.BoundsSize;
            if (solverSizeWorld.x <= 1e-5f || solverSizeWorld.y <= 1e-5f || solverSizeWorld.z <= 1e-5f)
            {
                return false;
            }

            Vector3 solverCenterWorld = wind.navierStokesSolver.BoundsCenter;
            localCenter = transform.InverseTransformPoint(solverCenterWorld);

            Vector3 localX = transform.InverseTransformVector(new Vector3(solverSizeWorld.x, 0f, 0f));
            Vector3 localY = transform.InverseTransformVector(new Vector3(0f, solverSizeWorld.y, 0f));
            Vector3 localZ = transform.InverseTransformVector(new Vector3(0f, 0f, solverSizeWorld.z));
            localSize = new Vector3(
                Mathf.Abs(localX.x) + Mathf.Abs(localY.x) + Mathf.Abs(localZ.x),
                Mathf.Abs(localX.y) + Mathf.Abs(localY.y) + Mathf.Abs(localZ.y),
                Mathf.Abs(localX.z) + Mathf.Abs(localY.z) + Mathf.Abs(localZ.z));
            return localSize.x > 1e-5f && localSize.y > 1e-5f && localSize.z > 1e-5f;
        }

        void ResolveLocalFrame(out Vector3 flowAxis, out Vector3 sideAxis, out Vector3 upAxis)
        {
            var wind = GetComponent<WindTunnelSimulation3D>();
            if (wind != null)
            {
                flowAxis = SnapToLocalAxis(transform.InverseTransformDirection(wind.ResolveTunnelLongAxis()), Vector3.right);
                upAxis = SnapToLocalAxis(transform.InverseTransformDirection(wind.ResolveTunnelVerticalAxis()), Vector3.up);
                sideAxis = Vector3.Cross(upAxis, flowAxis);
                if (sideAxis.sqrMagnitude < 0.5f)
                {
                    sideAxis = Vector3.Cross(Vector3.up, flowAxis);
                }
                sideAxis = SnapToLocalAxis(sideAxis, Vector3.forward);
                return;
            }

            Vector3 size = GetLocalSize();
            bool lengthOnZ = size.z >= size.x;
            flowAxis = lengthOnZ ? Vector3.forward : Vector3.right;
            sideAxis = lengthOnZ ? Vector3.right : Vector3.forward;
            upAxis = Vector3.up;
        }

        static Vector3 SnapToLocalAxis(Vector3 axis, Vector3 fallback)
        {
            if (axis.sqrMagnitude < 1e-6f)
            {
                return fallback;
            }

            axis.Normalize();
            float ax = Mathf.Abs(axis.x);
            float ay = Mathf.Abs(axis.y);
            float az = Mathf.Abs(axis.z);

            if (ax >= ay && ax >= az)
            {
                return new Vector3(Mathf.Sign(axis.x), 0f, 0f);
            }
            if (ay >= ax && ay >= az)
            {
                return new Vector3(0f, Mathf.Sign(axis.y), 0f);
            }
            return new Vector3(0f, 0f, Mathf.Sign(axis.z));
        }

        static float ProjectSize(Vector3 size, Vector3 axis)
        {
            Vector3 normalized = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.right;
            return Vector3.Dot(size, new Vector3(Mathf.Abs(normalized.x), Mathf.Abs(normalized.y), Mathf.Abs(normalized.z)));
        }

        bool TryGetFloorDisplaySurfaceCoordinate(Vector3 upAxis, out float floorSurfaceCoordinate)
        {
            floorSurfaceCoordinate = 0f;

            var wind = GetComponent<WindTunnelSimulation3D>();
            if (wind == null || wind.floorDisplay == null)
            {
                return false;
            }

            Renderer renderer = wind.floorDisplay.GetComponent<Renderer>();
            if (renderer == null)
            {
                floorSurfaceCoordinate = Vector3.Dot(wind.floorDisplay.localPosition, upAxis.normalized);
                return true;
            }

            Bounds localBounds = renderer.localBounds;
            Vector3 center = localBounds.center;
            Vector3 extents = localBounds.extents;
            float best = float.NegativeInfinity;

            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 localCorner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                        Vector3 worldCorner = wind.floorDisplay.TransformPoint(localCorner);
                        Vector3 enclosureLocalCorner = transform.InverseTransformPoint(worldCorner);
                        best = Mathf.Max(best, Vector3.Dot(enclosureLocalCorner, upAxis.normalized));
                    }
                }
            }

            if (float.IsNegativeInfinity(best))
            {
                return false;
            }

            floorSurfaceCoordinate = best;
            return true;
        }

        bool TryGetFloorDisplayLength(Vector3 flowAxis, out float floorLength)
        {
            floorLength = 0f;

            var wind = GetComponent<WindTunnelSimulation3D>();
            if (wind == null || wind.floorDisplay == null)
            {
                return false;
            }

            Renderer renderer = wind.floorDisplay.GetComponent<Renderer>();
            if (renderer == null)
            {
                return false;
            }

            Bounds localBounds = renderer.localBounds;
            Vector3 center = localBounds.center;
            Vector3 extents = localBounds.extents;
            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            Vector3 normalizedFlow = flowAxis.sqrMagnitude > 1e-6f ? flowAxis.normalized : Vector3.right;

            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 localCorner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                        Vector3 worldCorner = wind.floorDisplay.TransformPoint(localCorner);
                        Vector3 enclosureLocalCorner = transform.InverseTransformPoint(worldCorner);
                        float projected = Vector3.Dot(enclosureLocalCorner, normalizedFlow);
                        min = Mathf.Min(min, projected);
                        max = Mathf.Max(max, projected);
                    }
                }
            }

            if (float.IsInfinity(min) || float.IsInfinity(max))
            {
                return false;
            }

            floorLength = max - min;
            return floorLength > 0.01f;
        }

        static Color ScaleColor(Color color, float scale)
        {
            return new Color(color.r * scale, color.g * scale, color.b * scale, color.a);
        }

        void DestroyRoot()
        {
            if (root == null) return;
            if (Application.isPlaying) Destroy(root);
            else DestroyImmediate(root);
            root = null;
        }
    }
}
