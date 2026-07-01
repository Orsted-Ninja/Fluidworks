<div align="center">
  <img src="Showcase/photos/plane-wind/streamlines.png" alt="FluidWorks Plane Streamlines" width="100%">
  
  # 🌊 FluidWorks
  
  **A Unity-based real-time CFD-style visualization tool**
  <br>
  *Focused on interactive engineering workflows and visually stunning fluid dynamics.*

  [![Unity](https://img.shields.io/badge/Unity-6000.3.x-black?logo=unity)](#)
  [![Platform](https://img.shields.io/badge/Platform-Windows-blue)](#)
  [![Status](https://img.shields.io/badge/Status-Completed-success)](#)

  <br>
  
  **✨ My Miniproject by [Orsted-Ninja](https://github.com/Orsted-Ninja), with contributor [Abhinav Manoj](https://github.com/abhinavmanoj05)! ✨**
  
  *Special Mentions: The main scene, physics setup, physical interactions, and the MLP were extensively handled and fixed up by Orsted-Ninja.*

</div>

<br>

---

## 🚀 Downloads

Check out the [Releases](https://github.com/Orsted-Ninja/Fluidworks/releases) for our latest builds:
- 🖥️ **Windows Build**: Fully featured desktop experience with video export capabilities. **(Primary Build)**

*(Note: An experimental Android APK test build is also available in the repository files under the `Builds` folder, but is not included in the main release due to its size).*

---

## 📸 Showcase & Results

Take a look at some of the fluid dynamics simulations and UI from our project!

### 📄 Full Project Report
Read our comprehensive findings and technical details in the **[AeroFlow Project Report](Showcase/result/AeroFlow_Report.pdf)**.
*(Note: This comprehensive report was generated entirely by our own software after running a simulation on a 3D model!)*

<br>

### 🏎️ Vehicle Aerodynamics (Car)
<p align="center">
  <img src="Showcase/photos/car-wind/streamlines.png" width="48%">
  <img src="Showcase/photos/car-wind/surfacepressure.png" width="48%">
</p>

### ✈️ Aircraft Aerodynamics (Plane)
<p align="center">
  <img src="Showcase/photos/plane-wind/streamlines.png" width="48%">
  <img src="Showcase/photos/plane-wind/surfacepressure.png" width="48%">
</p>

### 💻 User Interface
<p align="center">
  <img src="Showcase/photos/UI-screenshots/homepage.png" width="48%">
  <img src="Showcase/photos/UI-screenshots/externalaero.png" width="48%">
</p>

---

## ✨ Features & Highlights

It includes:
- **External aero** wind-tunnel simulations
- **Internal flow** / pipe flow studies
- **Rotatory Mode** for rotating geometry
- **Free-surface** / dam-break simulations
- Runtime model import and segmentation tools
- Visual diagnostics, streamlines, pressure views, and exports

---

## ⚙️ Requirements

- Unity 6000.3.x or later
- URP-capable project setup
- Windows desktop build support for file dialogs and video export
- `ffmpeg` on PATH if you want MP4 video capture

---

## 🏎️ Quick Start

1. Open the project in Unity.
2. Open a scene such as `Assets/Scenes/WindTunnelSample.unity` or the main launcher scene used by the project.
3. Press Play.
4. Use the Home screen to choose a simulation template.
5. Load a model if the selected mode supports runtime geometry.
6. Adjust settings in the Properties panel.
7. Use the View and Simulation tabs to control playback, visualization, and camera presets.

---

## 🌀 Simulation Modes

### External Aero
- Wind-tunnel style external flow
- GPU Navier-Stokes grid solver
- Streamlines, velocity slices, pressure slices, and force metrics

### Internal Flow
- Pipe and duct style flow studies
- Pressure drop, flow rate, Reynolds number, and related diagnostics

### Rotatory Mode
- Rotating geometry workflows for windmills, fans, propellers, and turbines
- Segment model parts into rotating and static groups
- Define axis, pivot, motion settings, and rotating zone previews
- Surface and wake diagnostics for rotor-style setups

### Free-Surface Flow
- Dam-break and sloshing style free-surface simulations

### Validation / FSI Lite
- Validation and lightweight coupled-motion workflows used by the UI and project system

---

## 📦 Model Import

FluidWorks supports runtime import of common visual mesh formats:
- `OBJ`, `STL`, `GLTF`, `GLB`

The project also has paths for optional simulation proxy geometry in some workflows.

---

## 🔄 Rotatory Mode Workflow

1. Select `Rotatory Mode`.
2. Import a rotor-like model.
3. Open `Geometry` to access the model segmentation panel.
4. Press `Auto Segment Model`.
5. Reassign any misclassified parts using the collection controls.
6. Set:
   - rotation axis
   - pivot
   - RPM or tip-speed ratio
   - rotation direction
   - rotating zone size and offset
7. Press Play to run the solver and animation.

For single welded meshes, the project includes an automatic segmentation pass that tries to split blade-like and static regions into separate collections.

---

## 🖥️ UI Overview

- **`Home` tab**: project actions and template selection
- **`Simulation` tab**: playback and solver controls
- **`View` tab**: camera and render presets
- **`Windows` tab**: toggles the outline, console, and properties panels
- **Properties panel**: mode-specific controls, including segmentation and diagnostics

---

## 📊 Diagnostics and Exports

The project can surface metrics such as drag / lift / side force, torque, power, efficiency, pressure drop, wake deficit, and Reynolds number.

Export options include:
- CSV results
- JSON snapshots
- HTML reports
- viewport screenshots
- MP4 video capture when ffmpeg is available

---

## 📁 Repository Layout

- `Assets/Scripts/Sim3D/` - simulation drivers
- `Assets/Scripts/Physics/` - solver coupling and physics helpers
- `Assets/Scripts/Visualization/` - flow rendering and overlays
- `Assets/Scripts/UI/` - UI Toolkit controllers
- `Assets/Resources/UI/UXML/` - UI markup
- `Assets/Resources/UI/USS/` - UI styles
- `Assets/Resources/Compute/` - compute shaders loaded at runtime
- `Assets/Scenes/` - Unity scenes
- `Docs/` - workflow notes and test guides

---

## 📚 Useful Docs

- [Project overview](AeroFlow_Project_Overview.txt)
- [Simulation mode map](Docs/SimulationModeMap.md)
- [Dual asset pipeline](Docs/DualAssetPipeline.md)
- [Wind tunnel QA checklist](Docs/WindTunnel-QA-Checklist.md)

---

## 📌 Notes

- The project emphasizes interactive, visually plausible results.
- Some workflows are still lighter-weight or preview-oriented rather than full production CFD.
- Compute shaders and runtime import paths must be present for the corresponding modes to run correctly.
