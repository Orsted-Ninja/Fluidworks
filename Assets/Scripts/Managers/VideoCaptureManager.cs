using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace AeroFlow.Managers
{
    public class VideoCaptureManager : MonoBehaviour
    {
        [Header("Capture Settings")]
        public int captureWidth = 1920;
        public int captureHeight = 1080;
        public int captureFPS = 30;
        public float maxDurationSeconds = 30f;

        [Header("Capture Mode")]
        [Tooltip("When true, captures only the simulation camera (excludes UI). When false, captures full screen including UI.")]
        public bool simulationCameraOnly = true;

        [Header("Encoding")]
        public bool autoEncodeToMp4 = true;
        public bool deleteFramesAfterEncode = true;
        public string ffmpegPathOverride = "";

        private bool isRecording;
        private int frameIndex;
        private float startTime;
        private float nextFrameTime;
        private string captureDir;
        private string outputMp4Path;
        private Texture2D frameTexture;
        private RenderTexture captureRT;
        private Camera simulationCamera;

        public bool IsRecording => isRecording;

        public void ToggleRecording()
        {
            if (!isRecording) StartRecording();
            else StopRecording();
        }

        public void StartRecording()
        {
            if (isRecording) return;

            isRecording = true;
            frameIndex = 0;
            startTime = Time.unscaledTime;
            nextFrameTime = startTime;

            string captureStamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            captureDir = Path.Combine(Application.persistentDataPath, "Captures", captureStamp);
            if (!string.IsNullOrEmpty(outputMp4Path))
            {
                captureDir = Path.Combine(Path.GetDirectoryName(outputMp4Path), $"Capture_{captureStamp}");
            }
            Directory.CreateDirectory(captureDir);

            int width = captureWidth;
            int height = captureHeight;

            // Find the simulation camera (the main game camera, not overlay/UI cameras)
            simulationCamera = FindSimulationCamera();

            if (simulationCameraOnly && simulationCamera != null)
            {
                // Create a RenderTexture to capture camera output without UI
                captureRT = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                captureRT.antiAliasing = 2;
                frameTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
            }
            else
            {
                // Fallback: capture full screen
                width = Screen.width;
                height = Screen.height;
                frameTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
            }

            StartCoroutine(CaptureLoop());
            UnityEngine.Debug.Log($"[VideoCapture] Recording started ({(simulationCameraOnly ? "simulation only" : "full screen")}): {captureDir}");
        }

        public void StopRecording()
        {
            if (!isRecording) return;

            isRecording = false;

            if (captureRT != null)
            {
                captureRT.Release();
                Destroy(captureRT);
                captureRT = null;
            }

            UnityEngine.Debug.Log($"[VideoCapture] Recording stopped. {frameIndex} frames captured.");

            if (autoEncodeToMp4) StartCoroutine(EncodeMp4());
        }

        public void StartRecordingWithSavePath(string savePath)
        {
            if (string.IsNullOrEmpty(savePath)) return;
            outputMp4Path = savePath;
            StartRecording();
        }

        private Camera FindSimulationCamera()
        {
            // Look for the main tagged camera first
            Camera main = Camera.main;
            if (main != null && main.enabled && main.cameraType == CameraType.Game)
                return main;

            // Fallback: find the highest-depth enabled game camera
            Camera best = null;
            var cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++)
            {
                var c = cameras[i];
                if (!c.enabled || c.cameraType != CameraType.Game) continue;
                if (best == null || c.depth > best.depth)
                    best = c;
            }
            return best;
        }

        private IEnumerator CaptureLoop()
        {
            while (isRecording)
            {
                yield return new WaitForEndOfFrame();

                float now = Time.unscaledTime;
                if (now < nextFrameTime) continue;

                if (simulationCameraOnly && simulationCamera != null && captureRT != null)
                {
                    // Render camera to our RenderTexture (excludes UI overlay)
                    RenderTexture prevTarget = simulationCamera.targetTexture;
                    simulationCamera.targetTexture = captureRT;
                    simulationCamera.Render();
                    simulationCamera.targetTexture = prevTarget;

                    RenderTexture prevActive = RenderTexture.active;
                    RenderTexture.active = captureRT;
                    frameTexture.ReadPixels(new Rect(0, 0, captureRT.width, captureRT.height), 0, 0);
                    frameTexture.Apply(false);
                    RenderTexture.active = prevActive;
                }
                else
                {
                    // Full screen capture (includes UI)
                    frameTexture.ReadPixels(new Rect(0, 0, frameTexture.width, frameTexture.height), 0, 0);
                    frameTexture.Apply(false);
                }

                byte[] bytes = frameTexture.EncodeToPNG();
                string framePath = Path.Combine(captureDir, $"frame_{frameIndex:D05}.png");
                File.WriteAllBytes(framePath, bytes);

                frameIndex++;
                nextFrameTime += 1f / Mathf.Max(1, captureFPS);

                if ((now - startTime) >= maxDurationSeconds)
                {
                    StopRecording();
                    yield break;
                }
            }
        }

        private IEnumerator EncodeMp4()
        {
            string ffmpegPath = ResolveFfmpegPath();
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                UnityEngine.Debug.LogWarning("[VideoCapture] ffmpeg.exe not found. Frames saved as PNG sequence, but MP4 not encoded. " +
                    "Place ffmpeg.exe in StreamingAssets/Tools/ or set ffmpegPathOverride.");
                yield break;
            }

            string outputPath = !string.IsNullOrEmpty(outputMp4Path)
                ? outputMp4Path
                : Path.Combine(captureDir, "capture.mp4");
            string args = $"-y -framerate {captureFPS} -i \"{Path.Combine(captureDir, "frame_%05d.png")}\" -c:v libx264 -pix_fmt yuv420p \"{outputPath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false
            };

            Process proc = null;
            try
            {
                proc = Process.Start(psi);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"[VideoCapture] ffmpeg encode failed: {ex.Message}");
                yield break;
            }

            if (proc == null)
            {
                UnityEngine.Debug.LogError("[VideoCapture] Failed to start ffmpeg process.");
                yield break;
            }

            while (!proc.HasExited) yield return null;
            UnityEngine.Debug.Log($"[VideoCapture] MP4 saved: {outputPath}");

            if (deleteFramesAfterEncode)
            {
                foreach (var file in Directory.GetFiles(captureDir, "frame_*.png"))
                {
                    File.Delete(file);
                }

                try { Directory.Delete(captureDir, false); }
                catch { /* ignore if dir not empty */ }
            }

            outputMp4Path = null;
        }

        private string ResolveFfmpegPath()
        {
            if (!string.IsNullOrEmpty(ffmpegPathOverride) && File.Exists(ffmpegPathOverride))
            {
                return ffmpegPathOverride;
            }

            string streamingPath = Path.Combine(Application.streamingAssetsPath, "Tools", "ffmpeg.exe");
            if (File.Exists(streamingPath)) return streamingPath;

            // Try system PATH
            string pathEnv = System.Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in pathEnv.Split(Path.PathSeparator))
            {
                string candidate = Path.Combine(dir, "ffmpeg.exe");
                if (File.Exists(candidate)) return candidate;
            }

            return null;
        }

        private void OnDestroy()
        {
            if (frameTexture != null) Destroy(frameTexture);
            if (captureRT != null)
            {
                captureRT.Release();
                Destroy(captureRT);
            }
        }
    }
}
