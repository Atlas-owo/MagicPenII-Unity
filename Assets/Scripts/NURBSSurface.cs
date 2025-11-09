using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class NURBSSurface : MonoBehaviour
{
    [Header("Surface Resolution")]
    [Tooltip("Number of evaluation points in X direction")]
    public int resolutionX = 30;
    [Tooltip("Number of evaluation points in Z direction")]
    public int resolutionZ = 30;

    [Header("Surface Dimensions")]
    [Tooltip("Total width of surface in X direction (meters)")]
    public float totalWidth = 1f;
    [Tooltip("Total depth of surface in Z direction (meters)")]
    public float totalDepth = 1f;

    [Header("Plateau Profile Parameters")]
    [Range(0.05f, 0.9f)]
    [Tooltip("Width of flat plateau top as fraction of total width (0.05-0.9)")]
    public float plateauWidth = 0.3f;

    [Range(0.01f, 0.2f)]
    [Tooltip("Transition length for rise/fall as fraction of total width (0.01-0.2) - smaller = steeper")]
    public float transitionLength = 0.05f;

    [Range(1.0f, 10.0f)]
    [Tooltip("Transition steepness: higher = sharper corners (1-10)")]
    public float transitionSteepness = 5.0f;

    [Header("Height Control")]
    [Range(-0.2f, 0.2f)]
    [Tooltip("Height amplitude: Positive creates hills, negative creates valleys")]
    public float amplitude = 0.05f;
    [Tooltip("When enabled, SetHeight input values will be negated")]
    public bool reverse = false;

    void Start()
    {
        GenerateMesh();
    }

    void OnValidate()
    {
        // Ensure valid values
        resolutionX = Mathf.Max(2, resolutionX);
        resolutionZ = Mathf.Max(2, resolutionZ);
        totalWidth = Mathf.Max(0.1f, totalWidth);
        totalDepth = Mathf.Max(0.1f, totalDepth);

        // Clamp parameters
        plateauWidth = Mathf.Clamp(plateauWidth, 0.05f, 0.9f);
        transitionLength = Mathf.Clamp(transitionLength, 0.01f, 0.2f);
        transitionSteepness = Mathf.Clamp(transitionSteepness, 1.0f, 10.0f);

        // Ensure plateau width + 2*transition length doesn't exceed total width
        float maxTransition = (1f - plateauWidth) / 2f;
        if (transitionLength > maxTransition)
        {
            transitionLength = maxTransition;
        }

        if (Application.isPlaying || !Application.isPlaying)
        {
            GenerateMesh();
        }
    }

    void GenerateMesh()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        MeshCollider mc = GetComponent<MeshCollider>();

        if (mf == null || mc == null) return;

        Mesh mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Vector2> uvs = new List<Vector2>();

        // Generate vertices - uniform in Z direction (2D profile extruded along Z)
        for (int z = 0; z < resolutionZ; z++)
        {
            for (int x = 0; x < resolutionX; x++)
            {
                // Normalized coordinates [0, 1]
                float xNorm = (float)x / (resolutionX - 1);
                float zNorm = (float)z / (resolutionZ - 1);

                // World coordinates
                float worldX = (xNorm - 0.5f) * totalWidth;
                float worldZ = (zNorm - 0.5f) * totalDepth;

                // Calculate height based ONLY on X position (uniform in Z)
                float height = CalculatePlateauHeight(xNorm);

                vertices.Add(new Vector3(worldX, height, worldZ));
                uvs.Add(new Vector2(xNorm, zNorm));
            }
        }

        // Generate triangles
        for (int z = 0; z < resolutionZ - 1; z++)
        {
            for (int x = 0; x < resolutionX - 1; x++)
            {
                int i = z * resolutionX + x;

                triangles.Add(i);
                triangles.Add(i + resolutionX);
                triangles.Add(i + 1);

                triangles.Add(i + 1);
                triangles.Add(i + resolutionX);
                triangles.Add(i + resolutionX + 1);
            }
        }

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        mf.mesh = mesh;
        mc.sharedMesh = mesh;
    }

    /// <summary>
    /// Calculate plateau height profile based on X position only (uniform in Z)
    /// Creates flat-topped rectangular-like profile with steep smooth transitions
    /// </summary>
    /// <param name="xNorm">Normalized X position [0, 1]</param>
    /// <returns>Height value scaled by amplitude</returns>
    float CalculatePlateauHeight(float xNorm)
    {
        // Convert to centered coordinate [-0.5, 0.5]
        float xCentered = xNorm - 0.5f;

        // Use absolute distance from center for bilateral symmetry
        float distFromCenter = Mathf.Abs(xCentered);

        // Define plateau region boundaries (as fractions of half-width)
        float plateauHalfWidth = plateauWidth * 0.5f;
        float transitionEnd = plateauHalfWidth + transitionLength;

        float heightValue;

        if (distFromCenter <= plateauHalfWidth)
        {
            // Region 1: Flat plateau at full height
            heightValue = 1.0f;
        }
        else if (distFromCenter < transitionEnd)
        {
            // Region 2: Steep smooth transition using smoothstep with adjustable steepness
            float t = (distFromCenter - plateauHalfWidth) / transitionLength;

            // Use generalized smoothstep for steeper transitions
            // Higher transitionSteepness creates sharper corners
            heightValue = GeneralizedSmoothstep(1.0f - t, transitionSteepness);
        }
        else
        {
            // Region 3: Flat tail at baseline (Y = 0)
            // No oscillations, no exponential decay - just flat zero
            heightValue = 0.0f;
        }

        return amplitude * heightValue;
    }

    /// <summary>
    /// Generalized smoothstep function for variable steepness
    /// Higher order creates steeper transitions while maintaining smoothness
    /// </summary>
    /// <param name="t">Normalized position [0, 1]</param>
    /// <param name="steepness">Steepness factor (1-10)</param>
    /// <returns>Smoothly interpolated value</returns>
    float GeneralizedSmoothstep(float t, float steepness)
    {
        // Clamp input
        t = Mathf.Clamp01(t);

        // Apply power function to steepen the curve
        // Higher steepness = sharper transition
        float power = steepness;

        // Modified smoothstep with power adjustment for steepness
        // This maintains C1 continuity while creating steeper transitions
        float smoothed = t * t * (3.0f - 2.0f * t); // Standard smoothstep

        // Apply power to increase steepness
        if (steepness > 1.0f)
        {
            // Center the curve and apply power
            float centered = smoothed - 0.5f;
            smoothed = 0.5f + Mathf.Sign(centered) * Mathf.Pow(Mathf.Abs(centered * 2.0f), 1.0f / steepness) * 0.5f;
        }

        return smoothed;
    }

    /// <summary>
    /// Set the height of the plateau surface
    /// </summary>
    /// <param name="height">Height value (positive for hills, negative for valleys)</param>
    public void SetHeight(float height)
    {
        // Apply reverse logic if enabled
        amplitude = reverse ? -height : height;
        GenerateMesh();
    }

    /// <summary>
    /// Smoothly transition to a new height over time
    /// </summary>
    public void SetHeightSmooth(float targetHeight, float duration = 0.5f)
    {
        float finalTargetHeight = reverse ? -targetHeight : targetHeight;
        StartCoroutine(SmoothHeightTransition(finalTargetHeight, duration));
    }

    private System.Collections.IEnumerator SmoothHeightTransition(float targetHeight, float duration)
    {
        float startHeight = amplitude;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            amplitude = Mathf.Lerp(startHeight, targetHeight, t);
            GenerateMesh();
            yield return null;
        }

        amplitude = targetHeight;
        GenerateMesh();
    }

    /// <summary>
    /// Get the current height amplitude
    /// </summary>
    public float GetCurrentHeight()
    {
        return amplitude;
    }

    /// <summary>
    /// Set plateau width (as fraction of total width)
    /// </summary>
    public void SetPlateauWidth(float width)
    {
        plateauWidth = Mathf.Clamp(width, 0.05f, 0.9f);
        GenerateMesh();
    }

    /// <summary>
    /// Set transition length (as fraction of total width)
    /// </summary>
    public void SetTransitionLength(float length)
    {
        transitionLength = Mathf.Clamp(length, 0.01f, 0.2f);
        GenerateMesh();
    }

    /// <summary>
    /// Set transition steepness (higher = sharper corners)
    /// </summary>
    public void SetTransitionSteepness(float steepness)
    {
        transitionSteepness = Mathf.Clamp(steepness, 1.0f, 10.0f);
        GenerateMesh();
    }

    /// <summary>
    /// Get plateau width in world units (meters)
    /// </summary>
    public float GetPlateauWidthWorld()
    {
        return plateauWidth * totalWidth;
    }

    /// <summary>
    /// Get transition length in world units (meters)
    /// </summary>
    public float GetTransitionLengthWorld()
    {
        return transitionLength * totalWidth;
    }
}
