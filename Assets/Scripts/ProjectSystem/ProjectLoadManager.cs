using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace FluidWorks.ProjectSystem
{
    public class ProjectLoadManager : MonoBehaviour
    {
        // Provide references your actual Simulation Loaders, NavierStokes Grid Solvers here
        // public Sim3D.RuntimeModelLoader modelImporter;
        // public Sim3D.NavierStokesGridSolver solver;

        /// <summary>
        /// Loads a .fluidworks project and attempts to restore all aspects of the simulation context.
        /// </summary>
        public void LoadProject(string archivePath)
        {
            if (!File.Exists(archivePath))
            {
                Debug.LogError($"[LoadManager] Project file not found: {archivePath}");
                return;
            }

            string tempDirName = "Extract_" + Guid.NewGuid().ToString();
            string tempDir = Path.Combine(Application.temporaryCachePath, tempDirName);
            
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
            Directory.CreateDirectory(tempDir);

            try
            {
                Debug.Log($"[LoadManager] Extracting {archivePath} to {tempDir}...");
                
                // 1. Extract ZIP
                ZipFile.ExtractToDirectory(archivePath, tempDir);

                // 2. Read JSON
                string jsonPath = Path.Combine(tempDir, "project.json");
                if (!File.Exists(jsonPath))
                {
                    Debug.LogError("[LoadManager] Invalid project format: project.json missing from the .fluidworks package.");
                    return;
                }

                string jsonContent = File.ReadAllText(jsonPath);
                ProjectData projectData = JsonUtility.FromJson<ProjectData>(jsonContent);

                // 3. Restore all systems
                RestoreSimulationState(projectData, tempDir);

                Debug.Log($"[LoadManager] Successfully restored project: {projectData.metadata.projectName}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LoadManager] Critical error loading project: {ex.Message}");
            }
            finally
            {
                // We leave the tempDir for the session in case runtime loaders need the OBJ file 
                // Alternatively, we move the relevant contents prior to deletion.
                // Depending on memory lifecycle, clear it manually or let Unity clean on application exit.
                // Directory.Delete(tempDir, true); 
            }
        }

        private void RestoreSimulationState(ProjectData state, string currentExtractDir)
        {
            // --- Restore Camera Position & Settings ---
            if (Camera.main != null)
            {
                Camera.main.transform.position = state.visualization.cameraPosition;
                Camera.main.transform.rotation = state.visualization.cameraRotation;
                Debug.Log($"[LoadManager] Restored Camera to {state.visualization.cameraPosition}");
            }

            // --- Restore Geometry ---
            if (!string.IsNullOrEmpty(state.geometry.modelPath))
            {
                string trueModelPath = Path.Combine(currentExtractDir, state.geometry.modelPath);
                if (File.Exists(trueModelPath))
                {
                    Debug.Log($"[LoadManager] Importing Geometry from: {trueModelPath}");
                    var loader = FindAnyObjectByType<AeroFlow.Core.RuntimeModelLoader>();
                    if (loader != null)
                    {
                        loader.LoadModel(trueModelPath);
                    }
                }
            }

            // --- Restore Solver Parameters ---
            Debug.Log($"[LoadManager] Restoring Grid settings: {state.solver.gridSizeX}x{state.solver.gridSizeY}x{state.solver.gridSizeZ}");
            // solver.viscosity = state.solver.viscosity;
            // solver.density = state.solver.density;
            // solver.timeStep = state.solver.timeStep;

            // --- Restore Diagnostics ---
            // Example: Force UI elements to update with loaded state.diagnostics.maxVelocity
            Debug.Log($"[LoadManager] Diagnostic load: MeanVelocity = {state.diagnostics.meanVelocity}");
            
            // --- Restore Visualization state ---
            // Example: Restoring the slice plane
            // slicePlaneController.SetPosition(state.visualization.slicePlanePosition);
        }
    }
}
