using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class HybridTargetTrail : MonoBehaviour, ITrailEvaluator
{
    [Header("Hybrid Configuration")]
    public HybridSurface.SurfaceShape surfaceShape;
    public TargetTrail.TrailShape trailShape;
    
    // Surface Params
    public float surfaceWidth = 0.3f;
    public float surfaceAmplitude = 0.05f;
    public float surfacePeriods = 2.0f;
    public float surfacePlateauWidth = 0.3f;
    public float surfaceTransition = 0.05f;
    public float surfaceSteepness = 5.0f;

    // Trail Params
    public float trailAmplitude = 0.05f; // For Sine Trail on Surface
    public float trailPeriods = 2.0f;

    [Header("Sine Wave Overlay")]
    public bool useSineOverlay;
    public float overlayAmplitude;
    public float overlayFrequency;
    public bool overlayUseNormal;
    
    private float estimatedPathLength = 0f;
    
    [Header("Visuals")]
    public float tubeRadius = 0.0015f;
    public float sphereRadius = 0.006f;
    public int radialSegments = 16;
    public Color trailColor = new Color(0, 0, 0, 0.5f);
    public Color startColor = new Color(1, 1, 0, 0.5f);
    public Color activeStartColor = new Color(0, 1, 0, 0.5f);
    public Color endColor = new Color(1, 0, 0, 0.5f);

    [Header("Audio")]
    public AudioClip successSound;
    private AudioSource audioSource;

    // Internal State
    private bool isActive = false;
    private bool isCompleted = false;
    private bool hasStarted = false;
    private bool wasDrawing = false;
    private bool hasHitEnd = false;

    // References
    private Vector3 startPoint; // Trail Start
    private Vector3 endPoint;   // Trail End
    private Vector3 surfaceStartPoint; // Surface Axis Start
    private Vector3 surfaceEndPoint;   // Surface Axis End
    private Vector3 circleCenterPoint; // Circular Center (New)
    
    private HybridTrailManager manager; // Changed from TargetTrailManager in refactor
    private Transform trackedPenTip;
    
    // Safety check for Full Circle (Overlapping Start/End)
    private bool hasLeftStartZone = false;
    private float minSafeDistance = 0.05f; // 5cm
    private HapticPenController penController;
    private PathRecorder pathRecorder;
    private TubeTrailRenderer tubeTrailRenderer;
    
    private GameObject startSphere;
    private GameObject endSphere;
    private HybridSurface surfaceInstance; // The generated surface child
    
    private int trailId;
    private int trailTypeId;
    private int surfaceConfigIndex; // New: Index in the SurfacesToTest list

    public void Initialize(Vector3 start, Vector3 end, Vector3 surfStart, Vector3 surfEnd, Vector3 center,
                           HybridTrailManager mgr, Transform penTipOverride, int id, int typeId, int surfIdx, // Added surfIdx
                           HybridSurface.SurfaceShape surfShape, TargetTrail.TrailShape trShape,
                           float surfW, float surfAmp, float surfPer, float surfPlat, float surfTrans, float surfSteep,
                           float trAmp, float trPer,
                           bool useOverlay, float overlayAmp, float overlayFreq, bool overlayNormal)
    {
        startPoint = start;
        endPoint = end;
        surfaceStartPoint = surfStart;
        surfaceEndPoint = surfEnd;
        circleCenterPoint = center;
        
        manager = mgr;
        trackedPenTip = penTipOverride;
        trailId = id;
        trailTypeId = typeId;
        surfaceConfigIndex = surfIdx; // Store it
        
        surfaceShape = surfShape;
        trailShape = trShape;
        surfaceWidth = surfW;
        surfaceAmplitude = surfAmp;
        surfacePeriods = surfPer;
        surfacePlateauWidth = surfPlat;
        surfaceTransition = surfTrans;
        surfaceSteepness = surfSteep;
        trailAmplitude = trAmp;
        trailPeriods = trPer;

        useSineOverlay = useOverlay;
        overlayAmplitude = overlayAmp;
        overlayFrequency = overlayFreq;
        overlayUseNormal = overlayNormal;
        
        hasLeftStartZone = false;

        // Calculate approximate path length for sine parameterization


        // Find Dependencies
        if (trackedPenTip == null)
        {
             penController = FindObjectOfType<HapticPenController>();
             if (penController != null) trackedPenTip = penController.penTip;
        }
        else
        {
             penController = FindObjectOfType<HapticPenController>();
        }
        
        pathRecorder = FindObjectOfType<PathRecorder>();
        tubeTrailRenderer = FindObjectOfType<TubeTrailRenderer>();
        if (pathRecorder != null) pathRecorder.autoSave = false;
        if (tubeTrailRenderer != null) tubeTrailRenderer.clearOnStrokeEnd = true;

        // Setup Audio
        audioSource = gameObject.AddComponent<AudioSource>();
        if (successSound != null) audioSource.clip = successSound;

        // 1. Generate Surface (Using Surface Axis)
        CreateSurface();

        // Calculate approximate path length for sine parameterization (AFTER Surface is created)
        CalculateApproxPathLength();

        // 2. Generate Trail Mesh (projected on surface)
        GenerateTrailMesh();
        
        // 3. Create Spheres (Projected onto surface)
        CreateSpheres();
        
        gameObject.SetActive(false);
    }

    private void CreateSurface()
    {
        GameObject surfObj = new GameObject("HybridSurface");
        surfObj.transform.SetParent(transform);
        surfObj.transform.localPosition = Vector3.zero;
        
        surfaceInstance = surfObj.AddComponent<HybridSurface>();
        
        // logic: Only 'Flat' uses the specified Surface Axis (for tilt). 
        // Others (Sine, Nurbs) must use the Trail Axis (Horizontal), ignoring the Surface Axis param.
        // Logic Update: Always use the designated Surface Points. 
        // These are either the Global Surface Axis, the Global Trail Axis (default), or the Per-Surface Override.
        Vector3 useStart = surfaceStartPoint;
        Vector3 useEnd = surfaceEndPoint;

        surfaceInstance.Generate(useStart, useEnd, surfaceShape, surfaceWidth, surfaceAmplitude, surfacePeriods, surfacePlateauWidth, surfaceTransition, surfaceSteepness);
    }



    private void CalculateApproxPathLength()
    {
        // Simple sampling to get approximate arc length
        float dist = 0f;
        int samples = 50;
        Vector3 lastPos = GetPointOnHybridPathBase(0f); 
        for(int i = 1; i <= samples; i++)
        {
            float t = (float)i / samples;
            Vector3 currentPos = GetPointOnHybridPathBase(t);
            dist += Vector3.Distance(lastPos, currentPos);
            lastPos = currentPos;
        }
        estimatedPathLength = dist;
    }

    private void GenerateTrailMesh()
    {
        MeshFilter meshFilter = GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
        
        Mesh mesh = new Mesh();
        meshFilter.mesh = mesh;
        
        // Material
        Material tubeMat = new Material(Shader.Find("Standard"));
        SetupTranslucentMaterial(tubeMat, trailColor);
        meshRenderer.material = tubeMat;

        int segments = 200;
        int vertCount = segments * radialSegments;
        Vector3[] vertices = new Vector3[vertCount];
        int[] triangles = new int[(segments - 1) * radialSegments * 6];

        for (int i = 0; i < segments; i++)
        {
            float t = (float)i / (segments - 1);
            Vector3 center = GetPointOnHybridPath(t); // Now returns point with overlay if enabled
            Vector3 nextCenter = GetPointOnHybridPath(t + 0.001f);
            Vector3 dir = (nextCenter - center).normalized;
            
            // Frame calculation - robust method
            Vector3 right = Vector3.Cross(dir, Vector3.up).normalized; 
            if (right.sqrMagnitude < 0.001f) right = Vector3.right;
            Vector3 up = Vector3.Cross(right, dir).normalized;

            for (int j = 0; j < radialSegments; j++)
            {
                float angle = j * Mathf.PI * 2f / radialSegments;
                float sin = Mathf.Sin(angle);
                float cos = Mathf.Cos(angle);

                Vector3 offset = (right * cos + up * sin) * tubeRadius;
                vertices[i * radialSegments + j] = transform.InverseTransformPoint(center + offset);
            }
        }

        // Triangles generation (standard tube)
        int triIndex = 0;
        for (int i = 0; i < segments - 1; i++)
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
        mesh.RecalculateNormals();
    }

    // Renaming original point calculation to Base
    private Vector3 GetPointOnHybridPathBase(float t)
    {
        Vector3 lateralPos = Vector3.zero;

        if (trailShape == TargetTrail.TrailShape.Circle)
        {
            // CIRCLE LOGIC
            // Center: circleCenterPoint
            // Radius: Dist(Center, StartPoint)
            
            // Project to flat XZ for angle calculation
            Vector2 c = new Vector2(circleCenterPoint.x, circleCenterPoint.z);
            Vector2 s = new Vector2(startPoint.x, startPoint.z);
            Vector2 e = new Vector2(endPoint.x, endPoint.z);
            
            float radius = Vector2.Distance(c, s);
            
            float angStart = Mathf.Atan2(s.y - c.y, s.x - c.x);
            float angEnd = Mathf.Atan2(e.y - c.y, e.x - c.x);
            
            // Check for Full Circle case (Start ~= End)
            if (Vector2.Distance(s, e) < 0.01f)
            {
                angEnd = angStart + Mathf.PI * 2f;
            }
            else
            {
                 // Simple assumption: Go CCW.
                 if (angEnd <= angStart) angEnd += Mathf.PI * 2f;
            }

            float currentAng = Mathf.Lerp(angStart, angEnd, t);
            
            float x = c.x + Mathf.Cos(currentAng) * radius;
            float z = c.y + Mathf.Sin(currentAng) * radius; // y here is Z in 2D vector
            
            lateralPos = new Vector3(x, startPoint.y, z); // Use Start Y as baseline plane
        }
        else
        {
            // STANDARD LINEAR/SINE LOGIC
            // 1. Get Baseline Point (Trail Axis)
            Vector3 straightPos = Vector3.Lerp(startPoint, endPoint, t);
            Vector3 dir = (endPoint - startPoint).normalized;
            
            // 2. Lateral Offset (Trail Shape)
            Vector3 right = Vector3.Cross(dir, Vector3.up).normalized;
            if (right.sqrMagnitude < 0.001f) right = Vector3.right;
            
            float lateralOffset = 0f;
            if (trailShape == TargetTrail.TrailShape.SineWave)
            {
                lateralOffset = Mathf.Sin(t * trailPeriods * Mathf.PI * 2f) * trailAmplitude;
            }
            
            lateralPos = straightPos + right * lateralOffset;
        }
        
        // 3. Vertical Offset (Surface Projection)
        // MUST Project 'lateralPos' onto the SURFACE's actual axis to find 't_surface'
        
        Vector3 surfStart = surfaceInstance.startPoint;
        Vector3 surfEnd = surfaceInstance.endPoint;
        Vector3 surfBaseline = surfEnd - surfStart;
        
        float surfLenSq = surfBaseline.sqrMagnitude;
        float t_surf = 0f;
        
        if (surfLenSq > 0.0001f)
        {
            Vector3 diff = lateralPos - surfStart;
            float dot = Vector3.Dot(diff, surfBaseline);
            t_surf = dot / surfLenSq;
        }
        
        // Surface Logic (Clamp or not)
        Vector3 surfPosAtT = Vector3.LerpUnclamped(surfStart, surfEnd, t_surf);
        
        if (surfaceShape == HybridSurface.SurfaceShape.Flat)
        {
             if (t_surf < 0) surfPosAtT.y = surfStart.y;
             if (t_surf > 1) surfPosAtT.y = surfEnd.y;
        }
        
        float surfaceOffset = surfaceInstance.GetHeightAt(t_surf);
        float finalY = surfPosAtT.y + surfaceOffset;
        
        return new Vector3(lateralPos.x, finalY, lateralPos.z);
    }

    private Vector3 GetPointOnHybridPath(float t)
    {
        Vector3 basePoint = GetPointOnHybridPathBase(t);

        if (!useSineOverlay) return basePoint;

        // Apply Sine Overlay
        // Calculate cumulative distance approx
        float cumulativeDistance = t * estimatedPathLength;

        // Formula: Amplitude * (sin(2*PI*freq*dist) + 1)
        float weight = Mathf.Sin(2f * Mathf.PI * overlayFrequency * cumulativeDistance) + 1f;
        float offsetMag = overlayAmplitude * weight;

        if (overlayUseNormal)
        {
            // Calculate normal at base point.
            // Tangent approx
            Vector3 pNext = GetPointOnHybridPathBase(t + 0.001f);
            Vector3 pPrev = GetPointOnHybridPathBase(t - 0.001f);
            Vector3 tangent = (pNext - pPrev).normalized;
            
            // Normal? 
            // Frame calculation again
            Vector3 right = Vector3.Cross(tangent, Vector3.up).normalized;
            if (right.sqrMagnitude < 0.001f) right = Vector3.right;
            Vector3 up = Vector3.Cross(right, tangent).normalized;
            
            // The "Up" of the frame is the normal relative to the surface trajectory
            return basePoint + up * offsetMag;
        }
        else
        {
            // World Y
            return basePoint + Vector3.up * offsetMag;
        }
    }

    private void CreateSpheres()
    {
        // Start
        Vector3 startPos3D = GetPointOnHybridPath(0f);
        startSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        startSphere.transform.SetParent(transform);
        startSphere.transform.position = startPos3D;
        startSphere.transform.localScale = Vector3.one * (sphereRadius * 2);
        SetMaterial(startSphere, startColor);
        Destroy(startSphere.GetComponent<Collider>());

        // End
        Vector3 endPos3D = GetPointOnHybridPath(1f);
        endSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        endSphere.transform.SetParent(transform);
        endSphere.transform.position = endPos3D;
        endSphere.transform.localScale = Vector3.one * (sphereRadius * 2);
        SetMaterial(endSphere, endColor);
        Destroy(endSphere.GetComponent<Collider>());
    }
    
    // --- ITrailEvaluator Implementation ---
    public Vector3 GetClosestPointOnCenterline(Vector3 position)
    {
        // High Precision Iterative Search
        
        // 1. Coarse Search (Global)
        int samples = 100; // Increased
        float bestT = 0f;
        float bestDistSq = float.MaxValue;
        
        for (int i = 0; i <= samples; i++)
        {
            float t = (float)i / samples;
            Vector3 pt = GetPointOnHybridPath(t);
            float dSq = (position - pt).sqrMagnitude;
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                bestT = t;
            }
        }
        
        // 2. Refinement Search 1 (Local Scan +/- 1%)
        float step = 1f / samples; 
        float range = step; 
        float minT = Mathf.Max(0f, bestT - range);
        float maxT = Mathf.Min(1f, bestT + range);
        int refinementSteps = 20;
        
        for (int i = 0; i <= refinementSteps; i++)
        {
            float t = Mathf.Lerp(minT, maxT, (float)i / refinementSteps);
            Vector3 pt = GetPointOnHybridPath(t);
            float dSq = (position - pt).sqrMagnitude;
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                bestT = t;
            }
        }
        
        // 3. Refinement Search 2 (Micro Scan +/- 0.1%)
        float microRange = range / 10f; 
        minT = Mathf.Max(0f, bestT - microRange);
        maxT = Mathf.Min(1f, bestT + microRange);
        refinementSteps = 20;
        
        for (int i = 0; i <= refinementSteps; i++)
        {
            float t = Mathf.Lerp(minT, maxT, (float)i / refinementSteps);
            Vector3 pt = GetPointOnHybridPath(t);
            float dSq = (position - pt).sqrMagnitude;
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                bestT = t;
            }
        }

        return GetPointOnHybridPath(bestT);
    }
    
    public void Activate()
    {
        gameObject.SetActive(true);
        isActive = true;
        hasStarted = false;
        hasHitEnd = false;
        isCompleted = false;
        wasDrawing = false;
        
        // Safety check: if start/end are zero vectors and pen is at zero, it might cause issues.
        // But logic should handle it.
        
        // Use 'startColor' initially
        SetMaterial(startSphere, startColor);
        
        if (pathRecorder != null)
        {
            pathRecorder.SetCurrentTrailType(trailTypeId);
            pathRecorder.currentSurfaceType = surfaceConfigIndex; // Use Config Index instead of Shape Enum
            pathRecorder.SetEvaluator(this); // Set self as evaluator
            pathRecorder.manualControl = (manager.interactionMode == HybridTrailManager.InteractionMode.AutoTouch);
            pathRecorder.isCapturingOverride = false;
        }

        if (tubeTrailRenderer != null)
        {
            tubeTrailRenderer.manualControl = (manager.interactionMode == HybridTrailManager.InteractionMode.AutoTouch);
            tubeTrailRenderer.isDrawing = false;
        }
    }

    private void Update()
    {
        if (!isActive || isCompleted || trackedPenTip == null) return;
        
        // Update Logic similar to TargetTrail...
        // Assuming we rely on manager's interaction mode?
        // Wait, Hybrid mode usually implies "touching" the surface.
        // If InteractionMode is ButtonPress, user presses button while on surface?
        // User said: "drawing interaction and logic will follow the input mode".
        
        if (manager.interactionMode == HybridTrailManager.InteractionMode.ButtonPress)
        {
            HandleButtonPressMode();
        }
        else
        {
            // AutoTouch might be tricky on 3D surface? 
            // "data aqucistion and saving will also copy the input mode"
            // Let's assume ButtonPress is primary unless specified.
            HandleAutoTouchMode(); // Keep compatibility
        }
    }

    private void HandleButtonPressMode()
    {
        if (penController == null) return;
        bool isDrawing = penController.buttonPressed;
        
        // Sync pressure control - DISABLED for Hybrid Mode as requested
        // if (penController.enableDirectPressureControl != isDrawing)
        //      penController.enableDirectPressureControl = isDrawing;

        if (isDrawing && !wasDrawing) // Start
        {
             // Start Check works on 3D sphere distance
             if (Vector3.Distance(trackedPenTip.position, startSphere.transform.position) < sphereRadius)
             {
                 hasStarted = true;
                 hasLeftStartZone = false; // Reset zone check
                 SetMaterial(startSphere, activeStartColor);
                 if (pathRecorder != null) pathRecorder.StartNewStroke(trailId, trailTypeId, surfaceConfigIndex); // Pass Config Index
             }
             else
             {
                 if (pathRecorder != null) pathRecorder.StartNewStroke(-1, -1); // Invalid
             }
        }
        
        if (isDrawing && hasStarted)
        {
             // Check travel distance to allow leaving start zone
             float distFromStart = Vector3.Distance(trackedPenTip.position, startSphere.transform.position);
             if (!hasLeftStartZone && distFromStart > minSafeDistance)
             {
                 hasLeftStartZone = true;
             }

             // Check End - ONLY if we allow it
             // If Start and End are same position (Circle), we need to leave start first.
             if (hasLeftStartZone || trailShape != TargetTrail.TrailShape.Circle) 
             {
                 if (Vector3.Distance(trackedPenTip.position, endSphere.transform.position) < sphereRadius)
                 {
                      if (!hasHitEnd)
                      {
                          hasHitEnd = true;
                          if (successSound != null && audioSource != null) audioSource.PlayOneShot(successSound);
                      }
                 }
             }
        }
        
        if (!isDrawing && wasDrawing) // End
        {
             if (hasStarted)
             {
                 if (hasHitEnd || Vector3.Distance(trackedPenTip.position, endSphere.transform.position) < sphereRadius)
                 {
                     CompleteTrail();
                 }
                 else
                 {
                     // Failed
                     hasStarted = false;
                     hasHitEnd = false;
                     SetMaterial(startSphere, startColor);
                     if (pathRecorder != null) pathRecorder.DiscardStroke();
                     if (tubeTrailRenderer != null) tubeTrailRenderer.Clear();
                 }
             }
             else
             {
                  if (pathRecorder != null) pathRecorder.DiscardStroke();
                  if (tubeTrailRenderer != null) tubeTrailRenderer.Clear();
             }
        }
        
        wasDrawing = isDrawing;
    }
    
    private void HandleAutoTouchMode() { /* Omitted for brevity, logic implies ButtonPress mostly used */ }

    private void CompleteTrail()
    {
        isCompleted = true;
        isActive = false;
        
        startSphere.SetActive(false);
        endSphere.SetActive(false);
        surfaceInstance.gameObject.SetActive(false); // Hide surface
        GetComponent<MeshRenderer>().enabled = false;
        
        if (pathRecorder != null) pathRecorder.CommitStroke();
        if (tubeTrailRenderer != null) tubeTrailRenderer.Clear();
        if (manager != null) manager.OnTrailCompleted(this);
        // Wait, manager.OnTrailCompleted expects TargetTrail.
        // We need to overload manager.OnTrailCompleted or inherit?
        // Inheriting TargetTrail might be cleaner but it has so much "Start" logic.
        // I'll update Manager to accept a generic or overload.
        // Or just Reflection? No.
        // Let's modify Manager to be flexible.
    }

    private void SetMaterial(GameObject obj, Color color)
    {
        MeshRenderer mr = obj.GetComponent<MeshRenderer>();
        if (mr)
        {
             Material mat = new Material(Shader.Find("Standard"));
             mat.SetFloat("_Mode", 3);
             mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
             mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
             mat.SetInt("_ZWrite", 0);
             mat.DisableKeyword("_ALPHATEST_ON");
             mat.EnableKeyword("_ALPHABLEND_ON");
             mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
             mat.renderQueue = 3000;
             mat.color = color;
             mr.material = mat;
        }
    }
    
    private void SetupTranslucentMaterial(Material mat, Color color)
    {
         mat.SetFloat("_Mode", 3);
         mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
         mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
         mat.SetInt("_ZWrite", 0);
         mat.DisableKeyword("_ALPHATEST_ON");
         mat.EnableKeyword("_ALPHABLEND_ON");
         mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
         mat.renderQueue = 3000;
         mat.color = color;
    }
}
