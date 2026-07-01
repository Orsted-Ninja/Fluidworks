# Simulation Mode Map

This project has two primary simulation modes. Use this map as the source of truth.

## Folder Convention

- `Assets/Scripts/Sim3D/WindTunnel/...` = wind tunnel simulation logic
- `Assets/Scripts/Sim3D/DamBreak/...` = dam-break simulation logic
- `Assets/Scripts/Physics/WindTunnel/...` = wind tunnel physics/metrics coupling
- `Assets/Scripts/Physics/DamBreak/...` = dam-break/SPH physics helpers
- `Assets/Scripts/Visualization/WindTunnel/...` = wind tunnel visualization
- `Assets/Scripts/Visualization/DamBreak/...` = dam-break visualization
- `Assets/Scripts/UI/WindTunnel/...` and `Assets/Scripts/UI/DamBreak/...` = mode-specific property panels
- `Assets/Resources/Compute/WindTunnel/...` = wind tunnel compute shaders
- `Assets/Resources/Compute/DamBreak/...` = dam-break compute shaders
- `Assets/Resources/Compute/Shared/...` = compute includes shared by both modes
- `Assets/Resources/UI/UXML/WindTunnel/...` and `Assets/Resources/UI/UXML/DamBreak/...` = mode-specific UI markup

## Wind Tunnel (External Aero)

- Scene: `Assets/Scenes/WindTunnelSample.unity`
- Main script: `Assets/Scripts/Sim3D/WindTunnel/WindTunnelSimulation3D.cs`
- Primary solver: `Navier-Stokes Grid (GPU)` via `NavierStokesGridSolver`
- Compute shaders:
- `Assets/Resources/Compute/WindTunnel/NavierStokes3D.compute` (grid solve + particle advection)
- Metrics path: `Assets/Scripts/Physics/WindTunnel/ExternalAeroLoadEstimator.cs` + `Assets/Scripts/Managers/SimulationManager.cs`

## Dam Break (Free Surface)

- Scene: `Assets/Scenes/Test C (3D).unity` (template route)
- Main script: `Assets/Scripts/Sim3D/DamBreak/Simulation3D.cs`
- Solver: `SPH Particle Solver (GPU)`
- Compute shader:
- `Assets/Resources/Compute/DamBreak/FluidSim3D.compute`

## Why Compute Files Are Under `Resources/Compute`

Both simulation modes load compute shaders at runtime using `Resources.Load<ComputeShader>(...)`.

Examples:
- Wind Tunnel Navier solver: `Resources.Load<ComputeShader>("Compute/WindTunnel/NavierStokes3D")`
- Dam Break: `Resources.Load<ComputeShader>("Compute/DamBreak/FluidSim3D")`

So compute shaders used at runtime must be under `Assets/Resources/...`.

## Folder Rule

For simulation compute shaders in this project, treat `Assets/Resources/Compute` as authoritative.
