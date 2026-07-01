using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.Linq;

namespace FluidWorks.Settings
{
    public class SettingsUIController : MonoBehaviour
    {
        private VisualElement _root;
        private VisualElement _settingsOverlay;
        private VisualElement _allSettingsContainer;

        public void Initialize(VisualElement root)
        {
            _root = root;
            
            // Load and instantiate the template from Resources
            var template = Resources.Load<VisualTreeAsset>("UI/UXML/SettingsWindow");
            if (template == null)
            {
                Debug.LogError("[SettingsUI] Could not find SettingsWindow.uxml in Resources/UI/UXML/");
                return;
            }

            var window = template.Instantiate();
            window.style.position = Position.Absolute;
            window.style.left = 0;
            window.style.top = 0;
            window.style.right = 0;
            window.style.bottom = 0;
            window.pickingMode = PickingMode.Ignore; // Let children handle picking
            
            _settingsOverlay = window.Q<VisualElement>("settings-overlay");
            if (_settingsOverlay != null)
            {
                _settingsOverlay.style.display = DisplayStyle.None;
            }
            
            _root.Add(window);

            _allSettingsContainer = window.Q<VisualElement>("all-settings-container");

            // Bind Buttons
            window.Q<Button>("settings-close-x").clicked += Hide;
            window.Q<Button>("settings-back-btn").clicked += Hide;
            window.Q<Button>("settings-save-btn").clicked += SaveAndApply;
            window.Q<Button>("settings-reset-btn").clicked += ResetToDefaults;
        }

        public void Show()
        {
            if (_settingsOverlay == null) return;
            _settingsOverlay.parent?.BringToFront(); // Bring the TemplateContainer to front
            _settingsOverlay.style.display = DisplayStyle.Flex;
            PopulateAllSettings();
        }

        public void Hide()
        {
            if (_settingsOverlay == null) return;
            _settingsOverlay.style.display = DisplayStyle.None;
        }

        private void PopulateAllSettings()
        {
            _allSettingsContainer.Clear();
            
            AddCategoryToContainer("General");
            AddCategoryToContainer("Solver");
            AddCategoryToContainer("Visualization");
            AddCategoryToContainer("Reporting");
            AddCategoryToContainer("Performance");
            AddCategoryToContainer("Project Save");
            AddCategoryToContainer("Import");
            AddCategoryToContainer("Diagnostics");
        }

