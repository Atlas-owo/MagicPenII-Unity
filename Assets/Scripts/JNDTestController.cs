using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Valve.VR;

[System.Serializable]
public class TestParameters
{
    [Header("Test Identification")]
    public string testName = "Test";
    
    [Header("Stimulus Range")]
    public float minimumValue = 0.0f;
    public float maximumValue = 1.0f;
    
    [Header("Reference Stimulus")]
    public float referenceStimulus = 0.5f;
    
    [Header("Staircase Configuration")]
    public int numberOfSteps = 10;
    public int startStepSequ1 = 0;
    public int startStepSequ2 = 8;
    public int stopAmount = 5;
    public int numberThresholdPoints = 3;
    
    [Header("Step Sizes")]
    public int stepsUp = 1;
    public int stepsDown = 1;
    public int stepsUpStartEarly = 1;
    public int stepsDownStartEarly = 1;
    public int quickStartEarlyUntilReversals = 0;
    public int stepsUpStartLate = 1;
    public int stepsDownStartLate = 1;
    public int quickStartLateUntilReversals = 0;
    
    [Header("Advanced Settings")]
    public bool stopCriterionReversals = true;
    public bool strictLimits = false;
    public bool singleSequence = false;
    public bool singleSequenceUp = false;
    public string plotTitle = "";
    
    [Header("Experiment Info")]
    public string experimentName = "JND_Experiment";
    public string conditionName = "Condition";
}

public class JNDTestController : MonoBehaviour
{
    [Header("Test Configuration")]
    [SerializeField] private TestParameters[] testConfigurations;
    [SerializeField] private int participantNumber = 1;
    
    [Header("Control Settings")]
    [SerializeField] private bool autoStart = false;
    [SerializeField] private float delayBetweenTests = 2.0f;
    [SerializeField] private bool randomizeTestOrder = true;
    
    [Header("Keyboard Controls")]
    public KeyCode startTestsKey = KeyCode.Space;
    public KeyCode nextTestKey = KeyCode.Return;
    public KeyCode keycodeDifferenceDetected = KeyCode.Y;
    public KeyCode keycodeDifferenceNotDetected = KeyCode.N;

    [Header("VR Controller Controls")]
    [SerializeField] private bool enableVRInput = true;
    [SerializeField] private SteamVR_Action_Boolean leftButtonAction;
    [SerializeField] private SteamVR_Action_Boolean rightButtonAction;
    
    [Header("2AFC Settings")]
    [SerializeField] private float stimulusDuration = 2.0f;
    [SerializeField] private float delayBetweenStimuli = 1.0f;
    [SerializeField] private bool showComparisonCanvas = true;

    public enum SurfaceType { Gaussian, NURBS }

    [Header("Surface Settings")]
    [SerializeField] private SurfaceType surfaceType = SurfaceType.Gaussian;
    [SerializeField] private GaussianSurface gaussianSurface;
    [SerializeField] private NURBSSurface nurbsSurface;
    [SerializeField] private bool useSurface = true;

    [Header("Pacing Dot")]
    [SerializeField] private ObjectMover pacingDot;
    [SerializeField] private int blinkCount = 3;
    [SerializeField] private float waitAtEndDuration = 0.5f;
    [SerializeField] private float waitAfterMovementDuration = 1.0f;
    [SerializeField] private float returnSpeedMultiplier = 2.0f;
    
    [Header("Test Mode")]
    [SerializeField] private bool testMode = false;

    [Header("Visibility Settings")]
    [SerializeField] private bool showPen = true;
    [SerializeField] private GameObject penObject;
    [SerializeField] private bool showSurface = true;

    [Header("Debug Info")]
    [SerializeField] private int currentTestIndex = 0;
    [SerializeField] private bool isRunning = false;
    [SerializeField] private bool waitingForNextTest = false;
    
    private TestManager testManager;
    private bool staircaseInitialized = false;
    private int[] randomizedTestOrder; // Array of randomized indices
    private int currentOrderPosition = 0; // Current position in randomized order
    
