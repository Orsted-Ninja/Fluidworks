using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using AeroFlow.Sim3D.PipeFlow;
using AeroFlow.Core;

namespace AeroFlow.UI
{
    public class PipeFlowPropertiesController
    {
        private readonly VisualElement _root;
        private readonly bool _showSettingsOnOpen;
        private PipeFlowSimulation3D _sim;

        private VisualElement _settingsTab;
        private VisualElement _modelTab;
        private Button _settingsTabBtn;
        private Button _modelTabBtn;

        private Label _resMeanVel;
        private Label _resMaxVel;
        private Label _resPressureDrop;
        private Label _resFriction;
        private Label _resHeadLoss;
        private Label _resFlowRate;
        private Label _resReynolds;

        public PipeFlowPropertiesController(VisualElement root, bool showSettingsOnOpen = true)
        {
            _root = root;
            _showSettingsOnOpen = showSettingsOnOpen;
            BindControls();
        }

        private void BindControls()
        {
            _sim = Object.FindFirstObjectByType<PipeFlowSimulation3D>();
            if (_sim == null)
            {
                Debug.LogWarning("[PipeFlowProperties] PipeFlowSimulation3D not found in scene.");
                return;
            }

            var loader = Object.FindFirstObjectByType<RuntimeModelLoader>();

            BindPipeSettings();
            BindVisualization();
            BindFluidProperties();
            BindSimulationSettings();
            BindDiagnostics();
            BindTabs();
            BindModelActions(loader);
            BindVehicleProperties(_sim, loader);
        }

        private void BindTabs()
        {
            _settingsTab = _root.Q<VisualElement>("pipe-tab-settings");
            _modelTab = _root.Q<VisualElement>("pipe-tab-model");
            _settingsTabBtn = _root.Q<Button>("pipe-tab-btn-settings");
            _modelTabBtn = _root.Q<Button>("pipe-tab-btn-model");

            if (_settingsTabBtn != null)
            {
                _settingsTabBtn.clicked += () => ShowTab(true);
                _settingsTabBtn.RegisterCallback<ClickEvent>(_ => ShowTab(true));
            }

            if (_modelTabBtn != null)
            {
                _modelTabBtn.clicked += () => ShowTab(false);
                _modelTabBtn.RegisterCallback<ClickEvent>(_ => ShowTab(false));
            }

            ShowTab(_showSettingsOnOpen);
        }

        private void ShowTab(bool showSettings)
        {
            if (_settingsTab != null) _settingsTab.style.display = showSettings ? DisplayStyle.Flex : DisplayStyle.None;
            if (_modelTab != null) _modelTab.style.display = showSettings ? DisplayStyle.None : DisplayStyle.Flex;
            if (_settingsTab != null) _settingsTab.style.visibility = showSettings ? Visibility.Visible : Visibility.Hidden;
            if (_modelTab != null) _modelTab.style.visibility = showSettings ? Visibility.Hidden : Visibility.Visible;

            if (_settingsTabBtn != null)
            {
                if (showSettings) _settingsTabBtn.AddToClassList("tab-button-active");
                else _settingsTabBtn.RemoveFromClassList("tab-button-active");
            }

            if (_modelTabBtn != null)
            {
                if (!showSettings) _modelTabBtn.AddToClassList("tab-button-active");
                else _modelTabBtn.RemoveFromClassList("tab-button-active");
            }
        }

        private void BindPipeSettings()
        {
            var inletVel = _root.Q<Slider>("pipe-inlet-velocity-slider");
            SliderInputBinder.BindFloat(inletVel, _sim.settings.inletVelocity, 2, value => _sim.settings.inletVelocity = value);

            // pipeRadius and pipeLength are now auto-detected from the loaded mesh geometry
            var radius = _root.Q<Slider>("pipe-radius-slider");
            if (radius != null) radius.SetEnabled(false);
            var length = _root.Q<Slider>("pipe-length-slider");
            if (length != null) length.SetEnabled(false);
        }

        private void BindVisualization()
        {
            var visualization = _root.Q<DropdownField>("pipe-visualization-dropdown");
            if (visualization == null)
                return;

            visualization.choices = new List<string>
            {
                PipeFlowSimulation3D.VisualizationStreamlines,
                PipeFlowSimulation3D.VisualizationSurfacePressure,
                PipeFlowSimulation3D.VisualizationSurfaceFriction
            };

            string current = PipeFlowSimulation3D.NormalizeVisualizationMode(_sim.settings.visualizationMode);
            visualization.SetValueWithoutNotify(current);
            _sim.SetVisualizationMode(current);
            visualization.RegisterValueChangedCallback(evt =>
            {
                _sim.SetVisualizationMode(PipeFlowSimulation3D.NormalizeVisualizationMode(evt.newValue));
            });
        }

