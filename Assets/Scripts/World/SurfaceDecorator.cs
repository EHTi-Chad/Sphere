using System.Collections.Generic;
using UnityEngine;

public class SurfaceDecorator : MonoBehaviour
{
    public static SurfaceDecorator Instance { get; private set; }

    [Header("Density")]
    [SerializeField] int treeAttempts = 5000;
    [SerializeField] int rockAttempts = 1100;
    [SerializeField] int bushAttempts = 2000;
    [SerializeField] int grassAttempts = 3200; // each places a small tuft cluster (see TryPlaceGrass)
    [SerializeField] int flowerAttempts = 1000;
    [SerializeField] int detailAttempts = 1000;

    [Header("Tree Scale")]
    [SerializeField] float treeMinScale = 0.08f;
    [SerializeField] float treeMaxScale = 0.26f; // wider range → some real standouts among the canopy
    [SerializeField] float treeHeightStretchMin = 1.2f; // stretched taller on their own vertical axis
    [SerializeField] float treeHeightStretchMax = 1.5f; // (trunk width unaffected — just height)

    // Loaded prefabs
    GameObject[] treePrefabs;
    GameObject[] bushPrefabs;
    GameObject[] grassPrefabs;
    GameObject[] flowerPrefabs;
    GameObject[] standardRockPrefabs;
    GameObject[] tinyRockPrefabs;
    GameObject[] cliffPrefabs;
    GameObject mountainPrefab;
    GameObject[] detailPrefabs; // stumps, logs, branches, mushrooms

    Transform decorParent;
    List<GameObject> allDecorations = new List<GameObject>();

    // Rivers and lakes are carved into the mesh by a hydrology pass AFTER the base terrain height is
    // sampled — GetLandHeight()/IsLand() only know the raw noise height, so they have no idea a river
    // or lake sits at a given spot (it still reads as ordinary dry land to them). That let trees and
    // rocks spawn straight into rivers/lakes. IsWaterNear() already indexes exactly those water bodies
    // (built for the fording/drink system), so we reuse it here purely to keep static decorations off
    // of visible water — it does NOT affect movement or fording, creatures still wade through as before.
    const float WaterExclusionMargin = 2.5f;
    static bool OnWater(SphericalTerrain terrain, Vector3 surfacePos) => terrain.IsWaterNear(surfacePos, WaterExclusionMargin);

    void Awake()
    {
        Instance = this;
        LoadPrefabs();
    }

    void LoadPrefabs()
    {
        string basePath = "Proxy Games/Stylized Nature Kit Lite/Prefabs";

        treePrefabs = LoadAll($"{basePath}/Foliage/Trees");
        bushPrefabs = LoadSingle($"{basePath}/Foliage/Bush/Bush");
        grassPrefabs = LoadSingle($"{basePath}/Foliage/Grass/Grass");
        flowerPrefabs = LoadSingle($"{basePath}/Foliage/Flower/Flower");
        standardRockPrefabs = LoadAll($"{basePath}/Rocks/Standard Rocks");
        tinyRockPrefabs = LoadAll($"{basePath}/Rocks/Tiny Rocks");
        cliffPrefabs = LoadAll($"{basePath}/Rocks/Rock Cliffs");
        mountainPrefab = Resources.Load<GameObject>($"{basePath}/Rocks/Mountain/Mountain");

        // Detail objects: stumps, logs, branches, mushrooms
        var details = new List<GameObject>();
        AddIfFound(details, $"{basePath}/Foliage/Stump/Stump");
        AddIfFound(details, $"{basePath}/Foliage/Log/Log");
        AddIfFound(details, $"{basePath}/Foliage/Branch/Branch");
        AddIfFound(details, $"{basePath}/Foliage/Mushroom/Mushrooms Patch");
        detailPrefabs = details.Count > 0 ? details.ToArray() : null;

        Debug.Log($"[Decorator] Loaded prefabs — Trees:{treePrefabs?.Length ?? 0} Bushes:{bushPrefabs?.Length ?? 0} Rocks:{standardRockPrefabs?.Length ?? 0} Grass:{grassPrefabs?.Length ?? 0}");

        // If Resources.Load didn't work (not in Resources folder), try asset database approach
        if (treePrefabs == null || treePrefabs.Length == 0)
            LoadPrefabsDirect();

        // Swap in higher-detail third-party packs where available (editor only — see note).
        LoadExternalPacks();
    }