    // 2AFC State Management
    private enum ComparisonState
    {
        Idle,
        ShowingFirstStimulus,
        BlinkingDot,
        MovingDot,
        WaitingAtEnd,
        MovingBackToStart,
        WaitingAfterMovement,
        DelayBetweenStimuli,
        ShowingSecondStimulus,
        WaitingForResponse
    }
    
    private ComparisonState currentComparisonState = ComparisonState.Idle;
    private float currentTestStimulus;
    private float currentReferenceStimulus;
    private bool referenceIsFirst;
    private float stateTimer;
    private GameObject comparisonCanvas;
    private bool isShowingFirstStimulus = true; // Track which stimulus cycle we're in
    
    void Start()
    {
        testManager = FindObjectOfType<TestManager>();
        if (testManager != null)
        {
            testManager.enableTesting = false;
        }
        
        // Auto-find surface based on type
        if (surfaceType == SurfaceType.Gaussian && gaussianSurface == null)
        {
            gaussianSurface = FindObjectOfType<GaussianSurface>();
        }
        else if (surfaceType == SurfaceType.NURBS && nurbsSurface == null)
        {
            nurbsSurface = FindObjectOfType<NURBSSurface>();
        }
        
        // Initialize VR input actions if not assigned
        if (enableVRInput)
        {
            if (leftButtonAction == null)
            {
                leftButtonAction = SteamVR_Actions.default_Teleport;  // Usually mapped to touchpad/trigger
                Debug.Log("JNDTestController: Using default_Teleport for left button (Yes) - typically touchpad");
            }
            
            if (rightButtonAction == null)
            {
                rightButtonAction = SteamVR_Actions.default_GrabGrip;  // Usually grip button
                Debug.Log("JNDTestController: Using default_GrabGrip for right button (No) - typically grip");
            }
            
            Debug.Log("JNDTestController: VR Controller input enabled - Left Controller: Teleport action = Yes, Grip action = No");
            Debug.Log("JNDTestController: If buttons don't work, check SteamVR Input Bindings or assign different actions in inspector");
        }
        
        if (autoStart && testConfigurations.Length > 0)
        {
            StartTestSequence();
        }

        CreateComparisonCanvas();
        UpdateVisibility();
    }

    void OnValidate()
    {
        // Update visibility when values change in Inspector
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        // Update pen visibility
        if (penObject != null)
        {
            MeshRenderer[] penRenderers = penObject.GetComponentsInChildren<MeshRenderer>();
            foreach (var renderer in penRenderers)
            {
                renderer.enabled = showPen;
            }
        }

        // Update surface visibility based on current surface type
        if (surfaceType == SurfaceType.Gaussian && gaussianSurface != null)
        {
            MeshRenderer gaussianRenderer = gaussianSurface.GetComponent<MeshRenderer>();
            if (gaussianRenderer != null)
            {
                gaussianRenderer.enabled = showSurface;
            }
        }
        else if (surfaceType == SurfaceType.NURBS && nurbsSurface != null)
        {
            MeshRenderer nurbsRenderer = nurbsSurface.GetComponent<MeshRenderer>();
            if (nurbsRenderer != null)
            {
                nurbsRenderer.enabled = showSurface;
            }
        }
    }

    // DEPRECATED: No longer used - we now flatten surface instead of disabling collider
    // Kept for potential future use
    private void DisableSurfaceCollider()
    {
        if (surfaceType == SurfaceType.NURBS && nurbsSurface != null)
        {
            MeshCollider meshCollider = nurbsSurface.GetComponent<MeshCollider>();
            if (meshCollider != null)
            {
                meshCollider.enabled = false;
                Debug.Log("NURBS surface collider disabled");
            }
        }
        else if (surfaceType == SurfaceType.Gaussian && gaussianSurface != null)
        {
            MeshCollider meshCollider = gaussianSurface.GetComponent<MeshCollider>();
            if (meshCollider != null)
            {
                meshCollider.enabled = false;
                Debug.Log("Gaussian surface collider disabled");
            }
        }
    }

