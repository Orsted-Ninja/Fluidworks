using System.Collections.Generic;
using System.Text;
using AeroFlow.Core;
using UnityEngine;

namespace AeroFlow.Physics
{
    public class AeroGeometryAnalyzer : MonoBehaviour
    {
        public struct GeometricFeatures
        {
            public float aspectRatio;
            public float slendernessRatio;
            public float frontalBlockageRatio;
            public float volumeEfficiency;
            public float surfaceRoughness;
            public float symmetryScore;
            public float undersideClearance;
            public float rearTaperRatio;

            public float[] ToArray()
            {
                return new[]
                {
                    aspectRatio, slendernessRatio, frontalBlockageRatio, volumeEfficiency,
                    surfaceRoughness, symmetryScore, undersideClearance, rearTaperRatio
                };
            }
        }

        public struct AnalysisResult
        {
            public bool valid;
            public float overallScore;
            public string grade;
            public GeometricFeatures features;
            public string featureBreakdown;
            public string improvements;
            public float predictedCdLow;
            public float predictedCdHigh;
            public float separationRisk;
            public float downforcePotential;
            public float efficiencyScore;
        }

        private AnalysisResult cachedResult;
        private int lastAnalyzedModelId;

        private static readonly List<MeshFilter> MeshCache = new List<MeshFilter>(128);
        private static readonly List<SkinnedMeshRenderer> SkinnedCache = new List<SkinnedMeshRenderer>(32);

        public bool TryGetResult(out AnalysisResult result)
        {
            result = cachedResult;
            return cachedResult.valid;
        }

        public void AnalyzeModel(GameObject model, Bounds tunnelBounds, Vector3 flowDirection)
        {
            if (model == null) return;

            int modelId = model.GetInstanceID();
            if (modelId == lastAnalyzedModelId && cachedResult.valid) return;
            cachedResult = default;
            lastAnalyzedModelId = modelId;

            var renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            Bounds modelBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                modelBounds.Encapsulate(renderers[i].bounds);

            if (modelBounds.size.sqrMagnitude < 1e-6f) return;

            if (flowDirection.sqrMagnitude < 1e-6f) flowDirection = Vector3.right;
            flowDirection.Normalize();
            Vector3 upAxis = Vector3.up;
            Vector3 sideAxis = Vector3.Cross(upAxis, flowDirection).normalized;
            if (sideAxis.sqrMagnitude < 1e-6f) sideAxis = Vector3.right;

            GeometricFeatures features = ExtractFeatures(model, modelBounds, tunnelBounds, flowDirection, upAxis, sideAxis);
            float[] featureArray = features.ToArray();

            AeroMLPredictor.PredictionResult mlResult = AeroMLPredictor.Predict(featureArray);
            float[] importance = AeroMLPredictor.ComputeFeatureImportance(featureArray);

            float overallScore = ComputeOverallScore(features, mlResult);
            string grade = ScoreToGrade(overallScore);
            string breakdown = BuildFeatureBreakdown(features, importance);
            string improvements = BuildImprovements(features, mlResult, importance);

            cachedResult = new AnalysisResult
            {
                valid = true,
                overallScore = overallScore,
                grade = grade,
                features = features,
                featureBreakdown = breakdown,
                improvements = improvements,
                predictedCdLow = mlResult.predictedCd * 0.85f,
                predictedCdHigh = mlResult.predictedCd * 1.15f,
                separationRisk = mlResult.separationRisk,
                downforcePotential = mlResult.downforcePotential,
                efficiencyScore = mlResult.efficiencyScore
            };
        }

        public void InvalidateCache()
        {
            cachedResult = default;
            lastAnalyzedModelId = 0;
        }

