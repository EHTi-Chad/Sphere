using UnityEngine;

/// <summary>Scatters fish through the ocean, swimming just beneath the surface. No-ops gracefully on a dry world.</summary>
public class SwimmerSpawner : MonoBehaviour
{
    [SerializeField] int count = 16;
    [SerializeField] float respawnInterval = 20f;
    [SerializeField] float depthMin = 0.4f;
    [SerializeField] float depthMax = 1.6f;

    Transform parent;
    SphericalTerrain terrain;
    Shader shader;
    float respawnTimer;
    int activeCount;

    public void Generate(SphericalTerrain terrain, int seed)
    {
        this.terrain = terrain;
        Random.InitState(seed + 5151);

        parent = new GameObject("Swimmers").transform;
        parent.SetParent(transform);

        shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        // Scale shoal size with surface area, same as the other wildlife spawners.
        float scale = SphericalWorld.Instance != null ? Mathf.Clamp(SphericalWorld.Instance.Radius / 50f, 1f, 2.5f) : 1f;
        activeCount = Mathf.RoundToInt(count * scale);

        for (int i = 0; i < activeCount; i++)
            TrySpawnOne();
    }

    void Update()
    {
        if (terrain == null || parent == null) return;

        respawnTimer -= Time.deltaTime;
        if (respawnTimer <= 0f)
        {
            respawnTimer = respawnInterval;
            if (parent.childCount < activeCount)
                TrySpawnOne();
        }
    }

    void TrySpawnOne()
    {
        var world = SphericalWorld.Instance;
        if (world == null) return;

        for (int attempt = 0; attempt < 30; attempt++)
        {
            Vector3 dir = Random.onUnitSphere;
            float h = terrain.GetHeightAtDirection(dir);
            if (h > terrain.WaterLevel) continue; // land — keep looking for open water

            float depth = Random.Range(depthMin, depthMax);
            Vector3 pos = world.Center + dir * (world.Radius - depth);

            var fish = CreateFishMesh(shader);
            fish.name = "Swimmer";
            fish.transform.SetParent(parent);
            fish.transform.position = pos;
            fish.transform.up = dir.normalized;
            fish.AddComponent<Swimmer>();
            return;
        }
        // No water found within the attempt budget (dry world, or unlucky sampling this round) — skip silently.
    }

    GameObject CreateFishMesh(Shader shader)
    {
        var root = new GameObject();

        var body = Prim(PrimitiveType.Capsule, root.transform);
        body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        body.transform.localScale = new Vector3(0.18f, 0.32f, 0.18f);

        var tailFin = Prim(PrimitiveType.Cube, root.transform);
        tailFin.transform.localPosition = new Vector3(0f, 0f, -0.32f);
        tailFin.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
        tailFin.transform.localScale = new Vector3(0.02f, 0.22f, 0.22f);

        var dorsalFin = Prim(PrimitiveType.Cube, root.transform);
        dorsalFin.transform.localPosition = new Vector3(0f, 0.14f, 0.02f);
        dorsalFin.transform.localScale = new Vector3(0.02f, 0.14f, 0.12f);

        if (shader != null)
        {
            var mat = new Material(shader) { color = new Color(0.25f, 0.55f, 0.7f) };
            mat.SetFloat("_Smoothness", 0.4f);
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
                r.sharedMaterial = mat;
        }

        return root;
    }

    GameObject Prim(PrimitiveType type, Transform parent)
    {
        var go = GameObject.CreatePrimitive(type);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent);
        return go;
    }
}
