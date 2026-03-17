using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ExtrusionController : MonoBehaviour
{
    public enum InteractionState
    {
        Idle,
        Drawing,
        ShapeReady,
        Extruding
    }

    [Header("Configuration")]
    public float planeHeight = 0.0f;
    public float minPointDistance = 0.005f; // 5mm
    public KeyCode extrusionKey = KeyCode.Z;

    [Header("References")]
    public HapticPenController penController;
    public Material meshMaterial;
    public Material previewMaterial; // For the trail

    [Header("State")]
    public InteractionState currentState = InteractionState.Idle;
    
    // Internal
    private List<Vector3> currentPoints = new List<Vector3>();
    private GameObject currentShapeObject;
    private MeshFilter currentMeshFilter;
    private MeshRenderer currentMeshRenderer;
    private TubeTrailRenderer trailRenderer; // Option to reuse or use LineRenderer
    
    private float baseHeight = 0f;

    void Start()
    {
        if (penController == null)
            penController = FindObjectOfType<HapticPenController>();

        // Setup a simple trail renderer for the 2D drawing phase
        SetupTrailRenderer();
    }

    void SetupTrailRenderer()
    {
        trailRenderer = GetComponent<TubeTrailRenderer>();
        if (trailRenderer == null)
        {
            trailRenderer = gameObject.AddComponent<TubeTrailRenderer>();
            trailRenderer.radius = 0.002f;
            trailRenderer.color = Color.blue;
            trailRenderer.manualControl = true;
            trailRenderer.clearOnStrokeEnd = false;
        }
    }

    void Update()
    {
        if (penController == null) return;

        switch (currentState)
        {
            case InteractionState.Idle:
                HandleIdleState();
                break;
            case InteractionState.Drawing:
                HandleDrawingState();
                break;
            case InteractionState.ShapeReady:
                HandleShapeReadyState();
                break;
            case InteractionState.Extruding:
                HandleExtrudingState();
                break;
        }
    }

    void HandleIdleState()
    {
        // Transition to Drawing: Pen Button Pressed
        if (penController.buttonPressed)
        {
            StartDrawing();
        }
    }

    void HandleDrawingState()
    {
        // 1. Check for End
        if (!penController.buttonPressed)
        {
            FinishDrawing();
            return;
        }

        // 2. Add Points
        Vector3 rawPos = penController.penTip.position;
        // Project to plane
        Vector3 flatPos = new Vector3(rawPos.x, planeHeight, rawPos.z);

        if (currentPoints.Count == 0 || Vector3.Distance(currentPoints[currentPoints.Count - 1], flatPos) > minPointDistance)
        {
            currentPoints.Add(flatPos);
            
            // Visuals
            // We use the TubeTrailRenderer functionality manually
            // But TubeTrailRenderer expects a target object to follow in Update. 
            // Better to just manually construct a mesh or line for preview?
            // Actually, let's just use a LineRenderer for simplicity of 2D preview, 
            // or we can hack the TubeTrailRenderer.
            // Let's use LineRenderer for the "Drawing" phase, it's cheaper.
        }
        
        UpdateDrawingVisuals();
    }

    void HandleShapeReadyState()
    {
        // Transition to Extruding: Key Pressed
        if (Input.GetKeyDown(extrusionKey))
        {
            StartExtrusion();
        }
        
        // Cancel/Reset if button pressed again (New drawing)
        if (penController.buttonPressed)
        {
            // Discard old, start new
            CleanupCurrentShape();
            StartDrawing();
        }
    }

    void HandleExtrudingState()
    {
        // 1. Check for End
        if (Input.GetKeyUp(extrusionKey))
        {
            FinishExtrusion();
            return;
        }

        // 2. Update Height
        float currentY = penController.penTip.position.y;
        float height = currentY - planeHeight;

        // 3. Regenerate Mesh
        ProceduralExtrusionMesh.UpdateMesh(currentMeshFilter.mesh, currentPoints, height);
    }

    // --- Actions ---

    void StartDrawing()
    {
        currentState = InteractionState.Drawing;
        currentPoints.Clear();
        
        if (trailRenderer != null)
        {
            trailRenderer.Clear();
        }
        
        // Create a LineRenderer for simple preview if Tube is annoying
        LineRenderer lr = GetComponent<LineRenderer>();
        if (lr == null) lr = gameObject.AddComponent<LineRenderer>();
        lr.positionCount = 0;
        lr.startWidth = 0.005f;
        lr.endWidth = 0.005f;
        lr.material = previewMaterial != null ? previewMaterial : new Material(Shader.Find("Sprites/Default"));
        lr.enabled = true;
    }

    void UpdateDrawingVisuals()
    {
        LineRenderer lr = GetComponent<LineRenderer>();
        if (lr != null)
        {
            lr.positionCount = currentPoints.Count;
            lr.SetPositions(currentPoints.ToArray());
        }
    }

    void FinishDrawing()
    {
        if (currentPoints.Count < 3)
        {
            // Too small, invalid
            currentState = InteractionState.Idle;
            currentPoints.Clear();
            return;
        }

        // Close Loop
        currentPoints.Add(currentPoints[0]);
        UpdateDrawingVisuals();

        currentState = InteractionState.ShapeReady;
    }

    void StartExtrusion()
    {
        currentState = InteractionState.Extruding;
        
        // Hide Line Renderer
        LineRenderer lr = GetComponent<LineRenderer>();
        if (lr != null) lr.enabled = false;

        // Create the 3D Object
        if (currentShapeObject == null)
            currentShapeObject = new GameObject("ExtrusionObject");
            
        currentShapeObject.transform.position = Vector3.zero;
        currentShapeObject.transform.rotation = Quaternion.identity;

        currentMeshFilter = currentShapeObject.AddComponent<MeshFilter>();
        currentMeshRenderer = currentShapeObject.AddComponent<MeshRenderer>();
        currentMeshRenderer.material = meshMaterial != null ? meshMaterial : new Material(Shader.Find("Standard"));

        currentMeshFilter.mesh = new Mesh();
        
        // Initial Update
        ProceduralExtrusionMesh.UpdateMesh(currentMeshFilter.mesh, currentPoints, 0.001f);
    }

    void FinishExtrusion()
    {
        currentState = InteractionState.Idle;
        
        // Release control of the object (it stays in scene)
        currentShapeObject = null;
        currentMeshFilter = null;
        currentMeshRenderer = null;
    }

    void CleanupCurrentShape()
    {
        if (currentShapeObject != null && currentState != InteractionState.Idle)
        {
            Destroy(currentShapeObject);
        }
    }
}