        private GeometricFeatures ExtractFeatures(
            GameObject model, Bounds modelBounds, Bounds tunnelBounds,
            Vector3 flowDir, Vector3 upAxis, Vector3 sideAxis)
        {
            float length = ProjectSize(modelBounds, flowDir);
            float height = ProjectSize(modelBounds, upAxis);
            float width = ProjectSize(modelBounds, sideAxis);
            float maxCross = Mathf.Max(height, width);

            float aspectRatio = height > 1e-4f ? length / height : 1f;
            float slenderness = maxCross > 1e-4f ? length / maxCross : 1f;

            float frontalArea = height * width;
            float tunnelCross = ProjectSize(tunnelBounds, upAxis) * ProjectSize(tunnelBounds, sideAxis);
            float blockage = tunnelCross > 1e-4f ? frontalArea / tunnelCross : 0f;

            float volumeEff = ComputeVolumeEfficiency(model, modelBounds);
            float roughness = ComputeSurfaceRoughness(model);
            float symmetry = ComputeSymmetryScore(model, modelBounds, sideAxis);
            float clearance = ComputeUndersideClearance(model, modelBounds, tunnelBounds, upAxis);
            float rearTaper = ComputeRearTaperRatio(model, modelBounds, flowDir);

            return new GeometricFeatures
            {
                aspectRatio = aspectRatio,
                slendernessRatio = slenderness,
                frontalBlockageRatio = Mathf.Clamp01(blockage),
                volumeEfficiency = Mathf.Clamp01(volumeEff),
                surfaceRoughness = Mathf.Clamp01(roughness),
                symmetryScore = Mathf.Clamp01(symmetry),
                undersideClearance = Mathf.Clamp01(clearance),
                rearTaperRatio = Mathf.Clamp01(rearTaper)
            };
        }

        private static float ProjectSize(Bounds bounds, Vector3 axis)
        {
            Vector3 absAxis = new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
            return 2f * Vector3.Dot(bounds.extents, absAxis);
        }

        private float ComputeVolumeEfficiency(GameObject model, Bounds bounds)
        {
            float bbVolume = bounds.size.x * bounds.size.y * bounds.size.z;
            if (bbVolume < 1e-6f) return 0.5f;

            float meshVolume = 0f;
            Transform root = model.transform;
            MeshCache.Clear();
            root.GetComponentsInChildren(true, MeshCache);

            int sampleLimit = 6000;
            int totalTris = 0;

            for (int m = 0; m < MeshCache.Count; m++)
            {
                MeshFilter mf = MeshCache[m];
                if (mf == null || mf.sharedMesh == null) continue;
                Mesh mesh = mf.sharedMesh;
                Matrix4x4 ltw = mf.transform.localToWorldMatrix;
                Vector3[] verts = mesh.vertices;
                if (verts == null || verts.Length < 3) continue;

                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                    int[] tris = mesh.GetIndices(sub);
                    int stride = Mathf.Max(1, tris.Length / 3 / Mathf.Max(1, sampleLimit - totalTris));

                    for (int t = 0; t < tris.Length; t += 3 * stride)
                    {
                        if (tris[t] >= verts.Length || tris[t + 1] >= verts.Length || tris[t + 2] >= verts.Length) continue;
                        Vector3 a = ltw.MultiplyPoint3x4(verts[tris[t]]);
                        Vector3 b = ltw.MultiplyPoint3x4(verts[tris[t + 1]]);
                        Vector3 c = ltw.MultiplyPoint3x4(verts[tris[t + 2]]);
                        meshVolume += SignedTetraVolume(a, b, c) * stride;
                        totalTris++;
                    }
                }
            }

            meshVolume = Mathf.Abs(meshVolume);
            return Mathf.Clamp01(meshVolume / bbVolume);
        }

        private static float SignedTetraVolume(Vector3 a, Vector3 b, Vector3 c)
        {
            return Vector3.Dot(a, Vector3.Cross(b, c)) / 6f;
        }

