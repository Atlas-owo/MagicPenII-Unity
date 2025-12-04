using UnityEngine;

/// <summary>
/// Main orchestrator for the haptic pen system.
/// Coordinates all subsystems: serial communication, distance calculation, pressure state, and grasping.
/// </summary>
public class HapticPenControllerRefactored : MonoBehaviour
{
    [Header("Pen Objects")]
    public Transform penTip;

    [Header("Subsystem References")]
    public ArduinoSerialManager serialManager;
    public DistanceCalculator distanceCalculator;
    public PressureStateManager pressureStateManager;
    public HapticPenGraspingSystem graspingSystem;
    public HapticPenDebugUI debugUI;

    [Header("Auto-Create Subsystems")]
    public bool autoCreateSubsystems = true;

    // Button state tracking
    private bool previousButtonPressed = false;

    // Public properties for external access (for compatibility with existing code)
    public float PressureReading => serialManager != null ? serialManager.PressureReading : 0f;
    public long EncoderCount => serialManager != null ? serialManager.EncoderCount : 0;
    public float RealDistance => serialManager != null ? serialManager.RealDistance : 0f;
    public float CalculatedDistance => distanceCalculator != null ? distanceCalculator.CalculatedDistance : 0f;
    public float SmoothedDistance => distanceCalculator != null ? distanceCalculator.SmoothedDistance : 0f;
    public bool ButtonPressed => serialManager != null && serialManager.ButtonPressed;
    public bool HomeButtonPressed => serialManager != null && serialManager.HomeButtonPressed;

    void Start()
    {
        InitializeSubsystems();
        InitializePenTip();
        SetupEventHandlers();
    }

    void Update()
    {
        // Calculate distance and send to Arduino
        if (distanceCalculator != null && serialManager != null)
        {
            float pressureOffset = pressureStateManager != null ? pressureStateManager.PressureDistanceOffset : 0f;
            float distance = distanceCalculator.Calculate(pressureOffset);
            serialManager.SendDistance(distance);
        }

        // Handle grasping input
        HandleGraspingInput();

        // Debug keyboard input
        HandleDebugInput();
    }

    private void InitializeSubsystems()
    {
        // Auto-find or create subsystems if not assigned
        if (autoCreateSubsystems)
        {
            // ArduinoSerialManager
            if (serialManager == null)
            {
                serialManager = GetComponent<ArduinoSerialManager>();
                if (serialManager == null)
                {
                    serialManager = gameObject.AddComponent<ArduinoSerialManager>();
                }
            }

            // DistanceCalculator
            if (distanceCalculator == null)
            {
                distanceCalculator = GetComponent<DistanceCalculator>();
                if (distanceCalculator == null)
                {
                    distanceCalculator = gameObject.AddComponent<DistanceCalculator>();
                }
            }

            // PressureStateManager
            if (pressureStateManager == null)
            {
                pressureStateManager = GetComponent<PressureStateManager>();
                if (pressureStateManager == null)
                {
                    pressureStateManager = gameObject.AddComponent<PressureStateManager>();
                }
            }

            // HapticPenGraspingSystem (optional - don't auto-create)
            if (graspingSystem == null)
            {
                graspingSystem = GetComponent<HapticPenGraspingSystem>();
                if (graspingSystem == null)
                {
                    graspingSystem = FindObjectOfType<HapticPenGraspingSystem>();
                }
            }

            // HapticPenDebugUI
            if (debugUI == null)
            {
                debugUI = GetComponent<HapticPenDebugUI>();
                if (debugUI == null)
                {
                    debugUI = gameObject.AddComponent<HapticPenDebugUI>();
                }
            }
        }

        // Configure subsystem references
        if (distanceCalculator != null)
        {
            distanceCalculator.penTip = penTip;
            distanceCalculator.SetGraspingSystem(graspingSystem);
        }

        if (graspingSystem != null && graspingSystem.penTip == null)
        {
            graspingSystem.penTip = penTip;
        }

        // Configure debug UI references
        if (debugUI != null)
        {
            debugUI.serialManager = serialManager;
            debugUI.distanceCalculator = distanceCalculator;
            debugUI.pressureStateManager = pressureStateManager;
            debugUI.graspingSystem = graspingSystem;
        }
    }

    private void InitializePenTip()
    {
        if (penTip == null)
        {
            // Create pen tip if not assigned
            GameObject tipObj = new GameObject("PenTip");
            tipObj.transform.SetParent(transform);
            tipObj.transform.localPosition = Vector3.forward * 0.08f; // Default pen length
            penTip = tipObj.transform;

            // Add a small sphere to visualize the tip
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.SetParent(tipObj.transform);
            sphere.transform.localScale = Vector3.one * 0.001f;
            sphere.transform.localPosition = Vector3.zero;

            // Update distance calculator reference
            if (distanceCalculator != null)
            {
                distanceCalculator.penTip = penTip;
            }

            // Update grasping system reference
            if (graspingSystem != null)
            {
                graspingSystem.penTip = penTip;
            }
        }
    }

    private void SetupEventHandlers()
    {
        // Connect pressure updates from serial manager to pressure state manager
        if (serialManager != null && pressureStateManager != null)
        {
            serialManager.OnPressureUpdated += pressureStateManager.UpdatePressure;
        }
    }

    private void HandleGraspingInput()
    {
        if (graspingSystem != null && serialManager != null)
        {
            graspingSystem.HandleGraspInput(serialManager.ButtonPressed, previousButtonPressed);
        }

        if (serialManager != null)
        {
            previousButtonPressed = serialManager.ButtonPressed;
        }
    }

    private void HandleDebugInput()
    {
        // Reconnect serial
        if (Input.GetKeyDown(KeyCode.R))
        {
            if (serialManager != null)
            {
                // Reset pressure offset on reconnect
                if (pressureStateManager != null)
                {
                    pressureStateManager.ResetOffset();
                }
                serialManager.Reconnect();
            }
        }

        // Test serial send
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (serialManager != null)
            {
                serialManager.SendTestCommand();
            }
        }
    }

    void OnDestroy()
    {
        // Cleanup event handlers
        if (serialManager != null && pressureStateManager != null)
        {
            serialManager.OnPressureUpdated -= pressureStateManager.UpdatePressure;
        }
    }

    /// <summary>
    /// Set the reference surface for distance calculation.
    /// </summary>
    public void SetSurface(Transform surface)
    {
        if (distanceCalculator != null)
        {
            distanceCalculator.surface = surface;
        }
    }

    /// <summary>
    /// Get the current surface reference.
    /// </summary>
    public Transform GetSurface()
    {
        return distanceCalculator != null ? distanceCalculator.surface : null;
    }
}