        private void AddCategoryToContainer(string category)
        {
            var section = new VisualElement();
            var header = new Label(category.ToUpper());
            header.AddToClassList("section-header");
            section.Add(header);

            var settings = SettingsManager.Instance.settings;

            switch (category)
            {
                case "General":
                    AddSetting(section, "Theme Mode", "Select application UI theme", CreateDropdown(new[] { "Light", "Dark", "SystemDefault" }, settings.general.themeMode, v => settings.general.themeMode = v));
                    AddSetting(section, "Auto-Save", "Automatically save project changes", CreateToggle(settings.general.autoSaveEnabled, v => settings.general.autoSaveEnabled = v));
                    AddSetting(section, "Auto-Save Interval", "Minutes between saves", CreateSlider(1, 60, settings.general.autoSaveIntervalMinutes, v => settings.general.autoSaveIntervalMinutes = (int)v));
                    AddSetting(section, "Default Save Location", "Primary path for new projects", CreateTextField(settings.general.defaultProjectSavePath, v => settings.general.defaultProjectSavePath = v));
                    break;
                case "Solver":
                    AddSetting(section, "Grid Resolution", "Grid points (X/Y/Z cube)", CreateSlider(16, 256, settings.solver.defaultGridSizeX, v => {
                        settings.solver.defaultGridSizeX = (int)v;
                        settings.solver.defaultGridSizeY = (int)v;
                        settings.solver.defaultGridSizeZ = (int)v;
                    }));
                    AddSetting(section, "Time Step (dt)", "Integration time step in seconds", CreateTextField(settings.solver.defaultTimeStep.ToString(), v => { if (float.TryParse(v, out float res)) settings.solver.defaultTimeStep = res; }));
                    AddSetting(section, "Density (\u03c1)", "Fluid density (kg/m\u00b3)", CreateTextField(settings.solver.defaultDensity.ToString(), v => { if (float.TryParse(v, out float res)) settings.solver.defaultDensity = res; }));
                    AddSetting(section, "Viscosity (\u03bc)", "Dynamic viscosity (Pa\u00b7s)", CreateTextField(settings.solver.defaultViscosity.ToString(), v => { if (float.TryParse(v, out float res)) settings.solver.defaultViscosity = res; }));
                    AddSetting(section, "Iterations", "Steps per frame for NS solver", CreateSlider(5, 100, settings.solver.defaultIterationCount, v => settings.solver.defaultIterationCount = (int)v));
                    break;
                case "Visualization":
                    AddSetting(section, "Colormap", "Velocity/Pressure gradient", CreateDropdown(new[] { "Jet", "Viridis", "Plasma", "Turbo" }, settings.visualization.defaultColormap, v => settings.visualization.defaultColormap = v));
                    AddSetting(section, "Streamline Density", "Number of flow trace lines", CreateDropdown(new[] { "Low", "Medium", "High" }, settings.visualization.streamlineDensity, v => settings.visualization.streamlineDensity = v));
                    AddSetting(section, "Show Velocity Vectors", "Draw arrows in flow field", CreateToggle(settings.visualization.showVelocityVectors, v => settings.visualization.showVelocityVectors = v));
                    AddSetting(section, "Show Pressure Contours", "Enable body surface mapping", CreateToggle(settings.visualization.showPressureContours, v => settings.visualization.showPressureContours = v));
                    break;
                case "Reporting":
                    AddSetting(section, "Export Format", "Default file type for reports", CreateDropdown(new[] { "HTML", "PDF", "Both" }, settings.reporting.defaultReportFormat, v => settings.reporting.defaultReportFormat = v));
                    AddSetting(section, "Include Screenshots", "Add viewport captures to report", CreateToggle(settings.reporting.autoIncludeScreenshots, v => settings.reporting.autoIncludeScreenshots = v));
                    AddSetting(section, "Include Diagnostics", "Add solver health metrics to report", CreateToggle(settings.reporting.autoIncludeDiagnostics, v => settings.reporting.autoIncludeDiagnostics = v));
                    break;
                case "Performance":
                    AddSetting(section, "Simulation Quality", "Simulation fidelity level", CreateDropdown(new[] { "Low", "Medium", "High", "Ultra" }, settings.performance.simulationQuality, v => settings.performance.simulationQuality = v));
                    AddSetting(section, "GPU Acceleration", "Use Compute Shaders for solving", CreateToggle(settings.performance.enableGPUAcceleration, v => settings.performance.enableGPUAcceleration = v));
                    AddSetting(section, "Max Grid Limit", "Global cap for resolution", CreateSlider(64, 512, settings.performance.maxGridResolutionLimit, v => settings.performance.maxGridResolutionLimit = (int)v));
                    break;
                case "Project Save":
                    AddSetting(section, "Save Camera Position", "Keep view position in project", CreateToggle(settings.projectSave.saveCameraPosition, v => settings.projectSave.saveCameraPosition = v));
                    AddSetting(section, "Save Visual State", "Store render modes and colors", CreateToggle(settings.projectSave.saveVisualizationState, v => settings.projectSave.saveVisualizationState = v));
                    AddSetting(section, "Embed Model", "Store geometry inside project file", CreateToggle(settings.projectSave.embedModelInsideProjectFile, v => settings.projectSave.embedModelInsideProjectFile = v));
                    break;
                case "Import":
                    AddSetting(section, "Auto-Center Model", "Align model origin to tunnel center", CreateToggle(settings.import.autoCenterModel, v => settings.import.autoCenterModel = v));
                    AddSetting(section, "Auto-Align Flow", "Rotate model to face inlet", CreateToggle(settings.import.autoAlignFlowDirection, v => settings.import.autoAlignFlowDirection = v));
                    break;
                case "Diagnostics":
                    AddSetting(section, "Show Divergence Error", "Display solver stability index", CreateToggle(settings.diagnostics.showDivergenceError, v => settings.diagnostics.showDivergenceError = v));
                    AddSetting(section, "Show Pressure Drop", "Display inlet/outlet \u0394P", CreateToggle(settings.diagnostics.showPressureDrop, v => settings.diagnostics.showPressureDrop = v));
                    AddSetting(section, "Stability Warnings", "Notify if sim exceeds safe limits", CreateToggle(settings.diagnostics.enableStabilityWarnings, v => settings.diagnostics.enableStabilityWarnings = v));
                    break;
                default:
                    section.Add(new Label("Detailed configuration for " + category + " coming soon."));
                    break;
            }

            _allSettingsContainer.Add(section);
        }

        private void AddSetting(VisualElement container, string title, string desc, VisualElement control)
        {
            var row = new VisualElement();
            row.AddToClassList("setting-row");

            var labelCol = new VisualElement();
            labelCol.AddToClassList("setting-label-col");
            
            var titleLabel = new Label(title);
            titleLabel.AddToClassList("setting-title");
            labelCol.Add(titleLabel);

            var descLabel = new Label(desc);
            descLabel.AddToClassList("setting-desc");
            labelCol.Add(descLabel);

            var controlCol = new VisualElement();
            controlCol.AddToClassList("setting-control-col");
            controlCol.Add(control);

            row.Add(labelCol);
            row.Add(controlCol);
            container.Add(row);
        }

        private VisualElement CreateToggle(bool value, System.Action<bool> onValueChange)
        {
            var toggle = new Toggle { value = value };
            toggle.RegisterValueChangedCallback(evt => onValueChange(evt.newValue));
            return toggle;
        }

        private VisualElement CreateSlider(float min, float max, float current, System.Action<float> onValueChange)
        {
            var slider = new Slider(min, max) { value = current, showInputField = true };
            slider.RegisterValueChangedCallback(evt => onValueChange(evt.newValue));
            return slider;
        }

        private VisualElement CreateDropdown(IEnumerable<string> choices, string current, System.Action<string> onValueChange)
        {
            var dropdown = new DropdownField(choices.ToList(), current);
            dropdown.RegisterValueChangedCallback(evt => onValueChange(evt.newValue));
            return dropdown;
        }

        private VisualElement CreateTextField(string current, System.Action<string> onValueChange)
        {
            var textField = new TextField { value = current };
            textField.RegisterValueChangedCallback(evt => onValueChange(evt.newValue));
            return textField;
        }

        private void SaveAndApply()
        {
            SettingsManager.Instance.SaveSettings();
            Hide();
        }

        private void ResetToDefaults()
        {
            SettingsManager.Instance.ResetSettingsToDefault();
            PopulateAllSettings();
        }
    }
}
