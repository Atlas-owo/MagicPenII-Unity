using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class CarvingController : MonoBehaviour
{
    [Header("Cylinder Settings")]
    public float radius = 0.5f;
    public float length = 2.0f;
    public int radialSegments = 64;
    public int lengthSegments = 64;
    // Horizontal Orientation: Cylinder lies along the Z axis (or X axis)
    // We will align along Local Z axis for simplicity.

    [Header("Carving Settings")]
    public float carvingRadius = 0.05f; // Radius of the carving tool
    public float carvingStrength = 5.0f; // Speed of carving
    public float minRadius = 0.1f; // Stop carving at this inner radius

    [Header("Visuals")]
    public Color surfaceColor = new Color(1f, 1f, 1f, 1f); // 假设外层材质自带贴图，颜色设为纯白不影响贴图
    public Color coreColor = new Color(0.85f, 0.7f, 0.45f); // 内部木材的纯色
    public float skinDepth = 0.05f; // 切掉多深之后露出内层颜色
    public Material woodMaterial; // Custom wood material
    
    [Header("Wood Grain Noise")]
    public float noiseScale = 15.0f; // 噪声采样的缩放度，决定木纹的密集度
    public float noiseStrength = 0.3f; // 噪声对雕刻边缘粗糙度的影响强度
    
    [Header("Input")]
    public HapticPenController penController;

    [Header("Carving Tool References")]
    [Tooltip("场景中独立作为笔尖位置的物体 (不受 HapticPenController 内部影响)")]
    public Transform carvingTip;
    [Tooltip("场景中独立作为笔身位置的物体 (不受 HapticPenController 内部影响)")]
    public Transform carvingBase;

    // Internal Mesh Data
    private Mesh mesh;
    private Vector3[] originalVertices;
    private Vector3[] currentVertices;
    private Color[] colors;
    
    // Limits
    private float maxRadiusSq;
    private float minRadiusSq;
    
    // State Tracking
    private bool wasButtonPressed = false;
    private bool isColliderUpdatePending = false;

    void Start()
    {
        GenerateCylinder();
        
        if (penController == null)
            penController = FindObjectOfType<HapticPenController>();
            
        // Setup Material for Vertex Colors
        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (woodMaterial != null)
        {
            mr.material = woodMaterial;
        }
        else if (mr.material == null || mr.material.name.StartsWith("Default"))
        {
            // Try to find a standard shader that supports vertex colors
            // "Particiles/Standard Surface" or generic Standard might need custom setup
            // Or just create a simple material at runtime
            Material m = new Material(Shader.Find("Particles/Standard Surface"));
            m.SetFloat("_Mode", 0); // Opaque
            // Ensure Albedo is using Vertex Color
            mr.material = m;
        }
    }

    void Update()
    {
        if (penController == null) return;

        // 联动逻辑：按下按钮时进入物理压力阻力模式（Hybrid Mode），松开时恢复为基础的射线跟踪模式（Raycast）
        if (penController.buttonPressed)
        {
            // 注意：Hybrid Mode 在 HapticPenController 中依然需要 Raycast 来计算目标距离
            // 因此我们保持 enableRaycastControl = true，同时开启 enableDirectPressureControl
            penController.enableRaycastControl = true;
            penController.enableDirectPressureControl = true;

            // 执行物理雕刻：完全不理会 HapticPenController 内部的结构，
            // 直接根据你在脚本参数里面独立挂载的两个 Transform 进行雕刻线段判定。
            if (carvingTip != null && carvingBase != null)
            {
                Carve(carvingTip.position, carvingBase.position);
            }
            
            wasButtonPressed = true;
        }
        else
        {
            // 默认状态：纯射线模式（无压力反馈）
            penController.enableRaycastControl = true;
            penController.enableDirectPressureControl = false;
            
            // 如果刚刚松开按钮（一个雕刻阶段结束），则判断是否需要重构物理碰撞体和法线
            if (wasButtonPressed)
            {
                if (isColliderUpdatePending)
                {
                    MeshCollider mc = GetComponent<MeshCollider>();
                    if (mc != null)
                    {
                        mc.sharedMesh = mesh;
                    }
                    isColliderUpdatePending = false;
                }
                wasButtonPressed = false;
            }
        }
    }

    void GenerateCylinder()
    {
        mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        GetComponent<MeshFilter>().mesh = mesh;
        mesh.name = "CarvableCylinder";

        // Assign 'surface' tag to the GameObject this script is attached to
        gameObject.tag = "surface";

        // Vertices Calculation
        // Tube: (radialSegments + 1) * (lengthSegments + 1)
        // Caps: (radialSegments + 1) * 2 (Center point + edge ring? No, separate vertices for flat shading or smooth?
        // Let's keep it simple: Tube only first, but ensure it's X-aligned.
        // User asked for "Edges not rendered", might mean Caps. I will add Caps.
        // Cap Vertices: Center + Ring.
        
        int tubeVertCount = (radialSegments + 1) * (lengthSegments + 1);
        int capVertCount = (radialSegments + 1) * 2; // Bottom and Top caps (Center + Ring? No, simple fan)
        // Actually for a simple fan cap we need: 1 center + (radialSegments + 1) rim.
        
        // Simpler approach for single mesh with smooth shading on tube and flat on caps:
        // We generally need separate vertices for Caps to have hard edges.
        
        // Let's just do the Tube first properly along X.
        // If "edges" meant the seam, the (radialSegments + 1) handles that.
        
        int totalVerts = (radialSegments + 1) * (lengthSegments + 1);
        originalVertices = new Vector3[totalVerts];
        currentVertices = new Vector3[totalVerts];
        colors = new Color[totalVerts];
        Vector2[] uvs = new Vector2[totalVerts];
        
        int numTris = radialSegments * lengthSegments * 6;
        int[] triangles = new int[numTris];

        float angleStep = 2f * Mathf.PI / radialSegments;
        float xStep = length / lengthSegments;
        float halfLength = length / 2f;

        maxRadiusSq = radius * radius;
        minRadiusSq = minRadius * minRadius;

        int vIndex = 0;
        int tIndex = 0;

        for (int x = 0; x <= lengthSegments; x++)
        {
            float xPos = -halfLength + x * xStep;
            
            for (int r = 0; r <= radialSegments; r++)
            {
                float angle = r * angleStep;
                
                // Horizontal Cylinder along X axis
                // Y = cos, Z = sin, X = length
                float y = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;

                Vector3 pos = new Vector3(xPos, y, z);
                
                originalVertices[vIndex] = pos;
                currentVertices[vIndex] = pos;
                colors[vIndex] = surfaceColor;
                
                // 为了修正木纹材质旋转了90度的问题，我们交换U和V的映射
                // 之前: uvs[vIndex] = new Vector2((float)x / lengthSegments, (float)r / radialSegments);
                uvs[vIndex] = new Vector2((float)r / radialSegments, (float)x / lengthSegments);

                // Triangles
                if (x < lengthSegments && r < radialSegments)
                {
                    int current = x * (radialSegments + 1) + r;
                    int next = current + 1;
                    int nextRow = (x + 1) * (radialSegments + 1) + r;
                    int nextRowNext = nextRow + 1;

                    // Counter-Clockwise winding
                    triangles[tIndex++] = current;
                    triangles[tIndex++] = next;
                    triangles[tIndex++] = nextRow;

                    triangles[tIndex++] = next;
                    triangles[tIndex++] = nextRowNext;
                    triangles[tIndex++] = nextRow;
                }
                
                vIndex++;
            }
        }

        mesh.vertices = originalVertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.colors = colors;
        mesh.RecalculateNormals();
        
        GetComponent<MeshCollider>().sharedMesh = mesh;
    }

    void Carve(Vector3 worldPenTip, Vector3 worldPenBase)
    {
        Vector3 localPenTip = transform.InverseTransformPoint(worldPenTip);
        Vector3 localPenBase = transform.InverseTransformPoint(worldPenBase);
        bool meshChanged = false;

        // 计算笔尖到笔身的局部空间方向向量
        Vector3 penSegment = localPenBase - localPenTip;
        float penSegmentSq = penSegment.sqrMagnitude;

        // Optimization: Bounds check?
        // Brute force is fine for <10k verts
        
        for (int i = 0; i < currentVertices.Length; i++)
        {
            Vector3 v = currentVertices[i];
            
            // 计算顶点 v 到笔尖-笔身线段的最短距离
            Vector3 tipToV = v - localPenTip;
            float t = 0f;
            if (penSegmentSq > 0.00001f)
            {
                // 将顶点投影在线段上的点限制在 [0, 1] 以内，保证检测不会超出笔尖和笔身的范围
                t = Mathf.Clamp01(Vector3.Dot(tipToV, penSegment) / penSegmentSq);
            }
            Vector3 closestPointOnPen = localPenTip + t * penSegment;
            float dist = Vector3.Distance(v, closestPointOnPen);
            
            if (dist < carvingRadius)
            {
                // 常规平滑衰减
                float falloffT = Mathf.Clamp01(dist / carvingRadius);
                float baseFalloff = Mathf.Cos(falloffT * Mathf.PI * 0.5f); 
                
                // 加入基于顶点空间位置的柏林噪声 (Perlin Noise) 来模拟木材的肌理感和粗糙边缘
                // 采样时乘以 noiseScale，控制纹理的细密程度
                float noiseX = v.x * noiseScale;
                float noiseY = v.y * noiseScale;
                float noiseZ = v.z * noiseScale;
                // 用三维坐标采样2D噪声，组合一下
                float noise = Mathf.PerlinNoise(noiseX, noiseY + noiseZ) * noiseStrength;
                
                // 最终的衰减受到了噪声的影响，产生“毛糙”的雕刻切口
                // (乘以 baseFalloff 是为了保证边界处依然平滑过渡为0，不会产生生硬的边界锯齿)
                float falloff = Mathf.Max(0, baseFalloff - noise * baseFalloff);
                
                // Direction: Towards Center Axis (y=0, z=0)
                // Vertex is at (x, y, z). Center is at (x, 0, 0).
                Vector3 centerPoint = new Vector3(v.x, 0, 0);
                Vector3 dirToCenter = (centerPoint - v).normalized;
                
                // Move
                Vector3 displacement = dirToCenter * carvingStrength * falloff * Time.deltaTime;
                Vector3 newPos = v + displacement;
                
                // Clamp
                Vector3 radialVec = newPos - centerPoint;
                float currentRadSq = radialVec.sqrMagnitude;
                
                if (currentRadSq < minRadiusSq)
                {
                    newPos = centerPoint + radialVec.normalized * minRadius;
                }
                
                // Color Visuals: 切掉表皮厚度后，暴露出内部颜色
                float currentRad = Vector3.Distance(newPos, centerPoint);
                if (radius - currentRad > skinDepth)
                {
                    colors[i] = coreColor;
                }
                else
                {
                    colors[i] = surfaceColor;
                }

                currentVertices[i] = newPos;
                meshChanged = true;
            }
        }

        if (meshChanged)
        {
            mesh.vertices = currentVertices;
            mesh.colors = colors;
            mesh.RecalculateNormals(); // 已将法线重算加回，保证雕刻过程中光影即时正确
            
            // 标记网格发生了形变，等松开按钮时统一更新物理碰撞体，避免实时更新导致严重卡顿
            isColliderUpdatePending = true;
        }
    }
}
