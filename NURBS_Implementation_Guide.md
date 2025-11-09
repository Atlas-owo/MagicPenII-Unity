# NURBS Plateau Surface Implementation Guide

## Overview
This is a simplified plateau-shaped surface implementation for JND psychophysics experiments. The surface is a 2D profile curve (flat-topped bell curve) extruded uniformly along the Z-axis, creating a surface that is invariant in the Z-direction.

## Key Features

### Simplified Plateau-Only Design
- **Single shape type**: Flat-topped bell curve (plateau) only
- **2D profile extrusion**: Profile defined in X-Y plane, uniform in Z direction
- **Streamlined controls**: Only essential parameters for plateau geometry

### Surface Geometry
The surface consists of a 2D profile curve with three regions:
1. **Flat Plateau** (center): Constant height across the top
2. **Smooth Transitions** (shoulders): C2 continuous cosine curves
3. **Asymptotic Tails** (edges): Exponential decay to baseline

This profile is **extruded uniformly along the Z-axis**, meaning:
- All points with the same X coordinate have the same Y (height) value
- Z coordinate does not affect height
- Creates a "ridge-like" surface with bilateral symmetry

## Control Parameters

### Essential Parameters (Unity Inspector)

#### Surface Dimensions
- **Total Width** (meters): Physical width in X direction
  - Default: 1.0m
  - Range: 0.1m to any practical value

- **Total Depth** (meters): Physical depth in Z direction (extrusion length)
  - Default: 1.0m
  - Range: 0.1m to any practical value

#### Plateau Profile
- **Plateau Width** (0.05-0.9): Width of flat top as fraction of total width
  - Example: 0.3 = 30% of surface is flat plateau
  - Recommended: 0.2-0.4 for most experiments
  - Affects: Size of constant-height region

- **Transition Length** (0.01-0.2): Length of rise/fall as fraction of total width
  - Controls the horizontal span of shoulder curves
  - Smaller values = steeper slopes, sharper transitions
  - Larger values = gentler slopes, more gradual transitions
  - Recommended: 0.03-0.08 for steep, rectangular-like profile
  - Default: 0.05 (5% transition zone)

- **Transition Steepness** (1.0-10.0): Sharpness of corner transitions
  - Controls how abruptly the transition occurs
  - Higher values = sharper, more vertical transitions
  - Lower values = smoother, more gradual curves
  - Recommended: 5.0-8.0 for sharp corners
  - Default: 5.0 (steep but smooth)

#### Height Control
- **Amplitude** (-0.2 to 0.2): Maximum height in meters
  - Positive: Creates hills/elevations
  - Negative: Creates valleys/depressions
  - Typically controlled by experiment (JNDTestController)

- **Reverse**: Boolean flag to invert height values
  - Used for experimental control

#### Resolution
- **Resolution X**: Number of vertices along X axis
  - Default: 30
  - Higher = smoother profile curve (but more processing)

- **Resolution Z**: Number of vertices along Z axis
  - Default: 30
  - Higher = smoother extrusion (less visual banding)

## Mathematical Profile

### Coordinate System
```
X: Horizontal axis (profile variation)
Y: Vertical axis (height)
Z: Depth axis (uniform extrusion)

For any point (x, y, z) on the surface:
  y = f(x) * amplitude  [independent of z]
```

### Height Function f(x)

Given normalized position x ∈ [0, 1], centered at 0.5:

```
dist = |x - 0.5|  // Distance from center

Region 1 (Flat Plateau): dist ≤ plateauWidth/2
  f(x) = 1.0  [Full height - constant]

Region 2 (Steep Transition): plateauWidth/2 < dist < (plateauWidth/2 + transitionLength)
  t = (dist - plateauWidth/2) / transitionLength
  f(x) = GeneralizedSmoothstep(1 - t, steepness)

  Where GeneralizedSmoothstep creates steep but smooth transitions:
  - Standard smoothstep: s = t² * (3 - 2t)
  - Power adjustment: s' = 0.5 + sign(s - 0.5) * |2(s - 0.5)|^(1/steepness) * 0.5
  - Higher steepness = more vertical transition

Region 3 (Flat Tail): dist ≥ (plateauWidth/2 + transitionLength)
  f(x) = 0.0  [Baseline - completely flat, no oscillations]
```

