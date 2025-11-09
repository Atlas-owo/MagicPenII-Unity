using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class GaussianSurface : MonoBehaviour
{
    public int resolutionX = 30;  // x direction samples
    public int resolutionZ = 30;  // z direction samples
    public float sizeX = 1f;
    public float sizeZ = 1f;

    public float mean = 0f;
    public float sigma = 1f;
    [Range(-0.2f, 0.2f)]
    [Tooltip("Height amplitude: Positive creates hills, negative creates valleys")]
    public float amplitude = 2f;
    
    [Header("Height Control")]
    [Tooltip("When enabled, SetHeight input values will be negated (hills become valleys and vice versa)")]
    public bool reverse = false;

    void Start()
    {
        GenerateMesh();
    }

    void OnValidate()
    {
        // Prevent negative/zero values for parameters that require positive values
        resolutionX = Mathf.Max(2, resolutionX);
        resolutionZ = Mathf.Max(2, resolutionZ);
        sizeX = Mathf.Max(0.1f, sizeX);
        sizeZ = Mathf.Max(0.1f, sizeZ);
        sigma = Mathf.Max(0.01f, sigma);
        // Note: amplitude can now be negative to create valleys/depressions
        
        // Regenerate mesh when parameters change in editor
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

        Vector3[] vertices = new Vector3[resolutionX * resolutionZ];
        int[] triangles = new int[(resolutionX - 1) * (resolutionZ - 1) * 6];
        Vector2[] uvs = new Vector2[vertices.Length];

        // Generate vertices
        for (int z = 0; z < resolutionZ; z++)
        {
            for (int x = 0; x < resolutionX; x++)
            {
                float worldX = ((float)x / (resolutionX - 1) - 0.5f) * sizeX;
                float worldZ = ((float)z / (resolutionZ - 1) - 0.5f) * sizeZ;

                float y = Gaussian(worldX, mean, sigma, amplitude);

                vertices[z * resolutionX + x] = new Vector3(worldX, y, worldZ);
                uvs[z * resolutionX + x] = new Vector2((float)x / resolutionX, (float)z / resolutionZ);
            }
        }

        // Generate triangles
        int t = 0;
        for (int z = 0; z < resolutionZ - 1; z++)
        {
            for (int x = 0; x < resolutionX - 1; x++)
            {
                int i = z * resolutionX + x;

                triangles[t++] = i;
                triangles[t++] = i + resolutionX;
                triangles[t++] = i + 1;

                triangles[t++] = i + 1;
                triangles[t++] = i + resolutionX;
                triangles[t++] = i + resolutionX + 1;
            }
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.RecalculateNormals();

        mf.mesh = mesh;
        mc.sharedMesh = mesh;
    }

    float Gaussian(float x, float mean, float sigma, float amplitude)
    {
        // Gaussian function: positive amplitude creates hills, negative creates valleys
        return amplitude * Mathf.Exp(-(Mathf.Pow(x - mean, 2) / (2 * sigma * sigma)));
    }

    public void SetHeight(float height)
    {
        // Set amplitude - positive for hills, negative for valleys
        // Apply reverse logic if enabled
        amplitude = reverse ? -height : height;
        GenerateMesh();
    }
    
    public void SetHeightSmooth(float targetHeight, float duration = 0.5f)
    {
        // Apply reverse logic to target height
        float finalTargetHeight = reverse ? -targetHeight : targetHeight;
        StartCoroutine(SmoothHeightTransition(finalTargetHeight, duration));
    }
    
    private System.Collections.IEnumerator SmoothHeightTransition(float targetHeight, float duration)
    {
        // Note: targetHeight has already been processed through reverse logic in SetHeightSmooth
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
    
    public float GetCurrentHeight()
    {
        return amplitude;
    }
}