        private float ComputeSurfaceRoughness(GameObject model)
        {
            Transform root = model.transform;
            MeshCache.Clear();
            root.GetComponentsInChildren(true, MeshCache);

            float totalDeviation = 0f;
            int pairCount = 0;
            int sampleLimit = 4000;

            for (int m = 0; m < MeshCache.Count; m++)
            {
                MeshFilter mf = MeshCache[m];
                if (mf == null || mf.sharedMesh == null) continue;
                Mesh mesh = mf.sharedMesh;
                Vector3[] normals = mesh.normals;
                if (normals == null || normals.Length < 3) continue;
                Matrix4x4 ltw = mf.transform.localToWorldMatrix;

                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                    int[] tris = mesh.GetIndices(sub);
                    int stride = Mathf.Max(1, tris.Length / 3 / Mathf.Max(1, sampleLimit - pairCount));

                    for (int t = 0; t + 5 < tris.Length; t += 6 * stride)
                    {
                        int ia = tris[t], ib = tris[t + 1], ic = tris[t + 2];
                        int id = tris[t + 3], ie = tris[t + 4], ifIdx = tris[t + 5];
                        if (ia >= normals.Length || ib >= normals.Length || ic >= normals.Length) continue;
                        if (id >= normals.Length || ie >= normals.Length || ifIdx >= normals.Length) continue;

                        Vector3 n1 = ltw.MultiplyVector((normals[ia] + normals[ib] + normals[ic]) / 3f).normalized;
                        Vector3 n2 = ltw.MultiplyVector((normals[id] + normals[ie] + normals[ifIdx]) / 3f).normalized;
                        totalDeviation += 1f - Mathf.Abs(Vector3.Dot(n1, n2));
                        pairCount++;
                    }
                }
            }

