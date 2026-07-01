using System.Collections.Generic;
using AeroFlow.Core;
using UnityEngine;

namespace AeroFlow.Physics
{
    public class ExternalAeroLoadEstimator : MonoBehaviour
    {
        public struct AeroLoadSample
        {
            public bool valid;
            public float dragCoeff;
            public float liftCoeff;
            public float sideForceCoeff;
            public float meanSurfacePressure;
            public float meanWallShear;
            public float referenceArea;
            public float pressureDragForce;
            public float frictionDragForce;
            public Vector3 totalForce;
            public Vector3 totalMoment;
            public Vector3 centerOfPressure;
            public Vector3 referencePoint;
        }

        [Header("Sampling")]
        [Range(0.05f, 2f)] public float sampleInterval = 0.2f;
        [Range(0.1f, 2f)] public float pressureScale = 0.75f;
        [Range(0.1f, 2f)] public float shearScale = 0.35f;
        [Range(64, 2048)] public int maxSurfaceSamples = 640;
        [Range(0.001f, 0.1f)] public float probeOffset = 0.012f;
        [Range(0.01f, 2f)] public float referenceAreaOverride = 0f;

        private static readonly List<Renderer> RendererCache = new List<Renderer>(128);
        private static readonly List<MeshFilter> MeshFilterCache = new List<MeshFilter>(128);
        private static readonly List<SkinnedMeshRenderer> SkinnedRendererCache = new List<SkinnedMeshRenderer>(32);

        private Mesh bakedMesh;

        private float nextSampleTime;
        private bool valid;
        private float cd;
        private float cl;
        private float cs;
        private float meanSurfacePressure;
        private AeroLoadSample latestSample;

        public bool TryGetCoefficients(out float dragCoeff, out float liftCoeff, out float avgPressure)
        {
            dragCoeff = cd;
            liftCoeff = cl;
            avgPressure = meanSurfacePressure;
            return valid;
        }

        public bool TryGetAeroLoadSample(out AeroLoadSample sample)
        {
            sample = latestSample;
            return sample.valid;
        }

        private void Update()
        {
            if (Time.time < nextSampleTime) return;
            nextSampleTime = Time.time + Mathf.Max(0.05f, sampleInterval);
            SampleNow();
        }

        private void Awake()
        {
            bakedMesh = new Mesh { name = "ExternalAeroLoadEstimator_BakedMesh" };
        }

        private void OnDestroy()
        {
            if (bakedMesh != null)
            {
                if (Application.isPlaying) Destroy(bakedMesh);
                else DestroyImmediate(bakedMesh);
            }
        }

        private void SampleNow()
        {
            valid = false;
            cd = 0f;
            cl = 0f;
            meanSurfacePressure = 0f;
            latestSample = default;

            WindTunnelSimulation3D wind = FindAnyObjectByType<WindTunnelSimulation3D>();
            if (wind == null) return;

            GameObject loadedModel = RuntimeModelLookup.GetLoadedModel();
            if (loadedModel == null) return;

            Vector3 windDir = wind.ResolveWindDirection().normalized;
            if (windDir.sqrMagnitude < 1e-6f) windDir = Vector3.right;
            Vector3 liftAxis = wind.ResolveTunnelVerticalAxis();
            if (liftAxis.sqrMagnitude < 1e-6f) liftAxis = Vector3.up;

            float rho = Mathf.Max(wind.settings.airDensity, 0.01f);
            float vinf = Mathf.Max(wind.settings.inletVelocity, 0.1f);
            float qInf = 0.5f * rho * vinf * vinf;
            float probeDistance = Mathf.Max(probeOffset, loadedModel.transform.lossyScale.magnitude * 0.003f);

            var navier = wind.navierStokesSolver;
            var surfaceSolver = wind.surfaceAeroSolver != null
                ? wind.surfaceAeroSolver
                : wind.GetComponent<SurfaceAeroSolver>();
            if (surfaceSolver != null && surfaceSolver.TryGetSurfaceLoadSample(out var surfaceSample) && surfaceSample.valid)
            {
                cd = surfaceSample.dragCoeff;
                cl = surfaceSample.liftCoeff;
                cs = surfaceSample.sideForceCoeff;
                meanSurfacePressure = surfaceSample.meanSurfacePressure;
                valid = true;
                latestSample = new AeroLoadSample
                {
                    valid = true,
                    dragCoeff = surfaceSample.dragCoeff,
                    liftCoeff = surfaceSample.liftCoeff,
                    sideForceCoeff = surfaceSample.sideForceCoeff,
                    meanSurfacePressure = surfaceSample.meanSurfacePressure,
                    meanWallShear = surfaceSample.meanWallShear,
                    referenceArea = surfaceSample.referenceArea,
                    pressureDragForce = surfaceSample.pressureDragForce,
                    frictionDragForce = surfaceSample.frictionDragForce,
                    totalForce = surfaceSample.totalForce,
                    totalMoment = surfaceSample.totalMoment,
                    centerOfPressure = surfaceSample.centerOfPressure,
                    referencePoint = surfaceSample.referencePoint
                };
                return;
            }
            var loader = FindAnyObjectByType<RuntimeModelLoader>();
            Transform sampleRoot = loader != null && loader.CurrentSimulationGeometryRoot != null
                ? loader.CurrentSimulationGeometryRoot
                : loadedModel.transform;

            RendererCache.Clear();
            var renderers = loadedModel.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                if (renderer.GetComponentInParent<RuntimeSimulationProxy>() != null) continue;
                RendererCache.Add(renderer);
            }
            if (RendererCache.Count == 0) return;

