using System.Collections.Generic;
using UnityEngine;

public static class ProceduralExtrusionMesh
{
    public static void UpdateMesh(Mesh mesh, List<Vector3> basePointsInput, float height)
    {
        if (basePointsInput == null || basePointsInput.Count < 3) return;

        // Create a local copy to avoid modifying the original reference
        List<Vector3> basePoints = new List<Vector3>(basePointsInput);

        // 1. Enforce Counter-Clockwise Winding (for consistently outward normals)
        if (IsClockwise(basePoints))
        {
            basePoints.Reverse();
        }
        // We have N points in the loop (last == first).
        // Vertices:
        //  - Bottom Ring (N points)
        //  - Top Ring (N points)
        //  - (Optional) Cap vertices if needed for simple triangulation
        
        // For sharp edges on the top/bottom face vs side walls, we usually need duplicate vertices with different normals.
        // Or we can just build a smooth mesh for now. Let's do simple flat walls.
        // Actually, easiest valid mesh:
        // 1. Bottom Cap (facing down)
        // 2. Top Cap (facing up)
        // 3. Walls (facing out)
        
        // To keep it simple and performant for real-time:
        // Vertices = (N-1) * 4  (Each segment is a separate quad? No, that's too separated)
        // Vertices = 2 * N (Standard cylinder topology)
        // But for flat shading normals, we might want unique vertices per face.
        // Let's start with shared vertices (Smooth look) for simplicity.
        
        int count = basePoints.Count; // Includes the duplicate end point
        int segments = count - 1;

        // Total Vertices: Bottom Ring + Top Ring
        Vector3[] vertices = new Vector3[count * 2];
        Vector3[] normals = new Vector3[count * 2];
        Vector2[] uvs = new Vector2[count * 2];
        
        // Populate Vertices
        for (int i = 0; i < count; i++)
        {
            Vector3 p = basePoints[i];
            // Bottom
            vertices[i] = p;
            // Top
            vertices[i + count] = new Vector3(p.x, p.y + height, p.z);
        }

        // Triangles
        // 1. Walls
        List<int> tris = new List<int>();
        
        for (int i = 0; i < segments; i++)
        {
            int currentBottom = i;
            int nextBottom = i + 1;
            int currentTop = i + count;
            int nextTop = i + count + 1;
            
            // Quad: CurrentBottom -> CurrentTop -> NextTop -> NextBottom
            // Tri 1
            tris.Add(currentBottom);
            tris.Add(currentTop);
            tris.Add(nextTop);
            
            // Tri 2
            tris.Add(currentBottom);
            tris.Add(nextTop);
            tris.Add(nextBottom);
        }
        
        // 2. Caps (Simple Fan Triangulation - assumes convex or simple concave, might fail for complex shapes)
        // Top Cap (Clockwise or CCW depending on point order. Taking standard CCW)
        // Fan from index 0
        int topOffset = count;
        for (int i = 1; i < segments - 1; i++)
        {
            tris.Add(topOffset);
            tris.Add(topOffset + i + 1);
            tris.Add(topOffset + i);
        }
        
        // Bottom Cap (Reverse winding)
        for (int i = 1; i < segments - 1; i++)
        {
            tris.Add(0);
            tris.Add(i);
            tris.Add(i + 1);
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals(); // Auto calculate for now
    }

    private static bool IsClockwise(List<Vector3> points)
    {
        // Shoelace Formula (Signed Area) on XZ plane
        float sum = 0f;
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 current = points[i];
            Vector3 next = points[(i + 1) % points.Count];
            sum += (next.x - current.x) * (next.z + current.z);
        }
        // If sum > 0, it's Clockwise (in Unity's X-Right, Z-Forward, Y-Up system? Wait.)
        // Standard Math: x1y2 - y1x2... 
        // Unity: (next.x - curr.x) * (next.z + curr.z) is a common implementation for polygon winding.
        // Result > 0 is usually Clockwise.
        return sum > 0;
    }
}
