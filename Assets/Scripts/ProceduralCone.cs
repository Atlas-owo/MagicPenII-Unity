using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ProceduralCone : MonoBehaviour
{
    [Header("Cone Settings")]
    public float height = 0.2f;      // Height of the cone
    public float radius = 0.1f;      // Radius of the base
    public int segments = 32;        // Smoothness
    public bool pivotAtBase = true;  // If true, pivot is at center of base. If false, pivot is at center of volume.

    [Header("References")]
    public Transform tipMarker;      // Optional: Assign an empty object here to visually track the tip

    // Public API to get the Tip Position in World Space
    public Vector3 TipPosition
    {
        get
        {
            float yOffset = pivotAtBase ? height : height * 0.5f;
            return transform.TransformPoint(new Vector3(0, yOffset, 0));
        }
    }

    private void OnValidate()
    {
        Generate();
    }

    private void Start()
    {
        Generate();
    }

    [ContextMenu("Generate Cone")]
    public void Generate()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        Mesh mesh = new Mesh();
        mesh.name = "ProceduralCone";

        int vertexCount = segments + 2; // Base center + tip + circle vertices
        // Actually slightly more for hard edges usually, but let's do shared vertices for smooth cone, hard base needs separate.
        // Simplest: 
        // Tip: 1
        // Base Circle: segments (for side)
        // Base Circle: segments (for bottom)
        // Base Center: 1
        // Total = 1 + segments + segments + 1 = 2 * segments + 2
        
        Vector3[] vertices = new Vector3[segments * 2 + 2];
        int[] triangles = new int[segments * 6]; // 3 per side, 3 per bottom segment

        float yTip = pivotAtBase ? height : height * 0.5f;
        float yBase = pivotAtBase ? 0 : -height * 0.5f;

        // Vertices
        int vIndex = 0;
        
        // 0. Tip
        vertices[vIndex++] = new Vector3(0, yTip, 0);

        // 1. Side Vertices (Top Ring - technically same pos as tip but needed for normals if we wanted flat shading, but for smooth tip we share)
        // Let's interact share the Tip vertex for the "Side" fan.
        // Side Bottom Ring
        for (int i = 0; i < segments; i++)
        {
            float angle = (float)i / segments * Mathf.PI * 2f;
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;
            vertices[vIndex++] = new Vector3(x, yBase, z);
        }

        // 2. Bottom Cap Center
        int bottomCenterIdx = vIndex;
        vertices[vIndex++] = new Vector3(0, yBase, 0);

        // 3. Bottom Cap Ring
        int bottomRingStart = vIndex;
        for (int i = 0; i < segments; i++)
        {
            float angle = (float)i / segments * Mathf.PI * 2f;
            // Reverse winding for bottom? Or just normal logic.
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;
            vertices[vIndex++] = new Vector3(x, yBase, z);
        }

        // Triangles
        int tIndex = 0;

        // Side Fan
        // Tip is at 0. Side Ring starts at 1.
        for (int i = 0; i < segments; i++)
        {
            int current = i + 1;
            int next = (i + 1) % segments + 1;

            triangles[tIndex++] = 0;      // Tip
            triangles[tIndex++] = next;   // Next
            triangles[tIndex++] = current; // Current
        }

        // Bottom Fan
        // Center is bottomCenterIdx. Ring starts at bottomRingStart.
        // Normal should define down.
        for (int i = 0; i < segments; i++)
        {
            int current = bottomRingStart + i;
            int next = bottomRingStart + (i + 1) % segments;

            triangles[tIndex++] = bottomCenterIdx;
            triangles[tIndex++] = current; 
            triangles[tIndex++] = next;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        
        mf.mesh = mesh;

        // Update Child Marker if exists
        if (tipMarker != null)
        {
            tipMarker.localPosition = new Vector3(0, yTip, 0);
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(TipPosition, 0.005f);
    }
}
