using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class TubeTrailRenderer : MonoBehaviour
{
    [Header("Targeting")]
    public Transform targetObject;

    [Header("Tube Settings")]
    public float radius = 0.1f;
    public Color color = Color.white;
    public Material tubeMaterial;
    [Range(3, 32)]
    public int radialSegments = 8;
    public float minDistance = 0.05f;

    [Header("Control")]
    public bool isDrawing = false;
    public bool manualControl = false; // If true, isDrawing is controlled externally
    public bool clearOnStrokeEnd = true; // Default to true
    public HapticPenController penController;

    // List of strokes, where each stroke is a list of points
    private List<List<Vector3>> strokes = new List<List<Vector3>>();
    private List<Vector3> currentStroke;
    private bool wasDrawing = false;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Mesh mesh;

    private void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        mesh = new Mesh();
        meshFilter.mesh = mesh;

        if (tubeMaterial == null)
        {
            // Assign a default material if none is provided
            tubeMaterial = new Material(Shader.Find("Standard"));
            tubeMaterial.color = color;
        }
        meshRenderer.material = tubeMaterial;
    }

    private void Start()
    {
        if (penController == null)
        {
            penController = FindObjectOfType<HapticPenController>();
        }
    }

    private void Update()
    {
        if (targetObject == null) return;

        // Update material color if changed at runtime
        if (meshRenderer.material.color != color)
        {
            meshRenderer.material.color = color;
        }

        // Control drawing via pen button if controller is available AND manual control is OFF
        if (!manualControl && penController != null)
        {
            // User requested logic: isDrawing is true when button is NOT pressed
            isDrawing = penController.buttonPressed;
        }

        // Detect start of a new stroke
        if (isDrawing && !wasDrawing)
        {
            StartNewStroke();
        }
        
        // Detect end of a stroke
        if (!isDrawing && wasDrawing)
        {
            if (clearOnStrokeEnd)
            {
                Clear();
            }
        }

        wasDrawing = isDrawing;

        if (isDrawing)
        {
            AddPoint(targetObject.position);
        }
    }

    private void StartNewStroke()
    {
        currentStroke = new List<Vector3>();
        strokes.Add(currentStroke);
    }

    private void AddPoint(Vector3 position)
    {
        if (currentStroke == null) return;

        if (currentStroke.Count == 0 || Vector3.Distance(currentStroke[currentStroke.Count - 1], position) >= minDistance)
        {
            currentStroke.Add(position);
            GenerateMesh();
        }
    }

    public void Clear()
    {
        strokes.Clear();
        currentStroke = null;
        mesh.Clear();
    }

    private void GenerateMesh()
    {
        if (strokes.Count == 0) return;

        // 1. Calculate total vertices and triangles needed
        int totalVerts = 0;
        int totalTris = 0;

        foreach (var stroke in strokes)
        {
            if (stroke.Count < 2) continue;
            totalVerts += stroke.Count * radialSegments;
            totalTris += (stroke.Count - 1) * radialSegments * 6;
        }

        if (totalVerts == 0) return;

        // Check for mesh limits (Unity's default 16-bit index buffer limit is 65535 vertices)
        if (totalVerts > 65000)
        {
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }

        Vector3[] vertices = new Vector3[totalVerts];
        Vector2[] uvs = new Vector2[totalVerts];
        int[] triangles = new int[totalTris];

        int vertOffset = 0;
        int triOffset = 0;

        // 2. Generate geometry for each stroke
        foreach (var stroke in strokes)
        {
            if (stroke.Count < 2) continue;

            for (int i = 0; i < stroke.Count; i++)
            {
                Vector3 forward;
                if (i < stroke.Count - 1)
                {
                    forward = (stroke[i + 1] - stroke[i]).normalized;
                }
                else
                {
                    forward = (stroke[i] - stroke[i - 1]).normalized;
                }

                // Calculate a consistent up vector to minimize twisting
                Vector3 up = Vector3.up;
                if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.99f)
                {
                    up = Vector3.right;
                }
                
                Vector3 right = Vector3.Cross(forward, up).normalized;
                up = Vector3.Cross(right, forward).normalized;

                // Generate ring vertices
                for (int j = 0; j < radialSegments; j++)
                {
                    float angle = j * Mathf.PI * 2f / radialSegments;
                    float sin = Mathf.Sin(angle);
                    float cos = Mathf.Cos(angle);

                    Vector3 offset = (right * cos + up * sin) * radius;
                    Vector3 worldPos = stroke[i] + offset;
                    vertices[vertOffset + i * radialSegments + j] = transform.InverseTransformPoint(worldPos);
                    
                    // UVs
                    float u = (float)i / (stroke.Count - 1);
                    float v = (float)j / radialSegments;
                    uvs[vertOffset + i * radialSegments + j] = new Vector2(u, v);
                }
            }

            // Generate triangles
            for (int i = 0; i < stroke.Count - 1; i++)
            {
                for (int j = 0; j < radialSegments; j++)
                {
                    int currentRing = i * radialSegments;
                    int nextRing = (i + 1) * radialSegments;

                    int current = vertOffset + currentRing + j;
                    int next = vertOffset + currentRing + (j + 1) % radialSegments;
                    int nextRingCurrent = vertOffset + nextRing + j;
                    int nextRingNext = vertOffset + nextRing + (j + 1) % radialSegments;

                    // Triangle 1
                    triangles[triOffset++] = current;
                    triangles[triOffset++] = nextRingCurrent;
                    triangles[triOffset++] = next;

                    // Triangle 2
                    triangles[triOffset++] = nextRingCurrent;
                    triangles[triOffset++] = nextRingNext;
                    triangles[triOffset++] = next;
                }
            }

            vertOffset += stroke.Count * radialSegments;
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
    }
}
