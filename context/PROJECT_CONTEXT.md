# AeroFlow CFD Visualization Project Context

## Overview
AeroFlow is a Unity-based CFD visualization suite built around a lightweight GPU grid solver, approximate aero loads, and UI Toolkit desktop workflows. The product goal is an engineering-style external aero and free-surface exploration tool with strong visual feedback, not a full solver-validation environment.

**Working Directory:** `C:/Users/Beena C S/Downloads/Aeroflow (1)/Aeroflow`

## Recent Agentic Fixes
1. **Wind tunnel axis alignment:** The compute shader and particle recycle path now respect the actual tunnel flow axis instead of assuming the inlet is always on `+X`. This fixes `WindTunnelSample` and any other tunnel whose long axis is on `Z`.
2. **Self-bootstrapped streamline renderer:** `WindTunnelSimulation3D` now creates and wires the high-quality `StreamlineFieldRenderer` itself, then disables the legacy renderer. The tunnel no longer depends on `SimulationManager` to get streamlines.
3. **Model fit correction:** `RuntimeModelLoader.AlignToSimulationContext()` now scales relative to the imported model's current normalized size instead of overwriting it. Imported cars stop appearing absurdly tiny or oversized in the tunnel.
4. **Overlay ownership fix:** `SimulationManager` no longer mutates `ParticleSystem` modules every frame. `FlowParticleSystem` configures and owns its own particle system, which removes the recurring `NullReferenceException` / module-instancing error path.
5. **Engineering-style default visualization:** The wind tunnel no longer defaults to an always-on particle fog. Workflow templates now start with `showInstancedParticles = false`, pressure mode uses the model surface plus contour-style slices, and velocity mode combines focused streamlines with a wake plane.
6. **Flow slice renderer:** `FlowFieldSliceRenderer` now samples the solver onto transparent planes. Velocity mode shows a longitudinal slice near the body plus a downstream wake cut plane. Pressure mode shows a pressure-coefficient-style slice with the model footprint punched out for readability.
7. **Model-aware flow visuals:** `StreamlineFieldRenderer` now biases around the loaded body, uses body/wake analytical fallback when solver samples are sparse, and keeps the flow readable around the model instead of drawing a generic tunnel wash.
8. **Pressure shading upgrade:** `VisualsBootstrapper` now prefers a lit vertex-color pressure shader so model interaction reads like a shaded engineering contour instead of a flat debug overlay.
9. **Runtime CAD-style import restored:** `RuntimeModelLoader` is no longer GLB-only. Visual runtime import now supports `GLB`, `GLTF`, `OBJ`, and `STL`, and the dual-asset workflow can attach an optional hidden simulation proxy mesh (`OBJ`/`STL`) or store a CAD reference path (`STEP`/`IGES` family).
10. **Wind tunnel UI cleanup:** The wind tunnel properties panel and ribbon were cleaned up, slider ranges now match the simulation ranges, and corrupted text encoding was removed from the active UI assets.
11. **Build-time validation:** `Assets/Editor/ProjectValidation.cs` now checks scenes, required assets, streamline defaults, and UI text encoding before batch builds. `BatchBuild` writes validation results into `build-summary.txt`.
12. **Streamline rendering quality overhaul:** `StreamlineFieldRenderer` now uses solver data as the primary velocity source (82% weight, up from 35%) instead of the analytical ellipsoid fallback. Seed positions use a stable RNG for temporal coherence (no per-frame jitter). Lines are longer (`stepLength=0.028`, `maxTravel` up to 82% tunnel length), thicker (`lineWidth=0.038`), with visible tail tapering (end alpha 0.22), smoother curves (6 corner, 3 cap vertices), and a vivid 6-stop CFD-style color gradient. Wake lines survive through low-speed recirculation zones (0.5% speed threshold). The analytical flow minimum forward clamp was lowered to 6% for realistic wake curvature.

## Architecture Breakdown

