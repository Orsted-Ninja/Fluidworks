using System;
using System.IO;
using System.Collections;
using UnityEngine;

namespace FluidWorks.ProjectSystem
{
    public class PreviewCapture : MonoBehaviour
    {
        /// <summary>
        /// Captures a screenshot of the current camera view at the end of the frame.
        /// </summary>
        public static IEnumerator CaptureScreenshotCoroutine(string filePath, Action onComplete)
        {
            yield return new WaitForEndOfFrame();
            
            ScreenCapture.CaptureScreenshot(filePath);
            
            // ScreenCapture is asynchronous, ensure file exists before continuing
            float timeout = 2.0f;
            float timer = 0f;
            while (!File.Exists(filePath) && timer < timeout)
            {
                timer += Time.deltaTime;
                yield return null;
            }
            
            onComplete?.Invoke();
        }
    }
}