            return pairCount > 0 ? Mathf.Clamp01(totalDeviation / pairCount) : 0.3f;
        }

        private float ComputeSymmetryScore(GameObject model, Bounds modelBounds, Vector3 sideAxis)
        {
            Transform root = model.transform;
            MeshCache.Clear();
            root.GetComponentsInChildren(true, MeshCache);

            float centerPlane = Vector3.Dot(modelBounds.center, sideAxis);
            float totalScore = 0f;
            int sampleCount = 0;
            int sampleLimit = 3000;

            for (int m = 0; m < MeshCache.Count; m++)
            {
                MeshFilter mf = MeshCache[m];
                if (mf == null || mf.sharedMesh == null) continue;
                Vector3[] verts = mf.sharedMesh.vertices;
                if (verts == null) continue;
                Matrix4x4 ltw = mf.transform.localToWorldMatrix;
                int stride = Mathf.Max(1, verts.Length / Mathf.Max(1, sampleLimit - sampleCount));

                for (int v = 0; v < verts.Length && sampleCount < sampleLimit; v += stride)
                {
                    Vector3 worldVert = ltw.MultiplyPoint3x4(verts[v]);
                    float distFromCenter = Vector3.Dot(worldVert, sideAxis) - centerPlane;
                    Vector3 mirrored = worldVert - 2f * distFromCenter * sideAxis;

                    float halfWidth = Mathf.Max(ProjectSize(modelBounds, sideAxis) * 0.5f, 1e-4f);
                    float normalizedDist = Mathf.Abs(distFromCenter) / halfWidth;
                    totalScore += Mathf.Clamp01(1f - normalizedDist * 0.5f);
                    sampleCount++;
                }
            }

            return sampleCount > 0 ? totalScore / sampleCount : 0.5f;
        }

        private float ComputeUndersideClearance(GameObject model, Bounds modelBounds, Bounds tunnelBounds, Vector3 upAxis)
        {
            float modelBottom = Vector3.Dot(modelBounds.min, upAxis);
            float tunnelBottom = Vector3.Dot(tunnelBounds.min, upAxis);
            float modelHeight = ProjectSize(modelBounds, upAxis);

            float clearance = modelBottom - tunnelBottom;
            return modelHeight > 1e-4f ? Mathf.Clamp01(clearance / modelHeight) : 0f;
        }

        private float ComputeRearTaperRatio(GameObject model, Bounds modelBounds, Vector3 flowDir)
        {
            Transform root = model.transform;
            MeshCache.Clear();
            root.GetComponentsInChildren(true, MeshCache);

            float modelCenter = Vector3.Dot(modelBounds.center, flowDir);
            float halfLength = ProjectSize(modelBounds, flowDir) * 0.5f;
            float rearStart = modelCenter + halfLength * 0.5f;
            float rearEnd = modelCenter + halfLength;

            float frontArea = 0f, rearArea = 0f;
            int frontCount = 0, rearCount = 0;
            int sampleLimit = 3000;
            int sampled = 0;

            for (int m = 0; m < MeshCache.Count && sampled < sampleLimit; m++)
            {
                MeshFilter mf = MeshCache[m];
                if (mf == null || mf.sharedMesh == null) continue;
                Mesh mesh = mf.sharedMesh;
                Vector3[] verts = mesh.vertices;
                if (verts == null || verts.Length < 3) continue;
                Matrix4x4 ltw = mf.transform.localToWorldMatrix;

                for (int sub = 0; sub < mesh.subMeshCount && sampled < sampleLimit; sub++)
                {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                    int[] tris = mesh.GetIndices(sub);
                    int stride = Mathf.Max(1, tris.Length / 3 / Mathf.Max(1, sampleLimit - sampled));

                    for (int t = 0; t < tris.Length && sampled < sampleLimit; t += 3 * stride)
                    {
                        if (tris[t] >= verts.Length || tris[t + 1] >= verts.Length || tris[t + 2] >= verts.Length) continue;
                        Vector3 a = ltw.MultiplyPoint3x4(verts[tris[t]]);
                        Vector3 b = ltw.MultiplyPoint3x4(verts[tris[t + 1]]);
                        Vector3 c = ltw.MultiplyPoint3x4(verts[tris[t + 2]]);
                        Vector3 centroid = (a + b + c) / 3f;
                        float triArea = Vector3.Cross(b - a, c - a).magnitude * 0.5f * stride;
                        float axialPos = Vector3.Dot(centroid, flowDir);

                        if (axialPos < modelCenter)
                        {
                            frontArea += triArea;
                            frontCount++;
                        }
                        if (axialPos >= rearStart && axialPos <= rearEnd)
                        {
                            rearArea += triArea;
                            rearCount++;
                        }
                        sampled++;
                    }
                }
            }

            if (frontArea < 1e-6f) return 0.5f;
            float ratio = rearArea / frontArea;
            return Mathf.Clamp01(1f - ratio);
        }

        private float ComputeOverallScore(GeometricFeatures f, AeroMLPredictor.PredictionResult ml)
        {
            float geometryScore =
                Mathf.Clamp01(f.aspectRatio / 4f) * 15f +
                Mathf.Clamp01(f.slendernessRatio / 3.5f) * 15f +
                (1f - f.frontalBlockageRatio) * 10f +
                f.volumeEfficiency * 10f +
                (1f - f.surfaceRoughness) * 15f +
                f.symmetryScore * 10f +
                f.undersideClearance * 10f +
                f.rearTaperRatio * 15f;

            float mlScore = ml.efficiencyScore * 100f;
            return Mathf.Clamp(geometryScore * 0.6f + mlScore * 0.4f, 0f, 100f);
        }

        private static string ScoreToGrade(float score)
        {
            if (score >= 85f) return "Excellent";
            if (score >= 70f) return "Good";
            if (score >= 50f) return "Fair";
            if (score >= 30f) return "Poor";
            return "Needs Major Rework";
        }

        private static string FeatureGrade(float value, float excellent, float good, float fair)
        {
            if (value >= excellent) return "Excellent";
            if (value >= good) return "Good";
            if (value >= fair) return "Fair";
            return "Poor";
        }

        private string BuildFeatureBreakdown(GeometricFeatures f, float[] importance)
        {
            string[] names = { "Aspect Ratio", "Slenderness", "Blockage", "Volume Eff.", "Smoothness", "Symmetry", "Clearance", "Rear Taper" };
            float[] vals = f.ToArray();
            var sb = new StringBuilder(512);

            for (int i = 0; i < names.Length && i < vals.Length; i++)
            {
                string grade;
                switch (i)
                {
                    case 0: grade = FeatureGrade(vals[i], 3f, 2f, 1.2f); break;
                    case 1: grade = FeatureGrade(vals[i], 2.5f, 1.5f, 1f); break;
                    case 2: grade = FeatureGrade(1f - vals[i], 0.85f, 0.7f, 0.5f); break;
                    case 3: grade = FeatureGrade(vals[i], 0.7f, 0.45f, 0.25f); break;
                    case 4: grade = FeatureGrade(1f - vals[i], 0.8f, 0.6f, 0.4f); break;
                    case 5: grade = FeatureGrade(vals[i], 0.9f, 0.75f, 0.55f); break;
                    case 6: grade = FeatureGrade(vals[i], 0.15f, 0.08f, 0.03f); break;
                    case 7: grade = FeatureGrade(vals[i], 0.6f, 0.35f, 0.15f); break;
                    default: grade = "-"; break;
                }

                float imp = (i < importance.Length) ? importance[i] : 0f;
                sb.AppendLine($"{names[i]}: {vals[i]:F2} [{grade}] (impact: {imp:P0})");
            }

            return sb.ToString();
        }

        private string BuildImprovements(GeometricFeatures f, AeroMLPredictor.PredictionResult ml, float[] importance)
        {
            // Each suggestion is paired with its feature importance index for sorting
            var suggestions = new List<(string text, float impact)>(8);

            if (f.surfaceRoughness > 0.45f)
                suggestions.Add(("Smooth surface transitions: high normal variation indicates sharp edges that cause flow separation and increased drag.", importance.Length > 4 ? importance[4] : 0f));

            if (f.frontalBlockageRatio > 0.25f)
                suggestions.Add(($"Reduce frontal blockage ({f.frontalBlockageRatio:P0} of tunnel). Scale down the model or enlarge the tunnel to avoid wall interference.", importance.Length > 2 ? importance[2] : 0f));

            if (f.rearTaperRatio < 0.3f)
                suggestions.Add(("Add rear tapering: the back of the model ends abruptly, creating a large wake region. A gradual taper reduces form drag significantly.", importance.Length > 7 ? importance[7] : 0f));

            if (f.aspectRatio < 1.5f)
                suggestions.Add(("Low aspect ratio (boxy shape). Elongating the body in the flow direction reduces pressure drag.", importance.Length > 0 ? importance[0] : 0f));

            if (f.symmetryScore < 0.7f)
                suggestions.Add(("Asymmetric geometry detected. Improving lateral symmetry reduces crosswind sensitivity and yaw instability.", importance.Length > 5 ? importance[5] : 0f));

            if (f.undersideClearance < 0.05f)
                suggestions.Add(("Very low ground clearance. This can create ground-effect suction but may cause boundary layer issues at this resolution.", importance.Length > 6 ? importance[6] : 0f));

            if (f.volumeEfficiency < 0.3f)
                suggestions.Add(("Low volume efficiency (lots of concavities). Filling concave regions reduces separated flow zones.", importance.Length > 3 ? importance[3] : 0f));

            if (ml.separationRisk > 0.6f)
                suggestions.Add(($"ML predicts high flow separation risk ({ml.separationRisk:P0}). Consider adding vortex generators or smoothing leading edges.", 0.9f));

            if (ml.predictedCd > 0.5f)
                suggestions.Add(($"ML predicts high drag (Cd ~ {ml.predictedCd:F3}). Focus on reducing frontal area and improving rear geometry.", 0.95f));

            if (suggestions.Count == 0)
                suggestions.Add(("Model geometry is aerodynamically well-optimized for the current configuration.", 1f));

            // Sort by impact (highest first) so most important suggestions appear first
            suggestions.Sort((a, b) => b.impact.CompareTo(a.impact));

            var sb = new StringBuilder(256);
            for (int i = 0; i < suggestions.Count; i++)
            {
                sb.Append(i + 1).Append(". ").AppendLine(suggestions[i].text);
            }
            return sb.ToString();
        }
    }
}
