using UnityEngine;
using System.Collections.Generic;

public class HapticPenGraspingSystem : MonoBehaviour
{
    [Header("Grasping Configuration")]
    public LayerMask graspableLayerMask = 1; // Which layers can be grasped
    public float graspDistance = 0.05f; // Maximum distance to grasp an object
    public bool usePhysicsJoint = false; // Use FixedJoint instead of manual positioning
    public float graspForce = 1000f; // Force for physics joint (if used)

    [Header("References")]
    public Transform penTip; // Reference to the pen tip transform

    [Header("Debug")]
    public bool showDebugRays = true;
    public bool logGraspingData = true;

    // Grasping state
    private Transform graspedObject = null;
    private Vector3 originalGraspedObjectPosition;
    private Quaternion originalGraspedObjectRotation;
    private Vector3 originalGraspedObjectScale;
    private Rigidbody graspedObjectRigidbody;
    private FixedJoint graspJoint;
    private List<Collider> graspedObjectColliders = new List<Collider>();

    // Y-axis only movement variables
    private float initialPenTipY;
    private Vector3 graspOffset;

    // Events for external systems to subscribe to
    public System.Action<Transform> OnObjectGrasped;
    public System.Action<Transform> OnObjectReleased;

    // Properties for external access
    public Transform GraspedObject => graspedObject;
    public bool IsGrasping => graspedObject != null;
    public List<Collider> GraspedObjectColliders => graspedObjectColliders;

    void Start()
    {
        if (penTip == null)
        {
            Debug.LogError("PenTip reference not assigned to HapticPenGraspingSystem!");
        }

        // Ensure pen tip has a rigidbody for physics joints if needed
        if (usePhysicsJoint && penTip != null && penTip.GetComponent<Rigidbody>() == null)
        {
            Rigidbody rb = penTip.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true; // Pen tip is controlled by script, not physics
        }
    }

    void Update()
    {
        UpdateGraspedObjectPosition();
        DrawDebugVisuals();
    }

    public void HandleGraspInput(bool buttonPressed, bool previousButtonPressed)
    {
        // Detect button press/release
        bool buttonJustPressed = buttonPressed && !previousButtonPressed;
        bool buttonJustReleased = !buttonPressed && previousButtonPressed;

        if (buttonJustPressed)
        {
            AttemptGrasp();
        }
        else if (buttonJustReleased)
        {
            ReleaseGrasp();
        }
    }

    public void AttemptGrasp()
    {
        if (graspedObject != null || penTip == null) return;

        // Find all colliders within grasp distance on the graspable layer
        Collider[] nearbyColliders = Physics.OverlapSphere(penTip.position, graspDistance, graspableLayerMask);

        if (nearbyColliders.Length > 0)
        {
            // Get the closest graspable object
            float closestDistance = float.MaxValue;
            Collider closestCollider = null;

            foreach (Collider col in nearbyColliders)
            {
                float distance = Vector3.Distance(penTip.position, col.ClosestPoint(penTip.position));
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestCollider = col;
                }
            }

            if (closestCollider != null)
            {
                GraspObject(closestCollider.transform);
            }
        }

