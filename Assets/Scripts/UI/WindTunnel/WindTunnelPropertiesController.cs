using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using AeroFlow.Visualization;

namespace AeroFlow.UI
{
    public class WindTunnelPropertiesController
    {
        private readonly VisualElement _root;

        private StreamlineFieldRenderer _streamlineRenderer;

        public WindTunnelPropertiesController(VisualElement root)
        {
            _root = root;
            BindControls();
        }

        private void BindControls()
        {
            var sim = Object.FindFirstObjectByType<WindTunnelSimulation3D>();
            if (sim == null)
            {
                Debug.LogWarning("[WindTunnelProperties] WindTunnelSimulation3D not found in scene.");
                return;
            }

            var loader = Object.FindFirstObjectByType<AeroFlow.Core.RuntimeModelLoader>();
            _streamlineRenderer = sim.GetComponent<StreamlineFieldRenderer>() ?? Object.FindFirstObjectByType<StreamlineFieldRenderer>();
            HideBoundaryControlsInTunnelTab();

            BindTabs();
            BindProfiles(sim);
            BindVisualization(sim);
            BindSimulation(sim);
            BindVehicleProperties(sim, loader);
            BindModelActions(sim, loader);

            // Force a refresh of all values once at the end to ensure UI is in sync
            RefreshAllSliders(sim);
        }

        private void HideBoundaryControlsInTunnelTab()
        {
            string[] hiddenControlNames =
            {
                "inlet-velocity-slider",
                "angle-of-attack-slider",
                "turbulence-slider",
                "wind-direction-mode-dropdown",
                "inlet-source-dropdown",
                "flow-axis-dropdown",
                "wt-custom-dir-group",
                "outlet-pressure-field",
                "vehicle-moving-ground-toggle",
                "vehicle-ground-speed-scale-slider",
                "vehicle-wheel-rotation-toggle",
                "vehicle-flip-direction-toggle",
                "inlet-panel-width-slider",
                "inlet-panel-height-slider",
                "outlet-panel-width-slider",
                "outlet-panel-height-slider",
                "floor-length-scale-slider"
            };

            for (int i = 0; i < hiddenControlNames.Length; i++)
            {
                VisualElement element = _root.Q<VisualElement>(hiddenControlNames[i]);
                if (element != null)
                {
                    element.style.display = DisplayStyle.None;
                }
            }

            foreach (Label label in _root.Query<Label>().ToList())
            {
                if (label == null)
                {
                    continue;
                }

                if (label.text == "BOUNDARY CONDITIONS" || label.text == "INLET / OUTLET PANELS" || label.text == "FLOOR PLANE")
                {
                    label.style.display = DisplayStyle.None;
                }
            }
        }

        private void BindTabs()
        {
            var tabTunnel = _root.Q<VisualElement>("wt-tab-tunnel");
            var tabModel = _root.Q<VisualElement>("wt-tab-model");
            var tabBtnTunnel = _root.Q<Button>("wt-tab-btn-tunnel");
            var tabBtnModel = _root.Q<Button>("wt-tab-btn-model");

            void SelectTab(bool showTunnel)
            {
                if (tabTunnel != null) tabTunnel.style.display = showTunnel ? DisplayStyle.Flex : DisplayStyle.None;
                if (tabModel != null) tabModel.style.display = showTunnel ? DisplayStyle.None : DisplayStyle.Flex;
                if (tabTunnel != null) tabTunnel.style.visibility = showTunnel ? Visibility.Visible : Visibility.Hidden;
                if (tabModel != null) tabModel.style.visibility = showTunnel ? Visibility.Hidden : Visibility.Visible;

                Color active = new Color(80f / 255f, 140f / 255f, 1f, 0.18f);
                Color inactive = new Color(0f, 0f, 0f, 0f);

                if (tabBtnTunnel != null)
                {
                    tabBtnTunnel.style.backgroundColor = new StyleColor(showTunnel ? active : inactive);
                    tabBtnTunnel.style.borderBottomWidth = showTunnel ? 2 : 0;
                    tabBtnTunnel.style.borderBottomColor = new StyleColor(new Color(80f / 255f, 140f / 255f, 1f, 1f));
                }

                if (tabBtnModel != null)
                {
                    tabBtnModel.style.backgroundColor = new StyleColor(showTunnel ? inactive : active);
                    tabBtnModel.style.borderBottomWidth = showTunnel ? 0 : 2;
                    tabBtnModel.style.borderBottomColor = new StyleColor(new Color(80f / 255f, 140f / 255f, 1f, 1f));
                }
            }

            if (tabBtnTunnel != null)
            {
                tabBtnTunnel.clicked += () => SelectTab(true);
                tabBtnTunnel.RegisterCallback<ClickEvent>(_ => SelectTab(true));
            }

            if (tabBtnModel != null)
            {
                tabBtnModel.clicked += () => SelectTab(false);
                tabBtnModel.RegisterCallback<ClickEvent>(_ => SelectTab(false));
            }

            SelectTab(true);
        }

