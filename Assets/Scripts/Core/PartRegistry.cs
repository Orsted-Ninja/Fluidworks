using System.Collections.Generic;
using UnityEngine;
using AeroFlow.Display;
using AeroFlow.Rendering;

namespace AeroFlow.Core
{
    public enum PartSegmentationCollection
    {
        Unclassified,
        StaticStructure,
        RotatingBlade,
        TranslatingPart,
        PhysicsDriven
    }

    public class PartRegistry : MonoBehaviour
    {
        [System.Serializable]
        public class PartInfo
        {
            public string partId;
            public Transform partTransform;
            public float referenceArea;
            public PartMotionSettings motionSettings;
            public PartSegmentationCollection segmentationCollection = PartSegmentationCollection.Unclassified;
        }

        [SerializeField] private List<PartInfo> parts = new List<PartInfo>();
        private static readonly Dictionary<PartSegmentationCollection, Material> SegmentationMaterials = new Dictionary<PartSegmentationCollection, Material>();

        private static readonly string[] RotatingNameHints =
        {
            "blade", "rotor", "impeller", "turbine", "propeller", "fan", "windmill", "rotating"
        };

        private static readonly string[] StaticNameHints =
        {
            "tower", "mast", "base", "housing", "frame", "stator", "support", "column", "static"
        };

        public IReadOnlyList<PartInfo> Parts => parts;
        public List<PartInfo> GetParts() => parts;

        public bool HasMovingParts()
        {
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part == null || part.motionSettings == null) continue;
                if (part.motionSettings.motionType != PartMotionType.Static)
                {
                    return true;
                }
            }

