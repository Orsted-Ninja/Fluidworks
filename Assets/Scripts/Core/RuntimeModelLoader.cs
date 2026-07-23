using System.Collections.Generic;
using System.IO;
using System;
using System.Threading;
using System.Threading.Tasks;
using AeroFlow.Managers;
using AeroFlow.Rendering;
using AeroFlow.Sim3D.RotatingMachinery;
using GLTFast;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_STANDALONE || UNITY_EDITOR
using SFB;
#endif

namespace AeroFlow.Core
{
    /// <summary>
    /// Loads runtime geometry for both visualization and solver proxy workflows.
    /// Visual models support GLB/GLTF/OBJ/STL. Optional simulation geometry can be
    /// attached as a hidden proxy mesh for the wind-tunnel solver.
    /// </summary>
    public class RuntimeModelLoader : MonoBehaviour
    {
        [Header("Configuration")]
        public Transform modelPivot;
        public Material defaultMaterial;
        public Vector3 modelOffset;

        [Header("Dual Asset Workflow")]
        public bool promptForSimulationModel = false;
        public bool autoRegisterParts = true;
        public bool autoAddMovablePartComponents = false;
        public bool autoConfigurePartMotion = false;
        public bool autoSegmentSingleMeshModels = false;

        [Header("Dependencies")]
        public SimulationManager simManager;

        private static readonly List<Renderer> VisibleRendererCache = new List<Renderer>(128);
        private static readonly Color VisibleModelColor = new Color(0.78f, 0.80f, 0.84f, 1f);

        private GameObject currentModelInstance;
        private int loadRequestId;
        private bool isLoadInProgress;
        private CancellationTokenSource activeLoadCancellation;
        private Material runtimeDefaultMaterial;
        public DualModelDescriptor CurrentDescriptor { get; private set; }
        public MeshValidationReport CurrentMeshValidationReport { get; private set; }
        public PartRegistry CurrentPartRegistry { get; private set; }
        public Transform CurrentSimulationGeometryRoot { get; private set; }

        private Vector3 baseModelPosition;
        private Quaternion baseModelRotation = Quaternion.identity;
        private Vector3 baseModelScale = Vector3.one;
        private Transform baseModelParent;
        private float modelScaleFactor = 1f;
        private float autoFitScaleFactor = 1f;
        private bool damBreakInitialFitApplied;

        public void OpenFilePicker()
        {
#if UNITY_STANDALONE || UNITY_EDITOR
            if (isLoadInProgress)
            {
                return;
            }

            isLoadInProgress = true;

            var filters = new[]
            {
                new ExtensionFilter("Visual Models", "glb", "gltf", "obj", "stl")
            };

            StandaloneFileBrowser.OpenFilePanelAsync("Open Visual Model", "", filters, false, (visualPaths) =>
            {
                string visualPath = visualPaths != null && visualPaths.Length > 0 ? visualPaths[0] : null;
                if (string.IsNullOrWhiteSpace(visualPath))
                {
                    isLoadInProgress = false;
                    return;
                }

                if (promptForSimulationModel)
                {
                    var simFilters = new[]
                    {
                        new ExtensionFilter("Simulation Mesh / CAD", "obj", "stl", "step", "stp", "iges", "igs", "glb", "gltf")
                    };
                    StandaloneFileBrowser.OpenFilePanelAsync("Optional Simulation Mesh / CAD", "", simFilters, false, (simPaths) =>
                    {
                        string simulationPath = simPaths != null && simPaths.Length > 0 ? simPaths[0] : null;
                        LoadModel(visualPath, simulationPath);
                    });
                }
                else
                {
                    LoadModel(visualPath, null);
                }
            });
#else
            Debug.LogWarning("StandaloneFileBrowser is only supported on Desktop builds. Cannot open file picker.");
            string dummyPath = Path.Combine(Application.streamingAssetsPath, "model.glb");
            if (File.Exists(dummyPath))
            {
                isLoadInProgress = true;
                LoadModel(dummyPath);
            }
#endif
        }

