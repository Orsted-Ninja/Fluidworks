using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace AeroFlow.Editor
{
    public sealed class ProjectValidationReport
    {
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Errors = new List<string>();

        public bool HasErrors => Errors.Count > 0;
    }

    public static class ProjectValidation
    {
        static readonly string[] UiAssetPaths =
        {
            "Assets/Resources/UI/UXML/MainLayout.uxml",
            "Assets/Resources/UI/UXML/Ribbon.uxml",
            "Assets/Resources/UI/UXML/WindTunnel/WindTunnelProperties.uxml",
            "Assets/Resources/UI/USS/MainStyle.uss"
        };

        static readonly string[] WindTunnelScenePaths =
        {
            "Assets/Scenes/WindTunnelSample.unity",
            "Assets/Scenes/Wind Tunnel (3D).unity"
        };

        public static ProjectValidationReport Run()
        {
            var report = new ProjectValidationReport();

            ValidateRequiredAssets(report);
            ValidateUiTextEncoding(report);
            ValidateSampleScene(report);

            for (int i = 0; i < WindTunnelScenePaths.Length; i++)
            {
                ValidateWindTunnelScene(WindTunnelScenePaths[i], report);
            }

            return report;
        }

        static void ValidateRequiredAssets(ProjectValidationReport report)
        {
            ValidateFileExists("Assets/Resources/Compute/WindTunnel/NavierStokes3D.compute", report);
            for (int i = 0; i < UiAssetPaths.Length; i++)
            {
                ValidateFileExists(UiAssetPaths[i], report);
            }
        }

        static void ValidateFileExists(string path, ProjectValidationReport report)
        {
            if (!File.Exists(path))
            {
                report.Errors.Add($"Missing required asset: {path}");
            }
        }

        static void ValidateUiTextEncoding(ProjectValidationReport report)
        {
            for (int i = 0; i < UiAssetPaths.Length; i++)
            {
                string path = UiAssetPaths[i];
                if (!File.Exists(path)) continue;

                string text = File.ReadAllText(path);
                if (text.Contains("â") || text.Contains("Â") || text.Contains("Ã¢") || text.Contains("Ã‚"))
                {
                    report.Errors.Add($"UI asset contains corrupted text encoding: {path}");
                }
            }
        }

        static void ValidateSampleScene(ProjectValidationReport report)
        {
            const string path = "Assets/Scenes/SampleScene.unity";
            if (!File.Exists(path))
            {
                report.Errors.Add($"Missing required scene: {path}");
                return;
            }

            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

            if (Object.FindFirstObjectByType<UIDocument>() == null)
            {
                report.Errors.Add("SampleScene is missing a UIDocument.");
            }
            if (Object.FindFirstObjectByType<AeroFlow.UI.MainScreenController>() == null)
            {
                report.Errors.Add("SampleScene is missing MainScreenController.");
            }
            if (Object.FindFirstObjectByType<AeroFlow.Core.RuntimeModelLoader>() == null)
            {
                report.Errors.Add("SampleScene is missing RuntimeModelLoader.");
            }
            if (Object.FindFirstObjectByType<AeroFlow.Managers.SimulationManager>() == null)
            {
                report.Errors.Add("SampleScene is missing SimulationManager.");
            }
        }

        static void ValidateWindTunnelScene(string path, ProjectValidationReport report)
        {
            if (!File.Exists(path))
            {
                report.Errors.Add($"Missing required scene: {path}");
                return;
            }

            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var wind = Object.FindFirstObjectByType<WindTunnelSimulation3D>();
            if (wind == null)
            {
                report.Errors.Add($"Wind tunnel scene has no WindTunnelSimulation3D: {path}");
                return;
            }

            Vector3 tunnelSize = wind.GetTunnelSize();
            if (tunnelSize.x <= 0f || tunnelSize.y <= 0f || tunnelSize.z <= 0f)
            {
                report.Errors.Add($"Wind tunnel scene has invalid tunnel bounds: {path}");
            }

            if (wind.settings.streamlineDensity < WindTunnelSimulation3D.MinStreamlineDensity
                || wind.settings.streamlineDensity > WindTunnelSimulation3D.MaxStreamlineDensity)
            {
                report.Errors.Add(
                    $"Wind tunnel scene has out-of-range streamline density ({wind.settings.streamlineDensity}) in {path}.");
            }

            if (string.IsNullOrWhiteSpace(wind.settings.visualizationMode))
            {
                report.Errors.Add($"Wind tunnel scene has no visualization mode set: {path}");
            }
        }
    }
}
