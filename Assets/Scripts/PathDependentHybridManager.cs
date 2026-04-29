using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System;

public class PathDependentHybridManager : MonoBehaviour
{
    public enum InteractionMode { ButtonPress, AutoTouch }

    [Header("Settings")]
    public InteractionMode interactionMode = InteractionMode.ButtonPress;
    public float delayBetweenTrails = 1.0f;
    public AudioClip successSound;
    public bool randomizeOrder = true;

    [Header("Generation Geometry")]
    public Transform centerPoint;
    public Transform baseStartPoint;
    public Transform baseEndPoint;
    
    [Header("Task Configurations")]
    public List<TaskConfig> tasksToTest;
    public int repeatsPerTask = 3;
    public int rotationCount = 8;
    public float rotationAngle = 45.0f;

    [System.Serializable]
    public struct TaskConfig
    {
        public string name;
        public PathDependentHybridTrail.TrailShape shape;
        
        [Header("Overrides (Optional)")]
        public Transform overrideStart;
        public Transform overrideEnd;
        
        [Header("Sine Params")]
        public float amplitudeStart;
        public float amplitudeEnd;
        public float frequencyStart;
        public float frequencyEnd;
        public bool generateReversed; 
        
        [Header("Nurbs Params")]
        public float nurbsPlateauWidth;
        public float nurbsTransitionLength;
        public float nurbsTransitionSteepness;
        public float nurbsAmplitude;

        [Header("Vertical Lift Params")]
        public float liftSpeed;
        public float liftToleranceRadius;
        public float liftFloorSize;
        public bool showLiftHelpers;
    }

    [Header("Visual Settings")]
    public bool enableRibbon = true;
    public float planeWidth = 0.2f;
    public float paddingLength = 0.15f; 
    public Vector3 ribbonExpansionAxis = Vector3.forward;
    public Vector3 paddingExpansionAxis = Vector3.right;

    [Header("Haptic Mismatch Settings")]
    [Tooltip("Shift physical surfaces negatively along X-axis (in meters) for non-VerticalLift trails")]
    public float hapticSurfaceOffsetX = 0.0f;
    [Tooltip("If true, shows the physical surface with a blue semi-transparent material")]
    public bool showHapticSurface = false;

    public enum PenTaskMode { None, Haptic, MidAir }

    [Header("Pen Control Settings")]
    public PenTaskMode penTaskMode = PenTaskMode.Haptic;
    public Transform penTip;

    [Header("Testing & State")]
    public KeyCode skipKey = KeyCode.Space;
    public bool isFinished = false;

    [Header("Data Logging")]
    public string userId = "P00";
    [Tooltip("Leave empty to save in Assets/ folder. Example: D:/ExpData/")]
    public string customOutputDirectory = "";
    [Tooltip("When enabled, the setup runs normally but no data is saved to disk")]
    public bool testMode = false;

    private List<PathDependentHybridTrail> trails = new List<PathDependentHybridTrail>();
    private int currentTrailIndex = -1;
    private PathRecorder pathRecorder;