        private void BindProfiles(WindTunnelSimulation3D sim)
        {
            var standardBtn = _root.Q<Button>("profile-standard-button");
            var f1Btn = _root.Q<Button>("profile-f1-button");

            void UpdateProfileHighlight(string activeProfile)
            {
                standardBtn?.EnableInClassList("profile-button--active",
                    string.Equals(activeProfile, WindTunnelSimulation3D.StandardProfileName, System.StringComparison.OrdinalIgnoreCase));
                f1Btn?.EnableInClassList("profile-button--active",
                    string.Equals(activeProfile, WindTunnelSimulation3D.F1BalancedProfileName, System.StringComparison.OrdinalIgnoreCase));
            }

            if (standardBtn != null)
            {
                standardBtn.clicked += () =>
                {
                    if (sim == null) return;
                    sim.ApplyStandardProfile();
                    UpdateProfileHighlight(WindTunnelSimulation3D.StandardProfileName);
                    RefreshAllSliders(sim);
                };
            }

            if (f1Btn != null)
            {
                f1Btn.clicked += () =>
                {
                    if (sim == null) return;
                    sim.ApplyF1BalancedProfile();
                    UpdateProfileHighlight(WindTunnelSimulation3D.F1BalancedProfileName);
                    RefreshAllSliders(sim);
                };
            }

            UpdateProfileHighlight(sim.settings.tunnelProfile);
        }

        private void RefreshAllSliders(WindTunnelSimulation3D sim)
        {
            var streamlineDensity = _root.Q<SliderInt>("streamline-density-slider");
            var vizMode = _root.Q<DropdownField>("viz-mode-dropdown");
            var graphicsMode = _root.Q<DropdownField>("graphics-mode-dropdown");

            streamlineDensity?.SetValueWithoutNotify(sim.settings.streamlineDensity);
            vizMode?.SetValueWithoutNotify(sim.settings.visualizationMode);
            if (graphicsMode != null)
            {
                string graphicsLabel = WindTunnelSimulation3D.GetGraphicsModeLabel(sim.settings.graphicsMode);
                if (!graphicsMode.choices.Contains(graphicsLabel))
                {
                    graphicsLabel = "Fluid";
                }
                graphicsMode.SetValueWithoutNotify(graphicsLabel);
            }
        }

