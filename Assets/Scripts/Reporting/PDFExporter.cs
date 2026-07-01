using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace FluidWorks.Reporting
{
    public static class PDFExporter
    {
        private const string WkHtmlToPdfEnvVar = "FLUIDWORKS_WKHTMLTOPDF";

        public static bool ExportToPDF(string htmlFilePath, string outputPdfPath)
        {
            if (!File.Exists(htmlFilePath))
            {
                UnityEngine.Debug.LogError($"[PDFExporter] Source HTML not found: {htmlFilePath}");
                return false;
            }

            string executablePath = ResolveWkHtmlToPdfPath();
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                UnityEngine.Debug.LogWarning("[PDFExporter] wkhtmltopdf was not found. Saving the HTML report only. Set FLUIDWORKS_WKHTMLTOPDF or install wkhtmltopdf to enable PDF export.");
                return false;
            }

            try
            {
                string reportDirectory = Path.GetDirectoryName(htmlFilePath) ?? Application.persistentDataPath;
                string reportDirectoryUnix = reportDirectory.Replace("\\", "/");
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = $"--enable-local-file-access --allow \"{reportDirectoryUnix}\" --load-error-handling ignore --load-media-error-handling ignore \"{htmlFilePath}\" \"{outputPdfPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };

                using (Process process = Process.Start(psi))
                {
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        string error = process.StandardError.ReadToEnd();
                        UnityEngine.Debug.LogError($"[PDFExporter] wkhtmltopdf error (Exit Code {process.ExitCode}): {error}");
                        return false;
                    }
                }

                UnityEngine.Debug.Log($"[PDFExporter] Successfully exported PDF to: {outputPdfPath}");
                return true;
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"[PDFExporter] Failed to execute wkhtmltopdf: {ex.Message}. Ensure it is installed and added to PATH or set FLUIDWORKS_WKHTMLTOPDF.");
                return false;
            }
        }

        private static string ResolveWkHtmlToPdfPath()
        {
            string envPath = System.Environment.GetEnvironmentVariable(WkHtmlToPdfEnvVar);
            if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
            {
                return envPath;
            }

            string[] candidates =
            {
                @"C:\Program Files\wkhtmltopdf\bin\wkhtmltopdf.exe",
                @"C:\Program Files (x86)\wkhtmltopdf\bin\wkhtmltopdf.exe"
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                string candidate = candidates[i];
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "where.exe",
                    Arguments = "wkhtmltopdf",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(psi))
                {
                    if (process == null)
                    {
                        return null;
                    }

                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode == 0)
                    {
                        string firstLine = output.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries)[0];
                        if (!string.IsNullOrWhiteSpace(firstLine) && File.Exists(firstLine.Trim()))
                        {
                            return firstLine.Trim();
                        }
                    }
                }
            }
            catch
            {
                // Ignore lookup failures and fall through to null.
            }

            return null;
        }
    }
}
