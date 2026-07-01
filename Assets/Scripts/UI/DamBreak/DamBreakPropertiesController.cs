using UnityEngine;
using UnityEngine.UIElements;

namespace AeroFlow.UI
{
    public class DamBreakPropertiesController
    {
        private VisualElement _root;
        private VisualElement _settingsTab;
        private VisualElement _modelTab;
        private Button _settingsTabBtn;
        private Button _modelTabBtn;

        public DamBreakPropertiesController(VisualElement root)
        {
            _root = root;
            BindControls();
        }

        private void BindControls()
        {
            var sim = UnityEngine.Object.FindFirstObjectByType<Simulation3D>();
            if (sim == null)
            {
                Debug.LogWarning("[DamBreakProperties] Could not find Simulation3D in scene.");
                return;
            }

            var loader = UnityEngine.Object.FindFirstObjectByType<AeroFlow.Core.RuntimeModelLoader>();

            BindTabLogic();
            BindSettings(sim, loader);
            BindVehicleProperties(sim, loader);
        }

        private void BindTabLogic()
        {
            _settingsTab = _root.Q<VisualElement>("dam-break-tab-settings");
            _modelTab = _root.Q<VisualElement>("dam-break-tab-model");
            _settingsTabBtn = _root.Q<Button>("dam-break-btn-settings");
            _modelTabBtn = _root.Q<Button>("dam-break-btn-model");

            if (_settingsTabBtn != null) _settingsTabBtn.clicked += () => ShowTab(true);
            if (_modelTabBtn != null) _modelTabBtn.clicked += () => ShowTab(false);

            ShowTab(true);
        }

        private void ShowTab(bool showSettings)
        {
            if (_settingsTab != null) _settingsTab.style.display = showSettings ? DisplayStyle.Flex : DisplayStyle.None;
            if (_modelTab != null) _modelTab.style.display = showSettings ? DisplayStyle.None : DisplayStyle.Flex;

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

        private void BindSettings(Simulation3D sim, AeroFlow.Core.RuntimeModelLoader loader)
        {

            var pCount = _root.Q<SliderInt>("particle-count-slider");
            var wFill = _root.Q<Slider>("water-fill-slider");
            var btnReset = _root.Q<Button>("apply-reset-btn");

            var density = _root.Q<Slider>("density-slider");
            var visc = _root.Q<Slider>("viscosity-slider");
            var transparency = _root.Q<Slider>("particle-transparency-slider");
            var grav = _root.Q<Slider>("gravity-slider");

            if (transparency == null) Debug.LogWarning("[DamBreakProperties] Could not find 'particle-transparency-slider' in UXML.");

            var timeScale = _root.Q<Slider>("timescale-slider");
            var iters = _root.Q<SliderInt>("iterations-slider");
            var maxVel = _root.Q<Slider>("max-velocity-slider");

            // --- Numeric display + keyboard input for precision ---
            SliderInputBinder.BindInt(pCount, sim.settings.particleCount, v => sim.settings.particleCount = v);
            SliderInputBinder.BindFloat(wFill, sim.settings.waterFillRatio, 3, v => sim.settings.waterFillRatio = v);
            SliderInputBinder.BindFloat(density, sim.settings.density, 2, v => sim.settings.density = v);
            SliderInputBinder.BindFloat(visc, sim.settings.viscosity, 6, v => sim.settings.viscosity = v);
            
            if (transparency != null && sim.display != null)
            {
                SliderInputBinder.BindFloat(transparency, sim.display.particleAlpha, 2, v => sim.display.SetAlpha(v));
            }

            SliderInputBinder.BindFloat(grav, sim.settings.gravity, 2, v => sim.settings.gravity = v);
            SliderInputBinder.BindFloat(timeScale, sim.settings.timeScale, 3, v => sim.settings.timeScale = v);
            SliderInputBinder.BindInt(iters, sim.settings.iterationsPerFrame, v => sim.settings.iterationsPerFrame = v);
            SliderInputBinder.BindFloat(maxVel, sim.settings.maxVelocity, 2, v => sim.settings.maxVelocity = v);

            // Apply logic natively destroys and regenerates compute buffers
            if (btnReset != null)
            {
                btnReset.clicked += () => 
                {
                    if (loader != null) loader.ResetModelToBase(restoreRenderMode: true);
                    sim.ApplyAndReset();
                    sim.Pause();
                };
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

        private void BindVehicleProperties(Simulation3D sim, AeroFlow.Core.RuntimeModelLoader loader)
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
                sim.ApplyAndReset();
            }

            void RefreshSolverOnly()
            {
                sim.ApplyAndReset();
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

            if (modelScale != null)
            {
                float initialScale = loader != null ? loader.GetModelScale() : 1f;
                modelScale.SetValueWithoutNotify(initialScale);
                modelScale.RegisterValueChangedCallback(evt =>
                {
                    if (loader == null) return;
                    loader.SetModelScale(Mathf.Clamp(evt.newValue, 0.10f, 4.00f));
                    loader.RefreshModelPlacement(resetPhysics: false);
                });
            }

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
                        loader.RefreshModelPlacement(resetPhysics: false);
                    });
                }

                if (offsetY != null)
                {
                    offsetY.SetValueWithoutNotify(currentOffset.y);
                    offsetY.RegisterValueChangedCallback(evt =>
                    {
                        currentOffset.y = Mathf.Clamp(evt.newValue, -5f, 5f);
                        loader.SetModelOffset(currentOffset);
                        loader.RefreshModelPlacement(resetPhysics: false);
                    });
                }

                if (offsetZ != null)
                {
                    offsetZ.SetValueWithoutNotify(currentOffset.z);
                    offsetZ.RegisterValueChangedCallback(evt =>
                    {
                        currentOffset.z = Mathf.Clamp(evt.newValue, -5f, 5f);
                        loader.SetModelOffset(currentOffset);
                        loader.RefreshModelPlacement(resetPhysics: false);
                    });
                }
            }

            var loadBtn = _root.Q<Button>("load-model-button");
            if (loader != null && loadBtn != null)
            {
                loadBtn.clicked += loader.OpenFilePicker;
            }

            var resetBtn = _root.Q<Button>("reset-sim-button");
            if (resetBtn != null)
            {
                resetBtn.clicked += () => sim.ApplyAndReset();
            }
        }
    }
}