        private void BindWindConditions(WindTunnelSimulation3D sim, AeroFlow.Core.RuntimeModelLoader loader)
        {
            var fluidType = _root.Q<DropdownField>("fluid-type-dropdown");
            var density = _root.Q<Slider>("air-density-slider");
            var viscosity = _root.Q<Slider>("dynamic-viscosity-slider");
            var inletVel = _root.Q<Slider>("inlet-velocity-slider");
            var angleOfAtk = _root.Q<Slider>("angle-of-attack-slider");
            var turbulence = _root.Q<Slider>("turbulence-slider");
            var dirMode = _root.Q<DropdownField>("wind-direction-mode-dropdown");
            var inletSource = _root.Q<DropdownField>("inlet-source-dropdown");
            var flowAxis = _root.Q<DropdownField>("flow-axis-dropdown");
            var customGroup = _root.Q<VisualElement>("wt-custom-dir-group");
            var dirXField = _root.Q<FloatField>("dir-x-field");
            var dirYField = _root.Q<FloatField>("dir-y-field");
            var dirZField = _root.Q<FloatField>("dir-z-field");
            var outletPressure = _root.Q<FloatField>("outlet-pressure-field");
            var movingGround = _root.Q<Toggle>("vehicle-moving-ground-toggle");
            var wheelRotation = _root.Q<Toggle>("vehicle-wheel-rotation-toggle");
            var flipDirection = _root.Q<Toggle>("vehicle-flip-direction-toggle");
            var groundSpeedScale = _root.Q<Slider>("vehicle-ground-speed-scale-slider");
            var inletPanelWidth = _root.Q<Slider>("inlet-panel-width-slider");
            var inletPanelHeight = _root.Q<Slider>("inlet-panel-height-slider");
            var outletPanelWidth = _root.Q<Slider>("outlet-panel-width-slider");
            var outletPanelHeight = _root.Q<Slider>("outlet-panel-height-slider");
            var floorLengthScale = _root.Q<Slider>("floor-length-scale-slider");
            var enclosure = sim.GetComponent<WindTunnelEnclosure>() ?? Object.FindFirstObjectByType<WindTunnelEnclosure>();

            if (sim.settings.vehicle == null)
            {
                sim.settings.vehicle = new WindTunnelVehicleProperties();
            }

            if (fluidType != null)
            {
                if (fluidType.choices == null || fluidType.choices.Count == 0)
                {
                    fluidType.choices = new List<string> { "Air" };
                }
                fluidType.SetValueWithoutNotify("Air");
                fluidType.SetEnabled(false);
            }

            SliderInputBinder.BindFloat(inletVel, sim.settings.inletVelocity, 2, value => { if (sim != null) sim.settings.inletVelocity = value; });
            SliderInputBinder.BindFloat(angleOfAtk, sim.settings.angleOfAttack, 2, value => { if (sim != null) sim.settings.angleOfAttack = value; });
            SliderInputBinder.BindFloat(turbulence, sim.settings.turbulenceIntensity, 2, value => { if (sim != null) sim.settings.turbulenceIntensity = Mathf.Clamp(value, 0f, 100f); });
            SliderInputBinder.BindFloat(density, sim.settings.airDensity, 3, value => { if (sim != null) sim.settings.airDensity = Mathf.Clamp(value, 0.5f, 1500f); });
            SliderInputBinder.BindFloat(viscosity, sim.settings.dynamicViscosity, 6, value =>
            {
                if (sim != null) sim.settings.dynamicViscosity = Mathf.Clamp(value, 0.000001f, 0.02f);
            });

            if (dirMode != null)
            {
                if (dirMode.choices == null || dirMode.choices.Count == 0)
                {
                    dirMode.choices = new List<string> { "Auto", "Custom" };
                }

                bool custom = sim.settings.useCustomInletDirection;
                dirMode.SetValueWithoutNotify(custom ? "Custom" : "Auto");
                if (customGroup != null) customGroup.style.display = custom ? DisplayStyle.Flex : DisplayStyle.None;

                dirMode.RegisterValueChangedCallback(evt =>
                {
                    if (sim == null) return;
                    bool customMode = evt.newValue == "Custom";
                    sim.settings.useCustomInletDirection = customMode;
                    if (customGroup != null)
                    {
                        customGroup.style.display = customMode ? DisplayStyle.Flex : DisplayStyle.None;
                    }
                });
            }

            if (inletSource != null)
            {
                if (inletSource.choices == null || inletSource.choices.Count == 0)
                {
                    inletSource.choices = new List<string> { "Auto", "Left", "Right", "Front", "Back", "Top", "Bottom" };
                }

                if (!inletSource.choices.Contains(sim.settings.inletSource))
                {
                    sim.settings.inletSource = "Auto";
                }

                inletSource.SetValueWithoutNotify(sim.settings.inletSource);
                inletSource.RegisterValueChangedCallback(evt =>
                {
                    if (sim == null) return;
                    sim.settings.inletSource = evt.newValue;
                    sim.ResetSimulation();
                });
            }

            if (flowAxis != null)
            {
                if (flowAxis.choices == null || flowAxis.choices.Count == 0)
                {
                    flowAxis.choices = new List<string> { "X Axis", "Y Axis", "Z Axis" };
                }

                flowAxis.SetValueWithoutNotify(WindTunnelSimulation3D.GetFlowAxisLabel(sim.flowAxis));
                flowAxis.RegisterValueChangedCallback(evt =>
                {
                    if (sim == null) return;
                    sim.flowAxis = WindTunnelSimulation3D.ParseFlowAxisLabel(evt.newValue);
                    sim.ResetSimulation();
                });
            }

            void SyncDirection()
            {
                if (sim == null || !sim.settings.useCustomInletDirection) return;

                float x = dirXField?.value ?? sim.settings.inletDirection.x;
                float y = dirYField?.value ?? sim.settings.inletDirection.y;
                float z = dirZField?.value ?? sim.settings.inletDirection.z;
                Vector3 direction = new Vector3(x, y, z);
                if (direction.sqrMagnitude > 1e-6f)
                {
                    direction.Normalize();
                }
                sim.settings.inletDirection = direction;
            }

            dirXField?.SetValueWithoutNotify(sim.settings.inletDirection.x);
            dirYField?.SetValueWithoutNotify(sim.settings.inletDirection.y);
            dirZField?.SetValueWithoutNotify(sim.settings.inletDirection.z);
            dirXField?.RegisterValueChangedCallback(_ => SyncDirection());
            dirYField?.RegisterValueChangedCallback(_ => SyncDirection());
            dirZField?.RegisterValueChangedCallback(_ => SyncDirection());

            if (outletPressure != null)
            {
                outletPressure.SetValueWithoutNotify(sim.settings.outletStaticPressurePa);
                outletPressure.RegisterValueChangedCallback(evt =>
                {
                    if (sim != null) sim.settings.outletStaticPressurePa = Mathf.Clamp(evt.newValue, 50000f, 150000f);
                });
            }

            if (movingGround != null)
            {
                movingGround.SetValueWithoutNotify(sim.settings.vehicle.useMovingGround);
                movingGround.RegisterValueChangedCallback(evt =>
                {
                    if (sim == null) return;
                    sim.settings.vehicle.useMovingGround = evt.newValue;
                    sim.ResetSimulation();
                });
            }

            if (wheelRotation != null)
            {
                wheelRotation.SetValueWithoutNotify(sim.settings.vehicle.useWheelRotationProxies);
                wheelRotation.RegisterValueChangedCallback(evt =>
                {
                    if (sim == null) return;
                    sim.settings.vehicle.useWheelRotationProxies = evt.newValue;
                    sim.ResetSimulation();
                });
            }

            if (flipDirection != null)
            {
                flipDirection.SetValueWithoutNotify(sim.settings.flipVehicleDirection);
                flipDirection.RegisterValueChangedCallback(evt =>
                {
                    if (sim == null) return;
                    flipDirection.SetValueWithoutNotify(sim.settings.flipVehicleDirection);
                    flipDirection.RegisterValueChangedCallback(evt =>
                    {
                        if (sim == null) return;
                        sim.settings.flipVehicleDirection = evt.newValue;
                        loader?.AlignToSimulationContext();
                        sim.ResetSimulation();
                    });
                });
            }

            SliderInputBinder.BindFloat(groundSpeedScale, sim.settings.vehicle.groundSpeedScale, 2, value =>
            {
                if (sim == null) return;
                sim.settings.vehicle.groundSpeedScale = Mathf.Clamp(value, 0f, 1.5f);
                sim.ResetSimulation();
            });

            if (enclosure != null)
            {
                SliderInputBinder.BindFloat(inletPanelWidth, enclosure.inletPanelWidthScale, 2, value =>
                {
                    if (enclosure == null) return;
                    enclosure.inletPanelWidthScale = Mathf.Clamp(value, 0.2f, 1.2f);
                    enclosure.MarkDirty();
                });
                SliderInputBinder.BindFloat(inletPanelHeight, enclosure.inletPanelHeightScale, 2, value =>
                {
                    if (enclosure == null) return;
                    enclosure.inletPanelHeightScale = Mathf.Clamp(value, 0.2f, 1.2f);
                    enclosure.MarkDirty();
                });
                SliderInputBinder.BindFloat(outletPanelWidth, enclosure.outletPanelWidthScale, 2, value =>
                {
                    if (enclosure == null) return;
                    enclosure.outletPanelWidthScale = Mathf.Clamp(value, 0.2f, 1.2f);
                    enclosure.MarkDirty();
                });
                SliderInputBinder.BindFloat(outletPanelHeight, enclosure.outletPanelHeightScale, 2, value =>
                {
                    if (enclosure == null) return;
                    enclosure.outletPanelHeightScale = Mathf.Clamp(value, 0.2f, 1.2f);
                    enclosure.MarkDirty();
                });
                SliderInputBinder.BindFloat(floorLengthScale, enclosure.floorLengthScale, 2, value =>
                {
                    if (enclosure == null) return;
                    enclosure.floorLengthScale = Mathf.Clamp(value, 0.4f, 1.2f);
                    enclosure.MarkDirty();
                });
            }
        }

