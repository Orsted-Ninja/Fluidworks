using UnityEngine;

namespace AeroFlow.Core
{
    public static class RuntimeModelLookup
    {
        private static RuntimeModelLoader cachedLoader;
        private static GameObject cachedModel;
        private static Renderer[] cachedRenderers;

        public static void ClearCache()
        {
            cachedModel = null;
            cachedRenderers = null;
        }

        public static GameObject GetLoadedModel()
        {
            if (cachedModel != null) return cachedModel;

            if (cachedLoader == null)
                cachedLoader = Object.FindFirstObjectByType<RuntimeModelLoader>();

            if (cachedLoader != null)
            {
                cachedModel = cachedLoader.GetLoadedModelInstance();
                if (cachedModel != null)
                    return cachedModel;
            }

            cachedModel = GameObject.Find("LoadedModel");
            return cachedModel;
        }

        public static Renderer[] GetLoadedModelRenderers()
        {
            GameObject model = GetLoadedModel();
            if (model == null) return null;
            if (cachedRenderers == null || cachedRenderers.Length == 0 || cachedRenderers[0] == null)
            {
                cachedRenderers = model.GetComponentsInChildren<Renderer>(true);
            }
            return cachedRenderers;
        }

        public static bool TryGetRenderableBounds(out Bounds bounds)
        {
            bounds = default;
            GameObject loadedModel = GetLoadedModel();
            if (loadedModel == null)
            {
                return false;
            }

            if (cachedRenderers == null || cachedRenderers.Length == 0 || cachedRenderers[0] == null)
            {
                cachedRenderers = loadedModel.GetComponentsInChildren<Renderer>(true);
            }

            Renderer[] renderers = cachedRenderers;
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