    // DEPRECATED: No longer used - collider remains enabled throughout trial
    // Kept for potential future use
    private void EnableSurfaceCollider()
    {
        if (surfaceType == SurfaceType.NURBS && nurbsSurface != null)
        {
            MeshCollider meshCollider = nurbsSurface.GetComponent<MeshCollider>();
            if (meshCollider != null)
            {
                meshCollider.enabled = true;
                Debug.Log("NURBS surface collider enabled");
            }
        }
        else if (surfaceType == SurfaceType.Gaussian && gaussianSurface != null)
        {
            MeshCollider meshCollider = gaussianSurface.GetComponent<MeshCollider>();
            if (meshCollider != null)
            {
                meshCollider.enabled = true;
                Debug.Log("Gaussian surface collider enabled");
            }
        }
    }
    
    void Update()
    {
        if (Input.GetKeyDown(startTestsKey) && !isRunning)
        {
            StartTestSequence();
        }
        
        if (Input.GetKeyDown(nextTestKey) && waitingForNextTest)
        {
            StartNextTest();
        }
        
        if (isRunning && staircaseInitialized)
        {
            UpdateComparisonState();
            HandleComparisonInput();
            CheckTestCompletion();
        }
    }
    
    public void StartTestSequence()
    {
        if (testConfigurations == null || testConfigurations.Length == 0)
        {
            Debug.LogError("No test configurations found!");
            return;
        }
        
        // Initialize test order
        randomizedTestOrder = new int[testConfigurations.Length];
        for (int i = 0; i < testConfigurations.Length; i++)
        {
            randomizedTestOrder[i] = i;
        }
        
        // Shuffle the order if randomization is enabled
        if (randomizeTestOrder)
        {
            ShuffleArray(randomizedTestOrder);
            Debug.Log($"Starting randomized test sequence with {testConfigurations.Length} tests");
            Debug.Log($"Randomized order: [{string.Join(", ", randomizedTestOrder)}]");
        }
        else
        {
            Debug.Log($"Starting sequential test sequence with {testConfigurations.Length} tests");
        }
        
        currentOrderPosition = 0;
        isRunning = true;
        waitingForNextTest = false;
        
        InitializeCurrentTest();
    }
    
    // Fisher-Yates shuffle algorithm
    private void ShuffleArray(int[] array)
    {
        for (int i = array.Length - 1; i > 0; i--)
        {
            int randomIndex = UnityEngine.Random.Range(0, i + 1);
            int temp = array[i];
            array[i] = array[randomIndex];
            array[randomIndex] = temp;
        }
    }
    
    private void InitializeCurrentTest()
    {
        if (currentOrderPosition >= randomizedTestOrder.Length)
        {
            CompleteAllTests();
            return;
        }
        
        // Get the actual test index from the randomized order
        currentTestIndex = randomizedTestOrder[currentOrderPosition];
        TestParameters currentTest = testConfigurations[currentTestIndex];
        
        Debug.Log($"Initializing Test {currentOrderPosition + 1}/{testConfigurations.Length}: {currentTest.testName} (Configuration Index: {currentTestIndex})");
        
        string conditionName = $"{currentTest.conditionName}_{currentTestIndex + 1}";
        string plotTitle = string.IsNullOrEmpty(currentTest.plotTitle) ? 
            $"{currentTest.testName} - Participant {participantNumber}" : 
            currentTest.plotTitle;
        
        StaircaseProcedure.SP.Init(
            minimumValue: currentTest.minimumValue,
            maximumValue: currentTest.maximumValue,
            numberOfSteps: currentTest.numberOfSteps,
            startStepSequ1: currentTest.startStepSequ1,
            startStepSequ2: currentTest.startStepSequ2,
            stepsUp: currentTest.stepsUp,
            stepsDown: currentTest.stepsDown,
            stepsUpStartEarly: currentTest.stepsUpStartEarly,
            stepsDownStartEarly: currentTest.stepsDownStartEarly,
            quickStartEarlyUntilReversals: currentTest.quickStartEarlyUntilReversals,
            stepsUpStartLate: currentTest.stepsUpStartLate,
            stepsDownStartLate: currentTest.stepsDownStartLate,
            quickStartLateUntilReversals: currentTest.quickStartLateUntilReversals,
            stopAmount: currentTest.stopAmount,
            numberThresholdPoints: currentTest.numberThresholdPoints,
            experimentName: currentTest.experimentName,
            conditionName: conditionName,
            numberParticipant: participantNumber,
            stopCriterionReversals: currentTest.stopCriterionReversals,
            strictLimits: currentTest.strictLimits,
            singleSequence: currentTest.singleSequence,
            singleSequenceUp: currentTest.singleSequenceUp,
            plotTitle: plotTitle
        );
        
        float initialStimulus = StaircaseProcedure.SP.GetNextStimulus();
        staircaseInitialized = true;
        waitingForNextTest = false;
        
        StartComparisonTrial(initialStimulus, currentTest.referenceStimulus);
        
        Debug.Log($"2AFC Test initialized. Range: {currentTest.minimumValue} to {currentTest.maximumValue}, Reference: {currentTest.referenceStimulus}");
    }
    
