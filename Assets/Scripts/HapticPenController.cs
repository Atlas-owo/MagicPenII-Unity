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
    public GameObject customPenTipModel; // 可在此处挂载自定义的笔模型预制体 (Prefab)
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

    [Header("Control Mode")]
    public bool enableRaycastControl = true; // If false, defaults to 0 distance
    public bool enableDirectPressureControl = false;
    public bool enableMidairMode = false; // Forces pen to 0 extension

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

    [Header("Pressure Control")]
    public bool enablePressureControl = true;
    public float pressureRetractThreshold = 40f; // Above this, retract
    public float pressureExtendThreshold = 10f;  // Below this, extend
    public float retractionSpeed = 0.2f; // m/s
    public float extensionSpeed = 0.1f;  // m/s
    public bool enablePressureSmoothing = true;
    public float pressurePressSmoothTime = 0.05f; // Fast attack
    public float pressureReleaseSmoothTime = 0.4f; // Slow release
    public float pressureDeadband = 2.0f; // Hysteresis threshold

    [Header("Hybrid Pressure Speed Control")]
    [Tooltip("Pressure value (a) to start retraction mode")]
    public float pressureThresholdStart = 10f; 
    [Tooltip("Pressure value (e) below which extension is allowed (Zero Pressure Threshold)")]
    public float pressureThresholdExtension = 2.0f; // Relaxed from 0.5 to 2.0
    [Tooltip("Pressure value (b) for max retraction speed")]
    public float pressureThresholdMax = 100f;
    [Tooltip("Motor PWM (c) for start speed (0-255)")]
    public int motorSpeedStart = 100;
    [Tooltip("Motor PWM (d) for max speed (0-255)")]
    public int motorSpeedMax = 255;
    [Tooltip("Minimum extension distance (y) required to trigger extension command")]
    public float extensionThreshold = 0.002f; // 2mm
    [Tooltip("Maximum physical extension distance (m) of the pen hardware")]
    public float maxPhysicalExtension = 0.07f; // 75mm

    [Range(-0.01f, 0.01f)]
    public float hybridModeOffset = 0f; // Small offset added when Hybrid Mode is active

    [Header("Hybrid Extension Dynamics")]
    [Tooltip("Distance (m) for Minimum extension speed (Close to target)")]
    public float extDistMin = 0.002f; // 2mm
    [Tooltip("Distance (m) for Maximum extension speed (Far from target)")]
    public float extDistMax = 0.05f;  // 50mm
    [Tooltip("Minimum PWM speed (to overcome friction)")]
    public int extPWMMin = 80;
    [Tooltip("Maximum PWM speed")]
    public int extPWMMax = 255;

    [Header("Distance Smoothing")]
    public bool enableDistanceSmoothing = true; // Enable/disable smoothing
    public float smoothingFactor = 0.1f; // Lower = more smoothing (0-1)
    [Range(0.01f, 1.0f)]
    public float smoothingTime = 0.1f; // Time to reach target (seconds)

    [Header("Button Control Mode")]
    public bool enableButtonControl = false;
    public bool enableButtonShrink = true; // If false, button C is ignored for manual control
    public float buttonExtendSpeed = 0.5f;
    public float buttonShrinkSpeed = 0.5f;
    private float manualTargetDistance = 0f;
    private bool wasManualControlActive = false;



    [Header("Debug")]
    public bool showDebugRays = true;
    public bool logSerialData = true;
    public bool logDistanceData = false;

    // Parsed sensor data
    private float pressureReading = 0f;
    private float targetPressure = 0f; // Latched target for deadband
    private float smoothedPressure = 0f;
    private float pressureVelocity = 0f;
    private float currentRetractionOffset = 0f; // Integral value for velocity control
    private long encoderCount = 0;
    private float realDistance = 0f; // D value - real distance that the pen has extended
    public bool buttonPressed = false;
    public bool buttonCPressed = false; // New button C
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

    
    // New distance measurement variables
    private float distanceToSurface = 0f; // d_s
    private float distanceToObject = 0f;  // d_o
    private float calculatedDistance = 0f; // Final distance sent to Arduino

    // Pressure state tracking


    // Timing
    private float lastDistanceSendTime = 0f;
    private float distanceSendInterval = 0.005f; // Send distance every 50ms

    void Start()
    {
        InitializePen();
        InitializeGraspingSystem();
        ConnectToArduino();
        StartCoroutine(SerialReadCoroutine());
    }

    void Update()
    {
        UpdatePressureSmoothing();
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

            if (customPenTipModel != null)
            {
                // 如果用户指定了自定义笔模型，则实例化为笔尖的子物体
                GameObject model = Instantiate(customPenTipModel, tipObj.transform);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                // 如果模型自带碰撞体，可能会干扰射线检测，如有需要可将模型的layer设置为IgnoreRaycast
            }
            else
            {
                // Add a small sphere to visualize the tip
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.transform.SetParent(tipObj.transform);
                sphere.transform.localScale = Vector3.one * 0.001f;
                sphere.transform.localPosition = Vector3.zero;
            }
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
        calculatedDistance = 0f; // Default to 0 if raycast is disabled

        if (enableRaycastControl)
        {
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
            float effectiveOffset = distanceOffset;
            if (enableDirectPressureControl)
            {
                effectiveOffset += hybridModeOffset;
            }

            if (foundSurface)
            {
                distanceToSurface = surfaceHit.distance + effectiveOffset;
            }
            
            if (foundObject)
            {
                distanceToObject = objectHit.distance + effectiveOffset;
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
        }
        else
        {
             // Raycast Disabled -> Default to 0 (Already set above)
             distanceToSurface = 0f;
             distanceToObject = 0f;
        }

        // Apply Pressure Logic (Velocity Control)
        // DISABLE OLD LOGIC if new Hybrid Control is enabled
        if (enablePressureControl && !enableDirectPressureControl)
        {
            // Determine separate delta based on pressure state
            if (smoothedPressure > pressureRetractThreshold)
            {
                // Push Hard -> Retract (Increase offset)
                currentRetractionOffset += retractionSpeed * Time.deltaTime;
            }
            else if (smoothedPressure < pressureExtendThreshold)
            {
                // Release -> Extend (Decrease offset)
                currentRetractionOffset -= extensionSpeed * Time.deltaTime;
            }
            // Else: Middle Zone -> Hold (Do nothing)

            // Clamp Offset
            // Min offset is 0 (Full extension)
            // Max offset is calculatedDistance (Fully retracted to 0)
            currentRetractionOffset = Mathf.Clamp(currentRetractionOffset, 0f, calculatedDistance);

            // Apply Offset
            currentDistance = calculatedDistance - currentRetractionOffset;
        }
        else
        {
            currentDistance = calculatedDistance;
            currentRetractionOffset = 0f; // Reset if disabled
        }

        // Button Control Mode Override
        // Only active if enabled AND one of the buttons is pressed (and allowed)
        bool shrinking = enableButtonShrink && buttonCPressed;
        bool isManualAction = enableButtonControl && (buttonPressed || shrinking);
        
        if (isManualAction)
        {
            if (!wasManualControlActive)
            {
                manualTargetDistance = smoothedDistance; // Latch current position
                wasManualControlActive = true;
            }

            if (buttonPressed)
            {
                manualTargetDistance += buttonExtendSpeed * Time.deltaTime;
            }
            
            if (shrinking)
            {
                manualTargetDistance -= buttonShrinkSpeed * Time.deltaTime;
            }

            manualTargetDistance = Mathf.Clamp(manualTargetDistance, 0f, maxDistance);
            currentDistance = manualTargetDistance;
        }
        else
        {
            wasManualControlActive = false;

            // If Raycast is disabled and Button Control is enabled, 
            // Hold the last manual position instead of resetting to 0.
            if (!enableRaycastControl && enableButtonControl)
            {
                currentDistance = manualTargetDistance;
            }
        }

        // Midair Mode Override (Highest Priority)
        // Forces the pen to shrink to zero extension (fully retracted)
        if (enableMidairMode)
        {
            currentDistance = 0f;
            wasManualControlActive = false; // Reset manual control latch so it doesn't get stuck
        }

        // Clamp to 0 just in case
        currentDistance = Mathf.Max(0, currentDistance);

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


        

        if (logDistanceData)
        {
            string graspedInfo = (graspingSystem != null && graspingSystem.IsGrasping) ?
                $" (Ignoring grasped: {graspingSystem.GraspedObject.name})" : "";
            Debug.Log($"Distance: {currentDistance:F3} | d_s: {distanceToSurface:F3} | d_o: {distanceToObject:F3} | Calculated: {calculatedDistance:F3}{graspedInfo}");
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
            string command = "";

            if (enableDirectPressureControl)
            {
                // --- HYBRID CONTROL LOGIC ---

                // STATE 1: RETRACTION (Pressure >= a)
                if (pressureReading >= pressureThresholdStart)
                {
                    float t = Mathf.InverseLerp(pressureThresholdStart, pressureThresholdMax, pressureReading);
                    int pwm = (int)Mathf.Lerp(motorSpeedStart, motorSpeedMax, t);
                    
                    // Software Cushion: Reduce PWM near physical limit
                    float currentRealMeters = realDistance / 1000f;
                    if (currentRealMeters < 0.005f) { // Under 5mm
                        pwm = Mathf.Min(pwm, 100); 
                    }
                    if (currentRealMeters <= 0.001f) { // Bottom 1mm
                        pwm = 0;
                    }
                    
                    // Retract using negative direct velocity
                    command = $"A-{pwm}\n";
                }
                // STATE 2: EXTENSION (Pressure near 0)
                else if (pressureReading <= pressureThresholdExtension)
                {
                    // Hybrid Mode: Raycast Tracking with Linear Speed Mapping
                    
                    float targetDist = calculatedDistance;
                    
                    // Calculate difference (Error)
                    // realDistance is in mm (from Arduino), convert to meters
                    float currentRealMeters = realDistance / 1000f;

                    // Debug logic
                    if (logDistanceData) 
                    {
                        Debug.Log($"[Hybrid] Pres: {pressureReading:F1}, Targ: {targetDist:F3}, Real: {currentRealMeters:F3}");
                    }

                    // Maximum physical extension limit: stop if already at hardware max
                    if (currentRealMeters >= maxPhysicalExtension)
                    {
                        command = "S\n";
                    }
                    else
                    {
                        // Clamp target distance to physical max to prevent over-extension
                        targetDist = Mathf.Min(targetDist, maxPhysicalExtension);
                        float diff = targetDist - currentRealMeters;

                        // Condition: Target is further away than current position + threshold
                        if (diff > extensionThreshold) 
                        {
                            // LINEAR MAPPING
                            // Map 'diff' from [extDistMin, extDistMax] to [extPWMMin, extPWMMax]
                            float t = Mathf.InverseLerp(extDistMin, extDistMax, diff);
                            int pwm = (int)Mathf.Lerp(extPWMMin, extPWMMax, t);
                            
                            // Send positive Speed command
                            command = $"A{pwm}\n";
                        }
                        else
                        {
                            // Near enough, just hold or stop to prevent jitter
                            command = "S\n";
                        }
                    }
                }
                // STATE 3: STOP / DEADBAND (0 < Pressure < a)
                else
                {
                    // Light touch zone - Hold Position
                    command = "S\n";
                }
            }
            else
            {
                // Standard Position Control (Original)
                // Send smoothed distance value with 'M' prefix
                command = $"M{smoothedDistance * 1000:F1}\n";
            }

            if (!string.IsNullOrEmpty(command))
            {
                serialPort.Write(command);
                lastDistanceSendTime = Time.time;

                if (logSerialData)
                {
                    Debug.Log($"Sent to Arduino: {command.Trim()}");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error sending to Arduino: {e.Message}");
            isConnected = false;
        }
    }

    // Helper to get extension threshold (adapting to scale)
    private float extensionOffsetForCheck()
    {
        return extensionThreshold;
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

    void UpdatePressureSmoothing()
    {
        if (enablePressureSmoothing)
        {
            // Deadband Logic: Only update target if changed significantly
            if (Mathf.Abs(pressureReading - targetPressure) > pressureDeadband)
            {
                targetPressure = pressureReading;
            }

            // Asymmetric smoothing: Fast attack (Press), Slow release (Release)
            float targetTime = (targetPressure > smoothedPressure) ? pressurePressSmoothTime : pressureReleaseSmoothTime;
            smoothedPressure = Mathf.SmoothDamp(smoothedPressure, targetPressure, ref pressureVelocity, targetTime);
        }
        else
        {
            smoothedPressure = pressureReading;
            targetPressure = pressureReading; // Keep sync
            pressureVelocity = 0f; // Reset velocity when smoothing is disabled
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
                        buttonPressed = (button == 0); // Active LOW (Input Pullup)
                    }
                }
                else if (part.StartsWith("H"))
                {
                    if (int.TryParse(part.Substring(1), out int homeButton))
                    {
                        homeButtonPressed = (homeButton == 1);
                    }
                }
                else if (part.StartsWith("C"))
                {
                    if (int.TryParse(part.Substring(1), out int buttonC))
                    {
                        buttonCPressed = (buttonC == 0); // Active LOW (Input Pullup)
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error parsing sensor data '{data}': {e.Message}");
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
        GUILayout.Label($"Pressure: {pressureReading:F1} (Smoothed: {smoothedPressure:F1})");
        GUILayout.Label($"Encoder: {encoderCount}");
        GUILayout.Label($"Real Distance: {realDistance:F3}");
        GUILayout.Label($"Button: {(buttonPressed ? "Pressed" : "Released")}");
        GUILayout.Label($"Button C: {(buttonCPressed ? "Pressed" : "Released")}");
        GUILayout.Label($"Home Button: {(homeButtonPressed ? "Pressed" : "Released")}");
        
        bool shrinking = enableButtonShrink && buttonCPressed;
        if (enableButtonControl && (buttonPressed || shrinking)) 
        {
            GUILayout.Label("Status: MANUAL CONTROL ACTIVE");
        }
        if (enableMidairMode)
        {
            GUILayout.Label("Status: MIDAIR MODE ACTIVE");
        }

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



        GUILayout.EndArea();
    }
}