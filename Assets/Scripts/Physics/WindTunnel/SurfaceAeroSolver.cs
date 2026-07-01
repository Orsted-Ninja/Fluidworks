using System.Collections.Generic;
using AeroFlow.Core;
using UnityEngine;

namespace AeroFlow.Physics
{
    /// <summary>
    /// Surface-focused post-process built on top of the wind-tunnel velocity field.
    /// It produces per-vertex pressure/shear scalars for body heatmaps and a cleaner
    /// pressure-drag vs friction-drag force split than the coarse bulk diagnostics.
    /// </summary>
    public class SurfaceAeroSolver : MonoBehaviour
    {
        public enum SurfaceFieldMode
        {
            Pressure,
            Friction
        }

        public struct SurfaceAeroSample
        {
            public bool valid;
            public float referenceArea;
            public float meanSurfacePressure;
            public float meanWallShear;
            public float pressureDragForce;
            public float frictionDragForce;
            public float totalDragForce;
            public float dragCoeff;
            public float liftCoeff;
            public float sideForceCoeff;
            public Vector3 totalPressureForce;
            public Vector3 totalFrictionForce;
            public Vector3 totalForce;
            public Vector3 totalMoment;
            public Vector3 centerOfPressure;
            public Vector3 referencePoint;
        }

        private sealed class RendererSurfaceData
        {
            public float[] pressureValues;
            public float[] frictionValues;
            public float pressureMin;
            public float pressureMax;
            public float frictionMin;
            public float frictionMax;
        }

        [Header("Sampling")]
        [Range(0.03f, 0.5f)] public float sampleInterval = 0.10f;
        [Range(0.001f, 0.05f)] public float probeOffset = 0.010f;
        [Range(0.1f, 3f)] public float pressureBlendWeight = 0.85f;
        [Range(0.1f, 3f)] public float shearBlendWeight = 1.00f;
        [Range(0.05f, 0.5f)] public float boundaryLayerThicknessScale = 1.0f;

        private readonly Dictionary<int, RendererSurfaceData> surfaceDataByRendererId = new Dictionary<int, RendererSurfaceData>(64);
        private readonly List<MeshRenderer> visibleRenderers = new List<MeshRenderer>(128);

        private WindTunnelSimulation3D wind;
        private NavierStokesGridSolver navier;
        private bool subscribed;
        private float nextSampleTime;
        private SurfaceAeroSample latestSample;

        private void OnEnable()
        {
            ResolveSimulationReferences();
            EnsureSubscription();
        }

        private void OnDisable()
        {
            RemoveSubscription();
        }

        public void ResetData()
        {
            surfaceDataByRendererId.Clear();
            latestSample = default;
            nextSampleTime = 0f;
        }

        public bool TryGetSurfaceField(Renderer renderer, SurfaceFieldMode mode, out float[] values, out float minValue, out float maxValue)
        {
            values = null;
            minValue = 0f;
            maxValue = 0f;
            if (renderer == null)
            {
                return false;
            }

            if (!surfaceDataByRendererId.TryGetValue(renderer.GetInstanceID(), out RendererSurfaceData data) || data == null)
            {
                return false;
            }

            if (mode == SurfaceFieldMode.Friction)
            {
                values = data.frictionValues;
                minValue = data.frictionMin;
                maxValue = data.frictionMax;
            }
            else
            {
                values = data.pressureValues;
                minValue = data.pressureMin;
                maxValue = data.pressureMax;
            }

            return values != null && values.Length > 0;
        }

        public bool TryGetSurfaceLoadSample(out SurfaceAeroSample sample)
        {
            sample = latestSample;
            return sample.valid;
        }

        private void ResolveSimulationReferences()
        {
            if (wind == null)
            {
                wind = GetComponent<WindTunnelSimulation3D>() ?? FindAnyObjectByType<WindTunnelSimulation3D>();
            }

            if (navier == null && wind != null)
            {
                navier = wind.navierStokesSolver;
            }
        }

        private void EnsureSubscription()
        {
            if (subscribed)
            {
                return;
            }

            ResolveSimulationReferences();
            if (navier == null)
            {
                return;
            }

            navier.OnSimulationCompleted += HandleSimulationCompleted;
            subscribed = true;
        }

