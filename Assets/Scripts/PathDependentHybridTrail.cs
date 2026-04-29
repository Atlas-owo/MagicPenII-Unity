using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class PathDependentHybridTrail : MonoBehaviour, ITrailEvaluator
{
    public enum TrailShape { Straight, SineWave, Nurbs, VerticalLift }

    [Header("Configuration")]
    public TrailShape trailShape = TrailShape.Straight;
    public float tubeRadius = 0.003f;
    public float sphereRadius = 0.006f;
    public int radialSegments = 16;
    
    [Header("Sine Wave Properties")]
    public float amplitudeStart = 0.05f;
    public float amplitudeEnd = 0.1f;
    public float frequencyStart = 1.0f;
    public float frequencyEnd = 3.0f;

    [Header("Nurbs Properties")]
    public float nurbsPlateauWidth = 0.3f;
    public float nurbsTransitionLength = 0.05f;
    public float nurbsTransitionSteepness = 5.0f;
    public float nurbsAmplitude = 0.05f;

    [Header("Vertical Lift Properties")]
    public float liftSpeed = 0.05f;
    public float liftToleranceRadius = 0.02f;
    public float liftFloorSize = 0.5f;
    public bool showLiftHelpers = true;

    [Header("Visuals")]
    public bool enableRibbon = true;
    public float planeWidth = 0.2f;
    public float paddingLength = 0.15f; 
    public Vector3 ribbonExpansionAxis = Vector3.forward;
    public Vector3 paddingExpansionAxis = Vector3.right;
    public Color planeColor = new Color(0.5f, 0.5f, 0.5f, 0.2f);
    public Color trailColor = new Color(0, 0, 0, 0.5f);
    public Color startColor = new Color(0, 1, 0, 0.5f);
    public Color activeStartColor = new Color(0, 1, 0, 0.5f);
    public Color endColor = new Color(1, 0, 0, 0.5f);

    [Header("Haptic Mismatch")]
    public float hapticSurfaceOffsetX = 0.0f;
    public bool showHapticSurface = false;

    public AudioClip successSound;
    private AudioSource audioSource;

    [Header("State")]
    public bool isActive = false;
    public bool isCompleted = false;
    
    // Internal refs
    private Vector3 startPoint;
    private Vector3 endPoint;
    private GameObject startSphere;
    private GameObject endSphere;
    private GameObject visualPlane;
    private GameObject hapticPlane;
    private Transform trackedPenTip;
    private PathDependentHybridManager manager;
    private TubeTrailRenderer tubeTrailRenderer;
    private PathRecorder pathRecorder;
    private HapticPenController penController;
    private int trailId;
    private int trailTypeId;
    
    private bool hasStarted = false;
    private bool wasDrawing = false;
    private bool hasHitEnd = false;
    private bool isHoveringStartSphere = false;

    // Lift state
    private GameObject liftFloor;
    private GameObject liftDisk;
    private bool isLifting = false;

    public void Initialize(Vector3 start, Vector3 end, PathDependentHybridManager mgr, Transform penTipOverride, int id, int typeId)
    {
        startPoint = start;
        endPoint = end;
        manager = mgr;
        trailId = id;
        trailTypeId = typeId;
        
        if (trailShape == TrailShape.VerticalLift) 
        {
            CreateLiftHelpers();
            if (enableRibbon) GeneratePlaneMesh(false);
        }
        else 
        {
            if (enableRibbon) GeneratePlaneMesh(false);
            GeneratePlaneMesh(true);
        }
        GenerateTubeMesh();
        CreateSpheres();
        
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        if (successSound != null) audioSource.clip = successSound;

        if (penTipOverride != null) trackedPenTip = penTipOverride;
        else
        {
            penController = FindObjectOfType<HapticPenController>();
            if (penController != null) trackedPenTip = penController.penTip;
        }

        tubeTrailRenderer = FindObjectOfType<TubeTrailRenderer>();
        if (tubeTrailRenderer != null) tubeTrailRenderer.clearOnStrokeEnd = true;

        pathRecorder = FindObjectOfType<PathRecorder>();
        if (pathRecorder != null) pathRecorder.autoSave = false;
        
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
        isHoveringStartSphere = false;

        if (trailShape == TrailShape.VerticalLift)
        {
            if (liftFloor) liftFloor.SetActive(true);
            if (liftDisk) liftDisk.SetActive(false);
            isLifting = false;
        }
        
        if (startSphere) startSphere.SetActive(true);
        if (endSphere) endSphere.SetActive(true);
        if (visualPlane) visualPlane.SetActive(true);
        if (hapticPlane) hapticPlane.SetActive(true);
        GetComponent<MeshRenderer>().enabled = true;

        SetMaterial(startSphere, startColor);

        if (penController == null) penController = FindObjectOfType<HapticPenController>();

        if (pathRecorder != null)
        {
            pathRecorder.SetCurrentTrailType(trailTypeId);
            pathRecorder.SetEvaluator(this);
            pathRecorder.manualControl = (manager.interactionMode == PathDependentHybridManager.InteractionMode.AutoTouch);
            pathRecorder.isCapturingOverride = false;
        }

        if (tubeTrailRenderer != null)
        {
            tubeTrailRenderer.manualControl = (manager.interactionMode == PathDependentHybridManager.InteractionMode.AutoTouch);
            tubeTrailRenderer.isDrawing = false;
        }
    }

    private void Update()
    {
        if (!isActive || isCompleted || trackedPenTip == null || penController == null) return;

        bool isDrawing = manager.interactionMode == PathDependentHybridManager.InteractionMode.AutoTouch || penController.buttonCPressed;

        if (!hasStarted)
        {
            float dist = Vector3.Distance(trackedPenTip.position, startPoint);
            bool currentlyInside = dist < sphereRadius;

            if (currentlyInside && !isHoveringStartSphere)
            {
                isHoveringStartSphere = true;
                SetMaterial(startSphere, activeStartColor);
                if (successSound != null && audioSource != null) audioSource.PlayOneShot(successSound);
            }
            else if (!currentlyInside && isHoveringStartSphere)
            {
                isHoveringStartSphere = false;
                SetMaterial(startSphere, startColor);
            }
        }

        CheckVerticalLiftLogic(isDrawing);

        if (manager.interactionMode == PathDependentHybridManager.InteractionMode.ButtonPress)
            HandleButtonPressMode();
        else
            HandleAutoTouchMode();
    }

    private void HandleButtonPressMode()
    {
        bool isDrawing = penController.buttonCPressed;

        if (isDrawing && !wasDrawing)
        {
            if (Vector3.Distance(trackedPenTip.position, startPoint) < sphereRadius)
            {
                hasStarted = true;
                SetMaterial(startSphere, activeStartColor);
                if (trailShape == TrailShape.VerticalLift)
                {
                    isLifting = true;
                    if (liftFloor) liftFloor.SetActive(false);
                    if (liftDisk)
                    {
                        liftDisk.SetActive(true);
                        liftDisk.transform.position = startPoint;
                    }
                }
                if (pathRecorder != null) pathRecorder.StartNewStroke(trailId, trailTypeId);
            }
            else
            {
                hasStarted = false;
                if (pathRecorder != null) pathRecorder.StartNewStroke(-1, -1); 
            }
        }

        if (isDrawing && hasStarted)
        {
            if (Vector3.Distance(trackedPenTip.position, endPoint) < sphereRadius)
            {
                if (!hasHitEnd)
                {
                    hasHitEnd = true;
                    if (successSound != null && audioSource != null) audioSource.PlayOneShot(successSound);
                }
            }
        }

        if (!isDrawing && wasDrawing)
        {
            if (hasStarted)
            {
                if (hasHitEnd || Vector3.Distance(trackedPenTip.position, endPoint) < sphereRadius)
                {
                    if (!hasHitEnd && successSound != null && audioSource != null) audioSource.PlayOneShot(successSound);
                    CompleteTrail();
                }
                else
                {
                    ResetTrail();
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

    private void HandleAutoTouchMode()
    {
        if (!hasStarted)
        {
            if (Vector3.Distance(trackedPenTip.position, startPoint) < sphereRadius)
            {
                hasStarted = true;
                SetMaterial(startSphere, activeStartColor);
                if (trailShape == TrailShape.VerticalLift)
                {
                    isLifting = true;
                    if (liftFloor) liftFloor.SetActive(false);
                    if (liftDisk)
                    {
                        liftDisk.SetActive(true);
                        liftDisk.transform.position = startPoint;
                    }
                }
                if (pathRecorder != null)
                {
                    pathRecorder.isCapturingOverride = true;
                    pathRecorder.StartNewStroke(trailId, trailTypeId);
                }
                if (tubeTrailRenderer != null) tubeTrailRenderer.isDrawing = true;
            }
        }
        else
        {
            if (Vector3.Distance(trackedPenTip.position, endPoint) < sphereRadius)
            {
                if (successSound != null && audioSource != null) audioSource.PlayOneShot(successSound);
                CompleteTrail();
            }
        }
    }

    private void ResetTrail()
    {
        hasStarted = false;
        hasHitEnd = false;
        isHoveringStartSphere = false;
        SetMaterial(startSphere, startColor);

        if (trailShape == TrailShape.VerticalLift)
        {
            isLifting = false;
            if (liftFloor) liftFloor.SetActive(true);
            if (liftDisk)
            {
                liftDisk.SetActive(false);
                liftDisk.transform.position = startPoint;
            }
        }
    }

    private void CompleteTrail()
    {
        isCompleted = true;
        isActive = false;
        
        if (pathRecorder != null) pathRecorder.isCapturingOverride = false;
        if (tubeTrailRenderer != null) tubeTrailRenderer.isDrawing = false;

        if (startSphere) startSphere.SetActive(false);
        if (endSphere) endSphere.SetActive(false);
        if (visualPlane) visualPlane.SetActive(false);
        if (hapticPlane) hapticPlane.SetActive(false);
        if (liftFloor) liftFloor.SetActive(false);
        if (liftDisk) liftDisk.SetActive(false);
        GetComponent<MeshRenderer>().enabled = false;

        if (pathRecorder != null) pathRecorder.CommitStroke();
        if (tubeTrailRenderer != null) tubeTrailRenderer.Clear();
        if (manager != null) manager.OnTrailCompleted(this);
    }

    private void CreateSpheres()
    {
        startSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        startSphere.name = "StartSphere";
        startSphere.transform.SetParent(transform);
        startSphere.transform.position = startPoint;
        startSphere.transform.localScale = Vector3.one * (sphereRadius * 2);
        SetMaterial(startSphere, startColor);
        Destroy(startSphere.GetComponent<Collider>());

        endSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        endSphere.name = "EndSphere";
        endSphere.transform.SetParent(transform);
        endSphere.transform.position = endPoint;
        endSphere.transform.localScale = Vector3.one * (sphereRadius * 2);
        SetMaterial(endSphere, endColor);
        Destroy(endSphere.GetComponent<Collider>());
    }

    private void CreateLiftHelpers()
    {
        liftFloor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        liftFloor.name = "LiftFloor";
        liftFloor.transform.SetParent(transform);
        liftFloor.transform.position = startPoint;
        liftFloor.transform.localScale = Vector3.one * (liftFloorSize / 10f);
        
        int surfaceLayer = LayerMask.NameToLayer("Surface");
        if (surfaceLayer == -1) surfaceLayer = LayerMask.NameToLayer("surface");
        if (surfaceLayer != -1) liftFloor.layer = surfaceLayer;

        if (showLiftHelpers) SetMaterial(liftFloor, new Color(0, 1, 0, 0.3f));
        else liftFloor.GetComponent<MeshRenderer>().enabled = false;

        liftDisk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        liftDisk.name = "LiftDisk";
        liftDisk.transform.SetParent(transform);
        liftDisk.transform.position = startPoint;
        liftDisk.transform.localScale = new Vector3(liftToleranceRadius * 2f, 0.001f, liftToleranceRadius * 2f);
        
        // Swap default bouncy CapsuleCollider for a perfectly flat MeshCollider
        Destroy(liftDisk.GetComponent<Collider>());
        MeshCollider diskMC = liftDisk.AddComponent<MeshCollider>();
        diskMC.sharedMesh = liftDisk.GetComponent<MeshFilter>().sharedMesh;

        if (surfaceLayer != -1) liftDisk.layer = surfaceLayer;

        Vector3 dir = (endPoint - startPoint).normalized;
        if (dir.sqrMagnitude > 0.001f) liftDisk.transform.up = dir;

        if (showLiftHelpers) SetMaterial(liftDisk, new Color(1, 0.5f, 0, 0.8f));
        else liftDisk.GetComponent<MeshRenderer>().enabled = false;

        liftFloor.SetActive(false);
        liftDisk.SetActive(false);
    }

    private void CheckVerticalLiftLogic(bool isDrawing)
    {
        if (trailShape != TrailShape.VerticalLift || !isLifting || !hasStarted || !isDrawing) return;

        Vector3 lineDir = (endPoint - startPoint).normalized;
        Vector3 penVec = trackedPenTip.position - startPoint;
        Vector3 proj = Vector3.Project(penVec, lineDir);
        float distToLine = (penVec - proj).magnitude;
        
        if (distToLine > liftToleranceRadius)
        {
            ResetTrail();
            if (pathRecorder != null) pathRecorder.DiscardStroke();
            if (tubeTrailRenderer != null) tubeTrailRenderer.Clear();
        }
        else
        {
            if (liftDisk != null)
            {
                liftDisk.transform.position = Vector3.MoveTowards(liftDisk.transform.position, endPoint, liftSpeed * Time.deltaTime);
            }
        }
    }

    // --- Mathematical Definition of the Target Path ---
    public Vector3 GetPointOnPath(float t)
    {
        if (t >= 0f && t <= 1f)
        {
            return GetPointOnPathCore(t);
        }

        // Padding Zone Logic
        Vector3 baselineDir = endPoint - startPoint;
        float totalLength = baselineDir.magnitude;
        
        Vector3 horizontalDir = paddingExpansionAxis.normalized;
        if (horizontalDir.sqrMagnitude < 0.001f) horizontalDir = Vector3.right;

        if (t < 0f)
        {
            float extDistance = Mathf.Abs(t) * totalLength;
            Vector3 center0 = GetPointOnPathCore(0f);
            return center0 - horizontalDir * extDistance;
        }
        else // t > 1f
        {
            float extDistance = (t - 1f) * totalLength;
            Vector3 center1 = GetPointOnPathCore(1f);
            return center1 + horizontalDir * extDistance;
        }
    }

    private Vector3 GetPointOnPathCore(float t)
    {
        Vector3 straightPos = Vector3.Lerp(startPoint, endPoint, t);
        float heightOffset = GetPathHeightOffset(t);
        return straightPos + Vector3.up * heightOffset;
    }

    private float GetPathHeightOffset(float t)
    {
        if (trailShape == TrailShape.Straight)
        {
            return 0f;
        }
        else if (trailShape == TrailShape.SineWave)
        {
            float phase = 2f * Mathf.PI * (frequencyStart * t + 0.5f * (frequencyEnd - frequencyStart) * t * t);
            float currentAmplitude = Mathf.Lerp(amplitudeStart, amplitudeEnd, t);
            return Mathf.Sin(phase) * currentAmplitude;
        }
        else if (trailShape == TrailShape.Nurbs)
        {
            float tCentered = t - 0.5f;
            float distFromCenter = Mathf.Abs(tCentered);
            float plateauHalfWidth = nurbsPlateauWidth * 0.5f;
            float transitionEnd = plateauHalfWidth + nurbsTransitionLength;
            float heightValue;

            if (distFromCenter <= plateauHalfWidth)
                heightValue = 1.0f;
            else if (distFromCenter < transitionEnd)
            {
                float p = (distFromCenter - plateauHalfWidth) / nurbsTransitionLength;
                heightValue = GeneralizedSmoothstep(1.0f - p, nurbsTransitionSteepness);
            }
            else
                heightValue = 0.0f;

            return nurbsAmplitude * heightValue;
        }
        return 0f;
    }

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

    // --- Mesh Generation ---
    private void GeneratePlaneMesh(bool isHaptic)
    {
        GameObject targetPlane = new GameObject(isHaptic ? "HapticPlane" : "VisualPlane");
        targetPlane.transform.SetParent(transform);
        targetPlane.transform.localPosition = Vector3.zero;
        targetPlane.transform.localRotation = Quaternion.identity;

        MeshFilter mf = targetPlane.AddComponent<MeshFilter>();
        MeshRenderer mr = targetPlane.AddComponent<MeshRenderer>();
        MeshCollider mc = targetPlane.AddComponent<MeshCollider>();
        
        int layerIdx = LayerMask.NameToLayer("Default");
        if (isHaptic)
        {
            layerIdx = LayerMask.NameToLayer("Surface");
            if (layerIdx == -1) layerIdx = LayerMask.NameToLayer("surface");
        }
        if (layerIdx != -1) targetPlane.layer = layerIdx;

        Vector3 planeRight = ribbonExpansionAxis.normalized;
        if (planeRight.sqrMagnitude < 0.001f) planeRight = Vector3.forward;

        Vector3 halfWidthOffset = planeRight * (planeWidth * 0.5f);

        float distance = Vector3.Distance(startPoint, endPoint);
        float paddingRatio = distance > 0.001f ? (paddingLength / distance) : 0f;

        float tMin = -paddingRatio;
        float tMax = 1.0f + paddingRatio;

        int segments = Mathf.CeilToInt(distance * 200 * (1f + 2f * paddingRatio)); 
        if (segments < 10) segments = 10;

        Vector3[] vertices = new Vector3[(segments + 1) * 2];
        int[] triangles = new int[segments * 12]; 

        for (int i = 0; i <= segments; i++)
        {
            float pct = (float)i / segments;
            float t = Mathf.Lerp(tMin, tMax, pct);
            
            Vector3 centerPos = GetPointOnPath(t);

            if (isHaptic)
            {
                centerPos += Vector3.left * Mathf.Abs(hapticSurfaceOffsetX);
            }

            // Extend left and right horizontally from the path point
            vertices[i * 2] = transform.InverseTransformPoint(centerPos - halfWidthOffset);
            vertices[i * 2 + 1] = transform.InverseTransformPoint(centerPos + halfWidthOffset);
        }

        int triIndex = 0;
        for (int i = 0; i < segments; i++)
        {
            int botLeft = i * 2;
            int botRight = i * 2 + 1;
            int topLeft = (i + 1) * 2;
            int topRight = (i + 1) * 2 + 1;

            triangles[triIndex++] = botLeft;
            triangles[triIndex++] = topLeft;
            triangles[triIndex++] = botRight;

            triangles[triIndex++] = botRight;
            triangles[triIndex++] = topLeft;
            triangles[triIndex++] = topRight;

            triangles[triIndex++] = botLeft;
            triangles[triIndex++] = botRight;
            triangles[triIndex++] = topLeft;

            triangles[triIndex++] = botRight;
            triangles[triIndex++] = topRight;
            triangles[triIndex++] = topLeft;
        }

        Mesh mesh = new Mesh();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mf.mesh = mesh;
        mc.sharedMesh = mesh;

        Material mat = new Material(Shader.Find("Standard"));
        if (!isHaptic)
        {
            SetupTranslucentMaterial(mat, planeColor);
        }
        else
        {
            if (showHapticSurface)
            {
                SetupTranslucentMaterial(mat, new Color(0, 0, 1, 0.3f)); // Blue transparent offset mesh
            }
            else
            {
                mr.enabled = false;
            }
        }
        mr.material = mat;
        
        if (!isHaptic) visualPlane = targetPlane;
        else hapticPlane = targetPlane;
    }

    private void GenerateTubeMesh()
    {
        MeshFilter meshFilter = GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
        Mesh mesh = new Mesh();
        meshFilter.mesh = mesh;
        
        Material tubeMat = new Material(Shader.Find("Standard"));
        SetupTranslucentMaterial(tubeMat, trailColor);
        meshRenderer.material = tubeMat;

        float dist = Vector3.Distance(startPoint, endPoint);
        int curveSegments = Mathf.CeilToInt(dist * 400); 
        if (curveSegments < 10) curveSegments = 10;

        int vertCount = curveSegments * radialSegments;
        Vector3[] vertices = new Vector3[vertCount];
        int[] triangles = new int[(curveSegments - 1) * radialSegments * 6];

        Vector3 planeRight = ribbonExpansionAxis.normalized;
        if (planeRight.sqrMagnitude < 0.001f) planeRight = Vector3.forward;

        for (int i = 0; i < curveSegments; i++)
        {
            // Tube mesh ONLY spans from 0 to 1 (No padding inside the target tube)
            float t = (float)i / (curveSegments - 1);
            Vector3 currentPos = GetPointOnPath(t);

            Vector3 tangent;
            if (i < curveSegments - 1) tangent = (GetPointOnPath(t + 0.001f) - currentPos).normalized;
            else tangent = (currentPos - GetPointOnPath(t - 0.001f)).normalized;

            Vector3 frameUp = Vector3.Cross(tangent, planeRight).normalized;
            if (frameUp.sqrMagnitude < 0.001f) frameUp = Vector3.up;
            Vector3 frameRight = Vector3.Cross(frameUp, tangent).normalized;

            for (int j = 0; j < radialSegments; j++)
            {
                float angle = j * Mathf.PI * 2f / radialSegments;
                Vector3 offset = (frameRight * Mathf.Cos(angle) + frameUp * Mathf.Sin(angle)) * tubeRadius;
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
        mesh.RecalculateNormals();
    }

    private void SetMaterial(GameObject obj, Color color)
    {
        MeshRenderer mr = obj.GetComponent<MeshRenderer>();
        if (mr == null) return;
        Material mat = new Material(Shader.Find("Standard"));
        SetupTranslucentMaterial(mat, color);
        mr.material = mat;
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

    // --- ITrailEvaluator Implementation ---
    public Vector3 GetClosestPointOnCenterline(Vector3 position)
    {
        int samples = 100;
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
        
        float step = 1f / samples;
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
                bestT = t; 
            }
        }
        
        float microRange = range / 10f;
        minT = Mathf.Max(0f, bestT - microRange);
        maxT = Mathf.Min(1f, bestT + microRange);
        
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
}
