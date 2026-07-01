using System;
using UnityEngine;

namespace FluidWorks.ProjectSystem
{
    [Serializable]
    public class ProjectMetadata
    {
        public string projectName;
        public string creationDate;
        public string softwareVersion;
    }

    [Serializable]
    public class GeometryData
    {
        public string modelPath;
        public Vector3 modelScale;
        public Vector3 modelRotation;
        public Vector3 boundingBoxSize;
        public int triangleCount;
    }

    [Serializable]
    public class SolverParameters
    {
        public string solverType;
        public int gridSizeX;
        public int gridSizeY;
        public int gridSizeZ;
        public float viscosity;
        public float density;
        public float timeStep;
        public int iterationCount;
    }

    [Serializable]
    public class SimulationDiagnostics
    {
        public float meanVelocity;
        public float maxVelocity;
        public float pressureDrop;
        public float wallShear;
        public float vorticity;
        public float divergenceError;
    }

    [Serializable]
    public class VisualizationState
    {
        public float slicePlanePosition;
        public float streamlineDensity;
        public int selectedColormap; 
        public Vector3 cameraPosition;
        public Quaternion cameraRotation;
        public float cameraZoom;
    }

    /// <summary>
    /// Master serializable container for all FluidWorks project state
    /// </summary>
    [Serializable]
    public class ProjectData
    {
        public ProjectMetadata metadata;
        public GeometryData geometry;
        public SolverParameters solver;
        public SimulationDiagnostics diagnostics;
        public VisualizationState visualization;

        public ProjectData()
        {
            metadata = new ProjectMetadata();
            geometry = new GeometryData();
            solver = new SolverParameters();
            diagnostics = new SimulationDiagnostics();
            visualization = new VisualizationState();
        }
    }
}
