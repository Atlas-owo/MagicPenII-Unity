using UnityEngine;

public class ZAxisControlTester : MonoBehaviour
{
    [Header("Control Settings")]
    [Tooltip("The cube to be controlled")]
    public Transform targetCube;

    [Tooltip("The pen controller reference")]
    public HapticPenController penController;

    [Header("Ratio Control")]
    [Range(0.1f, 5.0f)]
    [Tooltip("Control/Display ratio - higher values = more sensitive")]
    public float controlDisplayRatio = 1.0f;

    [Header("Movement Constraints")]
    [Tooltip("Minimum Z position for the cube")]
    public float minZPosition = -2.0f;

    [Tooltip("Maximum Z position for the cube")]
    public float maxZPosition = 2.0f;

    [Header("Interaction Settings")]
    [Tooltip("Distance threshold for pen-cube interaction")]
    public float interactionDistance = 0.1f;

    [Tooltip("Use button press to enable dragging")]
    public bool requireButtonPress = true;

    // Private variables
    private Vector3 initialCubePosition;
    private Vector3 initialPenPosition;
    private bool isDragging = false;
    private float baseZPosition;

    void Start()
    {
        // Store initial positions
        if (targetCube != null)
        {
            initialCubePosition = targetCube.position;
            baseZPosition = initialCubePosition.z;
        }

        // Auto-find pen controller if not assigned
        if (penController == null)
        {
            penController = FindObjectOfType<HapticPenController>();
        }
    }

    void Update()
    {
        if (targetCube == null || penController == null) return;

        HandleInteraction();
        UpdateCubePosition();
    }

    void HandleInteraction()
    {
        Vector3 penTipPosition = penController.penTip.position;
        float distanceToCube = Vector3.Distance(penTipPosition,
targetCube.position);

        // Check if we should start/stop dragging
        if (requireButtonPress)
        {
            // Use button press from pen controller
            if (penController.buttonPressed && distanceToCube <= interactionDistance
 && !isDragging)
            {
                StartDragging(penTipPosition);
            }
            else if (!penController.buttonPressed && isDragging)
            {
                StopDragging();
            }
        }
        else
        {
            // Auto-drag when close enough
            if (distanceToCube <= interactionDistance && !isDragging)
            {
                StartDragging(penTipPosition);
            }
            else if (distanceToCube > interactionDistance * 2f && isDragging)
            {
                StopDragging();
            }
        }
    }

    void StartDragging(Vector3 penPosition)
    {
        isDragging = true;
        initialPenPosition = penPosition;
        baseZPosition = targetCube.position.z;

        Debug.Log("Started dragging cube");
    }

    void StopDragging()
    {
        isDragging = false;
        Debug.Log("Stopped dragging cube");
    }

    void UpdateCubePosition()
    {
        if (!isDragging) return;

        Vector3 currentPenPosition = penController.penTip.position;

        // Calculate Z-axis movement from initial pen position
        float penZMovement = currentPenPosition.z - initialPenPosition.z;

        // Apply the control/display ratio
        float scaledMovement = penZMovement * controlDisplayRatio;

        // Calculate new cube Z position
        float newZPosition = baseZPosition + scaledMovement;

        // Clamp to constraints
        newZPosition = Mathf.Clamp(newZPosition, minZPosition, maxZPosition);

        // Update cube position (only Z-axis changes)
        Vector3 newPosition = targetCube.position;
        newPosition.z = newZPosition;
        targetCube.position = newPosition;
    }

    // Debug visualization
    void OnDrawGizmos()
    {
        if (targetCube != null)
        {
            // Draw interaction sphere
            Gizmos.color = isDragging ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(targetCube.position, interactionDistance);

            // Draw movement constraints
            Gizmos.color = Color.red;
            Vector3 minPos = initialCubePosition;
            minPos.z = minZPosition;
            Vector3 maxPos = initialCubePosition;
            maxPos.z = maxZPosition;

            Gizmos.DrawWireCube(minPos, Vector3.one * 0.1f);
            Gizmos.DrawWireCube(maxPos, Vector3.one * 0.1f);
            Gizmos.DrawLine(minPos, maxPos);
        }
    }

    // GUI for runtime testing
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(Screen.width - 300, 10, 290, 200));

        GUILayout.Label("=== Z-Axis Control Test ===");
        GUILayout.Label($"Dragging: {(isDragging ? "YES" : "NO")}");
        GUILayout.Label($"Control Ratio: {controlDisplayRatio:F2}");

        GUILayout.Space(10);
        GUILayout.Label("Runtime Controls:");

        // Runtime ratio adjustment
        controlDisplayRatio = GUILayout.HorizontalSlider(controlDisplayRatio, 0.1f,
5.0f);
        GUILayout.Label($"Ratio: {controlDisplayRatio:F2}");

        if (GUILayout.Button("Reset Cube Position"))
        {
            if (targetCube != null)
            {
                targetCube.position = initialCubePosition;
                baseZPosition = initialCubePosition.z;
            }
        }

        GUILayout.EndArea();
    }
}
