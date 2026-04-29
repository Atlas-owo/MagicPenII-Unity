using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DynamicSineTrailManager : MonoBehaviour
{
    public enum SessionMode { Standard, Hybrid }
    public enum InteractionMode { ButtonPress, AutoTouch }

    [Header("Settings")]
    public SessionMode sessionMode = SessionMode.Standard;
    public InteractionMode interactionMode = InteractionMode.ButtonPress;
    public float delayBetweenTrails = 1.0f;
    public AudioClip successSound;
    public bool randomizeOrder = true;

    [Header("Generation Settings")]
    [Tooltip("The center point around which all trails will be generated.")]
    public Transform centerPoint;
    [Tooltip("A reference point defining the Start position for the 0-degree angle.")]
    public Transform baseStartPoint;
    [Tooltip("A reference point defining the End position for the 0-degree angle.")]
    public Transform baseEndPoint;
    
    public int rotationCount = 8;
    public float rotationAngle = 45.0f;
    public int repeatsPerTrail = 3;

    [Header("Dynamic Sine Wave Settings")]
    public float amplitudeStart = 0.05f;
    public float amplitudeEnd = 0.1f;
    public float frequencyStart = 1.0f;
    public float frequencyEnd = 3.0f;
    [Tooltip("If checked, generates an extra set of tasks with start and end values swapped.")]
    public bool generateReversedTasks = false;

    [Header("Visual Settings")]
    public bool enableRibbon = true;
    public float planeWidth = 0.2f;

    [Header("Pen Control Settings")]
    [Tooltip("If checked, the trail automatically toggles the pen's direct pressure control mode when drawing.")]
    public bool overridePenControlMode = true;

    [Header("References")]
    public Transform penTip;

    private List<DynamicSineTrail> trails = new List<DynamicSineTrail>();
    private int currentTrailIndex = -1;
    private PathRecorder pathRecorder;

    private void Start()
    {
        pathRecorder = FindObjectOfType<PathRecorder>();
        GenerateInputTasks();
        Debug.Log($"DynamicSineTrailManager: Generated {trails.Count} trails.");
        StartTrails();
    }

    private struct TaskDef
    {
        public int rotationStep;
        public bool isReversed;
    }

    private void GenerateInputTasks()
    {
        List<TaskDef> tasks = new List<TaskDef>();
        for (int rStep = 0; rStep < rotationCount; rStep++)
        {
            for (int r = 0; r < repeatsPerTrail; r++)
            {
                tasks.Add(new TaskDef { rotationStep = rStep, isReversed = false });
                if (generateReversedTasks)
                {
                    tasks.Add(new TaskDef { rotationStep = rStep, isReversed = true });
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

            // Rotate starting and ending coordinates around the center point (Y axis rotation)
            Vector3 startPos = rot * (defaultStart - center) + center;
            Vector3 endPos = rot * (defaultEnd - center) + center;

            GameObject trailObj = new GameObject($"DynamicTrail_Inst{i}_Rot{currentAngle}" + (task.isReversed ? "_Rev" : ""));
            trailObj.transform.SetParent(transform);
            
            DynamicSineTrail trail = trailObj.AddComponent<DynamicSineTrail>();
            
            // Assign mathematical and visual parameters
            trail.amplitudeStart = task.isReversed ? amplitudeEnd : amplitudeStart;
            trail.amplitudeEnd = task.isReversed ? amplitudeStart : amplitudeEnd;
            trail.frequencyStart = task.isReversed ? frequencyEnd : frequencyStart;
            trail.frequencyEnd = task.isReversed ? frequencyStart : frequencyEnd;
            
            trail.enableRibbon = enableRibbon;
            trail.planeWidth = planeWidth;
            trail.overridePenControlMode = overridePenControlMode;
            
            if (successSound != null) trail.successSound = successSound;

            trail.Initialize(startPos, endPos, this, penTip, i);
            trails.Add(trail);
        }
    }

    private void Update()
    {
        // Debug override to restart sequence
        if (Input.GetKeyDown(KeyCode.T))
        {
            StartTrails();
        }
    }

    public void StartTrails()
    {
        if (trails.Count == 0) return;
        
        if (pathRecorder != null) pathRecorder.StartRecording();

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
            Debug.Log("DynamicSineTrailManager: All trails completed.");
            if (pathRecorder != null) pathRecorder.StopRecording();
        }
    }

    public void OnTrailCompleted(DynamicSineTrail trail)
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
