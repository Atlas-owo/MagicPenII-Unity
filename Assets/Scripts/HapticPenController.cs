using System;
using System.IO.Ports;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class HapticPenController : MonoBehaviour
{
    [Header("Serial Communication")]
    public string portName = "COM6"; // Change to your Arduino port
    public int baudRate = 115200;
    private SerialPort serialPort;
    private bool isConnected = false;

    [Header("Pen Objects")]
    public Transform penBase; // The base of the pen (this GameObject)
    public Transform penTip; // Child object representing the pen tip
    public Transform surface; // The surface to measure distance to

    [Header("Distance Measurement")]
    public LayerMask surfaceLayerMask = 1; // Which layers count as surface
    public float maxDistance = 1f; // Maximum raycast distance
    [Range(-1.0f, 1.0f)]
    public float distanceOffset = 0f; // Offset to add to measured distance
    [Range(0.01f, 1.0f)]
    public float rayOriginOffset = 0.05f;

    public float valueOffset = 0.05f;
    public int maxEncoderCount = 3000;

    [Header("Multiple Objects")]
    public List<Transform> surfaceObjects = new List<Transform>(); // List of all objects to check distance to
    public bool includeAllCollidersInScene = false; // If true, will check against all colliders

    [Header("Grasping System Reference")]
    public HapticPenGraspingSystem graspingSystem; // Reference to the grasping system

    [Header("Pen Control")]
    public float penLength = 0.08f; // Current pen length
    public float minPenLength = 1f;
    public float maxPenLength = 10f;
    public float lengthChangeSpeed = 2f; // How fast the pen changes length

    [Header("Pressure Response")]
    public float pressureThreshold1 = 10f; // Lower threshold - normal operation below this
    public float pressureThreshold2 = 30f; // Upper threshold - shorten distance above this
    public float pressureSensitivity = 0.01f; // How much pressure affects pen length
    public float distanceShorteningAmount = 0.5f; // Rate of distance shortening per second when pressure > threshold2
    public bool invertPressureResponse = false; // If true, higher pressure = longer pen

    [Header("Distance Smoothing")]
    public bool enableDistanceSmoothing = true; // Enable/disable smoothing
    public float smoothingFactor = 0.1f; // Lower = more smoothing (0-1)
    [Range(0.01f, 1.0f)]
    public float smoothingTime = 0.1f; // Time to reach target (seconds)

    [Header("Debug")]
    public bool showDebugRays = true;
    public bool logSerialData = true;
    public bool logDistanceData = false;

    // Parsed sensor data
    private float pressureReading = 0f;
    private long encoderCount = 0;
    private float realDistance = 0f; // D value - real distance that the pen has extended
    public bool buttonPressed = false;
    public bool homeButtonPressed = false;
    private bool previousButtonPressed = false;

    // Public properties for data recording access
    public float PressureReading => pressureReading;
    public long EncoderCount => encoderCount;
    public float RealDistance => realDistance;
    public float CalculatedDistance => calculatedDistance;
    public float SmoothedDistance => smoothedDistance;

    // Distance tracking
    private float currentDistance = 0f;
    private float smoothedDistance = 0f; // Smoothed version for Arduino communication
    private float smoothVelocity = 0f; // Velocity for SmoothDamp
    private float targetPenLength = 5f;
    private float pressureDistanceOffset = 0f; // Cumulative offset from pressure
    
    // New distance measurement variables
    private float distanceToSurface = 0f; // d_s
    private float distanceToObject = 0f;  // d_o
    private float calculatedDistance = 0f; // Final distance sent to Arduino

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
        InitializeGraspingSystem();
        ConnectToArduino();
        StartCoroutine(SerialReadCoroutine());
    }

    void Update()
    {
        CalculateDistanceToArduino();
        SendDistanceToArduino();
        UpdatePenLength();
        HandleGraspingInput();

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

        if (surface == null)
        {
            Debug.LogWarning("Surface not assigned! Please assign a surface Transform.");
        }

        targetPenLength = penLength;
    }

    void InitializeGraspingSystem()
    {
        // Auto-find grasping system if not assigned
        if (graspingSystem == null)
        {
            graspingSystem = GetComponent<HapticPenGraspingSystem>();
            if (graspingSystem == null)
            {
                graspingSystem = FindObjectOfType<HapticPenGraspingSystem>();
            }
        }

        // Set pen tip reference in grasping system
        if (graspingSystem != null && graspingSystem.penTip == null)
        {
            graspingSystem.penTip = penTip;
        }

        if (graspingSystem == null)
        {
            Debug.LogWarning("HapticPenGraspingSystem not found! Grasping functionality will be disabled.");
        }
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

    void HandleGraspingInput()
    {
        if (graspingSystem != null)
        {
            graspingSystem.HandleGraspInput(buttonPressed, previousButtonPressed);
        }
        previousButtonPressed = buttonPressed;
    }

    void CalculateDistanceToArduino()
    {
        if (penTip == null) return;

        // Cast a ray from pen tip towards the surface
        Vector3 rayOrigin = penTip.position - (penTip.forward * rayOriginOffset);
        Vector3 rayDirection = penTip.forward; // Assuming pen points forward

        // Reset distance values
        distanceToSurface = maxDistance;
        distanceToObject = maxDistance;

        // Use RaycastAll to get all hits
        RaycastHit[] allHits = Physics.RaycastAll(rayOrigin, rayDirection, maxDistance, surfaceLayerMask);

        RaycastHit surfaceHit = new RaycastHit();
        RaycastHit objectHit = new RaycastHit();
        bool foundSurface = false;
        bool foundObject = false;
        float closestSurfaceDistance = maxDistance;
        float closestObjectDistance = maxDistance;

        foreach (RaycastHit hit in allHits)
        {
            // Check if this hit is from a grasped object - skip grasped objects
            bool isGraspedObject = false;
            if (graspingSystem != null && graspingSystem.IsGrasping)
            {
                if (graspingSystem.IsGraspedObjectCollider(hit.collider))
                {
                    isGraspedObject = true;
                }
            }

            if (isGraspedObject) continue;

            // Check if this hit is from the reference surface
            bool isSurface = false;
            if (surface != null && hit.transform == surface)
            {
                isSurface = true;
            }

            // If it's the surface and closer than previous surface hits
            if (isSurface && hit.distance < closestSurfaceDistance)
            {
                surfaceHit = hit;
                foundSurface = true;
                closestSurfaceDistance = hit.distance;
            }
            // If it's an object (not surface) and closer than previous object hits
            else if (!isSurface && hit.distance < closestObjectDistance)
            {
                objectHit = hit;
                foundObject = true;
                closestObjectDistance = hit.distance;
            }
        }

        // Update distance values
        if (foundSurface)
        {
            distanceToSurface = surfaceHit.distance + distanceOffset;
        }
        
        if (foundObject)
        {
            distanceToObject = objectHit.distance + distanceOffset;
        }

        // Calculate the final distance based on your logic
        if (foundObject && foundSurface)
        {
            // Both object and surface found
            float d_s = distanceToSurface;
            float d_o = distanceToObject;

            if (d_o < d_s) // Object is closer than surface
            {
                float difference = d_s - d_o;
                if (d_s <= difference)
                {
                    calculatedDistance = difference;
                }
                else
                {
                    calculatedDistance = d_s;
                }
            }
            else
            {
                // Object is farther than surface, use surface distance
                calculatedDistance = d_s;
            }
        }
        else if (foundSurface)
        {
            // Only surface found
            calculatedDistance = distanceToSurface;
        }
        else
        {
            // No valid hits found
            calculatedDistance = maxDistance + distanceOffset;
        }

        // Apply pressure offset
        if (currentPressureState == PressureState.High)
        {
            pressureDistanceOffset = distanceShorteningAmount;
        }
        else if (currentPressureState == PressureState.Low)
        {
            pressureDistanceOffset = 0;
        }

        // Apply the cumulative offset to the calculated distance
        currentDistance = Mathf.Max(0, calculatedDistance - pressureDistanceOffset);

        // Apply distance smoothing for Arduino communication
        if (enableDistanceSmoothing)
        {
            // Use SmoothDamp for frame-rate independent smoothing
            smoothedDistance = Mathf.SmoothDamp(smoothedDistance, currentDistance, ref smoothVelocity, smoothingTime);
        }
        else
        {
            // No smoothing - use raw distance
            smoothedDistance = currentDistance;
        }

        // Debug visualization
        if (showDebugRays)
        {
            if (foundSurface)
            {
                Debug.DrawRay(rayOrigin, rayDirection * surfaceHit.distance, Color.blue); // Blue for surface
            }
            if (foundObject)
            {
                Debug.DrawRay(rayOrigin, rayDirection * objectHit.distance, Color.yellow); // Yellow for objects
            }
            if (!foundSurface && !foundObject)
            {
                Debug.DrawRay(rayOrigin, rayDirection * maxDistance, Color.red); // Red for no hit
            }
        }

        if (logDistanceData)
        {
            string graspedInfo = (graspingSystem != null && graspingSystem.IsGrasping) ?
                $" (Ignoring grasped: {graspingSystem.GraspedObject.name})" : "";
            Debug.Log($"Distance: {currentDistance:F3} | d_s: {distanceToSurface:F3} | d_o: {distanceToObject:F3} | Calculated: {calculatedDistance:F3} | Offset: {pressureDistanceOffset:F3}{graspedInfo}");
        }
    }

    // public int DistanceToEncoder(float value)
    // {
    //     value -= valueOffset;
    //     if (value < 0)
    //     {
    //         value = -value;
    //     }

    //     float fromMin = 0.00f;
    //     float fromMax = 0.06f;
    //     float toMin = 10f;
    //     float toMax = maxEncoderCount;
    //     float t = (value - fromMin) / (fromMax - fromMin);
    //     float mappedFloat = toMin + (toMax - toMin) * t;

    //     return Mathf.FloorToInt(mappedFloat);
    // }

    void SendDistanceToArduino()
    {
        if (!isConnected || Time.time - lastDistanceSendTime < distanceSendInterval)
            return;

        try
        {
            string command;

            // Send the actual distance value instead of encoder counts
            switch (currentPressureState)
            {
                //case PressureState.Medium:
                //    // Between threshold1 and threshold2 - send 'S'
                //    command = "S\n";
                //    break;

                //case PressureState.Low:
                //case PressureState.High:
                //    command = $"D{currentDistance:F4}\n";
                //    break;
                default:
                    // Send smoothed distance value with 'M' prefix
                    command = $"M{smoothedDistance*1000:F1}\n";
                    // Debug.Log($"target distance:{smoothedDistance:F4} (raw:{currentDistance:F4}), encoder:{encoderCount}");
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
            yield return new WaitForSeconds(0.01f);
        }
    }

    void ParseSensorData(string data)
    {
        if (string.IsNullOrEmpty(data)) return;

        if (logSerialData)
        {
            Debug.Log($"Received from Arduino: {data}");
        }

        // Parse format: "P0|E1|D0.5|B1|H0"
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
                else if (part.StartsWith("D"))
                {
                    if (float.TryParse(part.Substring(1), out float distance))
                    {
                        realDistance = distance;
                    }
                }
                else if (part.StartsWith("B"))
                {
                    if (int.TryParse(part.Substring(1), out int button))
                    {
                        buttonPressed = (button == 1);
                    }
                }
                else if (part.StartsWith("H"))
                {
                    if (int.TryParse(part.Substring(1), out int homeButton))
                    {
                        homeButtonPressed = (homeButton == 1);
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
        if (isConnected && serialPort != null)
        {
            serialPort.Close();
        }
    }

    void OnDestroy()
    {
        if (isConnected && serialPort != null)
        {
            serialPort.Close();
        }
    }

    // GUI for debugging
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 300, 400));

        GUILayout.Label($"Serial: {(isConnected ? "Connected" : "Disconnected")}");
        GUILayout.Label($"Distance (Raw): {currentDistance:F3}");
        GUILayout.Label($"Distance (Smoothed): {smoothedDistance:F3}");
        GUILayout.Label($"Smoothing: {(enableDistanceSmoothing ? "ON" : "OFF")} (Time: {smoothingTime:F2}s)");
        GUILayout.Label($"d_s (Surface): {distanceToSurface:F3}");
        GUILayout.Label($"d_o (Object): {distanceToObject:F3}");
        GUILayout.Label($"Calculated: {calculatedDistance:F3}");
        GUILayout.Label($"Pressure Offset: {pressureDistanceOffset:F3}");
        GUILayout.Label($"Pen Length: {penLength:F2}");
        GUILayout.Label($"Pressure: {pressureReading:F1}");
        GUILayout.Label($"Pressure State: {currentPressureState}");
        GUILayout.Label($"Encoder: {encoderCount}");
        GUILayout.Label($"Real Distance: {realDistance:F3}");
        GUILayout.Label($"Button: {(buttonPressed ? "Pressed" : "Released")}");
        GUILayout.Label($"Home Button: {(homeButtonPressed ? "Pressed" : "Released")}");

        GUILayout.Space(10);

        // Grasping info from grasping system
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

        if (GUILayout.Button("Test Send (Space)"))
        {
            TestSerialSend();
        }

        if (GUILayout.Button("Reset Pressure Offset"))
        {
            pressureDistanceOffset = 0f;
        }

        GUILayout.EndArea();
    }
}