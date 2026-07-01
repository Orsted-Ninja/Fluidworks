using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace FluidWorks.Marketplace
{
    public class TemplateDownloadManager : MonoBehaviour
    {
        public static TemplateDownloadManager Instance { get; private set; }
        
        private string TemplatesPath => Path.Combine(Application.persistentDataPath, "Templates");
        private Dictionary<string, float> _activeDownloads = new Dictionary<string, float>();

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            if (!Directory.Exists(TemplatesPath)) Directory.CreateDirectory(TemplatesPath);
        }

        public bool IsDownloaded(string templateId)
        {
            return File.Exists(GetTemplateArchivePath(templateId));
        }

        public string GetTemplateArchivePath(string templateId)
        {
            return Path.Combine(TemplatesPath, templateId + ".fluidworks");
        }

        public float GetProgress(string templateId)
        {
            return _activeDownloads.ContainsKey(templateId) ? _activeDownloads[templateId] : 0f;
        }

        public IEnumerator DownloadTemplate(TemplateMetadata template, Action<bool> onComplete)
        {
            if (IsDownloaded(template.id))
            {
                onComplete?.Invoke(true);
                yield break;
            }

            string targetPath = GetTemplateArchivePath(template.id);
            using (UnityWebRequest request = UnityWebRequest.Get(template.downloadUrl))
            {
                _activeDownloads[template.id] = 0f;
                request.downloadHandler = new DownloadHandlerFile(targetPath);
                
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    _activeDownloads[template.id] = operation.progress;
                    yield return null;
                }

                _activeDownloads.Remove(template.id);

                if (request.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log($"[DownloadManager] Downloaded {template.name} to {targetPath}");
                    onComplete?.Invoke(true);
                }
                else
                {
                    Debug.LogError($"[DownloadManager] Failed to download {template.name}: {request.error}");
                    if (File.Exists(targetPath)) File.Delete(targetPath);
                    onComplete?.Invoke(false);
                }
            }
        }
    }
}