    private void HandleComparisonInput()
    {
        // In test mode, allow input during any relevant state
        // In normal mode, only allow input during WaitingForResponse
        bool canAcceptInput = testMode ? 
            (currentComparisonState == ComparisonState.ShowingFirstStimulus || 
             currentComparisonState == ComparisonState.DelayBetweenStimuli ||
             currentComparisonState == ComparisonState.ShowingSecondStimulus ||
             currentComparisonState == ComparisonState.WaitingForResponse) :
            (currentComparisonState == ComparisonState.WaitingForResponse);
            
        if (canAcceptInput)
        {
            // VR Controller Input (Left Controller)
            bool yesPressed = false;
            bool noPressed = false;
            
            if (enableVRInput && leftButtonAction != null && rightButtonAction != null)
            {
                yesPressed = leftButtonAction.GetStateDown(SteamVR_Input_Sources.LeftHand);
                noPressed = rightButtonAction.GetStateDown(SteamVR_Input_Sources.LeftHand);
            }
            
            // Keyboard Input (fallback)
            bool yesKey = Input.GetKeyDown(keycodeDifferenceDetected);
            bool noKey = Input.GetKeyDown(keycodeDifferenceNotDetected);
            
            if (yesPressed || yesKey)
            {
                ProcessComparisonResponse(true);
            }
            else if (noPressed || noKey)
            {
                ProcessComparisonResponse(false);
            }
        }
    }
    
    private void ProcessComparisonResponse(bool differenceDetected)
    {
        if (StaircaseProcedure.SP.IsFinished())
        {
            Debug.Log("Current staircase is already finished.");
            return;
        }
        
        // Hide comparison canvas
        if (comparisonCanvas != null)
        {
            comparisonCanvas.SetActive(false);
        }
        
        // Calculate current offset (reverse of the test stimulus calculation)
        float currentOffset = currentTestStimulus - currentReferenceStimulus;
        
        // Get the transformed response for the staircase
        bool staircaseResponse = GetStaircaseResponse(differenceDetected, currentOffset);
        
        // Log detailed information for debugging
        Debug.Log($"User Response: Difference {(differenceDetected ? "DETECTED" : "NOT DETECTED")}");
        Debug.Log($"Current Values - Test: {currentTestStimulus}, Reference: {currentReferenceStimulus}, Offset: {currentOffset}");
        Debug.Log($"Staircase Response: {(staircaseResponse ? "DETECTED" : "NOT DETECTED")} (transformed from user response)");
        
        StaircaseProcedure.SP.TrialFinished(staircaseResponse);
        
        if (StaircaseProcedure.SP.IsFinished())
        {
            float threshold = StaircaseProcedure.SP.GetThreshold();
            Debug.Log($"2AFC Test {currentTestIndex + 1} completed. Threshold: {threshold}");
            currentComparisonState = ComparisonState.Idle;
        }
        else
        {
            float nextOffset = StaircaseProcedure.SP.GetNextStimulus();
            TestParameters currentTest = testConfigurations[currentTestIndex];
            
            // Log the direction of change to verify correct behavior
            string direction;
            if (differenceDetected)
            {
                direction = Math.Abs(nextOffset) < Math.Abs(currentOffset) ? "CLOSER to reference" : "FURTHER from reference";
            }
            else
            {
                direction = Math.Abs(nextOffset) > Math.Abs(currentOffset) ? "FURTHER from reference" : "CLOSER to reference";
            }
            
            Debug.Log($"Next offset: {nextOffset} (moving {direction})");
            Debug.Log("---");
            
            StartComparisonTrial(nextOffset, currentTest.referenceStimulus);
        }
    }
    
