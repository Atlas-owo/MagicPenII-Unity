using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[System.Serializable]
public class RecordingDataPoint
{
    public float timestamp;
    public Vector3 penPosition;
    public Vector3 penRotation;
    public float pressure;
    public float realDistance;
    public long encoderCount;
    public bool buttonPressed;
    public bool homeButtonPressed;
    public float calculatedDistance;
    public float smoothedDistance;

    public RecordingDataPoint(float time, Transform penTransform, HapticPenController penController)
    {
        timestamp = time;
        penPosition = penTransform.position;
        penRotation = penTransform.eulerAngles;
        
        // Get data from pen controller using public properties
        pressure = penController.PressureReading;
        realDistance = penController.RealDistance;
        encoderCount = penController.EncoderCount;
        calculatedDistance = penController.CalculatedDistance;
        smoothedDistance = penController.SmoothedDistance;
        
        buttonPressed = penController.buttonPressed;
        homeButtonPressed = penController.homeButtonPressed;
    }

    public string ToCSVRow()
    {
        return $"{timestamp:F4},{penPosition.x:F6},{penPosition.y:F6},{penPosition.z:F6}," +
               $"{penRotation.x:F3},{penRotation.y:F3},{penRotation.z:F3}," +
               $"{pressure:F3},{realDistance:F6},{encoderCount},{calculatedDistance:F6},{smoothedDistance:F6}," +
               $"{(buttonPressed ? 1 : 0)},{(homeButtonPressed ? 1 : 0)}";
    }

    public static string GetCSVHeader()
    {
        return "Timestamp,PosX,PosY,PosZ,RotX,RotY,RotZ,Pressure,RealDistance,EncoderCount,CalculatedDistance,SmoothedDistance,ButtonPressed,HomeButtonPressed";
    }
}

public class DataRecorder : MonoBehaviour
{
    [Header("Recording Settings")]
    public Transform targetObject; // Object to record (usually the pen)
    public HapticPenController penController; // Reference to pen controller
    public float recordingFrequency = 100f; // Hz - how often to record data points
    public bool autoFindPenController = true;
    
    [Header("File Settings")]
    public string filePrefix = "PenData_";
    public string customFileName = ""; // Optional custom filename
    
    [Header("Recording Control")]
    public KeyCode startStopKey = KeyCode.R; // Key to start/stop recording
    public bool recordingEnabled = false;
    
    // Private variables
    private List<RecordingDataPoint> recordedData = new List<RecordingDataPoint>();
    private bool isRecording = false;
    private float recordingStartTime;
    private float nextRecordTime;
    private string currentFileName;
    
    // Events for external control
    public event System.Action OnRecordingStarted;
    public event System.Action<string> OnRecordingStopped; // Passes filename
    
    void Start()
    {
        // Auto-find components if not assigned
        if (targetObject == null)
        {
            targetObject = transform;
        }
        
        if (autoFindPenController && penController == null)
        {
            penController = FindObjectOfType<HapticPenController>();
            if (penController == null)
            {
                Debug.LogWarning("DataRecorder: No HapticPenController found in scene!");
            }
        }
        
        // Validate recording frequency
        recordingFrequency = Mathf.Max(1f, recordingFrequency);
    }
    
    void Update()
    {
        // Handle keyboard input
        if (recordingEnabled && Input.GetKeyDown(startStopKey))
        {
            if (isRecording)
            {
                StopRecording();
            }
            else
            {
                StartRecording();
            }
        }
        
        // Record data if recording is active
        if (isRecording && Time.time >= nextRecordTime)
        {
            RecordDataPoint();
            nextRecordTime = Time.time + (1f / recordingFrequency);
        }
    }
    
    /// <summary>
    /// Start recording data. Can be called from external scripts.
    /// </summary>
    public void StartRecording()
    {
        if (isRecording)
        {
            Debug.LogWarning("DataRecorder: Already recording!");
            return;
        }
        
        if (penController == null)
        {
            Debug.LogError("DataRecorder: No pen controller assigned!");
            return;
        }
        
        // Clear previous data
        recordedData.Clear();
        
        // Set recording parameters
        isRecording = true;
        recordingStartTime = Time.time;
        nextRecordTime = Time.time;
        
        // Generate filename
        currentFileName = GenerateFileName();
        
        Debug.Log($"DataRecorder: Started recording to {currentFileName}");
        OnRecordingStarted?.Invoke();
    }
    
