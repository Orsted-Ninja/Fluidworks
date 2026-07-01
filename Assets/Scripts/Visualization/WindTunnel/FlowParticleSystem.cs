using AeroFlow.Physics;
using UnityEngine;
using AeroFlow.Core;
using AeroFlow.Rendering;

namespace AeroFlow.Visualization
{
    [RequireComponent(typeof(ParticleSystem))]
    public class FlowParticleSystem : MonoBehaviour
    {
        public WindTunnelSimulation3D windTunnel;
        private NavierStokesGridSolver navier;

        private ParticleSystem particleSystemRef;
        private ParticleSystem.Particle[] particles;
        private bool configured;
        private Material runtimeMaterial;
        private static Texture2D smokeSpriteTexture;
        private static readonly Color EffectsBaseColor = new Color(0.82f, 0.90f, 1f, 0.14f);
        private string lastVisualizationMode;

        [Header("Visuals")]
        public Gradient speedColor;
        public float particleSize = 0.1f;
        [Range(0.2f, 1.2f)] public float emitterCoverage = 0.82f;

        private Bounds modelBoundsCache;
        private bool hasModelBounds;
        private float nextModelBoundsRefreshTime = -999f;

        private void Awake()
        {
            EnsureConfigured();
        }

        private void OnEnable()
        {
            EnsureConfigured();
            if (particleSystemRef != null && !particleSystemRef.isPlaying)
            {
                particleSystemRef.Play();
            }
        }

        private void Start()
        {
            EnsureConfigured();
        }

        public void EnsureConfigured()
        {
            if (configured && particleSystemRef != null)
            {
                return;
            }

            particleSystemRef = GetComponent<ParticleSystem>();
            if (particleSystemRef == null)
            {
                return;
            }

            var main = particleSystemRef.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSize = Mathf.Max(0.03f, particleSize);
            main.startLifetime = 3.6f;
            main.startSpeed = 0f;
            main.loop = true;
            main.playOnAwake = true;
            main.maxParticles = Mathf.Max(main.maxParticles, 420);
            main.startColor = new Color(0.84f, 0.92f, 1f, 0.18f);

            var emission = particleSystemRef.emission;
            emission.enabled = true;
            emission.rateOverTime = 24f;

            var shape = particleSystemRef.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(1.8f, 1.2f, 0.18f);

            var colorOverLifetime = particleSystemRef.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(CreateLifetimeTintGradient());

            var sizeOverLifetime = particleSystemRef.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, CreateLifetimeSizeCurve());

            var velocityOverLifetime = particleSystemRef.velocityOverLifetime;
            velocityOverLifetime.enabled = false;

            var limitVelocityOverLifetime = particleSystemRef.limitVelocityOverLifetime;
            limitVelocityOverLifetime.enabled = false;

            var forceOverLifetime = particleSystemRef.forceOverLifetime;
            forceOverLifetime.enabled = false;

            var rotationOverLifetime = particleSystemRef.rotationOverLifetime;
            rotationOverLifetime.enabled = false;

            var noise = particleSystemRef.noise;
            noise.enabled = false;

            var collision = particleSystemRef.collision;
            collision.enabled = false;

            var textureSheetAnimation = particleSystemRef.textureSheetAnimation;
            textureSheetAnimation.enabled = false;

            var trails = particleSystemRef.trails;
            trails.enabled = false;
            trails.mode = ParticleSystemTrailMode.PerParticle;
            trails.ratio = 0.25f;
            trails.lifetime = 0.38f;
            trails.dieWithParticles = true;
            trails.inheritParticleColor = true;

