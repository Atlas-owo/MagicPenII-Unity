using UnityEngine;

public class MovementFollower : MonoBehaviour
{
    [Header("Target Object")]
    [SerializeField] private Transform targetObject;

    [Header("Y-Axis Control")]
    [SerializeField] private float yAxisRatio = 1f;

    [Header("Keyboard Ratio Switching")]
    [SerializeField] private bool enableKeyboardSwitching = true;
    [SerializeField] private float[] ratioPresets = { 1f, 3f, 5f, 4f, 5f };
    [SerializeField] private KeyCode switchRatioKey = KeyCode.C;
    [SerializeField] private KeyCode previousRatioKey = KeyCode.V;

    private int currentRatioIndex = 0; // Start at index 2 (ratio = 1f)

    [Header("Y Offset Control")]
    [SerializeField] private bool useAutomaticYOffset = true;
    [SerializeField] private float yOffsetMultiplier = -0.3f;
    [SerializeField] private float manualYOffset = 0f;

    [Header("Rotation Control")]
    [SerializeField] private bool followRotation = true;
    [SerializeField] private float rotationMultiplier = 1f;

    [Header("Offset (Optional)")]
    [SerializeField] private Vector3 positionOffset = Vector3.zero;
    [SerializeField] private Vector3 rotationOffset = Vector3.zero;

    private Vector3 initialPosition;
    private Vector3 targetInitialPosition;
    private Quaternion initialRotation;
    private Quaternion targetInitialRotation;

    // Property to get the current Y offset based on settings
    private float CurrentYOffset
    {
        get
        {
            return useAutomaticYOffset ? (yOffsetMultiplier * yAxisRatio) : manualYOffset;
        }
    }

    void Start()
    {
        // Store initial positions and rotations
        initialPosition = transform.position;
        initialRotation = transform.rotation;

        if (targetObject != null)
        {
            targetInitialPosition = targetObject.position;
            targetInitialRotation = targetObject.rotation;
        }
        else
        {
            Debug.LogWarning("Target object is not assigned to MovementFollower script on " + gameObject.name);
        }

        // Initialize ratio from presets if available
        if (ratioPresets != null && ratioPresets.Length > 0 && currentRatioIndex < ratioPresets.Length)
        {
            yAxisRatio = ratioPresets[currentRatioIndex];
        }
    }

    void Update()
    {
        if (targetObject == null) return;

        // Handle keyboard input for ratio switching
        if (enableKeyboardSwitching)
        {
            HandleKeyboardInput();
        }

        FollowTarget();

        if (followRotation)
        {
            FollowRotation();
        }
    }

    void HandleKeyboardInput()
    {
        if (ratioPresets == null || ratioPresets.Length == 0) return;

        bool switchForward = Input.GetKeyDown(switchRatioKey) && !Input.GetKey(previousRatioKey);
        bool switchBackward = Input.GetKeyDown(switchRatioKey) && Input.GetKey(previousRatioKey);

        if (switchForward)
        {
            SwitchToNextRatio();
        }
        else if (switchBackward)
        {
            SwitchToPreviousRatio();
        }
    }

    void SwitchToNextRatio()
    {
        if (ratioPresets == null || ratioPresets.Length == 0) return;

        currentRatioIndex = (currentRatioIndex + 1) % ratioPresets.Length;
        yAxisRatio = ratioPresets[currentRatioIndex];

        Debug.Log($"Switched to Y-Axis Ratio: {yAxisRatio} (Preset {currentRatioIndex + 1}/{ratioPresets.Length})");
    }

    void SwitchToPreviousRatio()
    {
        if (ratioPresets == null || ratioPresets.Length == 0) return;

        currentRatioIndex = (currentRatioIndex - 1 + ratioPresets.Length) % ratioPresets.Length;
        yAxisRatio = ratioPresets[currentRatioIndex];

        Debug.Log($"Switched to Y-Axis Ratio: {yAxisRatio} (Preset {currentRatioIndex + 1}/{ratioPresets.Length})");
    }

    void FollowTarget()
    {
        // Calculate the movement delta from the target's initial position
        Vector3 targetMovement = targetObject.position - targetInitialPosition;

        // Apply the same X and Z movement, but scale Y movement by the ratio
        Vector3 scaledMovement = new Vector3(
            targetMovement.x,
            targetMovement.y * yAxisRatio,
            targetMovement.z
        );

        // Create the Y offset vector
        Vector3 yOffset = new Vector3(0, CurrentYOffset, 0);

        // Set the new position based on initial position + scaled movement + Y offset + general position offset
        transform.position = initialPosition + scaledMovement + yOffset + positionOffset;
    }