        private void BindFluidProperties()
        {
            var density = _root.Q<Slider>("pipe-density-slider");
            var viscosity = _root.Q<Slider>("pipe-viscosity-slider");
            var roughness = _root.Q<Slider>("pipe-roughness-slider");
            var turbulence = _root.Q<Slider>("pipe-turbulence-slider");
            var laminarToggle = _root.Q<Toggle>("pipe-laminar-toggle");

            SliderInputBinder.BindFloat(density, _sim.settings.fluidDensity, 3, value => _sim.settings.fluidDensity = Mathf.Clamp(value, 0.5f, 1500f));

            SliderInputBinder.BindFloat(viscosity, _sim.settings.dynamicViscosity, 6, value =>
            {
                _sim.settings.dynamicViscosity = Mathf.Clamp(value, 0.000001f, 0.02f);
            });

            // wallRoughness removed — mesh boundaries now define wall properties
            if (roughness != null) roughness.SetEnabled(false);

            SliderInputBinder.BindFloat(turbulence, _sim.settings.turbulenceIntensity, 1, value =>
            {
                _sim.settings.turbulenceIntensity = Mathf.Clamp(value, 0f, 100f);
            });

            // isLaminar toggle removed — flow regime determined by Reynolds number
            if (laminarToggle != null) laminarToggle.SetEnabled(false);
        }

        private void BindSimulationSettings()
        {
            var timeScale = _root.Q<Slider>("pipe-timescale-slider");
            var iterations = _root.Q<SliderInt>("pipe-iterations-slider");
            var flowAxis = _root.Q<DropdownField>("pipe-flow-axis-dropdown");

            SliderInputBinder.BindFloat(timeScale, _sim.settings.timeScale, 2, value => _sim.settings.timeScale = value);
            SliderInputBinder.BindInt(iterations, _sim.settings.iterationsPerFrame, value => _sim.settings.iterationsPerFrame = value);

            if (flowAxis != null)
            {
                if (flowAxis.choices == null || flowAxis.choices.Count == 0)
                {
                    flowAxis.choices = new List<string> { "X Axis", "Y Axis", "Z Axis" };
                }

                flowAxis.SetValueWithoutNotify(WindTunnelSimulation3D.GetFlowAxisLabel(_sim.flowAxis));
                flowAxis.RegisterValueChangedCallback(evt =>
                {
                    _sim.flowAxis = WindTunnelSimulation3D.ParseFlowAxisLabel(evt.newValue);
                });
            }
        }

        private void BindDiagnostics()
        {
            _resMeanVel = _root.Q<Label>("pipe-res-mean-vel");
            _resMaxVel = _root.Q<Label>("pipe-res-max-vel");
            _resPressureDrop = _root.Q<Label>("pipe-res-pressure-drop");
            _resFriction = _root.Q<Label>("pipe-res-friction");
            _resHeadLoss = _root.Q<Label>("pipe-res-head-loss");
            _resFlowRate = _root.Q<Label>("pipe-res-flow-rate");
            _resReynolds = _root.Q<Label>("pipe-res-reynolds");

            _root.schedule.Execute(PollDiagnostics).Every(300);
        }

        private void BindModelActions(RuntimeModelLoader loader)
        {
            var loadBtn = _root.Q<Button>("load-model-button");
            if (loader != null && loadBtn != null && loadBtn.userData == null)
            {
                loadBtn.userData = true;
                loadBtn.clicked += loader.OpenFilePicker;
            }

            var resetBtn = _root.Q<Button>("reset-sim-button");
            if (resetBtn != null && _sim != null)
            {
                resetBtn.clicked += () => _sim.InitializeIfNeeded();
            }

            var reportBtn = _root.Q<Button>("generate-report-btn");
            if (reportBtn != null && reportBtn.userData == null)
            {
                reportBtn.userData = true;
                reportBtn.clicked += () => {
                    var gen = UnityEngine.Object.FindAnyObjectByType<FluidWorks.Reporting.ReportGenerator>();
                    if (gen == null)
                    {
                        var go = new GameObject("ReportSystem");
                        go.AddComponent<FluidWorks.Reporting.ScreenshotCapture>();
                        gen = go.AddComponent<FluidWorks.Reporting.ReportGenerator>();
                    }
                    if (gen != null) gen.PromptAndGenerateReport();
                };
            }
        }

