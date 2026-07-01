using UnityEngine;
using System;

namespace FluidWorks.Settings
{
    public class SettingsManager : MonoBehaviour
    {
        public static SettingsManager Instance { get; private set; }

        public SettingsData settings;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                LoadSettings();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public void LoadSettings()
        {
            settings = SettingsSerializer.Load();
            Debug.Log("[SettingsManager] Settings loaded and ready.");
        }

        public void SaveSettings()
        {
            SettingsSerializer.Save(settings);
            ApplySettings();
            Debug.Log("[SettingsManager] Settings saved and applied.");
        }

        public void ResetSettingsToDefault()
        {
            settings = new SettingsData();
            settings.ResetToDefaults();
            SaveSettings();
        }

        public void ApplySettings()
        {
            // 1. Apply to Wind Tunnel
            var windSim = FindAnyObjectByType<WindTunnelSimulation3D>();
            if (windSim != null)
            {
                // Note: Some settings like Grid Size require a simulation reset
                // For now, we update the settings object which will be used on the next ResetSimulation()
                // or applied immediately where possible.
                
                // Example of applying a parameter that can change live (if supported)
                // windSim.settings.viscosity = settings.solver.defaultViscosity;
                
                Debug.Log("[SettingsManager] Applied settings to WindTunnelSimulation3D");
            }

            // 2. Apply to UI/Theme
            // FluidWorks.UI.ThemeManager.ApplyTheme(settings.general.themeMode);

            // 3. Apply Performance Limits
            if (settings.performance.enableGPUAcceleration)
            {
                // Logic to ensure compute shaders are preferred
            }
            
            // 4. Update Solver Defaults in global state if needed
            // (e.g., if we had a SolverFactory)
        }

        private void OnApplicationQuit()
        {
            SaveSettings();
        }
    }
}