    void FollowRotation()
    {
        // Calculate rotation difference from target's initial rotation
        Quaternion rotationDifference = targetObject.rotation * Quaternion.Inverse(targetInitialRotation);

        // Apply rotation multiplier by converting to euler angles
        Vector3 eulerDifference = rotationDifference.eulerAngles;

        // Normalize angles to -180 to 180 range for proper scaling
        if (eulerDifference.x > 180) eulerDifference.x -= 360;
        if (eulerDifference.y > 180) eulerDifference.y -= 360;
        if (eulerDifference.z > 180) eulerDifference.z -= 360;

        // Scale the rotation
        eulerDifference *= rotationMultiplier;

        // Apply the scaled rotation to our initial rotation
        Quaternion scaledRotation = Quaternion.Euler(eulerDifference);

        // Add rotation offset
        Quaternion offsetRotation = Quaternion.Euler(rotationOffset);

        transform.rotation = initialRotation * scaledRotation * offsetRotation;
    }

    // Public methods to adjust the ratio and rotation settings at runtime
    public void SetYAxisRatio(float newRatio)
    {
        yAxisRatio = newRatio;

        // Update current ratio index if it matches a preset
        if (ratioPresets != null)
        {
            for (int i = 0; i < ratioPresets.Length; i++)
            {
                if (Mathf.Approximately(ratioPresets[i], newRatio))
                {
                    currentRatioIndex = i;
                    break;
                }
            }
        }
    }

    public float GetYAxisRatio()
    {
        return yAxisRatio;
    }

    // New methods for keyboard ratio switching
    public void SetRatioPresets(float[] newPresets)
    {
        ratioPresets = newPresets;
        // Reset to first preset if current index is out of bounds
        if (ratioPresets != null && ratioPresets.Length > 0 && currentRatioIndex >= ratioPresets.Length)
        {
            currentRatioIndex = 0;
            yAxisRatio = ratioPresets[currentRatioIndex];
        }
    }

    public float[] GetRatioPresets()
    {
        return ratioPresets;
    }

    public void SetCurrentRatioIndex(int index)
    {
        if (ratioPresets != null && index >= 0 && index < ratioPresets.Length)
        {
            currentRatioIndex = index;
            yAxisRatio = ratioPresets[currentRatioIndex];
            Debug.Log($"Set Y-Axis Ratio: {yAxisRatio} (Preset {currentRatioIndex + 1}/{ratioPresets.Length})");
        }
    }

    public int GetCurrentRatioIndex()
    {
        return currentRatioIndex;
    }

    public void SetEnableKeyboardSwitching(bool enable)
    {
        enableKeyboardSwitching = enable;
    }

    public bool GetEnableKeyboardSwitching()
    {
        return enableKeyboardSwitching;
    }

    // Manual ratio switching methods (can be called from UI buttons, etc.)
    public void SwitchToNextRatioManual()
    {
        SwitchToNextRatio();
    }

    public void SwitchToPreviousRatioManual()
    {
        SwitchToPreviousRatio();
    }

    public void SetRotationMultiplier(float newMultiplier)
    {
        rotationMultiplier = newMultiplier;
    }

    public float GetRotationMultiplier()
    {
        return rotationMultiplier;
    }

    public void SetFollowRotation(bool shouldFollow)
    {
        followRotation = shouldFollow;
    }

    public bool GetFollowRotation()
    {
        return followRotation;
    }

    // New methods for Y offset control
    public void SetAutomaticYOffset(bool useAutomatic)
    {
        useAutomaticYOffset = useAutomatic;
    }

    public bool GetAutomaticYOffset()
    {
        return useAutomaticYOffset;
    }

    public void SetYOffsetMultiplier(float multiplier)
    {
        yOffsetMultiplier = multiplier;
    }

    public float GetYOffsetMultiplier()
    {
        return yOffsetMultiplier;
    }

    public void SetManualYOffset(float offset)
    {
        manualYOffset = offset;
    }

    public float GetManualYOffset()
    {
        return manualYOffset;
    }

    public float GetCurrentYOffset()
    {
        return CurrentYOffset;
    }

    // Method to reset reference positions and rotations (useful if you need to change the "zero point")
    public void ResetReferencePositions()
    {
        initialPosition = transform.position;
        initialRotation = transform.rotation;
        if (targetObject != null)
        {
            targetInitialPosition = targetObject.position;
            targetInitialRotation = targetObject.rotation;
        }
    }

    // Method to set a new target object
    public void SetTargetObject(Transform newTarget)
    {
        targetObject = newTarget;
        if (targetObject != null)
        {
            targetInitialPosition = targetObject.position;
            targetInitialRotation = targetObject.rotation;
        }
    }
}