using UnityEngine;

/// <summary>Scatters a small flock of flying critters into the sky above the planet.</summary>
public class FlyerSpawner : MonoBehaviour
{
    [SerializeField] int count = 8;
    [SerializeField] float respawnInterval = 25f;
    [SerializeField] float altitudeMin = 10f;
    [SerializeField] float altitudeMax = 20f;

    Transform parent;
    SphericalTerrain terrain;
    Shader shader;
    float respawnTimer;
    int activeCount;

    public void Generate(SphericalTerrain terrain, int seed)
    {
        this.terrain = terrain;
        Random.InitState(seed + 3131);

        parent = new GameObject("Flyers").transform;
        parent.SetParent(transform);

        shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        // Scale flock size with surface area, same as the other wildlife spawners.
        float scale = SphericalWorld.Instance != null ? Mathf.Clamp(SphericalWorld.Instance.Radius / 50f, 1f, 2.5f) : 1f;
        activeCount = Mathf.RoundToInt(count * scale);

        for (int i = 0; i < activeCount; i++)
            SpawnOne();
    }

    void Update()
    {
        if (terrain == null || parent == null) return;

        respawnTimer -= Time.deltaTime;
        if (respawnTimer <= 0f)
        {
            respawnTimer = respawnInterval;
            if (parent.childCount < activeCount)
                SpawnOne();
        }
    }

    void SpawnOne()
    {
        // Flyers never touch the ground, so unlike Critter/Predator they don't need a land check —
        // any direction on the sphere, including over water, is a valid spot to be airborne above.
        Vector3 dir = Random.onUnitSphere;
        float altitude = Random.Range(altitudeMin, altitudeMax);
        Vector3 pos = terrain.GetSurfacePoint(dir) + dir * altitude;

        var flyer = CreateFlyerMesh(shader, out Transform leftWing, out Transform rightWing);
        flyer.name = "Flyer";
        flyer.transform.SetParent(parent);
        flyer.transform.position = pos;
        flyer.transform.up = dir.normalized;

        flyer.AddComponent<Flyer>().SetWings(leftWing, rightWing);
    }

    GameObject CreateFlyerMesh(Shader shader, out Transform leftWing, out Transform rightWing)
    {
        var root = new GameObject();

        var body = Prim(PrimitiveType.Capsule, root.transform);
        body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        body.transform.localScale = new Vector3(0.22f, 0.42f, 0.22f);

        var head = Prim(PrimitiveType.Sphere, root.transform);
        head.transform.localPosition = new Vector3(0f, 0.05f, 0.4f);
        head.transform.localScale = Vector3.one * 0.22f;

        var beak = Prim(PrimitiveType.Cube, root.transform);
        beak.transform.localPosition = new Vector3(0f, 0.02f, 0.55f);
        beak.transform.localScale = new Vector3(0.06f, 0.06f, 0.16f);

        var tail = Prim(PrimitiveType.Cube, root.transform);
        tail.transform.localPosition = new Vector3(0f, 0f, -0.45f);
        tail.transform.localScale = new Vector3(0.28f, 0.03f, 0.22f);

        leftWing = CreateWing(root.transform, -1f);
        rightWing = CreateWing(root.transform, 1f);

        if (shader != null)
        {
            var mat = new Material(shader) { color = new Color(0.82f, 0.83f, 0.88f) };
            mat.SetFloat("_Smoothness", 0.2f);
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
                r.sharedMaterial = mat;
        }

        return root;
    }

    Transform CreateWing(Transform parent, float side)
    {
        // A pivot at the shoulder so rotating it flaps the wing about the body, rather than about
        // the wing's own center.
        var pivot = new GameObject("Wing").transform;
        pivot.SetParent(parent);
        pivot.localPosition = new Vector3(0.12f * side, 0.05f, 0f);
        pivot.localRotation = Quaternion.identity;

        var wing = Prim(PrimitiveType.Cube, pivot);
        wing.transform.localPosition = new Vector3(0.35f * side, 0f, 0f);
        wing.transform.localScale = new Vector3(0.7f, 0.02f, 0.3f);

        return pivot;
    }

    GameObject Prim(PrimitiveType type, Transform parent)
    {
        var go = GameObject.CreatePrimitive(type);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent);
        return go;
    }
}
