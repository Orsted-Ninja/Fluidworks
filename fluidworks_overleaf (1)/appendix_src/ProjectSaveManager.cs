using System;
using System.IO;
using System.IO.Compression;
using System.Collections;
using UnityEngine;

namespace FluidWorks.ProjectSystem
{
    public class ProjectSaveManager : MonoBehaviour
    {
        // Add references to actual solver/view managers here in the real engine

        /// <summary>
        /// Initiates the saving process to package the .fluidworks file
        /// </summary>
        public void SaveProject(string targetArchiveFile, ProjectData currentData, string sourceModelFilePath)
        {
            StartCoroutine(SaveRoutine(targetArchiveFile, currentData, sourceModelFilePath));
        }

        private IEnumerator SaveRoutine(string targetArchiveFile, ProjectData projectData, string sourceModelFilePath)
        {
            Debug.Log($"[SaveManager] Starting save to {targetArchiveFile}...");
            string tempDirName = "TempFluidWorksProject_" + Guid.NewGuid().ToString();
            string tempDir = Path.Combine(Application.temporaryCachePath, tempDirName);
            
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
            Directory.CreateDirectory(tempDir);

            // 1. Capture Preview
            string previewPath = Path.Combine(tempDir, "preview.png");
            bool captureComplete = false;
            yield return StartCoroutine(PreviewCapture.CaptureScreenshotCoroutine(previewPath, () => captureComplete = true));
            
            // Wait firmly just in case
            yield return new WaitUntil(() => captureComplete);

            try
            {
                // 2. Copy the active 3D Model into the container
                if (!string.IsNullOrEmpty(sourceModelFilePath) && File.Exists(sourceModelFilePath))
                {
                    string modelFileName = Path.GetFileName(sourceModelFilePath);
                    string destModelPath = Path.Combine(tempDir, modelFileName);
                    File.Copy(sourceModelFilePath, destModelPath, true);
                    
                    // The project data JSON should reference the relative file inside the ZIP
                    projectData.geometry.modelPath = modelFileName;
                }

                // 3. Serialize JSON Data
                string jsonContent = JsonUtility.ToJson(projectData, true);
                string jsonPath = Path.Combine(tempDir, "project.json");
                File.WriteAllText(jsonPath, jsonContent);

                // Create optional directories as per requirements
                Directory.CreateDirectory(Path.Combine(tempDir, "screenshots"));
                // Can write an optional report.html here if needed
                
                // 4. Zip the temp directory
                if (File.Exists(targetArchiveFile))
                {
                    File.Delete(targetArchiveFile);
                }

                string targetDir = Path.GetDirectoryName(targetArchiveFile);
                if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                // Package everything into .fluidworks
                ZipFile.CreateFromDirectory(tempDir, targetArchiveFile);
                Debug.Log($"[SaveManager] Project packaged cleanly to {targetArchiveFile}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveManager] Failed to save project: {ex.Message}");
            }
            finally
            {
                // 5. Cleanup temp directory
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }
    }
}