        private void BindVisualization(WindTunnelSimulation3D sim)
        {
            var vizMode = _root.Q<DropdownField>("viz-mode-dropdown");
            var streamlineDensity = _root.Q<SliderInt>("streamline-density-slider");
            var rawSolverToggle = _root.Q<Toggle>("raw-solver-toggle");

            if (vizMode != null)
            {
                if (vizMode.choices == null || vizMode.choices.Count == 0)
                {
                    vizMode.choices = new List<string>
                    {
                        WindTunnelSimulation3D.VisualizationStreamlines,
                        WindTunnelSimulation3D.VisualizationVerticalStreamlines,
                        WindTunnelSimulation3D.VisualizationHorizontalStreamlines,
                        WindTunnelSimulation3D.VisualizationSurfacePressure,
                        WindTunnelSimulation3D.VisualizationSurfaceFriction
                    };
                }

                string currentViz = WindTunnelSimulation3D.NormalizeVisualizationMode(sim.settings.visualizationMode);
                if (!vizMode.choices.Contains(currentViz))
                {
                    currentViz = WindTunnelSimulation3D.VisualizationStreamlines;
                }

                vizMode.SetValueWithoutNotify(currentViz);
                sim.SetVisualizationMode(currentViz);
                ApplyVisualizationMode(currentViz);

                vizMode.RegisterValueChangedCallback(evt =>
                {
                    if (sim == null) return;
                    string normalized = WindTunnelSimulation3D.NormalizeVisualizationMode(evt.newValue);
                    sim.SetVisualizationMode(normalized);
                    ApplyVisualizationMode(normalized);
                });
            }

            var graphicsMode = _root.Q<DropdownField>("graphics-mode-dropdown");
            if (graphicsMode != null)
            {
                if (graphicsMode.choices == null || graphicsMode.choices.Count == 0)
                {
                    graphicsMode.choices = new List<string> { "Fluid", "Off" };
                }

                if (sim.settings.graphicsMode == WindTunnelGraphicsMode.Particle)
                {
                    sim.SetGraphicsMode(WindTunnelGraphicsMode.Fluid);
                }

                graphicsMode.SetValueWithoutNotify(WindTunnelSimulation3D.GetGraphicsModeLabel(sim.settings.graphicsMode));
                graphicsMode.RegisterValueChangedCallback(evt =>
                {
                    if (sim == null) return;
                    sim.SetGraphicsMode(WindTunnelGraphicsModeParser.Parse(evt.newValue));
                });
            }

            if (streamlineDensity != null)
            {
                streamlineDensity.lowValue = WindTunnelSimulation3D.MinStreamlineDensity;
                streamlineDensity.highValue = WindTunnelSimulation3D.MaxStreamlineDensity;

                int currentDensity = sim.GetClampedStreamlineDensity();
                sim.settings.streamlineDensity = currentDensity;
                SliderInputBinder.BindInt(streamlineDensity, currentDensity, value =>
                {
                    if (sim == null) return;
                    sim.settings.streamlineDensity = Mathf.Clamp(
                        value,
                        WindTunnelSimulation3D.MinStreamlineDensity,
                        WindTunnelSimulation3D.MaxStreamlineDensity);

                    if (_streamlineRenderer != null)
                    {
                        _streamlineRenderer.maxLineCount = sim.settings.streamlineDensity;
                    }
                });
            }

            if (rawSolverToggle != null)
            {
                rawSolverToggle.SetValueWithoutNotify(sim.useRawSolverDataOnly);
                rawSolverToggle.RegisterValueChangedCallback(evt =>
                {
                    if (sim == null) return;
                    sim.useRawSolverDataOnly = evt.newValue;
                    sim.SetVisualizationMode(sim.settings.visualizationMode);
                });
            }

            Shader.SetGlobalInt("_AeroFlowClipEnabled", 0);
        }

