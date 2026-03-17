using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;

public class VerticalSliderExperiment : MonoBehaviour
{
    public enum ExperimentState { Initialize, Idle, Moving, Confirmed, Completed }

    [Header("References")]
    public HapticPenGraspingSystem graspingSystem;
    public Transform slider;
    public Transform targetIndicator;
    public Renderer sliderRenderer;
    public Material defaultMaterial;
    public Material targetMaterial;
    [Tooltip("Material when pen tip is hovering near the slider")]
    public Material hoverMaterial;

    [Header("Experiment Configuration")]
    public int numberOfRepetitions = 3;
    public float acceptanceTolerance = 0.010f; // 10mm
    public float visualAidTolerance = 0.001f; // 1mm
    public bool enableVisualAid = true;
    [Tooltip("When enabled, the experiment runs normally but no data is saved to disk")]
    public bool testMode = false;

    public enum ConditionMode { midair, haptic }
    public ConditionMode conditionMode = ConditionMode.midair;

    [Header("Data Logging")]
    [Tooltip("Participant identifier, recorded in CSV output")]
    public string userId = "P00";
    [Tooltip("Leave empty to save in Assets/ folder. Example: D:/ExpData/")]
    public string customOutputDirectory = "";
    
    [Header("Debug")]
    public ExperimentState currentState = ExperimentState.Initialize;
    public int currentTrialIndex = -1;
    public float currentError = 0f;

    [Serializable]
    public struct TrialConfig
    {
        public float startHeight; // meters
        public float targetHeight; // meters
    }

    private List<TrialConfig> trials = new List<TrialConfig>();
    private float currentTrialStartTime;
    private string summaryCsvFilePath;
    private string continuousCsvFilePath;
    private string experimentId;

    // Continuous data recording list
    private List<string> currentTrialContinuousData = new List<string>();

    void Start()
    {
        if (graspingSystem != null)
        {
            graspingSystem.OnObjectGrasped += HandleObjectGrasped;
            graspingSystem.OnObjectReleased += HandleObjectReleased;
        }
        else
        {
            Debug.LogWarning("VerticalSliderExperiment: Grasping System not assigned!");
        }

        if (sliderRenderer == null && slider != null)
        {
            sliderRenderer = slider.GetComponent<Renderer>();
        }

        if (!testMode) InitializeCSV();
        GenerateTrials();
        
        currentState = ExperimentState.Idle;
        StartNextTrial();
    }

    void OnDestroy()
    {
        if (graspingSystem != null)
        {
            graspingSystem.OnObjectGrasped -= HandleObjectGrasped;
            graspingSystem.OnObjectReleased -= HandleObjectReleased;
        }
    }

    void InitializeCSV()
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        experimentId = $"Exp_{timestamp}";
        
        // Base directory
        string baseDirectory = string.IsNullOrEmpty(customOutputDirectory) ? Application.dataPath : customOutputDirectory;
        
        // Append condition mode subdirectory (e.g., "midair" or "haptic")
        string conditionFolder = conditionMode.ToString();
        string directory = Path.Combine(baseDirectory, conditionFolder);

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string summaryFilename = $"{experimentId}_Summary.csv";
        summaryCsvFilePath = Path.Combine(directory, summaryFilename);
        
        string continuousFilename = $"{experimentId}_Continuous.csv";
        continuousCsvFilePath = Path.Combine(directory, continuousFilename);

