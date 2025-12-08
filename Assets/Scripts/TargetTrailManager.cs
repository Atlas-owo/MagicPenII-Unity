using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TargetTrailManager : MonoBehaviour
{
    [Header("Settings")]
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
    }

    [Header("Trail Configuration")]
    public List<TrailDefinition> preDefinedTrails = new List<TrailDefinition>();

    private void Start()
    {
        pathRecorder = FindObjectOfType<PathRecorder>();

        // 1. Add trails defined in Inspector
        foreach (var def in preDefinedTrails)
        {
            if (def.startTransform != null && def.endTransform != null)
            {
                AddStraightTrail(def.startTransform.position, def.endTransform.position);
            }
        }

        // 2. Add debug trail if configured (optional fallback)
        if (preDefinedTrails.Count == 0 && debugStartPoint != null && debugEndPoint != null)
        {
            AddStraightTrail(debugStartPoint.position, debugEndPoint.position);
        }

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

        // Pass trails.Count as the ID
        trail.Initialize(start, end, this, penTip, trails.Count);
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
}