### UI System
- `Assets/Resources/UI/UXML/` and `Assets/Resources/UI/USS/`: Layout and styling for the desktop UI. `Ribbon.uxml` owns the workflow tabs and quick transport controls. `WindTunnelProperties.uxml` owns the right-side tunnel controls.
- `Assets/Scripts/UI/RibbonController.cs`: Ribbon event hub plus playback state synchronization for the main and quick transport controls.
- `Assets/Scripts/UI/MainScreenController.cs`: Loads additive simulation scenes, binds the right-side property panels, coordinates visualization mode changes, and keeps the ribbon playback state in sync.
- `Assets/Scripts/UI/PropertiesPanelController.cs`, `ProjectTreeController.cs`, `BoundaryConditionsController.cs`: Right-panel view switching and tree selection logic.

### Cameras and Display
- `Assets/Scripts/UI/CameraController.cs`: Orbit/fly camera with framing and standard engineering views.
- `Assets/Scripts/Display/VisualsBootstrapper.cs`: Global display look, lighting, and model render-mode overrides.
- `Assets/Scripts/Visualization/WindTunnel/WindTunnelEnclosure.cs`: Procedural wind tunnel frame, glass, and floor-guide presentation.

### Physics and Computation
- `Assets/Scripts/Sim3D/WindTunnel/WindTunnelSimulation3D.cs`: Main wind tunnel driver. Owns tunnel bounds, inlet direction resolution, solver initialization, and streamline renderer setup.
- `Assets/Scripts/Physics/WindTunnel/NavierStokesGridSolver.cs`: GPU grid solver wrapper. Produces diagnostics and velocity-field snapshots for UI and visualization.
- `Assets/Resources/Compute/WindTunnel/NavierStokes3D.compute`: Wind tunnel kernels. `ApplyForces`, inlet forcing, turbulence basis, and particle recycling now work for any dominant flow axis.
- `Assets/Scripts/Visualization/WindTunnel/StreamlineFieldRenderer.cs`: High-quality RK2 streamline renderer used by the active wind tunnel.
- `Assets/Scripts/Visualization/WindTunnel/FlowFieldSliceRenderer.cs`: Transparent slice-plane renderer that samples solver velocity/pressure directly into engineering-style result planes.
- `Assets/Scripts/Visualization/WindTunnel/FlowParticleSystem.cs`: Optional particle overlay. Now self-configures safely and follows the solver/body interaction rather than acting like a generic smoke box. This is treated as a secondary/debug overlay, not the default presentation.

### Runtime Geometry
- `Assets/Scripts/Core/RuntimeModelLoader.cs`: Runtime visual-model and simulation-proxy loader. Supports `GLB`, `GLTF`, `OBJ`, and `STL` for visible geometry.
- `Assets/Scripts/Core/RuntimeMeshImporter.cs`: Lightweight runtime importer for `OBJ` and `STL`.
- `Assets/Scripts/Core/RuntimeSimulationProxy.cs`: Marker component for hidden solver-only geometry attached under the loaded model.

## Verification Notes
- `WindTunnelSample.unity` and `Wind Tunnel (3D).unity` now default to `Streamlines` with a density of `180`.
- `BatchBuild.BuildWindows64` fails early if required UI assets are missing, if UI files contain corrupted encoding markers, or if wind-tunnel scene defaults fall outside supported ranges.
- New agents should prefer the layered visualization path:
  - `Pressure`: surface contour + `FlowFieldSliceRenderer`
  - `Velocity`: focused `StreamlineFieldRenderer` + wake slice plane
  - particles: optional only
- Treat `WindTunnelStreamlineRenderer` as legacy compatibility only.
- Latest verified batch build: `Builds/BatchBuild/build-summary.txt` updated on `2026-03-07 10:09:07` with `Result: Succeeded`, `Errors: 0`, and only the expected Unity Cloud symbol-upload warning.
