using UnityEngine;
using UnityEngine.Rendering;
using AeroFlow.Core;
using AeroFlow.Rendering;

namespace AeroFlow.Visualization
{
    /// <summary>
    /// Renders engineering-style flow slices using solver samples on transparent
    /// planes. Velocity mode shows a longitudinal slice plus a wake cut plane.
    /// Pressure mode shows a pressure-coefficient slice near the model.
    /// </summary>
    public class FlowFieldSliceRenderer : MonoBehaviour
    {
        private enum SliceMetric
        {
            Velocity,
            Pressure
        }

        private sealed class SlicePlaneState
        {
            public string name;
            public GameObject gameObject;
            public MeshRenderer renderer;
            public Material material;
            public Texture2D texture;
            public Color32[] pixels;
        }

        [Header("References")]
        public WindTunnelSimulation3D windTunnel;
        public NavierStokesGridSolver navier;

        [Header("Sampling")]
        [Range(32, 192)] public int textureWidth = 96;
        [Range(24, 128)] public int textureHeight = 56;
        [Range(0.05f, 0.35f)] public float updateInterval = 0.12f;
        [Range(0.04f, 0.40f)] public float planeOpacity = 0.10f;

        [Header("Placement")]
        [Range(0.00f, 0.25f)] public float bodySliceOffsetFactor = 0.04f;
        [Range(0.10f, 3.00f)] public float wakePlaneDistanceFactor = 1.8f;

        [Header("Style")]
        public Gradient velocityGradient;
        public Gradient pressureGradient;

        private SlicePlaneState longitudinalPlane;
        private SlicePlaneState wakePlane;
        private float nextUpdateTime;
        private Bounds modelBoundsCache;
        private bool hasModelBounds;
        private float nextModelBoundsRefreshTime = -999f;
        private Mesh quadMesh;

        private void Awake()
        {
            if (windTunnel == null) windTunnel = FindAnyObjectByType<WindTunnelSimulation3D>();
            EnsureGradients();
            EnsureQuadMesh();
            EnsurePlanes();
        }

        private void OnEnable()
        {
            EnsurePlanes();
            SetVisible(false, false);
        }

        public void ApplyVisualizationMode(string mode)
        {
            string normalized = WindTunnelSimulation3D.NormalizeVisualizationMode(mode);
            bool showVelocity = string.Equals(normalized, WindTunnelSimulation3D.VisualizationVelocity, System.StringComparison.OrdinalIgnoreCase);
            bool showPressure = string.Equals(normalized, WindTunnelSimulation3D.VisualizationPressure, System.StringComparison.OrdinalIgnoreCase);
            SetVisible(showVelocity || showPressure, showVelocity);
        }

        private void OnDestroy()
        {
            ReleasePlane(longitudinalPlane);
            ReleasePlane(wakePlane);
        }

        private void Update()
        {
            if (windTunnel == null) windTunnel = FindAnyObjectByType<WindTunnelSimulation3D>();
            if (windTunnel == null)
            {
                SetVisible(false, false);
                return;
            }

            if (navier == null) navier = windTunnel.navierStokesSolver;

            string mode = windTunnel.settings.visualizationMode;
            bool velocityMode = string.Equals(mode, "Velocity", System.StringComparison.OrdinalIgnoreCase);
            bool pressureMode = string.Equals(mode, "Pressure", System.StringComparison.OrdinalIgnoreCase);
            if (!velocityMode && !pressureMode)
            {
                SetVisible(false, false);
                return;
            }

            if (Time.time < nextUpdateTime) return;
            nextUpdateTime = Time.time + Mathf.Max(0.05f, updateInterval);

            RefreshModelBounds();
            EnsureGradients();
            EnsurePlanes();
            if (pressureMode && !hasModelBounds)
            {
                SetVisible(false, false);
                return;
            }

            if (pressureMode)
            {
                UpdatePressureSlicePlane();
            }
            else
            {
                UpdateVelocitySlicePlanes();
            }
        }

