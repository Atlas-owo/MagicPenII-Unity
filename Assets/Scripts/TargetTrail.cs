using System;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class TargetTrail : MonoBehaviour
{
    [Header("Configuration")]
    public float tubeRadius = 0.01f;
    public float sphereRadius = 0.01f;
    public int radialSegments = 16;
    
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
    private Transform trackedPenTip; // The transform we are tracking
    private TargetTrailManager manager;
    private TubeTrailRenderer tubeTrailRenderer;
    private PathRecorder pathRecorder;
    private HapticPenController penController; // Need direct access for button state
    private int trailId;
    
    // Interaction state
    private bool hasStarted = false;
    private bool wasDrawing = false;

    public void Initialize(Vector3 start, Vector3 end, TargetTrailManager mgr, Transform penTipOverride, int id)
    {
        startPoint = start;
        endPoint = end;
        manager = mgr;
        trailId = id;
        
        GenerateMesh();
        CreateSpheres();
        
        // Setup Audio
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        
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
        
        // Initially hidden until activated by manager? 
        // Or manager instantiates it when needed. 
        // Let's assume Manager activates it.
        gameObject.SetActive(false);
    }

    public void Activate()
    {
        gameObject.SetActive(true);
        isActive = true;
        hasStarted = false;
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
    }

    private void Update()
    {
        if (!isActive || isCompleted || trackedPenTip == null || penController == null) return;

        bool isDrawing = !penController.buttonPressed;

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
                    pathRecorder.StartNewStroke(trailId);
                }
                Debug.Log("TargetTrail: Valid Stroke Started!");
            }
            else
            {
                hasStarted = false;
                // Start Invalid Stroke (will be discarded)
                // We still let PathRecorder buffer it, but we won't commit it.
                if (pathRecorder != null)
                {
                    pathRecorder.StartNewStroke(-1); // Invalid ID or just ignore
                }
                Debug.Log("TargetTrail: Invalid Stroke Started (Outside Start)");
            }
        }

        // 2. While Drawing
        if (isDrawing)
        {
            if (hasStarted)
            {
                // Check if we hit End Point
                if (Vector3.Distance(trackedPenTip.position, endPoint) < sphereRadius)
                {
                    // Success!
                    CompleteTrail();
                    return; // Exit immediately
                }
            }
        }

        // 3. Detect End of Stroke (Falling Edge)
        if (!isDrawing && wasDrawing)
        {
            // User lifted pen
            if (hasStarted)
            {
                // Started valid, but lifted before End -> Fail
                Debug.Log("TargetTrail: Stroke Failed (Lifted before End)");
                ResetTrail();
            }
            else
            {
                // Started invalid -> Discard
                Debug.Log("TargetTrail: Invalid Stroke Discarded");
            }

            // Always discard buffer on pen lift (unless we already committed in CompleteTrail)
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

        wasDrawing = isDrawing;
    }

    private void ResetTrail()
    {
        hasStarted = false;
        // Reset visual feedback
        SetMaterial(startSphere, startColor);
    }

    private void CompleteTrail()
    {
        isCompleted = true;
        isActive = false;
        
        Debug.Log("TargetTrail: Completed!");

        // Play Sound
        if (successSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(successSound);
        }
        
        // Disappear
        if (startSphere) startSphere.SetActive(false);
        if (endSphere) endSphere.SetActive(false);
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

        // Generate straight tube
        Vector3[] points = new Vector3[] { startPoint, endPoint };
        
        int vertCount = points.Length * radialSegments;
        Vector3[] vertices = new Vector3[vertCount];
        int[] triangles = new int[(points.Length - 1) * radialSegments * 6];

        Vector3 forward = (endPoint - startPoint).normalized;
        Vector3 up = Vector3.up;
        if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.99f) up = Vector3.right;
        Vector3 right = Vector3.Cross(forward, up).normalized;
        up = Vector3.Cross(right, forward).normalized;

        for (int i = 0; i < points.Length; i++)
        {
            for (int j = 0; j < radialSegments; j++)
            {
                float angle = j * Mathf.PI * 2f / radialSegments;
                float sin = Mathf.Sin(angle);
                float cos = Mathf.Cos(angle);

                Vector3 offset = (right * cos + up * sin) * tubeRadius;
                // Convert to local space
                vertices[i * radialSegments + j] = transform.InverseTransformPoint(points[i] + offset);
            }
        }

        int triIndex = 0;
        for (int i = 0; i < points.Length - 1; i++)
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
}