        try
        {
            // Initialize Summary CSV
            using (StreamWriter writer = new StreamWriter(summaryCsvFilePath, false))
            {
                writer.WriteLine("UserID,ExpID,TrialNumber,Condition,StartHeight(mm),TargetHeight(mm),FinalHeight(mm),Error(mm),CompletionTime(s)");
            }
            // Initialize Continuous Data CSV
            using (StreamWriter writer = new StreamWriter(continuousCsvFilePath, false))
            {
                writer.WriteLine("UserID,ExpID,TrialNumber,TimeSinceTrialStart(s),DistanceToTarget(mm)");
            }
            Debug.Log($"Initialized CSV Logs at: {directory}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to initialize CSV files: {e.Message}");
        }
    }

    void GenerateTrials()
    {
        trials.Clear();

        // All 6 positions in meters: 0, 10, 20, 30, 40, 50 mm
        float[] positions = { 0.00f, 0.01f, 0.02f, 0.03f, 0.04f, 0.05f };

        for (int rep = 0; rep < numberOfRepetitions; rep++)
        {
            // Full permutation: every position as start -> every other position as target
            foreach (float start in positions)
            {
                foreach (float target in positions)
                {
                    if (Mathf.Approximately(start, target)) continue; // skip same position trials

                    trials.Add(new TrialConfig { startHeight = start, targetHeight = target });
                }
            }
        }

        // Fisher-Yates Shuffle
        System.Random rng = new System.Random();
        int n = trials.Count;
        while (n > 1)
        {
            n--;
            int k = rng.Next(n + 1);
            TrialConfig value = trials[k];
            trials[k] = trials[n];
            trials[n] = value;
        }

        Debug.Log($"Generated {trials.Count} trials in total. ({positions.Length * (positions.Length - 1)} unique pairs x {numberOfRepetitions} repetitions)");
    }

    void StartNextTrial()
    {
        if (currentState == ExperimentState.Completed) return;

        currentTrialIndex++;
        if (currentTrialIndex >= trials.Count)
        {
            currentState = ExperimentState.Completed;
            Debug.Log("Experiment Completed!");
            UpdateVisualAid(false);
            return;
        }

        TrialConfig trial = trials[currentTrialIndex];
        
        // Reset slider position (Y axis) relative to parent
        if (slider != null)
        {
            Vector3 pos = slider.localPosition;
            pos.y = trial.startHeight;
            slider.localPosition = pos;
        }

        // Reset target position (Y axis) relative to parent
        if (targetIndicator != null)
        {
            Vector3 targetPos = targetIndicator.localPosition;
            targetPos.y = trial.targetHeight;
            targetIndicator.localPosition = targetPos;
        }

        UpdateVisualAid(false);
        currentState = ExperimentState.Idle;
        Debug.Log($"Started Trial {currentTrialIndex + 1}/{trials.Count} : {trial.startHeight * 1000}mm -> {trial.targetHeight * 1000}mm");
    }

    void ResetCurrentTrial()
    {
        Debug.LogWarning($"Trial {currentTrialIndex + 1} Failed. Resetting position.");
        TrialConfig trial = trials[currentTrialIndex];
        
        if (slider != null)
        {
            Vector3 pos = slider.localPosition;
            pos.y = trial.startHeight;
            slider.localPosition = pos;
        }
        UpdateVisualAid(false);
        currentState = ExperimentState.Idle;
    }

    void Update()
    {
        if (currentState == ExperimentState.Moving)
        {
            CheckVisualAid();
            RecordContinuousData();
        }
        else if (currentState == ExperimentState.Idle)
        {
            CheckHoverHighlight();
        }
    }

    void CheckHoverHighlight()
    {
        if (slider == null || sliderRenderer == null || graspingSystem == null || graspingSystem.penTip == null) return;

        Collider sliderCollider = slider.GetComponent<Collider>();
        if (sliderCollider == null) return;

        // Use collider's ClosestPoint: if distance to closest surface point is ~0, pen tip is touching/inside
        Vector3 penTipPos = graspingSystem.penTip.position;
        Vector3 closestPoint = sliderCollider.ClosestPoint(penTipPos);
        float dist = Vector3.Distance(penTipPos, closestPoint);
        bool isHovering = dist < 0.001f; // practically touching (< 1mm)

        if (isHovering && hoverMaterial != null)
        {
            if (sliderRenderer.sharedMaterial != hoverMaterial)
                sliderRenderer.material = hoverMaterial;
        }
        else
        {
            if (defaultMaterial != null && sliderRenderer.sharedMaterial != defaultMaterial)
                sliderRenderer.material = defaultMaterial;
        }
    }

    void RecordContinuousData()
    {
        if (slider == null || targetIndicator == null || trials == null || currentTrialIndex < 0) return;
        
        float timeSinceStart = Time.time - currentTrialStartTime;
        // distance to target indicator
        float distanceToTarget = (slider.localPosition.y - targetIndicator.localPosition.y) * 1000f; // mm, positive = above target, negative = below target

        string dataLine = $"{userId},{experimentId},{currentTrialIndex + 1},{timeSinceStart:F3},{distanceToTarget:F2}";
        currentTrialContinuousData.Add(dataLine);
    }

    void CheckVisualAid()
    {
        if (slider == null || targetIndicator == null || sliderRenderer == null) return;

        currentError = Mathf.Abs(slider.localPosition.y - targetIndicator.localPosition.y);
        
        if (enableVisualAid)
        {
            bool isInsideTolerance = currentError <= visualAidTolerance;
            UpdateVisualAid(isInsideTolerance);
        }
        else
        {
            UpdateVisualAid(false);
        }
    }

    void UpdateVisualAid(bool isTargetAcheived)
    {
        if (sliderRenderer != null)
        {
            Material focusMat = isTargetAcheived ? targetMaterial : (hoverMaterial != null ? hoverMaterial : defaultMaterial);
            // Compare instance ID to avoid unnecessary material assignments
            if (focusMat != null && sliderRenderer.sharedMaterial != focusMat)
            {
                sliderRenderer.material = focusMat;
            }
        }
    }

    private void HandleObjectGrasped(Transform grabbedObject)
    {
        if (currentState == ExperimentState.Idle && grabbedObject == slider)
        {
            currentState = ExperimentState.Moving;
            currentTrialStartTime = Time.time;
            currentTrialContinuousData.Clear(); // reset continuous log for new trial
            Debug.Log("Slider Grasped. Trial Timer Started.");
        }
    }

    private void HandleObjectReleased(Transform releasedObject)
    {
        if (currentState == ExperimentState.Moving && releasedObject == slider)
        {
            currentState = ExperimentState.Confirmed;
            EvaluateTrial();
        }
    }

    void EvaluateTrial()
    {
        if (slider == null || targetIndicator == null) return;

        TrialConfig trial = trials[currentTrialIndex];
        float finalHeight = slider.localPosition.y;
        float error = Mathf.Abs(finalHeight - trial.targetHeight);
        float completionTime = Time.time - currentTrialStartTime;

        if (error <= acceptanceTolerance)
        {
            // Trial Success
            if (!testMode)
            {
                LogTrialData(trial, finalHeight, error, completionTime);
                LogContinuousData(); // Dump recorded path to CSV
            }
            Debug.Log($"Trial {currentTrialIndex + 1} Success! Error: {error * 1000:F2}mm, Time: {completionTime:F2}s");
            StartCoroutine(WaitAndStartNext(1.0f));
        }
        else
        {
            // Trial Fail
            currentTrialContinuousData.Clear(); // discard failed trial continuous data
            Debug.Log($"Trial {currentTrialIndex + 1} Failed. Error {error * 1000:F2}mm exceeds tolerance {acceptanceTolerance * 1000}mm.");
            ResetCurrentTrial();
        }
    }

    IEnumerator WaitAndStartNext(float waitTime)
    {
        if (enableVisualAid)
            UpdateVisualAid(true); // Keep it green for a moment on success
        yield return new WaitForSeconds(waitTime);
        StartNextTrial();
    }

    void LogTrialData(TrialConfig trial, float finalHeight, float error, float completionTime)
    {
        try
        {
            using (StreamWriter writer = new StreamWriter(summaryCsvFilePath, true))
            {
                // UserID, ExpID, TrialNumber, Condition, StartHeight(mm), TargetHeight(mm), FinalHeight(mm), Error(mm), CompletionTime(s)
                string line = $"{userId},{experimentId},{currentTrialIndex + 1},{conditionMode},{trial.startHeight * 1000:F1},{trial.targetHeight * 1000:F1},{finalHeight * 1000:F2},{error * 1000:F2},{completionTime:F3}";
                writer.WriteLine(line);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to write summary to CSV: {e.Message}");
        }
    }

    void LogContinuousData()
    {
        if (currentTrialContinuousData.Count == 0) return;
        try
        {
            using (StreamWriter writer = new StreamWriter(continuousCsvFilePath, true))
            {
                foreach (string line in currentTrialContinuousData)
                {
                    writer.WriteLine(line);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to write continuous data to CSV: {e.Message}");
        }
    }
}
