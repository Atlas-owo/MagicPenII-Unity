# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a Unity-based VR psychophysics research project for haptic perception experiments using a custom haptic pen device. The system integrates:
- Unity VR (SteamVR) for the virtual environment
- Arduino-based haptic pen hardware via serial communication
- Psychometric testing using staircase procedures (2AFC - Two-Alternative Forced Choice)
- Real-time data collection and analysis

## Core Architecture

### Haptic Pen System
The haptic feedback system is the core of this project:

- **HapticPenController.cs**: Main controller for the haptic pen device
  - Manages Arduino serial communication (default: COM6, 115200 baud)
  - Calculates distance from pen tip to surfaces using raycasting
  - Implements distance smoothing for stable haptic feedback
  - Supports pressure-based interaction with three pressure states (Low/Medium/High)
  - Integrates with HapticPenGraspingSystem for object manipulation
  - Protocol: Sends `M{distance*1000}` commands, receives `P{pressure}|E{encoder}|D{distance}|B{button}|H{homeButton}` data
  - Key properties exposed: PressureReading, EncoderCount, RealDistance, CalculatedDistance, SmoothedDistance

- **ArduinoController.cs**: Simplified standalone Arduino interface
  - Can be used independently from HapticPenController
  - Provides event-driven architecture (OnPressureUpdated, OnEncoderUpdated, OnButtonUpdated)
  - Sends distance commands and custom commands to Arduino

### Experimental Control

- **JNDTestController.cs**: Just-Noticeable Difference (JND) psychophysics experiment controller
  - Manages multi-test experimental sessions with randomized order
  - Implements 2AFC (Two-Alternative Forced Choice) paradigm
  - Integrates with StaircaseProcedure for adaptive threshold estimation
  - Supports VR controller input (SteamVR actions) and keyboard fallback
  - State machine for stimulus presentation: ShowingFirstStimulus → BlinkingDot → MovingDot → WaitingAfterMovement → DelayBetweenStimuli → ShowingSecondStimulus → WaitingForResponse
  - Test mode available for debugging (skips pacing dot animations)
  - Key controls: Y/N for difference detection, VR left controller touchpad (Yes) and grip (No)

- **TestParameters**: Serializable configuration for individual tests
  - Defines stimulus ranges, reference values, staircase parameters
  - Supports both single and dual staircase sequences

### Visual Stimuli

- **GaussianSurface.cs**: Procedurally generated mesh for haptic-visual stimuli using Gaussian functions
  - Creates deformable surface using Gaussian function
  - SetHeight(float) method dynamically adjusts amplitude (hills/valleys)
  - Supports reverse mode (negates height for inverted perception studies)
  - Includes MeshCollider for pen raycasting and collision detection
  - Used in JND experiments to present variable height stimuli

- **NURBSSurface.cs**: Simplified plateau surface implementation for psychophysics experiments
  - **2D profile extrusion**: Profile defined in X-Y plane, uniform along Z-axis (Z-invariant surface)
  - **Rectangular-like profile**: Steep transitions create smoothed rectangular function (not bell curve)
  - **Key control parameters**:
    - Plateau Width: Controls size of flat top region (0.05-0.9 as fraction)
    - Transition Length: Controls horizontal span of transitions (0.01-0.2 as fraction, default 0.05)
    - Transition Steepness: Controls corner sharpness (1.0-10.0, default 5.0 for steep)
  - **Three-region profile**:
    - Flat plateau (center): Constant height at full amplitude
    - Steep smooth transitions (shoulders): Generalized smoothstep for near-vertical slopes
    - Flat tails (edges): Hardcoded Y=0, no oscillations or bouncing
  - **Key improvements**:
    - 4× steeper transitions (0.05 vs 0.2 default)
    - Flat stable tails with no exponential decay artifacts
    - Adjustable steepness for fine control
    - Monotonic descent guaranteed (no rising after descending)
  - **Bilateral symmetry**: Profile symmetric about centerline
  - **Height control**: SetHeight(float) method compatible with JNDTestController
  - MeshCollider included for haptic pen raycasting

### Data Collection

- **DataRecorder.cs**: Records experimental data to CSV files
  - Captures pen position, rotation, pressure, encoder counts, distances at configurable frequency (default 100Hz)
  - Saves to Application.persistentDataPath/PenRecordings/
  - CSV format includes: Timestamp, Position(XYZ), Rotation(XYZ), Pressure, RealDistance, EncoderCount, CalculatedDistance, SmoothedDistance, ButtonPressed, HomeButtonPressed
  - Can be controlled via keyboard (R key) or programmatically (StartRecording/StopRecording)
  - Provides events: OnRecordingStarted, OnRecordingStopped

