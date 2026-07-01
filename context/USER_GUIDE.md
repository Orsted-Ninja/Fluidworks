# User Guide (AeroFlow Wind Tunnel)

## System Requirements
- Windows 10/11
- Dedicated GPU with compute shader support
- Recommended: RTX 3050 or better

## Launch
1. Run AeroFlow.exe from the build folder.
2. The main UI loads with a center 3D viewport and side panels.

## Load a 3D Model
1. Click Select File... in the center prompt.
2. Choose a .glb or .gltf model file (STL/CAD prompts have been removed).
3. The model will appear in the viewport, align to the active simulation scene, and its rigidbody is reset so it sits flat on the floor.

## Camera Controls
- **Orbit**: Left Mouse Button (drag)
- **Pan**: Middle Mouse Button (drag)
- **Zoom**: Mouse Wheel
- **View Presets**: Use Ribbon > View tab:
  - Iso, Top, Front, Side

## Drag Model in Viewport
- Hold Left Ctrl and **Left Click + Drag** on the model to reposition it.
- This works on a horizontal plane and is meant for quick placement.

## Choose Simulation Mode
Ribbon > Home tab:
- **Wind Tunnel**: Aerodynamic flow visualization powered by the Navier–Stokes grid solver (visualized through streamlines).
- **Dam Break**: Water SPH simulation

## Playback Controls
- The View tab now includes quick ⏪/▶/⏩ buttons immediately to its right.
- These buttons mirror the Simulation tab’s play/pause state and adjust simulation speed for both dam-break and wind-tunnel scenes.

## Wind Tunnel Parameters
Right panel (Wind Tunnel Properties):
- **Inlet Velocity (m/s)**
- **Angle of Attack (°)**
- **Turbulence Intensity (%)**
- **Air Density (kg/m³)**
- **Viscosity (Pa·s)**
- **Visualization Mode**: Pressure / Velocity / Streamlines
- **Streamline Density**

## Dam Break Parameters
Right panel (Dam Break Properties):
- **Particle Count**
- **Water Fill Ratio**
- **Density, Viscosity**
- **Gravity, Time Scale, Iterations**
- **Apply & Reset Simulation** (required after changing particle count or fill ratio)

## Results & Quality Assessment
Right panel (Results):
- **Cd / Cl / Reynolds** updated in real time
- **Flow Regime**: Laminar / Transitional / Turbulent
- **Overall Rating**: Excellent / Good / Fair / Needs Work
- **Suggestions**: Design tips based on current shape and flow

## Export Simulation as MP4
1. Ribbon > Home tab
2. Click Export Video (MP4) to start recording.
3. Click again to stop recording.
4. The MP4 will be saved to:
   - %USERPROFILE%\AppData\LocalLow\<CompanyName>\<ProductName>\Captures\<timestamp>\capture.mp4

If MP4 is not created:
- Ensure fmpeg.exe exists at:
  - AeroFlow_Data\StreamingAssets\Tools\ffmpeg.exe

## Tips for Best Visuals
- Increase Streamline Density and Inlet Velocity for more dramatic streaklines and wake vortices.
- Keep the model centered so the streamlines wrap cleanly around it and the wake stays within view.

## Troubleshooting
- **No streamlines visible**: Confirm Wind Tunnel mode is active and the visualization mode is not set to Pressure.
- **Model invisible**: Ensure DefaultModelMaterial.mat is assigned in SimulationManager.
- **Results not updating**: Confirm SimulationManager exists in the scene and is active.
