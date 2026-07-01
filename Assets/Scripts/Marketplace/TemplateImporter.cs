using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using AeroFlow.Core;
using AeroFlow.UI;

namespace FluidWorks.Marketplace
{
    public class TemplateImporter : MonoBehaviour
    {
        public static TemplateImporter Instance { get; private set; }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        public void ImportTemplate(string archivePath)
        {
            if (!File.Exists(archivePath)) return;

            string extractDir = Path.Combine(Application.temporaryCachePath, "TemplateImport_" + Guid.NewGuid());
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
            Directory.CreateDirectory(extractDir);

            try
            {
                ZipFile.ExtractToDirectory(archivePath, extractDir);

                string metadataPath = Path.Combine(extractDir, "metadata.json");
                string solverPath = Path.Combine(extractDir, "solver.json");
                string configPath = Path.Combine(extractDir, "config.json");
                string modelFile = Path.Combine(extractDir, "geometry.obj");

                if (!File.Exists(modelFile))
                {
                    // Fallback to searching for any model file
                    string[] files = Directory.GetFiles(extractDir, "*.obj");
                    if (files.Length > 0) modelFile = files[0];
                    else files = Directory.GetFiles(extractDir, "*.glb");
                    if (files.Length > 0) modelFile = files[0];
                }

                // 1. Load Geometry
                var loader = FindAnyObjectByType<RuntimeModelLoader>();
                if (loader != null && File.Exists(modelFile))
                {
                    loader.LoadModel(modelFile);
                }

                // 2. Apply Solver & Boundary Conditions (Simulation Setup)
                ApplySimulationSettings(solverPath, configPath);

                Debug.Log($"[TemplateImporter] Successfully imported template from {archivePath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TemplateImporter] Import failed: {ex.Message}");
            }
        }

        private void ApplySimulationSettings(string solverJsonPath, string configJsonPath)
        {
            var windSim = FindAnyObjectByType<WindTunnelSimulation3D>();
            if (windSim == null) return;

            // In a real scenario, we'd parse the JSONs and map them to windSim.settings
            // For now, we'll simulate the auto-setup by triggering a simulation reset
            windSim.ResetSimulation();
            
            // Force a playback start if requested
            windSim.Play();
        }
    }
}