        private void BindSimulation(WindTunnelSimulation3D sim)
        {
            var timeScale = _root.Q<Slider>("timescale-slider");
            var iterations = _root.Q<SliderInt>("iterations-slider");

            SliderInputBinder.BindFloat(timeScale, sim.settings.timeScale, 3, value => { if (sim != null) sim.settings.timeScale = value; });
            SliderInputBinder.BindInt(iterations, sim.settings.iterationsPerFrame, value => { if (sim != null) sim.settings.iterationsPerFrame = value; });
        }

        private void BindVehicleProperties(WindTunnelSimulation3D sim, AeroFlow.Core.RuntimeModelLoader loader)
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
            var frontWeight = _root.Q<Slider>("vehicle-front-weight-slider");
            var aeroBalance = _root.Q<Slider>("vehicle-front-aero-balance-slider");
            var power = _root.Q<Slider>("vehicle-engine-power-slider");
            var efficiency = _root.Q<Slider>("vehicle-drivetrain-efficiency-slider");
            var rollingResistance = _root.Q<Slider>("vehicle-rolling-resistance-slider");
            var referenceArea = _root.Q<FloatField>("vehicle-reference-area-field");
            var trackWidth = _root.Q<FloatField>("vehicle-track-width-field");
            var wheelRadius = _root.Q<FloatField>("vehicle-wheel-radius-field");
            var wheelWidth = _root.Q<FloatField>("vehicle-wheel-width-field");
            var modelScale = _root.Q<Slider>("model-scale-slider");
            var offsetX = _root.Q<Slider>("model-offset-x-slider");
            var offsetY = _root.Q<Slider>("model-offset-y-slider");
            var offsetZ = _root.Q<Slider>("model-offset-z-slider");