    /// <summary>
    /// Pulls in realistic third-party packs the user imported. Editor-only (AssetDatabase); in a
    /// build these folders aren't in Resources, so it keeps the stylized/primitive set.
    /// </summary>
    void LoadExternalPacks()
    {
#if UNITY_EDITOR
        // Realistic boulders — used for rocks and cliffs. Skip cluster ("grup") and snow variants.
        var boulders = LoadFolder("Assets/Rocks and Boulders 2/Rocks/Prefabs", "grup", "snow");
        if (boulders != null && boulders.Length > 0)
        {
            standardRockPrefabs = boulders;
            cliffPrefabs = boulders;
            tinyRockPrefabs = boulders;
            Debug.Log($"[Decorator] Using Rocks and Boulders 2 — {boulders.Length} boulders");
        }
#endif
    }

#if UNITY_EDITOR
    GameObject[] LoadFolder(string folder, params string[] excludeContains)
    {
        if (!UnityEditor.AssetDatabase.IsValidFolder(folder)) return null;
        var guids = UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { folder });
        var list = new List<GameObject>();
        foreach (var g in guids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
            string lower = path.ToLowerInvariant();
            bool skip = false;
            foreach (var e in excludeContains)
                if (lower.Contains(e.ToLowerInvariant())) { skip = true; break; }
            if (skip) continue;

            var pf = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (pf != null) list.Add(pf);
        }
        return list.Count > 0 ? list.ToArray() : null;
    }