        private void UpdateVelocitySlicePlanes()
        {
            Bounds tunnelBounds = windTunnel.GetTunnelBounds();
            if (tunnelBounds.size.x < 0.1f || tunnelBounds.size.y < 0.1f || tunnelBounds.size.z < 0.1f)
            {
                tunnelBounds = new Bounds(windTunnel.transform.position, new Vector3(10f, 4f, 5f));
            }

            Vector3 dir = windTunnel.ResolveWindDirection();
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.right;
            dir.Normalize();

            BuildFlowFrame(dir, out Vector3 side, out Vector3 up);
            float tunnelLength = Vector3.Dot(tunnelBounds.size, Abs(dir));
            float tunnelWidth = Vector3.Dot(tunnelBounds.size, Abs(side));
            float tunnelHeight = Vector3.Dot(tunnelBounds.size, Abs(up));

            ConfigureLongitudinalPlane(tunnelBounds, dir, side, up, tunnelLength, tunnelHeight, out float sliceLength, out float sliceHeight);
            SamplePlaneTexture(
                longitudinalPlane,
                SliceMetric.Velocity,
                (u, v) => longitudinalPlane.gameObject.transform.position
                        + dir * (u * sliceLength * 0.5f)
                        + up * (v * sliceHeight * 0.5f));

            ConfigureWakePlane(tunnelBounds, dir, side, up, tunnelWidth, tunnelHeight, out float wakeWidth, out float wakeHeight);
            SamplePlaneTexture(
                wakePlane,
                SliceMetric.Velocity,
                (u, v) => wakePlane.gameObject.transform.position
                        + side * (u * wakeWidth * 0.5f)
                        + up * (v * wakeHeight * 0.5f));

            SetVisible(true, true);
        }

        private void UpdatePressureSlicePlane()
        {
            Bounds tunnelBounds = windTunnel.GetTunnelBounds();
            if (tunnelBounds.size.x < 0.1f || tunnelBounds.size.y < 0.1f || tunnelBounds.size.z < 0.1f)
            {
                tunnelBounds = new Bounds(windTunnel.transform.position, new Vector3(10f, 4f, 5f));
            }

            Vector3 dir = windTunnel.ResolveWindDirection();
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.right;
            dir.Normalize();

            BuildFlowFrame(dir, out Vector3 side, out Vector3 up);
            float tunnelLength = Vector3.Dot(tunnelBounds.size, Abs(dir));
            float tunnelHeight = Vector3.Dot(tunnelBounds.size, Abs(up));

            ConfigurePressurePlane(tunnelBounds, dir, side, up, tunnelLength, tunnelHeight, out float sliceLength, out float sliceHeight);
            SamplePlaneTexture(
                longitudinalPlane,
                SliceMetric.Pressure,
                (u, v) => longitudinalPlane.gameObject.transform.position
                        + dir * (u * sliceLength * 0.5f)
                        + up * (v * sliceHeight * 0.5f));

            SetVisible(true, false);
        }

        private void ConfigureLongitudinalPlane(
            Bounds tunnelBounds,
            Vector3 dir,
            Vector3 side,
            Vector3 up,
            float tunnelLength,
            float tunnelHeight,
            out float sliceLength,
            out float sliceHeight)
        {
            Vector3 planePosition = tunnelBounds.center;
            if (hasModelBounds)
            {
                float bodyHalfSide = ProjectHalfExtent(modelBoundsCache, side);
                float sideOffset = bodyHalfSide + tunnelBounds.size.magnitude * Mathf.Max(0.015f, bodySliceOffsetFactor * 0.45f);
                float signedOffset = sideOffset;
                planePosition = modelBoundsCache.center + side * signedOffset;
            }

            longitudinalPlane.gameObject.transform.position = planePosition;
            longitudinalPlane.gameObject.transform.rotation = Quaternion.LookRotation(side, up);
            if (hasModelBounds)
            {
                float bodyHalfLength = ProjectHalfExtent(modelBoundsCache, dir);
                float bodyHalfHeight = ProjectHalfExtent(modelBoundsCache, up);
                sliceLength = Mathf.Clamp(bodyHalfLength * 3.2f, tunnelLength * 0.08f, tunnelLength * 0.20f);
                sliceHeight = Mathf.Clamp(bodyHalfHeight * 2.4f, tunnelHeight * 0.10f, tunnelHeight * 0.28f);
            }
            else
            {
                sliceLength = tunnelLength * 0.20f;
                sliceHeight = tunnelHeight * 0.24f;
            }
            longitudinalPlane.gameObject.transform.localScale = new Vector3(sliceLength, sliceHeight, 1f);
        }

