using System.Collections.Generic;
using UnityEngine;

public static class IcoSphereGenerator
{
    static int GetMiddlePoint(int p1, int p2, List<Vector3> vertices, Dictionary<long, int> cache)
    {
        long smallerIndex = Mathf.Min(p1, p2);
        long greaterIndex = Mathf.Max(p1, p2);
        long key = (smallerIndex << 32) + greaterIndex;

        if (cache.TryGetValue(key, out int ret))
            return ret;

        Vector3 middle = ((vertices[p1] + vertices[p2]) * 0.5f).normalized;
        int i = vertices.Count;
        vertices.Add(middle);
        cache[key] = i;
        return i;
    }

    public static Mesh Generate(int subdivisions)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();

        float t = (1f + Mathf.Sqrt(5f)) / 2f;

        vertices.Add(new Vector3(-1, t, 0).normalized);
        vertices.Add(new Vector3(1, t, 0).normalized);
        vertices.Add(new Vector3(-1, -t, 0).normalized);
        vertices.Add(new Vector3(1, -t, 0).normalized);
        vertices.Add(new Vector3(0, -1, t).normalized);
        vertices.Add(new Vector3(0, 1, t).normalized);
        vertices.Add(new Vector3(0, -1, -t).normalized);
        vertices.Add(new Vector3(0, 1, -t).normalized);
        vertices.Add(new Vector3(t, 0, -1).normalized);
        vertices.Add(new Vector3(t, 0, 1).normalized);
        vertices.Add(new Vector3(-t, 0, -1).normalized);
        vertices.Add(new Vector3(-t, 0, 1).normalized);

        int[] baseTris = {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
            1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
            4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
        };
        triangles.AddRange(baseTris);

        var cache = new Dictionary<long, int>();
        for (int s = 0; s < subdivisions; s++)
        {
            var newTris = new List<int>();
            for (int i = 0; i < triangles.Count; i += 3)
            {
                int a = GetMiddlePoint(triangles[i], triangles[i + 1], vertices, cache);
                int b = GetMiddlePoint(triangles[i + 1], triangles[i + 2], vertices, cache);
                int c = GetMiddlePoint(triangles[i + 2], triangles[i], vertices, cache);

                newTris.AddRange(new[] { triangles[i], a, c });
                newTris.AddRange(new[] { triangles[i + 1], b, a });
                newTris.AddRange(new[] { triangles[i + 2], c, b });
                newTris.AddRange(new[] { a, b, c });
            }
            triangles = newTris;
            cache.Clear();
        }

        var mesh = new Mesh();
        if (vertices.Count > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
