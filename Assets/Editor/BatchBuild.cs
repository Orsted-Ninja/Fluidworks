using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AeroFlow.Editor
{
    public static class BatchBuild
    {
        private const string DefaultWindowsOutput = "Builds/BatchBuild/Aeroflow.exe";

        public static void BuildWindows64()
        {
            string projectRoot = Directory.GetCurrentDirectory();
            string outputPath = GetArgumentValue("-buildOutput");
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                outputPath = Path.Combine(projectRoot, DefaultWindowsOutput);
            }
            else if (!Path.IsPathRooted(outputPath))
            {
                outputPath = Path.GetFullPath(Path.Combine(projectRoot, outputPath));
            }

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                throw new InvalidOperationException("No enabled scenes found in EditorBuildSettings.");
            }

            string outputDirectory = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new InvalidOperationException("Could not resolve output directory.");
            }

            Directory.CreateDirectory(outputDirectory);

            ProjectValidationReport validation = ProjectValidation.Run();
            LogValidation(validation);
            if (validation.HasErrors)
            {
                WriteValidationFailureSummary(outputDirectory, outputPath, validation);
                throw new InvalidOperationException("Project validation failed before build.");
            }

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            Debug.Log($"[BatchBuild] Building Windows player to: {outputPath}");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            WriteSummary(report, outputDirectory, outputPath, validation);

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new Exception($"Build failed: {report.summary.result}");
            }

            Debug.Log($"[BatchBuild] Build succeeded in {report.summary.totalTime}. Size: {report.summary.totalSize} bytes.");
        }

        private static void WriteSummary(BuildReport report, string outputDirectory, string outputPath, ProjectValidationReport validation)
        {
            string summaryPath = Path.Combine(outputDirectory, "build-summary.txt");
            var lines = new List<string>
            {
                $"Result: {report.summary.result}",
                $"Output: {outputPath}",
                $"Platform: {report.summary.platform}",
                $"Time: {report.summary.totalTime}",
                $"Warnings: {report.summary.totalWarnings}",
                $"Errors: {report.summary.totalErrors}",
                $"SizeBytes: {report.summary.totalSize}"
            };

            AppendValidation(lines, validation);
            AppendMessages(lines, report, LogType.Warning, "WarningsDetail");
            AppendMessages(lines, report, LogType.Error, "ErrorsDetail");

            File.WriteAllLines(summaryPath, lines);
            Debug.Log($"[BatchBuild] Wrote summary: {summaryPath}");
        }

        private static void WriteValidationFailureSummary(string outputDirectory, string outputPath, ProjectValidationReport validation)
        {
            string summaryPath = Path.Combine(outputDirectory, "build-summary.txt");
            var lines = new List<string>
            {
                "Result: ValidationFailed",
                $"Output: {outputPath}",
                "Platform: StandaloneWindows64",
                "Warnings: 0",
                $"Errors: {validation.Errors.Count}"
            };

            AppendValidation(lines, validation);
            File.WriteAllLines(summaryPath, lines);
            Debug.Log($"[BatchBuild] Wrote summary: {summaryPath}");
        }

        private static void AppendMessages(List<string> lines, BuildReport report, LogType type, string sectionHeader)
        {
            string[] messages = report.steps
                .SelectMany(step => step.messages)
                .Where(message => message.type == type)
                .Select(message => message.content)
                .Where(content => !string.IsNullOrWhiteSpace(content))
                .Distinct()
                .ToArray();

            if (messages.Length == 0)
            {
                return;
            }

            lines.Add($"{sectionHeader}:");
            foreach (string message in messages)
            {
                lines.Add($"- {message}");
            }
        }

        private static void AppendValidation(List<string> lines, ProjectValidationReport validation)
        {
            if (validation == null) return;

            if (validation.Warnings.Count > 0)
            {
                lines.Add("ValidationWarnings:");
                for (int i = 0; i < validation.Warnings.Count; i++)
                {
                    lines.Add($"- {validation.Warnings[i]}");
                }
            }

            if (validation.Errors.Count > 0)
            {
                lines.Add("ValidationErrors:");
                for (int i = 0; i < validation.Errors.Count; i++)
                {
                    lines.Add($"- {validation.Errors[i]}");
                }
            }
        }

        private static void LogValidation(ProjectValidationReport validation)
        {
            if (validation == null) return;

            for (int i = 0; i < validation.Warnings.Count; i++)
            {
                Debug.LogWarning($"[ProjectValidation] {validation.Warnings[i]}");
            }

            for (int i = 0; i < validation.Errors.Count; i++)
            {
                Debug.LogError($"[ProjectValidation] {validation.Errors[i]}");
            }
        }

        private static string GetArgumentValue(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
