using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// Interface for trails to provide distance calculation
public interface ITrailEvaluator
{
    Vector3 GetClosestPointOnCenterline(Vector3 position);
}

public class PathRecorder : MonoBehaviour
{
    [Header("Participant Info")]
    public string userId = "P00";
    public string condition = "";

    [Header("Recording Settings")]
    public Transform targetObject;
    [Tooltip("Master switch for the recording session. When true, data can be captured.")]
    public bool isRecording = false;
    public HapticPenController penController;
    
    // Evaluator reference
    private ITrailEvaluator currentEvaluator;

    [Header("Data")]
    [Tooltip("If true, the current data point is considered part of a valid target trace.")]
    public bool isTargetTrace = false;

    [Header("Validation")]
    public bool autoSave = true; // If false, strokes are buffered until CommitStroke is called
    public bool manualControl = false; // If true, capturing is controlled externally via isCapturingOverride
    public bool isCapturingOverride = false; 
    public int currentTargetId = -1;
    public int currentTrailTypeId = -1; // ID of the trail definition (type)
    public int currentSurfaceType = -1; // New: Surface Type (0=Flat, 1=Nurbs, 2=Sine, -1=None)

    private List<PathDataPoint> recordedData = new List<PathDataPoint>();
    private List<PathDataPoint> currentStrokeBuffer = new List<PathDataPoint>(); // Buffer for current stroke
    private float startTime;
    private int currentStrokeId = 0;
    private bool wasCapturing = false;
    private bool lastIsRecording = false;
    private string currentSessionFilePath = "";

    [Serializable]
    public struct PathDataPoint
    {
        public float timestamp;
        public Vector3 position;
        public Vector3 rotation;
        public bool isTargetTrace;
        public int strokeId;
        public int targetId; 
        public int trailTypeId; 
        public int surfaceType; // New
        public float deviation; // Magnitude of error
        public Vector3 errorVector; // Vector from ClosestPoint to CurrentPosition (represents x,y,z error)

        public PathDataPoint(float timestamp, Vector3 position, Vector3 rotation, bool isTargetTrace, int strokeId, int targetId, int trailTypeId, int surfaceType, float deviation, Vector3 errorVector)
        {
            this.timestamp = timestamp;
            this.position = position;
            this.rotation = rotation;
            this.isTargetTrace = isTargetTrace;
            this.strokeId = strokeId;
            this.targetId = targetId;
            this.trailTypeId = trailTypeId;
            this.surfaceType = surfaceType;
            this.deviation = deviation;
            this.errorVector = errorVector;
        }
    }

    // ... [Start, Update methods need modification for loop] ...
    
    public void SetEvaluator(ITrailEvaluator evaluator)
    {
        currentEvaluator = evaluator;
    }

    private void Start()
    {
        if (penController == null)
        {
            penController = FindObjectOfType<HapticPenController>();
        }

        // If recording is enabled by default in Inspector, start the session immediately
        if (isRecording)
        {
            StartRecordingInternal();
        }

        lastIsRecording = isRecording;
    }

    private void Update()
    {
        // Detect state change (e.g. from Inspector)
        if (isRecording != lastIsRecording)
        {
            if (isRecording)
            {
                StartRecordingInternal();
            }
            else
            {
                StopRecordingInternal();
            }
            lastIsRecording = isRecording;
        }

        if (isRecording && targetObject != null)
        {
            if (recordedData.Count == 0 && currentStrokeBuffer.Count == 0)
            {
                startTime = Time.time;
            }

            // Determine if we should be capturing data right now
            bool isCapturing = false;
            
            if (manualControl)
            {
                isCapturing = isCapturingOverride;
            }
            else
            {
                if (penController != null)
                {
                    isCapturing = penController.buttonCPressed;
                }
                else
                {
                    isCapturing = true;
                }
            }

            // Detect new stroke (rising edge)
            if (isCapturing && !wasCapturing)
            {
                // If autoSave is true, we clear buffer here. 
                // If manual (autoSave false), StartNewStroke should have been called externally, or we just append.
                // Let's ensure buffer is clear if autoSave is on.
                if (autoSave)
                {
                    currentStrokeBuffer.Clear();
                }
            }
            
            // Detect end of stroke (falling edge)
            if (wasCapturing && !isCapturing)
            {
                if (autoSave)
                {
                    CommitStroke();
                }
            }

            wasCapturing = isCapturing;

            if (isCapturing)
            {
                float time = Time.time - startTime;
                
                // Calculate Deviation and Error Vector
                float currentDev = -1f;
                Vector3 errorVec = Vector3.zero;
                
                if (currentEvaluator != null)
                {
                    Vector3 closestPoint = currentEvaluator.GetClosestPointOnCenterline(targetObject.position);
                    // Error Vector = Position - ClosestPoint
                    // This creates a vector pointing FROM the ideal path TO the pen.
                    errorVec = targetObject.position - closestPoint;
                    currentDev = errorVec.magnitude;
                }
                
                currentStrokeBuffer.Add(new PathDataPoint(
                    time,
                    targetObject.position,
                    targetObject.eulerAngles,
                    isTargetTrace,
                    currentStrokeId, 
                    currentTargetId,
                    currentTrailTypeId,
                    currentSurfaceType, // Pass current surface type
                    currentDev,
                    errorVec
                ));
            }
        }
    }

    // Manual Control Methods