            Bounds full = RendererCache[0].bounds;
            for (int i = 1; i < RendererCache.Count; i++)
            {
                full.Encapsulate(RendererCache[i].bounds);
            }

            float refArea = referenceAreaOverride > 0f
                ? referenceAreaOverride
                : Mathf.Max(ComputeProjectedArea(full.size, windDir), 1e-4f);
            Vector3 referencePoint = wind.TryGetVehicleReferenceFrame(out WindTunnelVehicleReferenceFrame vehicleFrame) && vehicleFrame.valid
                ? vehicleFrame.centerOfGravity
                : full.center;

            int totalTriangleCount = CountSurfaceTriangles(sampleRoot);
            int stride = Mathf.Max(1, totalTriangleCount / Mathf.Max(maxSurfaceSamples, 1));

            Vector3 totalForce = Vector3.zero;
            Vector3 totalMoment = Vector3.zero;
            float pressureAccum = 0f;
            int sampleCount = 0;

            bool sampledSurface = AccumulateSurfaceSamples(
                sampleRoot,
                stride,
                probeDistance,
                windDir,
                liftAxis,
                navier,
                vinf,
                qInf,
                referencePoint,
                ref totalForce,
                ref totalMoment,
                ref pressureAccum,
                ref sampleCount);

            if (!sampledSurface)
            {
                FallbackRendererSampling(
                    windDir,
                    liftAxis,
                    navier,
                    vinf,
                    qInf,
                    referencePoint,
                    ref totalForce,
                    ref totalMoment,
                    ref pressureAccum,
                    ref sampleCount);
            }

            if (sampleCount <= 0) return;

            Vector3 dragAxis = windDir;
            float dragForce = Vector3.Dot(totalForce, dragAxis);
            float liftForce = Vector3.Dot(totalForce, liftAxis.normalized);
            Vector3 sideAxis = Vector3.Cross(windDir, liftAxis.normalized).normalized;
            if (sideAxis.sqrMagnitude < 1e-6f) sideAxis = Vector3.right;
            float sideForce = Vector3.Dot(totalForce, sideAxis);
            Vector3 centerOfPressure = referencePoint;
            if (totalForce.sqrMagnitude > 1e-6f)
            {
                centerOfPressure += Vector3.Cross(totalForce, totalMoment) / totalForce.sqrMagnitude;
            }

            cd = Mathf.Max(0f, dragForce / Mathf.Max(qInf * refArea, 1e-4f));
            cl = liftForce / Mathf.Max(qInf * refArea, 1e-4f);
            cs = sideForce / Mathf.Max(qInf * refArea, 1e-4f);
            meanSurfacePressure = pressureAccum / sampleCount;
            valid = true;
            latestSample = new AeroLoadSample
            {
                valid = true,
                dragCoeff = cd,
                liftCoeff = cl,
                sideForceCoeff = cs,
                meanSurfacePressure = meanSurfacePressure,
                meanWallShear = 0f,
                referenceArea = refArea,
                pressureDragForce = Mathf.Max(0f, dragForce),
                frictionDragForce = 0f,
                totalForce = totalForce,
                totalMoment = totalMoment,
                centerOfPressure = centerOfPressure,
                referencePoint = referencePoint
            };
        }

        private int CountSurfaceTriangles(Transform root)
        {
            if (root == null) return 0;

            int total = 0;

            MeshFilterCache.Clear();
            root.GetComponentsInChildren(true, MeshFilterCache);
            for (int i = 0; i < MeshFilterCache.Count; i++)
            {
                Mesh mesh = MeshFilterCache[i] != null ? MeshFilterCache[i].sharedMesh : null;
                if (mesh == null) continue;
                total += CountTriangleIndices(mesh) / 3;
            }

            SkinnedRendererCache.Clear();
            root.GetComponentsInChildren(true, SkinnedRendererCache);
            for (int i = 0; i < SkinnedRendererCache.Count; i++)
            {
                Mesh mesh = SkinnedRendererCache[i] != null ? SkinnedRendererCache[i].sharedMesh : null;
                if (mesh == null) continue;
                total += CountTriangleIndices(mesh) / 3;
            }

            return total;
        }

