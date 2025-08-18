using System;
using System.IO.Ports;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class ConstrainedHapticPenController : MonoBehaviour
{
    [Header("Serial Communication")]
    public string portName = "COM3"; // Change to your Arduino port
    public int baudRate = 115200;
    private SerialPort serialPort;
    private bool isConnected = false;

    [Header("Pen Objects")]
    public Transform penBase; // The base of the pen (this GameObject)
    public Transform penTip; // Child object representing the pen tip
    public Transform surface; // The surface to measure distance to

    [Header("Distance Measurement")]
    public LayerMask surfaceLayerMask = 1; // Which layers count as surface
    public float maxDistance = 100f; // Maximum raycast distance
    public float distanceOffset = 0f; // Offset to add to measured distance
    public float rayOriginOffset = 0.02f;

    [Header("Multiple Objects")]
    public List<Transform> surfaceObjects = new List<Transform>(); // List of all objects to check distance to
    public bool includeAllCollidersInScene = false; // If true, will check against all colliders

    [Header("Grasping System")]
    public LayerMask graspableLayerMask = 1; // Which layers can be grasped
    public float graspDistance = 0.05f; // Maximum distance to grasp an object
    public bool usePhysicsJoint = false; // Use FixedJoint instead of parenting
    public float graspForce = 1000f; // Force for physics joint (if used)

    // Grasping state
    private Transform graspedObject = null;
    private bool previousButtonPressed = false;
    private Vector3 originalGraspedObjectPosition;
    private Quaternion originalGraspedObjectRotation;
    private Vector3 originalGraspedObjectScale; // Store original scale
    private Vector3 graspStartPenTipPosition; // Store initial pen tip position when grasping starts
    private Rigidbody graspedObjectRigidbody;
    private FixedJoint graspJoint;
    private List<Collider> graspedObjectColliders = new List<Collider>(); // Store all colliders of grasped object

    [Header("Pen Control")]
    public float penLength = 5f; // Current pen length
    public float minPenLength = 1f;
    public float maxPenLength = 10f;
    public float lengthChangeSpeed = 2f; // How fast the pen changes length

    [Header("Pressure Response")]
    public float pressureThreshold1 = 10f; // Lower threshold - normal operation below this
    public float pressureThreshold2 = 30f; // Upper threshold - shorten distance above this
    public float pressureSensitivity = 0.01f; // How much pressure affects pen length
    public float distanceShorteningAmount = 0.5f; // Rate of distance shortening per second when pressure > threshold2
    public bool invertPressureResponse = false; // If true, higher pressure = longer pen

    [Header("Debug")]
    public bool showDebugRays = true;
    public bool logSerialData = true;
    public bool logDistanceData = false;
    public bool logGraspingData = true;

    // Parsed sensor data
    private float pressureReading = 0f;
    private long encoderCount = 0;
    public bool buttonPressed = false;

    // Distance tracking
    private float currentDistance = 0f;
    private float targetPenLength = 5f;
    private float pressureDistanceOffset = 0f; // Cumulative offset from pressure

    // Pressure state tracking
    private enum PressureState
    {
        Low,    // Below threshold1
        Medium, // Between threshold1 and threshold2
        High    // Above threshold2
    }
    private PressureState currentPressureState = PressureState.Low;

    // Timing
    private float lastDistanceSendTime = 0f;
    private float distanceSendInterval = 0.05f; // Send distance every 50ms

    void Start()
    {
        InitializePen();
        ConnectToArduino();
        StartCoroutine(SerialReadCoroutine());
    }

    void Update()
    {
        MeasureDistanceToSurface();
        SendDistanceToArduino();
        UpdatePenLength();
        HandleGrasping();
        UpdateGraspedObjectPosition(); // Handle constrained grasped object movement

        // Debug input
        if (Input.GetKeyDown(KeyCode.R))
        {
            ReconnectSerial();
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            TestSerialSend();
        }
    }

    void InitializePen()
    {
        if (penTip == null)
        {
            // Create pen tip if not assigned
            GameObject tipObj = new GameObject("PenTip");
            tipObj.transform.SetParent(transform);
            tipObj.transform.localPosition = Vector3.forward * penLength;
            penTip = tipObj.transform;

            // Add a small sphere to visualize the tip
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.SetParent(tipObj.transform);
            sphere.transform.localScale = Vector3.one * 0.001f;
            sphere.transform.localPosition = Vector3.zero;
        }

        // Ensure pen tip has a rigidbody for physics joints if needed
        if (usePhysicsJoint && penTip.GetComponent<Rigidbody>() == null)
        {
            Rigidbody rb = penTip.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true; // Pen tip is controlled by script, not physics
        }

        if (surface == null)
        {
            Debug.LogWarning("Surface not assigned! Please assign a surface Transform.");
        }

        targetPenLength = penLength;
    }

    void ConnectToArduino()
    {
        try
        {
            serialPort = new SerialPort(portName, baudRate);
            serialPort.ReadTimeout = 100;
            serialPort.WriteTimeout = 100;
            serialPort.Open();
            isConnected = true;
            Debug.Log($"Connected to Arduino on {portName}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to connect to Arduino: {e.Message}");
            isConnected = false;
        }
    }

    void HandleGrasping()
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

        previousButtonPressed = buttonPressed;
    }

    void AttemptGrasp()
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

    void GraspObject(Transform objectToGrasp)
    {
        graspedObject = objectToGrasp;
        originalGraspedObjectPosition = graspedObject.position;
        originalGraspedObjectRotation = graspedObject.rotation;
        originalGraspedObjectScale = graspedObject.localScale; // Store original scale
        graspStartPenTipPosition = penTip.position; // Store initial pen tip position
        graspedObjectRigidbody = graspedObject.GetComponent<Rigidbody>();

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
        else
        {
            // Simple parenting approach with scale preservation
            graspedObject.SetParent(penTip);
            // Restore original scale to prevent distortion
            graspedObject.localScale = originalGraspedObjectScale;
        }

        if (logGraspingData)
        {
            Debug.Log($"Grasped object: {graspedObject.name} with {graspedObjectColliders.Count} colliders");
        }
    }

    void UpdateGraspedObjectPosition()
    {
        if (graspedObject != null && !usePhysicsJoint)
        {
            // Calculate Y offset from pen tip movement
            float yOffset = penTip.position.y - graspStartPenTipPosition.y;

            // Apply only Y-axis movement to grasped object
            Vector3 newPosition = originalGraspedObjectPosition;
            newPosition.y += yOffset;
            graspedObject.position = newPosition;

            // Keep original rotation and scale
            graspedObject.rotation = originalGraspedObjectRotation;
            graspedObject.localScale = originalGraspedObjectScale;
        }
    }

    void ReleaseGrasp()
    {
        if (graspedObject == null) return;

        if (graspJoint != null)
        {
            // Remove physics joint
            DestroyImmediate(graspJoint);
            graspJoint = null;
        }
        else
        {
            // Remove from parent and restore original scale
            graspedObject.SetParent(null);
            graspedObject.localScale = originalGraspedObjectScale;
        }

        // Clear the stored colliders
        graspedObjectColliders.Clear();

        if (logGraspingData)
        {
            Debug.Log($"Released object: {graspedObject.name}");
        }

        graspedObject = null;
        graspedObjectRigidbody = null;
    }

    void MeasureDistanceToSurface()
    {
        if (penTip == null) return;

        // Cast a ray from pen tip towards the surface
        Vector3 rayOrigin = penTip.position - (penTip.forward * rayOriginOffset);
        Vector3 rayDirection = penTip.forward; // Assuming pen points forward

        float measuredDistance;

        // Use RaycastAll to get all hits and filter out grasped objects
        RaycastHit[] allHits = Physics.RaycastAll(rayOrigin, rayDirection, maxDistance, surfaceLayerMask);

        RaycastHit validHit = new RaycastHit();
        bool foundValidHit = false;
        float closestValidDistance = maxDistance;

        foreach (RaycastHit hit in allHits)
        {
            // Check if this hit is from a grasped object
            bool isGraspedObject = false;

            if (graspedObject != null)
            {
                // Check if the hit collider is one of the grasped object's colliders
                foreach (Collider graspedCollider in graspedObjectColliders)
                {
                    if (hit.collider == graspedCollider)
                    {
                        isGraspedObject = true;
                        break;
                    }
                }
            }

            // If this hit is not from a grasped object and is closer than our current best hit
            if (!isGraspedObject && hit.distance < closestValidDistance)
            {
                validHit = hit;
                foundValidHit = true;
                closestValidDistance = hit.distance;
            }
        }

        if (foundValidHit)
        {
            measuredDistance = validHit.distance + distanceOffset;

            if (showDebugRays)
            {
                Debug.DrawRay(rayOrigin, rayDirection * validHit.distance, Color.green);
                // Draw a line to show ignored grasped object hits
                if (graspedObject != null)
                {
                    Debug.DrawRay(rayOrigin, rayDirection * 0.1f, Color.cyan); // Short cyan line to show grasped object is ignored
                }
            }
        }
        else
        {
            measuredDistance = maxDistance + distanceOffset;

            if (showDebugRays)
            {
                Debug.DrawRay(rayOrigin, rayDirection * maxDistance, Color.red);
            }
        }

        // Update pressure offset based on current state
        if (currentPressureState == PressureState.High)
        {
            // Increase the offset while pressure is high
            pressureDistanceOffset += distanceShorteningAmount * Time.deltaTime;
        }
        else if (currentPressureState == PressureState.Low)
        {
            // Gradually reset the offset when pressure is low
            pressureDistanceOffset = Mathf.Max(0, pressureDistanceOffset - distanceShorteningAmount * Time.deltaTime * 2f);
        }
        // When pressure is Medium, maintain current offset (no change)

        // Apply the cumulative offset to the measured distance
        currentDistance = Mathf.Max(0, measuredDistance - pressureDistanceOffset);

        // Draw grasp sphere in debug
        if (showDebugRays)
        {
            Color graspColor = graspedObject != null ? Color.blue : Color.yellow;
            DrawDebugSphere(penTip.position, graspDistance, graspColor);
        }

        if (logDistanceData)
        {
            string graspedInfo = graspedObject != null ? $" (Ignoring grasped: {graspedObject.name})" : "";
            Debug.Log($"Distance: {currentDistance:F2} (Measured: {measuredDistance:F2}, Offset: {pressureDistanceOffset:F2}){graspedInfo}");
        }
    }

    void DrawDebugSphere(Vector3 center, float radius, Color color)
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

    public static int DistanceToEncoder(float value)
    {
        value -= 0.045f;
        if (value < 0)
        {
            value = -value;
        }

        float fromMin = 0.00f;
        float fromMax = 0.06f;
        float toMin = 10f;
        float toMax = 3000f;
        float t = (value - fromMin) / (fromMax - fromMin);
        float mappedFloat = toMin + (toMax - toMin) * t;

        return Mathf.FloorToInt(mappedFloat);
    }

    void SendDistanceToArduino()
    {
        if (!isConnected || Time.time - lastDistanceSendTime < distanceSendInterval)
            return;

        try
        {
            string command;

            // Check pressure state and send appropriate command
            switch (currentPressureState)
            {
                case PressureState.Medium:
                    // Between threshold1 and threshold2 - send 'S'
                    command = "S\n";
                    break;

                case PressureState.Low:
                case PressureState.High:
                default:
                    // Below threshold1 or above threshold2 - send normal distance
                    command = $"M{DistanceToEncoder(currentDistance)}\n";
                    Debug.Log($"target:{DistanceToEncoder(currentDistance)}, current:{encoderCount}");
                    break;
            }

            serialPort.Write(command);
            lastDistanceSendTime = Time.time;

            if (logSerialData)
            {
                Debug.Log($"Sent to Arduino: {command.Trim()} (Pressure State: {currentPressureState})");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error sending to Arduino: {e.Message}");
            isConnected = false;
        }
    }

    IEnumerator SerialReadCoroutine()
    {
        while (true)
        {
            if (isConnected)
            {
                try
                {
                    // Clear buffer by reading all available data
                    while (serialPort.BytesToRead > 0)
                    {
                        string data = serialPort.ReadLine();
                        // Only process the last (most recent) line
                        if (serialPort.BytesToRead == 0)
                        {
                            ParseSensorData(data);
                            if (logSerialData) Debug.Log($"Received: {data}");
                        }
                    }
                }
                catch (TimeoutException) { }
                catch (Exception e)
                {
                    Debug.LogError($"Error reading from Arduino: {e.Message}");
                    isConnected = false;
                }
            }
            yield return new WaitForSeconds(0.01f); // Reduced frequency
        }
    }

    void ParseSensorData(string data)
    {
        if (string.IsNullOrEmpty(data)) return;

        if (logSerialData)
        {
            Debug.Log($"Received from Arduino: {data}");
        }

        // Parse format: "P123|E123|B1"
        try
        {
            string[] parts = data.Split('|');

            foreach (string part in parts)
            {
                if (part.StartsWith("P"))
                {
                    if (float.TryParse(part.Substring(1), out float pressure))
                    {
                        pressureReading = pressure;
                        UpdatePressureState();
                    }
                }
                else if (part.StartsWith("E"))
                {
                    if (long.TryParse(part.Substring(1), out long encoder))
                    {
                        encoderCount = encoder;
                    }
                }
                else if (part.StartsWith("B"))
                {
                    if (int.TryParse(part.Substring(1), out int button))
                    {
                        buttonPressed = (button == 0);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error parsing sensor data '{data}': {e.Message}");
        }
    }

    void UpdatePressureState()
    {
        PressureState previousState = currentPressureState;

        if (pressureReading < pressureThreshold1)
        {
            currentPressureState = PressureState.Low;
        }
        else if (pressureReading >= pressureThreshold1 && pressureReading <= pressureThreshold2)
        {
            currentPressureState = PressureState.Medium;
        }
        else // pressureReading > pressureThreshold2
        {
            currentPressureState = PressureState.High;
        }

        // Log state changes
        if (previousState != currentPressureState && logSerialData)
        {
            Debug.Log($"Pressure state changed from {previousState} to {currentPressureState} (Pressure: {pressureReading:F1})");
        }
    }

    void UpdatePenLength()
    {
        if (penTip == null) return;

        // Smoothly interpolate to target length
        penLength = Mathf.MoveTowards(penLength, targetPenLength, lengthChangeSpeed * Time.deltaTime);

        // Update pen tip position
        penTip.localPosition = Vector3.forward * penLength;
    }

    void TestSerialSend()
    {
        if (isConnected)
        {
            try
            {
                serialPort.Write("TEST\n");
                Debug.Log("Sent test command to Arduino");
            }
            catch (Exception e)
            {
                Debug.LogError($"Error sending test command: {e.Message}");
            }
        }
    }

    void ReconnectSerial()
    {
        if (isConnected)
        {
            serialPort.Close();
            isConnected = false;
        }

        // Reset pressure offset on reconnect
        pressureDistanceOffset = 0f;

        ConnectToArduino();
    }

    void OnApplicationQuit()
    {
        ReleaseGrasp(); // Clean up any grasped objects
        if (isConnected && serialPort != null)
        {
            serialPort.Close();
        }
    }

    void OnDestroy()
    {
        ReleaseGrasp(); // Clean up any grasped objects
        if (isConnected && serialPort != null)
        {
            serialPort.Close();
        }
    }

    // GUI for debugging
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 300, 360));

        GUILayout.Label($"Serial: {(isConnected ? "Connected" : "Disconnected")}");
        GUILayout.Label($"Distance: {currentDistance:F2}");
        GUILayout.Label($"Pressure Offset: {pressureDistanceOffset:F3}");
        GUILayout.Label($"Pen Length: {penLength:F2}");
        GUILayout.Label($"Pressure: {pressureReading:F1}");
        GUILayout.Label($"Pressure State: {currentPressureState}");
        GUILayout.Label($"Encoder: {encoderCount}");
        GUILayout.Label($"Button: {(buttonPressed ? "Pressed" : "Released")}");

        // Grasping info
        GUILayout.Space(10);
        GUILayout.Label("=== Grasping ===");
        GUILayout.Label($"Grasped: {(graspedObject != null ? graspedObject.name : "None")}");
        GUILayout.Label($"Grasp Distance: {graspDistance:F3}");
        if (graspedObject != null)
        {
            GUILayout.Label($"Ignored Colliders: {graspedObjectColliders.Count}");
            GUILayout.Label($"Y Offset: {(penTip.position.y - graspStartPenTipPosition.y):F3}");
        }

        GUILayout.Space(10);

        if (GUILayout.Button("Test Send (Space)"))
        {
            TestSerialSend();
        }

        if (GUILayout.Button("Force Release Grasp"))
        {
            ReleaseGrasp();
        }

        if (GUILayout.Button("Reset Pressure Offset"))
        {
            pressureDistanceOffset = 0f;
        }

        GUILayout.EndArea();
    }
}