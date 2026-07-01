using UnityEngine;
using System.Collections.Generic;

namespace AeroFlow.Visualization
{
    public class LiquidInteractionLaymanMode : MonoBehaviour
    {
        [Header("Mode")]
        public bool modeEnabled = true;
        [Range(0.05f, 1f)] public float sampleInterval = 0.2f;
        [Range(128, 8192)] public int maxSamples = 2048;

        [Header("Visual")]
        public Color lowImpactColor = new Color(0.15f, 0.55f, 1f, 1f);
        public Color highImpactColor = new Color(1f, 0.28f, 0.05f, 1f);
        public float glowStrength = 2.0f;
        public float lineMaxLength = 1.8f;

        private Simulation3D sim;
        private Renderer[] modelRenderers;
        private MaterialPropertyBlock mpb;
        private float nextSampleTime;
        private Vector3[] posCache;
        private Vector3[] velCache;
        private float intensity;
        private string laymanLabel = "No Contact";

        private LineRenderer[] wakeLines;

        private void Awake()
        {
            mpb = new MaterialPropertyBlock();
            EnsureWakeLines();
        }

        private void Update()
        {
            if (!modeEnabled)
            {
                SetLinesVisible(false);
                return;
            }

            if (sim == null) sim = FindAnyObjectByType<Simulation3D>();
            if (sim == null || sim.positionBuffer == null || sim.velocityBuffer == null)
            {
                SetLinesVisible(false);
                return;
            }

            RefreshModelRenderers();
            if (modelRenderers == null || modelRenderers.Length == 0)
            {
                SetLinesVisible(false);
                return;
            }

            if (Time.time >= nextSampleTime)
            {
                nextSampleTime = Time.time + sampleInterval;
                SampleInteraction();
                ApplyModelTint();
                UpdateWakeLines();
            }

            // Only show wake lines if there is actually some interaction/intensity
            bool showLines = intensity > 0.12f;
            SetLinesVisible(showLines);
        }

        private void SampleInteraction()
        {
            RefreshModelRenderers();
            if (modelRenderers == null || modelRenderers.Length == 0)
            {
                intensity = 0f;
                laymanLabel = "No Contact";
                return;
            }

            int count = sim.positionBuffer.count;
            if (count <= 0)
            {
                intensity = 0f;
                laymanLabel = "No Contact";
                return;
            }

            if (posCache == null || posCache.Length != count)
            {
                posCache = new Vector3[count];
                velCache = new Vector3[count];
            }

            sim.positionBuffer.GetData(posCache);
            sim.velocityBuffer.GetData(velCache);

            Bounds b = modelRenderers[0].bounds;
            for (int i = 1; i < modelRenderers.Length; i++) b.Encapsulate(modelRenderers[i].bounds);
            b.Expand(0.2f);

            int stride = Mathf.Max(1, count / Mathf.Max(1, maxSamples));
            float weightedSpeed = 0f;
            int hits = 0;
            float maxSpeed = 0f;

            for (int i = 0; i < count; i += stride)
            {
                Vector3 p = posCache[i];
                if (!b.Contains(p)) continue;

                Vector3 v = velCache[i];
                float speed = v.magnitude;
                maxSpeed = Mathf.Max(maxSpeed, speed);

                Vector3 closest = b.ClosestPoint(p);
                float d = Vector3.Distance(closest, p);
                float proximityWeight = 1f - Mathf.Clamp01(d / 0.2f);
                weightedSpeed += speed * proximityWeight;
                hits++;
            }

            float meanSpeed = hits > 0 ? weightedSpeed / hits : 0f;
            float occupancy = Mathf.Clamp01(hits / (float)Mathf.Max(8, maxSamples / 4));
            float speedNorm = Mathf.Clamp01(meanSpeed / 4f);
            float maxNorm = Mathf.Clamp01(maxSpeed / 8f);
            intensity = Mathf.Clamp01(speedNorm * 0.65f + maxNorm * 0.2f + occupancy * 0.15f);

            if (hits < 6 || intensity < 0.15f) laymanLabel = "Light Contact";
            else if (intensity < 0.45f) laymanLabel = "Moderate Impact";
            else if (intensity < 0.75f) laymanLabel = "Strong Impact";
            else laymanLabel = "Severe Impact";
        }

