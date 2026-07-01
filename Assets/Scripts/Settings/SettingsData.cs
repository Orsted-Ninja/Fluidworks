using System;
using UnityEngine;

namespace FluidWorks.Settings
{
    [Serializable]
    public class SettingsData
    {
        public GeneralSettings general = new GeneralSettings();
        public SolverSettings solver = new SolverSettings();
        public VisualizationSettings visualization = new VisualizationSettings();
        public ReportingSettings reporting = new ReportingSettings();
        public ProjectSaveSettings projectSave = new ProjectSaveSettings();
        public PerformanceSettings performance = new PerformanceSettings();
        public ImportSettings import = new ImportSettings();
        public DiagnosticsSettings diagnostics = new DiagnosticsSettings();

        [Serializable]
        public class GeneralSettings
        {
            public string defaultProjectSavePath = "";
            public bool autoSaveEnabled = true;
            public int autoSaveIntervalMinutes = 10;
            public string themeMode = "Dark"; // Light, Dark, SystemDefault
            public bool openLastProjectOnStartup = false;
            public float uiScale = 1.0f;
        }

        [Serializable]
        public class SolverSettings
        {
            public int defaultGridSizeX = 64;
            public int defaultGridSizeY = 64;
            public int defaultGridSizeZ = 64;
            public float defaultViscosity = 0.001f;
            public float defaultDensity = 1.225f;
            public float defaultTimeStep = 0.02f;
            public int defaultIterationCount = 20;
            public string defaultSolverType = "NavierStokesGrid"; // NavierStokesGrid, SPH
            public bool adaptiveTimeStepEnabled = false;
        }

        [Serializable]
        public class VisualizationSettings
        {
            public string defaultColormap = "Jet"; // Jet, Viridis, Plasma, Turbo, Grayscale
            public string streamlineDensity = "Medium"; // Low, Medium, High
            public int slicePlaneResolution = 128;
            public bool showVelocityVectors = false;
            public bool showPressureContours = true;
            public bool showVorticityField = false;
            public float velocityScaleMultiplier = 1.0f;
        }

        [Serializable]
        public class ReportingSettings
        {
            public string defaultReportFormat = "PDF"; // HTML, PDF, Both
            public bool autoIncludeScreenshots = true;
            public bool autoIncludeDiagnostics = true;
            public bool autoIncludeConclusion = true;
            public string reportLogoPlacement = "Header"; // Header, Footer, Both
            public bool autoGenerateReportAfterSimulation = false;
        }

        [Serializable]
        public class ProjectSaveSettings
        {
            public bool saveCameraPosition = true;
            public bool saveVisualizationState = true;
            public bool saveScreenshotsInsideProject = true;
            public bool embedModelInsideProjectFile = true;
            public bool saveDiagnosticsInsideProject = true;
        }

        [Serializable]
        public class PerformanceSettings
        {
            public string simulationQuality = "Medium"; // Low, Medium, High, Ultra
            public bool enableGPUAcceleration = true;
            public int maxGridResolutionLimit = 256;
            public bool backgroundSimulationEnabled = false;
            public bool adaptiveSimulationResolution = true;
        }

        [Serializable]
        public class ImportSettings
        {
            public float defaultModelScaleFactor = 1.0f;
            public bool autoCenterModel = true;
            public bool autoAlignFlowDirection = true;
            public bool recalculateNormals = false;
        }

        [Serializable]
        public class DiagnosticsSettings
        {
            public bool showDivergenceError = true;
            public bool showVorticityMagnitude = true;
            public bool showPressureDrop = true;
            public bool enableStabilityWarnings = true;
        }

        public void ResetToDefaults()
        {
            general = new GeneralSettings();
            solver = new SolverSettings();
            visualization = new VisualizationSettings();
            reporting = new ReportingSettings();
            projectSave = new ProjectSaveSettings();
            performance = new PerformanceSettings();
            import = new ImportSettings();
            diagnostics = new DiagnosticsSettings();
            
            // Set default paths if needed
            general.defaultProjectSavePath = Application.persistentDataPath;
        }
    }
}
