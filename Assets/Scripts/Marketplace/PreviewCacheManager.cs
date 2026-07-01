using System.IO;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using System;

namespace FluidWorks.Marketplace
{
    public static class PreviewCacheManager
    {
        private static string CachePath => Path.Combine(Application.persistentDataPath, "PreviewCache");

        public static bool IsCached(string templateId)
        {
            return File.Exists(GetLocalPath(templateId));
        }

        public static string GetLocalPath(string templateId)
        {
            if (!Directory.Exists(CachePath)) Directory.CreateDirectory(CachePath);
            return Path.Combine(CachePath, templateId + ".mp4");
        }

        public static IEnumerator CachePreview(string templateId, string url, Action<string> onComplete)
        {
            string localPath = GetLocalPath(templateId);
            if (File.Exists(localPath))
            {
                onComplete?.Invoke(localPath);
                yield break;
            }

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.downloadHandler = new DownloadHandlerFile(localPath);
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    onComplete?.Invoke(localPath);
                }
                else
                {
                    Debug.LogWarning($"[PreviewCache] Failed to download {url}: {request.error}");
                    onComplete?.Invoke(null);
                }
            }
        }
    }
}