        private void ApplyModelTint()
        {
            Color tint = Color.Lerp(lowImpactColor, highImpactColor, intensity);
            Color emissive = tint * Mathf.Lerp(0.15f, glowStrength, intensity);

            for (int i = 0; i < modelRenderers.Length; i++)
            {
                var r = modelRenderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", tint);
                mpb.SetColor("_Color", tint);
                mpb.SetColor("_EmissionColor", emissive);
                r.SetPropertyBlock(mpb);
            }
        }

        private void EnsureWakeLines()
        {
            if (wakeLines != null && wakeLines.Length == 12) return;
            wakeLines = new LineRenderer[12];

            for (int i = 0; i < wakeLines.Length; i++)
            {
                var go = new GameObject("LiquidWakeLine_" + i);
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 12;
                lr.widthMultiplier = 0.025f;
                lr.useWorldSpace = true;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.numCapVertices = 3;
                lr.numCornerVertices = 4;
                Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Hidden/InternalErrorShader");
                lr.material = new Material(shader);
                wakeLines[i] = lr;
            }
        }

        private void UpdateWakeLines()
        {
            RefreshModelRenderers();
            if (wakeLines == null || wakeLines.Length == 0 || modelRenderers == null || modelRenderers.Length == 0)
                return;

            Bounds b = modelRenderers[0].bounds;
            for (int i = 1; i < modelRenderers.Length; i++) b.Encapsulate(modelRenderers[i].bounds);
            Vector3 c = b.center;

            float ySpread = Mathf.Max(0.12f, b.extents.y * 0.55f);
            float zSpread = Mathf.Max(0.12f, b.extents.z * 0.55f);
            float xSpread = Mathf.Max(0.12f, b.extents.x * 0.55f);

            Vector3[] seeds = new Vector3[wakeLines.Length];
            int idx = 0;
            for (int s = 0; s < 4 && idx < seeds.Length; s++, idx++)
            {
                float angle = s * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                seeds[idx] = c + new Vector3(0f, Mathf.Sin(angle) * ySpread, Mathf.Cos(angle) * zSpread);
            }
            for (int s = 0; s < 4 && idx < seeds.Length; s++, idx++)
            {
                float y = (s < 2 ? 1f : -1f) * ySpread * 0.65f;
                float z = (s % 2 == 0 ? 1f : -1f) * zSpread * 0.65f;
                seeds[idx] = c + new Vector3(-xSpread * 1.1f, y, z);
            }
            for (int s = 0; s < 4 && idx < seeds.Length; s++, idx++)
            {
                float y = (s < 2 ? 1f : -1f) * ySpread * 0.40f;
                float z = (s % 2 == 0 ? 1f : -1f) * zSpread * 0.40f;
                seeds[idx] = c + new Vector3(xSpread * 1.2f, y, z);
            }

            float stepSize = Mathf.Max(0.05f, lineMaxLength / 11f);

            for (int i = 0; i < wakeLines.Length; i++)
            {
                var lr = wakeLines[i];
                lr.enabled = true;

                Vector3 pos = seeds[i];
                int pointCount = lr.positionCount;
                float speedSum = 0f;

                for (int p = 0; p < pointCount; p++)
                {
                    lr.SetPosition(p, pos);
                    Vector3 localVel = SampleNearestParticleVelocity(pos);
                    float speed = localVel.magnitude;
                    speedSum += speed;
                    pos += (speed > 0.01f ? localVel.normalized : Vector3.right) * stepSize;
                }

                float avgSpeed = speedSum / Mathf.Max(1, pointCount);
                float speedT = Mathf.Clamp01(avgSpeed / 4f);
                Color lineColor = Color.Lerp(lowImpactColor, highImpactColor, Mathf.Max(speedT, intensity));
                lineColor.a = Mathf.Lerp(0.35f, 0.95f, Mathf.Max(speedT, intensity * 0.5f));

                lr.startColor = lineColor;
                lr.endColor = new Color(lineColor.r, lineColor.g, lineColor.b, 0.08f);
                lr.widthMultiplier = Mathf.Lerp(0.015f, 0.045f, speedT);
            }
        }