            void RefreshSolver()
            {
                if (sim != null) sim.ResetSimulation();
            }

            void RefreshSolverOnly()
            {
                if (sim != null) sim.ResetSimulation();
            }

            SliderInputBinder.BindFloat(mass, vehicle.massKg, 0, value => { if (sim != null) vehicle.massKg = Mathf.Clamp(value, 250f, 6000f); });
            SliderInputBinder.BindFloat(wheelbase, vehicle.wheelbaseMeters, 2, value =>
            {
                if (sim == null) return;
                vehicle.wheelbaseMeters = Mathf.Clamp(value, 1.5f, 6f);
                RefreshSolverOnly();
            });
            SliderInputBinder.BindFloat(cgHeight, vehicle.cgHeightMeters, 2, value =>
            {
                if (sim == null) return;
                vehicle.cgHeightMeters = Mathf.Clamp(value, 0.15f, 1.5f);
                RefreshSolverOnly();
            });
            SliderInputBinder.BindFloat(rideHeight, vehicle.rideHeightMeters, 3, value =>
            {
                if (sim == null) return;
                vehicle.rideHeightMeters = Mathf.Clamp(value, 0f, 0.5f);
                RefreshSolver();
            });
            SliderInputBinder.BindFloat(rake, vehicle.rakeAngleDegrees, 2, value =>
            {
                if (sim == null) return;
                vehicle.rakeAngleDegrees = Mathf.Clamp(value, -8f, 8f);
                RefreshSolver();
            });
            SliderInputBinder.BindFloat(frontWeight, vehicle.frontWeightDistribution * 100f, 1, value => { if (sim != null) vehicle.frontWeightDistribution = Mathf.Clamp01(value * 0.01f); });
            SliderInputBinder.BindFloat(aeroBalance, vehicle.frontAeroBalance * 100f, 1, value => { if (sim != null) vehicle.frontAeroBalance = Mathf.Clamp01(value * 0.01f); });
            SliderInputBinder.BindFloat(power, vehicle.enginePowerKw, 0, value => { if (sim != null) vehicle.enginePowerKw = Mathf.Clamp(value, 0f, 2000f); });
            SliderInputBinder.BindFloat(efficiency, vehicle.drivetrainEfficiency * 100f, 1, value => { if (sim != null) vehicle.drivetrainEfficiency = Mathf.Clamp(value * 0.01f, 0.5f, 1f); });
            SliderInputBinder.BindFloat(rollingResistance, vehicle.rollingResistanceCoeff, 3, value => { if (sim != null) vehicle.rollingResistanceCoeff = Mathf.Clamp(value, 0.001f, 0.08f); });

            if (referenceArea != null)
            {
                referenceArea.SetValueWithoutNotify(vehicle.referenceArea);
                referenceArea.RegisterValueChangedCallback(evt =>
                {
                    if (sim != null) vehicle.referenceArea = Mathf.Max(0f, evt.newValue);
                });
            }

