using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TargetTrailManager : MonoBehaviour
{
    public enum InteractionMode
    {
        ButtonPress,
        AutoTouch
    }

    [Header("Settings")]
    public InteractionMode interactionMode = InteractionMode.ButtonPress;
    public float delayBetweenTrails = 1.0f;
    public AudioClip successSound;

    [Header("Debug / Testing")]
    public Transform debugStartPoint;
    public Transform debugEndPoint;

    [Header("References")]
    [Tooltip("The Transform representing the pen tip. If null, trails will try to find HapticPenController automatically.")]
    public Transform penTip;

    private List<TargetTrail> trails = new List<TargetTrail>();
    private int currentTrailIndex = -1;
    private PathRecorder pathRecorder;

    [System.Serializable]
    public struct TrailDefinition
    {
        public Transform startTransform;
        public Transform endTransform;
        public TargetTrail.TrailShape shape;
        public float amplitudeStart; // e.g. 0.05
        public float amplitudeEnd;   // e.g. 0.1
        public float periods;        // e.g. 2
    }

    [Header("Trail Configuration")]
    public List<TrailDefinition> preDefinedTrails = new List<TrailDefinition>();
    public int repeatsPerTrail = 3;

    private void Start()
    {
        pathRecorder = FindObjectOfType<PathRecorder>();

        // Generate Task List (Indices of definitions)
        List<int> taskIndices = new List<int>();
        for (int i = 0; i < preDefinedTrails.Count; i++)
        {
            for (int r = 0; r < repeatsPerTrail; r++)
            {
                taskIndices.Add(i);
            }
        }
        
        // Shuffle Tasks (Fisher-Yates)
        // Only shuffle if we have multiple tasks
        if (taskIndices.Count > 1)
        {
            UnityEngine.Random.InitState((int)System.DateTime.Now.Ticks);
            int n = taskIndices.Count;
            while (n > 1)
            {
                n--;
                int k = UnityEngine.Random.Range(0, n + 1);
                int value = taskIndices[k];
                taskIndices[k] = taskIndices[n];
                taskIndices[n] = value;
            }
        }

        // 1. Create trails based on Shuffled Task List
        for (int i = 0; i < taskIndices.Count; i++)
        {
            int defIndex = taskIndices[i];
            TrailDefinition def = preDefinedTrails[defIndex];

            if (def.startTransform != null && def.endTransform != null)
            {
                // Create object
                GameObject trailObj = new GameObject($"TargetTrail_Inst{i}_Type{defIndex}");
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

                if (successSound != null) trail.successSound = successSound;

                // Initialize: Instance ID = i, Type ID = defIndex
                trail.Initialize(def.startTransform.position, def.endTransform.position, this, penTip, i, defIndex);
                trails.Add(trail);
            }
        }

        // 2. Add debug trail if configured (optional fallback, only if no specific trails)
        if (trails.Count == 0 && debugStartPoint != null && debugEndPoint != null)
        {
            AddStraightTrail(debugStartPoint.position, debugEndPoint.position);
        }

        Debug.Log($"TargetTrailManager: Generated {trails.Count} trails ({preDefinedTrails.Count} types x {repeatsPerTrail} repeats).");

        // 3. Start the sequence
        StartTrails();
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