            var renderer = particleSystemRef.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                Shader shader = RuntimeShaderResolver.FindFirstSupported(
                    "Sprites/Default",
                    "Legacy Shaders/Particles/Alpha Blended",
                    "Universal Render Pipeline/Particles/Unlit",
                    "Particles/Standard Unlit",
                    "Legacy Shaders/Particles/Additive");
                if (shader != null)
                {
                    runtimeMaterial = new Material(shader);
                    Texture2D smokeTexture = GetOrCreateSmokeSpriteTexture();
                    if (smokeTexture != null)
                    {
                        if (runtimeMaterial.HasProperty("_BaseMap")) runtimeMaterial.SetTexture("_BaseMap", smokeTexture);
                        if (runtimeMaterial.HasProperty("_MainTex")) runtimeMaterial.SetTexture("_MainTex", smokeTexture);
                    }
                    ConfigureSmokeMaterial(runtimeMaterial, EffectsBaseColor);
                    renderer.sharedMaterial = runtimeMaterial;
                    renderer.trailMaterial = runtimeMaterial;
                }
                renderer.sortMode = ParticleSystemSortMode.Distance;
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.alignment = ParticleSystemRenderSpace.View;
                renderer.allowRoll = false;
                renderer.lengthScale = 1f;
                renderer.velocityScale = 0f;
                renderer.cameraVelocityScale = 0f;
                renderer.minParticleSize = 0.0008f;
                renderer.maxParticleSize = 0.085f;
            }

