# AeroFlow Agent Notes

Mirror of the root `AGENTS.md` for project-context tooling.

## Wind Tunnel Baseline

- `Assets/Scripts/Sim3D/WindTunnel/WindTunnelSimulation3D.cs` is the runtime driver.
- `Assets/Scripts/Physics/WindTunnel/NavierStokesGridSolver.cs` and `Assets/Resources/Compute/WindTunnel/NavierStokes3D.compute` are the active solver path.
- `Assets/Scripts/Visualization/WindTunnel/StreamlineFieldRenderer.cs`, `Assets/Scripts/Visualization/WindTunnel/FlowFieldSliceRenderer.cs`, and `Assets/Scripts/Visualization/WindTunnel/FlowParticleSystem.cs` are the active visuals.
- `Assets/Scripts/Physics/WindTunnel/ObstacleVoxelizer.cs` owns the conservative occupancy mask used by the solver.

## Current Expectations

- Keep tunnel flow axis explicit through `WindTunnelSimulation3D.flowAxis`.
- Keep `useRawSolverDataOnly` as a debug-only toggle and leave it disabled by default.
- Keep vehicle-aware estimate inputs under `WindTunnelSettings.vehicle`. These inputs affect derived forces/loads/performance estimates only; they do not change the solver field.
- Use `WindTunnelSimulation3D.TryGetVehicleReferenceFrame` as the shared source for CG, axle, wheel, and ground-reference data.
- Preserve the visualization contract:
  - `Effects`: stylized readable wake.
  - `Velocity`: compact slices plus streamlines.
  - `Pressure`: body-adjacent slice, never a giant viewport-filling plane.
- `Surface Pressure` is a separate body-only contour mode. Do not collapse it into `Pressure`.
- Keep `WindTunnelSettings.flipVehicleDirection` as the default-on orientation toggle for loaded vehicles. It should only affect vehicle facing, not solver wind direction resolution.
- Keep divergence and mean vorticity diagnostics visible in the wind-tunnel properties panel.
- Keep vehicle-aware outputs separated from solver diagnostics:
  - `Drag Force`, `Vertical Aero Force`, `Downforce`, `CoP`, `Pitch/Yaw/Roll Moment`, `Axle Loads`, `Estimated Top Speed`
  - `Mean Velocity`, `Max Velocity`, `Pressure Drop`, `Wall Shear`, `Divergence Error`, `Mean Vorticity`
- Keep moving-ground and wheel-rotation behavior as boundary proxies in the solver, not rigid-body wheel physics.

## Streamline Rendering Quality

- `StreamlineFieldRenderer` uses solver data as the **primary** velocity source (82% weight in Streamlines mode, 55% in Effects mode). The analytical ellipsoid model is only a coarse fallback; do not regress the solver weight below 50%.
- Seed positions use a **stable RNG** (frame-independent) to avoid per-frame flickering. Do not reseed the RNG with `Time.frameCount`.
- Default quality settings: `stepLength=0.028`, `pointsPerLine=220`, `lineWidth=0.038`, `flowSpeed=1.5`, `seedJitter=0.06`.
- Lines must be long enough to traverse a meaningful portion of the tunnel (up to 82% at moderate velocities). Do not cap `maxTravel` below `tunnelLength * 0.50f`.
- Speed kill threshold is set to 0.5% of inlet velocity to preserve lines through wake regions. Do not raise above 2%.
- Minimum forward velocity clamp in `ComputeAnalyticalFlow` is 6% of freestream. Higher values (>15%) cause lines to look unnaturally straight in the wake.
- The velocity gradient uses a 6-stop CFD-style palette (deep blue → cyan → green → yellow → orange → red). Maintain vivid saturation.
- Line end alpha should be at least 0.20 for visible tail tapering. Alpha below 0.10 makes lines appear to stop abruptly.

## Validation

- Run Unity batch compile validation after meaningful wind-tunnel changes.
- Run the Windows batch build after the compile check passes.
- Use `Docs/WindTunnel-QA-Checklist.md` for smoke testing and update it if the workflow changes.

## Maintenance

- Update both this file and the root `AGENTS.md` whenever wind-tunnel behavior, controls, diagnostics, or validation steps change.
- If vehicle-aware labels or exports change, keep these files aligned:
  - `Assets/Resources/UI/UXML/WindTunnel/WindTunnelProperties.uxml`
  - `Assets/Scripts/UI/WindTunnel/WindTunnelPropertiesController.cs`
  - `Assets/Resources/UI/UXML/AnalysisProperties.uxml`
  - `Assets/Scripts/UI/MainScreenController.cs`
- If stance, wheel-proxy, or rolling-road logic changes, keep these files aligned:
  - `Assets/Scripts/Sim3D/WindTunnel/WindTunnelSimulation3D.cs`
  - `Assets/Scripts/Core/RuntimeModelLoader.cs`
  - `Assets/Scripts/Physics/WindTunnel/NavierStokesGridSolver.cs`
  - `Assets/Resources/Compute/WindTunnel/NavierStokes3D.compute`
