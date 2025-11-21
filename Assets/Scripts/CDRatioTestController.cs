using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Valve.VR;

/// <summary>
/// Trial data structure for C/D Ratio experiments.
/// Each trial consists of a NURBS amplitude and a C/D ratio.
/// </summary>
[System.Serializable]
public class CDRatioTrial
{
    public float nurbsAmplitude;
    public float cdRatio;
    public bool illusionDetected;      // Q1: Yes/No
    public int confidenceLevel;         // Q2: 1-5
    public int trialNumber;
}

/// <summary>
/// Main controller for C/D Ratio psychophysics experiments.
/// Tests the relationship between control/display ratios and perceptual illusions.
/// </summary>
public class CDRatioTestController : MonoBehaviour
{
    [Header("Experiment Configuration")]
    [Tooltip("List of NURBS surface amplitudes to test (in meters)")]
    [SerializeField] private float[] nurbsAmplitudes = new float[] { 0.01f, 0.02f, 0.03f };

    [Tooltip("List of C/D ratios to test")]
    [SerializeField] private float[] cdRatios = new float[] { 0.5f, 1.0f, 1.5f, 2.0f };

    [Tooltip("Number of repetitions for each amplitude-ratio combination")]
    [SerializeField] private int repetitionsPerCombination = 3;

    [Tooltip("Shuffle trials before starting")]
    [SerializeField] private bool shuffleTrials = true;

    [Header("Participant Info")]
    [SerializeField] private string participantID = "P001";
    [SerializeField] private bool promptForParticipantID = true;

    [Header("NURBS Surface")]
    [Tooltip("The NURBS surface to modify during trials")]
    [SerializeField] private NURBSSurface nurbsSurface;

    [Header("C/D Ratio Manipulator")]
    [Tooltip("Optional C/D ratio manipulator for object positioning")]
    [SerializeField] private CDRatioManipulator cdManipulator;

    [Header("Pacing Dot")]
    [SerializeField] private ObjectMover pacingDot;
    [SerializeField] private float waitAtEndDuration = 0.5f;
    [SerializeField] private float returnSpeedMultiplier = 2.0f;

    [Header("Keyboard Controls")]
    [SerializeField] private KeyCode startExperimentKey = KeyCode.Space;
    [SerializeField] private KeyCode yesKey = KeyCode.Y;
    [SerializeField] private KeyCode noKey = KeyCode.N;
    [SerializeField] private KeyCode confirmKey = KeyCode.O;

    [Header("VR Controller Controls")]
    [SerializeField] private bool enableVRInput = true;
    [SerializeField] private SteamVR_Action_Boolean leftButtonAction;
    [SerializeField] private SteamVR_Action_Boolean rightButtonAction;
    [SerializeField] private SteamVR_Action_Boolean confirmButtonAction;

    [Header("Test Mode")]
    [Tooltip("Skip pacing dot animations for quick testing")]
    [SerializeField] private bool testMode = false;

    [Header("Data Logging")]
    [SerializeField] private string dataOutputFolder = "CDRatioData";
    [SerializeField] private bool logToConsole = true;

    [Header("Debug Info")]
    [SerializeField] private bool isRunning = false;
    [SerializeField] private int currentTrialIndex = 0;
    [SerializeField] private int totalTrials = 0;

    // Trial management
    private List<CDRatioTrial> trials = new List<CDRatioTrial>();
    private CDRatioTrial currentTrial;

    // State machine
    private enum ExperimentState
    {
        Idle,
        ShowingStimulus,
        MovingDot,
        WaitingAtEnd,
        MovingBackToStart,
        WaitingForQ1,
        WaitingForQ2
    }

    private ExperimentState currentState = ExperimentState.Idle;
    private float stateTimer = 0f;

    // Q2 confidence slider state
    private int currentConfidenceValue = 3; // Start at middle value
    private const int MIN_CONFIDENCE = 1;
    private const int MAX_CONFIDENCE = 5;

    // UI Canvas
    private GameObject questionCanvas;
    private UnityEngine.UI.Text questionText;

