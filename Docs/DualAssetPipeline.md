# Dual Asset Pipeline (GLB + CAD/Sim Mesh)

## Why two models
- `GLB/GLTF`: fast runtime rendering, materials, hierarchy.
- `CAD/Sim mesh`: watertight, cleanup-ready geometry for CFD truth data or reduced-order calibration.

Use the same object ID for both assets so force/pressure mapping is consistent.

## Where to get matching GLB + CAD
1. OEM/engineering repositories: many provide STEP/IGES first; convert to GLB for visualization.
2. Public benchmarks:
- DrivAer / DrivAerNet: CAD/mesh references and CFD datasets.
- NASA CRM: official CAD geometry.
3. Commercial CAD libraries (GrabCAD/TraceParts/3Dfindit): download STEP, then derive GLB.

If you only find GLB, treat it as visual only and do not use it as a simulation mesh without cleanup.

## Conversion workflow
1. Source of truth: keep `STEP/STP` or `IGES`.
2. CAD cleanup in Blender/FreeCAD/SpaceClaim:
- remove tiny features, close gaps, fix normals, preserve moving-part pivots.
3. Export two outputs:
- Visual: `object.glb`
- Sim candidate: `object_sim.stl` or `object_sim.obj`
4. Keep part IDs stable (same names in visual hierarchy and sim mesh groups).

## Runtime integration in this repo
Current support added:
1. `RuntimeModelLoader` now allows optional second pick for simulation mesh/CAD path.
2. `DualModelDescriptor` stores visual + simulation path metadata.
3. `PartRegistry` builds per-part entries from loaded hierarchy.
4. `FluidLoadIntegrator` applies lightweight per-part loads (FSI-lite baseline).

## Lightweight ANSYS-like loop (practical)
1. Offline truth run in an external CFD package on cleaned CAD mesh.
2. Export reference curves (`Cd`, `Cl`, pressure drop, torque vs speed/AoA).
3. In Unity, tune `FluidLoadIntegrator` coefficients to match trends.
4. Use Unity for interactive what-if studies, not final certification numbers.

