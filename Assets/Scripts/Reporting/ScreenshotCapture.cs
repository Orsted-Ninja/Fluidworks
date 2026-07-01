using System;
using System.Collections;
using System.IO;
using AeroFlow.UI;
using UnityEngine;

namespace FluidWorks.Reporting
{
    public class ScreenshotCapture : MonoBehaviour
    {
        public struct ReportScreenshotSet
        {
            public string streamlinesPath;
            public string verticalStreamlinesPath;
            public string horizontalStreamlinesPath;
            public string surfacePressurePath;
            public string surfaceFrictionPath;
        }

        public static ScreenshotCapture Instance { get; private set; }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            string path = Path.Combine(Application.persistentDataPath, "screenshots");
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        }

        public IEnumerator CaptureReportScreenshots(Action<ReportScreenshotSet> onComplete)
        {
            var shots = new ReportScreenshotSet
            {
                streamlinesPath = Path.Combine(Application.persistentDataPath, "screenshots", "report_streamlines.png"),
                verticalStreamlinesPath = Path.Combine(Application.persistentDataPath, "screenshots", "report_vertical_streamlines.png"),
                horizontalStreamlinesPath = Path.Combine(Application.persistentDataPath, "screenshots", "report_horizontal_streamlines.png"),
                surfacePressurePath = Path.Combine(Application.persistentDataPath, "screenshots", "report_surface_pressure.png"),
                surfaceFrictionPath = Path.Combine(Application.persistentDataPath, "screenshots", "report_surface_friction.png")
            };

            var windSim = FindAnyObjectByType<WindTunnelSimulation3D>();
            var mainScreen = FindAnyObjectByType<MainScreenController>();
            bool restoreHudState = mainScreen != null && mainScreen.IsHudHidden();
            if (mainScreen != null)
            {
                mainScreen.SetHudHidden(true);
            }

            if (windSim == null)
            {
                yield return CaptureCurrentView(shots.streamlinesPath);
                File.Copy(shots.streamlinesPath, shots.verticalStreamlinesPath, true);
                File.Copy(shots.streamlinesPath, shots.horizontalStreamlinesPath, true);
                File.Copy(shots.streamlinesPath, shots.surfacePressurePath, true);
                File.Copy(shots.streamlinesPath, shots.surfaceFrictionPath, true);
                if (mainScreen != null)
                {
                    mainScreen.SetHudHidden(restoreHudState);
                }
                onComplete?.Invoke(shots);
                yield break;
            }

            string originalMode = windSim.settings.visualizationMode;
            WindTunnelGraphicsMode originalGraphics = windSim.settings.graphicsMode;

            yield return CaptureWindTunnelMode(windSim, WindTunnelSimulation3D.VisualizationStreamlines, shots.streamlinesPath);
            yield return CaptureWindTunnelMode(windSim, WindTunnelSimulation3D.VisualizationVerticalStreamlines, shots.verticalStreamlinesPath);
            yield return CaptureWindTunnelMode(windSim, WindTunnelSimulation3D.VisualizationHorizontalStreamlines, shots.horizontalStreamlinesPath);
            yield return CaptureWindTunnelMode(windSim, WindTunnelSimulation3D.VisualizationSurfacePressure, shots.surfacePressurePath);
            yield return CaptureWindTunnelMode(windSim, WindTunnelSimulation3D.VisualizationSurfaceFriction, shots.surfaceFrictionPath);

            windSim.SetGraphicsMode(originalGraphics);
            windSim.SetVisualizationMode(originalMode);
            yield return null;
            if (mainScreen != null)
            {
                mainScreen.SetHudHidden(restoreHudState);
            }

            onComplete?.Invoke(shots);
        }

        private IEnumerator CaptureWindTunnelMode(WindTunnelSimulation3D windSim, string mode, string outputPath)
        {
            windSim.SetGraphicsMode(WindTunnelGraphicsMode.Fluid);
            windSim.SetVisualizationMode(mode);
            int settleFrames = NeedsLongerSettle(mode) ? 8 : 3;
            for (int i = 0; i < settleFrames; i++)
            {
                yield return null;
            }

            if (NeedsLongerSettle(mode))
            {
                yield return new WaitForSecondsRealtime(0.15f);
            }

            yield return CaptureCurrentView(outputPath);
        }

        private static bool NeedsLongerSettle(string mode)
        {
            return mode == WindTunnelSimulation3D.VisualizationStreamlines
                || mode == WindTunnelSimulation3D.VisualizationVerticalStreamlines
                || mode == WindTunnelSimulation3D.VisualizationHorizontalStreamlines;
        }

        private IEnumerator CaptureCurrentView(string outputPath)
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(outputPath);

            float timeout = Time.realtimeSinceStartup + 2f;
            while (!File.Exists(outputPath) && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            yield return new WaitForSecondsRealtime(0.1f);
        }
    }
}