### Geometric Layout
```
Total Width (1.0)
├──────────────────┬──┬──────────────────┬──┬──────────────────┐
│   Flat Tail (L)  │T │  Flat Plateau (C) │T │   Flat Tail (L)  │
└──────────────────┴──┴──────────────────┴──┴──────────────────┘

Where:
  C = Plateau Width (e.g., 0.3 = 30%)
  T = Transition Length (e.g., 0.05 = 5%) - STEEP
  L = Remaining tail space = (1.0 - C - 2*T) / 2

Example with plateauWidth=0.3, transitionLength=0.05:
  Plateau: 0.3 (30% of width)
  Each transition: 0.05 (5% of width - steep!)
  Each tail: 0.3 (30% of width - flat at Y=0)
  Total: 0.3 + 0.05 + 0.3 + 0.05 + 0.3 = 1.0 ✓

Key characteristics:
  - Transitions are NARROW and STEEP (5% vs previous 20%)
  - Tails are FLAT at baseline (Y=0), no oscillations
  - Profile resembles rectangular function with smoothed corners
```

## Setup in Unity

### 1. Create Surface GameObject
```
Hierarchy → Right Click → Create Empty → Name: "Plateau_Surface"
```

### 2. Add Required Components
```
Inspector → Add Component:
  - Mesh Filter
  - Mesh Renderer
  - Mesh Collider
  - NURBSSurface (script)
```

### 3. Configure Surface Parameters

#### For Standard JND Experiments (Rectangular-like):
```
Surface Dimensions:
  Total Width: 1.0
  Total Depth: 1.0

Plateau Profile Parameters:
  Plateau Width: 0.3 (30% flat top)
  Transition Length: 0.05 (5% steep transitions)
  Transition Steepness: 5.0 (steep corners)

Height Control:
  Amplitude: 0.05 (will be controlled by experiment)
  Reverse: false

Surface Resolution:
  Resolution X: 40 (increase for smooth steep transitions)
  Resolution Z: 30
```

#### For Very Sharp Corners (Nearly Rectangular):
```
Plateau Width: 0.3 (30% flat)
Transition Length: 0.03 (3% very narrow transitions)
Transition Steepness: 8.0 (very steep)
Resolution X: 50 (higher resolution for sharp features)
```

#### For Wider Plateau (More Gradual):
```
Plateau Width: 0.5 (50% flat)
Transition Length: 0.08 (8% transitions)
Transition Steepness: 4.0 (moderate steepness)
```

### 4. Add Material
Assign a material to the Mesh Renderer for visibility:
- Standard Shader recommended
- Consider contrasting colors for better depth perception
- Optional: Enable wireframe mode for debugging

### 5. Configure JNDTestController
```
Select GameObject with JNDTestController
In Inspector → Surface Settings:
  - Surface Type: NURBS
  - Nurbs Surface: Drag your Plateau_Surface GameObject here
  - Use Surface: ✓ (checked)
```

## Public Methods (API)

### Height Control
```csharp
SetHeight(float height)
// Set surface height immediately
// Example: surface.SetHeight(0.05f);

SetHeightSmooth(float targetHeight, float duration = 0.5f)
// Animate to new height over time
// Example: surface.SetHeightSmooth(0.08f, 1.0f);

GetCurrentHeight()
// Returns current amplitude value
// Example: float h = surface.GetCurrentHeight();
```

### Plateau Geometry Control
```csharp
SetPlateauWidth(float width)
// Set plateau width (0.05-0.9)
// Example: surface.SetPlateauWidth(0.4f);

SetTransitionLength(float length)
// Set transition length (0.01-0.2)
// Example: surface.SetTransitionLength(0.05f);  // Steep transition

SetTransitionSteepness(float steepness)
// Set transition steepness (1.0-10.0)
// Higher = sharper corners
// Example: surface.SetTransitionSteepness(7.0f);  // Very steep

GetPlateauWidthWorld()
// Returns plateau width in meters
// Example: float w = surface.GetPlateauWidthWorld();

GetTransitionLengthWorld()
// Returns transition length in meters
// Example: float t = surface.GetTransitionLengthWorld();
```

## Visual Cross-Section

