using System.Collections.Generic;
using UnityEngine;

public class ResourceSpawner : MonoBehaviour
{
    public static ResourceSpawner Instance { get; private set; }

    [Header("Counts")]
    [SerializeField] int berryBushCount = 40;
    [SerializeField] int stoneDepositCount = 25;
    [SerializeField] int woodPileCount = 20;

    List<ResourceNode> allNodes = new List<ResourceNode>();
    Transform resourceParent;

    void Awake()
    {
        Instance = this;
    }

    public void Generate(SphericalTerrain terrain, int seed)
    {
        Clear();
        Random.InitState(seed + 3333);

        resourceParent = new GameObject("Resources").transform;
        resourceParent.SetParent(transform);

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        // Scale resource counts with surface area so bigger planets aren't barren.
        float scale = SphericalWorld.Instance != null ? Mathf.Clamp(SphericalWorld.Instance.Radius / 50f, 1f, 3.5f) : 1f;
        int berries = Mathf.RoundToInt(berryBushCount * scale);
        int stones = Mathf.RoundToInt(stoneDepositCount * scale);
        int woods = Mathf.RoundToInt(woodPileCount * scale);

        for (int i = 0; i < berries; i++)
            TrySpawnResource(terrain, ResourceNode.ResourceType.Berry, shader);

        for (int i = 0; i < stones; i++)
            TrySpawnResource(terrain, ResourceNode.ResourceType.Stone, shader);

        for (int i = 0; i < woods; i++)
            TrySpawnResource(terrain, ResourceNode.ResourceType.Wood, shader);
    }

    void TrySpawnResource(SphericalTerrain terrain, ResourceNode.ResourceType type, Shader shader)
    {
        Vector3 dir = Random.onUnitSphere;
        float landH = terrain.GetLandHeight(dir);
        if (landH < 0f) return;

        Vector3 surfacePos = terrain.GetSurfacePoint(dir);
        if (terrain.IsWaterNear(surfacePos, 2f)) return;

        // Type-specific placement
        switch (type)
        {
            case ResourceNode.ResourceType.Berry:
                if (landH < 0.03f || landH > 0.4f) return;
                break;
            case ResourceNode.ResourceType.Stone:
                if (landH < 0.25f) return;
                break;
            case ResourceNode.ResourceType.Wood:
                if (landH < 0.1f || landH > 0.55f) return;
                break;
        }

        Vector3 up = dir.normalized;

        var obj = new GameObject($"Resource_{type}");
        obj.transform.SetParent(resourceParent);
        obj.transform.position = surfacePos;

        var node = obj.AddComponent<ResourceNode>();
        float amount = type switch
        {
            ResourceNode.ResourceType.Berry => Random.Range(3f, 6f),
            ResourceNode.ResourceType.Stone => Random.Range(5f, 10f),
            ResourceNode.ResourceType.Wood => Random.Range(4f, 8f),
            _ => 5f
        };
        node.Init(type, amount);

        // Visual
        GameObject visual = CreateResourceVisual(type, surfacePos, up, shader);
        visual.transform.SetParent(obj.transform);
        node.SetVisual(visual);

        // Collider for detection
        var trigger = obj.AddComponent<SphereCollider>();
        trigger.radius = 2f;
        trigger.isTrigger = true;

        allNodes.Add(node);
    }

