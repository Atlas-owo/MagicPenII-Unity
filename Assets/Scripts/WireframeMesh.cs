using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(MeshFilter))]
public class WireframeMesh : MonoBehaviour
{
    void Start()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        Mesh mesh = mf.mesh;

        // 获取原始数据
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;

        List<Vector3> newVertices = new List<Vector3>();
        List<int> newTriangles = new List<int>();
        List<Vector3> barycentrics = new List<Vector3>();

        // 为每个三角形分配唯一的重心坐标
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int i0 = triangles[i];
            int i1 = triangles[i + 1];
            int i2 = triangles[i + 2];

            Vector3 v0 = vertices[i0];
            Vector3 v1 = vertices[i1];
            Vector3 v2 = vertices[i2];

            newVertices.Add(v0);
            newVertices.Add(v1);
            newVertices.Add(v2);

            newTriangles.Add(newTriangles.Count);
            newTriangles.Add(newTriangles.Count);
            newTriangles.Add(newTriangles.Count);

            barycentrics.Add(new Vector3(1, 0, 0));
            barycentrics.Add(new Vector3(0, 1, 0));
            barycentrics.Add(new Vector3(0, 0, 1));
        }

        // 生成新 Mesh
        Mesh newMesh = new Mesh();
        newMesh.vertices = newVertices.ToArray();
        newMesh.triangles = newTriangles.ToArray();
        newMesh.SetUVs(1, barycentrics); // 把重心坐标存到 UV1
        newMesh.RecalculateNormals();
        newMesh.RecalculateBounds();

        mf.mesh = newMesh;
    }
}