        private bool AccumulateSurfaceSamples(
            Transform root,
            int stride,
            float probeDistance,
            Vector3 windDir,
            Vector3 liftAxis,
            NavierStokesGridSolver navier,
            float vinf,
            float qInf,
            Vector3 referencePoint,
            ref Vector3 totalForce,
            ref Vector3 totalMoment,
            ref float pressureAccum,
            ref int sampleCount)
        {
            if (root == null) return false;

            bool any = false;

            MeshFilterCache.Clear();
            root.GetComponentsInChildren(true, MeshFilterCache);
            for (int i = 0; i < MeshFilterCache.Count; i++)
            {
                MeshFilter filter = MeshFilterCache[i];
                if (filter == null || filter.sharedMesh == null) continue;
                any |= SampleMesh(
                    filter.sharedMesh,
                    filter.transform.localToWorldMatrix,
                    stride,
                    probeDistance,
                    windDir,
                    liftAxis,
                    navier,
                    vinf,
                    qInf,
                    referencePoint,
                    ref totalForce,
                    ref totalMoment,
                    ref pressureAccum,
                    ref sampleCount);
            }

            SkinnedRendererCache.Clear();
            root.GetComponentsInChildren(true, SkinnedRendererCache);
            for (int i = 0; i < SkinnedRendererCache.Count; i++)
            {
                SkinnedMeshRenderer skinned = SkinnedRendererCache[i];
                if (skinned == null || skinned.sharedMesh == null) continue;
                if (bakedMesh == null)
                {
                    bakedMesh = new Mesh { name = "ExternalAeroLoadEstimator_BakedMesh" };
                }

                bakedMesh.Clear();
                skinned.BakeMesh(bakedMesh, true);
                any |= SampleMesh(
                    bakedMesh,
                    skinned.transform.localToWorldMatrix,
                    stride,
                    probeDistance,
                    windDir,
                    liftAxis,
                    navier,
                    vinf,
                    qInf,
                    referencePoint,
                    ref totalForce,
                    ref totalMoment,
                    ref pressureAccum,
                    ref sampleCount);
            }

            return any;
        }

        private bool SampleMesh(
            Mesh mesh,
            Matrix4x4 localToWorld,
            int stride,
            float probeDistance,
            Vector3 windDir,
            Vector3 liftAxis,
            NavierStokesGridSolver navier,
            float vinf,
            float qInf,
            Vector3 referencePoint,
            ref Vector3 totalForce,
            ref Vector3 totalMoment,
            ref float pressureAccum,
            ref int sampleCount)
        {
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            if (vertices == null || vertices.Length == 0)
            {
                return false;
            }

            bool any = false;
            int triStride = Mathf.Max(1, stride);
            bool hasNormals = normals != null && normals.Length == vertices.Length;

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

                for (int tri = 0; tri < triangles.Length; tri += 3 * triStride)
                {
                    int ia = triangles[tri];
                    int ib = triangles[tri + 1];
                    int ic = triangles[tri + 2];
                    if (ia < 0 || ib < 0 || ic < 0 || ia >= vertices.Length || ib >= vertices.Length || ic >= vertices.Length)
                    {
                        continue;
                    }

                    Vector3 a = localToWorld.MultiplyPoint3x4(vertices[ia]);
                    Vector3 b = localToWorld.MultiplyPoint3x4(vertices[ib]);
                    Vector3 c = localToWorld.MultiplyPoint3x4(vertices[ic]);
                    Vector3 geometricNormal = Vector3.Cross(b - a, c - a);
                    float doubleArea = geometricNormal.magnitude;
                    if (doubleArea <= 1e-8f) continue;

                    Vector3 outwardNormal;
                    if (hasNormals)
                    {
                        outwardNormal = localToWorld.MultiplyVector((normals[ia] + normals[ib] + normals[ic]) / 3f).normalized;
                        if (outwardNormal.sqrMagnitude < 1e-6f)
                        {
                            outwardNormal = geometricNormal.normalized;
                        }
                    }
                    else
                    {
                        outwardNormal = geometricNormal.normalized;
                    }

                    Vector3 centroid = (a + b + c) / 3f;
                    float sampleArea = 0.5f * doubleArea * triStride;
                    SamplePoint(
                        centroid + outwardNormal * probeDistance,
                        outwardNormal,
                        sampleArea,
                        windDir,
                        liftAxis,
                        navier,
                        vinf,
                        qInf,
                        referencePoint,
                        ref totalForce,
                        ref totalMoment,
                        ref pressureAccum,
                        ref sampleCount);
                    any = true;
                }
            }