        private void ConfigurePressurePlane(
            Bounds tunnelBounds,
            Vector3 dir,
            Vector3 side,
            Vector3 up,
            float tunnelLength,
            float tunnelHeight,
            out float sliceLength,
            out float sliceHeight)
        {
            float bodyHalfSide = hasModelBounds ? ProjectHalfExtent(modelBoundsCache, side) : tunnelBounds.extents.magnitude * 0.08f;
            float sideOffset = bodyHalfSide + tunnelBounds.extents.magnitude * Mathf.Max(0.012f, bodySliceOffsetFactor * 0.35f);
            Vector3 planePosition = hasModelBounds ? modelBoundsCache.center + side * sideOffset : tunnelBounds.center + side * sideOffset;

            float bodyLength = hasModelBounds ? ProjectHalfExtent(modelBoundsCache, dir) * 2.1f : tunnelLength * 0.16f;
            float bodyHeight = hasModelBounds ? ProjectHalfExtent(modelBoundsCache, up) * 1.8f : tunnelHeight * 0.18f;
            sliceLength = Mathf.Clamp(bodyLength, tunnelLength * 0.08f, tunnelLength * 0.18f);
            sliceHeight = Mathf.Clamp(bodyHeight, tunnelHeight * 0.10f, tunnelHeight * 0.24f);

            longitudinalPlane.gameObject.transform.position = planePosition;
            longitudinalPlane.gameObject.transform.rotation = Quaternion.LookRotation(side, up);
            longitudinalPlane.gameObject.transform.localScale = new Vector3(sliceLength, sliceHeight, 1f);
        }

        private void ConfigureWakePlane(
            Bounds tunnelBounds,
            Vector3 dir,
            Vector3 side,
            Vector3 up,
            float tunnelWidth,
            float tunnelHeight,
            out float wakeWidth,
            out float wakeHeight)
        {
            Vector3 planePosition = tunnelBounds.center + dir * (tunnelBounds.extents.magnitude * 0.12f);
            if (hasModelBounds)
            {
                float bodyHalfLength = ProjectHalfExtent(modelBoundsCache, dir);
                planePosition = modelBoundsCache.center + dir * (bodyHalfLength * Mathf.Clamp(wakePlaneDistanceFactor, 1.1f, 1.6f));
            }

            wakePlane.gameObject.transform.position = planePosition;
            wakePlane.gameObject.transform.rotation = Quaternion.LookRotation(dir, up);
            if (hasModelBounds)
            {
                float bodyHalfSide = ProjectHalfExtent(modelBoundsCache, side);
                float bodyHalfHeight = ProjectHalfExtent(modelBoundsCache, up);
                wakeWidth = Mathf.Clamp(bodyHalfSide * 2.3f, tunnelWidth * 0.08f, tunnelWidth * 0.22f);
                wakeHeight = Mathf.Clamp(bodyHalfHeight * 2.1f, tunnelHeight * 0.10f, tunnelHeight * 0.24f);
            }
            else
            {
                wakeWidth = tunnelWidth * 0.18f;
                wakeHeight = tunnelHeight * 0.20f;
            }
            wakePlane.gameObject.transform.localScale = new Vector3(wakeWidth, wakeHeight, 1f);
        }

        private void SamplePlaneTexture(SlicePlaneState plane, SliceMetric metric, System.Func<float, float, Vector3> samplePosition)
        {
            if (plane == null || plane.texture == null || plane.pixels == null || samplePosition == null)
            {
                return;
            }

            float vInf = Mathf.Max(0.1f, windTunnel.settings.inletVelocity);
            float rho = Mathf.Max(0.01f, windTunnel.settings.airDensity);
            float qInf = 0.5f * rho * vInf * vInf;
            Vector3 freestream = windTunnel.ResolveWindDirection() * vInf;
            bool rawSolverOnly = windTunnel.useRawSolverDataOnly;
            int width = plane.texture.width;
            int height = plane.texture.height;
            int index = 0;

            for (int y = 0; y < height; y++)
            {
                float v = height > 1 ? (y / (float)(height - 1)) * 2f - 1f : 0f;
                for (int x = 0; x < width; x++)
                {
                    float u = width > 1 ? (x / (float)(width - 1)) * 2f - 1f : 0f;
                    Vector3 worldPos = samplePosition(u, v);
                    Vector3 velocity = rawSolverOnly ? Vector3.zero : freestream;
                    float pressure = 0f;
                    bool sampled = navier != null && navier.TrySampleFlow(worldPos, out velocity, out pressure);

                    if (!float.IsFinite(velocity.x) || !float.IsFinite(velocity.y) || !float.IsFinite(velocity.z))
                    {
                        velocity = rawSolverOnly ? Vector3.zero : freestream;
                        sampled = false;
                    }
                    else if (sampled && !rawSolverOnly)
                    {
                        // Keep slices readable even when the simplified grid solve collapses
                        // locally toward zero away from the inlet.
                        velocity = velocity * 0.82f + freestream * 0.18f;
                    }
                    if (!float.IsFinite(pressure))
                    {
                        pressure = 0f;
                        sampled = false;
                    }

                    Color color;
                    if (metric == SliceMetric.Velocity)
                    {
                        if (rawSolverOnly && !sampled)
                        {
                            color = new Color(0.02f, 0.02f, 0.03f, planeOpacity * 0.10f);
                        }
                        else
                        {
                            float speedRatio = velocity.magnitude / Mathf.Max(vInf, 1e-4f);
                            float t = Mathf.InverseLerp(0.15f, 1.35f, speedRatio);
                            color = velocityGradient.Evaluate(Mathf.Clamp01(t));
                            color.a = planeOpacity * (sampled ? 0.42f : 0.20f);
                        }
                    }
                    else
                    {
                        if (rawSolverOnly && !sampled)
                        {
                            color = new Color(0.02f, 0.02f, 0.03f, planeOpacity * 0.10f);
                        }
                        else
                        {
                            float cp = qInf > 1e-5f ? pressure / qInf : 0f;
                            if (!sampled)
                            {
                                cp = 1f - Mathf.Pow(velocity.magnitude / Mathf.Max(vInf, 1e-4f), 2f);
                            }
                            float t = Mathf.InverseLerp(-1.5f, 1.0f, cp);
                            color = pressureGradient.Evaluate(Mathf.Clamp01(t));
                            color.a = planeOpacity * (sampled ? 0.28f : 0.14f);
                        }
                    }

                    if (hasModelBounds && modelBoundsCache.Contains(worldPos))
                    {
                        color.a = 0f;
                    }

                    plane.pixels[index++] = color;
                }
            }

            plane.texture.SetPixels32(plane.pixels);
            plane.texture.Apply(false, false);
        }

