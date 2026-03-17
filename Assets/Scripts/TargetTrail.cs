using System;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class TargetTrail : MonoBehaviour, ITrailEvaluator
{
    public enum TrailShape
    {
        Straight,
        SineWave,
        Nurbs,
        Circle
    }

    [Header("Configuration")]
    public TrailShape trailShape = TrailShape.Straight;
    public float tubeRadius = 0.003f;
    public float sphereRadius = 0.006f;
    public int radialSegments = 16;
    
    [Header("Sine Wave Settings")]
    public float amplitudeStart = 0.05f;
    public float amplitudeEnd = 0.1f;
    public float periods = 2.0f;

    [Header("NURBS / Plateau Settings")]
    [Range(0.05f, 0.9f)]
    public float nurbsPlateauWidth = 0.3f;
    [Range(0.01f, 0.2f)]
    public float nurbsTransitionLength = 0.05f;
    [Range(1.0f, 10.0f)]
    public float nurbsTransitionSteepness = 5.0f;
    public float nurbsAmplitude = 0.05f;

    [Header("Visuals")]
    public Color trailColor = new Color(0, 0, 0, 0.5f); // Translucent Black
    public Color startColor = new Color(1, 1, 0, 0.5f); // Translucent Yellow
    public Color activeStartColor = new Color(0, 1, 0, 0.5f); // Translucent Green (Active)
    public Color endColor = new Color(1, 0, 0, 0.5f);   // Translucent Red
    
    [Header("Audio")]
    public AudioClip successSound;
    private AudioSource audioSource;

    [Header("State")]
    public bool isActive = false;
    public bool isCompleted = false;
    
    // Internal references
    private Vector3 startPoint;
    private Vector3 endPoint;
    private GameObject startSphere;
    private GameObject endSphere;
    private GameObject guideSphere; // New Guide Ball
    private Transform trackedPenTip; // The transform we are tracking
    private TargetTrailManager manager;
    private TubeTrailRenderer tubeTrailRenderer;
    private PathRecorder pathRecorder;
    private HapticPenController penController; // Need direct access for button state
    private int trailId;      // Instance ID
    private int trailTypeId;  // Definition Type ID
    
    // Interaction state
    private bool hasStarted = false;
    private bool wasDrawing = false;
    private bool hasHitEnd = false;

    public void Initialize(Vector3 start, Vector3 end, TargetTrailManager mgr, Transform penTipOverride, int id, int typeId)
    {
        startPoint = start;
        endPoint = end;
        manager = mgr;
        trailId = id;
        trailTypeId = typeId;
        
        GenerateMesh();
        CreateSpheres();
        
        // Setup Audio
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        if (successSound != null)
        {
            audioSource.clip = successSound;
        }

        // Determine what to track
        if (penTipOverride != null)
        {
            trackedPenTip = penTipOverride;
        }
        else
        {
            // Fallback: Find pen controller
            penController = FindObjectOfType<HapticPenController>();
            if (penController != null)
            {
                trackedPenTip = penController.penTip;
            }
        }
        
        if (trackedPenTip == null)
        {
            Debug.LogWarning("TargetTrail: No pen tip found to track!");
        }

        // Find TubeTrailRenderer
        tubeTrailRenderer = FindObjectOfType<TubeTrailRenderer>();
        if (tubeTrailRenderer != null)
        {
            tubeTrailRenderer.clearOnStrokeEnd = true;
        }

        // Find PathRecorder
        pathRecorder = FindObjectOfType<PathRecorder>();
        if (pathRecorder != null)
        {
            // Disable auto-save so we can manually control commits
            pathRecorder.autoSave = false;
        }
        
        // Initially hidden until activated by manager
        gameObject.SetActive(false);
    }

    public void Activate()
    {
        gameObject.SetActive(true);
        isActive = true;
        hasStarted = false;
        hasHitEnd = false;
        isCompleted = false;
        wasDrawing = false;
        
        // Ensure visuals are on
        if (startSphere) startSphere.SetActive(true);
        if (endSphere) endSphere.SetActive(true);
        GetComponent<MeshRenderer>().enabled = true;

        // Reset start sphere color
        SetMaterial(startSphere, startColor);

        // Ensure we have pen controller reference for button state
        if (penController == null)
        {
            penController = FindObjectOfType<HapticPenController>();
        }

        // Set Recorder Type ID
        if (pathRecorder != null)
        {
            pathRecorder.SetCurrentTrailType(trailTypeId);
            pathRecorder.SetEvaluator(this); // Set self as evaluator
            pathRecorder.manualControl = (manager.interactionMode == TargetTrailManager.InteractionMode.AutoTouch);
            pathRecorder.isCapturingOverride = false;
        }

        if (tubeTrailRenderer != null)
        {
            tubeTrailRenderer.manualControl = (manager.interactionMode == TargetTrailManager.InteractionMode.AutoTouch);
            tubeTrailRenderer.isDrawing = false;
        }
    }

    private void Update()
    {
        if (!isActive || isCompleted || trackedPenTip == null || penController == null) return;

        if (manager.interactionMode == TargetTrailManager.InteractionMode.ButtonPress)
        {
            HandleButtonPressMode();
        }
        else
        {
            HandleAutoTouchMode();
        }
        
        UpdateGuideBall();
    }

    private void UpdateGuideBall()
    {
        if (guideSphere != null && trailShape == TrailShape.Straight)
        {
            if (hasStarted && !isCompleted && isActive)
            {
                if (!guideSphere.activeSelf) guideSphere.SetActive(true);
                // Use infinite line projection
                Vector3 closestPoint = GetClosestPointOnLine(startPoint, endPoint, trackedPenTip.position);
                guideSphere.transform.position = closestPoint;
            }
            else
            {
                if (guideSphere.activeSelf) guideSphere.SetActive(false);
            }
        }
    }

    // Renamed to 'Line' to reflect infinite extent
    private Vector3 GetClosestPointOnLine(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ap = p - a;
        Vector3 ab = b - a;
        float magnitudeAB = ab.sqrMagnitude;
        if (magnitudeAB < 0.000001f) return a;
        float ABAPproduct = Vector3.Dot(ap, ab);
        float distance = ABAPproduct / magnitudeAB;

        // Removed clamping (distance < 0 or > 1) to allow movement beyond endpoints
        return a + ab * distance;
    }

    private void HandleButtonPressMode()
    {
        bool isDrawing = penController.buttonPressed;

        // [NEW] Dynamic Actuation Switching:
        // When button is pressed -> Active Hybrid Pressure Control
        // When button is released -> Disable (Standard Raycast Mode)
        if (penController.enableDirectPressureControl != isDrawing)
        {
            penController.enableDirectPressureControl = isDrawing;
        }

        // 1. Detect Start of Stroke (Rising Edge)
        if (isDrawing && !wasDrawing)
        {
            // Check if we are at Start Point
            if (Vector3.Distance(trackedPenTip.position, startPoint) < sphereRadius)
            {
                hasStarted = true;
                // Visual feedback: Change start sphere to Green
                SetMaterial(startSphere, activeStartColor);
                
                // Start Valid Stroke
                if (pathRecorder != null)
                {
                    pathRecorder.StartNewStroke(trailId, trailTypeId);
                }
                Debug.Log($"TargetTrail: Valid Stroke Started! (Instance {trailId}, Type {trailTypeId})");
            }
            else
            {
                hasStarted = false;
                // Start Invalid Stroke (will be discarded)
                if (pathRecorder != null)
                {
                    pathRecorder.StartNewStroke(-1, -1); 
                }
                Debug.Log("TargetTrail: Invalid Stroke Started (Outside Start)");
            }
        }

            // 2. While Drawing
        // (We no longer auto-complete here; just wait for user to release)
        if (isDrawing)
        {
            if (hasStarted)
            {
                if (Vector3.Distance(trackedPenTip.position, endPoint) < sphereRadius)
                {
                    if (!hasHitEnd)
                    {
                        hasHitEnd = true;
                        // Play sound on first contact
                        if (successSound != null && audioSource != null) audioSource.PlayOneShot(successSound);
                    }
                }
            }
        }

        // 3. Detect End of Stroke (Falling Edge / Button Release)
        if (!isDrawing && wasDrawing)
        {
            // User lifted pen
            if (hasStarted)
            {
                // Check if we hit End Point at any time during the stroke
                if (hasHitEnd)
                {
                    // Success!
                    CompleteTrail();
                }
                // Double check if we released INSIDE the sphere but missed the frame check
                else if (Vector3.Distance(trackedPenTip.position, endPoint) < sphereRadius)
                {
                    if (successSound != null && audioSource != null) audioSource.PlayOneShot(successSound);
                    CompleteTrail();
                }
                else
                {
                    // Started valid, but lifted outside End -> Fail
                    Debug.Log("TargetTrail: Stroke Failed (Lifted outside End Area)");
                    ResetTrail();
                    
                    // Discard data
                    if (pathRecorder != null)
                    {
                        pathRecorder.DiscardStroke();
                    }
                    
                    // Clear visual trail
                    if (tubeTrailRenderer != null)
                    {
                        tubeTrailRenderer.Clear();
                    }
                }
            }
            else
            {
                // Started invalid -> Discard
                Debug.Log("TargetTrail: Invalid Stroke Discarded");
                // (Cleanup handles below)
                 if (pathRecorder != null) pathRecorder.DiscardStroke();
                 if (tubeTrailRenderer != null) tubeTrailRenderer.Clear();
            }
        }

        wasDrawing = isDrawing;
    }

    private void HandleAutoTouchMode()
    {
        if (!hasStarted)
        {
            // Wait for Start Touch
            if (Vector3.Distance(trackedPenTip.position, startPoint) < sphereRadius)
            {
                hasStarted = true;
                
                // Start Everything
                SetMaterial(startSphere, activeStartColor);
                
                if (pathRecorder != null)
                {
                    pathRecorder.isCapturingOverride = true;
                    pathRecorder.StartNewStroke(trailId, trailTypeId);
                }
                
                if (tubeTrailRenderer != null)
                {
                    tubeTrailRenderer.isDrawing = true;
                    // Need to manually trigger logic in TubeTrailRenderer? 
                    // No, setting isDrawing=true in Update (via manual check) should trigger "StartNewStroke" inside TubeTrailRenderer if it detects edge.
                    // But TubeTrailRenderer's Update runs every frame. We just set the boolean.
                }

                Debug.Log($"TargetTrail: Auto-Touch Started! (Instance {trailId})");
            }
        }
        else
        {
            // We are "Drawing" (implicitly)
            // Check Distances
            
            // 1. Check Success (End Point)
            if (Vector3.Distance(trackedPenTip.position, endPoint) < sphereRadius)
            {
                if (successSound != null && audioSource != null) audioSource.PlayOneShot(successSound);
                CompleteTrail();
            }
            
            // 2. Optional: Check "Drift" failure? 
            // If user behaves weirdly, maybe we stop? 
            // "when hit the ending point, the stroke automatically ends". 
            // It doesn't say "if you stop touching the start point it fails". 
            // So once started, it goes until it hits End.
            // BUT, if they move VERY far away, maybe we should cancel?
            // For now, assume strict "Start -> ... -> End".
        }
    }

    private void ResetTrail()
    {
        hasStarted = false;
        hasHitEnd = false;
        // Reset visual feedback
        SetMaterial(startSphere, startColor);
        if (guideSphere) guideSphere.SetActive(false);
    }

    private void CompleteTrail()
    {
        isCompleted = true;
        isActive = false;
        
        // Reset AutoTouch State
        if (pathRecorder != null)
        {
            pathRecorder.isCapturingOverride = false;
            // pathRecorder.manualControl = false; // Keep it true if mode dictates, or reset?
            // Better to keep it consistent with Activate()
        }
        if (tubeTrailRenderer != null)
        {
            tubeTrailRenderer.isDrawing = false;
        }

        Debug.Log("TargetTrail: Completed!");

        Debug.Log("TargetTrail: Completed!");

        // Play Sound (Moved to trigger points)
        // if (successSound != null && audioSource != null) ...
        
        // Disappear
        if (startSphere) startSphere.SetActive(false);
        if (endSphere) endSphere.SetActive(false);
        if (guideSphere) guideSphere.SetActive(false);
        GetComponent<MeshRenderer>().enabled = false;

        // Commit Data
        if (pathRecorder != null)
        {
            pathRecorder.CommitStroke();
        }

        // Clear user's drawn trail
        if (tubeTrailRenderer != null)
        {
            tubeTrailRenderer.Clear();
        }

        // Notify Manager
        if (manager != null)
        {
            manager.OnTrailCompleted(this);
        }
    }

    private void CreateSpheres()
    {
        // Start Sphere
        startSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        startSphere.name = "StartSphere";
        startSphere.transform.SetParent(transform);
        startSphere.transform.position = startPoint;
        startSphere.transform.localScale = Vector3.one * (sphereRadius * 2);
        SetMaterial(startSphere, startColor);
        Destroy(startSphere.GetComponent<Collider>()); // Remove collider to avoid physics interference

        // End Sphere
        endSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        endSphere.name = "EndSphere";
        endSphere.transform.SetParent(transform);
        endSphere.transform.position = endPoint;
        endSphere.transform.localScale = Vector3.one * (sphereRadius * 2);
        SetMaterial(endSphere, endColor);
        Destroy(endSphere.GetComponent<Collider>());

        // Guide Sphere (Initially Hidden)
        guideSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        guideSphere.name = "GuideSphere";
        guideSphere.transform.SetParent(transform);
        guideSphere.transform.position = startPoint;
        guideSphere.transform.localScale = Vector3.one * (0.01f * 2); // Radius 0.01
        SetMaterial(guideSphere, new Color(1, 1, 1, 0.5f)); // Translucent White
        Destroy(guideSphere.GetComponent<Collider>());
        guideSphere.SetActive(false);
    }

    private void GenerateMesh()
    {
        MeshFilter meshFilter = GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
        
        Mesh mesh = new Mesh();
        meshFilter.mesh = mesh;
        
        // Create material for tube
        Material tubeMat = new Material(Shader.Find("Standard"));
        SetupTranslucentMaterial(tubeMat, trailColor);
        meshRenderer.material = tubeMat;

        // Determine number of segments based on length
        float dist = Vector3.Distance(startPoint, endPoint);
        int curveSegments = Mathf.CeilToInt(dist * 200); // Increased resolution (0.5cm)
        if (trailShape == TrailShape.SineWave) curveSegments *= 2; 
        if (trailShape == TrailShape.Nurbs) curveSegments *= 4; // Higher resolution for sharp transitions
        if (curveSegments < 2) curveSegments = 2;

        int vertCount = curveSegments * radialSegments;
        Vector3[] vertices = new Vector3[vertCount];
        int[] triangles = new int[(curveSegments - 1) * radialSegments * 6];

        // Calculate Plane Normal for Frame Consistency (Zero Twist)
        Vector3 baseline = endPoint - startPoint;
        Vector3 baselineDir = baseline.normalized;
        Vector3 waveUp = Vector3.Cross(baselineDir, Vector3.forward).normalized;
        if (waveUp.sqrMagnitude < 0.001f) waveUp = Vector3.up;
        
        // The curve lies in the plane defined by Baseline and WaveUp.
        // The normal to this plane is constant.
        Vector3 planeNormal = Vector3.Cross(baselineDir, waveUp).normalized;
        
        // Fallback for straight line on Z axis where cross might be weird?
        // If baseline is (0,0,1), waveUp is (0,1,0), planeNormal is (-1,0,0). Correct.

        for (int i = 0; i < curveSegments; i++)
        {
            float t = (float)i / (curveSegments - 1);
            Vector3 currentPos = GetPointOnPath(t);

            // Calculate tangent
            Vector3 tangent;
            if (i < curveSegments - 1)
            {
                tangent = (GetPointOnPath(t + 0.001f) - currentPos).normalized;
            }
            else
            {
                tangent = (currentPos - GetPointOnPath(t - 0.001f)).normalized;
            }

            // Construct Frame using Fixed Plane Normal
            // Right vector = PlaneNormal (Constant)
            // Up vector = Cross(Tangent, Right) (Changes with Tangent)
            // Note: We need to ensure Tangent is not parallel to PlaneNormal. 
            // Since Tangent is IN the plane, and PlaneNormal is orthogonal to plane, they are always perpendicular.
            // So Cross product is safe and stable.
            
            // We'll use this consistent frame to avoid twisting.
            Vector3 frameRight = planeNormal;
            Vector3 frameUp = Vector3.Cross(tangent, frameRight).normalized;
            // Recalculate Right to ensure perfect orthogonality (though it should be already)
            frameRight = Vector3.Cross(frameUp, tangent).normalized;

            for (int j = 0; j < radialSegments; j++)
            {
                float angle = j * Mathf.PI * 2f / radialSegments;
                float sin = Mathf.Sin(angle);
                float cos = Mathf.Cos(angle);

                // Build ring on the Frame
                Vector3 offset = (frameRight * cos + frameUp * sin) * tubeRadius;
                // Convert to local space
                vertices[i * radialSegments + j] = transform.InverseTransformPoint(currentPos + offset);
            }
        }

        int triIndex = 0;
        for (int i = 0; i < curveSegments - 1; i++)
        {
            for (int j = 0; j < radialSegments; j++)
            {
                int current = i * radialSegments + j;
                int next = i * radialSegments + (j + 1) % radialSegments;
                int nextRingCurrent = (i + 1) * radialSegments + j;
                int nextRingNext = (i + 1) * radialSegments + (j + 1) % radialSegments;

                triangles[triIndex++] = current;
                triangles[triIndex++] = nextRingCurrent;
                triangles[triIndex++] = next;

                triangles[triIndex++] = nextRingCurrent;
                triangles[triIndex++] = nextRingNext;
                triangles[triIndex++] = next;
            }
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals(); // Should be smooth now with consistent topology
    }

    private Vector3 GetPointOnPath(float t)
    {
        Vector3 straightPos = Vector3.Lerp(startPoint, endPoint, t);

        if (trailShape == TrailShape.Straight)
        {
            return straightPos;
        }
        else if (trailShape == TrailShape.SineWave)
        {
            // Baseline direction
            Vector3 baseline = endPoint - startPoint;
            Vector3 baselineDir = baseline.normalized;

            // Define "Up" for the sine wave. 
            // Standard Perpendicular on XY plane (Assuming Z is Normal):
            // Cross(baselineDir, Vector3.forward) gives a vector in XY plane perpendicular to baseline.
            Vector3 waveUp = Vector3.Cross(baselineDir, Vector3.forward).normalized;
            // If cross is zero (baseline is Z), fallback
            if (waveUp.sqrMagnitude < 0.001f) waveUp = Vector3.up;

            float currentAmplitude = Mathf.Lerp(amplitudeStart, amplitudeEnd, t);
            // 2 periods means 0 to 4pi? User asked for 2 periods. 
            // 2 periods = 2 * 2PI = 4PI.
            // But wait, "2 downhills and 2 uphills".
            // 1 period = 1 uphill (peak) + 1 downhill (trough). 
            // So 2 periods is correct.
            float sineValue = Mathf.Sin(t * periods * Mathf.PI * 2f);

            return straightPos + waveUp * currentAmplitude * sineValue;
        }
        else if (trailShape == TrailShape.Nurbs)
        {
            // Baseline direction
            Vector3 baseline = endPoint - startPoint;
            Vector3 baselineDir = baseline.normalized;

            // Define "Up" 
            Vector3 waveUp = Vector3.Cross(baselineDir, Vector3.forward).normalized;
            if (waveUp.sqrMagnitude < 0.001f) waveUp = Vector3.up;

            float height = CalculateNurbsHeight(t);
            return straightPos + waveUp * height;
        }

        return straightPos;
    }


    private void SetMaterial(GameObject obj, Color color)
    {
        MeshRenderer mr = obj.GetComponent<MeshRenderer>();
        Material mat = new Material(Shader.Find("Standard"));
        SetupTranslucentMaterial(mat, color);
        mr.material = mat;
    }

    private void SetupTranslucentMaterial(Material mat, Color color)
    {
        mat.SetFloat("_Mode", 3); // Transparent
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
        mat.color = color;
    }

    private float CalculateNurbsHeight(float t)
    {
        // Convert to centered coordinate [-0.5, 0.5]
        float tCentered = t - 0.5f;
        float distFromCenter = Mathf.Abs(tCentered);

        float plateauHalfWidth = nurbsPlateauWidth * 0.5f;
        float transitionEnd = plateauHalfWidth + nurbsTransitionLength;

        float heightValue;

        if (distFromCenter <= plateauHalfWidth)
        {
            heightValue = 1.0f;
        }
        else if (distFromCenter < transitionEnd)
        {
            float p = (distFromCenter - plateauHalfWidth) / nurbsTransitionLength;
            heightValue = GeneralizedSmoothstep(1.0f - p, nurbsTransitionSteepness);
        }
        else
        {
            heightValue = 0.0f;
        }

        return nurbsAmplitude * heightValue;
    }

    // --- ITrailEvaluator Implementation ---

    public Vector3 GetClosestPointOnCenterline(Vector3 position)
    {
        // High Precision Iterative Search
        
        // 1. Coarse Search (Global)
        int samples = 100; // Increased from 50
        float bestT = 0f;
        float bestDistSq = float.MaxValue;
        
        for (int i = 0; i <= samples; i++)
        {
            float t = (float)i / samples;
            Vector3 pt = GetPointOnPath(t);
            float dSq = (position - pt).sqrMagnitude;
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                bestT = t;
            }
        }
        
        // 2. Refinement Search 1 (Local Scan +/- 1%)
        float step = 1f / samples; // 0.01
        float range = step; 
        float minT = Mathf.Max(0f, bestT - range);
        float maxT = Mathf.Min(1f, bestT + range);
        int refinementSteps = 20;
        
        for (int i = 0; i <= refinementSteps; i++)
        {
            float t = Mathf.Lerp(minT, maxT, (float)i / refinementSteps);
            Vector3 pt = GetPointOnPath(t);
            float dSq = (position - pt).sqrMagnitude;
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                bestT = t; // Update bestT for next pass
            }
        }
        
        // 3. Refinement Search 2 (Micro Scan +/- 0.1%)
        float microRange = range / 10f; // 0.001
        minT = Mathf.Max(0f, bestT - microRange);
        maxT = Mathf.Min(1f, bestT + microRange);
        refinementSteps = 20; // Another 20 samples in very small window
        
        for (int i = 0; i <= refinementSteps; i++)
        {
            float t = Mathf.Lerp(minT, maxT, (float)i / refinementSteps);
            Vector3 pt = GetPointOnPath(t);
            float dSq = (position - pt).sqrMagnitude;
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                bestT = t;
            }
        }

        return GetPointOnPath(bestT);
    }
    
    // ... existing ...

    private float GeneralizedSmoothstep(float t, float steepness)
    {
        t = Mathf.Clamp01(t);
        float smoothed = t * t * (3.0f - 2.0f * t);
        if (steepness > 1.0f)
        {
            float centered = smoothed - 0.5f;
            smoothed = 0.5f + Mathf.Sign(centered) * Mathf.Pow(Mathf.Abs(centered * 2.0f), 1.0f / steepness) * 0.5f;
        }
        return smoothed;
    }
}