        if (logGraspingData)
        {
            Debug.Log($"Grasp attempt - Found {nearbyColliders.Length} objects within {graspDistance} units");
        }
    }

    private void GraspObject(Transform objectToGrasp)
    {
        graspedObject = objectToGrasp;
        originalGraspedObjectPosition = graspedObject.position;
        originalGraspedObjectRotation = graspedObject.rotation;
        originalGraspedObjectScale = graspedObject.localScale;
        graspedObjectRigidbody = graspedObject.GetComponent<Rigidbody>();

        // Store initial pen tip Y position and calculate offset
        initialPenTipY = penTip.position.y;
        graspOffset = graspedObject.position - penTip.position;

        // Store all colliders of the grasped object and its children
        graspedObjectColliders.Clear();
        Collider[] allColliders = graspedObject.GetComponentsInChildren<Collider>();
        graspedObjectColliders.AddRange(allColliders);

        if (usePhysicsJoint && graspedObjectRigidbody != null)
        {
            // Use physics joint for more realistic grasping
            graspJoint = penTip.gameObject.AddComponent<FixedJoint>();
            graspJoint.connectedBody = graspedObjectRigidbody;
            graspJoint.breakForce = graspForce;
            graspJoint.breakTorque = graspForce;
        }

        // Trigger event
        OnObjectGrasped?.Invoke(graspedObject);

        if (logGraspingData)
        {
            Debug.Log($"Grasped object: {graspedObject.name} with {graspedObjectColliders.Count} colliders");
        }
    }

    public void ReleaseGrasp()
    {
        if (graspedObject == null) return;

        Transform releasedObject = graspedObject;

        if (graspJoint != null)
        {
            // Remove physics joint
            DestroyImmediate(graspJoint);
            graspJoint = null;
        }

        // Clear the stored colliders
        graspedObjectColliders.Clear();

        if (logGraspingData)
        {
            Debug.Log($"Released object: {graspedObject.name}");
        }

        // Trigger event before clearing reference
        OnObjectReleased?.Invoke(releasedObject);

        graspedObject = null;
        graspedObjectRigidbody = null;
    }

    private void UpdateGraspedObjectPosition()
    {
        if (graspedObject == null || penTip == null) return;

        // Don't update position if using physics joint (let physics handle it)
        if (usePhysicsJoint && graspJoint != null) return;

        // Calculate the Y movement of the pen tip since grasping started
        float penTipYMovement = penTip.position.y - initialPenTipY;

        // Apply only the Y movement to the grasped object
        Vector3 newPosition = originalGraspedObjectPosition;
        newPosition.y += penTipYMovement;

        graspedObject.position = newPosition;

        // Ensure the object maintains its original rotation and scale
        graspedObject.rotation = originalGraspedObjectRotation;
        graspedObject.localScale = originalGraspedObjectScale;
    }

    private void DrawDebugVisuals()
    {
        if (!showDebugRays || penTip == null) return;

        // Draw grasp sphere in debug
        Color graspColor = graspedObject != null ? Color.blue : Color.yellow;
        DrawDebugSphere(penTip.position, graspDistance, graspColor);
    }

    private void DrawDebugSphere(Vector3 center, float radius, Color color)
    {
        // Simple debug sphere using lines
        int segments = 16;
        float angleStep = 360f / segments;

        for (int i = 0; i < segments; i++)
        {
            float angle1 = i * angleStep * Mathf.Deg2Rad;
            float angle2 = (i + 1) * angleStep * Mathf.Deg2Rad;

            Vector3 point1 = center + new Vector3(Mathf.Cos(angle1), Mathf.Sin(angle1), 0) * radius;
            Vector3 point2 = center + new Vector3(Mathf.Cos(angle2), Mathf.Sin(angle2), 0) * radius;

            Debug.DrawLine(point1, point2, color);
        }
    }

    // Public method to check if a collider belongs to the grasped object
    public bool IsGraspedObjectCollider(Collider collider)
    {
        if (graspedObject == null || collider == null) return false;

        foreach (Collider graspedCollider in graspedObjectColliders)
        {
            if (collider == graspedCollider)
            {
                return true;
            }
        }
        return false;
    }

    // Force release from external scripts
    public void ForceRelease()
    {
        ReleaseGrasp();
    }

    // Get current Y movement for debug purposes
    public float GetYMovement()
    {
        if (graspedObject == null || penTip == null) return 0f;
        return penTip.position.y - initialPenTipY;
    }

    void OnDestroy()
    {
        ReleaseGrasp(); // Clean up any grasped objects
    }

    // GUI for debugging (can be disabled if not needed)
    public void DrawGraspingGUI()
    {
        GUILayout.Label("=== Grasping System ===");
        GUILayout.Label($"Grasped: {(graspedObject != null ? graspedObject.name : "None")}");
        GUILayout.Label($"Grasp Distance: {graspDistance:F3}");
        if (graspedObject != null)
        {
            GUILayout.Label($"Ignored Colliders: {graspedObjectColliders.Count}");
            GUILayout.Label($"Y Movement: {GetYMovement():F3}");
        }

        if (GUILayout.Button("Force Release Grasp"))
        {
            ForceRelease();
        }
    }
}