    private void CheckTestCompletion()
    {
        if (staircaseInitialized && StaircaseProcedure.SP.IsFinished())
        {
            FinishCurrentTest();
        }
    }
    
    private void FinishCurrentTest()
    {
        TestParameters completedTest = testConfigurations[currentTestIndex];
        Debug.Log($"Test '{completedTest.testName}' completed successfully!");
        
        staircaseInitialized = false;
        currentOrderPosition++;
        
        if (currentOrderPosition < randomizedTestOrder.Length)
        {
            waitingForNextTest = true;
            Debug.Log($"Press {nextTestKey} to start the next test, or wait {delayBetweenTests} seconds for auto-start");
            StartCoroutine(AutoStartNextTest());
        }
        else
        {
            CompleteAllTests();
        }
    }
    
    private IEnumerator AutoStartNextTest()
    {
        yield return new WaitForSeconds(delayBetweenTests);
        
        if (waitingForNextTest)
        {
            StartNextTest();
        }
    }
    
    private void StartNextTest()
    {
        if (currentOrderPosition < randomizedTestOrder.Length)
        {
            InitializeCurrentTest();
        }
    }
    
    private void CompleteAllTests()
    {
        isRunning = false;
        waitingForNextTest = false;
        staircaseInitialized = false;
        
        Debug.Log("All tests completed successfully!");
        
        if (testManager != null)
        {
            testManager.enableTesting = true;
        }
    }
    
    public void AddTestConfiguration(TestParameters newTest)
    {
        List<TestParameters> testList = new List<TestParameters>(testConfigurations);
        testList.Add(newTest);
        testConfigurations = testList.ToArray();
    }
    
    public int GetCurrentTestIndex()
    {
        return currentTestIndex;
    }
    
    public int GetTotalTestCount()
    {
        return testConfigurations != null ? testConfigurations.Length : 0;
    }
    
    public bool IsRunning()
    {
        return isRunning;
    }
    
    public TestParameters GetCurrentTestParameters()
    {
        if (currentTestIndex < testConfigurations.Length)
        {
            return testConfigurations[currentTestIndex];
        }
        return null;
    }
    
    void OnGUI()
    {
        if (!isRunning) return;
        
        GUILayout.BeginArea(new Rect(10, 10, 400, 200));
        
        GUILayout.Label($"JND Test Controller", new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold });
        