        private void RemoveSubscription()
        {
            if (!subscribed || navier == null)
            {
                subscribed = false;
                return;
            }

            navier.OnSimulationCompleted -= HandleSimulationCompleted;
            subscribed = false;
        }

        private void HandleSimulationCompleted()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (Time.time < nextSampleTime)
            {
                return;
            }

            nextSampleTime = Time.time + Mathf.Max(0.03f, sampleInterval);
            RebuildSurfaceData();
        }

        private void RebuildSurfaceData()
        {
            ResolveSimulationReferences();
            if (wind == null || navier == null)
            {
                ResetData();
                return;
            }

            GameObject loadedModel = RuntimeModelLookup.GetLoadedModel();
            if (loadedModel == null)
            {
                ResetData();
                return;
            }

            CollectVisibleRenderers(loadedModel);
            if (visibleRenderers.Count == 0)
            {
                ResetData();
                return;
            }

            Vector3 windDir = wind.ResolveWindDirection().normalized;
            if (windDir.sqrMagnitude < 1e-6f) windDir = Vector3.right;
            Vector3 liftAxis = wind.ResolveTunnelVerticalAxis();
            if (liftAxis.sqrMagnitude < 1e-6f) liftAxis = Vector3.up;
            Vector3 sideAxis = Vector3.Cross(windDir, liftAxis).normalized;
            if (sideAxis.sqrMagnitude < 1e-6f) sideAxis = Vector3.right;

            float rho = Mathf.Max(wind.settings.airDensity, 0.01f);
            float mu = Mathf.Max(wind.settings.dynamicViscosity, 1e-6f);
            float vInf = Mathf.Max(wind.settings.inletVelocity, 0.1f);
            float qInf = 0.5f * rho * vInf * vInf;
            Bounds tunnelBounds = wind.GetTunnelBounds();
            float tunnelLength = Mathf.Max(tunnelBounds.size.x, Mathf.Max(tunnelBounds.size.y, tunnelBounds.size.z));
            float delta = Mathf.Max(0.003f, tunnelLength / 64f * Mathf.Max(boundaryLayerThicknessScale, 0.05f));
            float sampleOffset = Mathf.Max(probeOffset, tunnelLength * 0.0008f);

            Bounds renderBounds = visibleRenderers[0].bounds;
            for (int i = 1; i < visibleRenderers.Count; i++)
            {
                renderBounds.Encapsulate(visibleRenderers[i].bounds);
            }

            float referenceArea = Mathf.Max(ComputeProjectedArea(renderBounds.size, windDir), 1e-4f);
            Vector3 referencePoint = wind.TryGetVehicleReferenceFrame(out WindTunnelVehicleReferenceFrame vehicleFrame) && vehicleFrame.valid
                ? vehicleFrame.centerOfGravity
                : renderBounds.center;

            surfaceDataByRendererId.Clear();

            Vector3 totalPressureForce = Vector3.zero;
            Vector3 totalFrictionForce = Vector3.zero;
            Vector3 totalMoment = Vector3.zero;
            float pressureAreaWeighted = 0f;
            float shearAreaWeighted = 0f;
            float totalArea = 0f;

            for (int rendererIndex = 0; rendererIndex < visibleRenderers.Count; rendererIndex++)
            {
                MeshRenderer renderer = visibleRenderers[rendererIndex];
                if (renderer == null) continue;
                MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
                if (meshFilter == null || meshFilter.sharedMesh == null) continue;

                Mesh mesh = meshFilter.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                if (vertices == null || normals == null || vertices.Length == 0 || normals.Length != vertices.Length)
                {
                    continue;
                }

                Matrix4x4 localToWorld = renderer.transform.localToWorldMatrix;
                float[] pressureValues = new float[vertices.Length];
                float[] frictionValues = new float[vertices.Length];
                Vector3[] sampledVelocities = new Vector3[vertices.Length];
                Vector3[] worldVertices = new Vector3[vertices.Length];
                Vector3[] worldNormals = new Vector3[vertices.Length];

                float pressureMin = float.PositiveInfinity;
                float pressureMax = float.NegativeInfinity;
                float frictionMin = float.PositiveInfinity;
                float frictionMax = float.NegativeInfinity;

                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 worldPos = localToWorld.MultiplyPoint3x4(vertices[i]);
                    Vector3 worldNormal = localToWorld.MultiplyVector(normals[i]).normalized;
                    if (worldNormal.sqrMagnitude < 1e-6f)
                    {
                        worldNormal = Vector3.up;
                    }

                    Vector3 samplePos = worldPos + worldNormal * sampleOffset;
                    bool sampled = navier.TrySampleFlow(samplePos, out Vector3 velocityAtPoint, out float sampledPressure);
                    if (!sampled || !float.IsFinite(velocityAtPoint.x) || !float.IsFinite(velocityAtPoint.y) || !float.IsFinite(velocityAtPoint.z))
                    {
                        velocityAtPoint = windDir * vInf;
                        sampled = false;
                    }

                    if (!float.IsFinite(sampledPressure))
                    {
                        sampledPressure = 0f;
                    }

                    float facing = Mathf.Clamp(Vector3.Dot(worldNormal, -windDir), -1f, 1f);
                    float geometricCp = facing >= 0f
                        ? Mathf.Pow(facing, 1.55f)
                        : -0.60f * Mathf.Pow(-facing, 1.12f);
                    float sampledCp = sampled && qInf > 1e-5f ? sampledPressure / qInf : geometricCp;
                    float cp = Mathf.Lerp(geometricCp, sampledCp, sampled ? Mathf.Clamp01(pressureBlendWeight * 0.7f) : 0.15f);
                    cp = Mathf.Clamp(cp, -2.5f, 1.5f);

                    Vector3 tangentialVelocity = velocityAtPoint - worldNormal * Vector3.Dot(velocityAtPoint, worldNormal);
                    float sampledTau = mu * tangentialVelocity.magnitude / delta;
                    float geometricTau = mu * vInf * (1f - Mathf.Abs(Vector3.Dot(worldNormal, windDir)) * 0.65f) / delta;
                    float tau = Mathf.Lerp(geometricTau, sampledTau, sampled ? Mathf.Clamp01(shearBlendWeight * 0.75f) : 0.20f);
                    if (!float.IsFinite(tau)) tau = 0f;

                    pressureValues[i] = cp;
                    frictionValues[i] = tau;
                    sampledVelocities[i] = velocityAtPoint;
                    worldVertices[i] = worldPos;
                    worldNormals[i] = worldNormal;

                    pressureMin = Mathf.Min(pressureMin, cp);
                    pressureMax = Mathf.Max(pressureMax, cp);
                    frictionMin = Mathf.Min(frictionMin, tau);
                    frictionMax = Mathf.Max(frictionMax, tau);
                }

                if (!float.IsFinite(pressureMin) || !float.IsFinite(pressureMax))
                {
                    pressureMin = -1f;
                    pressureMax = 1f;
                }
                if (!float.IsFinite(frictionMin) || !float.IsFinite(frictionMax))
                {
                    frictionMin = 0f;
                    frictionMax = 1f;
                }

                for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                {
                    if (mesh.GetTopology(subMesh) != MeshTopology.Triangles)
                    {
                        continue;
                    }

                    int[] triangles = mesh.GetIndices(subMesh);
                    if (triangles == null || triangles.Length < 3)
                    {
                        continue;
                    }

                    for (int tri = 0; tri < triangles.Length; tri += 3)
                    {
                        int ia = triangles[tri];
                        int ib = triangles[tri + 1];
                        int ic = triangles[tri + 2];
                        if (ia < 0 || ib < 0 || ic < 0 || ia >= vertices.Length || ib >= vertices.Length || ic >= vertices.Length)
                        {
                            continue;
                        }

                        Vector3 a = worldVertices[ia];
                        Vector3 b = worldVertices[ib];
                        Vector3 c = worldVertices[ic];
                        Vector3 areaVector = Vector3.Cross(b - a, c - a);
                        float doubleArea = areaVector.magnitude;
                        if (doubleArea <= 1e-8f)
                        {
                            continue;
                        }

                        float area = 0.5f * doubleArea;
                        Vector3 outwardNormal = (worldNormals[ia] + worldNormals[ib] + worldNormals[ic]).normalized;
                        if (outwardNormal.sqrMagnitude < 1e-6f)
                        {
                            outwardNormal = areaVector.normalized;
                        }

                        float cp = (pressureValues[ia] + pressureValues[ib] + pressureValues[ic]) / 3f;
                        float pLocal = cp * qInf;
                        float tau = Mathf.Max(0f, (frictionValues[ia] + frictionValues[ib] + frictionValues[ic]) / 3f);

                        Vector3 velocity = (sampledVelocities[ia] + sampledVelocities[ib] + sampledVelocities[ic]) / 3f;
                        Vector3 tangential = velocity - outwardNormal * Vector3.Dot(velocity, outwardNormal);
                        if (tangential.sqrMagnitude < 1e-6f)
                        {
                            tangential = windDir - outwardNormal * Vector3.Dot(windDir, outwardNormal);
                        }
                        Vector3 shearDirection = tangential.sqrMagnitude > 1e-6f ? tangential.normalized : Vector3.zero;

                        Vector3 pressureForce = -outwardNormal * (pLocal * area);
                        Vector3 frictionForce = shearDirection * (tau * area);
                        Vector3 totalForce = pressureForce + frictionForce;
                        Vector3 centroid = (a + b + c) / 3f;

                        totalPressureForce += pressureForce;
                        totalFrictionForce += frictionForce;
                        totalMoment += Vector3.Cross(centroid - referencePoint, totalForce);
                        pressureAreaWeighted += pLocal * area;
                        shearAreaWeighted += tau * area;
                        totalArea += area;
                    }
                }

                surfaceDataByRendererId[renderer.GetInstanceID()] = new RendererSurfaceData
                {
                    pressureValues = pressureValues,
                    frictionValues = frictionValues,
                    pressureMin = pressureMin,
                    pressureMax = pressureMax,
                    frictionMin = frictionMin,
                    frictionMax = frictionMax
                };
            }

