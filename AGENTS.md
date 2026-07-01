# AeroFlow Agent Notes

This repository is a working Unity desktop CFD-style visualization tool, not a blank starter project. Treat the current wind-tunnel stack as the primary external-aero path.

## Wind Tunnel Baseline

- `Assets/Scripts/Sim3D/WindTunnel/WindTunnelSimulation3D.cs` is the runtime driver.
- `Assets/Scripts/Physics/WindTunnel/NavierStokesGridSolver.cs` and `Assets/Resources/Compute/WindTunnel/NavierStokes3D.compute` are the active solver path.
- `Assets/Scripts/Visualization/WindTunnel/StreamlineFieldRenderer.cs`, `Assets/Scripts/Visualization/WindTunnel/FlowFieldSliceRenderer.cs`, and `Assets/Scripts/Visualization/WindTunnel/FlowParticleSystem.cs` are the current visualization outputs.
- Obstacle coupling depends on both `Assets/Scripts/Physics/WindTunnel/ObstacleVoxelizer.cs` and `Assets/Scripts/Physics/WindTunnel/NavierStokesGridSolver.cs`.

## Current Expectations

- Keep tunnel flow axis explicit through `WindTunnelSimulation3D.flowAxis`. Do not reintroduce longest-axis heuristics.
- Keep `useRawSolverDataOnly` as a debug toggle. It should stay off by default for normal use.
- Keep vehicle-aware estimate inputs under `WindTunnelSettings.vehicle`. These inputs affect derived forces/loads/performance estimates only; they do not change the solver field.
- Use `WindTunnelSimulation3D.TryGetVehicleReferenceFrame` as the shared source for CG, axle, wheel, and ground-reference data. Do not duplicate that logic in new systems.
- Preserve the current mode contract:
  - `Effects`: readable stylized wake around a loaded vehicle.
  - `Velocity`: compact solver-driven slices and focused streamlines.
  - `Pressure`: model pressure view plus a body-adjacent slice, not a full-screen slab.
- `Surface Pressure` is an additive body-only contour mode. Keep it separate from `Pressure`, which should continue to include the body-adjacent slice view.
- Keep `WindTunnelSettings.flipVehicleDirection` exposed as the default-on import-orientation toggle. It should rotate the loaded vehicle to face the inlet without changing solver flow direction logic.
- Solver diagnostics should remain available in the wind-tunnel properties panel, including divergence and mean vorticity.
- Vehicle-aware outputs should remain separated from solver diagnostics:
  - `Drag Force`, `Vertical Aero Force`, `Downforce`, `CoP`, `Pitch/Yaw/Roll Moment`, `Axle Loads`, `Estimated Top Speed`
  - `Mean Velocity`, `Max Velocity`, `Pressure Drop`, `Wall Shear`, `Divergence Error`, `Mean Vorticity`
- Keep moving-ground and wheel-rotation behavior in the solver as boundary proxies, not as rigid-body wheel physics.

## Streamline Rendering Quality

- `StreamlineFieldRenderer` uses solver data as the **primary** velocity source (82% weight in Streamlines mode, 55% in Effects mode). The analytical ellipsoid model is only a coarse fallback; do not regress the solver weight below 50%.
- Seed positions use a **stable RNG** (frame-independent) to avoid per-frame flickering. Do not reseed the RNG with `Time.frameCount`.
- Default quality settings: `stepLength=0.028`, `pointsPerLine=220`, `lineWidth=0.038`, `flowSpeed=1.5`, `seedJitter=0.06`.
- Lines must be long enough to traverse a meaningful portion of the tunnel (up to 82% at moderate velocities). Do not cap `maxTravel` below `tunnelLength * 0.50f`.
- Speed kill threshold is set to 0.5% of inlet velocity to preserve lines through wake regions. Do not raise above 2%.
- Minimum forward velocity clamp in `ComputeAnalyticalFlow` is 6% of freestream. Higher values (>15%) cause lines to look unnaturally straight in the wake.
- The velocity gradient uses a 6-stop CFD-style palette (deep blue → cyan → green → yellow → orange → red). Maintain vivid saturation.
- Line end alpha should be at least 0.20 for visible tail tapering. Alpha below 0.10 makes lines appear to stop abruptly.

## When Editing

- If you change wind-tunnel controls or result labels, update both:
  - `Assets/Resources/UI/UXML/WindTunnel/WindTunnelProperties.uxml`
  - `Assets/Scripts/UI/WindTunnel/WindTunnelPropertiesController.cs`
- If you change vehicle-aware result labels or exports, also update both:
  - `Assets/Resources/UI/UXML/AnalysisProperties.uxml`
  - `Assets/Scripts/UI/MainScreenController.cs`
- If you change stance, wheel-proxy, or rolling-road behavior, review all of:
  - `Assets/Scripts/Sim3D/WindTunnel/WindTunnelSimulation3D.cs`
  - `Assets/Scripts/Core/RuntimeModelLoader.cs`
  - `Assets/Scripts/Physics/WindTunnel/NavierStokesGridSolver.cs`
  - `Assets/Resources/Compute/WindTunnel/NavierStokes3D.compute`
- If you change solver behavior, review:
  - `Docs/WindTunnel-QA-Checklist.md`
  - `Assets/Scenes/Wind Tunnel (3D).unity`
- If you change runtime model placement or obstacle masks, verify the path from:
  - `Assets/Scripts/Core/RuntimeModelLoader.cs`
  - `Assets/Scripts/Physics/WindTunnel/ObstacleVoxelizer.cs`
  - `Assets/Scripts/Physics/WindTunnel/NavierStokesGridSolver.cs`

## Validation

- Run a Unity batch compile check after non-trivial wind-tunnel changes.
- Run the Windows batch build when the compile check passes.
- Use `Docs/WindTunnel-QA-Checklist.md` as the smoke-test source of truth.

## Maintenance

- Update this file when the wind-tunnel workflow, controls, diagnostics, or validation expectations change.
- Keep `context/AGENTS.md` aligned with this file.