        private void BindVehicleProperties(PipeFlowSimulation3D sim, RuntimeModelLoader loader)
        {
            if (sim.settings.vehicle == null)
            {
                sim.settings.vehicle = new WindTunnelVehicleProperties();
            }

            var vehicle = sim.settings.vehicle;
            var mass = _root.Q<Slider>("vehicle-mass-slider");
            var wheelbase = _root.Q<Slider>("vehicle-wheelbase-slider");
            var cgHeight = _root.Q<Slider>("vehicle-cg-height-slider");
            var rideHeight = _root.Q<Slider>("vehicle-ride-height-slider");
            var rake = _root.Q<Slider>("vehicle-rake-slider");
            var referenceArea = _root.Q<FloatField>("vehicle-reference-area-field");
            var trackWidth = _root.Q<FloatField>("vehicle-track-width-field");
            var modelScale = _root.Q<Slider>("model-scale-slider");
            var offsetX = _root.Q<Slider>("model-offset-x-slider");
            var offsetY = _root.Q<Slider>("model-offset-y-slider");
            var offsetZ = _root.Q<Slider>("model-offset-z-slider");

            void RefreshAlignmentAndSolver()
            {
                loader?.RefreshModelPlacement();
                sim.InitializeIfNeeded();
            }

            void RefreshSolverOnly()
            {
                sim.InitializeIfNeeded();
            }

            SliderInputBinder.BindFloat(mass, vehicle.massKg, 0, value => vehicle.massKg = Mathf.Clamp(value, 250f, 6000f));
            SliderInputBinder.BindFloat(wheelbase, vehicle.wheelbaseMeters, 2, value =>
            {
                vehicle.wheelbaseMeters = Mathf.Clamp(value, 1.5f, 6f);
                RefreshSolverOnly();
            });
            SliderInputBinder.BindFloat(cgHeight, vehicle.cgHeightMeters, 2, value =>
            {
                vehicle.cgHeightMeters = Mathf.Clamp(value, 0.15f, 1.5f);
                RefreshSolverOnly();
            });
            SliderInputBinder.BindFloat(rideHeight, vehicle.rideHeightMeters, 3, value =>
            {
                vehicle.rideHeightMeters = Mathf.Clamp(value, 0f, 0.5f);
                RefreshAlignmentAndSolver();
            });
            SliderInputBinder.BindFloat(rake, vehicle.rakeAngleDegrees, 2, value =>
            {
                vehicle.rakeAngleDegrees = Mathf.Clamp(value, -8f, 8f);
                RefreshAlignmentAndSolver();
            });

            if (referenceArea != null)
            {
                referenceArea.SetValueWithoutNotify(vehicle.referenceArea);
                referenceArea.RegisterValueChangedCallback(evt =>
                {
                    vehicle.referenceArea = Mathf.Max(0f, evt.newValue);
                });
            }

            if (trackWidth != null)
            {
                trackWidth.SetValueWithoutNotify(vehicle.trackWidthMeters);
                trackWidth.RegisterValueChangedCallback(evt =>
                {
                    vehicle.trackWidthMeters = Mathf.Max(0f, evt.newValue);
                    RefreshSolverOnly();
                });
            }

            if (modelScale != null)
            {
                float initialScale = loader != null ? loader.GetModelScale() : 1f;
                modelScale.SetValueWithoutNotify(initialScale);
                modelScale.RegisterValueChangedCallback(evt =>
                {
                    if (loader == null) return;
                    loader.SetModelScale(Mathf.Clamp(evt.newValue, 0.10f, 4.00f));
                    RefreshAlignmentAndSolver();
                });
            }

            if (loader != null)
            {
                Vector3 currentOffset = loader.GetModelOffset();

                if (offsetX != null)
                {
                    offsetX.SetValueWithoutNotify(currentOffset.x);
                    offsetX.RegisterValueChangedCallback(evt =>
                    {
                        currentOffset.x = Mathf.Clamp(evt.newValue, -5f, 5f);
                        loader.SetModelOffset(currentOffset);
                        RefreshAlignmentAndSolver();
                    });
                }

                if (offsetY != null)
                {
                    offsetY.SetValueWithoutNotify(currentOffset.y);
                    offsetY.RegisterValueChangedCallback(evt =>
                    {
                        currentOffset.y = Mathf.Clamp(evt.newValue, -5f, 5f);
                        loader.SetModelOffset(currentOffset);
                        RefreshAlignmentAndSolver();
                    });
                }

                if (offsetZ != null)
                {
                    offsetZ.SetValueWithoutNotify(currentOffset.z);
                    offsetZ.RegisterValueChangedCallback(evt =>
                    {
                        currentOffset.z = Mathf.Clamp(evt.newValue, -5f, 5f);
                        loader.SetModelOffset(currentOffset);
                        RefreshAlignmentAndSolver();
                    });
                }
            }
        }

        private void PollDiagnostics()
        {
            if (_sim == null)
            {
                _sim = Object.FindFirstObjectByType<PipeFlowSimulation3D>();
            }

            if (_sim == null || !_sim.TryGetDiagnostics(out var diag))
            {
                SetWaiting();
                return;
            }

            SetLabel(_resMeanVel, $"{diag.meanVelocity:F2} m/s");
            SetLabel(_resMaxVel, $"{diag.maxVelocity:F2} m/s");
            SetLabel(_resPressureDrop, $"{diag.pressureDrop:F2} Pa");
            // frictionFactor is now derived by SimulationManager; show opening count instead
            SetLabel(_resFriction, $"{diag.openingCount} openings");
            SetLabel(_resHeadLoss, $"{diag.fluidCellCount} cells");
            SetLabel(_resFlowRate, $"{diag.flowRate:F4} m³/s");
            SetLabel(_resReynolds, $"{diag.pipeReynolds:F0}");
        }

        private void SetWaiting()
        {
            SetLabel(_resMeanVel, "--");
            SetLabel(_resMaxVel, "--");
            SetLabel(_resPressureDrop, "--");
            SetLabel(_resFriction, "--");
            SetLabel(_resHeadLoss, "--");
            SetLabel(_resFlowRate, "--");
            SetLabel(_resReynolds, "--");
        }

        private static void SetLabel(Label label, string text)
        {
            if (label != null) label.text = text;
        }
    }
}
