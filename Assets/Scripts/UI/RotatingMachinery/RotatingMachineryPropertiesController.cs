using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using AeroFlow.Sim3D.RotatingMachinery;
using AeroFlow.Core;

namespace AeroFlow.UI
{
    public class RotatingMachineryPropertiesController
    {
        private readonly VisualElement _root;
        private RotatingMachinerySimulation3D _sim;
        private DropdownField _applicationDropdown;
        private DropdownField _directionDropdown;
        private DropdownField _motionModeDropdown;
        private Slider _rpmSlider;
        private Slider _tipSpeedRatioSlider;

        private VisualElement _settingsTab;
        private VisualElement _modelTab;
        private Button _settingsTabBtn;
        private Button _modelTabBtn;

        private Label _resTorque;
        private Label _resPower;
        private Label _resEfficiency;
        private Label _resSwirl;
        private Label _resReynolds;
        private Label _resMeanVel;
        private Label _resPressureDrop;
        private Label _resAngularVel;
        private Label _resTipSpeedRatio;
        private Label _resWakeDeficit;
        private Label _resEnergyDirection;
        private Label _resApplication;

        public RotatingMachineryPropertiesController(VisualElement root)
        {
            _root = root;
            BindControls();
        }

        private void BindControls()
        {
            _sim = Object.FindFirstObjectByType<RotatingMachinerySimulation3D>();
            if (_sim == null)
            {
                Debug.LogWarning("[MachineryProperties] RotatingMachinerySimulation3D not found in scene.");
                return;
            }

            var loader = Object.FindFirstObjectByType<RuntimeModelLoader>();

            BindMachinerySettings();
            BindFlowConditions();
            BindVisualization();
            BindSimulationSettings();
            BindDiagnostics();
            BindTabs();
            BindModelActions(loader);
            BindVehicleProperties(_sim, loader);
        }