        private Vector3 SampleNearestParticleVelocity(Vector3 worldPos)
        {
            if (posCache == null || velCache == null) return Vector3.zero;

            float searchRadiusSq = 0.25f;
            Vector3 weightedVel = Vector3.zero;
            float totalWeight = 0f;
            int count = posCache.Length;
            int stride = Mathf.Max(1, count / Mathf.Max(1, maxSamples));

            for (int i = 0; i < count; i += stride)
            {
                float distSq = (posCache[i] - worldPos).sqrMagnitude;
                if (distSq < searchRadiusSq)
                {
                    float weight = 1f / Mathf.Max(distSq, 0.001f);
                    weightedVel += velCache[i] * weight;
                    totalWeight += weight;
                }
            }

            return totalWeight > 0f ? weightedVel / totalWeight : Vector3.zero;
        }

        private void SetLinesVisible(bool v)
        {
            if (wakeLines == null) return;
            for (int i = 0; i < wakeLines.Length; i++)
            {
                if (wakeLines[i] != null) wakeLines[i].enabled = v;
            }
        }

        private void RefreshModelRenderers()
        {
            if (modelRenderers != null && modelRenderers.Length > 0)
            {
                bool hasAliveRenderer = false;
                for (int i = 0; i < modelRenderers.Length; i++)
                {
                    if (modelRenderers[i] != null)
                    {
                        hasAliveRenderer = true;
                        break;
                    }
                }

                if (hasAliveRenderer)
                {
                    var alive = new List<Renderer>(modelRenderers.Length);
                    for (int i = 0; i < modelRenderers.Length; i++)
                    {
                        if (modelRenderers[i] != null) alive.Add(modelRenderers[i]);
                    }
                    modelRenderers = alive.ToArray();
                    if (modelRenderers.Length > 0) return;
                }
            }

            var model = GameObject.Find("LoadedModel");
            if (model == null)
            {
                modelRenderers = null;
                return;
            }

            modelRenderers = model.GetComponentsInChildren<Renderer>();
        }

        private void OnGUI()
        {
            if (!modeEnabled) return;
            if (sim == null || !sim.isActiveAndEnabled) return;

            Rect rect = new Rect(18, Screen.height - 90, 340, 64);
            GUI.Box(rect, "Liquid Interaction Mode");
            GUI.Label(new Rect(rect.x + 12, rect.y + 26, rect.width - 24, 20), "Object Status: " + laymanLabel);
            GUI.Label(new Rect(rect.x + 12, rect.y + 44, rect.width - 24, 20), "Intensity: " + Mathf.RoundToInt(intensity * 100f) + "%");
        }

        private void OnDisable()
        {
            SetLinesVisible(false);
        }

        private void OnDestroy()
        {
            if (wakeLines == null) return;
            for (int i = 0; i < wakeLines.Length; i++)
            {
                var lr = wakeLines[i];
                if (lr == null) continue;
                
                var m = lr.material;
                if (m != null)
                {
                    if (Application.isPlaying) Destroy(m);
                    else DestroyImmediate(m);
                }

                if (lr.gameObject != null)
                {
                    if (Application.isPlaying) Destroy(lr.gameObject);
                    else DestroyImmediate(lr.gameObject);
                }
            }
        }
    }
}
