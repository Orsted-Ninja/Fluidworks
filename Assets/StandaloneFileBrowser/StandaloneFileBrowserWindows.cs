#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

namespace SFB
{
    public class StandaloneFileBrowserWindows : IStandaloneFileBrowser
    {
        public string[] OpenFilePanel(string title, string directory, ExtensionFilter[] extensions, bool multiselect)
        {
            string script = BuildOpenFileScript(title, directory, extensions, multiselect);
            return RunDialogForPaths(script);
        }

        public void OpenFilePanelAsync(string title, string directory, ExtensionFilter[] extensions, bool multiselect, Action<string[]> cb)
        {
            cb?.Invoke(OpenFilePanel(title, directory, extensions, multiselect));
        }

        public string[] OpenFolderPanel(string title, string directory, bool multiselect)
        {
            string script = BuildOpenFolderScript(title, directory);
            return RunDialogForPaths(script);
        }

        public void OpenFolderPanelAsync(string title, string directory, bool multiselect, Action<string[]> cb)
        {
            cb?.Invoke(OpenFolderPanel(title, directory, multiselect));
        }

        public string SaveFilePanel(string title, string directory, string defaultName, ExtensionFilter[] extensions)
        {
            string script = BuildSaveFileScript(title, directory, defaultName, extensions);
            string[] paths = RunDialogForPaths(script);
            return paths.Length > 0 ? paths[0] : string.Empty;
        }

        public void SaveFilePanelAsync(string title, string directory, string defaultName, ExtensionFilter[] extensions, Action<string> cb)
        {
            cb?.Invoke(SaveFilePanel(title, directory, defaultName, extensions));
        }

        private static string[] RunDialogForPaths(string script)
        {
            try
            {
                string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -STA -EncodedCommand " + encoded,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using Process process = Process.Start(startInfo);
                if (process == null)
                {
                    UnityEngine.Debug.LogError("[StandaloneFileBrowser] Failed to launch powershell.exe.");
                    return Array.Empty<string>();
                }

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        UnityEngine.Debug.LogError("[StandaloneFileBrowser] File dialog failed: " + error.Trim());
                    }
                    return Array.Empty<string>();
                }

                if (!string.IsNullOrWhiteSpace(error))
                {
                    UnityEngine.Debug.LogWarning("[StandaloneFileBrowser] File dialog reported: " + error.Trim());
                }

