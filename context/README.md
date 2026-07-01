# Project Overview

This is a Unity-based real-time CFD visualization tool focused on wind-tunnel and dam-break style simulations. The project favors interactive, visually plausible flow rather than full Navier-Stokes accuracy.

## Key Directories

- `Assets/Scripts/Sim3D/`
  - `Simulation3D.cs`: Dam-break style SPH particle simulation.
  - `Spawner3D.cs`: Particle spawning for the 3D simulation.
  - `WindTunnel/`: Wind tunnel simulation driver and scene-facing settings.

- `Assets/Scripts/Physics/`
  - Approximate physics utilities and grid diagnostics, including `NavierStokesGridSolver`.

- `Assets/Scripts/Visualization/`
  - Flow visualization helpers, including `StreamlineFieldRenderer`, particles, and surface pressure coloring.

- `Assets/Scripts/Managers/`
  - `SimulationManager`: central coordinator for parameters, metrics, and UI sync.
  - `VideoCaptureManager`: ffmpeg capture integration.

- `Assets/Scripts/UI/`
  - UI Toolkit controllers and input bindings (`MainScreenController`, panels, ribbon, camera).

- `Assets/Scripts/Core/`
  - Runtime model loading (`RuntimeModelLoader`), runtime mesh import (`RuntimeMeshImporter`), simulation proxy tagging, and drag controls. The loader now supports visible `GLB`/`GLTF`/`OBJ`/`STL` imports and can attach an optional hidden simulation mesh proxy.

- `Assets/Scripts/Display/`
  - Rendering helpers and visual bootstrapping.

- `Assets/Scripts/ComputeHelpers/`
  - Compute buffer helpers and GPU sorting utilities.

- `Assets/Resources/UI/`
  - UI Toolkit UXML/USS layout and styles.

- `Assets/Resources/Compute/`
  - Compute shaders for the wind tunnel grid solver.

- `Assets/Scenes/`
  - Main scenes (`SampleScene`, `WindTunnelSample`, `Wind Tunnel (3D)`, `Test C (3D)`).

## Notes

- Runtime model loading uses glTFast for glTF-family files plus an in-project runtime importer for `OBJ`/`STL`.
- The active wind tunnel path uses `WindTunnelSimulation3D` plus a layered engineering view:
  - `StreamlineFieldRenderer` for body-focused streamlines
  - `FlowFieldSliceRenderer` for transparent velocity/pressure cut planes
  - `SurfacePressureVisualizer` plus a lit vertex-color shader for model interaction
  - `FlowParticleSystem` only as an optional overlay, not the default view
- The ribbon quick controls use ASCII-safe transport buttons (`<<`, `>`, `>>`) and stay synchronized with the Simulation tab.
- Batch builds now run `ProjectValidation` before building and record validation results in `Builds/BatchBuild/build-summary.txt`.
