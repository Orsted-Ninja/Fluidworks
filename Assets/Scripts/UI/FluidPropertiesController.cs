using UnityEngine;
using UnityEngine.UIElements;
using AeroFlow.Visualization;
using AeroFlow.Sim3D.PipeFlow;
using AeroFlow.Sim3D.RotatingMachinery;

namespace AeroFlow.UI
{
    public class FluidPropertiesController
    {
        private readonly VisualElement _root;

        public FluidPropertiesController(VisualElement root)
        {
            _root = root;
            BindControls();
        }

        private void BindControls()
        {
            var windSim = Object.FindFirstObjectByType<WindTunnelSimulation3D>();
            var pipeSim = Object.FindFirstObjectByType<PipeFlowSimulation3D>();
            var machinerySim = Object.FindFirstObjectByType<RotatingMachinerySimulation3D>();

            // Find controls in FluidProperties.uxml
            var density = _root.Q<Slider>("fluid-density-slider");
            var viscosity = _root.Q<Slider>("fluid-viscosity-slider");
            var turbulenceActive = _root.Q<Toggle>("fluid-turbulence-active-toggle");
            var solverModel = _root.Q<DropdownField>("fluid-model-dropdown");

            if (windSim != null && windSim.isActiveAndEnabled)
            {
                SliderInputBinder.BindFloat(density, windSim.settings.airDensity, 3, v => windSim.settings.airDensity = Mathf.Clamp(v, 0.5f, 1500f));
                SliderInputBinder.BindFloat(viscosity, windSim.settings.dynamicViscosity, 6, v => windSim.settings.dynamicViscosity = Mathf.Clamp(v, 0.000001f, 0.02f));
                if (turbulenceActive != null)
                {
                    turbulenceActive.SetValueWithoutNotify(true);
                    turbulenceActive.SetEnabled(false);
                }
                if (solverModel != null)
                {
                    solverModel.SetValueWithoutNotify("LES (Smagorinsky)");
                    solverModel.SetEnabled(false);
                }
            }
            else if (pipeSim != null && pipeSim.isActiveAndEnabled)
            {
                SliderInputBinder.BindFloat(density, pipeSim.settings.fluidDensity, 3, v => pipeSim.settings.fluidDensity = Mathf.Clamp(v, 0.5f, 1500f));
                SliderInputBinder.BindFloat(viscosity, pipeSim.settings.dynamicViscosity, 6, v => pipeSim.settings.dynamicViscosity = Mathf.Clamp(v, 0.000001f, 0.02f));
                if (turbulenceActive != null)
                {
                    turbulenceActive.SetValueWithoutNotify(pipeSim.settings.turbulenceIntensity > 0.01f);
                    turbulenceActive.RegisterValueChangedCallback(evt =>
                    {
                        pipeSim.settings.turbulenceIntensity = evt.newValue ? Mathf.Max(pipeSim.settings.turbulenceIntensity, 1f) : 0f;
                    });
                }
                if (solverModel != null)
                {
                    solverModel.SetValueWithoutNotify("Laminar");
                    solverModel.SetEnabled(false);
                }
            }
            else if (machinerySim != null && machinerySim.isActiveAndEnabled)
            {
                SliderInputBinder.BindFloat(density, machinerySim.settings.fluidDensity, 3, v => machinerySim.settings.fluidDensity = Mathf.Clamp(v, 0.5f, 1500f));
                SliderInputBinder.BindFloat(viscosity, machinerySim.settings.dynamicViscosity, 6, v => machinerySim.settings.dynamicViscosity = Mathf.Clamp(v, 0.000001f, 0.02f));
                if (turbulenceActive != null)
                {
                    turbulenceActive.SetValueWithoutNotify(machinerySim.settings.turbulenceIntensity > 0.01f);
                    turbulenceActive.RegisterValueChangedCallback(evt =>
                    {
                        machinerySim.settings.turbulenceIntensity = evt.newValue ? Mathf.Max(machinerySim.settings.turbulenceIntensity, 1f) : 0f;
                    });
                }
                if (solverModel != null)
                {
                    solverModel.SetValueWithoutNotify("LES (Smagorinsky)");
                    solverModel.SetEnabled(false);
                }
            }
            else
            {
                if (density != null) density.SetEnabled(false);
                if (viscosity != null) viscosity.SetEnabled(false);
                if (turbulenceActive != null) turbulenceActive.SetEnabled(false);
                if (solverModel != null) solverModel.SetEnabled(false);
            }
        }
    }
}