            if (trackWidth != null)
            {
                trackWidth.SetValueWithoutNotify(vehicle.trackWidthMeters);
                trackWidth.RegisterValueChangedCallback(evt =>
                {
                    if (sim == null) return;
                    vehicle.trackWidthMeters = Mathf.Max(0f, evt.newValue);
                    RefreshSolverOnly();
                });
            }

            if (wheelRadius != null)
            {
                wheelRadius.SetValueWithoutNotify(vehicle.wheelRadiusMeters);
                wheelRadius.RegisterValueChangedCallback(evt =>
                {
                    if (sim == null) return;
                    vehicle.wheelRadiusMeters = Mathf.Max(0f, evt.newValue);
                    RefreshSolverOnly();
                });
            }

            if (wheelWidth != null)
            {
                wheelWidth.SetValueWithoutNotify(vehicle.wheelWidthMeters);
                wheelWidth.RegisterValueChangedCallback(evt =>
                {
                    if (sim == null) return;
                    vehicle.wheelWidthMeters = Mathf.Max(0f, evt.newValue);
                    RefreshSolverOnly();
                });
            }

            if (modelScale != null)
            {
                float initialScale = loader != null ? loader.GetModelScale() : 1f;
                modelScale.SetValueWithoutNotify(initialScale);
                modelScale.RegisterValueChangedCallback(evt =>
                {
                    if (loader == null || sim == null) return;
                    loader.SetModelScale(Mathf.Clamp(evt.newValue, 0.10f, 4.00f));
                    loader.RefreshModelPlacement();
                    RefreshSolver();
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
                        if (loader == null || sim == null) return;
                        currentOffset.x = Mathf.Clamp(evt.newValue, -5f, 5f);
                        loader.SetModelOffset(currentOffset);
                        loader.RefreshModelPlacement();
                        RefreshSolver();
                    });
                }

                if (offsetY != null)
                {
                    offsetY.SetValueWithoutNotify(currentOffset.y);
                    offsetY.RegisterValueChangedCallback(evt =>
                    {
                        if (loader == null || sim == null) return;
                        currentOffset.y = Mathf.Clamp(evt.newValue, -5f, 5f);
                        loader.SetModelOffset(currentOffset);
                        loader.RefreshModelPlacement();
                        RefreshSolver();
                    });
                }

                if (offsetZ != null)
                {
                    offsetZ.SetValueWithoutNotify(currentOffset.z);
                    offsetZ.RegisterValueChangedCallback(evt =>
                    {
                        if (loader == null || sim == null) return;
                        currentOffset.z = Mathf.Clamp(evt.newValue, -5f, 5f);
                        loader.SetModelOffset(currentOffset);
                        loader.RefreshModelPlacement();
                        RefreshSolver();
                    });
                }
            }
        }

        private void BindModelActions(WindTunnelSimulation3D sim, AeroFlow.Core.RuntimeModelLoader loader)
        {
            var loadModelBtn = _root.Q<Button>("load-model-button");
            var resetWindBtn = _root.Q<Button>("reset-wind-button");

            if (loader != null && loadModelBtn != null && loadModelBtn.userData == null)
            {
                loadModelBtn.userData = true;
                loadModelBtn.clicked += () => { if (loader != null) loader.OpenFilePicker(); };
            }

            if (resetWindBtn != null && resetWindBtn.userData == null)
            {
                resetWindBtn.userData = true;
                resetWindBtn.clicked += () => { if (sim != null) sim.ResetSimulation(); };
            }

        }

        private void ApplyVisualizationMode(string visualizationMode)
        {
            var mainScreen = Object.FindFirstObjectByType<MainScreenController>();
            if (mainScreen != null)
            {
                mainScreen.ApplyVisualizationMode(visualizationMode);
            }
        }

        private static class WindTunnelGraphicsModeParser
        {
            public static WindTunnelGraphicsMode Parse(string value)
            {
                if (string.Equals(value, "Particles", System.StringComparison.OrdinalIgnoreCase))
                {
                    return WindTunnelGraphicsMode.Particle;
                }

                if (string.Equals(value, "Off", System.StringComparison.OrdinalIgnoreCase))
                {
                    return WindTunnelGraphicsMode.Off;
                }

                return WindTunnelGraphicsMode.Fluid;
            }
        }
    }
}