#endif

    void LoadPrefabsDirect()
    {
        string basePath = "Assets/Proxy Games/Stylized Nature Kit Lite/Prefabs";

        treePrefabs = LoadPrefabsFromPath(
            $"{basePath}/Foliage/Trees/Spruce 1.prefab",
            $"{basePath}/Foliage/Trees/Spruce 2.prefab"
        );

        bushPrefabs = LoadPrefabsFromPath($"{basePath}/Foliage/Bush/Bush.prefab");
        grassPrefabs = LoadPrefabsFromPath($"{basePath}/Foliage/Grass/Grass.prefab");
        flowerPrefabs = LoadPrefabsFromPath($"{basePath}/Foliage/Flower/Flower.prefab");

        standardRockPrefabs = LoadPrefabsFromPath(
            $"{basePath}/Rocks/Standard Rocks/Standard Rock 1.prefab",
            $"{basePath}/Rocks/Standard Rocks/Standard Rock 2.prefab",
            $"{basePath}/Rocks/Standard Rocks/Standard Rock 3.prefab",
            $"{basePath}/Rocks/Standard Rocks/Standard Rock 4.prefab",
            $"{basePath}/Rocks/Standard Rocks/Standard Rock 5.prefab"
        );

        tinyRockPrefabs = LoadPrefabsFromPath(
            $"{basePath}/Rocks/Tiny Rocks/Tiny Rock 1.prefab",
            $"{basePath}/Rocks/Tiny Rocks/Tiny Rock 2.prefab",
            $"{basePath}/Rocks/Tiny Rocks/Tiny Rock 3.prefab",
            $"{basePath}/Rocks/Tiny Rocks/Tiny Rock 4.prefab",
            $"{basePath}/Rocks/Tiny Rocks/Tiny Rock 5.prefab"
        );

        cliffPrefabs = LoadPrefabsFromPath(
            $"{basePath}/Rocks/Rock Cliffs/Rock Cliff 1.prefab",
            $"{basePath}/Rocks/Rock Cliffs/Rock Cliff 2.prefab",
            $"{basePath}/Rocks/Rock Cliffs/Rock Cliff 3.prefab",
            $"{basePath}/Rocks/Rock Cliffs/Rock Cliff 4.prefab",
            $"{basePath}/Rocks/Rock Cliffs/Rock Cliff 5.prefab"
        );

        var details = new List<GameObject>();
        var stump = LoadPrefabFromPath($"{basePath}/Foliage/Stump/Stump.prefab");
        var log = LoadPrefabFromPath($"{basePath}/Foliage/Log/Log.prefab");
        var branch = LoadPrefabFromPath($"{basePath}/Foliage/Branch/Branch.prefab");
        var mushroom = LoadPrefabFromPath($"{basePath}/Foliage/Mushroom/Mushrooms Patch.prefab");
        if (stump) details.Add(stump);
        if (log) details.Add(log);
        if (branch) details.Add(branch);
        if (mushroom) details.Add(mushroom);
        detailPrefabs = details.Count > 0 ? details.ToArray() : null;

        Debug.Log($"[Decorator] Direct load — Trees:{treePrefabs?.Length ?? 0} Bushes:{bushPrefabs?.Length ?? 0} Rocks:{standardRockPrefabs?.Length ?? 0}");
    }

    GameObject[] LoadAll(string resourcePath)
    {
        var loaded = Resources.LoadAll<GameObject>(resourcePath);
        return loaded != null && loaded.Length > 0 ? loaded : null;
    }

    GameObject[] LoadSingle(string resourcePath)
    {
        var obj = Resources.Load<GameObject>(resourcePath);
        return obj != null ? new[] { obj } : null;
    }

    void AddIfFound(List<GameObject> list, string resourcePath)
    {
        var obj = Resources.Load<GameObject>(resourcePath);
        if (obj != null) list.Add(obj);
    }

    GameObject LoadPrefabFromPath(string assetPath)
    {
#if UNITY_EDITOR
        return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
#else
        return null;
#endif
    }

    GameObject[] LoadPrefabsFromPath(params string[] assetPaths)
    {
        var results = new List<GameObject>();
#if UNITY_EDITOR
        foreach (var path in assetPaths)
        {
            var obj = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (obj != null) results.Add(obj);
        }
#endif
        return results.Count > 0 ? results.ToArray() : null;
    }

    public void Generate(SphericalTerrain terrain, int seed)
    {
        Clear();
        Random.InitState(seed + 7777);

        decorParent = new GameObject("Decorations").transform;
        decorParent.SetParent(transform);

        bool hasPrefabs = treePrefabs != null && treePrefabs.Length > 0;
        Debug.Log($"[Decorator] Generating with {(hasPrefabs ? "PREFAB" : "PRIMITIVE")} mode");

        // Scale foliage density with surface AREA (radius²), not radius — a planet twice as wide has
        // ~4x the surface to cover, so linear scaling (the old behaviour) left big worlds looking
        // emptier the larger they got. Capped so a titanic world's generation time / object count
        // stays bounded rather than growing without limit.
        float scale = SphericalWorld.Instance != null
            ? Mathf.Clamp((SphericalWorld.Instance.Radius / 50f) * (SphericalWorld.Instance.Radius / 50f), 1f, 7f)
            : 1f;

        int trees = Mathf.RoundToInt(treeAttempts * scale);
        int rocks = Mathf.RoundToInt(rockAttempts * scale);
        int bushes = Mathf.RoundToInt(bushAttempts * scale);
        int grasses = Mathf.RoundToInt(grassAttempts * scale);
        int flowers = Mathf.RoundToInt(flowerAttempts * scale);
        int details = Mathf.RoundToInt(detailAttempts * scale);

        for (int i = 0; i < trees; i++)
            TryPlaceTree(terrain);
        for (int i = 0; i < rocks; i++)
            TryPlaceRock(terrain);
        for (int i = 0; i < bushes; i++)
            TryPlaceBush(terrain);
        for (int i = 0; i < grasses; i++)
            TryPlaceGrass(terrain);
        for (int i = 0; i < flowers; i++)
            TryPlaceFlower(terrain);
        for (int i = 0; i < details; i++)
            TryPlaceDetail(terrain);

        Debug.Log($"[Decorator] Placed {allDecorations.Count} objects");
    }

    GameObject PickRandom(GameObject[] prefabs)
    {
        if (prefabs == null || prefabs.Length == 0) return null;
        return prefabs[Random.Range(0, prefabs.Length)];
    }

    Shader urpLitShader;
    Dictionary<Material, Material> materialFixCache = new Dictionary<Material, Material>();

    GameObject PlacePrefab(GameObject prefab, Vector3 surfacePos, Vector3 up, float scale)
    {
        if (prefab == null) return null;
        var obj = Instantiate(prefab, surfacePos, Quaternion.identity, decorParent);
        obj.transform.up = up;
        obj.transform.Rotate(0f, Random.Range(0f, 360f), 0f, Space.Self);
        obj.transform.localScale = Vector3.one * scale;

        // Remove colliders from decoration prefabs to avoid physics overhead
        foreach (var col in obj.GetComponentsInChildren<Collider>())
            Destroy(col);

        // Fix pink/purple materials — convert broken shaders to URP Lit
        FixMaterials(obj);

        // GPU instancing so dense foliage batches cheaply instead of one draw call each.
        foreach (var r in obj.GetComponentsInChildren<MeshRenderer>())
            foreach (var m in r.sharedMaterials)
                if (m != null) m.enableInstancing = true;

        allDecorations.Add(obj);
        return obj;
    }

    void FixMaterials(GameObject obj)
    {
        if (urpLitShader == null)
            urpLitShader = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLitShader == null) return;

        foreach (var renderer in obj.GetComponentsInChildren<MeshRenderer>())
        {
            var mats = renderer.sharedMaterials;
            bool changed = false;

            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;

                // Check if shader is broken (pink) or is the old Standard shader
                bool isBroken = mats[i].shader == null
                    || mats[i].shader.name == "Hidden/InternalErrorShader"
                    || mats[i].shader.name == "Standard"
                    || mats[i].shader.name.Contains("Error")
                    || mats[i].shader.name.Contains("HDRP")
                    || mats[i].shader.name.StartsWith("HD ");

                if (!isBroken) continue;

                // Use cache so we don't create duplicate materials
                if (materialFixCache.TryGetValue(mats[i], out Material cached))
                {
                    mats[i] = cached;
                    changed = true;
                    continue;
                }

                // Create a new URP Lit material preserving color and texture
                var fixedMat = new Material(urpLitShader);
                fixedMat.name = mats[i].name + "_URP";

                // Copy main texture
                if (mats[i].HasProperty("_MainTex"))
                {
                    var tex = mats[i].GetTexture("_MainTex");
                    if (tex != null)
                        fixedMat.SetTexture("_BaseMap", tex);
                }

                // Copy color
                if (mats[i].HasProperty("_Color"))
                    fixedMat.SetColor("_BaseColor", mats[i].GetColor("_Color"));

                // Copy smoothness
                if (mats[i].HasProperty("_Glossiness"))
                    fixedMat.SetFloat("_Smoothness", mats[i].GetFloat("_Glossiness"));

                // Copy metallic
                if (mats[i].HasProperty("_Metallic"))
                    fixedMat.SetFloat("_Metallic", mats[i].GetFloat("_Metallic"));

                // Handle transparency
                if (mats[i].HasProperty("_Color") && mats[i].GetColor("_Color").a < 0.99f)
                {
                    fixedMat.SetFloat("_Surface", 1f);
                    fixedMat.SetOverrideTag("RenderType", "Transparent");
                    fixedMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    fixedMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    fixedMat.SetInt("_ZWrite", 0);
                    fixedMat.renderQueue = 3000;
                }

                materialFixCache[mats[i]] = fixedMat;
                mats[i] = fixedMat;
                changed = true;
            }

            if (changed)
                renderer.sharedMaterials = mats;
        }
    }

    // ---- Fallback primitives for when no prefabs are loaded ----

    GameObject PlaceFallbackTree(Vector3 surfacePos, Vector3 up, float scale)
    {
        var tree = new GameObject("Tree");
        tree.transform.SetParent(decorParent);
        tree.transform.position = surfacePos;

        var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(trunk.GetComponent<Collider>());
        trunk.transform.SetParent(tree.transform);
        trunk.transform.localPosition = up * scale * 0.7f;
        trunk.transform.localScale = new Vector3(0.12f * scale, scale * 0.7f, 0.12f * scale);
        trunk.transform.up = up;
        trunk.GetComponent<MeshRenderer>().material = MakeMat(new Color(0.3f, 0.18f, 0.08f));

        var canopy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(canopy.GetComponent<Collider>());
        canopy.transform.SetParent(tree.transform);
        canopy.transform.localPosition = up * scale * 1.4f;
        float cw = scale * Random.Range(0.8f, 1.2f);
        canopy.transform.localScale = new Vector3(cw, scale * 0.7f, cw);
        canopy.GetComponent<MeshRenderer>().material = MakeMat(new Color(0.1f, 0.35f, 0.06f));

        allDecorations.Add(tree);
        return tree;
    }

    GameObject PlaceFallbackRock(Vector3 surfacePos, Vector3 up, float scale)
    {
        var rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(rock.GetComponent<Collider>());
        rock.transform.SetParent(decorParent);
        rock.transform.position = surfacePos + up * scale * 0.3f;
        rock.transform.localScale = new Vector3(scale * 1.1f, scale * 0.6f, scale * 0.9f);
        rock.transform.up = up;
        rock.transform.Rotate(Random.Range(-20f, 20f), Random.Range(0f, 360f), 0f, Space.Self);
        rock.GetComponent<MeshRenderer>().material = MakeMat(new Color(0.42f, 0.38f, 0.33f));
        allDecorations.Add(rock);
        return rock;
    }

    Material fallbackMatCache;
    Material MakeMat(Color c)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader == null) return null;
        var mat = new Material(shader) { color = c };
        mat.SetFloat("_Smoothness", 0.08f);
        mat.enableInstancing = true;
        return mat;
    }

    // ---- Placement logic ----

    void TryPlaceTree(SphericalTerrain terrain)
    {
        Vector3 dir = Random.onUnitSphere;
        float landH = terrain.GetLandHeight(dir);
        if (landH < 0f) return;

        if (landH < 0.06f || landH > 0.6f) return;

        // Biome gate: forests love temperate/wet ground; deserts and cold dry land stay nearly bare.
        float treeMoist = terrain.MoistureAt(dir);
        float treeTemp = Mathf.Clamp01(1f - Mathf.Abs(dir.y) * 1.15f - landH * 0.4f);
        float biomeTreeChance = treeTemp > 0.7f && treeMoist < 0.4f ? 0.04f   // desert
                              : treeTemp < 0.25f ? treeMoist * 0.5f            // cold → only wet taiga
                              : Mathf.Clamp01(treeMoist + 0.15f);              // wetter = more trees
        if (Random.value > biomeTreeChance) return;

        float density;
        bool isDense;
        int clusterMin, clusterMax;
        float clusterSpread;

        if (landH < 0.15f)      { density = 0.2f;  isDense = false; clusterMin = 2; clusterMax = 4; clusterSpread = 1.5f; }
        else if (landH < 0.25f) { density = 0.6f;  isDense = false; clusterMin = 4; clusterMax = 8; clusterSpread = 2f; }
        else if (landH < 0.5f)  { density = 1.0f;  isDense = true;  clusterMin = 8; clusterMax = 20; clusterSpread = 3f; }
        else                    { density = 0.35f; isDense = false; clusterMin = 2; clusterMax = 4; clusterSpread = 1.5f; }

        // Multi-octave cluster noise for natural forest shapes
        float n1 = Mathf.PerlinNoise(dir.x * 4f + 100f, dir.z * 4f + 100f);
        float n2 = Mathf.PerlinNoise(dir.x * 8f + 200f, dir.z * 8f + 200f) * 0.5f;
        float n3 = Mathf.PerlinNoise(dir.x * 16f + 300f, dir.z * 16f + 300f) * 0.25f;
        float clusterNoise = (n1 + n2 + n3) / 1.75f;

        if (isDense)
            density *= Mathf.Lerp(0.1f, 1f, clusterNoise);
        else
            density *= clusterNoise > 0.45f ? 1f : 0.1f;

        if (Random.value > density) return;

        // Place a cluster of trees around this point
        int count = Random.Range(clusterMin, clusterMax + 1);
        Vector3 up = dir.normalized;

        for (int i = 0; i < count; i++)
        {
            // Offset each tree from the center point along the surface
            Vector3 treeDir;
            if (i == 0)
            {
                treeDir = dir;
            }
            else
            {
                Vector3 tangent = Vector3.Cross(up, Random.onUnitSphere).normalized;
                float offsetDist = Random.Range(0.3f, clusterSpread) / (terrain.GetComponent<SphericalWorld>()?.Radius ?? 50f);
                treeDir = (dir + tangent * offsetDist).normalized;

                // Make sure the offset tree is still on valid land
                float offsetLandH = terrain.GetLandHeight(treeDir);
                if (offsetLandH < 0.04f || offsetLandH > 0.62f) continue;
            }

            Vector3 surfacePos = terrain.GetSurfacePoint(treeDir);
            if (OnWater(terrain, surfacePos)) continue; // don't grow a tree out of a river/lake

            Vector3 treeUp = treeDir.normalized;
            float scale = Random.Range(treeMinScale, treeMaxScale);
            if (isDense) scale *= Random.Range(1.0f, 1.5f);

            // Vary scale within cluster for natural look
            scale *= Random.Range(0.7f, 1.3f);

            GameObject placedTree = treePrefabs != null && treePrefabs.Length > 0
                ? PlacePrefab(PickRandom(treePrefabs), surfacePos, treeUp, scale)
                : PlaceFallbackTree(surfacePos, treeUp, scale * 2f);

            // Stretch taller on the tree's own vertical (local Y) axis only, so trunks don't get
            // proportionally thicker too — reads as tall growth, not just "bigger."
            if (placedTree != null)
            {
                Vector3 s = placedTree.transform.localScale;
                s.y *= Random.Range(treeHeightStretchMin, treeHeightStretchMax);
                placedTree.transform.localScale = s;
            }
        }
    }

    void TryPlaceRock(SphericalTerrain terrain)
    {
        Vector3 dir = Random.onUnitSphere;
        float landH = terrain.GetLandHeight(dir);
        if (landH < 0f) return;

        float density = landH > 0.5f ? 1f : landH > 0.3f ? 0.5f : 0.14f;
        if (Random.value > density) return;

        Vector3 surfacePos = terrain.GetSurfacePoint(dir);
        if (OnWater(terrain, surfacePos)) return;
        Vector3 up = dir.normalized;

        if (standardRockPrefabs != null && standardRockPrefabs.Length > 0)
        {
            float scale;
            GameObject[] pool;

            if (landH > 0.7f && cliffPrefabs != null && cliffPrefabs.Length > 0)
            {
                pool = cliffPrefabs;
                scale = Random.Range(0.3f, 0.7f);
            }
            else if (landH > 0.5f)
            {
                pool = standardRockPrefabs;
                scale = Random.Range(0.2f, 0.6f);
            }
            else
            {
                pool = tinyRockPrefabs != null && tinyRockPrefabs.Length > 0 ? tinyRockPrefabs : standardRockPrefabs;
                scale = Random.Range(0.15f, 0.35f);
            }

            PlacePrefab(PickRandom(pool), surfacePos, up, scale);
        }
        else
        {
            PlaceFallbackRock(surfacePos, up, Random.Range(0.3f, 1f));
        }
    }

    void TryPlaceBush(SphericalTerrain terrain)
    {
        Vector3 dir = Random.onUnitSphere;
        float landH = terrain.GetLandHeight(dir);
        if (landH < 0.02f || landH > 0.4f) return;

        Vector3 surfacePos = terrain.GetSurfacePoint(dir);
        if (OnWater(terrain, surfacePos)) return;
        Vector3 up = dir.normalized;
        float scale = Random.Range(0.2f, 0.5f);

        if (bushPrefabs != null && bushPrefabs.Length > 0)
            PlacePrefab(PickRandom(bushPrefabs), surfacePos, up, scale);
    }

    void TryPlaceGrass(SphericalTerrain terrain)
    {
        Vector3 dir = Random.onUnitSphere;
        float landH = terrain.GetLandHeight(dir);
        if (landH < 0.02f || landH > 0.4f) return;
        if (grassPrefabs == null || grassPrefabs.Length == 0) return;

        // Deserts are sandy, not grassy — drop most grass in hot, dry biomes.
        float gMoist = terrain.MoistureAt(dir);
        float gTemp = Mathf.Clamp01(1f - Mathf.Abs(dir.y) * 1.15f - landH * 0.4f);
        if (gTemp > 0.7f && gMoist < 0.35f && Random.value > 0.12f) return;

        Vector3 up = dir.normalized;
        float radius = terrain.GetComponent<SphericalWorld>()?.Radius ?? 50f;

        // Drop a small tuft cluster so ground cover reads as dense grass, not lone blades.
        int tufts = Random.Range(3, 6);
        for (int i = 0; i < tufts; i++)
        {
            Vector3 gdir = i == 0
                ? dir
                : (dir + Vector3.Cross(up, Random.onUnitSphere).normalized * (Random.Range(0.2f, 1.3f) / radius)).normalized;

            float gh = terrain.GetLandHeight(gdir);
            if (gh < 0.02f || gh > 0.42f) continue;

            Vector3 gPos = terrain.GetSurfacePoint(gdir);
            if (OnWater(terrain, gPos)) continue;

            PlacePrefab(PickRandom(grassPrefabs), gPos, gdir.normalized, Random.Range(0.18f, 0.55f));
        }
    }

    void TryPlaceFlower(SphericalTerrain terrain)
    {
        Vector3 dir = Random.onUnitSphere;
        float landH = terrain.GetLandHeight(dir);
        if (landH < 0.05f || landH > 0.3f) return;

        // Flowers cluster in patches
        float clusterNoise = Mathf.PerlinNoise(dir.x * 10f + 200f, dir.z * 10f + 200f);
        if (clusterNoise < 0.6f) return;

        Vector3 surfacePos = terrain.GetSurfacePoint(dir);
        if (OnWater(terrain, surfacePos)) return;
        Vector3 up = dir.normalized;
        float scale = Random.Range(0.15f, 0.35f);

        if (flowerPrefabs != null && flowerPrefabs.Length > 0)
            PlacePrefab(PickRandom(flowerPrefabs), surfacePos, up, scale);
    }

    void TryPlaceDetail(SphericalTerrain terrain)
    {
        if (detailPrefabs == null || detailPrefabs.Length == 0) return;

        Vector3 dir = Random.onUnitSphere;
        float landH = terrain.GetLandHeight(dir);
        if (landH < 0.1f || landH > 0.55f) return;

        // Details mostly in forest zones
        float clusterNoise = Mathf.PerlinNoise(dir.x * 6f + 100f, dir.z * 6f + 100f);
        if (clusterNoise < 0.4f) return;

        Vector3 surfacePos = terrain.GetSurfacePoint(dir);
        if (OnWater(terrain, surfacePos)) return;
        Vector3 up = dir.normalized;
        float scale = Random.Range(0.15f, 0.4f);

        PlacePrefab(PickRandom(detailPrefabs), surfacePos, up, scale);
    }

    public void Clear()
    {
        foreach (var d in allDecorations)
            if (d != null) Destroy(d);
        allDecorations.Clear();
        if (decorParent != null)
            Destroy(decorParent.gameObject);
    }
}
