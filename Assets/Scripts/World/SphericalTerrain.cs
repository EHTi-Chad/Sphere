using UnityEngine;

public class SphericalTerrain : MonoBehaviour
{
    [Header("Generation")]
    [SerializeField] int seed = 42;
    [SerializeField] int subdivisions = 5;
    [SerializeField] float radius = 50f;
    [SerializeField] float heightScale = 8f;
    [SerializeField] float waterLevel = 0.4f;

    [Header("Noise")]
    [SerializeField] int octaves = 6;
    [SerializeField] float baseFrequency = 1.5f;
    [SerializeField] float lacunarity = 2.1f;
    [SerializeField] float persistence = 0.45f;

    [Header("Colors")]
    [SerializeField] Color deepWater = new Color(0.05f, 0.1f, 0.35f);
    [SerializeField] Color shallowWater = new Color(0.1f, 0.3f, 0.55f);
    [SerializeField] Color sand = new Color(0.76f, 0.7f, 0.5f);
    [SerializeField] Color grass = new Color(0.2f, 0.55f, 0.15f);
    [SerializeField] Color forest = new Color(0.1f, 0.35f, 0.08f);
    [SerializeField] Color rock = new Color(0.45f, 0.4f, 0.35f);
    [SerializeField] Color snow = new Color(0.92f, 0.92f, 0.95f);

    [Header("Water")]
    [SerializeField] float waterAlpha = 0.6f;

    float[] heightMap;
    Vector3[] directions;
    Mesh terrainMesh;

    public int Seed => seed;
    public float WaterLevel => waterLevel;

    public void SetRadius(float r) { radius = r; }

    public void Generate()
    {
        Random.InitState(seed);
        float seedOffsetX = Random.Range(-10000f, 10000f);
        float seedOffsetY = Random.Range(-10000f, 10000f);
        float seedOffsetZ = Random.Range(-10000f, 10000f);

        terrainMesh = IcoSphereGenerator.Generate(subdivisions);
        var vertices = terrainMesh.vertices;
        var colors = new Color[vertices.Length];
        heightMap = new float[vertices.Length];
        directions = new Vector3[vertices.Length];

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 dir = vertices[i].normalized;
            directions[i] = dir;

            float h = SampleHeight(dir, seedOffsetX, seedOffsetY, seedOffsetZ);
            heightMap[i] = h;

            float displacement = h > waterLevel ? (h - waterLevel) * heightScale : 0f;
            vertices[i] = dir * (radius + displacement);

            colors[i] = GetTerrainColor(h);
        }

        terrainMesh.SetVertices(vertices);
        terrainMesh.colors = colors;
        terrainMesh.RecalculateNormals();
        terrainMesh.RecalculateBounds();

        var filter = GetComponent<MeshFilter>();
        if (filter == null) filter = gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = terrainMesh;

        var renderer = GetComponent<MeshRenderer>();
        if (renderer == null) renderer = gameObject.AddComponent<MeshRenderer>();
        var shader = Shader.Find("Custom/VertexColorLit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        var mat = new Material(shader);
        mat.SetFloat("_Smoothness", 0.2f);
        renderer.material = mat;

        var collider = GetComponent<MeshCollider>();
        if (collider == null) collider = gameObject.AddComponent<MeshCollider>();
        collider.sharedMesh = terrainMesh;

        CreateWaterSphere();
    }

    float SampleHeight(Vector3 dir, float offX, float offY, float offZ)
    {
        float value = 0f;
        float amplitude = 1f;
        float frequency = baseFrequency;
        float maxValue = 0f;

        for (int o = 0; o < octaves; o++)
        {
            float nx = dir.x * frequency + offX;
            float ny = dir.y * frequency + offY;
            float nz = dir.z * frequency + offZ;

            float sample = SampleNoise3D(nx, ny, nz);
            value += sample * amplitude;
            maxValue += amplitude;

            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return value / maxValue;
    }

    float SampleNoise3D(float x, float y, float z)
    {
        float xy = Mathf.PerlinNoise(x, y);
        float xz = Mathf.PerlinNoise(x + 31.416f, z + 47.853f);
        float yz = Mathf.PerlinNoise(y + 67.291f, z + 83.124f);
        return (xy + xz + yz) / 3f;
    }

    Color GetTerrainColor(float height)
    {
        if (height < waterLevel * 0.6f) return deepWater;
        if (height < waterLevel * 0.9f) return shallowWater;
        if (height < waterLevel) return Color.Lerp(shallowWater, sand, (height - waterLevel * 0.9f) / (waterLevel * 0.1f));

        float landHeight = (height - waterLevel) / (1f - waterLevel);
        if (landHeight < 0.05f) return sand;
        if (landHeight < 0.3f) return Color.Lerp(sand, grass, (landHeight - 0.05f) / 0.25f);
        if (landHeight < 0.55f) return Color.Lerp(grass, forest, (landHeight - 0.3f) / 0.25f);
        if (landHeight < 0.75f) return Color.Lerp(forest, rock, (landHeight - 0.55f) / 0.2f);
        return Color.Lerp(rock, snow, (landHeight - 0.75f) / 0.25f);
    }

    void CreateWaterSphere()
    {
        var waterObj = new GameObject("Water");
        waterObj.transform.SetParent(transform);
        waterObj.transform.localPosition = Vector3.zero;

        var waterMesh = IcoSphereGenerator.Generate(4);
        var waterVerts = waterMesh.vertices;
        for (int i = 0; i < waterVerts.Length; i++)
            waterVerts[i] = waterVerts[i].normalized * radius;
        waterMesh.SetVertices(waterVerts);
        waterMesh.RecalculateNormals();

        var filter = waterObj.AddComponent<MeshFilter>();
        filter.sharedMesh = waterMesh;

        var renderer = waterObj.AddComponent<MeshRenderer>();
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = 3000;
        mat.color = new Color(0.1f, 0.3f, 0.6f, waterAlpha);
        mat.SetFloat("_Smoothness", 0.9f);
        renderer.material = mat;
    }

    public float GetHeightAtDirection(Vector3 direction)
    {
        Random.State currentState = Random.state;
        Random.InitState(seed);
        float offX = Random.Range(-10000f, 10000f);
        float offY = Random.Range(-10000f, 10000f);
        float offZ = Random.Range(-10000f, 10000f);
        Random.state = currentState;

        return SampleHeight(direction.normalized, offX, offY, offZ);
    }

    public bool IsLand(Vector3 worldPosition)
    {
        Vector3 dir = (worldPosition - transform.position).normalized;
        return GetHeightAtDirection(dir) > waterLevel;
    }

    public Vector3 GetSurfacePoint(Vector3 direction)
    {
        float h = GetHeightAtDirection(direction);
        float displacement = h > waterLevel ? (h - waterLevel) * heightScale : 0f;
        return transform.position + direction.normalized * (radius + displacement);
    }
}