            return any;
        }

        private static int CountTriangleIndices(Mesh mesh)
        {
            if (mesh == null) return 0;

            int total = 0;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                if (mesh.GetTopology(subMesh) != MeshTopology.Triangles) continue;
                total += (int)mesh.GetIndexCount(subMesh);
            }

            return total;
        }

        private void FallbackRendererSampling(
            Vector3 windDir,
            Vector3 liftAxis,
            NavierStokesGridSolver navier,
            float vinf,
            float qInf,
            Vector3 referencePoint,
            ref Vector3 totalForce,
            ref Vector3 totalMoment,
            ref float pressureAccum,
            ref int sampleCount)
        {
            for (int i = 0; i < RendererCache.Count; i++)
            {
                Bounds b = RendererCache[i].bounds;
                Vector3 c = b.center;
                Vector3 e = b.extents;

                SamplePoint(c + new Vector3(e.x, 0f, 0f), Vector3.right, e.y * e.z, windDir, liftAxis, navier, vinf, qInf, referencePoint, ref totalForce, ref totalMoment, ref pressureAccum, ref sampleCount);
                SamplePoint(c + new Vector3(-e.x, 0f, 0f), Vector3.left, e.y * e.z, windDir, liftAxis, navier, vinf, qInf, referencePoint, ref totalForce, ref totalMoment, ref pressureAccum, ref sampleCount);
                SamplePoint(c + new Vector3(0f, e.y, 0f), Vector3.up, e.x * e.z, windDir, liftAxis, navier, vinf, qInf, referencePoint, ref totalForce, ref totalMoment, ref pressureAccum, ref sampleCount);
                SamplePoint(c + new Vector3(0f, -e.y, 0f), Vector3.down, e.x * e.z, windDir, liftAxis, navier, vinf, qInf, referencePoint, ref totalForce, ref totalMoment, ref pressureAccum, ref sampleCount);
                SamplePoint(c + new Vector3(0f, 0f, e.z), Vector3.forward, e.x * e.y, windDir, liftAxis, navier, vinf, qInf, referencePoint, ref totalForce, ref totalMoment, ref pressureAccum, ref sampleCount);
                SamplePoint(c + new Vector3(0f, 0f, -e.z), Vector3.back, e.x * e.y, windDir, liftAxis, navier, vinf, qInf, referencePoint, ref totalForce, ref totalMoment, ref pressureAccum, ref sampleCount);
            }
        }

        private void SamplePoint(
            Vector3 worldPos,
            Vector3 outwardNormal,
            float area,
            Vector3 windDir,
            Vector3 liftAxis,
            NavierStokesGridSolver navier,
            float vinf,
            float qInf,
            Vector3 referencePoint,
            ref Vector3 totalForce,
            ref Vector3 totalMoment,
            ref float pressureAccum,
            ref int sampleCount)
        {
            if (outwardNormal.sqrMagnitude < 1e-6f) return;
            outwardNormal.Normalize();

            float pLocal = 0f;
            Vector3 vel = Vector3.zero;
            bool sampled = navier != null && navier.TrySampleFlow(worldPos, out vel, out pLocal);
            if (!sampled || !float.IsFinite(pLocal) || !float.IsFinite(vel.x) || !float.IsFinite(vel.y) || !float.IsFinite(vel.z))
            {
                float facing = Mathf.Clamp(Vector3.Dot(outwardNormal, -windDir), -1f, 1f);
                float cp = facing >= 0f
                    ? Mathf.Pow(facing, 1.45f)
                    : -0.55f * Mathf.Pow(-facing, 1.10f);
                pLocal = cp * qInf;
                float localSpeedScale = facing >= 0f
                    ? Mathf.Lerp(0.28f, 0.95f, 1f - facing)
                    : Mathf.Lerp(1.05f, 1.25f, -facing);
                vel = windDir * (vinf * localSpeedScale);
            }

            float areaEff = Mathf.Max(area, 1e-5f);
            Vector3 pressureForce = -outwardNormal * (pLocal * pressureScale * areaEff);
            Vector3 tangentialVel = vel - Vector3.Project(vel, outwardNormal);
            Vector3 shearDir = tangentialVel.sqrMagnitude > 1e-6f ? tangentialVel.normalized : Vector3.zero;
            float liftBias = Mathf.Abs(Vector3.Dot(outwardNormal, liftAxis.normalized));
            Vector3 shearForce = shearDir * (qInf * shearScale * areaEff * Mathf.Lerp(0.05f, 0.10f, liftBias));

            Vector3 appliedForce = pressureForce + shearForce;
            totalForce += appliedForce;
            totalMoment += Vector3.Cross(worldPos - referencePoint, appliedForce);
            pressureAccum += pLocal;
            sampleCount++;
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
