using UnityEngine;
using UnityEngine.UIElements;
using AeroFlow.Visualization;

namespace AeroFlow.UI
{
    public class BoundaryConditionsController
    {
        private readonly VisualElement _root;

        public BoundaryConditionsController(VisualElement root)
        {
            _root = root;
            BindControls();
        }

        private void BindControls()
        {
            var windSim = Object.FindFirstObjectByType<WindTunnelSimulation3D>();
            var pipeSim = Object.FindFirstObjectByType<AeroFlow.Sim3D.PipeFlow.PipeFlowSimulation3D>();
            var damSim = Object.FindFirstObjectByType<Simulation3D>();

            var windSection = _root.Q<VisualElement>("windtunnel-boundary-section");
            var pipeSection = _root.Q<VisualElement>("pipeflow-boundary-section");
            var damSection = _root.Q<VisualElement>("dambreak-boundary-section");
            var windStatus = _root.Q<Label>("windtunnel-boundary-status");
            var damStatus = _root.Q<Label>("dambreak-boundary-status");

            if (windSection != null)
            {
                bool isWindActive = windSim != null && windSim.isActiveAndEnabled;
                windSection.SetEnabled(isWindActive);
                windSection.style.display = isWindActive ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (pipeSection != null)
            {
                bool isPipeActive = pipeSim != null && pipeSim.isActiveAndEnabled;
                pipeSection.SetEnabled(isPipeActive);
                pipeSection.style.display = isPipeActive ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (damSection != null)
            {
                bool isDamActive = damSim != null && damSim.isActiveAndEnabled;
                damSection.SetEnabled(isDamActive);
                damSection.style.display = isDamActive ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (windStatus != null && windSim != null)
            {
                windStatus.text = "Active external aero scene detected.";
            }

            if (damStatus != null && damSim != null)
            {
                damStatus.text = "Active free-surface scene detected.";
            }

            BindWindTunnel(windSim);
            BindPipeFlow(pipeSim);
            BindDamBreak(damSim);

            var modelScale = _root.Q<Slider>("bc-model-scale-slider");
            var loader = Object.FindFirstObjectByType<AeroFlow.Core.RuntimeModelLoader>();
            if (loader != null && modelScale != null)
            {
                modelScale.SetValueWithoutNotify(loader.GetModelScale());
                modelScale.RegisterValueChangedCallback(evt =>
                {
                    loader.SetModelScale(Mathf.Clamp(evt.newValue, 0.2f, 3.0f));
                    loader.RefreshModelPlacement();
                });
            }
        }

        private void BindPipeFlow(AeroFlow.Sim3D.PipeFlow.PipeFlowSimulation3D sim)
        {
            if (sim == null) return;

            var density = _root.Q<Slider>("bc-pipe-density-slider");
            var inletVel = _root.Q<Slider>("bc-pipe-inlet-velocity-slider");
            var turbulence = _root.Q<Slider>("bc-pipe-turbulence-slider");
            var flowAxis = _root.Q<DropdownField>("bc-pipe-flow-axis-dropdown");

            SliderInputBinder.BindFloat(density, sim.settings.fluidDensity, 3, v => sim.settings.fluidDensity = Mathf.Clamp(v, 0.5f, 1500f));
            SliderInputBinder.BindFloat(inletVel, sim.settings.inletVelocity, 2, v => sim.settings.inletVelocity = v);
            SliderInputBinder.BindFloat(turbulence, sim.settings.turbulenceIntensity, 1, v => sim.settings.turbulenceIntensity = Mathf.Clamp(v, 0f, 100f));

            if (flowAxis != null)
            {
                flowAxis.SetValueWithoutNotify(WindTunnelSimulation3D.GetFlowAxisLabel(sim.flowAxis));
                flowAxis.RegisterValueChangedCallback(evt =>
                {
                    sim.flowAxis = WindTunnelSimulation3D.ParseFlowAxisLabel(evt.newValue);
                });
            }
        }

        private void BindDamBreak(Simulation3D sim)
        {
            if (sim == null) return;

            var sizeX = _root.Q<Slider>("bc-dam-size-x-slider");
            var sizeY = _root.Q<Slider>("bc-dam-size-y-slider");
            var sizeZ = _root.Q<Slider>("bc-dam-size-z-slider");
            var loader = Object.FindFirstObjectByType<AeroFlow.Core.RuntimeModelLoader>();

            Vector3 currentSize = sim.transform.localScale;

            if (sizeX != null)
            {
                sizeX.SetValueWithoutNotify(currentSize.x);
                sizeX.RegisterValueChangedCallback(evt =>
                {
                    currentSize.x = Mathf.Clamp(evt.newValue, 1f, 20f);
                    sim.transform.localScale = currentSize;
                    loader?.AlignToSimulationContext();
                    sim.ApplyAndReset();
                });
            }

            if (sizeY != null)
            {
                sizeY.SetValueWithoutNotify(currentSize.y);
                sizeY.RegisterValueChangedCallback(evt =>
                {
                    currentSize.y = Mathf.Clamp(evt.newValue, 1f, 20f);
                    sim.transform.localScale = currentSize;
                    loader?.AlignToSimulationContext();
                    sim.ApplyAndReset();
                });
            }

            if (sizeZ != null)
            {
                sizeZ.SetValueWithoutNotify(currentSize.z);
                sizeZ.RegisterValueChangedCallback(evt =>
                {
                    currentSize.z = Mathf.Clamp(evt.newValue, 1f, 20f);
                    sim.transform.localScale = currentSize;
                    loader?.AlignToSimulationContext();
                    sim.ApplyAndReset();
                });
            }
        }

        private void BindWindTunnel(WindTunnelSimulation3D sim)
        {
            if (sim == null) return;

            var density = _root.Q<Slider>("bc-air-density-slider");
            var inletVel = _root.Q<Slider>("bc-inlet-velocity-slider");
            var angle = _root.Q<Slider>("bc-angle-of-attack-slider");
            var turbulence = _root.Q<Slider>("bc-turbulence-slider");
            var useCustomDir = _root.Q<Toggle>("bc-custom-direction-toggle");
            var inletSource = _root.Q<DropdownField>("bc-inlet-source-dropdown");
            var flowAxis = _root.Q<DropdownField>("bc-flow-axis-dropdown");
            var dirX = _root.Q<Slider>("bc-dir-x-slider");
            var dirY = _root.Q<Slider>("bc-dir-y-slider");
            var dirZ = _root.Q<Slider>("bc-dir-z-slider");
            var outletPressure = _root.Q<FloatField>("bc-outlet-pressure-field");
            var movingGround = _root.Q<Toggle>("bc-moving-ground-toggle");
            var groundSpeedScale = _root.Q<Slider>("bc-ground-speed-scale-slider");
            var wheelRotation = _root.Q<Toggle>("bc-wheel-rotation-toggle");
            var flipDirection = _root.Q<Toggle>("bc-flip-direction-toggle");
            var inletPanelWidth = _root.Q<Slider>("bc-inlet-panel-width-slider");
            var inletPanelHeight = _root.Q<Slider>("bc-inlet-panel-height-slider");
            var outletPanelWidth = _root.Q<Slider>("bc-outlet-panel-width-slider");
            var outletPanelHeight = _root.Q<Slider>("bc-outlet-panel-height-slider");
            var floorLengthScale = _root.Q<Slider>("bc-floor-length-scale-slider");
            var enclosure = sim.GetComponent<WindTunnelEnclosure>() ?? Object.FindFirstObjectByType<WindTunnelEnclosure>();
            var loader = Object.FindFirstObjectByType<AeroFlow.Core.RuntimeModelLoader>();

            if (sim.settings.vehicle == null)
            {
                sim.settings.vehicle = new WindTunnelVehicleProperties();
            }

            if (useCustomDir != null) useCustomDir.value = sim.settings.useCustomInletDirection;
            SliderInputBinder.BindFloat(density, sim.settings.airDensity, 3, v => sim.settings.airDensity = Mathf.Clamp(v, 0.5f, 1500f));
            SliderInputBinder.BindFloat(inletVel, sim.settings.inletVelocity, 2, v => sim.settings.inletVelocity = v);
            SliderInputBinder.BindFloat(angle, sim.settings.angleOfAttack, 2, v => sim.settings.angleOfAttack = v);
            SliderInputBinder.BindFloat(turbulence, sim.settings.turbulenceIntensity, 2, v => sim.settings.turbulenceIntensity = Mathf.Clamp(v, 0f, 100f));
            SliderInputBinder.BindFloat(dirX, sim.settings.inletDirection.x, 3, v => sim.settings.inletDirection.x = v);
            SliderInputBinder.BindFloat(dirY, sim.settings.inletDirection.y, 3, v => sim.settings.inletDirection.y = v);
            SliderInputBinder.BindFloat(dirZ, sim.settings.inletDirection.z, 3, v => sim.settings.inletDirection.z = v);

            if (inletSource == null)
            {
                inletSource = new DropdownField("Wind Source Face")
                {
                    name = "bc-inlet-source-dropdown",
                    choices = new System.Collections.Generic.List<string> { "Auto", "Left", "Right", "Front", "Back", "Top", "Bottom" }
                };
                if (useCustomDir != null && useCustomDir.parent != null)
                {
                    int idx = useCustomDir.parent.IndexOf(useCustomDir);
                    useCustomDir.parent.Insert(Mathf.Min(idx + 1, useCustomDir.parent.childCount), inletSource);
                }
            }
            if (inletSource != null)
            {
                if (!inletSource.choices.Contains(sim.settings.inletSource)) sim.settings.inletSource = "Auto";
                inletSource.SetValueWithoutNotify(sim.settings.inletSource);
                inletSource.RegisterValueChangedCallback(evt =>
                {
                    sim.settings.inletSource = evt.newValue;
                    sim.ResetSimulation();
                });
            }

            if (flowAxis != null)
            {
                flowAxis.SetValueWithoutNotify(WindTunnelSimulation3D.GetFlowAxisLabel(sim.flowAxis));
                flowAxis.RegisterValueChangedCallback(evt =>
                {
                    sim.flowAxis = WindTunnelSimulation3D.ParseFlowAxisLabel(evt.newValue);
                    sim.ResetSimulation();
                });
            }

            void SetDirEnabled(bool enabled)
            {
                dirX?.SetEnabled(enabled);
                dirY?.SetEnabled(enabled);
                dirZ?.SetEnabled(enabled);
            }

            if (useCustomDir != null)
            {
                SetDirEnabled(useCustomDir.value);
                useCustomDir.RegisterValueChangedCallback(evt =>
                {
                    sim.settings.useCustomInletDirection = evt.newValue;
                    SetDirEnabled(evt.newValue);
                });
            }

            if (outletPressure != null)
            {
                outletPressure.SetValueWithoutNotify(sim.settings.outletStaticPressurePa);
                outletPressure.RegisterValueChangedCallback(evt => sim.settings.outletStaticPressurePa = Mathf.Clamp(evt.newValue, 50000f, 150000f));
            }

            if (movingGround != null)
            {
                movingGround.SetValueWithoutNotify(sim.settings.vehicle.useMovingGround);
                movingGround.RegisterValueChangedCallback(evt =>
                {
                    sim.settings.vehicle.useMovingGround = evt.newValue;
                    sim.ResetSimulation();
                });
            }

            if (groundSpeedScale != null)
            {
                SliderInputBinder.BindFloat(groundSpeedScale, sim.settings.vehicle.groundSpeedScale, 2, v =>
                {
                    sim.settings.vehicle.groundSpeedScale = Mathf.Clamp(v, 0f, 1.5f);
                    sim.ResetSimulation();
                });
            }

            if (wheelRotation != null)
            {
                wheelRotation.SetValueWithoutNotify(sim.settings.vehicle.useWheelRotationProxies);
                wheelRotation.RegisterValueChangedCallback(evt =>
                {
                    sim.settings.vehicle.useWheelRotationProxies = evt.newValue;
                    sim.ResetSimulation();
                });
            }

            if (flipDirection != null)
            {
                flipDirection.SetValueWithoutNotify(sim.settings.flipVehicleDirection);
                flipDirection.RegisterValueChangedCallback(evt =>
                {
                    sim.settings.flipVehicleDirection = evt.newValue;
                    loader?.AlignToSimulationContext();
                    sim.ResetSimulation();
                });
            }

            if (enclosure != null)
            {
                SliderInputBinder.BindFloat(inletPanelWidth, enclosure.inletPanelWidthScale, 2, v =>
                {
                    enclosure.inletPanelWidthScale = Mathf.Clamp(v, 0.2f, 1.2f);
                    enclosure.MarkDirty();
                });
                SliderInputBinder.BindFloat(inletPanelHeight, enclosure.inletPanelHeightScale, 2, v =>
                {
                    enclosure.inletPanelHeightScale = Mathf.Clamp(v, 0.2f, 1.2f);
                    enclosure.MarkDirty();
                });
                SliderInputBinder.BindFloat(outletPanelWidth, enclosure.outletPanelWidthScale, 2, v =>
                {
                    enclosure.outletPanelWidthScale = Mathf.Clamp(v, 0.2f, 1.2f);
                    enclosure.MarkDirty();
                });
                SliderInputBinder.BindFloat(outletPanelHeight, enclosure.outletPanelHeightScale, 2, v =>
                {
                    enclosure.outletPanelHeightScale = Mathf.Clamp(v, 0.2f, 1.2f);
                    enclosure.MarkDirty();
                });
                SliderInputBinder.BindFloat(floorLengthScale, enclosure.floorLengthScale, 2, v =>
                {
                    enclosure.floorLengthScale = Mathf.Clamp(v, 0.4f, 1.2f);
                    enclosure.MarkDirty();
                });
            }
        }
    }
}