        private void RefreshModelBounds()
        {
            if (Time.time < nextModelBoundsRefreshTime) return;
            nextModelBoundsRefreshTime = Time.time + 0.25f;

            if (windTunnel != null && windTunnel.TryGetLoadedModelBounds(out Bounds bounds))
            {
                modelBoundsCache = bounds;
                hasModelBounds = true;
                return;
            }

            hasModelBounds = false;
            GameObject model = RuntimeModelLookup.GetLoadedModel();
            if (model == null) return;

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                if (renderer.GetComponentInParent<AeroFlow.Core.RuntimeSimulationProxy>() != null) continue;

                if (!hasModelBounds)
                {
                    modelBoundsCache = renderer.bounds;
                    hasModelBounds = true;
                }
                else
                {
                    modelBoundsCache.Encapsulate(renderer.bounds);
                }
            }
        }

        private void EnsurePlanes()
        {
            EnsurePlane(ref longitudinalPlane, "FlowSlice_Longitudinal");
            EnsurePlane(ref wakePlane, "FlowSlice_Wake");
        }

        private void EnsurePlane(ref SlicePlaneState state, string name)
        {
            if (state != null && state.gameObject != null && state.texture != null
                && state.texture.width == textureWidth && state.texture.height == textureHeight)
            {
                return;
            }

            state ??= new SlicePlaneState();
            state.name = name;

            GameObject go = state.gameObject;
            if (go == null)
            {
                Transform existing = transform.Find(name);
                go = existing != null ? existing.gameObject : new GameObject(name);
                go.transform.SetParent(transform, false);
                state.gameObject = go;
            }
            else
            {
                go.name = name;
            }

            if (quadMesh == null)
            {
                EnsureQuadMesh();
            }

            MeshFilter filter = go.GetComponent<MeshFilter>();
            if (filter == null)
            {
                filter = go.AddComponent<MeshFilter>();
            }
            if (filter == null)
            {
                Debug.LogWarning($"[FlowSlice] Recreating plane '{name}' because MeshFilter recovery failed.");
                Object.Destroy(go);
                go = new GameObject(name);
                go.transform.SetParent(transform, false);
                state.gameObject = go;
                filter = go.AddComponent<MeshFilter>();
            }
            filter.sharedMesh = quadMesh;

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                renderer = go.AddComponent<MeshRenderer>();
            }
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.sortingOrder = 12;

            if (state.material == null)
            {
                Shader shader = RuntimeShaderResolver.FindSliceShader();
                if (shader == null)
                {
                    Debug.LogError($"[FlowSlice] FAILED to create material for '{name}': Slice shader not found.");
                    state.material = new Material(Shader.Find("Hidden/InternalErrorShader"));
                }
                else
                {
                    state.material = new Material(shader);
                }
            }

            if (state.texture != null)
            {
                Object.Destroy(state.texture);
            }

            Texture2D texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            if (state.material.HasProperty("_BaseMap")) state.material.SetTexture("_BaseMap", texture);
            else if (state.material.HasProperty("_MainTex")) state.material.SetTexture("_MainTex", texture);
            if (state.material.HasProperty("_Tint")) state.material.SetColor("_Tint", Color.white);
            if (state.material.HasProperty("_Color")) state.material.SetColor("_Color", Color.white);
            state.material.renderQueue = (int)RenderQueue.Transparent + 20;

