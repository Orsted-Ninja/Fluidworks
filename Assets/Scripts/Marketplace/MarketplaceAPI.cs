using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace FluidWorks.Marketplace
{
    public static class MarketplaceAPI
    {
        private const string BaseUrl = "https://fluidworks-marketplace/templates.json";

        public static IEnumerator FetchTemplates(Action<List<TemplateMetadata>> onSuccess, Action<string> onError)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(BaseUrl))
            {
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(request.error);
                }
                else
                {
                    try
                    {
                        string json = request.downloadHandler.text;
                        // Support both root-array and object-wrapped responses
                        TemplateMetadata[] list;
                        if (json.TrimStart().StartsWith("["))
                        {
                            list = JsonHelper.FromJson<TemplateMetadata>(json);
                        }
                        else
                        {
                            var response = JsonUtility.FromJson<MarketplaceResponse>(json);
                            list = response.templates;
                        }
                        
                        onSuccess?.Invoke(new List<TemplateMetadata>(list));
                    }
                    catch (Exception ex)
                    {
                        onError?.Invoke("JSON Parse Error: " + ex.Message);
                    }
                }
            }
        }
    }

    // Standard Unity JsonUtility array wrapper
    public static class JsonHelper
    {
        public static T[] FromJson<T>(string json)
        {
            Wrapper<T> wrapper = JsonUtility.FromJson<Wrapper<T>>("{\"Items\":" + json + "}");
            return wrapper.Items;
        }

        [Serializable]
        private class Wrapper<T>
        {
            public T[] Items;
        }
    }
}