### Side View (X-Y Plane) - Rectangular-like Profile
```
Height
  ↑
  │      ┌──────────────────┐         ← Flat Plateau (Region 1)
  │      │                  │         ← Full height, constant
  │     ╱│                  │╲        ← STEEP Transitions (Region 2)
  │    ╱ │                  │ ╲       ← Nearly vertical, smooth
  │   ╱  │                  │  ╲
  │__╱___│__________________│___╲__→ X Position
  │←─Flat─→|←─P─→|←─Flat─→|         ← Flat Tails at Y=0 (Region 3)
         T                T

Legend:
  P = Plateau Width (30%)
  T = Transition Length (5% - STEEP!)
  Flat = Tail regions at Y=0 (30% each side)

Key Features:
  ✓ Sharp, steep transitions (nearly vertical)
  ✓ Flat tails at baseline (Y=0)
  ✓ No oscillations or bouncing
  ✓ Resembles rectangular function with smoothed corners
```

### Top View (X-Z Plane)
```
Z ↑
  │ ═══════════════════════════    ← All points at same X have same height
  │ ═══════════════════════════    ← Surface is uniform in Z direction
  │ ═══════════════════════════    ← Profile extruded along Z
  │ ═══════════════════════════
  │ ═══════════════════════════
  └─────────────────────────────→ X
```

## Continuity Properties

### Mathematical Smoothness
- **C0 (Position continuity)**: ✓ No gaps or jumps
- **C1 (Tangent continuity)**: ✓ Smooth slopes everywhere (transitions to flat tails)
- **Smoothness in transitions**: ✓ Generalized smoothstep ensures smooth derivatives

### Key Improvements Over Previous Version
- **Steeper transitions**: Transition length reduced from 0.2 to 0.05 (4x narrower)
- **Adjustable steepness**: New parameter controls sharpness (1-10 range)
- **Flat tails**: Baseline regions are exactly Y=0 (no exponential decay oscillations)
- **Monotonic descent**: Once curve descends from plateau, it never rises again
- **Rectangular-like**: Overall profile resembles smoothed rectangular function

### Symmetry
- **Bilateral symmetry**: ✓ Mirror symmetry about center (X = 0.5)
- **Z-axis uniformity**: ✓ Identical profile at all Z positions

### Monotonicity
- **No oscillations**: ✓ Tails remain flat at Y=0
- **No bouncing**: ✓ No upward curves after descending
- **Stable baseline**: ✓ Tails converge to and stay at zero

## Performance Considerations

### Mesh Complexity
- Vertices = resolutionX × resolutionZ
- Triangles = 2 × (resolutionX - 1) × (resolutionZ - 1)

### Recommended Settings by Platform
```
VR Headset:
  Resolution X: 20-30
  Resolution Z: 20-30
  (~400-900 vertices)

Desktop:
  Resolution X: 30-50
  Resolution Z: 30-50
  (~900-2500 vertices)

High-Quality Visualization:
  Resolution X: 50-100
  Resolution Z: 50-100
  (~2500-10000 vertices)
```

### Optimization Tips
- Lower resolutionZ if performance is an issue (Z is uniform anyway)
- Use resolutionX for profile smoothness (more important)
- Avoid calling SetHeight() every frame - use SetHeightSmooth() for animations
- MeshCollider updates automatically but can be expensive for high-res meshes

## Troubleshooting

### Surface Not Visible
- Check MeshRenderer is enabled and has a material assigned
- Verify amplitude is not zero
- Check camera position and lighting

### Transitions Not Steep Enough
- Decrease **Transition Length** (try 0.03-0.05 for very steep)
- Increase **Transition Steepness** (try 6.0-8.0 for sharp corners)
- Increase **Resolution X** (40-50) for better representation of steep features

### Transitions Too Steep (Not Smooth)
- Increase **Transition Length** (try 0.08-0.12)
- Decrease **Transition Steepness** (try 3.0-4.0)
- Ensure resolution is adequate (min 30 in X direction)

### Tails Not Flat / Oscillating
- **This should not happen** with the new implementation (tails are hardcoded to Y=0)
- If you see oscillations, check that you're using the updated NURBSSurface.cs
- Verify transitionLength + plateauWidth/2 < 0.5 (ensure transitions fit)

### Plateau Too Small/Large
- Adjust **Plateau Width**:
  - Increase for wider flat top
  - Decrease for narrower flat top
- Note: With steep transitions (0.05), you have more room for plateau
- Ensure plateau width + 2×transition length < 1.0