    public void StartNewStroke(int targetId, int typeId = -1, int surfType = -1)
    {
        currentStrokeBuffer.Clear();
        currentTargetId = targetId;
        if (typeId != -1) currentTrailTypeId = typeId;
        currentSurfaceType = surfType; // Update surface type
        currentStrokeId++; // Increment ID for the new stroke
    }
    
    public void SetCurrentTrailType(int typeId)
    {
        currentTrailTypeId = typeId;
    }

    public void CommitStroke()
    {
        if (currentStrokeBuffer.Count > 0)
        {
            // Update StrokeID in buffer
            for (int i = 0; i < currentStrokeBuffer.Count; i++)
            {
                var p = currentStrokeBuffer[i];
                p.strokeId = currentStrokeId;
                currentStrokeBuffer[i] = p;
            }

            recordedData.AddRange(currentStrokeBuffer);
            AppendDataToFile(currentStrokeBuffer);
            Debug.Log($"PathRecorder: Stroke {currentStrokeId} (Target {currentTargetId}, Type {currentTrailTypeId}) Saved. ({currentStrokeBuffer.Count} points)");
        }
        currentStrokeBuffer.Clear();
    }

    public void DiscardStroke()
    {
        if (currentStrokeBuffer.Count > 0)
        {
            Debug.Log($"PathRecorder: Stroke Discarded ({currentStrokeBuffer.Count} points).");
        }
        currentStrokeBuffer.Clear();
    }
    
    /// <summary>
    /// Starts a new recording session. Clears previous data.
    /// </summary>
    public void StartRecording()
    {
        isRecording = true;
    }

    /// <summary>
    /// Stops recording.
    /// </summary>
    public void StopRecording()
    {
        isRecording = false;
    }

    private void StartRecordingInternal()
    {
        recordedData.Clear();
        currentStrokeBuffer.Clear();
        currentStrokeId = 0;
        startTime = Time.time;
        wasCapturing = false;
        
        // Generate file path once at the start of the session
        string directoryPath = Application.persistentDataPath;
        if (!string.IsNullOrEmpty(customSavePath))
        {
            if (Directory.Exists(customSavePath))
            {
                directoryPath = customSavePath;
            }
            else
            {
                Debug.LogWarning($"PathRecorder: Custom path '{customSavePath}' does not exist. Falling back to persistentDataPath.");
            }
        }

        string fileName;
        if (!string.IsNullOrEmpty(customFileName))
        {
            fileName = customFileName.EndsWith(".csv") ? customFileName : customFileName + ".csv";
        }
        else
        {
            fileName = $"PathData_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv";
        }
        currentSessionFilePath = Path.Combine(directoryPath, fileName);
        
        // Create file and write header
        InitializeFile();
        
        Debug.Log($"PathRecorder: Started recording session. File: {currentSessionFilePath}");
    }

    private void StopRecordingInternal()
    {
        // SaveData(); // Data is now appended incrementally
        Debug.Log("PathRecorder: Stopped recording session.");
    }

    [Header("File Settings")]
    [Tooltip("Directory to save CSV files. If empty, uses Application.persistentDataPath.")]
    public string customSavePath = "";
    [Tooltip("Custom filename (e.g. 'MyData'). If empty, uses timestamp.")]
    public string customFileName = "";

    private void InitializeFile()
    {
        try
        {
            using (StreamWriter writer = new StreamWriter(currentSessionFilePath, false))
            {
                writer.WriteLine("UserID,Condition,Timestamp,Position_X,Position_Y,Position_Z,Rotation_X,Rotation_Y,Rotation_Z,IsTargetTrace,StrokeID,TargetID,TrailTypeID,SurfaceTypeID,DistToCenterline,Error_X,Error_Y,Error_Z");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"PathRecorder: Failed to init file. Error: {e.Message}");
        }
    }

    private void AppendDataToFile(List<PathDataPoint> points)
    {
        if (string.IsNullOrEmpty(currentSessionFilePath)) return;

        try
        {
            using (StreamWriter writer = new StreamWriter(currentSessionFilePath, true))
            {
                foreach (var point in points)
                {
                    string line = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17}",
                        userId,
                        condition,
                        point.timestamp,
                        point.position.x,
                        point.position.y,
                        point.position.z,
                        point.rotation.x,
                        point.rotation.y,
                        point.rotation.z,
                        point.isTargetTrace,
                        point.strokeId,
                        point.targetId,
                        point.trailTypeId,
                        point.surfaceType, // Add SurfaceType
                        point.deviation,
                        point.errorVector.x,
                        point.errorVector.y,
                        point.errorVector.z);
                    writer.WriteLine(line);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"PathRecorder: Failed to append data. Error: {e.Message}");
        }
    }
    
    // Deprecated monolithic save, but keeping structure for compatibility if needed
    public void SaveData() 
    {
        // No-op or full rewrite if needed, but we use Append now.
    }
    
    // Optional: Visualize the recording status
    private void OnGUI()
    {
        if (isRecording)
        {
            GUI.color = Color.red;
            GUI.Label(new Rect(10, 10, 400, 20), $"RECORDING SESSION: {currentStrokeId} Valid Strokes");
            GUI.Label(new Rect(10, 30, 400, 20), $"Target ID: {currentTargetId}");
            GUI.Label(new Rect(10, 50, 400, 20), $"File: {Path.GetFileName(currentSessionFilePath)}");
        }
    }
}