    // Data logging
    private string csvFilePath;
    private List<CDRatioTrial> completedTrials = new List<CDRatioTrial>();

    void Start()
    {
        // Initialize VR input actions if not assigned
        if (enableVRInput)
        {
            if (leftButtonAction == null)
            {
                leftButtonAction = SteamVR_Actions.default_Teleport;
                Debug.Log("CDRatioTestController: Using default_Teleport for Yes button");
            }

            if (rightButtonAction == null)
            {
                rightButtonAction = SteamVR_Actions.default_GrabGrip;
                Debug.Log("CDRatioTestController: Using default_GrabGrip for No button");
            }

            if (confirmButtonAction == null)
            {
                // Use trigger for confirm
                confirmButtonAction = SteamVR_Actions.default_InteractUI;
                Debug.Log("CDRatioTestController: Using default_InteractUI for Confirm button");
            }
        }

        // Auto-find NURBS surface if not assigned
        if (nurbsSurface == null)
        {
            nurbsSurface = FindObjectOfType<NURBSSurface>();
            if (nurbsSurface != null)
            {
                Debug.Log("CDRatioTestController: Auto-found NURBSSurface");
            }
        }

        // Auto-find pacing dot if not assigned
        if (pacingDot == null)
        {
            pacingDot = FindObjectOfType<ObjectMover>();
            if (pacingDot != null)
            {
                Debug.Log("CDRatioTestController: Auto-found ObjectMover (pacing dot)");
            }
        }

        CreateQuestionCanvas();

        // Prompt for participant ID if enabled
        if (promptForParticipantID)
        {
            // In a real implementation, you would show a UI dialog here
            // For now, we'll use the default participantID from the inspector
            Debug.Log($"CDRatioTestController: Using Participant ID: {participantID}");
        }
    }

    void Update()
    {
        // Start experiment
        if (Input.GetKeyDown(startExperimentKey) && !isRunning)
        {
            StartExperiment();
        }

        // Update state machine
        if (isRunning)
        {
            UpdateExperimentState();
            HandleInput();
        }
    }

    /// <summary>
    /// Initialize and start the experiment.
    /// </summary>
    public void StartExperiment()
    {
        Debug.Log("=== Starting C/D Ratio Experiment ===");

        // Generate trials
        GenerateTrials();

        if (trials.Count == 0)
        {
            Debug.LogError("No trials generated! Check amplitude and ratio arrays.");
            return;
        }

        // Initialize data logging
        InitializeDataLogging();

        // Reset state
        currentTrialIndex = 0;
        isRunning = true;

        // Start first trial
        StartTrial(currentTrialIndex);
    }

    /// <summary>
    /// Generate all trials: Cartesian product of amplitudes × ratios × repetitions, then shuffle.
    /// </summary>
    private void GenerateTrials()
    {
        trials.Clear();

        // Generate Cartesian product
        foreach (float amplitude in nurbsAmplitudes)
        {
            foreach (float ratio in cdRatios)
            {
                // Repeat each combination
                for (int rep = 0; rep < repetitionsPerCombination; rep++)
                {
                    CDRatioTrial trial = new CDRatioTrial
                    {
                        nurbsAmplitude = amplitude,
                        cdRatio = ratio,
                        illusionDetected = false,
                        confidenceLevel = 0,
                        trialNumber = 0 // Will be set after shuffling
                    };
                    trials.Add(trial);
                }
            }
        }

        // Shuffle if enabled
        if (shuffleTrials)
        {
            ShuffleTrials(trials);
            Debug.Log($"Generated and shuffled {trials.Count} trials");
        }
        else
        {
            Debug.Log($"Generated {trials.Count} trials (not shuffled)");
        }

        // Assign trial numbers after shuffling
        for (int i = 0; i < trials.Count; i++)
        {
            trials[i].trialNumber = i + 1;
        }

        totalTrials = trials.Count;

        Debug.Log($"Trial generation complete: {nurbsAmplitudes.Length} amplitudes × {cdRatios.Length} ratios × {repetitionsPerCombination} reps = {totalTrials} trials");
    }

