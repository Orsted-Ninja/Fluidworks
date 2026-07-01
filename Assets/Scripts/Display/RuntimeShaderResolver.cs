using UnityEngine;
using UnityEngine.Rendering;

namespace AeroFlow.Rendering
{
    public static class RuntimeShaderResolver
    {
        private static bool IsScriptableRenderPipelineActive()
        {
            return GraphicsSettings.currentRenderPipeline != null || QualitySettings.renderPipeline != null;
        }

        public static Shader FindLitShader()
        {
            return IsScriptableRenderPipelineActive()
                ? FindFirstSupported("Universal Render Pipeline/Lit", "Standard")
                : FindFirstSupported("Standard", "Legacy Shaders/Diffuse", "Universal Render Pipeline/Lit");
        }

        public static Shader FindSliceShader()
        {
            return IsScriptableRenderPipelineActive()
                ? FindFirstSupported(
                    "AeroFlow/FlowSliceURP",
                    "Universal Render Pipeline/Particles/Unlit",
                    "Universal Render Pipeline/Unlit",
                    "Particles/Standard Unlit",
                    "Sprites/Default")
                : FindFirstSupported(
                    "Particles/Standard Unlit",
                    "Legacy Shaders/Particles/Alpha Blended",
                    "Sprites/Default",
                    "Unlit/Texture",
                    "Unlit/Color");
        }

        public static Shader FindLineShader()
        {
            return IsScriptableRenderPipelineActive()
                ? FindFirstSupported(
                    "AeroFlow/StreamlineFlowURP",
                    "Universal Render Pipeline/Particles/Unlit",
                    "Universal Render Pipeline/Unlit",
                    "Particles/Standard Unlit",
                    "Legacy Shaders/Particles/Additive",
                    "Sprites/Default")
                : FindFirstSupported(
                    "AeroFlow/StreamlineFlowURP",
                    "Particles/Standard Unlit",
                    "Legacy Shaders/Particles/Additive",
                    "Legacy Shaders/Particles/Alpha Blended",
                    "Sprites/Default",
                    "Unlit/Color");
        }

        public static Shader FindPressureShader()
        {
            return IsScriptableRenderPipelineActive()
                ? FindFirstSupported("AeroFlow/VertexColorLitURP", "AeroFlow/VertexColorURP", "AeroFlow/SectionableURP", "Universal Render Pipeline/Lit", "Standard")
                : FindFirstSupported("AeroFlow/VertexColorBuiltin", "AeroFlow/SectionableBuiltin", "Standard", "Universal Render Pipeline/Lit");
        }

        public static Shader FindPressureCloudShader()
        {
            return IsScriptableRenderPipelineActive()
                ? FindFirstSupported(
                    "Universal Render Pipeline/Particles/Unlit",
                    "Universal Render Pipeline/Unlit",
                    "Particles/Standard Unlit",
                    "Sprites/Default")
                : FindFirstSupported(
                    "Particles/Standard Unlit",
                    "Legacy Shaders/Particles/Alpha Blended",
                    "Legacy Shaders/Particles/Additive",
                    "Sprites/Default",
                    "Unlit/Color");
        }

        public static Shader FindSimpleUnlitShader()
        {
            return IsScriptableRenderPipelineActive()
                ? FindFirstSupported("Universal Render Pipeline/Unlit", "Universal Render Pipeline/Particles/Unlit", "Unlit/Color")
                : FindFirstSupported("Unlit/Color", "Sprites/Default", "Particles/Standard Unlit", "Universal Render Pipeline/Unlit");
        }

        public static Shader FindSectionableShader()
        {
            return IsScriptableRenderPipelineActive()
                ? FindFirstSupported("AeroFlow/SectionableURP", "Universal Render Pipeline/Lit", "Standard")
                : FindFirstSupported("AeroFlow/SectionableBuiltin", "Standard", "Universal Render Pipeline/Lit");
        }

        public static Shader FindFirstSupported(params string[] shaderNames)
        {
            if (shaderNames == null)
            {
                Debug.LogError("[Shaders] CRITICAL: shaderNames array was null when trying to resolve shaders.");
                return null;
            }

            foreach (string shaderName in shaderNames)
            {
                if (string.IsNullOrWhiteSpace(shaderName))
                {
                    Debug.LogWarning("[Shaders] Skipping null or whitespace shader name in resolution attempt.");
                    continue;
                }

                Shader shader = Shader.Find(shaderName);
                if (shader != null && shader.isSupported)
                {
                    Debug.Log($"[Shaders] Resolved '{shaderName}' successfully and it is supported.");
                    return shader;
                }
                else if (shader != null && !shader.isSupported)
                {
                    Debug.LogWarning($"[Shaders] Attempt to resolve '{shaderName}' found the shader, but it is not supported.");
                }
                else
                {
                    Debug.LogWarning($"[Shaders] Attempt to resolve '{shaderName}' failed (shader not found).");
                }
            }

            Debug.LogError("[Shaders] CRITICAL: None of the requested shaders could be resolved or were supported: " + string.Join(", ", shaderNames));
            return null;
        }
    }
}
