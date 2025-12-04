using System;
using UnityEngine;

/// <summary>
/// Manages pressure state transitions and calculates pressure-based distance offsets.
/// </summary>
public class PressureStateManager : MonoBehaviour
{
    [Header("Pressure Thresholds")]
    public float pressureThreshold1 = 10f; // Lower threshold - normal operation below this
    public float pressureThreshold2 = 30f; // Upper threshold - shorten distance above this

    [Header("Pressure Response")]
    public float distanceShorteningAmount = 0.5f; // Distance offset when pressure > threshold2

    [Header("Debug")]
    public bool logStateChanges = false;

    // Pressure states
    public enum PressureState
    {
        Low,    // Below threshold1
        Medium, // Between threshold1 and threshold2
        High    // Above threshold2
    }

    // Current state
    private PressureState currentState = PressureState.Low;
    private float currentPressure = 0f;
    private float pressureDistanceOffset = 0f;

    // Public properties
    public PressureState CurrentState => currentState;
    public float CurrentPressure => currentPressure;
    public float PressureDistanceOffset => pressureDistanceOffset;

    // Events
    public event Action<PressureState, PressureState> OnStateChanged; // (oldState, newState)

    /// <summary>
    /// Update the pressure value and recalculate state.
    /// </summary>
    /// <param name="pressure">New pressure reading</param>
    public void UpdatePressure(float pressure)
    {
        currentPressure = pressure;
        UpdateState();
        UpdateDistanceOffset();
    }

    /// <summary>
    /// Reset the pressure offset to zero.
    /// </summary>
    public void ResetOffset()
    {
        pressureDistanceOffset = 0f;
    }

    private void UpdateState()
    {
        PressureState previousState = currentState;

        if (currentPressure < pressureThreshold1)
        {
            currentState = PressureState.Low;
        }
        else if (currentPressure >= pressureThreshold1 && currentPressure <= pressureThreshold2)
        {
            currentState = PressureState.Medium;
        }
        else // currentPressure > pressureThreshold2
        {
            currentState = PressureState.High;
        }

        // Fire event and log on state change
        if (previousState != currentState)
        {
            OnStateChanged?.Invoke(previousState, currentState);

            if (logStateChanges)
            {
                Debug.Log($"[PressureStateManager] State changed from {previousState} to {currentState} (Pressure: {currentPressure:F1})");
            }
        }
    }

    private void UpdateDistanceOffset()
    {
        if (currentState == PressureState.High)
        {
            pressureDistanceOffset = distanceShorteningAmount;
        }
        else if (currentState == PressureState.Low)
        {
            pressureDistanceOffset = 0f;
        }
        // Medium state maintains the current offset
    }
}
