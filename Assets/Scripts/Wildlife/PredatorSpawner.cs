using UnityEngine;

/// <summary>Scatters a few predators across the land and keeps the population topped up.</summary>
public class PredatorSpawner : MonoBehaviour
{
    [SerializeField] int count = 3;
    [SerializeField] float respawnInterval = 45f;

    [Tooltip("New predators (including a fresh New Game's opening wave and later respawns) ignore " +
             "player creatures for this many seconds — still wander and hunt critters, just no danger " +
             "to the player until the world has had a chance to settle in.")]
    [SerializeField] float dangerGraceSeconds = 180f; // 3 min — matches Arrival now taking a little longer

    Transform parent;
    SphericalTerrain terrain;
    Shader shader;
    float respawnTimer;
    int activeCount;

    public void Generate(SphericalTerrain terrain, int seed)
    {
        this.terrain = terrain;
        Random.InitState(seed + 9191);

        parent = new GameObject("Predators").transform;
        parent.SetParent(transform);

        shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        // A few per world, scaling gently with size — predators are meant to be rare and dangerous.
        float scale = SphericalWorld.Instance != null ? Mathf.Clamp(SphericalWorld.Instance.Radius / 60f, 1f, 2.5f) : 1f;
        activeCount = Mathf.Max(1, Mathf.RoundToInt(count * scale));

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
            if (landH > 0.75f) continue; // off the snowy peaks

            Vector3 pos = terrain.GetSurfacePoint(dir);
            if (terrain.IsWaterNear(pos, 2f)) continue;

            var pred = CreatePredatorMesh(shader);
            pred.name = "Predator";
            pred.transform.SetParent(parent);
            pred.transform.position = pos;
            pred.transform.up = dir.normalized;
            pred.AddComponent<Predator>().SetInitialGrace(dangerGraceSeconds);
            return;
        }
    }

    GameObject CreatePredatorMesh(Shader shader)
    {
        var root = new GameObject();

        var body = Prim(PrimitiveType.Capsule, root.transform);
        body.transform.localPosition = new Vector3(0f, 0.55f, 0f);
        body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        body.transform.localScale = new Vector3(0.55f, 0.875f, 0.55f);

        var head = Prim(PrimitiveType.Sphere, root.transform);
        head.transform.localPosition = new Vector3(0f, 0.875f, 0.6f);
        head.transform.localScale = Vector3.one * 0.45f;

        var snout = Prim(PrimitiveType.Cube, root.transform);
        snout.transform.localPosition = new Vector3(0f, 0.62f, 0.9f);
        snout.transform.localScale = new Vector3(0.2f, 0.16f, 0.3f);

        var tail = Prim(PrimitiveType.Capsule, root.transform);
        tail.transform.localPosition = new Vector3(0f, 0.5f, -0.6f);
        tail.transform.localRotation = Quaternion.Euler(65f, 0f, 0f);
        tail.transform.localScale = new Vector3(0.12f, 0.4f, 0.12f);

        if (shader != null)
        {
            var mat = new Material(shader) { color = new Color(0.30f, 0.08f, 0.09f) }; // dark blood-red
            mat.SetFloat("_Smoothness", 0.12f);
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
                r.sharedMaterial = mat;
        }

        // Glowing eyes — menace, and a way to spot the threat from a distance.
        AddGlowEye(root.transform, new Vector3(-0.14f, 0.76f, 0.82f), shader);
        AddGlowEye(root.transform, new Vector3(0.14f, 0.76f, 0.82f), shader);

        return root;
    }

    GameObject Prim(PrimitiveType type, Transform parent)
    {
        var go = GameObject.CreatePrimitive(type);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent);
        return go;
    }

    void AddGlowEye(Transform parent, Vector3 localPos, Shader shader)
    {
        var e = Prim(PrimitiveType.Sphere, parent);
        e.transform.localPosition = localPos;
        e.transform.localScale = Vector3.one * 0.11f;
        if (shader != null)
        {
            var m = new Material(shader) { color = new Color(1f, 0.65f, 0.05f) };
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", new Color(1f, 0.45f, 0f) * 3f);
            e.GetComponent<MeshRenderer>().sharedMaterial = m;
        }
    }
}
