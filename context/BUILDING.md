# Building the Project (Unity 6000.3.1f1, URP, Windows)

## Requirements
- Unity `6000.3.1f1` (Unity Hub recommended)
- Windows 10/11
- GPU with compute shader support (RTX 3050 or better recommended)
- Optional for MP4 export: `ffmpeg.exe` at `Assets/StreamingAssets/Tools/ffmpeg.exe`

## Open the Project
1. Open Unity Hub.
2. Click `Add` and select the project folder: `C:\Users\Beena C S\Downloads\Aeroflow (1)\Aeroflow`.
3. Ensure Unity opens with version `6000.3.1f1`.

## URP Setup (if not already configured)
1. Go to `Edit > Project Settings > Graphics`.
2. Ensure the Scriptable Render Pipeline Asset is set to URP.
3. If missing, create a URP pipeline asset and assign it.

## Build Settings
1. Open `File > Build Settings`.
2. Platform: `Windows, Mac, Linux`.
3. Target Platform: `Windows`.
4. Architecture: `x86_64`.
5. Add scenes in this order:
   - `Assets/Scenes/SampleScene.unity`
   - `Assets/Scenes/Wind Tunnel (3D).unity`
   - `Assets/Scenes/Test C (3D).unity`
6. Click `Player Settings`:
   - `Resolution and Presentation`:
     - Set a default resolution (e.g., 1920x1080).
   - `Other Settings`:
     - `Scripting Backend`: IL2CPP (recommended)
     - `Api Compatibility Level`: .NET Standard 2.1

## Build
1. Click `Build`.
2. Choose an output folder (e.g., `Builds\AeroFlow`).
3. Wait for build completion.

## Batch Build
Run this from PowerShell to produce a Windows player without opening the editor UI:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.1f1\Editor\Unity.exe' `
  -batchmode -nographics -quit `
  -projectPath 'C:\Users\Beena C S\Downloads\Aeroflow (1)\Aeroflow' `
  -executeMethod AeroFlow.Editor.BatchBuild.BuildWindows64 `
  -buildOutput 'C:\Users\Beena C S\Downloads\Aeroflow (1)\Aeroflow\Builds\BatchBuild\Aeroflow.exe'
```

The build summary is written to `Builds\BatchBuild\build-summary.txt`.
The batch build now runs an editor validation pass first. If required scenes/assets are missing, wind-tunnel defaults are invalid, or UI text assets contain corrupted encoding markers, the build stops early and the validation results are written to the same summary file.
The current verified batch build summary was regenerated on `2026-03-07 10:09:07` and succeeded with only the Unity Cloud native-symbol upload warning.

## Post-Build Check
- Ensure `ffmpeg.exe` is present at:
  - `Builds\AeroFlow\AeroFlow_Data\StreamingAssets\Tools\ffmpeg.exe`
- If missing, copy from your project:
  - `Assets/StreamingAssets/Tools/ffmpeg.exe`
- For wind-tunnel smoke testing in the Editor:
  - `Streamlines` should show focused lines around the body and not fill the whole tunnel when no model is loaded.
  - `Velocity` should show streamlines plus transparent slice planes, including a wake cut plane downstream of the model.
  - `Pressure` should show the model surface contour plus a transparent pressure slice, with no particle fog by default.
  - The particle overlay is optional/debug only and should remain off unless explicitly enabled.

## Common Build Issues
- **Compute shader errors**: Ensure GPU drivers are up to date.
- **UI missing**: Confirm `Assets/Resources/UI/UXML/MainLayout.uxml` is assigned to the `UIDocument` in `SampleScene`.
- **MP4 export not working**: Confirm `ffmpeg.exe` is present in `StreamingAssets/Tools`.