### Haptic Pen Not Detecting Surface
- Ensure MeshCollider component is present
- Check surface has correct Layer (match HapticPenController's surfaceLayerMask)
- Verify surface is within maxDistance of pen tip
- Check that surface is not behind other objects in raycast path

### Surface Looks Jagged/Blocky
- Increase resolutionX for smoother X profile
- Increase resolutionZ if seeing banding in Z direction
- Check normals are calculated correctly (should be automatic)

## Usage Example

```csharp
// Get reference to surface
NURBSSurface surface = GetComponent<NURBSSurface>();

// Configure plateau geometry for steep, rectangular-like profile
surface.SetPlateauWidth(0.3f);           // 30% flat top
surface.SetTransitionLength(0.05f);      // 5% steep transition
surface.SetTransitionSteepness(5.0f);    // Steep but smooth corners

// Set height for experiment
surface.SetHeight(0.05f);                // 5cm high

// Animate height change
surface.SetHeightSmooth(0.08f, 1.0f);    // Transition to 8cm over 1 second

// For very sharp corners (nearly rectangular)
surface.SetTransitionLength(0.03f);      // Even narrower
surface.SetTransitionSteepness(8.0f);    // Very steep

// Query current state
float currentHeight = surface.GetCurrentHeight();
float plateauWidthMeters = surface.GetPlateauWidthWorld();
float transitionMeters = surface.GetTransitionLengthWorld();

Debug.Log($"Plateau: {plateauWidthMeters}m, Transition: {transitionMeters}m, Height: {currentHeight}m");
```

## Integration with JND Experiments

The NURBSSurface is fully compatible with JNDTestController:

1. **Automatic height control**: JNDTestController calls SetHeight() automatically
2. **Stimulus presentation**: Surface updates in real-time during trials
3. **Collision detection**: MeshCollider enables haptic pen interaction
4. **Height variation**: Experiment controls amplitude for threshold testing

### Typical Experiment Flow
```
1. Initialize surface with default geometry (plateau width, transition length)
2. JNDTestController starts trial
3. JNDTestController calls surface.SetHeight(stimulusValue)
4. Surface regenerates mesh with new amplitude
5. User explores surface with haptic pen
6. Repeat for next trial with different stimulus value
```

## Design Rationale

### Why Z-Axis Extrusion?
- **Simplicity**: 2D profile is easier to understand and control
- **Consistency**: Same tactile experience regardless of Z position
- **Performance**: Simplified geometry calculation
- **Experimental control**: Only X-position matters for stimulus variation

### Why Steep Transitions?
- **Rectangular-like profile**: Resembles smoothed rectangular function, not bell curve
- **Clear boundaries**: Sharper transitions make plateau edges more perceptible
- **Reduced transition zone**: More surface area dedicated to plateau vs. gradual slopes
- **Perceptual salience**: Steep changes are easier to detect tactually
- **Design intent**: Profile should look like rectangular function with rounded corners

### Why Flat Tails at Y=0?
- **No oscillations**: Eliminates unwanted ripples or bouncing after descent
- **Monotonic descent**: Ensures curve never rises after descending from plateau
- **Stable baseline**: Provides clear, constant reference at ground level
- **Eliminates artifacts**: Previous exponential decay could cause small oscillations
- **Simplicity**: Flat is conceptually clearer than asymptotic approach

### Why Adjustable Steepness Parameter?
- **Fine control**: Allows precise tuning of corner sharpness
- **Experimental flexibility**: Different studies may need different transition profiles
- **Smoothness guarantee**: Higher steepness maintains smooth derivatives (no discontinuities)
- **Wide range**: 1.0-10.0 covers gentle to nearly-vertical transitions

### Why These Parameters?
- **Plateau Width**: Direct control of flat region size (key experimental variable)
- **Transition Length**: Direct control of transition zone width (horizontal span)
- **Transition Steepness**: Direct control of corner sharpness (vertical rate)
- **Amplitude**: Standard height control compatible with existing experiment code
- **Fractions**: Normalized values (0-1) make parameters independent of absolute surface size

### Key Improvements in This Version
1. **4× steeper default**: Transition length 0.05 vs 0.2 (80% reduction)
2. **Flat tails**: Removed exponential decay, hardcoded Y=0 for stability
3. **Steepness control**: New parameter for fine-tuning corner sharpness
4. **No oscillations**: Guaranteed monotonic descent, no bouncing
5. **Rectangular paradigm**: Profile resembles smoothed rectangle, not bell curve