            return false;
        }

        public void SetPartCollection(PartInfo part, PartSegmentationCollection collection, bool applyVisuals = true)
        {
            if (part == null)
            {
                return;
            }

            part.segmentationCollection = collection;
            ApplyMotionForCollection(part);
            if (applyVisuals)
            {
                ApplySegmentationVisuals();
            }
        }

        public void ApplySegmentationVisuals()
        {
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part == null || part.partTransform == null) continue;

                var renderer = part.partTransform.GetComponent<Renderer>();
                if (renderer == null) continue;

                var state = renderer.GetComponent<RenderModeState>();
                if (state == null)
                {
                    state = renderer.gameObject.AddComponent<RenderModeState>();
                }
                state.CacheOriginal(renderer);
                state.ApplyOriginal(renderer);
                state.ApplySegmentationTint(renderer, GetCollectionMaterial(part.segmentationCollection));
            }
        }

        public static string GetCollectionLabel(PartSegmentationCollection collection)
        {
            switch (collection)
            {
                case PartSegmentationCollection.StaticStructure: return "Static Structure";
                case PartSegmentationCollection.RotatingBlade: return "Rotating Blade";
                case PartSegmentationCollection.TranslatingPart: return "Translating Part";
                case PartSegmentationCollection.PhysicsDriven: return "Physics Driven";
                default: return "Unclassified";
            }
        }

        public static PartSegmentationCollection ParseCollectionLabel(string value)
        {
            switch (value)
            {
                case "Static Structure": return PartSegmentationCollection.StaticStructure;
                case "Rotating Blade": return PartSegmentationCollection.RotatingBlade;
                case "Translating Part": return PartSegmentationCollection.TranslatingPart;
                case "Physics Driven": return PartSegmentationCollection.PhysicsDriven;
                default: return PartSegmentationCollection.Unclassified;
            }
        }

        public void Rebuild(Transform modelRoot, bool autoAddMovablePartComponents)
        {
            parts.Clear();
            if (modelRoot == null) return;

            var candidates = new List<Transform>();
            CollectRenderableLeafParts(modelRoot, candidates);

            if (candidates.Count == 0)
            {
                CollectRenderableFallbackParts(modelRoot, candidates);
            }

            if (candidates.Count == 0)
            {
                candidates.Add(modelRoot);
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                Transform t = candidates[i];
                if (!TryComputeBounds(t, out var bounds)) continue;

                var part = new PartInfo
                {
                    partId = string.IsNullOrEmpty(t.name) ? $"part_{i}" : t.name,
                    partTransform = t,
                    referenceArea = EstimateReferenceArea(bounds.size)
                };

                var motion = t.GetComponent<PartMotionSettings>();
                if (motion == null)
                {
                    motion = t.gameObject.AddComponent<PartMotionSettings>();
                }
                motion.partId = part.partId;
                part.motionSettings = motion;
                part.segmentationCollection = PartSegmentationCollection.Unclassified;

                parts.Add(part);

                if (autoAddMovablePartComponents && t.GetComponent<MovablePart>() == null)
                {
                    var movable = t.gameObject.AddComponent<MovablePart>();
                    movable.partId = part.partId;
                }
            }
        }

        public void AutoIdentifyParts()
        {
            if (parts.Count == 0) return;

            if (!TryGetCombinedBounds(out var modelBounds))
            {
                modelBounds = default;
            }

            var movingParts = new List<PartInfo>();
            var staticParts = new List<PartInfo>();

            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part == null || part.partTransform == null || part.motionSettings == null) continue;

                part.motionSettings.partId = part.partId;
                if (IsLikelyRotatingPart(part, modelBounds))
                {
                    movingParts.Add(part);
                }
                else
                {
                    staticParts.Add(part);
                }
            }

            if (movingParts.Count == 0)
            {
                for (int i = 0; i < parts.Count; i++)
                {
                    var part = parts[i];
                    part?.motionSettings?.AutoConfigure();
                    if (part != null)
                    {
                        SyncCollectionFromMotion(part);
                        ApplyMotionForCollection(part);
                    }
                }
                ApplySegmentationVisuals();
                return;
            }

            Vector3 rotationAxis = EstimateRotationAxis(movingParts);
            Vector3 pivot = EstimateRotationPivot(movingParts);
            float rpm = EstimateRotationSpeed(movingParts);

            for (int i = 0; i < movingParts.Count; i++)
            {
                var motion = movingParts[i].motionSettings;
                if (motion == null) continue;

                motion.motionType = PartMotionType.ConstantRotation;
                motion.intensity = Mathf.Max(motion.intensity, rpm);
                motion.axis = rotationAxis;
                motion.useWorldAxis = true;
                motion.SetWorldPivot(pivot);
                movingParts[i].segmentationCollection = PartSegmentationCollection.RotatingBlade;
            }

            for (int i = 0; i < staticParts.Count; i++)
            {
                var motion = staticParts[i].motionSettings;
                if (motion == null) continue;

                if (motion.motionType == PartMotionType.Static)
                {
                    motion.ClearWorldPivot();
                }
                staticParts[i].segmentationCollection = PartSegmentationCollection.StaticStructure;
            }

            ApplySegmentationVisuals();
        }

        public bool TryGetCombinedBounds(out Bounds bounds)
        {
            bounds = default;
            bool initialized = false;

            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                if (p == null || p.partTransform == null) continue;
                if (!TryComputeBounds(p.partTransform, out var partBounds)) continue;

                if (!initialized)
                {
                    bounds = partBounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(partBounds);
                }
            }

            return initialized;
        }

        public bool TryGetPartBounds(PartInfo part, out Bounds bounds)
        {
            bounds = default;
            if (part == null || part.partTransform == null) return false;
            return TryComputeBounds(part.partTransform, out bounds);
        }

        private static void CollectRenderableLeafParts(Transform root, List<Transform> result)
        {
            if (root == null) return;

            if (IsRenderableLeaf(root))
            {
                result.Add(root);
                return;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                CollectRenderableLeafParts(root.GetChild(i), result);
            }
        }

        private static void CollectRenderableFallbackParts(Transform root, List<Transform> result)
        {
            if (root == null) return;

            if (HasRenderableGeometry(root))
            {
                result.Add(root);
                return;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                CollectRenderableFallbackParts(root.GetChild(i), result);
            }
        }

        private static bool IsRenderableLeaf(Transform root)
        {
            if (root == null) return false;
            if (root.GetComponent<RuntimeSimulationProxy>() != null) return false;
            if (!HasDirectRenderable(root)) return false;

            for (int i = 0; i < root.childCount; i++)
            {
                if (HasRenderableGeometry(root.GetChild(i)))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasDirectRenderable(Transform root)
        {
            if (root == null) return false;
            return root.GetComponent<Renderer>() != null
                || root.GetComponent<MeshFilter>() != null
                || root.GetComponent<SkinnedMeshRenderer>() != null;
        }

        private static bool IsLikelyRotatingPart(PartInfo part, Bounds modelBounds)
        {
            if (part == null || part.partTransform == null) return false;

            string name = string.IsNullOrEmpty(part.partId) ? part.partTransform.name : part.partId;
            string lowerName = name.ToLowerInvariant();

            for (int i = 0; i < StaticNameHints.Length; i++)
            {
                if (lowerName.Contains(StaticNameHints[i]))
                {
                    return false;
                }
            }

            for (int i = 0; i < RotatingNameHints.Length; i++)
            {
                if (lowerName.Contains(RotatingNameHints[i]))
                {
                    return true;
                }
            }

            if (!TryComputeBounds(part.partTransform, out var bounds))
            {
                return false;
            }

            Vector3 size = bounds.size;
            float largest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            float mid = Mathf.Max(Mathf.Min(Mathf.Max(size.x, size.y), Mathf.Max(size.y, size.z)), Mathf.Min(size.x, size.z));
            float smallest = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            float thinness = smallest / Mathf.Max(largest, 1e-4f);
            float slenderness = largest / Mathf.Max(mid, 1e-4f);
            float modelLargest = Mathf.Max(modelBounds.size.x, Mathf.Max(modelBounds.size.y, modelBounds.size.z));
            bool shellLike = thinness < 0.22f && largest > modelLargest * 0.12f;
            bool bladeLike = slenderness > 1.9f && largest > modelLargest * 0.10f;
            return shellLike || bladeLike;
        }

        private static Vector3 EstimateRotationAxis(List<PartInfo> movingParts)
        {
            if (movingParts == null || movingParts.Count == 0)
            {
                return Vector3.up;
            }

            Vector3 min = movingParts[0].partTransform.position;
            Vector3 max = min;
            for (int i = 0; i < movingParts.Count; i++)
            {
                Vector3 p = movingParts[i].partTransform.position;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            Vector3 extents = max - min;
            if (extents.x <= extents.y && extents.x <= extents.z) return Vector3.right;
            if (extents.y <= extents.x && extents.y <= extents.z) return Vector3.up;
            return Vector3.forward;
        }

        private static Vector3 EstimateRotationPivot(List<PartInfo> movingParts)
        {
            if (movingParts == null || movingParts.Count == 0)
            {
                return Vector3.zero;
            }

            Vector3 sum = Vector3.zero;
            int count = 0;
            for (int i = 0; i < movingParts.Count; i++)
            {
                if (movingParts[i]?.partTransform == null) continue;
                sum += movingParts[i].partTransform.position;
                count++;
            }

            return count > 0 ? sum / count : Vector3.zero;
        }

        private static float EstimateRotationSpeed(List<PartInfo> movingParts)
        {
            if (movingParts == null || movingParts.Count == 0) return 90f;

            for (int i = 0; i < movingParts.Count; i++)
            {
                string name = movingParts[i] != null ? (movingParts[i].partId ?? movingParts[i].partTransform.name) : "";
                string lower = name.ToLowerInvariant();
                if (lower.Contains("windmill") || lower.Contains("turbine"))
                {
                    return 60f;
                }
                if (lower.Contains("fan") || lower.Contains("impeller"))
                {
                    return 900f;
                }
            }

            return 120f;
        }

        private static bool TryComputeBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            if (root == null) return false;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bool initialized = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                if (renderer.GetComponentInParent<RuntimeSimulationProxy>() != null) continue;

                if (!initialized)
                {
                    bounds = renderer.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return initialized;
        }

        private static bool HasRenderableGeometry(Transform root)
        {
            return TryComputeBounds(root, out _);
        }

        private static float EstimateReferenceArea(Vector3 size)
        {
            float yz = Mathf.Max(size.y * size.z, 1e-6f);
            float xz = Mathf.Max(size.x * size.z, 1e-6f);
            float xy = Mathf.Max(size.x * size.y, 1e-6f);
            return Mathf.Max(yz, Mathf.Max(xz, xy));
        }

        private void ApplyMotionForCollection(PartInfo part)
        {
            if (part == null || part.motionSettings == null) return;

            switch (part.segmentationCollection)
            {
                case PartSegmentationCollection.RotatingBlade:
                    part.motionSettings.motionType = PartMotionType.ConstantRotation;
                    if (part.motionSettings.intensity <= 0f)
                    {
                        part.motionSettings.intensity = EstimateRotationSpeed(new List<PartInfo> { part });
                    }
                    break;
                case PartSegmentationCollection.TranslatingPart:
                    part.motionSettings.motionType = PartMotionType.ConstantTranslation;
                    break;
                case PartSegmentationCollection.PhysicsDriven:
                    part.motionSettings.motionType = PartMotionType.PhysicsDriven;
                    break;
                case PartSegmentationCollection.StaticStructure:
                case PartSegmentationCollection.Unclassified:
                default:
                    part.motionSettings.motionType = PartMotionType.Static;
                    part.motionSettings.ClearWorldPivot();
                    break;
            }
        }

        private void SyncCollectionFromMotion(PartInfo part)
        {
            if (part == null || part.motionSettings == null) return;

            switch (part.motionSettings.motionType)
            {
                case PartMotionType.ConstantRotation:
                    part.segmentationCollection = PartSegmentationCollection.RotatingBlade;
                    break;
                case PartMotionType.ConstantTranslation:
                    part.segmentationCollection = PartSegmentationCollection.TranslatingPart;
                    break;
                case PartMotionType.PhysicsDriven:
                    part.segmentationCollection = PartSegmentationCollection.PhysicsDriven;
                    break;
                case PartMotionType.Static:
                default:
                    part.segmentationCollection = PartSegmentationCollection.StaticStructure;
                    break;
            }
        }

        private static Material GetCollectionMaterial(PartSegmentationCollection collection)
        {
            if (SegmentationMaterials.TryGetValue(collection, out var existing) && existing != null)
            {
                return existing;
            }

            Shader shader = RuntimeShaderResolver.FindLitShader()
                         ?? RuntimeShaderResolver.FindPressureShader()
                         ?? RuntimeShaderResolver.FindSimpleUnlitShader()
                         ?? Shader.Find("Standard");
            if (shader == null)
            {
                return null;
            }

            var material = new Material(shader)
            {
                name = $"Segmentation_{collection}"
            };

            Color color = GetCollectionColor(collection);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", color * 0.65f);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            SegmentationMaterials[collection] = material;
            return material;
        }

        private static Color GetCollectionColor(PartSegmentationCollection collection)
        {
            switch (collection)
            {
                case PartSegmentationCollection.StaticStructure:
                    return new Color(0.24f, 0.58f, 1.00f, 0.28f);
                case PartSegmentationCollection.RotatingBlade:
                    return new Color(0.10f, 0.95f, 0.85f, 0.34f);
                case PartSegmentationCollection.TranslatingPart:
                    return new Color(1.00f, 0.74f, 0.18f, 0.30f);
                case PartSegmentationCollection.PhysicsDriven:
                    return new Color(0.84f, 0.40f, 1.00f, 0.30f);
                default:
                    return new Color(0.80f, 0.85f, 0.92f, 0.20f);
            }
        }
    }
}