        public async void LoadModel(string visualPath, string simulationPath = null)
        {
            if (string.IsNullOrWhiteSpace(visualPath))
            {
                return;
            }

            int requestId = ++loadRequestId;
            CancellationTokenSource cancellationSource = BeginLoadCancellation();
            CancellationToken cancellationToken = cancellationSource.Token;
            GameObject pendingModelInstance = null;
            try
            {
                await EnsureSimulationContextAsync(cancellationToken);
                if (requestId != loadRequestId)
                {
                    return;
                }

                ClearCurrentModel();
                EnsureDefaultMaterial();

                var descriptor = new DualModelDescriptor
                {
                    sourceObjectId = Path.GetFileNameWithoutExtension(visualPath),
                    visualModelPath = visualPath,
                    simulationModelPath = string.IsNullOrWhiteSpace(simulationPath) ? null : simulationPath
                };

                pendingModelInstance = CreatePendingModelRoot(requestId);
                bool visualLoaded = await LoadVisualModelAsync(visualPath, pendingModelInstance, requestId, cancellationToken);

                if (requestId != loadRequestId || pendingModelInstance == null)
                {
                    DestroyModelRoot(pendingModelInstance);
                    return;
                }

                if (!visualLoaded)
                {
                    Debug.LogError($"[ModelLoader] Failed to load visual model at path: {visualPath}");
                    DestroyModelRoot(pendingModelInstance);
                    return;
                }

                AttachModelRoot(pendingModelInstance);
                pendingModelInstance.name = "LoadedModel";
                currentModelInstance = pendingModelInstance;
                CurrentDescriptor = descriptor;
                AttachSimulationProxy(simulationPath);
                PostProcessModel();
                NotifyModelLoaded();
            }
            catch (OperationCanceledException)
            {
                if (requestId == loadRequestId)
                {
                    DestroyModelRoot(pendingModelInstance);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ModelLoader] Critical exception during load: {ex}");
                if (requestId == loadRequestId)
                {
                    ClearCurrentModel();
                }
                else
                {
                    DestroyModelRoot(pendingModelInstance);
                }
            }
            finally
            {
                if (ReferenceEquals(activeLoadCancellation, cancellationSource))
                {
                    activeLoadCancellation = null;
                }

                cancellationSource.Dispose();

                if (requestId == loadRequestId)
                {
                    isLoadInProgress = false;
                }
            }
        }

        private void ClearCurrentModel()
        {
            if (currentModelInstance != null)
            {
                DestroyModelRoot(currentModelInstance);
            }
            
            RuntimeModelLookup.ClearCache();

            currentModelInstance = null;
            CurrentDescriptor = null;
            CurrentMeshValidationReport = default;
            CurrentPartRegistry = null;
            CurrentSimulationGeometryRoot = null;
            autoFitScaleFactor = 1f;
            modelScaleFactor = 1f;
            baseModelScale = Vector3.one;
            damBreakInitialFitApplied = false;
        }

        private GameObject CreatePendingModelRoot(int requestId)
        {
            var root = new GameObject($"LoadedModel_Pending_{requestId}");
            if (this != null)
            {
                root.transform.SetParent(transform, false);
            }

            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            return root;
        }

        private void AttachModelRoot(GameObject modelRoot)
        {
            if (modelRoot == null) return;
            if (modelPivot != null && !HasActiveSimulationContext())
            {
                modelRoot.transform.SetParent(modelPivot, false);
            }

            modelRoot.transform.localPosition = Vector3.zero;
            modelRoot.transform.localRotation = Quaternion.identity;
            modelRoot.transform.localScale = Vector3.one;
            baseModelScale = modelRoot.transform.localScale;
            autoFitScaleFactor = 1f;
            modelScaleFactor = 1f;
            damBreakInitialFitApplied = false;
        }

        private bool HasActiveSimulationContext()
        {
            return FindAnyObjectByType<WindTunnelSimulation3D>() != null
                || FindAnyObjectByType<Simulation3D>() != null
                || FindAnyObjectByType<AeroFlow.Sim3D.PipeFlow.PipeFlowSimulation3D>() != null
                || FindAnyObjectByType<RotatingMachinerySimulation3D>() != null;
        }

        private static void DestroyModelRoot(GameObject modelRoot)
        {
            if (modelRoot == null) return;
            if (modelRoot.name == "LoadedModel")
            {
                modelRoot.name = $"LoadedModel_Discarded_{Time.frameCount}";
            }
            UnityEngine.Object.Destroy(modelRoot);
        }

