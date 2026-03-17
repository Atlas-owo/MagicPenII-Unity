using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HybridTrailManager : MonoBehaviour
{
    [Header("Settings")]
    public InteractionMode interactionMode = InteractionMode.ButtonPress;
    public float delayBetweenTrails = 1.0f;
    public AudioClip successSound;
    public bool randomizeOrder = true;

    [Header("References")]
    public Transform penTip;

    [Header("debug")]
    public Transform rotationCenter; // Optional, if we want rotation here too? 
    // User didn't explicitly ask for rotation in Hybrid, but "Rotation 8 directions" task was finished.
    // "Previously rotation 8 directions feature is finished".
    // "Sine wave overlay is separately added for hybrid mode".
    // So Hybrid doesn't necessarily need the 8-way rotation unless implied?
    // The user said: "Before rotation 8 directions feature is finished... Sine wave overlay is for hybrid...".
    // It implies Hybrid might NOT have rotation, or maybe it should?
    // Let's stick to the prompt: "Sine wave overlay is for hybrid".
    // I will NOT add the 8-way rotation to Hybrid unless I see it was already there or requested.
    // Original TargetTrailManager had Hybrid logic but NO rotation logic for Hybrid (it was separate).
    // So I will just implement the Sine Overlay.

    public enum InteractionMode
    {
        ButtonPress,
        AutoTouch
    }

    [System.Serializable]
    public struct SurfaceConfig
    {
        public string name; 
        public HybridSurface.SurfaceShape shape;
        
        public float width;           // Default 0.3
        
        [Header("Overrides (Optional)")]
        public Transform overrideStart;
        public Transform overrideEnd;

        // Sine Params
        public float amplitude;       
        public float periods;         
        
        // Nurbs Params
        public float nurbsPlateauWidth;      
        public float nurbsTransitionLength;  
        public float nurbsTransitionSteepness; 
        public float nurbsAmplitude;
    }

    [System.Serializable]
    public struct TrailConfig
    {
        public string name;
        public TargetTrail.TrailShape shape;
        
        // Sine Params
        public float amplitude;
        public float periods;
    }

    [System.Serializable]
    public struct HybridSessionSettings
    {
        [Header("Axis - Trail")]
        public Transform startPoint;
        public Transform endPoint;

        [Header("Axis - Circle")]
        public Transform circleCenter;

        [Header("Axis - Surface")]
        public Transform surfaceStartPoint;
        public Transform surfaceEndPoint;

        [Header("Test Pool")]
        public List<SurfaceConfig> surfacesToTest;
        public List<TrailConfig> trailsToTest;
    }

    [Header("Hybrid Configuration")]
    public HybridSessionSettings hybridSettings;
    public int repeatsPerTrail = 3;

    [Header("Sine Wave Overlay (New)")]
    public bool enableSineOverlay = false;
    public float sineOverlayAmplitude = 0.02f;
    public float sineOverlayFrequency = 10.0f;
    public bool sineOverlayUseNormal = false;

    private List<HybridTargetTrail> hybridTrails = new List<HybridTargetTrail>();
    private int currentTrailIndex = -1;
    private PathRecorder pathRecorder;

    private void Start()
    {
        pathRecorder = FindObjectOfType<PathRecorder>();

        // Enforce Raycast Mode for Hybrid Session
        var penController = FindObjectOfType<HapticPenController>();
        if (penController != null)
        {
            penController.enableRaycastControl = true;
            
            // Also ensure the Raycast Mask includes the 'Surface' layer
            int surfaceLayer = LayerMask.NameToLayer("Surface");
            if (surfaceLayer == -1) surfaceLayer = LayerMask.NameToLayer("surface");
            
            if (surfaceLayer != -1)
            {
                // Add to mask (using bitwise OR)
                penController.surfaceLayerMask |= (1 << surfaceLayer);
            }
        }
        
        GenerateHybridTasks();

        Debug.Log($"HybridTrailManager: Generated {hybridTrails.Count} trails.");

        // Start the sequence
        StartTrails();
    }

    private void GenerateHybridTasks()
    {
        if (hybridSettings.startPoint == null || hybridSettings.endPoint == null)
        {
            Debug.LogError("Hybrid Settings Start/End points are missing!");
            return;
        }

        // Default Surface Points to Trail Points if not assigned
        Transform sStart = hybridSettings.surfaceStartPoint != null ? hybridSettings.surfaceStartPoint : hybridSettings.startPoint;
        Transform sEnd = hybridSettings.surfaceEndPoint != null ? hybridSettings.surfaceEndPoint : hybridSettings.endPoint;

        // Generate Combinations
        List<HybridTaskCombo> combinations = new List<HybridTaskCombo>();
        
        int typeCounter = 0;
        int surfIdx = 0; // Outer loop index
        foreach (var surfConfig in hybridSettings.surfacesToTest)
        {
            foreach (var trailConfig in hybridSettings.trailsToTest)
            {
                combinations.Add(new HybridTaskCombo { 
                    surfaceCfg = surfConfig, 
                    trailCfg = trailConfig, 
                    typeId = typeCounter,
                    surfaceIndex = surfIdx // Store the specific index of the surface config
                });
                typeCounter++;
            }
            surfIdx++;
        }

        // Expansion (Repeats)
        List<HybridTaskCombo> finalTasks = new List<HybridTaskCombo>();
        foreach (var combo in combinations)
        {
            for (int r = 0; r < repeatsPerTrail; r++)
            {
                finalTasks.Add(combo);
            }
        }

        // Shuffle
        if (randomizeOrder && finalTasks.Count > 1)
        {
            UnityEngine.Random.InitState((int)System.DateTime.Now.Ticks);
            int n = finalTasks.Count;
            while (n > 1)
            {
                n--;
                int k = UnityEngine.Random.Range(0, n + 1);
                var value = finalTasks[k];
                finalTasks[k] = finalTasks[n];
                finalTasks[n] = value;
            }
        }

        // Instantiation
        for (int i = 0; i < finalTasks.Count; i++)
        {
            HybridTaskCombo task = finalTasks[i];
            SurfaceConfig sDef = task.surfaceCfg;
            TrailConfig tDef = task.trailCfg;
            
            GameObject trailObj = new GameObject($"HybridTrail_Inst{i}_Type{task.typeId}_{sDef.shape}_{tDef.shape}");
            trailObj.transform.SetParent(transform);
            HybridTargetTrail hTrail = trailObj.AddComponent<HybridTargetTrail>();

            if (successSound != null) hTrail.successSound = successSound;

            // Apply settings from the specific Config objects
            float sWidth = sDef.width > 0 ? sDef.width : 0.3f;
            
            // Amplitude Logic: Use nurbsAmplitude for Nurbs, amplitude for others
            float sAmp = 0.05f;
            if (sDef.shape == HybridSurface.SurfaceShape.Nurbs)
            {
                sAmp = sDef.nurbsAmplitude != 0 ? sDef.nurbsAmplitude : 0.05f;
            }
            else
            {
                sAmp = sDef.amplitude != 0 ? sDef.amplitude : 0.05f;
            }
            float sPer = sDef.periods > 0 ? sDef.periods : 2.0f;
            
            float surfPlat = sDef.nurbsPlateauWidth > 0 ? sDef.nurbsPlateauWidth : 0.3f;
            float surfTrans = sDef.nurbsTransitionLength > 0 ? sDef.nurbsTransitionLength : 0.05f;
            float surfSteep = sDef.nurbsTransitionSteepness > 0 ? sDef.nurbsTransitionSteepness : 5.0f;
            
            // Trail Params
            float tAmp = tDef.amplitude; 
            float tPer = tDef.periods > 0 ? tDef.periods : 2.0f;
            
            // Circular Center
            Vector3 centerPos = hybridSettings.circleCenter != null ? hybridSettings.circleCenter.position : hybridSettings.startPoint.position;

            // Determine Surface Start/End (Override vs Global)
            Vector3 finalSurfStart = sDef.overrideStart != null ? sDef.overrideStart.position : sStart.position;
            Vector3 finalSurfEnd = sDef.overrideEnd != null ? sDef.overrideEnd.position : sEnd.position;

            // Initialize with new Sine Overlay params
            hTrail.Initialize(hybridSettings.startPoint.position, hybridSettings.endPoint.position, 
                finalSurfStart, finalSurfEnd, centerPos, // Pass correct surface points
                this, penTip, i, task.typeId, task.surfaceIndex, // Pass Surface Index
                sDef.shape, tDef.shape,
                sWidth, sAmp, sPer, surfPlat, surfTrans, surfSteep,
                tAmp, tPer,
                // New params
                enableSineOverlay, sineOverlayAmplitude, sineOverlayFrequency, sineOverlayUseNormal
                );

            hybridTrails.Add(hTrail);
        }
    }


    private struct HybridTaskCombo
    {
        public SurfaceConfig surfaceCfg;
        public TrailConfig trailCfg;
        public int typeId;
        public int surfaceIndex;
    }

    private void Update()
    {
        // Debug key to restart
        if (Input.GetKeyDown(KeyCode.T))
        {
            StartTrails();
        }
    }

    public void StartTrails()
    {
        if (hybridTrails.Count == 0) return;
        
        // Start Recording Session
        if (pathRecorder != null)
        {
            pathRecorder.StartRecording();
        }

        currentTrailIndex = 0;
        ActivateTrail(currentTrailIndex);
    }

    private void ActivateTrail(int index)
    {
        if (index >= 0 && index < hybridTrails.Count)
        {
            hybridTrails[index].Activate();
            Debug.Log($"HybridTrailManager: Activated Hybrid trail {index}");
        }
        else
        {
            Debug.Log("HybridTrailManager: All hybrid trails completed.");
            if (pathRecorder != null) pathRecorder.StopRecording();
        }
    }

    public void OnTrailCompleted(HybridTargetTrail trail)
    {
        StartCoroutine(WaitAndNext());
    }

    private IEnumerator WaitAndNext()
    {
        yield return new WaitForSeconds(delayBetweenTrails);
        currentTrailIndex++;
        ActivateTrail(currentTrailIndex);
    }
}
