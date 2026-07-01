using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using AeroFlow.Core;
using AeroFlow.Managers;
using AeroFlow.Sim3D.PipeFlow;
using AeroFlow.Sim3D.RotatingMachinery;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using SFB;
#endif

namespace FluidWorks.Reporting
{
    public class ReportGenerator : MonoBehaviour
    {
        public static ReportGenerator Instance { get; private set; }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        public void GenerateReport(string outputPdfPath = null)
        {
            StartCoroutine(GenerateReportRoutine(outputPdfPath));
        }

        public void PromptAndGenerateReport()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            string defaultFolder = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile) + "\\Downloads";
            string path = StandaloneFileBrowser.SaveFilePanel(
                "Save PDF Report",
                defaultFolder,
                "AeroFlow_Report",
                "pdf"
            );
            if (string.IsNullOrEmpty(path)) return;
            GenerateReport(path);
#else
            string fallback = Path.Combine(Application.persistentDataPath, "AeroFlow_Report.pdf");
            GenerateReport(fallback);
#endif
        }

        private IEnumerator GenerateReportRoutine(string outputPdfPath = null)
        {
            Debug.Log("[ReportGenerator] Starting report generation...");

            // 1. Load Template
            var templateAsset = Resources.Load<TextAsset>("Templates/FluidWorks_Report_Template");
            if (templateAsset == null)
            {
                Debug.LogError("[ReportGenerator] Could not load HTML template from Resources/Templates/FluidWorks_Report_Template");
                yield break;
            }
            string html = templateAsset.text;

            // 2. Capture Screenshots
            string streamlinesPath = "", verticalStreamlinesPath = "", horizontalStreamlinesPath = "", surfacePressurePath = "", surfaceFrictionPath = "";
            bool captureDone = false;
            
            if (ScreenshotCapture.Instance == null)
            {
                var cap = FindAnyObjectByType<ScreenshotCapture>();
                if (cap == null)
                {
                    var go = new GameObject("ScreenshotSystem");
                    cap = go.AddComponent<ScreenshotCapture>();
                }
            }

            yield return StartCoroutine(ScreenshotCapture.Instance.CaptureReportScreenshots(shots => {
                streamlinesPath = shots.streamlinesPath;
                verticalStreamlinesPath = shots.verticalStreamlinesPath;
                horizontalStreamlinesPath = shots.horizontalStreamlinesPath;
                surfacePressurePath = shots.surfacePressurePath;
                surfaceFrictionPath = shots.surfaceFrictionPath;
                captureDone = true;
            }));
            yield return new WaitUntil(() => captureDone);

            // 3. Detect Simulation and Gather Data
            var windSim = FindAnyObjectByType<WindTunnelSimulation3D>();
            var pipeSim = FindAnyObjectByType<PipeFlowSimulation3D>();
            var rotSim = FindAnyObjectByType<RotatingMachinerySimulation3D>();
            var damSim = FindAnyObjectByType<Simulation3D>();
            var loader = FindAnyObjectByType<RuntimeModelLoader>();
            var simManager = SimulationManager.Instance != null
                ? SimulationManager.Instance
                : FindAnyObjectByType<SimulationManager>();
            SimulationMetrics metrics = simManager != null ? simManager.GetLatestMetrics() : default;

            var data = new Dictionary<string, string>();
            
            // Project Metadata
            string simType = "CFD Analysis";
            if (windSim != null) simType = "External Aerodynamics";
            else if (pipeSim != null) simType = "Internal Pipe Flow";
            else if (rotSim != null) simType = "Rotating Machinery";
            else if (damSim != null) simType = "Free Surface Flow";

            data["{PROJECT_NAME}"] = $"FluidWorks {simType} Report";
            string modelPath = loader?.CurrentDescriptor?.visualModelPath;
            data["{MODEL_NAME}"] = string.IsNullOrEmpty(modelPath) ? "Generic Model" : Path.GetFileName(modelPath);
            data["{DATE}"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            data["{SOFTWARE_VERSION}"] = "FluidWorks v1.1.0";
            data["{CAMERA_POSITION}"] = Camera.main != null ? Camera.main.transform.position.ToString("F2") : "Standard View";

            // Default fallback values for data keys
            string[] keys = { 
                "{MEAN_VELOCITY}", "{MAX_VELOCITY}", "{PRESSURE_DROP}", 
                "{WALL_SHEAR}", "{VORTICITY}", "{DIVERGENCE_ERROR}", 
                "{VISCOSITY}", "{DENSITY}", "{TIMESTEP}", 
                "{ITERATION_COUNT}", "{GRID_SIZE}",
                "{RESULTS_OVERVIEW_ROWS}", "{MODE_SPECIFIC_ROWS}",
                "{ASSESSMENT_ROWS}", "{MODEL_QUALITY_ROWS}"
            };
            foreach (var k in keys) data[k] = "--";

            // Solver Setup & Diagnostics
            bool diagFound = false;

            if (windSim != null)
            {
                data["{VISCOSITY}"] = windSim.settings.dynamicViscosity.ToString("F6");
                data["{DENSITY}"] = windSim.settings.airDensity.ToString("F2");
                data["{TIMESTEP}"] = windSim.settings.timeScale.ToString("F4");
                data["{ITERATION_COUNT}"] = windSim.settings.iterationsPerFrame.ToString();
                data["{GRID_SIZE}"] = windSim.flowAxis.ToString();

                var solver = FindAnyObjectByType<NavierStokesGridSolver>();
                if (solver != null && solver.TryGetDiagnostics(out NavierStokesGridSolver.NavierDiagnostics diag))
                {
                    data["{MEAN_VELOCITY}"] = diag.meanVelocity.ToString("F2");
                    data["{MAX_VELOCITY}"] = diag.maxVelocity.ToString("F2");
                    data["{PRESSURE_DROP}"] = diag.pressureDrop.ToString("F2");
                    data["{WALL_SHEAR}"] = diag.wallShear.ToString("F4");
                    data["{VORTICITY}"] = diag.meanVorticity.ToString("F4");
                    data["{DIVERGENCE_ERROR}"] = diag.divergenceL1.ToString("F6");
                    data["{AUTO_CONCLUSION}"] = GenerateConclusion("Wind Tunnel", diag.divergenceL1, diag.pressureDrop, diag.maxVelocity);
                    diagFound = true;
                }
            }
            else if (pipeSim != null)
            {
                data["{VISCOSITY}"] = pipeSim.settings.dynamicViscosity.ToString("F6");
                data["{DENSITY}"] = pipeSim.settings.fluidDensity.ToString("F2");
                data["{TIMESTEP}"] = pipeSim.settings.timeScale.ToString("F4");
                data["{ITERATION_COUNT}"] = pipeSim.settings.iterationsPerFrame.ToString();
                data["{GRID_SIZE}"] = pipeSim.flowAxis.ToString();

                if (pipeSim.TryGetDiagnostics(out PipeFlowDiagnostics diag))
                {
                    data["{MEAN_VELOCITY}"] = diag.meanVelocity.ToString("F2");
                    data["{MAX_VELOCITY}"] = diag.maxVelocity.ToString("F2");
                    data["{PRESSURE_DROP}"] = diag.pressureDrop.ToString("F2");
                    data["{WALL_SHEAR}"] = diag.wallShear.ToString("F4");
                    data["{VORTICITY}"] = "N/A";
                    data["{DIVERGENCE_ERROR}"] = diag.divergenceL1.ToString("F6");
                    data["{AUTO_CONCLUSION}"] = GenerateConclusion("Pipe Flow", diag.divergenceL1, diag.pressureDrop, diag.maxVelocity);
                    diagFound = true;
                }
            }
            else if (rotSim != null)
            {
                data["{VISCOSITY}"] = rotSim.settings.dynamicViscosity.ToString("F6");
                data["{DENSITY}"] = rotSim.settings.fluidDensity.ToString("F2");
                data["{TIMESTEP}"] = rotSim.settings.timeScale.ToString("F4");
                data["{ITERATION_COUNT}"] = rotSim.settings.iterationsPerFrame.ToString();
                data["{GRID_SIZE}"] = rotSim.flowAxis.ToString();

                if (rotSim.TryGetDiagnostics(out RotatingMachineryDiagnostics diag))
                {
                    data["{MEAN_VELOCITY}"] = diag.meanVelocity.ToString("F2");
                    data["{MAX_VELOCITY}"] = diag.maxVelocity.ToString("F2");
                    data["{PRESSURE_DROP}"] = diag.pressureDrop.ToString("F2");
                    data["{WALL_SHEAR}"] = diag.wallShear.ToString("F4");
                    data["{VORTICITY}"] = (diag.torque).ToString("F2") + " Nm (Torque)";
                    data["{DIVERGENCE_ERROR}"] = diag.divergenceL1.ToString("F6");
                    data["{AUTO_CONCLUSION}"] = GenerateConclusion("Rotating Machinery", diag.divergenceL1, diag.pressureDrop, diag.maxVelocity);
                    diagFound = true;
                }
            }
            else if (damSim != null)
            {
                data["{VISCOSITY}"] = damSim.settings.viscosity.ToString("F6");
                data["{DENSITY}"] = damSim.settings.density.ToString("F2");
                data["{TIMESTEP}"] = damSim.settings.timeScale.ToString("F4");
                data["{ITERATION_COUNT}"] = damSim.settings.iterationsPerFrame.ToString();
                data["{GRID_SIZE}"] = damSim.transform.localScale.ToString();

                data["{MEAN_VELOCITY}"] = "N/A";
                data["{MAX_VELOCITY}"] = damSim.settings.maxVelocity.ToString("F2");
                data["{PRESSURE_DROP}"] = "N/A";
                data["{WALL_SHEAR}"] = "N/A";
                data["{VORTICITY}"] = "N/A";
                data["{DIVERGENCE_ERROR}"] = "N/A";
                data["{AUTO_CONCLUSION}"] = "Free-surface simulation completed successfully. Particle distribution analyzed for collision dynamics.";
                diagFound = true;
            }

            if (!diagFound)
            {
                data["{AUTO_CONCLUSION}"] = "Simulation diagnostics unavailable. Verify active simulation state.";
            }

            data["{RESULTS_OVERVIEW_ROWS}"] = BuildResultsOverviewRows(metrics);
            data["{MODE_SPECIFIC_ROWS}"] = BuildModeSpecificRows(metrics);
            data["{ASSESSMENT_ROWS}"] = BuildAssessmentRows(metrics);
            data["{MODEL_QUALITY_ROWS}"] = BuildModelQualityRows(metrics);

            // Images
            data["{LOGO_IMAGE}"] = LoadLogoDataUri();
            data["{STREAMLINE_IMAGE}"] = "file:///" + streamlinesPath.Replace("\\", "/");
            data["{VERTICAL_STREAMLINE_IMAGE}"] = "file:///" + verticalStreamlinesPath.Replace("\\", "/");
            data["{HORIZONTAL_STREAMLINE_IMAGE}"] = "file:///" + horizontalStreamlinesPath.Replace("\\", "/");
            data["{SURFACE_PRESSURE_IMAGE}"] = "file:///" + surfacePressurePath.Replace("\\", "/");
            data["{SURFACE_FRICTION_IMAGE}"] = "file:///" + surfaceFrictionPath.Replace("\\", "/");
            data["{VELOCITY_IMAGE}"] = data["{STREAMLINE_IMAGE}"];
            data["{PRESSURE_IMAGE}"] = data["{SURFACE_PRESSURE_IMAGE}"];
            
            // 4. Process
            html = TemplateParser.ReplacePlaceholders(html, data);

            // 5. Save HTML
            string reportDirectory = !string.IsNullOrEmpty(outputPdfPath)
                ? Path.GetDirectoryName(outputPdfPath)
                : Application.persistentDataPath;
            if (string.IsNullOrEmpty(reportDirectory))
            {
                reportDirectory = Application.persistentDataPath;
            }

            string reportBaseName = !string.IsNullOrEmpty(outputPdfPath)
                ? Path.GetFileNameWithoutExtension(outputPdfPath)
                : "GeneratedReport";

            string reportHtmlPath = Path.Combine(reportDirectory, reportBaseName + ".html");
            File.WriteAllText(reportHtmlPath, html);

            // 6. Export PDF
            string reportPdfPath = !string.IsNullOrEmpty(outputPdfPath)
                ? outputPdfPath
                : Path.Combine(reportDirectory, reportBaseName + ".pdf");
            bool pdfSuccess = PDFExporter.ExportToPDF(reportHtmlPath, reportPdfPath);

            if (pdfSuccess)
            {
                Debug.Log($"[ReportGenerator] Report complete! Saved to {reportPdfPath}");
                yield break;
            }

            Debug.LogWarning($"[ReportGenerator] PDF export failed. HTML report saved to {reportHtmlPath}");
        }

        private string GenerateConclusion(string type, float div, float pressDrop, float maxVel)
        {
            string summary = $"<b>{type} Assessment:</b> ";
            
            if (div < 0.005f)
                summary += "Solver converged successfully. Results are numerically stable. ";
            else
                summary += "Solver divergence detected; results may require further refinement. ";

            if (pressDrop > 500f)
                summary += "Significant pressure drop observed, indicating high drag or energy loss. ";
            else
                summary += "Low pressure drop indicates a highly optimized flow path. ";

            if (maxVel > 100f)
                summary += "High flow acceleration detected, potential for structural stress or cavitation in extreme zones. ";

            return summary;
        }

        private static string BuildResultsOverviewRows(SimulationMetrics m)
        {
            var sb = new StringBuilder();

            if (string.Equals(m.simulationMode, "DamBreak", StringComparison.OrdinalIgnoreCase))
            {
                AppendHtmlRow(sb, "Kinetic Energy", $"{m.liquidKineticEnergy:F2} J");
                AppendHtmlRow(sb, "RMS Velocity", $"{m.liquidVelocityRms:F3} m/s");
                AppendHtmlRow(sb, "Splash Height", $"{m.liquidSplashHeight:F3} m");
                AppendHtmlRow(sb, "Impact Pressure Proxy", $"{m.liquidImpactPressure:F2} Pa");
                AppendHtmlRow(sb, "Containment", $"{m.liquidContainment:P1}");
                AppendHtmlRow(sb, "Stability Index", $"{m.liquidStability:P1}");
                return sb.ToString();
            }

            if (string.Equals(m.simulationMode, "PipeFlow", StringComparison.OrdinalIgnoreCase))
            {
                AppendHtmlRow(sb, "Friction Factor", $"{m.pipeFrictionFactor:F4}");
                AppendHtmlRow(sb, "Head Loss", $"{m.pipeHeadLoss:F3} m");
                AppendHtmlRow(sb, "Flow Rate", $"{m.pipeFlowRate:F4} m3/s");
                AppendHtmlRow(sb, "Pipe Reynolds", $"{m.pipeReynolds:E2}");
                AppendHtmlRow(sb, "Pressure Gradient", $"{m.pipePressureGradient:F2} Pa/m");
                AppendHtmlRow(sb, "Mean Velocity", $"{m.navierMeanVelocity:F3} m/s");
                AppendHtmlRow(sb, "Max Velocity", $"{m.navierMaxVelocity:F3} m/s");
                AppendHtmlRow(sb, "Pressure Drop", $"{m.navierPressureDrop:F3} Pa");
                AppendHtmlRow(sb, "Wall Shear", $"{m.navierWallShear:F5} Pa");
                AppendHtmlRow(sb, "Divergence Error", $"{m.navierDivergenceL1:E2}");
                return sb.ToString();
            }

            if (string.Equals(m.simulationMode, "RotatingMachinery", StringComparison.OrdinalIgnoreCase))
            {
                AppendHtmlRow(sb, "Torque", $"{m.machineryTorque:F2} Nm");
                AppendHtmlRow(sb, "Power", $"{m.machineryPower:F1} W");
                AppendHtmlRow(sb, "Efficiency", $"{m.machineryEfficiency:P1}");
                AppendHtmlRow(sb, "Machine Reynolds", $"{m.reynolds:E2}");
                AppendHtmlRow(sb, "Angular Velocity", $"{m.machineryAngularVelocity:F2} rad/s");
                AppendHtmlRow(sb, "Tip-Speed Ratio", $"{m.machineryTipSpeedRatio:F2}");
                AppendHtmlRow(sb, "Wake Deficit", $"{m.machineryWakeDeficit:P1}");
                AppendHtmlRow(sb, "Mean Velocity", $"{m.navierMeanVelocity:F3} m/s");
                AppendHtmlRow(sb, "Max Velocity", $"{m.navierMaxVelocity:F3} m/s");
                AppendHtmlRow(sb, "Pressure Drop", $"{m.navierPressureDrop:F3} Pa");
                AppendHtmlRow(sb, "Wall Shear", $"{m.navierWallShear:F5} Pa");
                AppendHtmlRow(sb, "Divergence Error", $"{m.navierDivergenceL1:E2}");
                return sb.ToString();
            }

            AppendHtmlRow(sb, "Drag Coefficient", $"{m.drag:F4}");
            AppendHtmlRow(sb, "Lift Coefficient", $"{m.lift:F4}");
            AppendHtmlRow(sb, "Side Force Coefficient", $"{m.sideForceCoeff:F4}");
            AppendHtmlRow(sb, "Reynolds", $"{m.reynolds:E2}");
            AppendHtmlRow(sb, "Dynamic Pressure", $"{m.pressure:F0} Pa");
            AppendHtmlRow(sb, "Frontal Area", $"{m.referenceArea:F2} m2");
            AppendHtmlRow(sb, "Drag Force", $"{m.dragForce:F0} N");
            AppendHtmlRow(sb, "Vertical Aero Force", $"{m.verticalAeroForce:F0} N");
            AppendHtmlRow(sb, "Downforce", $"{m.downforce:F0} N");
            AppendHtmlRow(sb, "CoP Longitudinal", $"{m.centerOfPressureLongitudinal:F2} m");
            AppendHtmlRow(sb, "Pitch Moment", $"{m.pitchMoment:F0} Nm");
            AppendHtmlRow(sb, "Yaw Moment", $"{m.yawMoment:F0} Nm");
            AppendHtmlRow(sb, "Roll Moment", $"{m.rollMoment:F0} Nm");
            AppendHtmlRow(sb, "Front Axle Load", $"{m.frontAxleLoad:F0} N");
            AppendHtmlRow(sb, "Rear Axle Load", $"{m.rearAxleLoad:F0} N");
            AppendHtmlRow(sb, "Estimated Top Speed", $"{m.estimatedTopSpeed * 3.6f:F1} km/h");
            AppendHtmlRow(sb, "Mean Velocity", $"{m.navierMeanVelocity:F3} m/s");
            AppendHtmlRow(sb, "Peak Velocity", $"{m.navierMaxVelocity:F3} m/s");
            AppendHtmlRow(sb, "Pressure Drop", $"{m.navierPressureDrop:F3} Pa");
            AppendHtmlRow(sb, "Wall Shear", $"{m.navierWallShear:F5} Pa");
            AppendHtmlRow(sb, "Divergence Error", $"{m.navierDivergenceL1:E2}");
            return sb.ToString();
        }

        private static string BuildModeSpecificRows(SimulationMetrics m)
        {
            var sb = new StringBuilder();

            if (string.Equals(m.simulationMode, "PipeFlow", StringComparison.OrdinalIgnoreCase))
            {
                AppendHtmlRow(sb, "Flow Regime", m.flowRegime ?? "-");
                AppendHtmlRow(sb, "Assessment", m.assessment ?? "-");
                AppendHtmlRow(sb, "Overall Rating", m.qualityRating ?? "-");
                AppendHtmlRow(sb, "Quality Score", $"{m.qualityScore:P0}");
                AppendHtmlRow(sb, "Suggestions", m.qualityTips ?? "-");
                return sb.ToString();
            }

            if (string.Equals(m.simulationMode, "RotatingMachinery", StringComparison.OrdinalIgnoreCase))
            {
                AppendHtmlRow(sb, "Application", m.machineryApplicationLabel ?? "-");
                AppendHtmlRow(sb, "Energy Direction", m.machineryEnergyDirection ?? "-");
                AppendHtmlRow(sb, "Mean Swirl", $"{m.machineryMeanSwirl:F3} m/s");
                AppendHtmlRow(sb, "Flow Regime", m.flowRegime ?? "-");
                AppendHtmlRow(sb, "Assessment", m.assessment ?? "-");
                AppendHtmlRow(sb, "Overall Rating", m.qualityRating ?? "-");
                AppendHtmlRow(sb, "Quality Score", $"{m.qualityScore:P0}");
                AppendHtmlRow(sb, "Suggestions", m.qualityTips ?? "-");
                return sb.ToString();
            }

            if (string.Equals(m.simulationMode, "DamBreak", StringComparison.OrdinalIgnoreCase))
            {
                AppendHtmlRow(sb, "Liquid State", m.flowRegime ?? "-");
                AppendHtmlRow(sb, "Assessment", m.assessment ?? "-");
                AppendHtmlRow(sb, "Overall Rating", m.qualityRating ?? "-");
                AppendHtmlRow(sb, "Stability Score", $"{m.qualityScore:P0}");
                AppendHtmlRow(sb, "Suggestions", m.qualityTips ?? "-");
                return sb.ToString();
            }

            AppendHtmlRow(sb, "Regime", m.flowRegime ?? "-");
            AppendHtmlRow(sb, "Assessment", m.assessment ?? "-");
            AppendHtmlRow(sb, "Rating", m.qualityRating ?? "-");
            AppendHtmlRow(sb, "Score", $"{m.qualityScore:P0}");
            AppendHtmlRow(sb, "Suggestions", m.qualityTips ?? "-");
            return sb.ToString();
        }

        private static string BuildAssessmentRows(SimulationMetrics m)
        {
            var sb = new StringBuilder();
            AppendHtmlRow(sb, "Simulation Mode", m.simulationMode ?? "-");
            AppendHtmlRow(sb, "Timestamp", $"{m.timestamp:F1} s");
            AppendHtmlRow(sb, "Flow Regime", m.flowRegime ?? "-");
            AppendHtmlRow(sb, "Assessment", m.assessment ?? "-");
            AppendHtmlRow(sb, "Quality Rating", m.qualityRating ?? "-");
            AppendHtmlRow(sb, "Quality Score", $"{m.qualityScore:P0}");
            AppendHtmlRow(sb, "Suggestions", m.qualityTips ?? "-");
            return sb.ToString();
        }

        private static string BuildModelQualityRows(SimulationMetrics m)
        {
            var sb = new StringBuilder();
            AppendHtmlRow(sb, "Overall Score", $"{m.modelQualityScore:F0} / 100");
            AppendHtmlRow(sb, "Grade", m.modelQualityGrade ?? "-");
            AppendHtmlRow(sb, "Predicted Cd Range", $"{m.modelPredictedCdLow:F3} - {m.modelPredictedCdHigh:F3}");
            AppendHtmlRow(sb, "Separation Risk", $"{m.modelSeparationRisk:P0}");
            AppendHtmlRow(sb, "Downforce Potential", $"{m.modelDownforcePotential:P0}");
            AppendHtmlRow(sb, "ML Efficiency Score", $"{m.modelEfficiencyScore:P0}");
            AppendHtmlRow(sb, "Feature Breakdown", string.IsNullOrWhiteSpace(m.modelFeatureBreakdown) ? "-" : m.modelFeatureBreakdown);
            AppendHtmlRow(sb, "Suggested Improvements", string.IsNullOrWhiteSpace(m.modelImprovements) ? "-" : m.modelImprovements);
            return sb.ToString();
        }

        private static void AppendHtmlRow(StringBuilder sb, string label, string value)
        {
            sb.Append("<tr><td>")
                .Append(label)
                .Append("</td><td>")
                .Append(string.IsNullOrWhiteSpace(value) ? "-" : value)
                .Append("</td></tr>");
        }

        private string LoadLogoDataUri()
        {
            Texture2D logo = Resources.Load<Texture2D>("UI/Images/logos/transparent-black");
            if (logo == null)
            {
                logo = Resources.Load<Texture2D>("UI/Images/logos/transparent-white");
            }

            if (logo == null)
            {
                return "transparent-black.png";
            }

            byte[] bytes = EncodeTextureToPngBytes(logo);
            if (bytes == null || bytes.Length == 0)
            {
                return "transparent-black.png";
            }

            string base64 = Convert.ToBase64String(bytes);
            return $"data:image/png;base64,{base64}";
        }

        private static byte[] EncodeTextureToPngBytes(Texture2D source)
        {
            if (source == null)
            {
                return null;
            }

            RenderTexture previous = RenderTexture.active;
            RenderTexture temp = null;
            Texture2D readable = null;
            try
            {
                temp = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Graphics.Blit(source, temp);
                RenderTexture.active = temp;

                readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                readable.Apply();
                return readable.EncodeToPNG();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[ReportGenerator] Failed to encode logo texture: {ex.Message}");
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                if (temp != null)
                {
                    RenderTexture.ReleaseTemporary(temp);
                }

                if (readable != null)
                {
                    Destroy(readable);
                }
            }
        }
    }
}