        // Test mode indicator
        if (testMode)
        {
            GUILayout.Label($"[TEST MODE ACTIVE]", new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = Color.yellow } });
        }
        
        GUILayout.Label($"Test {currentOrderPosition + 1} of {testConfigurations.Length}");
        
        if (randomizedTestOrder != null && currentOrderPosition < randomizedTestOrder.Length)
        {
            int actualTestIndex = randomizedTestOrder[currentOrderPosition];
            TestParameters current = testConfigurations[actualTestIndex];
            GUILayout.Label($"Current Test: {current.testName} (Config #{actualTestIndex + 1})");
            GUILayout.Label($"Participant: {participantNumber}");
            GUILayout.Label($"Range: {current.minimumValue} - {current.maximumValue}");
            if (randomizeTestOrder)
            {
                GUILayout.Label($"Order: Randomized");
            }
        }
        
        if (waitingForNextTest)
        {
            GUILayout.Label($"Press {nextTestKey} for next test", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
        }
        else if (staircaseInitialized)
        {
            if (testMode)
            {
                // Test mode instructions - show different messages
                switch (currentComparisonState)
                {
                    case ComparisonState.ShowingFirstStimulus:
                        GUILayout.Label("Presenting first stimulus... (Press Y/N anytime)");
                        break;
                    case ComparisonState.DelayBetweenStimuli:
                        GUILayout.Label("Preparing second stimulus... (Press Y/N anytime)");
                        break;
                    case ComparisonState.ShowingSecondStimulus:
                        GUILayout.Label("Presenting second stimulus... (Press Y/N anytime)");
                        break;
                    case ComparisonState.WaitingForResponse:
                        GUILayout.Label($"Do you feel the difference? Press {keycodeDifferenceDetected} (Yes) or {keycodeDifferenceNotDetected} (No)");
                        break;
                    default:
                        GUILayout.Label("Ready for input...");
                        break;
                }
            }
            else
            {
                // Normal mode instructions - original behavior
                switch (currentComparisonState)
                {
                    case ComparisonState.ShowingFirstStimulus:
                        GUILayout.Label(isShowingFirstStimulus ? "Presenting first stimulus..." : "Presenting second stimulus...");
                        break;
                    case ComparisonState.BlinkingDot:
                        GUILayout.Label("Pacing dot blinking...");
                        break;
                    case ComparisonState.MovingDot:
                        GUILayout.Label("Pacing dot moving to end...");
                        break;
                    case ComparisonState.WaitingAtEnd:
                        GUILayout.Label("Waiting at end position...");
                        break;
                    case ComparisonState.MovingBackToStart:
                        GUILayout.Label("Pacing dot returning to start...");
                        break;
                    case ComparisonState.WaitingAfterMovement:
                        GUILayout.Label("Waiting after movement...");
                        break;
                    case ComparisonState.DelayBetweenStimuli:
                        GUILayout.Label("Preparing second stimulus...");
                        break;
                    case ComparisonState.ShowingSecondStimulus:
                        GUILayout.Label("Presenting second stimulus...");
                        break;
                    case ComparisonState.WaitingForResponse:
                        GUILayout.Label($"Do you feel the difference? Press {keycodeDifferenceDetected} (Yes) or {keycodeDifferenceNotDetected} (No)");
                        break;
                }
            }
        }
        
        GUILayout.EndArea();
    }
    
    private void UpdateSurfaceHeight(float stimulusValue)
    {
        if (!useSurface) return;

        switch (surfaceType)
        {
            case SurfaceType.Gaussian:
                if (gaussianSurface != null)
                {
                    gaussianSurface.SetHeight(stimulusValue / 1000);
                    Debug.Log($"Updated Gaussian surface height to: {stimulusValue}");
                }
                break;

            case SurfaceType.NURBS:
                if (nurbsSurface != null)
                {
                    nurbsSurface.SetHeight((stimulusValue / 1000) + 0.0025f);
                    Debug.Log($"Updated NURBS surface height to: {stimulusValue}");
                }
                break;
        }
    }
    
    private bool GetStaircaseResponse(bool userDetectedDifference, float currentOffset)
    {
        // Transform user response based on reference-centered logic
        if (userDetectedDifference) 
        {
            // User detected difference -> move test stimulus closer to reference (offset toward 0)
            // If offset is positive, tell staircase "detected" so it decreases
            // If offset is negative, tell staircase "not detected" so it increases (toward zero)
            return currentOffset > 0;
        }
        else 
        {
            // User didn't detect difference -> move test stimulus away from reference (offset away from 0)
            // If offset is positive, tell staircase "not detected" so it increases (more positive)
            // If offset is negative/zero, tell staircase "detected" so it decreases (more negative)
            return currentOffset <= 0;
        }
    }
    
    private void CreateComparisonCanvas()
    {
        // Create a VR-compatible UI canvas for the comparison question
        GameObject canvasGO = new GameObject("ComparisonCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 100;
        
        // Position the canvas in front of the user in world space
        canvasGO.transform.position = new Vector3(0, 0.5f, 1.5f); // 2 meters in front, 1.5m high
        canvasGO.transform.rotation = Quaternion.identity;
        canvasGO.transform.localScale = Vector3.one * 0.001f; // Scale down for proper world space size
        
        canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
        canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        
        // Set canvas size for world space
        RectTransform canvasRect = canvasGO.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(3840, 1080); // Large size that gets scaled down
        
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
        
        UnityEngine.UI.Text textComponent = textGO.AddComponent<UnityEngine.UI.Text>();
        textComponent.text = "(Please answer after you move back to green dot)\nDo you feel the difference between these two heights?\n\nPress Y (Yes) or N (No)";
        textComponent.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        textComponent.fontSize = 70;
        textComponent.color = Color.white;
        textComponent.alignment = TextAnchor.MiddleCenter;
        
        RectTransform textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.1f, 0.2f);
        textRect.anchorMax = new Vector2(0.9f, 0.8f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        
        comparisonCanvas = canvasGO;
        comparisonCanvas.SetActive(false);
    }
    
    private void StartComparisonTrial(float testStimulusOffset, float referenceStimulus)
    {
        // Calculate actual test stimulus as reference + offset from staircase
        currentTestStimulus = testStimulusOffset + referenceStimulus;
        currentReferenceStimulus = referenceStimulus;
        referenceIsFirst = UnityEngine.Random.value > 0.5f;
        isShowingFirstStimulus = true; // Reset stimulus tracking

        currentComparisonState = ComparisonState.ShowingFirstStimulus;
        stateTimer = 0f;

        // Show first stimulus (reference or test, randomly chosen)
        float firstStimulus = referenceIsFirst ? referenceStimulus : currentTestStimulus;
        UpdateSurfaceHeight(firstStimulus);

        Debug.Log($"Starting 2AFC trial - Offset: {testStimulusOffset}, Test: {currentTestStimulus}, Reference: {referenceStimulus}, Reference first: {referenceIsFirst}");
    }
    
    private void UpdateComparisonState()
    {
        stateTimer += Time.deltaTime;
        
        switch (currentComparisonState)
        {
            case ComparisonState.ShowingFirstStimulus:
                if (testMode)
                {
                    // In test mode, skip pacing dot and go directly to delay between stimuli
                    currentComparisonState = ComparisonState.DelayBetweenStimuli;
                    isShowingFirstStimulus = false; // Mark that we're moving to second stimulus
                    stateTimer = 0f;
                }
                else
                {
                    // Normal mode: Start the dot blinking process
                    currentComparisonState = ComparisonState.BlinkingDot;
                    stateTimer = 0f;

                    if (pacingDot != null)
                    {
                        pacingDot.BlinkTimes(blinkCount, () => {
                            // After blinking is complete, start moving the dot
                            currentComparisonState = ComparisonState.MovingDot;
                            pacingDot.MoveToDestination(() => {
                                // After reaching destination, flatten the surface (set amplitude to 0)
                                UpdateSurfaceHeight(0);
                                // Wait at end before returning
                                currentComparisonState = ComparisonState.WaitingAtEnd;
                                stateTimer = 0f;
                            });
                        });
                    }
                    else
                    {
                        // Fallback if no pacing dot is assigned
                        currentComparisonState = ComparisonState.DelayBetweenStimuli;
                        isShowingFirstStimulus = false;
                        // Hide stimulus
                        UpdateSurfaceHeight(0);
                        stateTimer = 0f;
                    }
                }
                break;

            case ComparisonState.BlinkingDot:
            case ComparisonState.MovingDot:
                // These states are handled by callbacks, just wait
                break;

            case ComparisonState.WaitingAtEnd:
                if (stateTimer >= waitAtEndDuration)
                {
                    if (pacingDot != null)
                    {
                        // After waiting at end, start moving back to start with faster speed
                        currentComparisonState = ComparisonState.MovingBackToStart;
                        float originalSpeed = pacingDot.moveSpeed;
                        pacingDot.moveSpeed = originalSpeed * returnSpeedMultiplier;

                        pacingDot.ResetToStart(() => {
                            // Restore original speed
                            pacingDot.moveSpeed = originalSpeed;

                            // After returning to start, proceed based on which stimulus we just showed
                            if (isShowingFirstStimulus)
                            {
                                currentComparisonState = ComparisonState.DelayBetweenStimuli;
                                isShowingFirstStimulus = false; // Mark that we're moving to second stimulus
                                // Hide stimulus
                                UpdateSurfaceHeight(0);
                            }
                            else
                            {
                                currentComparisonState = ComparisonState.WaitingForResponse;
                                // Hide stimulus
                                UpdateSurfaceHeight(0);
                                // Show comparison canvas
                                if (showComparisonCanvas && comparisonCanvas != null)
                                {
                                    comparisonCanvas.SetActive(true);
                                }
                            }
                            stateTimer = 0f;
                        });
                    }
                    else
                    {
                        // Fallback if no pacing dot
                        if (isShowingFirstStimulus)
                        {
                            currentComparisonState = ComparisonState.DelayBetweenStimuli;
                            isShowingFirstStimulus = false;
                        }
                        else
                        {
                            currentComparisonState = ComparisonState.WaitingForResponse;
                            if (showComparisonCanvas && comparisonCanvas != null)
                            {
                                comparisonCanvas.SetActive(true);
                            }
                        }
                        UpdateSurfaceHeight(0);
                    }
                    stateTimer = 0f;
                }
                break;

            case ComparisonState.MovingBackToStart:
                // These states are handled by callbacks, just wait
                break;

            case ComparisonState.WaitingAfterMovement:
                // This state is now deprecated - logic moved to ResetToStart callback
                // Kept for fallback compatibility
                if (stateTimer >= waitAfterMovementDuration)
                {
                    // After wait period, proceed based on which stimulus we just showed
                    if (isShowingFirstStimulus)
                    {
                        currentComparisonState = ComparisonState.DelayBetweenStimuli;
                        isShowingFirstStimulus = false; // Mark that we're moving to second stimulus
                    }
                    else
                    {
                        currentComparisonState = ComparisonState.WaitingForResponse;
                        // Show comparison canvas
                        if (showComparisonCanvas && comparisonCanvas != null)
                        {
                            comparisonCanvas.SetActive(true);
                        }
                    }

                    // Hide stimulus
                    UpdateSurfaceHeight(0);

                    stateTimer = 0f;
                }
                break;
                
            case ComparisonState.DelayBetweenStimuli:
                if (stateTimer >= delayBetweenStimuli)
                {
                    currentComparisonState = ComparisonState.ShowingSecondStimulus;
                    stateTimer = 0f;
                    // Show second stimulus and start the process again
                    float secondStimulus = referenceIsFirst ? currentTestStimulus : currentReferenceStimulus;
                    UpdateSurfaceHeight(secondStimulus);
                }
                break;
                
            case ComparisonState.ShowingSecondStimulus:
                if (testMode)
                {
                    // In test mode, skip pacing dot and go directly to waiting for response
                    currentComparisonState = ComparisonState.WaitingForResponse;
                    // Show comparison canvas immediately in test mode
                    if (showComparisonCanvas && comparisonCanvas != null)
                    {
                        comparisonCanvas.SetActive(true);
                    }
                    stateTimer = 0f;
                }
                else
                {
                    // Normal mode: Start the dot blinking process for second stimulus
                    currentComparisonState = ComparisonState.BlinkingDot;
                    stateTimer = 0f;

                    if (pacingDot != null)
                    {
                        pacingDot.BlinkTimes(blinkCount, () => {
                            // After blinking is complete, start moving the dot
                            currentComparisonState = ComparisonState.MovingDot;
                            pacingDot.MoveToDestination(() => {
                                // After reaching destination, flatten the surface (set amplitude to 0)
                                UpdateSurfaceHeight(0);
                                // Wait at end before returning
                                currentComparisonState = ComparisonState.WaitingAtEnd;
                                stateTimer = 0f;
                            });
                        });
                    }
                    else
                    {
                        // Fallback if no pacing dot is assigned
                        currentComparisonState = ComparisonState.WaitingForResponse;
                        // Hide stimulus
                        UpdateSurfaceHeight(0);
                        // Show comparison canvas
                        if (showComparisonCanvas && comparisonCanvas != null)
                        {
                            comparisonCanvas.SetActive(true);
                        }
                        stateTimer = 0f;
                    }
                }
                break;
        }
    }
}