    GameObject CreateResourceVisual(ResourceNode.ResourceType type, Vector3 pos, Vector3 up, Shader shader)
    {
        var visual = new GameObject("Visual");

        switch (type)
        {
            case ResourceNode.ResourceType.Berry:
            {
                // Green bush with red berries
                var bush = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.Destroy(bush.GetComponent<Collider>());
                bush.transform.SetParent(visual.transform);
                bush.transform.position = pos + up * 0.4f;
                bush.transform.localScale = new Vector3(0.8f, 0.6f, 0.8f);
                bush.transform.up = up;
                var bushMat = new Material(shader) { color = new Color(0.15f, 0.45f, 0.12f) };
                bushMat.SetFloat("_Smoothness", 0.1f);
                bush.GetComponent<MeshRenderer>().material = bushMat;

                // Berries
                for (int i = 0; i < 5; i++)
                {
                    var berry = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Object.Destroy(berry.GetComponent<Collider>());
                    berry.transform.SetParent(visual.transform);
                    Vector3 offset = Vector3.Cross(up, Random.onUnitSphere).normalized * 0.3f + up * Random.Range(0.25f, 0.55f);
                    berry.transform.position = pos + offset;
                    berry.transform.localScale = Vector3.one * 0.12f;
                    var berryMat = new Material(shader) { color = new Color(0.8f, 0.1f, 0.1f) };
                    berry.GetComponent<MeshRenderer>().material = berryMat;
                }
                break;
            }
            case ResourceNode.ResourceType.Stone:
            {
                int count = Random.Range(2, 5);
                for (int i = 0; i < count; i++)
                {
                    var stone = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Object.Destroy(stone.GetComponent<Collider>());
                    stone.transform.SetParent(visual.transform);
                    Vector3 offset = Vector3.Cross(up, Random.onUnitSphere).normalized * Random.Range(0f, 0.4f);
                    float s = Random.Range(0.2f, 0.6f);
                    stone.transform.position = pos + offset + up * s * 0.4f;
                    stone.transform.localScale = new Vector3(s * Random.Range(0.7f, 1.3f), s * 0.6f, s * Random.Range(0.7f, 1.3f));
                    stone.transform.up = up;
                    var mat = new Material(shader) { color = new Color(Random.Range(0.35f, 0.5f), Random.Range(0.33f, 0.48f), Random.Range(0.3f, 0.4f)) };
                    mat.SetFloat("_Smoothness", 0.05f);
                    stone.GetComponent<MeshRenderer>().material = mat;
                }
                break;
            }
            case ResourceNode.ResourceType.Wood:
            {
                // Fallen log
                var log = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.Destroy(log.GetComponent<Collider>());
                log.transform.SetParent(visual.transform);
                log.transform.position = pos + up * 0.15f;
                Vector3 side = Vector3.Cross(up, Random.onUnitSphere).normalized;
                log.transform.up = side;
                log.transform.localScale = new Vector3(0.15f, 0.6f, 0.15f);
                var logMat = new Material(shader) { color = new Color(0.4f, 0.25f, 0.12f) };
                logMat.SetFloat("_Smoothness", 0.05f);
                log.GetComponent<MeshRenderer>().material = logMat;

                // A couple small branches
                for (int i = 0; i < 2; i++)
                {
                    var branch = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Object.Destroy(branch.GetComponent<Collider>());
                    branch.transform.SetParent(visual.transform);
                    Vector3 bSide = Vector3.Cross(up, Random.onUnitSphere).normalized;
                    branch.transform.position = pos + Vector3.Cross(up, Random.onUnitSphere).normalized * 0.3f + up * 0.1f;
                    branch.transform.up = bSide;
                    branch.transform.localScale = new Vector3(0.06f, 0.3f, 0.06f);
                    branch.GetComponent<MeshRenderer>().material = logMat;
                }
                break;
            }
        }

        return visual;
    }

    public ResourceNode FindNearest(Vector3 position, ResourceNode.ResourceType type, float maxRange = 50f)
    {
        ResourceNode best = null;
        float bestDist = maxRange;

        foreach (var node in allNodes)
        {
            if (node == null || node.IsEmpty || node.Type != type) continue;
            float d = Vector3.Distance(position, node.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = node;
            }
        }
        return best;
    }

    public ResourceNode FindNearestAny(Vector3 position, float maxRange = 50f)
    {
        ResourceNode best = null;
        float bestDist = maxRange;

        foreach (var node in allNodes)
        {
            if (node == null || node.IsEmpty) continue;
            float d = Vector3.Distance(position, node.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = node;
            }
        }
        return best;
    }

    void Clear()
    {
        foreach (var n in allNodes)
            if (n != null) Destroy(n.gameObject);
        allNodes.Clear();

        if (resourceParent != null)
            Destroy(resourceParent.gameObject);
    }
}
