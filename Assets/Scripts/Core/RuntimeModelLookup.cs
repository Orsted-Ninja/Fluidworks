using UnityEngine;

namespace AeroFlow.Core
{
    public static class RuntimeModelLookup
    {
        public static GameObject GetLoadedModel()
        {
            var loader = Object.FindFirstObjectByType<RuntimeModelLoader>();
            if (loader != null)
            {
                GameObject instance = loader.GetLoadedModelInstance();
                if (instance != null)
                {
                    return instance;
                }
            }

            return GameObject.Find("LoadedModel");
        }

        public static bool TryGetRenderableBounds(out Bounds bounds)
        {
            bounds = default;
            GameObject loadedModel = GetLoadedModel();
            if (loadedModel == null)
            {
                return false;
            }

            Renderer[] renderers = loadedModel.GetComponentsInChildren<Renderer>(true);
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
    }
}