        private async Task<bool> LoadVisualModelAsync(string visualPath, GameObject targetRoot, int requestId, CancellationToken cancellationToken)
        {
            if (targetRoot == null)
            {
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();

            string extension = Path.GetExtension(visualPath).ToLowerInvariant();
            if (RuntimeMeshImporter.SupportsRuntimeMesh(visualPath))
            {
                if (cancellationToken.IsCancellationRequested || requestId != loadRequestId || targetRoot == null)
                {
                    return false;
                }

                GameObject imported = RuntimeMeshImporter.Import(visualPath, targetRoot.transform, defaultMaterial, true, "VisualModel");
                return imported != null;
            }

            if (extension != ".glb" && extension != ".gltf")
            {
                Debug.LogError($"[ModelLoader] Unsupported visual model format: {visualPath}");
                return false;
            }

            var gltf = new GltfImport();
            bool loaded = await gltf.Load(visualPath);
            if (!loaded)
            {
                return false;
            }

            if (cancellationToken.IsCancellationRequested || requestId != loadRequestId || targetRoot == null)
            {
                return false;
            }

            Transform targetTransform = SafeGetTransform(targetRoot);
            if (targetTransform == null)
            {
                return false;
            }

            try
            {
                bool instantiated = await gltf.InstantiateMainSceneAsync(targetTransform, cancellationToken);
                return instantiated && !cancellationToken.IsCancellationRequested && requestId == loadRequestId && targetRoot != null;
            }
            catch (MissingReferenceException)
            {
                return false;
            }
        }

        private void AttachSimulationProxy(string simulationPath)
        {
            CurrentSimulationGeometryRoot = null;
            if (currentModelInstance == null || string.IsNullOrWhiteSpace(simulationPath))
            {
                return;
            }

            if (!RuntimeMeshImporter.SupportsSimulationReference(simulationPath))
            {
                Debug.LogWarning($"[ModelLoader] Unsupported simulation geometry reference: {simulationPath}");
                return;
            }

            if (!RuntimeMeshImporter.SupportsRuntimeMesh(simulationPath))
            {
                Debug.Log($"[ModelLoader] Stored simulation CAD reference only: {simulationPath}");
                return;
            }

            var proxyRoot = new GameObject("SimulationProxy");
            proxyRoot.transform.SetParent(currentModelInstance.transform, false);
            proxyRoot.transform.localPosition = Vector3.zero;
            proxyRoot.transform.localRotation = Quaternion.identity;
            proxyRoot.transform.localScale = Vector3.one;

            var marker = proxyRoot.AddComponent<RuntimeSimulationProxy>();
            marker.Initialize(simulationPath);

            GameObject proxyMesh = RuntimeMeshImporter.Import(simulationPath, proxyRoot.transform, defaultMaterial, false, "SimulationMesh");
            if (proxyMesh == null)
            {
                Destroy(proxyRoot);
                return;
            }

            CurrentSimulationGeometryRoot = proxyRoot.transform;
        }

        private async Task EnsureSimulationContextAsync(CancellationToken cancellationToken)
        {
            WindTunnelSimulation3D activeWind = FindAnyObjectByType<WindTunnelSimulation3D>();
            var activePipe = FindAnyObjectByType<AeroFlow.Sim3D.PipeFlow.PipeFlowSimulation3D>();
            var activeMachinery = FindAnyObjectByType<RotatingMachinerySimulation3D>();
            Simulation3D activeDam = FindAnyObjectByType<Simulation3D>();
            var ui = FindAnyObjectByType<UI.MainScreenController>();
            if (ui != null)
            {
                ui.EnsureActiveWorkflowSceneLoaded();
            }

            if ((activeWind != null || activePipe != null || activeMachinery != null || activeDam != null)
                && (ui == null || !ui.IsEnvironmentLoading()))
            {
                return;
            }

            float timeoutAt = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < timeoutAt)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (ui != null && ui.IsEnvironmentLoading())
                {
                    await Task.Yield();
                    continue;
                }

                activeWind = FindAnyObjectByType<WindTunnelSimulation3D>();
                activePipe = FindAnyObjectByType<AeroFlow.Sim3D.PipeFlow.PipeFlowSimulation3D>();
                activeMachinery = FindAnyObjectByType<RotatingMachinerySimulation3D>();
                activeDam = FindAnyObjectByType<Simulation3D>();
                if (activeWind != null || activePipe != null || activeMachinery != null || activeDam != null)
                {
                    await Task.Yield();
                    return;
                }

                await Task.Yield();
            }

            Debug.LogWarning("[ModelLoader] Continuing without an active simulation context after waiting for scene load.");
        }

        private CancellationTokenSource BeginLoadCancellation()
        {
            CancelActiveLoad();
            activeLoadCancellation = new CancellationTokenSource();
            return activeLoadCancellation;
        }

        private void CancelActiveLoad()
        {
            if (activeLoadCancellation == null)
            {
                return;
            }

            if (!activeLoadCancellation.IsCancellationRequested)
            {
                activeLoadCancellation.Cancel();
            }

            activeLoadCancellation.Dispose();
            activeLoadCancellation = null;
        }

        private static Transform SafeGetTransform(GameObject targetRoot)
        {
            if (targetRoot == null)
            {
                return null;
            }

            try
            {
                return targetRoot.transform;
            }
            catch (MissingReferenceException)
            {
                return null;
            }
        }

        private void NotifyModelLoaded()
        {
            if (simManager != null)
            {
                simManager.OnModelLoaded();
            }
            else
            {
                Debug.LogWarning("[ModelLoader] SimulationManager is missing.");
            }

            var mainScreen = FindAnyObjectByType<UI.MainScreenController>();
            if (mainScreen != null)
            {
                mainScreen.HideLoadPrompt();
                mainScreen.OnRuntimeModelLoaded();
            }
        }

