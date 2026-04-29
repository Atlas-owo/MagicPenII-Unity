using System;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class DynamicSineTrail : MonoBehaviour, ITrailEvaluator
{
    [Header("Configuration")]
    public float tubeRadius = 0.003f;
    public float sphereRadius = 0.006f;
    public int radialSegments = 16;
    
    [Header("Dynamic Sine Wave Properties")]
    public float amplitudeStart = 0.05f;
    public float amplitudeEnd = 0.1f;
    public float frequencyStart = 1.0f;
    public float frequencyEnd = 3.0f;

    [Header("Visuals")]
    public bool enableRibbon = true;
    public float planeWidth = 0.2f;
    public Color planeColor = new Color(0.5f, 0.5f, 0.5f, 0.2f);
    public Color trailColor = new Color(0, 0, 0, 0.5f);
    public Color startColor = new Color(1, 1, 0, 0.5f);
    public Color activeStartColor = new Color(0, 1, 0, 0.5f);
    public Color endColor = new Color(1, 0, 0, 0.5f);
    
    public AudioClip successSound;
    private AudioSource audioSource;

    [Header("Control Override")]
    public bool overridePenControlMode = true;

    [Header("State")]
    public bool isActive = false;
    public bool isCompleted = false;
    
    private Vector3 startPoint;
    private Vector3 endPoint;
    private GameObject startSphere;
    private GameObject endSphere;
    private GameObject visualPlane;
    private Transform trackedPenTip;
    private DynamicSineTrailManager manager;
    private TubeTrailRenderer tubeTrailRenderer;
    private PathRecorder pathRecorder;
    private HapticPenController penController;
    private int trailId;
    
    private bool hasStarted = false;
    private bool wasDrawing = false;
    private bool hasHitEnd = false;

    public void Initialize(Vector3 start, Vector3 end, DynamicSineTrailManager mgr, Transform penTipOverride, int id)
    {
        startPoint = start;
        endPoint = end;
        manager = mgr;
        trailId = id;
        
        if (enableRibbon) GeneratePlaneMesh();
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
        
        if (startSphere) startSphere.SetActive(true);
        if (endSphere) endSphere.SetActive(true);
        if (visualPlane) visualPlane.SetActive(true);
        GetComponent<MeshRenderer>().enabled = true;

        SetMaterial(startSphere, startColor);

        if (penController == null) penController = FindObjectOfType<HapticPenController>();

        if (pathRecorder != null)
        {
            pathRecorder.SetCurrentTrailType(0); // Arbitrary integer identifier
            pathRecorder.SetEvaluator(this);
            pathRecorder.manualControl = (manager.interactionMode == DynamicSineTrailManager.InteractionMode.AutoTouch);
            pathRecorder.isCapturingOverride = false;
        }

        if (tubeTrailRenderer != null)
        {
            tubeTrailRenderer.manualControl = (manager.interactionMode == DynamicSineTrailManager.InteractionMode.AutoTouch);
            tubeTrailRenderer.isDrawing = false;
        }
    }

    private void Update()
    {
        if (!isActive || isCompleted || trackedPenTip == null || penController == null) return;

        if (manager.interactionMode == DynamicSineTrailManager.InteractionMode.ButtonPress)
            HandleButtonPressMode();
        else
            HandleAutoTouchMode();
    }

    private void HandleButtonPressMode()
    {
        bool isDrawing = penController.buttonPressed;
        if (overridePenControlMode && penController.enableDirectPressureControl != isDrawing)
            penController.enableDirectPressureControl = isDrawing;

        if (isDrawing && !wasDrawing)
        {
            if (Vector3.Distance(trackedPenTip.position, startPoint) < sphereRadius)
            {
                hasStarted = true;
                SetMaterial(startSphere, activeStartColor);
                if (pathRecorder != null) pathRecorder.StartNewStroke(trailId, 0);
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
                if (pathRecorder != null)
                {
                    pathRecorder.isCapturingOverride = true;
                    pathRecorder.StartNewStroke(trailId, 0);
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
        SetMaterial(startSphere, startColor);
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

    private void GeneratePlaneMesh()
    {
        visualPlane = new GameObject("VisualPlane");
        visualPlane.transform.SetParent(transform);
        visualPlane.transform.localPosition = Vector3.zero;
        visualPlane.transform.localRotation = Quaternion.identity;

        MeshFilter mf = visualPlane.AddComponent<MeshFilter>();
        MeshRenderer mr = visualPlane.AddComponent<MeshRenderer>();
        MeshCollider mc = visualPlane.AddComponent<MeshCollider>();
        
        // Try setting the layer to "surface" or "Surface"
        int layerIdx = LayerMask.NameToLayer("surface");
        if (layerIdx == -1) layerIdx = LayerMask.NameToLayer("Surface");
        if (layerIdx != -1) visualPlane.layer = layerIdx;

        Vector3 baselineDir = (endPoint - startPoint).normalized;
        // Direction perpendicular to the path in the horizontal (X-Z) plane
        Vector3 planeRight = Vector3.Cross(Vector3.up, baselineDir).normalized;
        if (planeRight.sqrMagnitude < 0.001f) planeRight = Vector3.right;

        Vector3 halfWidthOffset = planeRight * (planeWidth * 0.5f);

        // Generate a continuous ribbon that follows the sine wave's height
        float dist = Vector3.Distance(startPoint, endPoint);
        int segments = Mathf.CeilToInt(dist * 200); // High res for smooth curves
        if (segments < 10) segments = 10;

        Vector3[] vertices = new Vector3[(segments + 1) * 2];
        int[] triangles = new int[segments * 12]; // 2 triangles per face * 3 vertices * 2 for double-sided

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            // GetPointOnPath naturally includes the sine wave's Y-axis height at this specific t
            Vector3 centerPos = GetPointOnPath(t);

            // Extend left and right horizontally from the curving center point
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

            // Front face
            triangles[triIndex++] = botLeft;
            triangles[triIndex++] = topLeft;
            triangles[triIndex++] = botRight;

            triangles[triIndex++] = botRight;
            triangles[triIndex++] = topLeft;
            triangles[triIndex++] = topRight;

            // Back face
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
        SetupTranslucentMaterial(mat, planeColor);
        mr.material = mat;
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
        int curveSegments = Mathf.CeilToInt(dist * 400); // High res for changing frequencies
        if (curveSegments < 10) curveSegments = 10;

        int vertCount = curveSegments * radialSegments;
        Vector3[] vertices = new Vector3[vertCount];
        int[] triangles = new int[(curveSegments - 1) * radialSegments * 6];

        Vector3 baselineDir = (endPoint - startPoint).normalized;
        Vector3 planeNormal = Vector3.Cross(baselineDir, Vector3.up).normalized;
        if (planeNormal.sqrMagnitude < 0.001f) planeNormal = Vector3.right;

        for (int i = 0; i < curveSegments; i++)
        {
            float t = (float)i / (curveSegments - 1);
            Vector3 currentPos = GetPointOnPath(t);

            Vector3 tangent;
            if (i < curveSegments - 1) tangent = (GetPointOnPath(t + 0.001f) - currentPos).normalized;
            else tangent = (currentPos - GetPointOnPath(t - 0.001f)).normalized;

            Vector3 frameRight = planeNormal;
            Vector3 frameUp = Vector3.Cross(tangent, frameRight).normalized;
            frameRight = Vector3.Cross(frameUp, tangent).normalized;

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

    private Vector3 GetPointOnPath(float t)
    {
        Vector3 straightPos = Vector3.Lerp(startPoint, endPoint, t);
        Vector3 baselineDir = (endPoint - startPoint).normalized;
        
        // Ensure sine wave oscillates vertically (Y-axis)
        Vector3 waveOffsetDir = Vector3.up;

        // Mathematical integration of f(t) over interval [0, t]:
        // Integral(f_start + (f_end - f_start)*u)du = f_start*t + 0.5*(f_end - f_start)*t^2
        float phase = 2f * Mathf.PI * (frequencyStart * t + 0.5f * (frequencyEnd - frequencyStart) * t * t);
        
        float currentAmplitude = Mathf.Lerp(amplitudeStart, amplitudeEnd, t);
        float sineValue = Mathf.Sin(phase);

        return straightPos + waveOffsetDir * (currentAmplitude * sineValue);
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
