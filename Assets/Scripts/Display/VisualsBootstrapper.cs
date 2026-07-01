using UnityEngine;
using AeroFlow.Core;
using AeroFlow.Rendering;
using System.Collections.Generic;

namespace AeroFlow.Display
{
    public class VisualsBootstrapper : MonoBehaviour
    {
        private static readonly int UsePressureMapId = Shader.PropertyToID("_UsePressureMap");

        public static string LastRenderMode = "Standard";
        
        /// <summary>
        /// Global toggle used by simulation drivers (e.g. WindTunnelSimulation3D) to bypass 
        /// performance budgets and force maximum visual fidelity.
        /// </summary>
        public static bool ForceMaxPerformanceGlobal = false;

        private static Material _pressureMaterial;
        private static Material _wireframeMaterial;
        private static Material _ghostMaterial;

        [Header("Look")]
        public Color backgroundColor = new Color(0.03f, 0.05f, 0.08f);

        private void Start()
        {
            if (Camera.main != null)
            {
                Camera.main.backgroundColor = backgroundColor;
                Camera.main.clearFlags = CameraClearFlags.SolidColor;
            }
            ApplySavedRenderMode();
        }

        private void ApplySavedRenderMode()
        {
            SetRenderMode(LastRenderMode);
        }

        public void SetRenderMode(string mode)
        {
            LastRenderMode = mode;
            var modelObj = RuntimeModelLookup.GetLoadedModel();
            if (modelObj == null) return;
            
            var renderers = modelObj.GetComponentsInChildren<Renderer>(true);
            EnsureMaterials();
            
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                if (renderer.GetComponentInParent<RuntimeSimulationProxy>() != null) continue;

                var state = renderer.GetComponent<RenderModeState>();
                if (state == null) state = renderer.gameObject.AddComponent<RenderModeState>();
                state.CacheOriginal(renderer);
                var pressureVis = renderer.GetComponent<AeroFlow.Visualization.SurfacePressureVisualizer>();

                if (mode == "Wireframe")
                {
                    GL.wireframe = false;
                    if (pressureVis != null) pressureVis.enabled = false;
                    state.ApplyOriginal(renderer);
                    state.SetWireframeOverlay(true, _wireframeMaterial);
                }
                else if (mode == "Pressure")
                {
                    GL.wireframe = false;
                    if (pressureVis != null) pressureVis.enabled = true;
                    if (pressureVis != null && _pressureMaterial != null)
                    {
                        state.ApplyPressure(renderer, _pressureMaterial);
                    }
                    else
                    {
                        state.ApplyOriginal(renderer);
                    }
                    state.SetWireframeOverlay(false, _wireframeMaterial);
                }
                else if (mode == "Ghost")
                {
                    GL.wireframe = false;
                    if (pressureVis != null) pressureVis.enabled = false;
                    state.ApplyGhost(renderer, _ghostMaterial);
                    state.SetWireframeOverlay(false, _wireframeMaterial);
                }
                else // Standard
                {
                    GL.wireframe = false;
                    if (pressureVis != null) pressureVis.enabled = false;
                    state.ApplyOriginal(renderer);
                    state.SetWireframeOverlay(false, _wireframeMaterial);
                }
            }
        }

        private void EnsureMaterials()
        {
            if (_pressureMaterial == null)
            {
                var shader = RuntimeShaderResolver.FindPressureShader();
                if (shader != null)
                {
                    _pressureMaterial = new Material(shader);
                    _pressureMaterial.SetFloat(UsePressureMapId, 1f);
                }
            }
            if (_wireframeMaterial == null)
            {
                var shader = RuntimeShaderResolver.FindSimpleUnlitShader();
                if (shader != null)
                {
                    _wireframeMaterial = new Material(shader);
                    _wireframeMaterial.color = new Color(0.2f, 0.9f, 1.0f, 1.0f);
                }
            }
            if (_ghostMaterial == null)
            {
                var shader = RuntimeShaderResolver.FindLitShader() ?? RuntimeShaderResolver.FindPressureShader();
                if (shader != null)
                {
                    _ghostMaterial = new Material(shader);
                    _ghostMaterial.name = "GhostMaterial";
                    Color ghostCol = new Color(0.7f, 0.85f, 1.0f, 0.28f);
                    if (_ghostMaterial.HasProperty("_BaseColor")) _ghostMaterial.SetColor("_BaseColor", ghostCol);
                    if (_ghostMaterial.HasProperty("_Color")) _ghostMaterial.SetColor("_Color", ghostCol);
                    if (_ghostMaterial.HasProperty("_Surface")) _ghostMaterial.SetFloat("_Surface", 1f); // Transparent
                    if (_ghostMaterial.HasProperty("_SrcBlend")) _ghostMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    if (_ghostMaterial.HasProperty("_DstBlend")) _ghostMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    if (_ghostMaterial.HasProperty("_ZWrite")) _ghostMaterial.SetFloat("_ZWrite", 0f);
                    if (_ghostMaterial.HasProperty("_Cull")) _ghostMaterial.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
                    _ghostMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
            }
        }
    }
}