            Vector3 totalCombinedForce = totalPressureForce + totalFrictionForce;
            Vector3 centerOfPressure = referencePoint;
            if (totalCombinedForce.sqrMagnitude > 1e-6f)
            {
                centerOfPressure += Vector3.Cross(totalCombinedForce, totalMoment) / totalCombinedForce.sqrMagnitude;
            }

            latestSample = new SurfaceAeroSample
            {
                valid = surfaceDataByRendererId.Count > 0 && totalArea > 1e-6f,
                referenceArea = referenceArea,
                meanSurfacePressure = totalArea > 1e-6f ? pressureAreaWeighted / totalArea : 0f,
                meanWallShear = totalArea > 1e-6f ? shearAreaWeighted / totalArea : 0f,
                pressureDragForce = Mathf.Max(0f, Vector3.Dot(totalPressureForce, windDir)),
                frictionDragForce = Mathf.Max(0f, Vector3.Dot(totalFrictionForce, windDir)),
                totalDragForce = Mathf.Max(0f, Vector3.Dot(totalCombinedForce, windDir)),
                dragCoeff = Mathf.Max(0f, Vector3.Dot(totalCombinedForce, windDir) / Mathf.Max(qInf * referenceArea, 1e-4f)),
                liftCoeff = Vector3.Dot(totalCombinedForce, liftAxis.normalized) / Mathf.Max(qInf * referenceArea, 1e-4f),
                sideForceCoeff = Vector3.Dot(totalCombinedForce, sideAxis) / Mathf.Max(qInf * referenceArea, 1e-4f),
                totalPressureForce = totalPressureForce,
                totalFrictionForce = totalFrictionForce,
                totalForce = totalCombinedForce,
                totalMoment = totalMoment,
                centerOfPressure = centerOfPressure,
                referencePoint = referencePoint
            };
        }

        private void CollectVisibleRenderers(GameObject modelRoot)
        {
            visibleRenderers.Clear();
            if (modelRoot == null)
            {
                return;
            }

            MeshRenderer[] renderers = modelRoot.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer renderer = renderers[i];
                if (renderer == null) continue;
                if (renderer.GetComponentInParent<RuntimeSimulationProxy>() != null) continue;
                if (renderer.GetComponent<MeshFilter>() == null) continue;
                visibleRenderers.Add(renderer);
            }
        }

        private static float ComputeProjectedArea(Vector3 size, Vector3 direction)
        {
            Vector3 absDir = new Vector3(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z));
            return size.y * size.z * absDir.x
                 + size.x * size.z * absDir.y
                 + size.x * size.y * absDir.z;
        }
    }
}
