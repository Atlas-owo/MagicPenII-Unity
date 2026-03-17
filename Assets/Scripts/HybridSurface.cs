using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class HybridSurface : MonoBehaviour
{
    public enum SurfaceShape
    {
        Flat,
        Nurbs,
        SineWave
    }

    [Header("Configuration")]
    public SurfaceShape shape = SurfaceShape.Flat;
    public float width = 0.3f;
    
    // NURBS / Plateau Params
    public float plateauWidthRatio = 0.3f; // 0.05 - 0.9
    public float transitionLengthRatio = 0.05f; // 0.01 - 0.2
    public float transitionSteepness = 5.0f; // 1 - 10
    
    // Sine / General Amplitude
    public float amplitude = 0.05f;
    public float periods = 2.0f;

    public Vector3 startPoint;
    public Vector3 endPoint;
    
    // For Logic
    private float length;
    private Quaternion rotation;

    public void Generate(Vector3 start, Vector3 end, SurfaceShape shapeType, float surfWidth, float amp, float per, float platW, float transL, float steepness)
    {
        this.startPoint = start;
        this.endPoint = end;
        this.shape = shapeType;
        this.width = surfWidth;
        this.amplitude = amp;
        this.periods = per;
        this.plateauWidthRatio = platW;
        this.transitionLengthRatio = transL;
        this.transitionSteepness = steepness;

        GenerateMesh();
        
        // Ensure layer is set for haptic controller
        int surfaceLayer = LayerMask.NameToLayer("Surface");
        if (surfaceLayer == -1)
        {
            // Try lowercase 'surface' just in case
            surfaceLayer = LayerMask.NameToLayer("surface");
        }

        if (surfaceLayer != -1)
        {
            gameObject.layer = surfaceLayer;
            // Also ensure the collider is enabled
            if (GetComponent<MeshCollider>() != null) GetComponent<MeshCollider>().enabled = true;
        }
        else
        {
            Debug.LogError("Layer 'Surface' (or 'surface') not found! raycast cannot detect it. Please create the layer in the Unity Inspector.");
        }
    }

    // --- Visual Settings ---
    public float longitudinalPadding = 0.15f; // Extra length at start/end
    
    private void GenerateMesh()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        MeshCollider mc = GetComponent<MeshCollider>();
        MeshRenderer mr = GetComponent<MeshRenderer>();

        Mesh mesh = new Mesh();
        
        // Resolution
        int resX = 150; // Increased resolution for smoother padding
        // Actually, if we want the surface to be flat across width, 2 is fine. 
        // Logic: The "height" variation is along the path (Start->End).
        // It acts like a ribbon.

        Vector3 baseline = endPoint - startPoint;
        length = baseline.magnitude;
        Vector3 dir = baseline.normalized;
        
        // Calculate Right Vector (Lateral)
        Vector3 up = Vector3.up; 
        Vector3 right = Vector3.Cross(dir, up).normalized;
        if (right.sqrMagnitude < 0.001f) right = Vector3.right; // Handle vertical path case

        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Vector2> uvs = new List<Vector2>();

        // Calculate Padding Ratio
        // We want t=0 to be Start, t=1 to be End.
        // We extend range: tMin to tMax.
        float paddingRatio = (length > 0.001f) ? (longitudinalPadding / length) : 0f;
        float tMin = -paddingRatio;
        float tMax = 1.0f + paddingRatio;

        for (int i = 0; i < resX; i++)
        {
            // Map i to [tMin, tMax]
            float pct = (float)i / (resX - 1);
            float t = Mathf.Lerp(tMin, tMax, pct);
            
            // Get Height
            float height = GetHeightAt(t);
            
            // Center point calculation
            Vector3 centerPos = Vector3.LerpUnclamped(startPoint, endPoint, t);
            
            // HORIZONTAL PADDING LOGIC FOR FLAT SURFACE
            // If it's a Tilted Flat Surface, we want "Horizontal Extensions".
            // i.e., clamp the Y-value to StartPoint.y (if t < 0) or EndPoint.y (if t > 1).
            if (shape == SurfaceShape.Flat)
            {
                if (t < 0) centerPos.y = startPoint.y;
                else if (t > 1) centerPos.y = endPoint.y;
            }
            // For Sine/Nurbs (which now use Horizontal Trail Axis), they are already horizontal, so Lerp is fine.

            // Apply Height (Vertical offset - from GetHeightAt logic)
            // Note: Flat returns 0 height, so centerPos Y is the key.
            centerPos += Vector3.up * height; 

            // Create Left and Right vertices
            Vector3 leftPos = centerPos - right * (width * 0.5f);
            Vector3 rightPos = centerPos + right * (width * 0.5f);

            vertices.Add(leftPos);
            vertices.Add(rightPos);
            
            // UVs
            uvs.Add(new Vector2(0, pct));
            uvs.Add(new Vector2(1, pct));
        }

        // Triangles
        for (int i = 0; i < resX - 1; i++)
        {
            int baseIdx = i * 2;
            int nextIdx = (i + 1) * 2;

            // Top side (Standard winding)
            triangles.Add(baseIdx);
            triangles.Add(nextIdx);
            triangles.Add(baseIdx + 1);

            triangles.Add(baseIdx + 1);
            triangles.Add(nextIdx);
            triangles.Add(nextIdx + 1);

            // Bottom side (Reversed winding to make it double-sided)
            // This ensures raycasts hitting from the other side are detected, 
            // and the surface is visible from below if the user looks up.
            // Also fixes cases where 'Top' might have been calculated inverted.
            triangles.Add(baseIdx);
            triangles.Add(baseIdx + 1);
            triangles.Add(nextIdx);

            triangles.Add(baseIdx + 1);
            triangles.Add(nextIdx + 1);
            triangles.Add(nextIdx);
        }

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        mf.mesh = mesh;
        mc.sharedMesh = mesh; // Important for collision
        
        // Material Setup: White, 80% Transparent (Alpha 0.2)
        if (mr.sharedMaterial == null || mr.sharedMaterial.name == "Default-Material")
        {
            Material transMat = new Material(Shader.Find("Standard"));
            transMat.name = "HybridTransparent";
            
            // Set Rendering Mode to Transparent (Mode 3)
            transMat.SetFloat("_Mode", 3);
            transMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            transMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            transMat.SetInt("_ZWrite", 0);
            transMat.DisableKeyword("_ALPHATEST_ON");
            transMat.EnableKeyword("_ALPHABLEND_ON");
            transMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            transMat.renderQueue = 3000;

            // Color: White with 0.2 Alpha
            transMat.color = new Color(1f, 1f, 1f, 0.2f); // 80% Transparency
            
            mr.material = transMat;
        }
    }

    public float GetHeightAt(float t)
    {
        switch (shape)
        {
            case SurfaceShape.Flat:
                return 0f;
                
            case SurfaceShape.SineWave:
                // Sine wave logic: 
                // Maybe 0 at start/end? 
                // t * periods * 2PI
                // If we want it to start and end at 0, periods should be integer?
                // User said "defined with two points and parameters".
                return Mathf.Sin(t * periods * Mathf.PI * 2f) * amplitude;
                
            case SurfaceShape.Nurbs:
                return CalculateNurbsHeight(t);
                
            default:
                return 0f;
        }
    }

    private float CalculateNurbsHeight(float t)
    {
        // Reusing logic from TargetTrail.cs / NURBSSurface.cs
        float tCentered = t - 0.5f;
        float distFromCenter = Mathf.Abs(tCentered);

        float plateauHalfWidth = plateauWidthRatio * 0.5f;
        float transitionEnd = plateauHalfWidth + transitionLengthRatio;

        float heightValue;

        if (distFromCenter <= plateauHalfWidth)
        {
            heightValue = 1.0f;
        }
        else if (distFromCenter < transitionEnd)
        {
            float p = (distFromCenter - plateauHalfWidth) / transitionLengthRatio;
            heightValue = GeneralizedSmoothstep(1.0f - p, transitionSteepness);
        }
        else
        {
            heightValue = 0.0f;
        }

        return amplitude * heightValue;
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
}