        private void BindTabs()
        {
            _settingsTab = _root.Q<VisualElement>("machinery-tab-settings");
            _modelTab = _root.Q<VisualElement>("machinery-tab-model");
            _settingsTabBtn = _root.Q<Button>("machinery-btn-settings");
            _modelTabBtn = _root.Q<Button>("machinery-btn-model");

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

            ShowTab(true);
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


        private void BindMachinerySettings()
        {
            _applicationDropdown = _root.Q<DropdownField>("machinery-application-dropdown");
            _directionDropdown = _root.Q<DropdownField>("machinery-direction-dropdown");
            _motionModeDropdown = _root.Q<DropdownField>("machinery-motion-mode-dropdown");
            _rpmSlider = _root.Q<Slider>("machinery-rpm-slider");
            _tipSpeedRatioSlider = _root.Q<Slider>("machinery-tip-speed-ratio-slider");
            var rotAxis = _root.Q<DropdownField>("machinery-rotation-axis-dropdown");
            var zoneRadius = _root.Q<Slider>("machinery-zone-radius-slider");
            var zoneHeight = _root.Q<Slider>("machinery-zone-height-slider");
            var zoneOffset = _root.Q<Slider>("machinery-zone-offset-slider");

            BindDropdownChoices(_applicationDropdown, new List<string>
            {
                "Windmill",
                "Fan",
                "Propeller",
                "Axial Turbine",
                "Radial Turbine"
            });

            BindDropdownChoices(_directionDropdown, new List<string>
            {
                "Counter-Clockwise",
                "Clockwise"
            });

            BindDropdownChoices(_motionModeDropdown, new List<string>
            {
                "Constant Speed",
                "Fluid-Driven Adaptive",
                "Torque-Coupled (Future)"
            });

            if (_applicationDropdown != null)
            {
                _applicationDropdown.SetValueWithoutNotify(ApplicationTypeToLabel(_sim.settings.applicationType));
                _applicationDropdown.RegisterValueChangedCallback(evt =>
                {
                    ApplyPreset(LabelToApplicationType(evt.newValue));
                    SyncMotionControls();
                });
            }

            if (_directionDropdown != null)
            {
                _directionDropdown.SetValueWithoutNotify(DirectionToLabel(_sim.settings.rotationDirection));
                _directionDropdown.RegisterValueChangedCallback(evt =>
                {
                    _sim.settings.rotationDirection = LabelToDirection(evt.newValue);
                });
            }

            if (_motionModeDropdown != null)
            {
                _motionModeDropdown.SetValueWithoutNotify(MotionModeToLabel(_sim.settings.motionMode));
                _motionModeDropdown.RegisterValueChangedCallback(evt =>
                {
                    _sim.settings.motionMode = LabelToMotionMode(evt.newValue);
                });
            }

            SliderInputBinder.BindFloat(_rpmSlider, _sim.settings.angularVelocityRPM, 0, value =>
            {
                _sim.settings.angularVelocityRPM = Mathf.Clamp(value, 10f, 10000f);
                UpdateTipSpeedRatioFromRpm();
                SyncMotionControls();
            });

            SliderInputBinder.BindFloat(_tipSpeedRatioSlider, _sim.settings.tipSpeedRatio, 2, value =>
            {
                _sim.settings.tipSpeedRatio = Mathf.Clamp(value, 0.1f, 20f);
                UpdateRpmFromTipSpeedRatio();
                SyncMotionControls();
            });

            if (rotAxis != null)
            {
                if (rotAxis.choices == null || rotAxis.choices.Count == 0)
                {
                    rotAxis.choices = new List<string> { "Y Axis (Up)", "X Axis", "Z Axis" };
                }

                string current = RotationAxisToLabel(_sim.settings.rotationAxis);
                rotAxis.SetValueWithoutNotify(current);
                rotAxis.RegisterValueChangedCallback(evt =>
                {
                    _sim.settings.rotationAxis = LabelToRotationAxis(evt.newValue);
                });
            }

            SliderInputBinder.BindFloat(zoneRadius, _sim.settings.rotatingZoneRadius, 2, value =>
            {
                _sim.settings.rotatingZoneRadius = Mathf.Clamp(value, 0.1f, 5f);
                UpdateRpmFromTipSpeedRatio();
                SyncMotionControls();
            });

            SliderInputBinder.BindFloat(zoneHeight, _sim.settings.rotatingZoneHalfHeight, 2, value =>
            {
                _sim.settings.rotatingZoneHalfHeight = Mathf.Clamp(value, 0.05f, 3f);
            });

            SliderInputBinder.BindFloat(zoneOffset, _sim.settings.rotatingZoneAxisOffset, 2, value =>
            {
                _sim.settings.rotatingZoneAxisOffset = Mathf.Clamp(value, -2f, 2f);
            });

            SyncMotionControls();
        }

        private void BindFlowConditions()
        {
            var inletVel = _root.Q<Slider>("machinery-inlet-velocity-slider");
            var density = _root.Q<Slider>("machinery-density-slider");
            var viscosity = _root.Q<Slider>("machinery-viscosity-slider");
            var turbulence = _root.Q<Slider>("machinery-turbulence-slider");

            SliderInputBinder.BindFloat(inletVel, _sim.settings.inletVelocity, 2, value =>
            {
                _sim.settings.inletVelocity = Mathf.Clamp(value, 0.1f, 80f);
                UpdateRpmFromTipSpeedRatio();
                SyncMotionControls();
            });

            SliderInputBinder.BindFloat(density, _sim.settings.fluidDensity, 3, value =>
            {
                _sim.settings.fluidDensity = Mathf.Clamp(value, 0.5f, 1500f);
            });

            SliderInputBinder.BindFloat(viscosity, _sim.settings.dynamicViscosity, 6, value =>
            {
                _sim.settings.dynamicViscosity = Mathf.Clamp(value, 0.000001f, 0.02f);
            });

            SliderInputBinder.BindFloat(turbulence, _sim.settings.turbulenceIntensity, 1, value =>
            {
                _sim.settings.turbulenceIntensity = Mathf.Clamp(value, 0f, 100f);
            });
        }

        private void BindVisualization()
        {
            var visualization = _root.Q<DropdownField>("machinery-visualization-dropdown");
            if (visualization == null || _sim == null)
                return;

            visualization.choices = new List<string>
            {
                WindTunnelSimulation3D.VisualizationStreamlines,
                WindTunnelSimulation3D.VisualizationVerticalStreamlines,
                WindTunnelSimulation3D.VisualizationHorizontalStreamlines,
                WindTunnelSimulation3D.VisualizationSurfacePressure
            };

            string current = NormalizeVisualizationMode(_sim.settings.visualizationMode);
            _sim.settings.visualizationMode = current;
            visualization.SetValueWithoutNotify(current);
            ApplyVisualizationMode(current);

            visualization.RegisterValueChangedCallback(evt =>
            {
                string normalized = NormalizeVisualizationMode(evt.newValue);
                _sim.settings.visualizationMode = normalized;
                ApplyVisualizationMode(normalized);
            });
        }

        private void BindSimulationSettings()
        {
            var timeScale = _root.Q<Slider>("machinery-timescale-slider");
            var iterations = _root.Q<SliderInt>("machinery-iterations-slider");
            var flowAxis = _root.Q<DropdownField>("machinery-flow-axis-dropdown");

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
            _resTorque = _root.Q<Label>("machinery-res-torque");
            _resPower = _root.Q<Label>("machinery-res-power");
            _resEfficiency = _root.Q<Label>("machinery-res-efficiency");
            _resSwirl = _root.Q<Label>("machinery-res-swirl");
            _resReynolds = _root.Q<Label>("machinery-res-reynolds");
            _resMeanVel = _root.Q<Label>("machinery-res-mean-vel");
            _resPressureDrop = _root.Q<Label>("machinery-res-pressure-drop");
            _resAngularVel = _root.Q<Label>("machinery-res-angular-vel");
            _resTipSpeedRatio = _root.Q<Label>("machinery-res-tip-speed-ratio");
            _resWakeDeficit = _root.Q<Label>("machinery-res-wake-deficit");
            _resEnergyDirection = _root.Q<Label>("machinery-res-energy-direction");
            _resApplication = _root.Q<Label>("machinery-res-application");

            _root.schedule.Execute(PollDiagnostics).Every(300);
        }

        private void BindModelActions(RuntimeModelLoader loader)
        {
            var loadModelBtn = _root.Q<Button>("load-model-button");
            if (loader != null && loadModelBtn != null && loadModelBtn.userData == null)
            {
                loadModelBtn.userData = true;
                loadModelBtn.clicked += loader.OpenFilePicker;
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

        private void BindVehicleProperties(RotatingMachinerySimulation3D sim, RuntimeModelLoader loader)
        {
            if (sim == null)
            {
                return;
            }

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
                _sim.InitializeIfNeeded();
            }

            SliderInputBinder.BindFloat(mass, vehicle.massKg, 0, value => vehicle.massKg = Mathf.Clamp(value, 250f, 6000f));
            SliderInputBinder.BindFloat(wheelbase, vehicle.wheelbaseMeters, 2, value => vehicle.wheelbaseMeters = Mathf.Clamp(value, 1.5f, 6f));
            SliderInputBinder.BindFloat(cgHeight, vehicle.cgHeightMeters, 2, value => vehicle.cgHeightMeters = Mathf.Clamp(value, 0.15f, 1.5f));
            SliderInputBinder.BindFloat(rideHeight, vehicle.rideHeightMeters, 3, value => vehicle.rideHeightMeters = Mathf.Clamp(value, 0f, 0.5f));
            SliderInputBinder.BindFloat(rake, vehicle.rakeAngleDegrees, 2, value => vehicle.rakeAngleDegrees = Mathf.Clamp(value, -8f, 8f));

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
            if (_sim == null || !_sim.TryGetDiagnostics(out var diag))
            {
                SetWaiting();
                return;
            }

            SetLabel(_resTorque, $"{diag.torque:F2} Nm");
            SetLabel(_resPower, $"{diag.power:F1} W");
            SetLabel(_resEfficiency, $"{diag.efficiency * 100f:F1} %");
            SetLabel(_resSwirl, $"{diag.meanSwirl:F2} m/s");
            SetLabel(_resReynolds, $"{diag.machineReynolds:F0}");
            SetLabel(_resMeanVel, $"{diag.meanVelocity:F2} m/s");
            SetLabel(_resPressureDrop, $"{diag.pressureDrop:F2} Pa");
            SetLabel(_resAngularVel, $"{diag.angularVelocityRadS:F2} rad/s");
            SetLabel(_resTipSpeedRatio, $"{diag.tipSpeedRatio:F2}");
            SetLabel(_resWakeDeficit, $"{diag.wakeVelocityDeficit:P1}");
            SetLabel(_resEnergyDirection, diag.energyDirection ?? "-");
            SetLabel(_resApplication, diag.applicationLabel ?? "-");
        }

        private void SetWaiting()
        {
            SetLabel(_resTorque, "--");
            SetLabel(_resPower, "--");
            SetLabel(_resEfficiency, "--");
            SetLabel(_resSwirl, "--");
            SetLabel(_resReynolds, "--");
            SetLabel(_resMeanVel, "--");
            SetLabel(_resPressureDrop, "--");
            SetLabel(_resAngularVel, "--");
            SetLabel(_resTipSpeedRatio, "--");
            SetLabel(_resWakeDeficit, "--");
            SetLabel(_resEnergyDirection, "--");
            SetLabel(_resApplication, "--");
        }

        private void ApplyPreset(RotatoryApplicationType preset)
        {
            _sim.ApplyPreset(preset);
        }

        private void SyncMotionControls()
        {
            if (_sim == null)
            {
                return;
            }

            if (_applicationDropdown != null)
                _applicationDropdown.SetValueWithoutNotify(ApplicationTypeToLabel(_sim.settings.applicationType));
            if (_directionDropdown != null)
                _directionDropdown.SetValueWithoutNotify(DirectionToLabel(_sim.settings.rotationDirection));
            if (_motionModeDropdown != null)
                _motionModeDropdown.SetValueWithoutNotify(MotionModeToLabel(_sim.settings.motionMode));
            if (_rpmSlider != null)
                _rpmSlider.SetValueWithoutNotify(_sim.settings.angularVelocityRPM);
            if (_tipSpeedRatioSlider != null)
                _tipSpeedRatioSlider.SetValueWithoutNotify(_sim.settings.tipSpeedRatio);
        }

        private void UpdateTipSpeedRatioFromRpm()
        {
            if (_sim == null) return;
            float radius = Mathf.Max(_sim.settings.rotatingZoneRadius, 1e-3f);
            float omega = Mathf.Abs(_sim.settings.angularVelocityRPM) * Mathf.PI / 30f;
            float tsr = omega * radius / Mathf.Max(_sim.settings.inletVelocity, 1e-3f);
            _sim.settings.tipSpeedRatio = tsr;
            if (_tipSpeedRatioSlider != null)
                _tipSpeedRatioSlider.SetValueWithoutNotify(tsr);
        }

        private void UpdateRpmFromTipSpeedRatio()
        {
            if (_sim == null) return;
            float radius = Mathf.Max(_sim.settings.rotatingZoneRadius, 1e-3f);
            float rpm = _sim.settings.tipSpeedRatio * Mathf.Max(_sim.settings.inletVelocity, 1e-3f) / (2f * Mathf.PI * radius) * 60f;
            _sim.settings.angularVelocityRPM = Mathf.Clamp(rpm, 10f, 10000f);
            if (_rpmSlider != null)
                _rpmSlider.SetValueWithoutNotify(_sim.settings.angularVelocityRPM);
        }

        private static void BindDropdownChoices(DropdownField field, List<string> choices)
        {
            if (field == null || choices == null || choices.Count == 0) return;
            field.choices = choices;
        }

        private static string ApplicationTypeToLabel(RotatoryApplicationType type)
        {
            switch (type)
            {
                case RotatoryApplicationType.Fan: return "Fan";
                case RotatoryApplicationType.Propeller: return "Propeller";
                case RotatoryApplicationType.AxialTurbine: return "Axial Turbine";
                case RotatoryApplicationType.RadialTurbine: return "Radial Turbine";
                default: return "Windmill";
            }
        }

        private static RotatoryApplicationType LabelToApplicationType(string label)
        {
            switch (label)
            {
                case "Fan": return RotatoryApplicationType.Fan;
                case "Propeller": return RotatoryApplicationType.Propeller;
                case "Axial Turbine": return RotatoryApplicationType.AxialTurbine;
                case "Radial Turbine": return RotatoryApplicationType.RadialTurbine;
                default: return RotatoryApplicationType.Windmill;
            }
        }

        private static string DirectionToLabel(RotatoryRotationDirection direction)
        {
            return direction == RotatoryRotationDirection.Clockwise ? "Clockwise" : "Counter-Clockwise";
        }

        private static RotatoryRotationDirection LabelToDirection(string label)
        {
            return string.Equals(label, "Clockwise", System.StringComparison.OrdinalIgnoreCase)
                ? RotatoryRotationDirection.Clockwise
                : RotatoryRotationDirection.CounterClockwise;
        }

        private static string MotionModeToLabel(RotatoryMotionMode mode)
        {
            switch (mode)
            {
                case RotatoryMotionMode.FluidDrivenAdaptive: return "Fluid-Driven Adaptive";
                case RotatoryMotionMode.TorqueCoupledFuture: return "Torque-Coupled (Future)";
                default: return "Constant Speed";
            }
        }

        private static RotatoryMotionMode LabelToMotionMode(string label)
        {
            if (string.Equals(label, "Fluid-Driven Adaptive", System.StringComparison.OrdinalIgnoreCase))
                return RotatoryMotionMode.FluidDrivenAdaptive;
            if (string.Equals(label, "Torque-Coupled (Future)", System.StringComparison.OrdinalIgnoreCase))
                return RotatoryMotionMode.TorqueCoupledFuture;
            return RotatoryMotionMode.ConstantSpeed;
        }

        private static void SetLabel(Label label, string text)
        {
            if (label != null) label.text = text;
        }

        private static string NormalizeVisualizationMode(string value)
        {
            if (string.Equals(value, WindTunnelSimulation3D.VisualizationSurfacePressure, System.StringComparison.OrdinalIgnoreCase))
                return WindTunnelSimulation3D.VisualizationSurfacePressure;
            if (string.Equals(value, WindTunnelSimulation3D.VisualizationSurfaceFriction, System.StringComparison.OrdinalIgnoreCase))
                return WindTunnelSimulation3D.VisualizationSurfaceFriction;
            if (string.Equals(value, WindTunnelSimulation3D.VisualizationVerticalStreamlines, System.StringComparison.OrdinalIgnoreCase))
                return WindTunnelSimulation3D.VisualizationVerticalStreamlines;
            if (string.Equals(value, WindTunnelSimulation3D.VisualizationHorizontalStreamlines, System.StringComparison.OrdinalIgnoreCase))
                return WindTunnelSimulation3D.VisualizationHorizontalStreamlines;
            return WindTunnelSimulation3D.VisualizationStreamlines;
        }

        private static void ApplyVisualizationMode(string visualizationMode)
        {
            var mainScreen = Object.FindFirstObjectByType<MainScreenController>();
            if (mainScreen != null)
            {
                mainScreen.ApplyVisualizationMode(visualizationMode);
            }
        }

        private static string RotationAxisToLabel(Vector3 axis)
        {
            if (Mathf.Abs(axis.y) >= Mathf.Abs(axis.x) && Mathf.Abs(axis.y) >= Mathf.Abs(axis.z))
                return "Y Axis (Up)";
            if (Mathf.Abs(axis.z) >= Mathf.Abs(axis.x))
                return "Z Axis";
            return "X Axis";
        }

        private static Vector3 LabelToRotationAxis(string label)
        {
            switch (label)
            {
                case "X Axis": return Vector3.right;
                case "Z Axis": return Vector3.forward;
                default: return Vector3.up;
            }
        }
    }
}
