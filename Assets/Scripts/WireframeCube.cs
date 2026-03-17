using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class WireframeCube : MonoBehaviour
{
    private LineRenderer lineRenderer;

    void Start()
    {
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.positionCount = 16;
        lineRenderer.useWorldSpace = false; // 使用局部坐标，这样线框会跟随物体移动和旋转
        
        // 设置默认线宽，你可以自己在面板里调
        lineRenderer.startWidth = 0.006f;
        lineRenderer.endWidth = 0.006f;

        DrawCube();
    }

    void DrawCube()
    {
        // 我们画一个单位大小为 1 的正方体（也就是各个顶点在 0.5 和 -0.5 的位置）
        // 因为设置了 useWorldSpace = false，它会自动受到 Transform 的 Scale 影响而变大变小
        Vector3[] points = new Vector3[16];

        // 底部四个点 (-0.5, -0.5, -0.5), (0.5, -0.5, -0.5) 等
        Vector3 p0 = new Vector3(-0.5f, -0.5f, -0.5f);
        Vector3 p1 = new Vector3( 0.5f, -0.5f, -0.5f);
        Vector3 p2 = new Vector3( 0.5f, -0.5f,  0.5f);
        Vector3 p3 = new Vector3(-0.5f, -0.5f,  0.5f);
        
        // 顶部四个点
        Vector3 p4 = new Vector3(-0.5f,  0.5f, -0.5f);
        Vector3 p5 = new Vector3( 0.5f,  0.5f, -0.5f);
        Vector3 p6 = new Vector3( 0.5f,  0.5f,  0.5f);
        Vector3 p7 = new Vector3(-0.5f,  0.5f,  0.5f);

        // 一笔画完长方体的 16 个步骤顺序
        points[0] = p0; points[1] = p1; points[2] = p2; points[3] = p3;
        points[4] = p0; // 画完底面，回到起点
        points[5] = p4; // 往上走到顶面
        points[6] = p5; points[7] = p6; points[8] = p7;
        points[9] = p4; // 画完顶面，回到顶面起点
        points[10] = p5; points[11] = p1; // 画一条竖线并回到 p1
        points[12] = p2; points[13] = p6; // 画另一条竖线并回到 p6
        points[14] = p7; points[15] = p3; // 画最后一条竖线结束在 p3

        lineRenderer.SetPositions(points);
    }
}