    /// <summary>
    /// Stop recording and save data to CSV. Can be called from external scripts.
    /// </summary>
    public void StopRecording()
    {
        if (!isRecording)
        {
            Debug.LogWarning("DataRecorder: Not currently recording!");
            return;
        }
        
        isRecording = false;
        
        // Save data to file
        string savedPath = SaveDataToCSV();
        
        Debug.Log($"DataRecorder: Stopped recording. Saved {recordedData.Count} data points to {savedPath}");
        OnRecordingStopped?.Invoke(savedPath);
        
        // Clear data to free memory
        recordedData.Clear();
    }
    
    /// <summary>
    /// Toggle recording on/off. Can be called from external scripts.
    /// </summary>
    public void ToggleRecording()
    {
        if (isRecording)
        {
            StopRecording();
        }
        else
        {
            StartRecording();
        }
    }
    
    private void RecordDataPoint()
    {
        if (targetObject == null || penController == null) return;
        
        float relativeTime = Time.time - recordingStartTime;
        RecordingDataPoint dataPoint = new RecordingDataPoint(relativeTime, targetObject, penController);
        recordedData.Add(dataPoint);
    }
    
    private string GenerateFileName()
    {
        if (!string.IsNullOrEmpty(customFileName))
        {
            return customFileName.EndsWith(".csv") ? customFileName : customFileName + ".csv";
        }
        
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        return $"{filePrefix}{timestamp}.csv";
    }
    
    private string SaveDataToCSV()
    {
        // Create directory if it doesn't exist
        string directoryPath = Path.Combine(Application.persistentDataPath, "PenRecordings");
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
        
        string fullPath = Path.Combine(directoryPath, currentFileName);
        
        try
        {
            using (StreamWriter writer = new StreamWriter(fullPath))
            {
                // Write header
                writer.WriteLine(RecordingDataPoint.GetCSVHeader());
                
                // Write data points
                foreach (RecordingDataPoint dataPoint in recordedData)
                {
                    writer.WriteLine(dataPoint.ToCSVRow());
                }
            }
            
            Debug.Log($"DataRecorder: Successfully saved {recordedData.Count} data points to {fullPath}");
            return fullPath;
        }
        catch (Exception e)
        {
            Debug.LogError($"DataRecorder: Failed to save CSV file: {e.Message}");
            return "";
        }
    }
    
    /// <summary>
    /// Get the current recording status
    /// </summary>
    public bool IsRecording => isRecording;
    
    /// <summary>
    /// Get the number of recorded data points in current session
    /// </summary>
    public int RecordedPointCount => recordedData.Count;
    
    /// <summary>
    /// Get the current recording duration in seconds
    /// </summary>
    public float RecordingDuration => isRecording ? Time.time - recordingStartTime : 0f;
    
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(Screen.width - 300, 10, 290, 200));
        
        GUILayout.Label("=== Data Recorder ===");
        GUILayout.Label($"Status: {(isRecording ? "RECORDING" : "Stopped")}");
        
        if (isRecording)
        {
            GUILayout.Label($"Duration: {RecordingDuration:F1}s");
            GUILayout.Label($"Data Points: {RecordedPointCount}");
            GUILayout.Label($"Frequency: {recordingFrequency:F0} Hz");
        }
        
        GUILayout.Label($"Target: {(targetObject != null ? targetObject.name : "None")}");
        GUILayout.Label($"Pen Controller: {(penController != null ? "Connected" : "Missing")}");
        
        GUILayout.Space(10);
        
        if (recordingEnabled)
        {
            string buttonText = isRecording ? $"Stop Recording ({startStopKey})" : $"Start Recording ({startStopKey})";
            if (GUILayout.Button(buttonText))
            {
                ToggleRecording();
            }
        }
        else
        {
            GUILayout.Label("Recording disabled in settings");
        }
        
        if (GUILayout.Button("Open Recordings Folder"))
        {
            string path = Path.Combine(Application.persistentDataPath, "PenRecordings");
            if (Directory.Exists(path))
            {
                System.Diagnostics.Process.Start(path);
            }
        }
        
        GUILayout.EndArea();
    }
}