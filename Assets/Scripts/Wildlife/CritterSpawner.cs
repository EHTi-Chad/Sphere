using UnityEngine;

public class CritterSpawner : MonoBehaviour
{
    [SerializeField] int count = 18;
    [SerializeField] float respawnInterval = 20f;

    Transform parent;
    SphericalTerrain terrain;
    Shader shader;
    float respawnTimer;
    int activeCount;

    public void Generate(SphericalTerrain terrain, int seed)
    {
        this.terrain = terrain;
        Random.InitState(seed + 7777);

        parent = new GameObject("Critters").transform;
        parent.SetParent(transform);

        shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        // Scale herd size with surface area (kept modest — each critter scans every frame).
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
        for (int attempt = 0; attempt < 30; attempt++)
        {
            Vector3 dir = Random.onUnitSphere;
            float landH = terrain.GetLandHeight(dir);
            if (landH < 0.1f) continue; // clear of the waterline, not just barely non-negative
            if (landH > 0.7f) continue; // keep them off the snowy peaks

            Vector3 pos = terrain.GetSurfacePoint(dir);
            if (terrain.IsWaterNear(pos, 2f)) continue;

            var critter = CreateCritterMesh(shader);
            critter.name = "Critter";
            critter.transform.SetParent(parent);
            critter.transform.position = pos;
            critter.transform.up = dir.normalized;
            critter.AddComponent<Critter>();
            return;
        }
    }

    GameObject CreateCritterMesh(Shader shader)
    {
        var root = new GameObject();

        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Object.Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(root.transform);
        body.transform.localPosition = new Vector3(0f, 0.35f, 0f);
        body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        body.transform.localScale = new Vector3(0.35f, 0.5f, 0.35f);

        var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(head.GetComponent<Collider>());
        head.transform.SetParent(root.transform);
        head.transform.localPosition = new Vector3(0f, 0.625f, 0.35f);
        head.transform.localScale = Vector3.one * 0.3f;

        if (shader != null)
        {
            var mat = new Material(shader) { color = new Color(0.55f, 0.42f, 0.28f) };
            mat.SetFloat("_Smoothness", 0.1f);
            // sharedMaterial, not material — the latter instantiates a NEW clone per renderer (2 per
            // critter, body+head), leaking a growing pile of orphaned Material instances every time
            // critters respawn over a long session. PredatorSpawner/FlyerSpawner/SwimmerSpawner already
            // use sharedMaterial correctly; this one was the missed sibling.
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
                r.sharedMaterial = mat;
        }

        return root;
    }
}