        private void PostProcessModel()
        {
            if (currentModelInstance == null)
            {
                return;
            }

            if (!TryGetRenderableBounds(out Bounds bounds))
            {
                Debug.LogWarning("[ModelLoader] Imported model has no visible renderers.");
                return;
            }

            float maxDim = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (maxDim > 1e-5f)
            {
                currentModelInstance.transform.localScale *= 3.0f / maxDim;
                TryGetRenderableBounds(out bounds);
            }

            AlignToSimulationContext();
            TryGetRenderableBounds(out bounds);

            Rigidbody rb = currentModelInstance.GetComponent<Rigidbody>();
            if (rb == null) rb = currentModelInstance.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            if (currentModelInstance.GetComponent<ModelDragController>() == null)
            {
                var drag = currentModelInstance.AddComponent<ModelDragController>();
                drag.dragPlaneHeight = currentModelInstance.transform.position.y;
            }

            if (autoSegmentSingleMeshModels && currentModelInstance != null)
            {
                MeshSegmentationUtility.TryAutoSegmentSingleMesh(currentModelInstance, out _);
            }

            CollectVisibleRenderers(VisibleRendererCache);
            for (int i = 0; i < VisibleRendererCache.Count; i++)
            {
                Renderer renderer = VisibleRendererCache[i];
                if (renderer == null) continue;

                EnsureRendererVisible(renderer);
                ApplyDefaultMaterial(renderer);

                var meshFilter = renderer.GetComponent<MeshFilter>();
                if (meshFilter != null)
                {
                    var pressureVis = renderer.GetComponent<AeroFlow.Visualization.SurfacePressureVisualizer>();
                    if (pressureVis == null)
                    {
                        pressureVis = renderer.gameObject.AddComponent<AeroFlow.Visualization.SurfacePressureVisualizer>();
                    }
                    pressureVis.enabled = false;
                }
            }

            Debug.Log($"[ModelLoader] Imported visible model with {VisibleRendererCache.Count} renderer(s). Bounds: {bounds.size} at {bounds.center}.");

            CurrentMeshValidationReport = MeshValidationUtility.Validate(currentModelInstance);
            if (CurrentMeshValidationReport.meshCount > 0)
            {
                if (CurrentMeshValidationReport.valid)
                {
                    Debug.Log($"[ModelLoader] Mesh validation passed. {CurrentMeshValidationReport.summary}. {CurrentMeshValidationReport.suggestions}");
                }
                else
                {
                    Debug.LogWarning($"[ModelLoader] Mesh validation found issues. {CurrentMeshValidationReport.summary}. {CurrentMeshValidationReport.suggestions}");
                }
            }

            EnsureRootCollider(bounds);

            if (autoRegisterParts)
            {
                CurrentPartRegistry = currentModelInstance.GetComponent<PartRegistry>();
                if (CurrentPartRegistry == null)
                {
                    CurrentPartRegistry = currentModelInstance.AddComponent<PartRegistry>();
                }

                CurrentPartRegistry.Rebuild(currentModelInstance.transform, autoAddMovablePartComponents);
                if (autoConfigurePartMotion)
                {
                    CurrentPartRegistry.AutoIdentifyParts();
                }
                CurrentPartRegistry.ApplySegmentationVisuals();
                if (CurrentPartRegistry.HasMovingParts() || autoConfigurePartMotion)
                {
                    EnsurePartMotionAnimator();
                }

                var loadIntegrator = FindAnyObjectByType<AeroFlow.Physics.FluidLoadIntegrator>();
                if (loadIntegrator != null)
                {
                    loadIntegrator.partRegistry = CurrentPartRegistry;
                }
            }

            var bootstrapper = FindAnyObjectByType<AeroFlow.Display.VisualsBootstrapper>();
            if (bootstrapper != null)
            {
                bootstrapper.SetRenderMode(AeroFlow.Display.VisualsBootstrapper.LastRenderMode);
            }

            var camController = FindAnyObjectByType<AeroFlow.UI.CameraController>();
            if (camController != null)
            {
                camController.FrameBounds(bounds, 1.35f);
            }
        }

        private void EnsurePartMotionAnimator()
        {
            if (currentModelInstance == null || CurrentPartRegistry == null)
            {
                return;
            }

            var animator = currentModelInstance.GetComponent<PartMotionAnimator>();
            if (animator == null)
            {
                animator = currentModelInstance.AddComponent<PartMotionAnimator>();
            }
            animator.partRegistry = CurrentPartRegistry;
        }