### Utility Components

- **ObjectMover.cs**: Handles pacing dot movement and blinking for 2AFC trials
  - Coordinates stimulus timing between start/end positions
  - Supports callback-based async operations (BlinkTimes, MoveToDestination, TeleportToStart)

- **StaircaseProcedure**: Third-party adaptive testing library
  - Located in Assets/StaircaseProcedure/
  - Implements adaptive staircase methods for threshold estimation
  - Integrates with Python for live plotting (optional)
  - Access via StaircaseProcedure.SP singleton

## Unity Scenes

- **JND.unity** / **JND down.unity**: Main JND experimental scenes
- **CD ratio.unity**: CD (Constant Difference) ratio experiments
- **curve.unity**: Curve/surface testing
- **Surface test.unity**: Surface interaction testing
- **SampleScene.unity**: Development/testing scene

## Arduino Communication Protocol

**Unity → Arduino:**
- `M{value}` - Set target distance in millimeters (e.g., "M50.5")
- `S` - Stop/hold command (context-dependent)
- `TEST` - Test command for debugging

**Arduino → Unity:**
- Format: `P{pressure}|E{encoder}|D{distance}|B{button}|H{homeButton}`
- Example: `P25.3|E1500|D0.045|B0|H0`

## Development Workflow

### Building and Running
This is a Unity project. Open in Unity Editor (version can be found in ProjectSettings/ProjectVersion.txt):
1. Open Unity Hub → Add project from disk → Select this directory
2. Open any scene in Assets/Scenes/
3. Press Play in Unity Editor to run
4. For VR: Ensure SteamVR is running and headset is connected

### Switching Between Gaussian and NURBS Surfaces
The JNDTestController supports both surface types:
1. In the Unity Inspector, select the GameObject with JNDTestController
2. In "Surface Settings":
   - Set "Surface Type" to either "Gaussian" or "NURBS"
   - Assign the appropriate surface component to "Gaussian Surface" or "Nurbs Surface" field
   - Ensure "Use Surface" is checked
3. Create surfaces in scene:
   - **Gaussian**: GameObject → 3D Object → Plane → Add GaussianSurface component
   - **NURBS**: GameObject → Create Empty → Add MeshFilter, MeshRenderer, MeshCollider, and NURBSSurface components
4. Configure surface parameters in Inspector (amplitude, size, resolution, etc.)
5. The JNDTestController will automatically use the selected surface type during experiments

### Hardware Setup
- Connect Arduino to COM6 (or configure port in HapticPenController)
- Ensure OptiTrack or other tracking system is calibrated for pen tracking
- Verify SteamVR bindings for controller input

### Testing Without Hardware
- Set `isConnected = false` or handle connection failures gracefully
- Arduino connection errors won't crash the system (try-catch protection)
- VR input can fallback to keyboard: Y/N for responses, R for recording, Space for test commands

### Common Debug Controls
- **R** - Reconnect serial port
- **Space** - Test serial send
- **Y/N** - Yes/No responses in JND tests
- **M** - Move pacing dot (in ObjectMover)
- GUI overlays show real-time status in play mode

## Key Design Patterns

### Distance Calculation Logic
When both object and surface are detected:
- If object is closer than surface: `calculatedDistance = (d_s - d_o < d_s) ? (d_s - d_o) : d_s`
- Otherwise: use surface distance
- Grasped objects are excluded from distance calculations

### Response Transformation in JND
User responses are transformed based on offset direction to implement proper adaptive behavior:
- User detects difference + positive offset → tell staircase "detected" (decrease)
- User detects difference + negative offset → tell staircase "not detected" (increase toward zero)
- This ensures stimuli converge toward the perceptual threshold

### Async Stimulus Presentation
Uses coroutines and callbacks for precise timing control in 2AFC trials. The pacing dot provides visual cues for exploration timing.

## Data Output Locations

- **CSV recordings**: `Application.persistentDataPath/PenRecordings/`
  - Windows: `C:\Users\{Username}\AppData\LocalLow\DefaultCompany\optitrack_test\PenRecordings\`
- **Staircase results**: Configured via resultsPath in StaircaseProcedure component
- **Unity logs**: Unity Editor Console or Player.log

## Important Notes

- Serial port must be closed properly on application quit (handled in OnApplicationQuit/OnDestroy)
- Distance smoothing uses SmoothDamp for frame-rate independence
- All distance values in meters unless noted (Arduino protocol uses millimeters)
- VR action bindings may need verification in SteamVR Input settings if controller buttons don't work