    private void Start()
    {
        pathRecorder = FindObjectOfType<PathRecorder>();
        
        var penController = FindObjectOfType<HapticPenController>();
        if (penController != null)
        {
            if (penTaskMode == PenTaskMode.Haptic)
            {
                penController.enableRaycastControl = true;
                penController.enableMidairMode = false;
                penController.enableDirectPressureControl = false;
                
                int surfaceLayer = LayerMask.NameToLayer("Surface");
                if (surfaceLayer == -1) surfaceLayer = LayerMask.NameToLayer("surface");
                if (surfaceLayer != -1) penController.surfaceLayerMask |= (1 << surfaceLayer);
            }
            else if (penTaskMode == PenTaskMode.MidAir)
            {
                penController.enableRaycastControl = false;
                penController.enableMidairMode = true;
                penController.enableDirectPressureControl = false;
            }
        }

        GenerateInputTasks();
        Debug.Log($"PathDependentHybridManager: Generated {trails.Count} trails.");

        if (pathRecorder != null && !testMode)
        {
            pathRecorder.userId = this.userId;
            pathRecorder.condition = this.penTaskMode.ToString();

            string baseDir = string.IsNullOrEmpty(customOutputDirectory) ? Application.dataPath : customOutputDirectory;
            string conditionFolder = this.penTaskMode.ToString();
            string finalDir = Path.Combine(baseDir, conditionFolder);
            
            if (!System.IO.Directory.Exists(finalDir)) System.IO.Directory.CreateDirectory(finalDir);
            
            pathRecorder.customSavePath = finalDir;
            pathRecorder.customFileName = $"{userId}_{conditionFolder}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        }

        StartTrails();
    }

    private struct TaskDef
    {
        public TaskConfig config;
        public int rotationStep;
        public bool isReversed;
        public int typeId;
    }

    private void GenerateInputTasks()
    {
        List<TaskDef> tasks = new List<TaskDef>();
        
        for (int i = 0; i < tasksToTest.Count; i++)
        {
            TaskConfig config = tasksToTest[i];
            
            for (int rStep = 0; rStep < rotationCount; rStep++)
            {
                for (int r = 0; r < repeatsPerTask; r++)
                {
                    tasks.Add(new TaskDef { config = config, rotationStep = rStep, isReversed = false, typeId = i });
                    
                    if (config.generateReversed && config.shape == PathDependentHybridTrail.TrailShape.SineWave)
                    {
                        tasks.Add(new TaskDef { config = config, rotationStep = rStep, isReversed = true, typeId = i });
                    }
                }
            }
        }
        
        if (randomizeOrder && tasks.Count > 1)
        {
            UnityEngine.Random.InitState((int)System.DateTime.Now.Ticks);
            int n = tasks.Count;
            while (n > 1)
            {
                n--;
                int k = UnityEngine.Random.Range(0, n + 1);
                var value = tasks[k];
                tasks[k] = tasks[n];
                tasks[n] = value;
            }
        }

        Vector3 center = centerPoint != null ? centerPoint.position : Vector3.zero;
        Vector3 defaultStart = baseStartPoint != null ? baseStartPoint.position : center + Vector3.forward * 0.1f;
        Vector3 defaultEnd = baseEndPoint != null ? baseEndPoint.position : center + Vector3.forward * 0.4f;

        for (int i = 0; i < tasks.Count; i++)
        {
            TaskDef task = tasks[i];
            float currentAngle = task.rotationStep * rotationAngle;
            Quaternion rot = Quaternion.Euler(0, currentAngle, 0);

            Vector3 startBase = task.config.overrideStart != null ? task.config.overrideStart.position : defaultStart;
            Vector3 endBase = task.config.overrideEnd != null ? task.config.overrideEnd.position : defaultEnd;

            Vector3 startPos = rot * (startBase - center) + center;
            Vector3 endPos = rot * (endBase - center) + center;

            GameObject trailObj = new GameObject($"PathTrail_Inst{i}_Type{task.typeId}_{task.config.shape}_Rot{currentAngle}" + (task.isReversed ? "_Rev" : ""));
            trailObj.transform.SetParent(transform);
            
            PathDependentHybridTrail trail = trailObj.AddComponent<PathDependentHybridTrail>();
            
            trail.trailShape = task.config.shape;

            if (task.config.shape == PathDependentHybridTrail.TrailShape.SineWave)
            {
                trail.amplitudeStart = task.isReversed ? task.config.amplitudeEnd : task.config.amplitudeStart;
                trail.amplitudeEnd = task.isReversed ? task.config.amplitudeStart : task.config.amplitudeEnd;
                trail.frequencyStart = task.isReversed ? task.config.frequencyEnd : task.config.frequencyStart;
                trail.frequencyEnd = task.isReversed ? task.config.frequencyStart : task.config.frequencyEnd;
            }
            else if (task.config.shape == PathDependentHybridTrail.TrailShape.Nurbs)
            {
                // Fallbacks if Unity inspector leaves them 0 empty
                trail.nurbsPlateauWidth = task.config.nurbsPlateauWidth > 0 ? task.config.nurbsPlateauWidth : 0.3f;
                trail.nurbsTransitionLength = task.config.nurbsTransitionLength > 0 ? task.config.nurbsTransitionLength : 0.05f;
                trail.nurbsTransitionSteepness = task.config.nurbsTransitionSteepness > 0 ? task.config.nurbsTransitionSteepness : 5.0f;
                trail.nurbsAmplitude = task.config.nurbsAmplitude != 0 ? task.config.nurbsAmplitude : 0.05f;
            }
            else if (task.config.shape == PathDependentHybridTrail.TrailShape.VerticalLift)
            {
                trail.liftSpeed = task.config.liftSpeed > 0 ? task.config.liftSpeed : 0.05f;
                trail.liftToleranceRadius = task.config.liftToleranceRadius > 0 ? task.config.liftToleranceRadius : 0.02f;
                trail.liftFloorSize = task.config.liftFloorSize > 0 ? task.config.liftFloorSize : 0.5f;
                trail.showLiftHelpers = task.config.showLiftHelpers;
            }
            
            trail.enableRibbon = enableRibbon;
            trail.planeWidth = planeWidth;
            trail.paddingLength = paddingLength;
            trail.ribbonExpansionAxis = ribbonExpansionAxis;
            trail.paddingExpansionAxis = paddingExpansionAxis;
            trail.hapticSurfaceOffsetX = hapticSurfaceOffsetX;
            trail.showHapticSurface = showHapticSurface;
            
            if (successSound != null) trail.successSound = successSound;

            trail.Initialize(startPos, endPos, this, penTip, i, task.typeId);
            trails.Add(trail);
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.T))
        {
            StartTrails();
        }

        if (Input.GetKeyDown(skipKey))
        {
            SkipCurrentTrail();
        }
    }

    public void StartTrails()
    {
        if (trails.Count == 0) return;
        
        isFinished = false;
        if (pathRecorder != null && !testMode) pathRecorder.StartRecording();

        currentTrailIndex = 0;
        ActivateTrail(currentTrailIndex);
    }

    private void ActivateTrail(int index)
    {
        if (index >= 0 && index < trails.Count)
        {
            trails[index].Activate();
        }
        else
        {
            Debug.Log("PathDependentHybridManager: All trails completed.");
            if (pathRecorder != null && !testMode) pathRecorder.StopRecording();
            isFinished = true;
        }
    }

    public void SkipCurrentTrail()
    {
        if (currentTrailIndex >= 0 && currentTrailIndex < trails.Count)
        {
            trails[currentTrailIndex].gameObject.SetActive(false);
            Debug.Log($"PathDependentHybridManager: Skipped trail {currentTrailIndex}");
            currentTrailIndex++;
            ActivateTrail(currentTrailIndex);
        }
    }

    public void OnTrailCompleted(PathDependentHybridTrail trail)
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