        private void EnsureDefaultMaterial()
        {
            Shader shader = RuntimeShaderResolver.FindLitShader();
            Material sourceMaterial = defaultMaterial;
            if (sourceMaterial == null && shader == null)
            {
                return;
            }

            if (runtimeDefaultMaterial == null)
            {
                runtimeDefaultMaterial = sourceMaterial != null
                    ? new Material(sourceMaterial)
                    : new Material(shader);
                runtimeDefaultMaterial.name = "RuntimeDefaultModelMaterial";
            }

            SanitizeVisibleMaterial(runtimeDefaultMaterial);
            defaultMaterial = runtimeDefaultMaterial;
        }

        private void ApplyDefaultMaterial(Renderer renderer)
        {
            if (renderer == null || defaultMaterial == null)
            {
                return;
            }

            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
            {
                materials = new Material[1];
            }

            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = defaultMaterial;
            }
            renderer.sharedMaterials = materials;
        }

        private void EnsureRendererVisible(Renderer renderer)
        {
            if (renderer == null) return;

            Transform current = renderer.transform;
            while (current != null)
            {
                if (!current.gameObject.activeSelf)
                {
                    current.gameObject.SetActive(true);
                }

                if (current == currentModelInstance?.transform)
                {
                    break;
                }

                current = current.parent;
            }

            renderer.forceRenderingOff = false;
            renderer.enabled = true;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        private static void SanitizeVisibleMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", VisibleModelColor);
            if (material.HasProperty("_Color")) material.SetColor("_Color", VisibleModelColor);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.62f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.62f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.18f);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 0f);
            if (material.HasProperty("_Mode")) material.SetFloat("_Mode", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.One);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 1f);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)CullMode.Off);
            if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
            if (material.HasProperty("_AlphaToMask")) material.SetFloat("_AlphaToMask", 0f);

            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = -1;
        }

        private void CollectVisibleRenderers(List<Renderer> results)
        {
            results.Clear();
            if (currentModelInstance == null)
            {
                return;
            }

            Renderer[] renderers = currentModelInstance.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                if (renderer.GetComponentInParent<RuntimeSimulationProxy>() != null) continue;
                results.Add(renderer);
            }
        }

        public void SetModelOffset(Vector3 offset)
        {
            modelOffset = offset;
            ApplyModelOffset();
        }

        private void ApplyModelOffset()
        {
            if (currentModelInstance == null) return;
            currentModelInstance.transform.position = baseModelPosition + modelOffset;
        }

        public void SetModelScale(float scaleFactor)
        {
            modelScaleFactor = Mathf.Max(0.05f, scaleFactor);
            ApplyModelScale();
        }

        public float GetModelScale()
        {
            return modelScaleFactor;
        }

        public Vector3 GetModelOffset()
        {
            return modelOffset;
        }

        public void RefreshModelPlacement(bool resetPhysics = true)
        {
            if (currentModelInstance == null)
            {
                return;
            }

            ApplyModelOffset();
            ApplyModelScale();

            if (TryGetRenderableBounds(out Bounds bounds))
            {
                EnsureRootCollider(bounds);
            }

            var drag = currentModelInstance.GetComponent<ModelDragController>();
            if (drag != null)
            {
                drag.dragPlaneHeight = currentModelInstance.transform.position.y;
            }

            if (resetPhysics)
            {
                ResetLoadedModelPhysics();
            }
        }

        private void ApplyModelScale()
        {
            if (currentModelInstance == null) return;
            currentModelInstance.transform.localScale = baseModelScale * modelScaleFactor * autoFitScaleFactor;
        }

        private void CacheBaseTransform()
        {
            if (currentModelInstance == null) return;
            baseModelParent = currentModelInstance.transform.parent;
            baseModelRotation = currentModelInstance.transform.rotation;
        }

        public bool HasLoadedModel()
        {
            return currentModelInstance != null;
        }

        public GameObject GetLoadedModelInstance()
        {
            return currentModelInstance;
        }

        public bool TryGetSimulationBounds(out Bounds bounds)
        {
            bounds = default;
            Transform simulationRoot = CurrentSimulationGeometryRoot != null ? CurrentSimulationGeometryRoot : currentModelInstance != null ? currentModelInstance.transform : null;
            if (simulationRoot == null)
            {
                return false;
            }

            Renderer[] renderers = simulationRoot.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return false;
            }

            bool initialized = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                if (!initialized)
                {
                    bounds = renderers[i].bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }
            return initialized;
        }

        public void AlignToSimulationContext()
        {
            if (currentModelInstance == null) return;
            currentModelInstance.transform.localScale = baseModelScale * modelScaleFactor;
            if (!TryGetRenderableBounds(out Bounds bounds)) return;

            var windSim = FindAnyObjectByType<WindTunnelSimulation3D>();
            var pipeSim = FindAnyObjectByType<AeroFlow.Sim3D.PipeFlow.PipeFlowSimulation3D>();
            var damSim = FindAnyObjectByType<Simulation3D>();
            var machinerySim = FindAnyObjectByType<RotatingMachinerySimulation3D>();
            if (pipeSim != null && pipeSim.isActiveAndEnabled)
            {
                currentModelInstance.transform.SetParent(null, true);

                Bounds placementBounds;
                Vector3 flowAxis;
                Vector3 sideAxis;
                Vector3 upAxis;

                if (windSim != null && windSim.isActiveAndEnabled)
                {
                    placementBounds = windSim.GetTunnelBounds();
                    flowAxis = windSim.ResolveTunnelLongAxis();
                    sideAxis = windSim.ResolveTunnelSideAxis();
                    upAxis = windSim.ResolveTunnelVerticalAxis();
                }
                else
                {
                    Vector3 fallbackSize = pipeSim.transform.lossyScale;
                    if (fallbackSize.x < 0.01f || fallbackSize.y < 0.01f || fallbackSize.z < 0.01f)
                    {
                        fallbackSize = new Vector3(5f, 3f, 3f);
                    }

                    placementBounds = new Bounds(pipeSim.transform.position, fallbackSize);
                    flowAxis = pipeSim.ResolveFlowDirection().normalized;
                    upAxis = Vector3.up;
                    if (Mathf.Abs(Vector3.Dot(flowAxis, upAxis)) > 0.92f)
                    {
                        upAxis = Vector3.forward;
                    }

                    sideAxis = Vector3.Cross(upAxis, flowAxis).normalized;
                    if (sideAxis.sqrMagnitude < 1e-6f)
                    {
                        sideAxis = Vector3.right;
                    }

                    upAxis = Vector3.Cross(flowAxis, sideAxis).normalized;
                }

                float modelLength = ProjectSizeAlong(bounds, flowAxis);
                float modelWidth = ProjectSizeAlong(bounds, sideAxis);
                float modelHeight = ProjectSizeAlong(bounds, upAxis);
                float plateLength = ProjectSizeAlong(placementBounds, flowAxis);
                float plateWidth = ProjectSizeAlong(placementBounds, sideAxis);
                float plateHeight = ProjectSizeAlong(placementBounds, upAxis);

                if (modelLength > 1e-5f && modelWidth > 1e-5f && modelHeight > 1e-5f)
                {
                    float fitScale = Mathf.Min(
                        plateLength * 0.60f / modelLength,
                        plateWidth * 0.60f / modelWidth,
                        plateHeight * 0.32f / modelHeight);
                    ApplyFitScale(fitScale);
                    TryGetRenderableBounds(out bounds);
                }

                float floorCoord = Vector3.Dot(placementBounds.center, upAxis) - ProjectHalfExtent(placementBounds, upAxis);
                float targetUpCoord = floorCoord + 0.015f + ProjectHalfExtent(bounds, upAxis);
                Vector3 desiredBoundsCenter = placementBounds.center;
                desiredBoundsCenter += upAxis * (targetUpCoord - Vector3.Dot(desiredBoundsCenter, upAxis));
                desiredBoundsCenter += sideAxis * (Vector3.Dot(placementBounds.center, sideAxis) - Vector3.Dot(desiredBoundsCenter, sideAxis));
                desiredBoundsCenter += flowAxis * (Vector3.Dot(placementBounds.center, flowAxis) - Vector3.Dot(desiredBoundsCenter, flowAxis));
                baseModelPosition = ResolveRootPositionForBoundsCenter(bounds, desiredBoundsCenter);
            }
            else if (machinerySim != null && machinerySim.isActiveAndEnabled)
            {
                currentModelInstance.transform.SetParent(null, true);
                Vector3 axis = machinerySim.settings.rotationAxis.sqrMagnitude > 1e-6f
                    ? machinerySim.settings.rotationAxis.normalized
                    : Vector3.up;

                currentModelInstance.transform.rotation = Quaternion.FromToRotation(Vector3.up, axis)
                    * Quaternion.AngleAxis(90f, axis); // rotate propeller to horizontal default

                if (TryGetRenderableBounds(out bounds))
                {
                    AlignModelToAxis(bounds, axis, machinerySim);
                    TryGetRenderableBounds(out bounds);
                }

                Vector3 desiredCenter = machinerySim.transform.position;
                desiredCenter += axis * machinerySim.settings.rotatingZoneAxisOffset;
                baseModelPosition = ResolveRootPositionForBoundsCenter(bounds, desiredCenter);
            }
            else if (windSim != null && windSim.isActiveAndEnabled)
            {
                currentModelInstance.transform.SetParent(null, true);
                Bounds tunnelBounds = windSim.GetTunnelBounds();
                Vector3 flowAxis = windSim.ResolveTunnelLongAxis();
                Vector3 sideAxis = windSim.ResolveTunnelSideAxis();
                Vector3 upAxis = windSim.ResolveTunnelVerticalAxis();
                float modelLength = ProjectSizeAlong(bounds, flowAxis);
                float modelWidth = ProjectSizeAlong(bounds, sideAxis);
                float modelHeight = ProjectSizeAlong(bounds, upAxis);
                float tunnelLength = ProjectSizeAlong(tunnelBounds, flowAxis);
                float tunnelWidth = ProjectSizeAlong(tunnelBounds, sideAxis);
                float tunnelHeight = ProjectSizeAlong(tunnelBounds, upAxis);

                if (modelLength > 1e-5f && modelWidth > 1e-5f && modelHeight > 1e-5f)
                {
                    float fitScale = Mathf.Min(
                        tunnelLength * 0.38f / modelLength,
                        tunnelWidth * 0.58f / modelWidth,
                        tunnelHeight * 0.50f / modelHeight);
                    ApplyFitScale(fitScale);
                    TryGetRenderableBounds(out bounds);
                    modelHeight = ProjectSizeAlong(bounds, upAxis);
                }

                Quaternion targetRotation = windSim.ResolveVehicleRakeRotation();
                if (windSim.settings.flipVehicleDirection)
                {
                    Vector3 flipAxis = upAxis;
                    if (flipAxis.sqrMagnitude < 1e-6f)
                    {
                        flipAxis = Vector3.up;
                    }
                    else
                    {
                        flipAxis = flipAxis.normalized;
                    }

                    targetRotation = Quaternion.AngleAxis(180f, flipAxis) * targetRotation;
                }
                currentModelInstance.transform.rotation = targetRotation;
                TryGetRenderableBounds(out bounds);

                float floorCoord = Vector3.Dot(tunnelBounds.center, upAxis) - ProjectHalfExtent(tunnelBounds, upAxis);
                float rideHeight = Mathf.Max(windSim.settings.vehicle != null ? windSim.settings.vehicle.rideHeightMeters : 0f, 0f);

                if (rideHeight < 0.001f)
                {
                    rideHeight = Mathf.Clamp(modelHeight * 0.02f, 0.02f, 0.05f);
                }

                float targetUpCoord = floorCoord + rideHeight + ProjectHalfExtent(bounds, upAxis);
                Vector3 wakeOffset = flowAxis * (-tunnelLength * 0.08f);
                Vector3 desiredBoundsCenter = tunnelBounds.center + wakeOffset;
                desiredBoundsCenter += upAxis * (targetUpCoord - Vector3.Dot(desiredBoundsCenter, upAxis));
                desiredBoundsCenter += sideAxis * (Vector3.Dot(tunnelBounds.center, sideAxis) - Vector3.Dot(desiredBoundsCenter, sideAxis));
                baseModelPosition = ResolveRootPositionForBoundsCenter(bounds, desiredBoundsCenter);
            }
            else
            {
                if (modelPivot != null) currentModelInstance.transform.SetParent(modelPivot, true);
                
                // Robust grounding for other modes (Pipe Flow, etc.)
                // Use Y=0 as a safe default floor if no wind tunnel is present.
                float groundY = 0.01f; // 1cm gap from zero plane
                float halfHeight = ProjectHalfExtent(bounds, Vector3.up);
                Vector3 desiredBoundsCenter = new Vector3(0f, groundY + halfHeight, 0f);
                
                if (damSim != null && damSim.isActiveAndEnabled)
                {
                    Vector3 simCenter = damSim.transform.position;
                    Vector3 simSize = damSim.transform.localScale;

                    // DamBreak needs its own fit each time the user returns from another mode.
                    // Reusing the previous mode's scale can leave the model parked high above the tank.
                    float maxDim = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                    float maxFit = Mathf.Min(simSize.x, Mathf.Min(simSize.y, simSize.z)) * 0.45f;
                    if (maxDim > 1e-5f)
                    {
                        float fitScale = maxFit / maxDim;
                        ApplyFitScale(fitScale);
                        damBreakInitialFitApplied = true;
                        TryGetRenderableBounds(out bounds);
                    }

                    desiredBoundsCenter = new Vector3(simCenter.x, simCenter.y + simSize.y * 0.5f + bounds.extents.y + 0.2f, simCenter.z);
                }
                
                baseModelPosition = ResolveRootPositionForBoundsCenter(bounds, desiredBoundsCenter);
            }

            ApplyModelOffset();
            CacheBaseTransform();
            ApplyModelScale();
            EnsureRootCollider(bounds);
            ResetLoadedModelPhysics();

            var drag = currentModelInstance.GetComponent<ModelDragController>();
            if (drag != null) drag.dragPlaneHeight = currentModelInstance.transform.position.y;
        }

        private bool TryGetRenderableBounds(out Bounds bounds)
        {
            bounds = default;
            if (currentModelInstance == null) return false;

            CollectVisibleRenderers(VisibleRendererCache);
            if (VisibleRendererCache.Count == 0)
            {
                return false;
            }

            bounds = VisibleRendererCache[0].bounds;
            for (int i = 1; i < VisibleRendererCache.Count; i++)
            {
                bounds.Encapsulate(VisibleRendererCache[i].bounds);
            }
            return true;
        }

        private void ApplyFitScale(float fitScale)
        {
            if (currentModelInstance == null) return;
            fitScale = Mathf.Clamp(fitScale, 0.05f, 25f);
            autoFitScaleFactor = fitScale;
            currentModelInstance.transform.localScale = baseModelScale * modelScaleFactor * autoFitScaleFactor;
        }

        private static float ProjectSizeAlong(Bounds bounds, Vector3 axis)
        {
            axis = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.right;
            Vector3 extents = bounds.extents;
            Vector3 absAxis = new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
            return 2f * Vector3.Dot(extents, absAxis);
        }

        private static float ProjectHalfExtent(Bounds bounds, Vector3 axis)
        {
            return ProjectSizeAlong(bounds, axis) * 0.5f;
        }

        private Vector3 ResolveRootPositionForBoundsCenter(Bounds bounds, Vector3 desiredBoundsCenter)
        {
            if (currentModelInstance == null)
            {
                return desiredBoundsCenter;
            }

            Vector3 worldDelta = desiredBoundsCenter - bounds.center;
            return currentModelInstance.transform.position + worldDelta;
        }

        private void AlignModelToAxis(Bounds bounds, Vector3 axis, RotatingMachinerySimulation3D sim)
        {
            if (currentModelInstance == null || sim == null) return;

            Vector3 perp = Vector3.Cross(axis, Vector3.up);
            if (perp.sqrMagnitude < 1e-6f)
            {
                perp = Vector3.Cross(axis, Vector3.forward);
            }
            perp.Normalize();
            Vector3 other = Vector3.Cross(axis, perp).normalized;

            float crossSize = Mathf.Max(ProjectSizeAlong(bounds, perp), ProjectSizeAlong(bounds, other));
            if (crossSize < 1e-5f) return;

            float desiredDiameter = Mathf.Max(sim.settings.rotatingZoneRadius * 2f * 0.9f, 0.1f);
            float fitScale = desiredDiameter / crossSize;
            ApplyFitScale(fitScale);
        }

        private void EnsureRootCollider(Bounds bounds)
        {
            if (currentModelInstance == null) return;
            BoxCollider rootCollider = currentModelInstance.GetComponent<BoxCollider>();
            if (rootCollider == null) rootCollider = currentModelInstance.AddComponent<BoxCollider>();

            Vector3 lossy = currentModelInstance.transform.lossyScale;
            rootCollider.center = currentModelInstance.transform.InverseTransformPoint(bounds.center);
            rootCollider.size = new Vector3(
                Mathf.Abs(lossy.x) > 1e-5f ? bounds.size.x / Mathf.Abs(lossy.x) : bounds.size.x,
                Mathf.Abs(lossy.y) > 1e-5f ? bounds.size.y / Mathf.Abs(lossy.y) : bounds.size.y,
                Mathf.Abs(lossy.z) > 1e-5f ? bounds.size.z / Mathf.Abs(lossy.z) : bounds.size.z
            );
            rootCollider.isTrigger = false;
        }

        private void ResetLoadedModelPhysics()
        {
            if (currentModelInstance == null) return;
            Rigidbody rb = currentModelInstance.GetComponent<Rigidbody>();
            if (rb == null) return;

            rb.isKinematic = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.Sleep();
        }

        public void ResetModelToBase(bool restoreRenderMode = true)
        {
            if (currentModelInstance == null) return;

            currentModelInstance.transform.SetParent(baseModelParent, true);
            currentModelInstance.transform.position = baseModelPosition + modelOffset;
            currentModelInstance.transform.rotation = baseModelRotation;
            currentModelInstance.transform.localScale = baseModelScale * modelScaleFactor * autoFitScaleFactor;

            var rb = currentModelInstance.GetComponent<Rigidbody>();
            if (rb != null)
            {
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.Sleep();
            }

            var drag = currentModelInstance.GetComponent<ModelDragController>();
            if (drag != null)
            {
                drag.dragPlaneHeight = currentModelInstance.transform.position.y;
            }

            if (restoreRenderMode)
            {
                var bootstrapper = FindAnyObjectByType<AeroFlow.Display.VisualsBootstrapper>();
                if (bootstrapper != null)
                {
                    bootstrapper.SetRenderMode(AeroFlow.Display.VisualsBootstrapper.LastRenderMode);
                }
            }
        }

#if UNITY_STANDALONE || UNITY_EDITOR
        // Removed synchronous BrowseForVisualModel and BrowseForSimulationModel
#endif

        private void OnDestroy()
        {
            CancelActiveLoad();
        }
    }
}