            renderer.sharedMaterial = state.material;
            go.SetActive(false);

            state.renderer = renderer;
            state.texture = texture;
            state.pixels = new Color32[textureWidth * textureHeight];
        }

        private void EnsureGradients()
        {
            if (velocityGradient == null)
            {
                velocityGradient = new Gradient();
                velocityGradient.mode = GradientMode.Blend;
                velocityGradient.SetKeys(
                    new[]
                    {
                        new GradientColorKey(new Color(0.02f, 0.08f, 0.55f), 0.00f),
                        new GradientColorKey(new Color(0.04f, 0.35f, 0.95f), 0.14f),
                        new GradientColorKey(new Color(0.06f, 0.72f, 0.98f), 0.28f),
                        new GradientColorKey(new Color(0.08f, 0.92f, 0.60f), 0.42f),
                        new GradientColorKey(new Color(0.40f, 0.98f, 0.22f), 0.55f),
                        new GradientColorKey(new Color(0.95f, 0.90f, 0.10f), 0.68f),
                        new GradientColorKey(new Color(1.00f, 0.50f, 0.05f), 0.82f),
                        new GradientColorKey(new Color(0.92f, 0.15f, 0.05f), 1.00f)
                    },
                    new[]
                    {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(1f, 1f)
                    });
            }

            if (pressureGradient == null)
            {
                pressureGradient = new Gradient();
                pressureGradient.mode = GradientMode.Blend;
                pressureGradient.SetKeys(
                    new[]
                    {
                        new GradientColorKey(new Color(0.02f, 0.08f, 0.60f), 0.00f),
                        new GradientColorKey(new Color(0.05f, 0.40f, 0.95f), 0.18f),
                        new GradientColorKey(new Color(0.08f, 0.78f, 0.95f), 0.35f),
                        new GradientColorKey(new Color(0.12f, 0.92f, 0.50f), 0.50f),
                        new GradientColorKey(new Color(0.92f, 0.90f, 0.15f), 0.65f),
                        new GradientColorKey(new Color(1.00f, 0.50f, 0.08f), 0.80f),
                        new GradientColorKey(new Color(0.90f, 0.12f, 0.06f), 1.00f)
                    },
                    new[]
                    {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(1f, 1f)
                    });
            }
        }

        private void EnsureQuadMesh()
        {
            if (quadMesh != null) return;

            quadMesh = new Mesh { name = "FlowSliceQuad" };
            quadMesh.SetVertices(new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f)
            });
            quadMesh.SetUVs(0, new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            });
            quadMesh.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0);
            quadMesh.RecalculateBounds();
        }

        private void SetVisible(bool showLongitudinal, bool showWake)
        {
            if (longitudinalPlane?.gameObject != null)
            {
                longitudinalPlane.gameObject.SetActive(showLongitudinal);
            }
            if (wakePlane?.gameObject != null)
            {
                wakePlane.gameObject.SetActive(showWake);
            }
        }

        private static void ReleasePlane(SlicePlaneState plane)
        {
            if (plane == null) return;
            if (plane.texture != null) Object.Destroy(plane.texture);
            if (plane.material != null) Object.Destroy(plane.material);
        }

        private static float ProjectHalfExtent(Bounds bounds, Vector3 axis)
        {
            return Vector3.Dot(bounds.extents, Abs(axis.normalized));
        }

        private void BuildFlowFrame(Vector3 dir, out Vector3 side, out Vector3 up)
        {
            Vector3 referenceUp = windTunnel != null ? windTunnel.ResolveTunnelVerticalAxis() : Vector3.up;
            if (Mathf.Abs(Vector3.Dot(referenceUp.normalized, dir.normalized)) > 0.92f)
            {
                referenceUp = windTunnel != null ? windTunnel.ResolveTunnelLongAxis() : Vector3.forward;
            }

            side = Vector3.Cross(referenceUp, dir).normalized;
            if (side.sqrMagnitude < 1e-6f)
            {
                side = Vector3.Cross(Vector3.up, dir).normalized;
            }
            if (side.sqrMagnitude < 1e-6f)
            {
                side = Vector3.right;
            }

            up = Vector3.Cross(dir, side).normalized;
            if (up.sqrMagnitude < 1e-6f)
            {
                up = windTunnel != null ? windTunnel.ResolveTunnelVerticalAxis() : Vector3.up;
            }
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }
    }
}