    /// <summary>
    /// Fisher-Yates shuffle algorithm for randomizing trials.
    /// </summary>
    private void ShuffleTrials(List<CDRatioTrial> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int randomIndex = UnityEngine.Random.Range(0, i + 1);
            CDRatioTrial temp = list[i];
            list[i] = list[randomIndex];
            list[randomIndex] = temp;
        }
    }

    /// <summary>
    /// Start a specific trial.
    /// </summary>
    private void StartTrial(int trialIndex)
    {
        if (trialIndex >= trials.Count)
        {
            CompleteExperiment();
            return;
        }

        currentTrial = trials[trialIndex];
        currentTrialIndex = trialIndex;

        Debug.Log($"\n=== Trial {currentTrial.trialNumber}/{totalTrials} ===");
        Debug.Log($"Amplitude: {currentTrial.nurbsAmplitude:F4}m, C/D Ratio: {currentTrial.cdRatio:F3}");

        // Ensure target surface is visible at start of trial
        if (cdManipulator != null)
        {
            cdManipulator.SetTargetSurfaceVisibility(true);
            cdManipulator.SetReferenceSurfaceCollider(true);
        }

        // Apply stimulus
        ApplyStimulus(currentTrial.nurbsAmplitude, currentTrial.cdRatio);

        // Start stimulus presentation
        currentState = ExperimentState.ShowingStimulus;
        stateTimer = 0f;

        // Initialize pacing dot
        if (pacingDot != null)
        {
            pacingDot.TeleportToStart();

            // Ensure dot is visible
            Renderer dotRenderer = pacingDot.GetComponent<Renderer>();
            if (dotRenderer != null && dotRenderer.material != null)
            {
                Color dotColor = dotRenderer.material.color;
                dotColor.a = 1f;
                dotRenderer.material.color = dotColor;
            }
        }
    }

    /// <summary>
    /// Apply stimulus to NURBS surface and optionally to objects via C/D manipulator.
    /// </summary>
    private void ApplyStimulus(float amplitude, float cdRatio)
    {
        // Set NURBS surface amplitude
        if (nurbsSurface != null)
        {
            nurbsSurface.SetHeight(amplitude);
            Debug.Log($"Set NURBS amplitude to {amplitude:F4}m");
        }
        else
        {
            Debug.LogWarning("No NURBS surface assigned!");
        }

        // Apply C/D ratio via manipulator (if assigned)
        if (cdManipulator != null)
        {
            cdManipulator.ApplyRatio(cdRatio);
        }
    }

    /// <summary>
    /// Update the experiment state machine.
    /// </summary>
    private void UpdateExperimentState()
    {
        stateTimer += Time.deltaTime;

        switch (currentState)
        {
            case ExperimentState.ShowingStimulus:
                if (testMode)
                {
                    // Skip pacing dot in test mode
                    currentState = ExperimentState.WaitingForQ1;
                    ShowQuestion1();
                    stateTimer = 0f;
                }
                else
                {
                    // Start moving pacing dot
                    currentState = ExperimentState.MovingDot;
                    stateTimer = 0f;

                    if (pacingDot != null)
                    {
                        pacingDot.MoveToDestination(() => {
                            currentState = ExperimentState.WaitingAtEnd;
                            stateTimer = 0f;
                        });
                    }
                    else
                    {
                        // No pacing dot, skip to Q1
                        currentState = ExperimentState.WaitingForQ1;
                        ShowQuestion1();
                        stateTimer = 0f;
                    }
                }
                break;

            case ExperimentState.MovingDot:
                // Handled by callback
                break;

            case ExperimentState.WaitingAtEnd:
                if (stateTimer >= waitAtEndDuration)
                {
                    if (pacingDot != null)
                    {
                        // Hide target surface and disable reference surface collider when moving back
                        if (cdManipulator != null)
                        {
                            cdManipulator.SetTargetSurfaceVisibility(false);
                            cdManipulator.SetReferenceSurfaceCollider(false);
                        }

                        // Move back to start with faster speed
                        currentState = ExperimentState.MovingBackToStart;
                        float originalSpeed = pacingDot.moveSpeed;
                        pacingDot.moveSpeed = originalSpeed * returnSpeedMultiplier;

                        pacingDot.ResetToStart(() => {
                            // Restore original speed
                            pacingDot.moveSpeed = originalSpeed;

                            // Flatten surface and proceed to Q1
                            if (nurbsSurface != null)
                            {
                                nurbsSurface.SetHeight(0);
                            }

                            // Re-enable reference surface collider for next trial
                            if (cdManipulator != null)
                            {
                                cdManipulator.SetReferenceSurfaceCollider(true);
                            }

                            currentState = ExperimentState.WaitingForQ1;
                            ShowQuestion1();
                            stateTimer = 0f;
                        });
                    }
                    else
                    {
                        // No pacing dot, skip to Q1
                        if (nurbsSurface != null)
                        {
                            nurbsSurface.SetHeight(0);
                        }
                        currentState = ExperimentState.WaitingForQ1;
                        ShowQuestion1();
                        stateTimer = 0f;
                    }
                }
                break;

            case ExperimentState.MovingBackToStart:
                // Handled by callback
                break;

            case ExperimentState.WaitingForQ1:
                // Input handled in HandleInput()
                break;

            case ExperimentState.WaitingForQ2:
                // Input handled in HandleInput()
                break;
        }
    }

    /// <summary>
    /// Handle user input based on current state.
    /// </summary>
    private void HandleInput()
    {
        // Get VR input
        bool yesPressed = false;
        bool noPressed = false;
        bool confirmPressed = false;

        if (enableVRInput && leftButtonAction != null && rightButtonAction != null)
        {
            yesPressed = leftButtonAction.GetStateDown(SteamVR_Input_Sources.LeftHand);
            noPressed = rightButtonAction.GetStateDown(SteamVR_Input_Sources.LeftHand);

            if (confirmButtonAction != null)
            {
                confirmPressed = confirmButtonAction.GetStateDown(SteamVR_Input_Sources.LeftHand);
            }
        }

        // Get keyboard input
        bool yesKey = Input.GetKeyDown(this.yesKey);
        bool noKey = Input.GetKeyDown(this.noKey);
        bool confirmKey = Input.GetKeyDown(this.confirmKey);

        // Handle Q1: "Do you feel the illusion?" (Y/N)
        if (currentState == ExperimentState.WaitingForQ1)
        {
            if (yesPressed || yesKey)
            {
                ProcessQ1Response(true);
            }
            else if (noPressed || noKey)
            {
                ProcessQ1Response(false);
            }
        }

        // Handle Q2: Confidence slider (Y=left/decrease, N=right/increase, O=confirm)
        else if (currentState == ExperimentState.WaitingForQ2)
        {
            if (yesPressed || yesKey)
            {
                // Move slider left (decrease)
                currentConfidenceValue = Mathf.Max(MIN_CONFIDENCE, currentConfidenceValue - 1);
                UpdateQuestion2Display();
            }
            else if (noPressed || noKey)
            {
                // Move slider right (increase)
                currentConfidenceValue = Mathf.Min(MAX_CONFIDENCE, currentConfidenceValue + 1);
                UpdateQuestion2Display();
            }
            else if (confirmPressed || confirmKey)
            {
                // Confirm selection
                ProcessQ2Response(currentConfidenceValue);
            }
        }
    }

    /// <summary>
    /// Process response to Q1: "Do you feel the illusion?"
    /// </summary>
    private void ProcessQ1Response(bool illusionDetected)
    {
        currentTrial.illusionDetected = illusionDetected;

        Debug.Log($"Q1 Response: Illusion {(illusionDetected ? "DETECTED" : "NOT DETECTED")}");

        // Move to Q2
        currentState = ExperimentState.WaitingForQ2;
        ShowQuestion2();
        stateTimer = 0f;
    }

    /// <summary>
    /// Process response to Q2: Confidence level
    /// </summary>
    private void ProcessQ2Response(int confidenceLevel)
    {
        currentTrial.confidenceLevel = confidenceLevel;

        Debug.Log($"Q2 Response: Confidence Level = {confidenceLevel}");

        // Hide question canvas
        if (questionCanvas != null)
        {
            questionCanvas.SetActive(false);
        }

        // Log trial data
        LogTrialData(currentTrial);

        // Move to next trial
        currentTrialIndex++;

        if (currentTrialIndex < trials.Count)
        {
            StartTrial(currentTrialIndex);
        }
        else
        {
            CompleteExperiment();
        }
    }

    /// <summary>
    /// Show Question 1 UI
    /// </summary>
    private void ShowQuestion1()
    {
        if (questionCanvas != null && questionText != null)
        {
            questionText.text = "Do you feel the illusion?\n\nPress Y (Yes) or N (No)";
            questionCanvas.SetActive(true);
        }

        Debug.Log("Waiting for Q1 response: Do you feel the illusion? (Y/N)");
    }

    /// <summary>
    /// Show Question 2 UI
    /// </summary>
    private void ShowQuestion2()
    {
        // Reset confidence slider to middle value
        currentConfidenceValue = 3;

        UpdateQuestion2Display();

        Debug.Log("Waiting for Q2 response: How confident are you? (1-5)");
        Debug.Log("Use Y (decrease), N (increase), O (confirm)");
    }

    /// <summary>
    /// Update Question 2 display with current confidence value
    /// </summary>
    private void UpdateQuestion2Display()
    {
        if (questionCanvas != null && questionText != null)
        {
            // Create visual slider representation
            string slider = GenerateSliderVisual(currentConfidenceValue);

            questionText.text = $"How confident are you?\n\n{slider}\n\nY (Left) | N (Right) | O (Confirm)";
            questionCanvas.SetActive(true);
        }
    }

    /// <summary>
    /// Generate visual representation of confidence slider
    /// </summary>
    private string GenerateSliderVisual(int currentValue)
    {
        string slider = "";
        for (int i = MIN_CONFIDENCE; i <= MAX_CONFIDENCE; i++)
        {
            if (i == currentValue)
            {
                slider += $"[{i}] ";
            }
            else
            {
                slider += $" {i}  ";
            }
        }
        return slider;
    }

    /// <summary>
    /// Create UI canvas for questions
    /// </summary>
    private void CreateQuestionCanvas()
    {
        GameObject canvasGO = new GameObject("CDRatioQuestionCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 100;

        // Position canvas in front of user
        canvasGO.transform.position = new Vector3(0, 0.5f, 2f);
        canvasGO.transform.rotation = Quaternion.identity;
        canvasGO.transform.localScale = Vector3.one * 0.001f;

        canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
        canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        RectTransform canvasRect = canvasGO.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(3840, 1080);

        // Create background panel
        GameObject panelGO = new GameObject("Panel");
        panelGO.transform.SetParent(canvasGO.transform, false);

        UnityEngine.UI.Image panelImage = panelGO.AddComponent<UnityEngine.UI.Image>();
        panelImage.color = new Color(0, 0, 0, 0.8f);

        RectTransform panelRect = panelGO.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        // Create text
        GameObject textGO = new GameObject("QuestionText");
        textGO.transform.SetParent(panelGO.transform, false);

        questionText = textGO.AddComponent<UnityEngine.UI.Text>();
        questionText.text = "";
        questionText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        questionText.fontSize = 70;
        questionText.color = Color.white;
        questionText.alignment = TextAnchor.MiddleCenter;

        RectTransform textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.1f, 0.2f);
        textRect.anchorMax = new Vector2(0.9f, 0.8f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        questionCanvas = canvasGO;
        questionCanvas.SetActive(false);
    }

    /// <summary>
    /// Initialize data logging (create CSV file with headers)
    /// </summary>
    private void InitializeDataLogging()
    {
        // Create output directory
        string outputDir = Path.Combine(Application.persistentDataPath, dataOutputFolder);
        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        // Create CSV file with timestamp
        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = $"CDRatio_{participantID}_{timestamp}.csv";
        csvFilePath = Path.Combine(outputDir, fileName);

        // Write CSV header
        string header = "ParticipantID,TrialNumber,NurbsAmplitude,CDRatio,IllusionDetected,ConfidenceLevel";
        File.WriteAllText(csvFilePath, header + "\n");

        Debug.Log($"Data logging initialized: {csvFilePath}");
    }

    /// <summary>
    /// Log trial data to CSV file
    /// </summary>
    private void LogTrialData(CDRatioTrial trial)
    {
        completedTrials.Add(trial);

        // Format CSV row
        string illusionStr = trial.illusionDetected ? "Yes" : "No";
        string row = $"{participantID},{trial.trialNumber},{trial.nurbsAmplitude:F4},{trial.cdRatio:F3},{illusionStr},{trial.confidenceLevel}";

        // Append to CSV file
        File.AppendAllText(csvFilePath, row + "\n");

        if (logToConsole)
        {
            Debug.Log($"Trial data logged: {row}");
        }
    }

    /// <summary>
    /// Complete the experiment
    /// </summary>
    private void CompleteExperiment()
    {
        isRunning = false;
        currentState = ExperimentState.Idle;

        Debug.Log("\n=== Experiment Complete! ===");
        Debug.Log($"Total trials completed: {completedTrials.Count}/{totalTrials}");
        Debug.Log($"Data saved to: {csvFilePath}");

        // Hide question canvas
        if (questionCanvas != null)
        {
            questionCanvas.SetActive(false);
        }

        // Flatten surface
        if (nurbsSurface != null)
        {
            nurbsSurface.SetHeight(0);
        }
    }

    /// <summary>
    /// GUI display for experiment status
    /// </summary>
    void OnGUI()
    {
        if (!isRunning && currentState == ExperimentState.Idle)
        {
            GUILayout.BeginArea(new Rect(10, 10, 400, 150));
            GUILayout.Label("C/D Ratio Test Controller", new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold });
            GUILayout.Label($"Participant: {participantID}");
            GUILayout.Label($"Total Trials: {totalTrials}");
            GUILayout.Label($"Press {startExperimentKey} to start experiment");
            GUILayout.EndArea();
            return;
        }

        if (isRunning)
        {
            GUILayout.BeginArea(new Rect(10, 10, 400, 250));

            GUILayout.Label("C/D Ratio Test Controller", new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold });

            if (testMode)
            {
                GUILayout.Label("[TEST MODE ACTIVE]", new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = Color.yellow } });
            }

            GUILayout.Label($"Participant: {participantID}");
            GUILayout.Label($"Trial {currentTrial.trialNumber}/{totalTrials}");

            if (currentTrial != null)
            {
                GUILayout.Label($"Amplitude: {currentTrial.nurbsAmplitude:F4}m");
                GUILayout.Label($"C/D Ratio: {currentTrial.cdRatio:F3}");
            }

            // State-specific messages
            switch (currentState)
            {
                case ExperimentState.ShowingStimulus:
                    GUILayout.Label("Presenting stimulus...");
                    break;
                case ExperimentState.MovingDot:
                    GUILayout.Label("Pacing dot moving...");
                    break;
                case ExperimentState.WaitingAtEnd:
                    GUILayout.Label("Waiting at end position...");
                    break;
                case ExperimentState.MovingBackToStart:
                    GUILayout.Label("Pacing dot returning...");
                    break;
                case ExperimentState.WaitingForQ1:
                    GUILayout.Label($"Q1: Press {yesKey} (Yes) or {noKey} (No)");
                    break;
                case ExperimentState.WaitingForQ2:
                    GUILayout.Label($"Q2: {yesKey} (Left) | {noKey} (Right) | {confirmKey} (Confirm)");
                    GUILayout.Label($"Current value: {currentConfidenceValue}");
                    break;
            }

            GUILayout.EndArea();
        }
    }
}
