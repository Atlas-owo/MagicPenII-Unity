using UnityEngine;

/// <summary>
/// Displays debug information for the haptic pen system in an OnGUI overlay.
/// </summary>
public class HapticPenDebugUI : MonoBehaviour
{
    [Header("References")]
    public ArduinoSerialManager serialManager;
    public DistanceCalculator distanceCalculator;
    public PressureStateManager pressureStateManager;
    public HapticPenGraspingSystem graspingSystem;

    [Header("UI Settings")]
    public bool showUI = true;
    public Rect uiArea = new Rect(10, 10, 300, 400);

    void OnGUI()
    {
        if (!showUI) return;

        GUILayout.BeginArea(uiArea);

        // Serial connection status
        if (serialManager != null)
        {
            GUILayout.Label($"Serial: {(serialManager.IsConnected ? "Connected" : "Disconnected")}");
        }
        else
        {
            GUILayout.Label("Serial: Not Assigned");
        }

        // Distance information
        if (distanceCalculator != null)
        {
            GUILayout.Label($"Distance (Raw): {distanceCalculator.CurrentDistance:F3}");
            GUILayout.Label($"Distance (Smoothed): {distanceCalculator.SmoothedDistance:F3}");
            GUILayout.Label($"Smoothing: {(distanceCalculator.enableSmoothing ? "ON" : "OFF")} (Time: {distanceCalculator.smoothingTime:F2}s)");
            GUILayout.Label($"d_s (Surface): {distanceCalculator.DistanceToSurface:F3}");
            GUILayout.Label($"d_o (Object): {distanceCalculator.DistanceToObject:F3}");
            GUILayout.Label($"Calculated: {distanceCalculator.CalculatedDistance:F3}");
        }
        else
        {
            GUILayout.Label("Distance Calculator: Not Assigned");
        }

        // Pressure information
        if (pressureStateManager != null)
        {
            GUILayout.Label($"Pressure Offset: {pressureStateManager.PressureDistanceOffset:F3}");
            GUILayout.Label($"Pressure: {pressureStateManager.CurrentPressure:F1}");
            GUILayout.Label($"Pressure State: {pressureStateManager.CurrentState}");
        }
        else if (serialManager != null)
        {
            GUILayout.Label($"Pressure: {serialManager.PressureReading:F1}");
        }

        // Arduino sensor data
        if (serialManager != null)
        {
            GUILayout.Label($"Encoder: {serialManager.EncoderCount}");
            GUILayout.Label($"Real Distance: {serialManager.RealDistance:F3}");
            GUILayout.Label($"Button: {(serialManager.ButtonPressed ? "Pressed" : "Released")}");
            GUILayout.Label($"Home Button: {(serialManager.HomeButtonPressed ? "Pressed" : "Released")}");
        }

        GUILayout.Space(10);

        // Grasping info
        if (graspingSystem != null)
        {
            graspingSystem.DrawGraspingGUI();
        }
        else
        {
            GUILayout.Label("=== Grasping System ===");
            GUILayout.Label("Not Connected");
        }

        GUILayout.Space(10);

        // Action buttons
        if (serialManager != null)
        {
            if (GUILayout.Button("Test Send (Space)"))
            {
                serialManager.SendTestCommand();
            }

            if (GUILayout.Button("Reconnect (R)"))
            {
                serialManager.Reconnect();
            }
        }

        if (pressureStateManager != null)
        {
            if (GUILayout.Button("Reset Pressure Offset"))
            {
                pressureStateManager.ResetOffset();
            }
        }

        GUILayout.EndArea();
    }
}
