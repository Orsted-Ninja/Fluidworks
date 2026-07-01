# Wind Tunnel QA Checklist

Use this file while testing `External Aero` with the F1 model.

## Before Each Test

1. Stop Play mode completely if the previous run looked broken.
2. Start Play mode again.
3. Open `External Aero`.
4. Load the model.
5. Click `Reset Wind Tunnel`.
6. Wait 3 to 5 seconds before judging the result.

## Quick Smoke Tests

Run these first. If any of them fail badly, stop and capture a screenshot before continuing.

### 1. Baseline Stable

Set:
- Wind Speed: `30`
- Angle of Attack: `0`
- Turbulence: `1`
- Wind Direction: `Auto`
- Mode: `Effects`
- Streamline Count: `80`
- Time Scale: `1.0`
- Iterations / Frame: `4`

Expected:
- Flow should stay close to the car.
- No full-screen white lines.
- No giant planes covering the viewport.
- No disappearing model.

Bug signs:
- Nothing renders at all.
- The effect fills the whole screen.
- Changing settings does nothing.

### 2. Effects Mode Switch Test

Set:
- Wind Speed: `50`
- Angle of Attack: `0`
- Turbulence: `1`
- Wind Direction: `Auto`

Do this:
1. Set Mode to `Effects`
2. Set Mode to `Streamlines`
3. Set Mode to `Velocity`
4. Set Mode to `Pressure`
5. Set Mode back to `Effects`

Expected:
- Each mode should replace the previous one cleanly.
- Old lines, particles, or slices should not remain on screen.

Bug signs:
- A previous mode stays visible underneath the new mode.
- `Effects` becomes blank after switching back.
- Velocity or Pressure creates giant slabs across the scene.

### 3. Left and Right Yaw Check

Run twice.

Case A:
- Wind Speed: `50`
- Angle of Attack: `12`
- Turbulence: `1`
- Wind Direction: `Auto`
- Mode: `Effects`

Case B:
- Wind Speed: `50`
- Angle of Attack: `-12`
- Turbulence: `1`
- Wind Direction: `Auto`
- Mode: `Effects`

Expected:
- The flow should shift to opposite sides between the two runs.
- The pattern should not stay perfectly symmetric.

Bug signs:
- Both runs look almost identical.
- Flow direction does not react to AoA.

### 4. Velocity and Pressure Regression

Use the same values for both runs:
- Wind Speed: `50`
- Angle of Attack: `0`
- Turbulence: `1`
- Wind Direction: `Auto`
- Time Scale: `1.0`
- Iterations / Frame: `4`

Run A:
- Mode: `Velocity`

Run B:
- Mode: `Pressure`

Expected:
- Visualization should stay near the car.
- The whole tunnel should not turn into one giant plane.

Bug signs:
- Full-screen colored slabs.
- Completely blank mode.
- Mode still shows the previous renderer.

## Direction Tests

These are useful for finding bugs in custom wind direction handling.

### 5. Mild Crosswind

Set:
- Wind Speed: `50`
- Angle of Attack: `0`
- Turbulence: `1`
- Wind Direction: `Custom`
- Custom Direction: `0.87, 0.00, 0.50`
- Mode: `Effects`
- Streamline Count: `72`
- Time Scale: `1.0`
- Iterations / Frame: `5`

Expected:
- Flow should come in diagonally instead of straight ahead.

Bug signs:
- Custom direction is ignored.
- Flow still behaves like `Auto`.

### 6. Upwash / Downwash Check

Set:
- Wind Speed: `50`
- Angle of Attack: `0`
- Turbulence: `1`
- Wind Direction: `Custom`
- Custom Direction: `0.96, 0.28, 0.00`
- Mode: `Effects`
- Streamline Count: `72`
- Time Scale: `1.0`
- Iterations / Frame: `5`

Expected:
- Flow should visibly tilt upward.

Bug signs:
- The visual disappears.
- The model gets clipped or the effect explodes vertically.

## Stability Tests

### 7. Medium Turbulence

Set:
- Wind Speed: `50`
- Angle of Attack: `0`
- Turbulence: `10`
- Wind Direction: `Auto`
- Mode: `Effects`
- Streamline Count: `80`
- Time Scale: `1.0`
- Iterations / Frame: `5`

Expected:
- Flow should become less uniform but still readable.

Bug signs:
- Heavy flicker.
- Sudden jumps to full-screen geometry.

### 8. Extreme Turbulence Stress

Set:
- Wind Speed: `50`
- Angle of Attack: `0`
- Turbulence: `40`
- Wind Direction: `Auto`
- Mode: `Effects`
- Streamline Count: `80`
- Time Scale: `0.7`
- Iterations / Frame: `6`

Expected:
- More chaotic motion, but still controlled.

Bug signs:
- NaN-looking lines.
- Visuals shoot far outside the test area.
- Console starts spamming warnings or errors.

### 9. Near-Zero Speed

Set:
- Wind Speed: `1`
- Angle of Attack: `0`
- Turbulence: `1`
- Wind Direction: `Auto`
- Mode: `Effects`
- Streamline Count: `60`
- Time Scale: `1.0`
- Iterations / Frame: `4`

Expected:
- Very weak motion.
- The app should remain stable.

Bug signs:
- Crash, freeze, or endless console spam.
- Visual disappears forever even after increasing speed again.

### 10. Maximum Speed Edge Case

Set:
- Wind Speed: `150`
- Angle of Attack: `0`
- Turbulence: `0`
- Wind Direction: `Auto`
- Mode: `Effects`
- Streamline Count: `60`
- Time Scale: `0.5`
- Iterations / Frame: `8`

Expected:
- Stronger flow, but not a screen-filling mess.

Bug signs:
- Whole-screen streaks.
- Severe clipping.
- UI becomes unresponsive.

## Best Test Order

Use this order if you want a short but useful pass:

1. Baseline Stable
2. Effects Mode Switch Test
3. Left and Right Yaw Check
4. Mild Crosswind
5. Extreme Turbulence Stress
6. Velocity and Pressure Regression
7. Maximum Speed Edge Case

## What To Capture If Something Breaks

Take a screenshot that shows:
- the `Mode` dropdown
- the visible output
- the console if there is a warning

Also note:
- which test name failed
- whether it broke only after switching modes
- whether AoA or Custom Direction changed anything
- whether the bug happened in the editor, standalone build, or both

## Short Bug Note Template

Copy this when reporting a bug:

```text
Test:
Mode:
Wind Speed:
Angle of Attack:
Turbulence:
Wind Direction:
Custom Direction:
Time Scale:
Iterations / Frame:

What happened:

What I expected:

Console warnings/errors:
```
