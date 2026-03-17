using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TargetTrailManager : MonoBehaviour
{
    public enum SessionMode
    {
        Standard,
        Hybrid
    }

    public enum InteractionMode
    {
        ButtonPress,
        AutoTouch
    }

    [Header("Settings")]
    public SessionMode sessionMode = SessionMode.Standard;
    public InteractionMode interactionMode = InteractionMode.ButtonPress;
    
    // Legacy settings (apply to both or primarily Standard)
    public float delayBetweenTrails = 1.0f;
    public AudioClip successSound;
    public bool randomizeOrder = true;

    [Header("Rotation Settings")]
    public bool enableRotatedVariations = false;
    public Transform rotationCenter;
    public int rotationCount = 8;
    public float rotationAngle = 45.0f;

    [Header("Debug / Testing")]
    public Transform debugStartPoint;
    public Transform debugEndPoint;

    [Header("References")]
    public Transform penTip;

    private List<TargetTrail> trails = new List<TargetTrail>();
    private int currentTrailIndex = -1;
    private PathRecorder pathRecorder;

    // --- INPUT MODE CONFIGURATION (Standard) ---
    [System.Serializable]
    public struct TrailDefinition
    {
        public Transform startTransform;
        public Transform endTransform;
        public TargetTrail.TrailShape shape;
        public float amplitudeStart; // e.g. 0.05
        public float amplitudeEnd;   // e.g. 0.1
        public float periods;        // e.g. 2
        
        // NURBS / Plateau Settings (Only for TargetTrail Nurbs)
        public float nurbsPlateauWidth;      // Default 0.3
        public float nurbsTransitionLength;  // Default 0.05
        public float nurbsTransitionSteepness; // Default 5.0
        public float nurbsAmplitude;         // Default 0.05
    }

    [Header("Input Configuration")]
    public List<TrailDefinition> preDefinedTrails = new List<TrailDefinition>();

    public int repeatsPerTrail = 3;

    private void Start()
    {
        pathRecorder = FindObjectOfType<PathRecorder>();
        GenerateInputTasks();

        Debug.Log($"TargetTrailManager: Generated {trails.Count} trails.");

        // Start the sequence
        StartTrails();
    }

     private struct TaskDef
     {
         public int defIndex;
         public int rotationStep;
     }

    private void GenerateInputTasks()
    {
         // Generate Task List
        List<TaskDef> tasks = new List<TaskDef>();
        
        int rotations = enableRotatedVariations ? rotationCount : 1;

        for (int rStep = 0; rStep < rotations; rStep++)
        {
            for (int i = 0; i < preDefinedTrails.Count; i++)
            {
                for (int r = 0; r < repeatsPerTrail; r++)
                {
                    tasks.Add(new TaskDef { defIndex = i, rotationStep = rStep });
                }
            }
        }
        
        // Shuffle Tasks
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

        // Create Trails
        for (int i = 0; i < tasks.Count; i++)
        {
            TaskDef task = tasks[i];
            int defIndex = task.defIndex;
            TrailDefinition def = preDefinedTrails[defIndex];

             if (def.startTransform != null && def.endTransform != null)
             {
                int rotAngleInt = (int)(task.rotationStep * rotationAngle);
                GameObject trailObj = new GameObject($"TargetTrail_Inst{i}_Type{defIndex}_Rot{rotAngleInt}");
                trailObj.transform.SetParent(transform);
                
                TargetTrail trail = trailObj.AddComponent<TargetTrail>();
                
                // Configure Shape & Params
                trail.trailShape = def.shape;
                if (def.shape == TargetTrail.TrailShape.SineWave) 
                {
                    trail.amplitudeStart = def.amplitudeStart;
                    trail.amplitudeEnd = def.amplitudeEnd;
                    trail.periods = def.periods > 0 ? def.periods : 2.0f; 
                }
                else if (def.shape == TargetTrail.TrailShape.Nurbs)
                {
                    trail.nurbsPlateauWidth = def.nurbsPlateauWidth > 0 ? def.nurbsPlateauWidth : 0.3f;
                    trail.nurbsTransitionLength = def.nurbsTransitionLength > 0 ? def.nurbsTransitionLength : 0.05f;
                    trail.nurbsTransitionSteepness = def.nurbsTransitionSteepness > 0 ? def.nurbsTransitionSteepness : 5.0f;
                    trail.nurbsAmplitude = def.nurbsAmplitude != 0 ? def.nurbsAmplitude : 0.05f; 
                }

                if (successSound != null) trail.successSound = successSound;

                // Calculate Rotated Positions
                Vector3 startPos = def.startTransform.position;
                Vector3 endPos = def.endTransform.position;

                if (enableRotatedVariations)
                {
                    Vector3 center = rotationCenter != null ? rotationCenter.position : Vector3.zero;
                    float currentAngle = task.rotationStep * rotationAngle;
                    Quaternion rot = Quaternion.Euler(0, currentAngle, 0);

                    startPos = rot * (startPos - center) + center;
                    endPos = rot * (endPos - center) + center;
                }

                trail.Initialize(startPos, endPos, this, penTip, i, defIndex);
                trails.Add(trail);
             }
        }

        // Debug Fallback
        if (trails.Count == 0 && debugStartPoint != null && debugEndPoint != null)
        {
            AddStraightTrail(debugStartPoint.position, debugEndPoint.position);
        }
    }




    private void Update()
    {
        // Debug key to restart
        if (Input.GetKeyDown(KeyCode.T))
        {
            StartTrails();
        }
    }

    public void AddStraightTrail(Vector3 start, Vector3 end)
    {
        GameObject trailObj = new GameObject($"TargetTrail_{trails.Count}");
        trailObj.transform.SetParent(transform);
        
        TargetTrail trail = trailObj.AddComponent<TargetTrail>();
        
        // Pass global settings if needed
        if (successSound != null) trail.successSound = successSound;

        // Pass trails.Count as the ID, and -1 as TypeID (Debug/Manual)
        trail.Initialize(start, end, this, penTip, trails.Count, -1);
        trails.Add(trail);
    }

    public void StartTrails()
    {
         if (trails.Count == 0) return;
        
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
        if (index >= 0 && index < trails.Count)
        {
            trails[index].Activate();
            Debug.Log($"TargetTrailManager: Activated trail {index}");
        }
        else
        {
            Debug.Log("TargetTrailManager: All trails completed.");
            // Stop Recording Session
            if (pathRecorder != null)
            {
                pathRecorder.StopRecording();
            }
        }
    }

    public void OnTrailCompleted(TargetTrail trail)
    {
        StartCoroutine(WaitAndNext());
    }

    private IEnumerator WaitAndNext()
    {
        yield return new WaitForSeconds(delayBetweenTrails);
        currentTrailIndex++;
        ActivateTrail(currentTrailIndex);
    }

    // --- Export Functionality ---

    [System.Serializable]
    private class TrailExportData
    {
        public List<TrailDefExport> definitions = new List<TrailDefExport>();
    }

    [System.Serializable]
    private class TrailDefExport
    {
        public int typeId;
        public Vector3 startPosition;
        public Vector3 endPosition;
        public string shape;
        public float amplitudeStart;
        public float amplitudeEnd;
        public float periods;
        public float nurbsPlateauWidth;
        public float nurbsTransitionLength;
        public float nurbsTransitionSteepness;
        public float nurbsAmplitude;
    }

    [ContextMenu("Export Trail Definitions")]
    public void ExportTrailDefinitions()
    {
        TrailExportData exportData = new TrailExportData();

        for (int i = 0; i < preDefinedTrails.Count; i++)
        {
            var def = preDefinedTrails[i];
            if (def.startTransform != null && def.endTransform != null)
            {
                TrailDefExport exportItem = new TrailDefExport();
                exportItem.typeId = i;
                exportItem.startPosition = def.startTransform.position;
                exportItem.endPosition = def.endTransform.position;
                exportItem.shape = def.shape.ToString();
                exportItem.amplitudeStart = def.amplitudeStart;
                exportItem.amplitudeEnd = def.amplitudeEnd;
                exportItem.periods = def.periods > 0 ? def.periods : 2.0f; // Ensure default matches logic
                exportItem.nurbsPlateauWidth = def.nurbsPlateauWidth;
                exportItem.nurbsTransitionLength = def.nurbsTransitionLength;
                exportItem.nurbsTransitionSteepness = def.nurbsTransitionSteepness;
                exportItem.nurbsTransitionSteepness = def.nurbsTransitionSteepness;
                exportItem.nurbsAmplitude = def.nurbsAmplitude;

                exportData.definitions.Add(exportItem);
            }
        }

        string json = JsonUtility.ToJson(exportData, true);
        string filename = "TrailDefinitions.json";
        // Save to same folder as CSVs usually go, or project root for now.
        // Let's use persistentDataPath to be safe and consistent with PathRecorder default.
        string path = System.IO.Path.Combine(Application.persistentDataPath, filename);
        
        try
        {
            System.IO.File.WriteAllText(path, json);
            Debug.Log($"TargetTrailManager: Exported definitions to {path}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"TargetTrailManager: Failed to export. Error: {e.Message}");
        }
    }
}