                return ParseOutputLines(output);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[StandaloneFileBrowser] File dialog exception: " + ex);
                return Array.Empty<string>();
            }
        }

        private static string[] ParseOutputLines(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return Array.Empty<string>();
            }

            return output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string BuildOpenFileScript(string title, string directory, ExtensionFilter[] extensions, bool multiselect)
        {
            string initialDirectory = NormalizeDirectory(directory);
            var lines = new List<string>
            {
                "Add-Type -AssemblyName System.Windows.Forms",
                "[System.Windows.Forms.Application]::EnableVisualStyles()",
                "$dialog = New-Object System.Windows.Forms.OpenFileDialog",
                "$dialog.Title = '" + EscapeForPowerShell(title) + "'",
                "$dialog.Multiselect = $" + multiselect.ToString().ToLowerInvariant(),
                "$dialog.Filter = '" + EscapeForPowerShell(BuildDialogFilter(extensions)) + "'"
            };

            if (!string.IsNullOrWhiteSpace(initialDirectory))
            {
                lines.Add("$dialog.InitialDirectory = '" + EscapeForPowerShell(initialDirectory) + "'");
            }

            lines.Add("$form = New-Object System.Windows.Forms.Form");
            lines.Add("$form.TopMost = $true");
            lines.Add("if ($dialog.ShowDialog($form) -eq [System.Windows.Forms.DialogResult]::OK) {");
            lines.Add("  $dialog.FileNames | ForEach-Object { [Console]::Out.WriteLine($_) }");
            lines.Add("}");

            return string.Join(Environment.NewLine, lines);
        }

        private static string BuildOpenFolderScript(string title, string directory)
        {
            string initialDirectory = NormalizeDirectory(directory);
            var lines = new List<string>
            {
                "Add-Type -AssemblyName System.Windows.Forms",
                "[System.Windows.Forms.Application]::EnableVisualStyles()",
                "$dialog = New-Object System.Windows.Forms.FolderBrowserDialog",
                "$dialog.Description = '" + EscapeForPowerShell(title) + "'",
                "$dialog.ShowNewFolderButton = $true"
            };

            if (!string.IsNullOrWhiteSpace(initialDirectory))
            {
                lines.Add("$dialog.SelectedPath = '" + EscapeForPowerShell(initialDirectory) + "'");
            }

            lines.Add("$form = New-Object System.Windows.Forms.Form");
            lines.Add("$form.TopMost = $true");
            lines.Add("if ($dialog.ShowDialog($form) -eq [System.Windows.Forms.DialogResult]::OK) {");
            lines.Add("  [Console]::Out.WriteLine($dialog.SelectedPath)");
            lines.Add("}");

            return string.Join(Environment.NewLine, lines);
        }

        private static string BuildSaveFileScript(string title, string directory, string defaultName, ExtensionFilter[] extensions)
        {
            string initialDirectory = NormalizeDirectory(directory);
            string defaultExtension = GetDefaultExtension(extensions);
            string fileName = string.IsNullOrWhiteSpace(defaultName) ? string.Empty : defaultName;

            if (!string.IsNullOrWhiteSpace(defaultExtension) &&
                !fileName.EndsWith("." + defaultExtension, StringComparison.OrdinalIgnoreCase))
            {
                fileName += "." + defaultExtension;
            }

            var lines = new List<string>
            {
                "Add-Type -AssemblyName System.Windows.Forms",
                "[System.Windows.Forms.Application]::EnableVisualStyles()",
                "$dialog = New-Object System.Windows.Forms.SaveFileDialog",
                "$dialog.Title = '" + EscapeForPowerShell(title) + "'",
                "$dialog.Filter = '" + EscapeForPowerShell(BuildDialogFilter(extensions)) + "'",
                "$dialog.FileName = '" + EscapeForPowerShell(fileName) + "'",
                "$dialog.OverwritePrompt = $true"
            };

            if (!string.IsNullOrWhiteSpace(initialDirectory))
            {
                lines.Add("$dialog.InitialDirectory = '" + EscapeForPowerShell(initialDirectory) + "'");
            }

            if (!string.IsNullOrWhiteSpace(defaultExtension))
            {
                lines.Add("$dialog.DefaultExt = '" + EscapeForPowerShell(defaultExtension) + "'");
                lines.Add("$dialog.AddExtension = $true");
            }

            lines.Add("$form = New-Object System.Windows.Forms.Form");
            lines.Add("$form.TopMost = $true");
            lines.Add("if ($dialog.ShowDialog($form) -eq [System.Windows.Forms.DialogResult]::OK) {");
            lines.Add("  [Console]::Out.WriteLine($dialog.FileName)");
            lines.Add("}");

            return string.Join(Environment.NewLine, lines);
        }

        private static string BuildDialogFilter(ExtensionFilter[] extensions)
        {
            if (extensions == null || extensions.Length == 0)
            {
                return "All Files (*.*)|*.*";
            }

            var parts = new List<string>();
            for (int i = 0; i < extensions.Length; i++)
            {
                ExtensionFilter filter = extensions[i];
                if (filter.Extensions == null || filter.Extensions.Length == 0)
                {
                    continue;
                }

                string[] normalizedExtensions = NormalizeExtensions(filter.Extensions);
                if (normalizedExtensions.Length == 0)
                {
                    continue;
                }

                string label = string.IsNullOrWhiteSpace(filter.Name) ? "Files" : filter.Name;
                string patternList = string.Join(";", normalizedExtensions);
                parts.Add(label + " (" + patternList + ")");
                parts.Add(patternList);
            }

            parts.Add("All Files (*.*)");
            parts.Add("*.*");
            return string.Join("|", parts);
        }

        private static string[] NormalizeExtensions(string[] extensions)
        {
            var normalized = new List<string>();
            for (int i = 0; i < extensions.Length; i++)
            {
                string extension = extensions[i];
                if (string.IsNullOrWhiteSpace(extension))
                {
                    continue;
                }

                string trimmed = extension.Trim();
                if (trimmed.StartsWith("*."))
                {
                    normalized.Add(trimmed);
                }
                else if (trimmed.StartsWith("."))
                {
                    normalized.Add("*" + trimmed);
                }
                else
                {
                    normalized.Add("*." + trimmed);
                }
            }

            return normalized.ToArray();
        }

        private static string GetDefaultExtension(ExtensionFilter[] extensions)
        {
            if (extensions == null || extensions.Length == 0)
            {
                return string.Empty;
            }

            for (int i = 0; i < extensions.Length; i++)
            {
                ExtensionFilter filter = extensions[i];
                if (filter.Extensions == null || filter.Extensions.Length == 0)
                {
                    continue;
                }

                for (int e = 0; e < filter.Extensions.Length; e++)
                {
                    string extension = filter.Extensions[e];
                    if (!string.IsNullOrWhiteSpace(extension))
                    {
                        return extension.Trim().TrimStart('*', '.');
                    }
                }
            }

            return string.Empty;
        }

        private static string EscapeForPowerShell(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("'", "''");
        }

        private static string NormalizeDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return null;
            }

            try
            {
                string fullPath = Path.GetFullPath(directory);
                if (Directory.Exists(fullPath))
                {
                    return fullPath;
                }

                string parent = Path.GetDirectoryName(fullPath);
                return Directory.Exists(parent) ? parent : null;
            }
            catch
            {
                return null;
            }
        }
    }
}

#endif