            EnsureGradient();
            EnsureParticleCapacity(main.maxParticles);
            RebindDependencies();
            AlignEmitterToTunnel();
            configured = true;
        }

        public void SetFlowParameters(float speed, float turb)
        {
            EnsureConfigured();
            if (particleSystemRef == null) return;

            var emission = particleSystemRef.emission;
            float speedFactor = Mathf.Clamp(speed / 150f, 0.1f, 1f);
            float turbFactor = Mathf.Clamp01(turb * 0.01f);
            if (windTunnel == null) return;

            string mode = windTunnel.settings.visualizationMode;
            bool streamlines = string.Equals(mode, WindTunnelSimulation3D.VisualizationStreamlines, System.StringComparison.OrdinalIgnoreCase);
            bool velocity = string.Equals(mode, WindTunnelSimulation3D.VisualizationVelocity, System.StringComparison.OrdinalIgnoreCase);
            bool pressure = string.Equals(mode, WindTunnelSimulation3D.VisualizationPressure, System.StringComparison.OrdinalIgnoreCase);
            bool effects = string.Equals(mode, WindTunnelSimulation3D.VisualizationEffects, System.StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(lastVisualizationMode, mode, System.StringComparison.Ordinal))
            {
                particleSystemRef.Clear();
                lastVisualizationMode = mode;
            }
            var main = particleSystemRef.main;
            main.startSize = Mathf.Max(0.035f, particleSize * (effects ? 1.05f : 1f));
            main.startLifetime = effects ? 1.2f : 2.8f;
            main.maxParticles = Mathf.Max(main.maxParticles, effects ? 420 : 96);

            float emissionRate = effects
                ? Mathf.Lerp(18f, 34f, speedFactor) * (1f + 0.10f * turbFactor)
                : 0f;
            emission.rateOverTime = emissionRate;

            var trails = particleSystemRef.trails;
            trails.enabled = effects;
            trails.ratio = 0.18f;
            trails.lifetime = 0.32f;
            trails.dieWithParticles = true;

            if (!effects)
            {
                particleSystemRef.Clear();
            }

            var renderer = particleSystemRef.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.lengthScale = 1f;
                renderer.velocityScale = 0f;
                renderer.cameraVelocityScale = 0f;
            }
        }

        public void ApplyVisualizationMode(string mode)
        {
            EnsureConfigured();
            if (particleSystemRef == null)
            {
                return;
            }

            particleSystemRef.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particleSystemRef.Clear();
        }

        private void Update()
        {
            EnsureConfigured();
            RebindDependencies();
            if (particleSystemRef == null || windTunnel == null)
            {
                return;
            }

            if (!IsEffectsMode())
            {
                if (particleSystemRef.particleCount > 0)
                {
                    particleSystemRef.Clear();
                }
                return;
            }

            RefreshModelBounds();
            AlignEmitterToTunnel();
            EnsureParticleCapacity(particleSystemRef.main.maxParticles);

            int count = particleSystemRef.GetParticles(particles);
            float dt = Mathf.Clamp(Time.deltaTime, 0.0001f, 0.05f);
            Bounds tunnelBounds = windTunnel.GetTunnelBounds();
            Vector3 dir = GetWindDirectionFromSettings();
            Vector3 side = windTunnel.ResolveTunnelSideAxis();
            Vector3 up = windTunnel.ResolveTunnelVerticalAxis();
            float tunnelLength = Vector3.Dot(tunnelBounds.size, Abs(dir));
            float tunnelWidth = Vector3.Dot(tunnelBounds.size, Abs(side));
            float tunnelHeight = Vector3.Dot(tunnelBounds.size, Abs(up));

            for (int i = 0; i < count; i++)
            {
                Vector3 pos = particles[i].position;
                Vector3 vel = GetFlowVelocity(pos, dir, side, up);
                if (!IsFinite(vel))
                {
                    vel = dir * Mathf.Max(1f, windTunnel.settings.inletVelocity);
                }
                if (IsEffectsMode())
                {
                    vel = StyleVelocityForEffects(pos, vel, dir, side, up);
                }

                Vector3 nextPos = pos + vel * dt;
                if (TryProjectOutsideBody(ref nextPos, dir))
                {
                    vel = Vector3.ProjectOnPlane(vel, (nextPos - modelBoundsCache.center).normalized);
                    if (vel.sqrMagnitude < 1e-4f)
                    {
                        vel = dir * Mathf.Max(1f, windTunnel.settings.inletVelocity * 0.6f);
                    }
                }

                if (ShouldRespawnForEffects(nextPos, dir, side, up))
                {
                    RespawnParticle(ref particles[i], tunnelBounds, dir, side, up, tunnelLength, tunnelWidth, tunnelHeight);
                    continue;
                }

                if (!tunnelBounds.Contains(nextPos) || !IsFinite(nextPos))
                {
                    RespawnParticle(ref particles[i], tunnelBounds, dir, side, up, tunnelLength, tunnelWidth, tunnelHeight);
                    continue;
                }

                particles[i].position = nextPos;
                particles[i].velocity = vel;
                particles[i].rotation3D = vel.sqrMagnitude > 0.1f ? Quaternion.LookRotation(vel, up).eulerAngles : Vector3.zero;
                particles[i].startColor = EvaluateParticleColor(nextPos, vel, dir, side, up);
                particles[i].startSize = EvaluateParticleSize(nextPos, vel, dir, side, up);
            }

            particleSystemRef.SetParticles(particles, count);
        }

        private void RebindDependencies()
        {
            if (windTunnel == null) windTunnel = FindAnyObjectByType<WindTunnelSimulation3D>();
            if (navier == null && windTunnel != null) navier = windTunnel.navierStokesSolver;
        }

        private void EnsureGradient()
        {
            if (speedColor != null) return;

            speedColor = new Gradient();
            speedColor.mode = GradientMode.Blend;
            speedColor.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.04f, 0.30f, 0.90f), 0f),
                    new GradientColorKey(new Color(0.06f, 0.72f, 0.98f), 0.25f),
                    new GradientColorKey(new Color(0.10f, 0.95f, 0.72f), 0.45f),
                    new GradientColorKey(new Color(0.95f, 0.90f, 0.15f), 0.68f),
                    new GradientColorKey(new Color(1f, 0.50f, 0.08f), 0.85f),
                    new GradientColorKey(new Color(1f, 0.22f, 0.06f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.90f, 0f),
                    new GradientAlphaKey(1f, 0.5f),
                    new GradientAlphaKey(0.95f, 1f)
                });
        }

        private void EnsureParticleCapacity(int maxParticles)
        {
            int capacity = Mathf.Max(128, maxParticles);
            if (particles == null || particles.Length != capacity)
            {
                particles = new ParticleSystem.Particle[capacity];
            }
        }

        private float NormalizeSpeed(float speed)
        {
            float vInf = windTunnel != null ? Mathf.Max(1f, windTunnel.settings.inletVelocity) : 1f;
            float value = speed / (vInf * 1.35f);
            return Mathf.Clamp01(float.IsFinite(value) ? value : 0f);
        }

        private Color EvaluateParticleColor(Vector3 position, Vector3 velocity, Vector3 dir, Vector3 side, Vector3 up)
        {
            float speedNorm = NormalizeSpeed(velocity.magnitude);
            if (!IsEffectsMode())
            {
                return speedColor.Evaluate(speedNorm);
            }

            float vInf = Mathf.Max(1f, windTunnel != null ? windTunnel.settings.inletVelocity : 1f);
            float forward = Mathf.Clamp01(Vector3.Dot(velocity, dir) / vInf);
            float recirculation = Mathf.Clamp01(1f - forward);
            float wake = ComputeWakeFactor(position, dir, side, up);
            float density = Mathf.Clamp01(0.26f + wake * 0.52f + recirculation * 0.28f + (1f - speedNorm) * 0.18f);

            Color baseTint = Color.Lerp(
                new Color(0.72f, 0.80f, 0.86f, 0.08f),
                new Color(0.92f, 0.97f, 1f, 0.30f),
                density);
            Color tracerTint = new Color(0.66f, 0.88f, 1f, baseTint.a);
            Color color = Color.Lerp(baseTint, tracerTint, Mathf.Clamp01(speedNorm * 0.30f + wake * 0.18f));
            color.a = Mathf.Clamp01(0.06f + density * 0.30f);
            return color;
        }

        private float EvaluateParticleSize(Vector3 position, Vector3 velocity, Vector3 dir, Vector3 side, Vector3 up)
        {
            float baseSize = Mathf.Max(0.03f, particleSize);
            if (!IsEffectsMode())
            {
                return baseSize;
            }

            float wake = ComputeWakeFactor(position, dir, side, up);
            float forward = Mathf.Clamp01(Vector3.Dot(velocity.normalized, dir));
            float recirculation = Mathf.Clamp01(1f - forward);
            float scale = Mathf.Clamp01(0.22f + wake * 0.42f + recirculation * 0.18f + (1f - NormalizeSpeed(velocity.magnitude)) * 0.16f);
            return baseSize * Mathf.Lerp(1.10f, 1.85f, scale);
        }

        private Vector3 StyleVelocityForEffects(Vector3 position, Vector3 velocity, Vector3 dir, Vector3 side, Vector3 up)
        {
            float vInf = Mathf.Max(1f, windTunnel != null ? windTunnel.settings.inletVelocity : 1f);
            float wake = ComputeWakeFactor(position, dir, side, up);
            float presentationSpeed = Mathf.Clamp(
                Mathf.Lerp(vInf * 0.08f, vInf * 0.18f, 1f - wake),
                2.5f,
                10f);
            Vector3 lateral = Vector3.ProjectOnPlane(velocity, dir) * 0.14f;

            float noiseA = Mathf.PerlinNoise(Vector3.Dot(position, side) * 0.42f + Time.time * 0.33f, Vector3.Dot(position, up) * 0.38f + 5.7f);
            float noiseB = Mathf.PerlinNoise(Vector3.Dot(position, up) * 0.47f + 9.2f, Vector3.Dot(position, dir) * 0.14f + Time.time * 0.27f);
            Vector3 swirl = side * ((noiseA - 0.5f) * 2f) + up * ((noiseB - 0.5f) * 2f);
            float noiseC = Mathf.PerlinNoise(Vector3.Dot(position, dir) * 0.18f + 17.3f, Time.time * 0.19f + Vector3.Dot(position, side) * 0.22f);
            swirl += dir * ((noiseC - 0.5f) * 2f * wake * 0.35f);

            Vector3 styled = dir * presentationSpeed + lateral;
            styled += swirl * (presentationSpeed * (0.30f + wake * 0.65f));
            if (wake > 0.18f)
            {
                styled -= dir * (presentationSpeed * wake * 0.22f);
            }
            return styled;
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

        private Vector3 GetFlowVelocity(Vector3 pos, Vector3 dir, Vector3 side, Vector3 up)
        {
            float vInf = Mathf.Max(1f, windTunnel != null ? windTunnel.settings.inletVelocity : 1f);
            bool effects = IsEffectsMode();
            bool rawSolverOnly = windTunnel != null && windTunnel.useRawSolverDataOnly;

            if (navier != null && navier.TrySampleFlow(pos, out var sampledVelocity, out _))
            {
                if (IsFinite(sampledVelocity) && sampledVelocity.sqrMagnitude > 1e-6f)
                {
                    if (rawSolverOnly)
                    {
                        return sampledVelocity;
                    }
                    float solverWeight = effects ? 0.60f : 0.85f;
                    return sampledVelocity * solverWeight + dir * vInf * (1f - solverWeight);
                }
            }

            if (rawSolverOnly)
            {
                return Vector3.zero;
            }

            Vector3 analytical = ComputeAnalyticalFlow(pos, dir, side, up, vInf);
            float turbulence = Mathf.Clamp01(windTunnel.settings.turbulenceIntensity * 0.01f);
            float noiseA = Mathf.PerlinNoise(Vector3.Dot(pos, side) * 0.55f + Time.time * 0.7f, Vector3.Dot(pos, up) * 0.45f);
            float noiseB = Mathf.PerlinNoise(Vector3.Dot(pos, up) * 0.50f + 11.3f, Vector3.Dot(pos, dir) * 0.18f + Time.time * 0.5f);
            Vector3 noise = side * ((noiseA - 0.5f) * 2f) + up * ((noiseB - 0.5f) * 2f);
            analytical += noise * (vInf * (effects ? 0.10f : 0.06f) * turbulence);

            float forward = Vector3.Dot(analytical, dir);
            if (forward < vInf * 0.08f)
            {
                analytical += dir * (vInf * 0.08f - forward);
            }
            return analytical;
        }

        private Vector3 ComputeAnalyticalFlow(Vector3 pos, Vector3 dir, Vector3 side, Vector3 up, float vInf)
        {
            Vector3 freestream = dir * vInf;
            if (!hasModelBounds)
            {
                return freestream;
            }

            Vector3 rel = pos - modelBoundsCache.center;
            float axial = Vector3.Dot(rel, dir);
            float lateral = Vector3.Dot(rel, side);
            float vertical = Vector3.Dot(rel, up);

            float halfLength = Mathf.Max(ProjectHalfExtent(modelBoundsCache, dir) * 1.05f, 0.14f);
            float halfWidth = Mathf.Max(ProjectHalfExtent(modelBoundsCache, side) * 0.90f, 0.10f);
            float halfHeight = Mathf.Max(ProjectHalfExtent(modelBoundsCache, up) * 0.90f, 0.10f);

            float ellipsoid = axial * axial / (halfLength * halfLength)
                            + lateral * lateral / (halfWidth * halfWidth)
                            + vertical * vertical / (halfHeight * halfHeight);

            Vector3 crossSection = side * (lateral / Mathf.Max(halfWidth * halfWidth, 1e-4f))
                                 + up * (vertical / Mathf.Max(halfHeight * halfHeight, 1e-4f));
            if (crossSection.sqrMagnitude > 1e-6f)
            {
                crossSection.Normalize();
            }

            if (ellipsoid < 1f)
            {
                Vector3 normal = (dir * (axial / Mathf.Max(halfLength * halfLength, 1e-4f)) + crossSection).normalized;
                Vector3 tangent = Vector3.ProjectOnPlane(dir, normal).normalized;
                if (tangent.sqrMagnitude < 1e-6f) tangent = dir;
                return tangent * (vInf * 0.55f) + normal * (vInf * 0.10f);
            }

            float shell = Mathf.Sqrt(Mathf.Max(ellipsoid, 1e-4f));
            float influence = Mathf.Clamp01(1.9f - shell);
            float throat = Mathf.Clamp01(1.25f - Mathf.Abs(axial) / (halfLength * 1.6f));
            float crossRadius = Mathf.Sqrt(
                lateral * lateral / Mathf.Max(halfWidth * halfWidth, 1e-4f)
              + vertical * vertical / Mathf.Max(halfHeight * halfHeight, 1e-4f));
            float nearSurface = Mathf.Clamp01(1.45f - crossRadius);

            Vector3 sidewash = crossSection * (vInf * 0.85f * influence);
            Vector3 acceleratedCore = dir * (vInf * 0.28f * throat * nearSurface);

            Vector3 wake = Vector3.zero;
            if (axial > 0f)
            {
                float wakeLength = halfLength * 6f;
                float wakeAxial = Mathf.Clamp01(1f - axial / Mathf.Max(wakeLength, 1e-4f));
                float wakeCore = wakeAxial * Mathf.Clamp01(1.2f - crossRadius);
                wake -= dir * (vInf * 0.58f * wakeCore);

                Vector3 swirlDir = Vector3.Cross(dir, crossSection).normalized;
                if (swirlDir.sqrMagnitude > 1e-6f)
                {
                    wake += swirlDir * (vInf * 0.12f * wakeCore * Mathf.Sign(vertical == 0f ? 1f : vertical));
                }
            }

            return freestream + sidewash + acceleratedCore + wake;
        }

        private bool TryProjectOutsideBody(ref Vector3 position, Vector3 flowDirection)
        {
            if (!hasModelBounds || !modelBoundsCache.Contains(position))
            {
                return false;
            }

            Vector3 min = modelBoundsCache.min;
            Vector3 max = modelBoundsCache.max;
            float dxMin = Mathf.Abs(position.x - min.x);
            float dxMax = Mathf.Abs(max.x - position.x);
            float dyMin = Mathf.Abs(position.y - min.y);
            float dyMax = Mathf.Abs(max.y - position.y);
            float dzMin = Mathf.Abs(position.z - min.z);
            float dzMax = Mathf.Abs(max.z - position.z);

            float best = dxMin;
            Vector3 projected = new Vector3(min.x, position.y, position.z);

            if (dxMax < best) { best = dxMax; projected = new Vector3(max.x, position.y, position.z); }
            if (dyMin < best) { best = dyMin; projected = new Vector3(position.x, min.y, position.z); }
            if (dyMax < best) { best = dyMax; projected = new Vector3(position.x, max.y, position.z); }
            if (dzMin < best) { best = dzMin; projected = new Vector3(position.x, position.y, min.z); }
            if (dzMax < best) { projected = new Vector3(position.x, position.y, max.z); }

            Vector3 outward = (projected - modelBoundsCache.center).normalized;
            if (outward.sqrMagnitude < 1e-6f)
            {
                outward = Vector3.up;
            }

            position = projected + outward * 0.035f + flowDirection * 0.015f;
            return true;
        }

        private void RespawnParticle(
            ref ParticleSystem.Particle particle,
            Bounds tunnelBounds,
            Vector3 dir,
            Vector3 side,
            Vector3 up,
            float tunnelLength,
            float tunnelWidth,
            float tunnelHeight)
        {
            bool effects = IsEffectsMode();
            float centerSide = 0f;
            float centerUp = 0f;
            float spawnSideHalf = tunnelWidth * (effects ? 0.22f : 0.42f) * emitterCoverage;
            float spawnUpHalf = tunnelHeight * (effects ? 0.16f : 0.34f) * emitterCoverage;
            Vector3 seedCenter = tunnelBounds.center - dir * (0.48f * tunnelLength);

            if (hasModelBounds)
            {
                centerSide = Vector3.Dot(modelBoundsCache.center - tunnelBounds.center, side);
                centerUp = Vector3.Dot(modelBoundsCache.center - tunnelBounds.center, up);
                spawnSideHalf = Mathf.Clamp(ProjectHalfExtent(modelBoundsCache, side) * (effects ? 1.65f : 2.2f), tunnelWidth * 0.08f, tunnelWidth * (effects ? 0.26f : 0.42f));
                spawnUpHalf = Mathf.Clamp(ProjectHalfExtent(modelBoundsCache, up) * (effects ? 1.45f : 1.9f), tunnelHeight * 0.07f, tunnelHeight * (effects ? 0.22f : 0.35f));

                if (effects)
                {
                    float upstreamOffset = Mathf.Clamp(ProjectHalfExtent(modelBoundsCache, dir) * 2.1f, tunnelLength * 0.10f, tunnelLength * 0.24f);
                    Vector3 focusedSeed = modelBoundsCache.center - dir * upstreamOffset;
                    seedCenter = tunnelBounds.Contains(focusedSeed) ? focusedSeed : seedCenter;
                }
            }

            particle.position = seedCenter
                              + side * (centerSide + Random.Range(-spawnSideHalf, spawnSideHalf))
                              + up * (centerUp + Random.Range(-spawnUpHalf, spawnUpHalf));
            particle.velocity = dir * Mathf.Max(1f, windTunnel.settings.inletVelocity);
            particle.startColor = effects
                ? new Color(0.88f, 0.95f, 1f, 0.12f)
                : speedColor.Evaluate(0.1f);
            float lifetime = effects ? Random.Range(0.8f, 1.5f) : Random.Range(1.0f, 2.8f);
            particle.startLifetime = lifetime;
            particle.remainingLifetime = lifetime;
            particle.startSize = Mathf.Max(0.035f, particleSize * (effects ? Random.Range(0.95f, 1.35f) : Random.Range(0.85f, 1.15f)));
        }

        private void AlignEmitterToTunnel()
        {
            if (windTunnel == null || particleSystemRef == null) return;

            Bounds tunnelBounds = windTunnel.GetTunnelBounds();
            Vector3 dir = GetWindDirectionFromSettings();
            Vector3 side = windTunnel.ResolveTunnelSideAxis();
            Vector3 up = windTunnel.ResolveTunnelVerticalAxis();
            float tunnelLength = Vector3.Dot(tunnelBounds.size, Abs(dir));
            float tunnelWidth = Vector3.Dot(tunnelBounds.size, Abs(side));
            float tunnelHeight = Vector3.Dot(tunnelBounds.size, Abs(up));
            bool effects = IsEffectsMode();
            float sideCoverage = emitterCoverage;
            float upCoverage = emitterCoverage;
            float emitterDepth = Mathf.Max(0.12f, tunnelLength * 0.03f);
            Vector3 emitterOrigin = tunnelBounds.center - dir * (0.48f * tunnelLength);

            if (effects)
            {
                sideCoverage = Mathf.Clamp(emitterCoverage * 0.58f, 0.18f, 0.82f);
                upCoverage = Mathf.Clamp(emitterCoverage * 0.42f, 0.12f, 0.62f);
                emitterDepth = Mathf.Max(0.08f, tunnelLength * 0.014f);
                if (hasModelBounds)
                {
                    float upstreamOffset = Mathf.Clamp(ProjectHalfExtent(modelBoundsCache, dir) * 2.2f, tunnelLength * 0.10f, tunnelLength * 0.24f);
                    Vector3 focusedOrigin = modelBoundsCache.center - dir * upstreamOffset;
                    emitterOrigin = tunnelBounds.Contains(focusedOrigin) ? focusedOrigin : emitterOrigin;
                    sideCoverage = Mathf.Clamp(ProjectHalfExtent(modelBoundsCache, side) * 2.2f / Mathf.Max(tunnelWidth, 1e-4f), 0.14f, 0.42f);
                    upCoverage = Mathf.Clamp(ProjectHalfExtent(modelBoundsCache, up) * 1.8f / Mathf.Max(tunnelHeight, 1e-4f), 0.10f, 0.34f);
                }
            }

            transform.position = emitterOrigin;
            transform.rotation = Quaternion.LookRotation(dir, up);

            var shape = particleSystemRef.shape;
            shape.position = Vector3.zero;
            shape.scale = new Vector3(
                tunnelWidth * sideCoverage,
                tunnelHeight * upCoverage,
                emitterDepth);
        }

        private Vector3 GetWindDirectionFromSettings()
        {
            return windTunnel != null ? windTunnel.ResolveWindDirection() : Vector3.right;
        }

        private static float ProjectHalfExtent(Bounds bounds, Vector3 axis)
        {
            Vector3 absAxis = Abs(axis.normalized);
            return Vector3.Dot(bounds.extents, absAxis);
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }

        private bool IsEffectsMode()
        {
            return windTunnel != null
                && (
                    windTunnel.settings.graphicsMode == WindTunnelGraphicsMode.Particle
                    || string.Equals(windTunnel.settings.visualizationMode, WindTunnelSimulation3D.VisualizationEffects, System.StringComparison.OrdinalIgnoreCase));
        }

        private bool ShouldRespawnForEffects(Vector3 position, Vector3 dir, Vector3 side, Vector3 up)
        {
            if (!IsEffectsMode() || !hasModelBounds)
            {
                return false;
            }

            Vector3 rel = position - modelBoundsCache.center;
            float axial = Vector3.Dot(rel, dir);
            float lateral = Mathf.Abs(Vector3.Dot(rel, side));
            float vertical = Mathf.Abs(Vector3.Dot(rel, up));
            float halfLength = Mathf.Max(ProjectHalfExtent(modelBoundsCache, dir), 0.08f);
            float halfWidth = Mathf.Max(ProjectHalfExtent(modelBoundsCache, side), 0.05f);
            float halfHeight = Mathf.Max(ProjectHalfExtent(modelBoundsCache, up), 0.05f);

            return axial < -halfLength * 2.8f
                || axial > halfLength * 4.4f
                || lateral > halfWidth * 3.2f
                || vertical > halfHeight * 2.6f;
        }

        private float ComputeWakeFactor(Vector3 position, Vector3 dir, Vector3 side, Vector3 up)
        {
            if (!hasModelBounds)
            {
                return 0.2f;
            }

            Vector3 rel = position - modelBoundsCache.center;
            float axial = Vector3.Dot(rel, dir);
            float lateral = Mathf.Abs(Vector3.Dot(rel, side));
            float vertical = Mathf.Abs(Vector3.Dot(rel, up));
            float halfLength = Mathf.Max(ProjectHalfExtent(modelBoundsCache, dir), 0.08f);
            float halfWidth = Mathf.Max(ProjectHalfExtent(modelBoundsCache, side), 0.05f);
            float halfHeight = Mathf.Max(ProjectHalfExtent(modelBoundsCache, up), 0.05f);

            float downstream = axial <= -halfLength * 0.15f
                ? 0f
                : Mathf.Clamp01((axial + halfLength * 0.15f) / (halfLength * 4.8f));
            float lateralBand = Mathf.Clamp01(1f - lateral / (halfWidth * 2.6f));
            float verticalBand = Mathf.Clamp01(1f - vertical / (halfHeight * 2.2f));
            return downstream * lateralBand * verticalBand;
        }

        private static Gradient CreateLifetimeTintGradient()
        {
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(0.92f, 0.97f, 1f), 0.45f),
                    new GradientColorKey(new Color(0.84f, 0.9f, 0.96f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.20f, 0.18f),
                    new GradientAlphaKey(0.34f, 0.52f),
                    new GradientAlphaKey(0.06f, 1f)
                });
            return gradient;
        }

        private static AnimationCurve CreateLifetimeSizeCurve()
        {
            return new AnimationCurve(
                new Keyframe(0f, 0.58f),
                new Keyframe(0.24f, 0.96f),
                new Keyframe(0.64f, 1.18f),
                new Keyframe(1f, 0.42f));
        }

        private static Texture2D GetOrCreateSmokeSpriteTexture()
        {
            if (smokeSpriteTexture != null)
            {
                return smokeSpriteTexture;
            }

            const int size = 128;
            smokeSpriteTexture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            smokeSpriteTexture.wrapMode = TextureWrapMode.Clamp;
            smokeSpriteTexture.filterMode = FilterMode.Bilinear;

            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = ((x + 0.5f) / size) * 2f - 1f;
                    float v = ((y + 0.5f) / size) * 2f - 1f;
                    float radius = Mathf.Sqrt(u * u + v * v);
                    float radial = Mathf.Pow(1f - Mathf.Clamp01(radius), 2.45f);
                    float angle = Mathf.Atan2(v, u);
                    float swirl = 0.5f + 0.5f * Mathf.Sin(angle * 3.5f + radius * 8f);
                    float noise = Mathf.PerlinNoise(u * 2.8f + 7.1f, v * 2.8f + 13.4f);
                    float feather = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(1f - radius * 1.08f));
                    float alpha = Mathf.Clamp01(radial * feather * (0.48f + swirl * 0.18f + noise * 0.12f));
                    byte a = (byte)Mathf.RoundToInt(alpha * 255f);
                    pixels[x + y * size] = new Color32(255, 255, 255, a);
                }
            }

            smokeSpriteTexture.SetPixels32(pixels);
            smokeSpriteTexture.Apply(false, true);
            return smokeSpriteTexture;
        }

        private static void ConfigureSmokeMaterial(Material material, Color baseColor)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
            if (material.HasProperty("_Color")) material.SetColor("_Color", baseColor);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 10;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }
    }
}
