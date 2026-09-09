using System.Collections.Generic;
using UnityEngine;

public class CreatureCamp : MonoBehaviour
{
    [SerializeField] string ownerName;
    [SerializeField] float campRadius = 5f;

    Dictionary<ResourceNode.ResourceType, float> stockpile = new Dictionary<ResourceNode.ResourceType, float>();
    int shelterLevel; // 0 = open ground, 1 = lean-to, 2 = hut, 3 = house
    Vector3 surfaceUp;

    // Visuals
    Transform baseVisual;
    Transform shelterVisual;
    Transform stockpileVisual;
    float lastStockHash;

    // Cached materials
    Material woodMat;
    Material stoneMat;
    Material thatchMat;
    Material foodMat;
    Material meatMat;
    Material fenceMat;

    // Build costs per level — nudged up so reaching each shelter tier (a StoryDirector milestone)
    // takes a meaningful chunk of a ~30-45 min session rather than a couple of quick gather trips.
    static readonly float[] woodCost = { 0, 4, 10, 18 };
    static readonly float[] stoneCost = { 0, 2, 5, 12 };

    // Distance from camp center to the shelter/house pivot. The shelter geometry in BuildLeanTo/
    // Hut/House was originally hand-tuned at a scale smaller than the creature meant to live inside
    // it (a creature stands ~1.6-2 units tall; the old hut roof topped out at 0.85) — see
    // BuildShelterVisual's structureScale. This distance needs to clear the largest (house) footprint
    // once scaled up, so it doesn't swallow the camp's stockpile/fire-pit area.
    const float ShelterOffsetDistance = 5f;

    // Whichever staged construction (shelter or fire pit) is currently in progress — see
    // ConstructionTimeline. Only one build action happens at a time per camp, so a single shared
    // reference is enough; each new Build*Visual call simply replaces it.
    ConstructionTimeline activeConstruction;
    public bool IsUnderConstruction => activeConstruction != null && !activeConstruction.IsComplete;

    public string OwnerName => ownerName;
    public float CampRadius => campRadius;
    public int ShelterLevel => shelterLevel;
    public float ShelterProtection => shelterLevel * 0.25f;

    // Single source of truth for "how much wood/stone does the next upgrade need" — CreatureBody's
    // fast-layer gather decisions read these instead of keeping their own separate copy of the cost
    // table (which had silently drifted out of sync with this one).
    public float NextUpgradeWoodCost => shelterLevel >= 3 ? 0f : woodCost[shelterLevel + 1];
    public float NextUpgradeStoneCost => shelterLevel >= 3 ? 0f : stoneCost[shelterLevel + 1];

    public void Init(string owner, Vector3 position, Vector3 up)
    {
        ownerName = owner;
        transform.position = position;
        surfaceUp = up;

        stockpile[ResourceNode.ResourceType.Berry] = 0f;
        stockpile[ResourceNode.ResourceType.Stone] = 0f;
        stockpile[ResourceNode.ResourceType.Wood] = 0f;
        stockpile[ResourceNode.ResourceType.Meat] = 0f;

        CreateMaterials();
        CreateBaseVisual();
    }

    void CreateMaterials()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) { Debug.LogError("[Camp] No shader found for camp materials"); return; }

        woodMat = new Material(shader) { color = new Color(0.35f, 0.22f, 0.1f) };
        woodMat.SetFloat("_Smoothness", 0.08f);

        stoneMat = new Material(shader) { color = new Color(0.5f, 0.47f, 0.42f) };
        stoneMat.SetFloat("_Smoothness", 0.05f);

        thatchMat = new Material(shader) { color = new Color(0.55f, 0.45f, 0.25f) };
        thatchMat.SetFloat("_Smoothness", 0.05f);

        foodMat = new Material(shader) { color = new Color(0.7f, 0.15f, 0.1f) };
        foodMat.SetFloat("_Smoothness", 0.2f);

        meatMat = new Material(shader) { color = new Color(0.45f, 0.12f, 0.1f) };
        meatMat.SetFloat("_Smoothness", 0.25f);

        fenceMat = new Material(shader) { color = new Color(0.4f, 0.28f, 0.14f) };
        fenceMat.SetFloat("_Smoothness", 0.06f);
    }

    void CreateBaseVisual()
    {
        var baseObj = new GameObject("CampBase");
        baseObj.transform.SetParent(transform);
        baseVisual = baseObj.transform;

        // Ground circle marker — flat disc
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(ground.GetComponent<Collider>());
        ground.transform.SetParent(baseVisual);
        ground.transform.position = transform.position + surfaceUp * 0.01f;
        ground.transform.localScale = new Vector3(3f, 0.02f, 3f);
        ground.transform.up = surfaceUp;
        if (woodMat != null)
        {
            // Softer/more transparent than before — this is just a worn clearing marker, not the
            // shelter itself; it should recede once a real 3D structure exists nearby.
            var groundMat = new Material(woodMat) { color = new Color(0.28f, 0.2f, 0.11f, 0.3f) };
            groundMat.SetFloat("_Smoothness", 0.02f);
            ground.GetComponent<MeshRenderer>().material = groundMat;
        }
        // The fire pit is no longer pre-placed — creatures build it (see BuildFirePitVisual).
    }

    // ---- Fire pit (a deliberate build step after shelter) ----
    public bool HasFirePit { get; private set; }
    Transform firePitVisual;
    public float FirePitWoodCost => 2f;
    public float FirePitStoneCost => 1f;

    public bool CanBuildFirePit()
    {
        return !HasFirePit
            && GetStock(ResourceNode.ResourceType.Wood) >= FirePitWoodCost
            && GetStock(ResourceNode.ResourceType.Stone) >= FirePitStoneCost;
    }

    public bool TryBuildFirePit()
    {
        if (!CanBuildFirePit()) return false;
        stockpile[ResourceNode.ResourceType.Wood] -= FirePitWoodCost;
        stockpile[ResourceNode.ResourceType.Stone] -= FirePitStoneCost;
        HasFirePit = true;
        BuildFirePitVisual();
        UpdateStockpileVisual();
        Debug.Log($"[Camp] {ownerName} began building a fire pit");
        return true;
    }

    /// <summary>Builds the fire pit. Normally stages in over real time (stones, then logs, then the
    /// flame catching last) via ConstructionTimeline; pass instant:true when restoring a save, where
    /// it should simply already exist.</summary>
    void BuildFirePitVisual(bool instant = false)
    {
        if (firePitVisual != null) Object.Destroy(firePitVisual.gameObject);

        var pitObj = new GameObject("FirePit");
        pitObj.transform.SetParent(transform);
        firePitVisual = pitObj.transform;

        Vector3 right = GetRight();
        Vector3 fwd = Vector3.Cross(right, surfaceUp).normalized;

        // A house is big enough to hold the fire inside (same offset as its own pivot, see
        // BuildShelterVisual); smaller shelters keep the fire beside them, at camp center.
        Vector3 center = shelterLevel >= 3
            ? transform.position + fwd * ShelterOffsetDistance
            : transform.position;

        // Pivot at the fire base so the grow-in animation rises from the ground.
        firePitVisual.position = center;

        ConstructionTimeline timeline = null;
        if (!instant)
        {
            timeline = pitObj.AddComponent<ConstructionTimeline>();
            timeline.Init(12f);
        }

        // Ring of stones — laid first.
        var stones = new List<GameObject>();
        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f * Mathf.Deg2Rad;
            Vector3 offset = (right * Mathf.Cos(angle) + fwd * Mathf.Sin(angle)) * 0.3f;
            var stone = MakePrim(PrimitiveType.Sphere, firePitVisual);
            stone.transform.position = center + offset + surfaceUp * 0.06f;
            stone.transform.localScale = new Vector3(0.1f, 0.07f, 0.1f);
            stone.transform.up = surfaceUp;
            stone.GetComponent<MeshRenderer>().material = stoneMat;
            stones.Add(stone);
        }
        AddOrReveal(timeline, stones, 0f);

        // Criss-crossed logs — placed next.
        var logs = new List<GameObject>();
        for (int i = 0; i < 2; i++)
        {
            var log = MakePrim(PrimitiveType.Cylinder, firePitVisual);
            log.transform.position = center + surfaceUp * 0.07f;
            log.transform.localScale = new Vector3(0.05f, 0.25f, 0.05f);
            log.transform.up = (i == 0 ? right : fwd);
            log.GetComponent<MeshRenderer>().material = woodMat;
            logs.Add(log);
        }
        AddOrReveal(timeline, logs, 0.45f);

        // Glowing flame (emissive — reads as fire without the cost of a real light) — catches last.
        var flame = MakePrim(PrimitiveType.Sphere, firePitVisual);
        flame.transform.position = center + surfaceUp * 0.18f;
        flame.transform.localScale = new Vector3(0.22f, 0.32f, 0.22f);
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader != null)
        {
            var fireMat = new Material(shader) { color = new Color(1f, 0.5f, 0.12f) };
            fireMat.EnableKeyword("_EMISSION");
            fireMat.SetColor("_EmissionColor", new Color(1f, 0.45f, 0.1f) * 3f);
            flame.GetComponent<MeshRenderer>().material = fireMat;
        }
        AddOrReveal(timeline, new List<GameObject> { flame }, 0.85f);

        // Scale up around its base pivot to stay proportionate to the (now much bigger) shelter.
        firePitVisual.localScale = Vector3.one * 1.8f;

        activeConstruction = timeline;
        if (instant) firePitVisual.gameObject.AddComponent<GrowIn>();
    }

    /// <summary>Registers a group of parts to reveal at a point in the construction timeline, or —
    /// when timeline is null (restoring from a save) — leaves them visible immediately.</summary>
    void AddOrReveal(ConstructionTimeline timeline, List<GameObject> parts, float revealAtNormalized)
    {
        if (timeline != null) timeline.AddStage(parts, revealAtNormalized);
    }

    /// <summary>Builds the shelter for the current level. Normally stages in over real time (frame,
    /// then walls, then roof — see the per-tier methods) via ConstructionTimeline, so the player can
    /// actually watch a house go up; pass instant:true when restoring a save, where it should simply
    /// already exist complete.</summary>
    void BuildShelterVisual(bool instant = false)
    {
        if (shelterVisual != null) Object.Destroy(shelterVisual.gameObject);

        var shelterObj = new GameObject("Shelter");
        shelterObj.transform.SetParent(transform);
        shelterVisual = shelterObj.transform;

        Vector3 right = GetRight();
        Vector3 fwd = Vector3.Cross(right, surfaceUp).normalized;
        Vector3 shelterCenter = transform.position + fwd * ShelterOffsetDistance;

        // Pivot at the structure's base so the grow-in animation rises from the ground.
        shelterVisual.position = shelterCenter;

        ConstructionTimeline timeline = null;
        if (!instant)
        {
            timeline = shelterObj.AddComponent<ConstructionTimeline>();
            float duration = shelterLevel switch { 1 => 18f, 2 => 26f, 3 => 36f, _ => 10f };
            timeline.Init(duration);
        }

        switch (shelterLevel)
        {
            case 1: BuildLeanTo(shelterCenter, right, fwd, timeline); break;
            case 2: BuildHut(shelterCenter, right, fwd, timeline); break;
            case 3: BuildHouse(shelterCenter, right, fwd, timeline); break;
        }

        // Scale the whole assembled structure up around its base pivot. BuildLeanTo/Hut/House's hand-
        // placed primitive coordinates were tuned at a scale smaller than the creature meant to live
        // inside them — e.g. the hut roof used to sit at height 0.85 while a creature stands ~1.6-2
        // units tall. Each level steps up further so upgrading to a bigger shelter visibly means
        // something, not just "more polygons at the same tiny size."
        float structureScale = shelterLevel switch
        {
            1 => 3.0f,
            2 => 3.4f,
            3 => 3.8f,
            _ => 1f
        };
        shelterVisual.localScale = Vector3.one * structureScale;

        activeConstruction = timeline;
        if (instant) shelterObj.AddComponent<GrowIn>();
    }

    // Reveal order: frame first, then walls, then roof last — like watching a real building go up.
    void BuildLeanTo(Vector3 center, Vector3 right, Vector3 fwd, ConstructionTimeline timeline)
    {
        var frame = new List<GameObject>();

        // Two angled support poles
        for (int i = -1; i <= 1; i += 2)
        {
            var pole = MakePrim(PrimitiveType.Cylinder, shelterVisual);
            pole.transform.position = center + right * i * 0.5f + surfaceUp * 0.35f;
            pole.transform.localScale = new Vector3(0.05f, 0.5f, 0.05f);
            pole.transform.up = surfaceUp;
            pole.transform.Rotate(25f * i, 0f, 0f, Space.Self);
            pole.GetComponent<MeshRenderer>().material = woodMat;
            frame.Add(pole);
        }

        // Ridge pole
        var ridge = MakePrim(PrimitiveType.Cylinder, shelterVisual);
        ridge.transform.position = center + surfaceUp * 0.6f - fwd * 0.1f;
        ridge.transform.localScale = new Vector3(0.04f, 0.6f, 0.04f);
        ridge.transform.up = right;
        ridge.GetComponent<MeshRenderer>().material = woodMat;
        frame.Add(ridge);
        AddOrReveal(timeline, frame, 0f);

        // Thatch panels — go on last.
        var roof = new List<GameObject>();
        for (int i = 0; i < 3; i++)
        {
            var panel = MakePrim(PrimitiveType.Cube, shelterVisual);
            panel.transform.position = center + surfaceUp * (0.35f + i * 0.12f) - fwd * (0.15f + i * 0.1f);
            panel.transform.localScale = new Vector3(1.1f, 0.03f, 0.4f);
            panel.transform.up = surfaceUp;
            panel.transform.Rotate(35f, 0f, 0f, Space.Self);
            panel.GetComponent<MeshRenderer>().material = thatchMat;
            roof.Add(panel);
        }
        AddOrReveal(timeline, roof, 0.6f);
    }

    void BuildHut(Vector3 center, Vector3 right, Vector3 fwd, ConstructionTimeline timeline)
    {
        var frame = new List<GameObject>();

        // Four corner posts
        for (int i = 0; i < 4; i++)
        {
            float angle = (i * 90f + 45f) * Mathf.Deg2Rad;
            Vector3 offset = (right * Mathf.Cos(angle) + fwd * Mathf.Sin(angle)) * 0.6f;

            var post = MakePrim(PrimitiveType.Cylinder, shelterVisual);
            post.transform.position = center + offset + surfaceUp * 0.4f;
            post.transform.localScale = new Vector3(0.06f, 0.4f, 0.06f);
            post.transform.up = surfaceUp;
            post.GetComponent<MeshRenderer>().material = woodMat;
            frame.Add(post);
        }

        // Cross beams
        for (int i = 0; i < 2; i++)
        {
            var beam = MakePrim(PrimitiveType.Cylinder, shelterVisual);
            beam.transform.position = center + surfaceUp * 0.8f;
            beam.transform.localScale = new Vector3(0.04f, 0.7f, 0.04f);
            beam.transform.up = i == 0 ? right : fwd;
            beam.GetComponent<MeshRenderer>().material = woodMat;
            frame.Add(beam);
        }
        AddOrReveal(timeline, frame, 0f);

        // Half-walls
        var walls = new List<GameObject>();
        for (int i = 0; i < 3; i++)
        {
            float angle = (i * 90f) * Mathf.Deg2Rad;
            Vector3 offset = (right * Mathf.Cos(angle) + fwd * Mathf.Sin(angle)) * 0.65f;

            var wall = MakePrim(PrimitiveType.Cube, shelterVisual);
            wall.transform.position = center + offset + surfaceUp * 0.25f;
            wall.transform.localScale = new Vector3(0.06f, 0.4f, 1.2f);
            wall.transform.up = surfaceUp;
            wall.transform.LookAt(center + surfaceUp * 0.25f, surfaceUp);
            wall.GetComponent<MeshRenderer>().material = woodMat;
            walls.Add(wall);
        }
        AddOrReveal(timeline, walls, 0.4f);

        // Thatch roof — goes on last.
        var roof = MakePrim(PrimitiveType.Cube, shelterVisual);
        roof.transform.position = center + surfaceUp * 0.85f;
        roof.transform.localScale = new Vector3(1.5f, 0.06f, 1.5f);
        roof.transform.up = surfaceUp;
        roof.GetComponent<MeshRenderer>().material = thatchMat;
        AddOrReveal(timeline, new List<GameObject> { roof }, 0.75f);
    }

    void BuildHouse(Vector3 center, Vector3 right, Vector3 fwd, ConstructionTimeline timeline)
    {
        // Stone foundation — laid first.
        var foundation = MakePrim(PrimitiveType.Cube, shelterVisual);
        foundation.transform.position = center + surfaceUp * 0.05f;
        foundation.transform.localScale = new Vector3(1.8f, 0.1f, 1.8f);
        foundation.transform.up = surfaceUp;
        foundation.GetComponent<MeshRenderer>().material = stoneMat;
        AddOrReveal(timeline, new List<GameObject> { foundation }, 0f);

        // Stone walls (4 sides, one with gap for door) — go up next.
        var walls = new List<GameObject>();
        Vector3[] wallDirs = { right, -right, fwd, -fwd };
        for (int i = 0; i < 4; i++)
        {
            if (i == 3) continue; // door opening on -fwd side

            var wall = MakePrim(PrimitiveType.Cube, shelterVisual);
            wall.transform.position = center + wallDirs[i] * 0.8f + surfaceUp * 0.45f;
            bool isSide = (i < 2);
            wall.transform.localScale = isSide
                ? new Vector3(0.1f, 0.8f, 1.6f)
                : new Vector3(1.6f, 0.8f, 0.1f);
            wall.transform.up = surfaceUp;
            wall.GetComponent<MeshRenderer>().material = stoneMat;
            walls.Add(wall);
        }
        AddOrReveal(timeline, walls, 0.3f);

        // Door frame posts.
        var doorPosts = new List<GameObject>();
        for (int s = -1; s <= 1; s += 2)
        {
            var doorPost = MakePrim(PrimitiveType.Cylinder, shelterVisual);
            doorPost.transform.position = center - fwd * 0.8f + right * s * 0.3f + surfaceUp * 0.4f;
            doorPost.transform.localScale = new Vector3(0.06f, 0.4f, 0.06f);
            doorPost.transform.up = surfaceUp;
            doorPost.GetComponent<MeshRenderer>().material = woodMat;
            doorPosts.Add(doorPost);
        }
        AddOrReveal(timeline, doorPosts, 0.55f);

        // Peaked roof (two angled planes) + ridge cap — the finishing touch.
        var roof = new List<GameObject>();
        for (int s = -1; s <= 1; s += 2)
        {
            var roofPanel = MakePrim(PrimitiveType.Cube, shelterVisual);
            roofPanel.transform.position = center + surfaceUp * 1f + right * s * 0.45f;
            roofPanel.transform.localScale = new Vector3(1.1f, 0.06f, 1.9f);
            roofPanel.transform.up = surfaceUp;
            roofPanel.transform.Rotate(0f, 0f, s * 20f, Space.Self);
            roofPanel.GetComponent<MeshRenderer>().material = thatchMat;
            roof.Add(roofPanel);
        }
        var ridgeCap = MakePrim(PrimitiveType.Cylinder, shelterVisual);
        ridgeCap.transform.position = center + surfaceUp * 1.1f;
        ridgeCap.transform.localScale = new Vector3(0.06f, 0.95f, 0.06f);
        ridgeCap.transform.up = fwd;
        ridgeCap.GetComponent<MeshRenderer>().material = woodMat;
        roof.Add(ridgeCap);
        AddOrReveal(timeline, roof, 0.8f);
    }

    void UpdateStockpileVisual()
    {
        float hash = GetStock(ResourceNode.ResourceType.Berry) * 100 +
                     GetStock(ResourceNode.ResourceType.Wood) * 10 +
                     GetStock(ResourceNode.ResourceType.Stone) +
                     GetStock(ResourceNode.ResourceType.Meat) * 1000;

        // Only rebuild if stock changed meaningfully
        if (Mathf.Abs(hash - lastStockHash) < 0.5f) return;
        lastStockHash = hash;

        if (stockpileVisual != null) Object.Destroy(stockpileVisual.gameObject);

        float totalStock = TotalStock();
        if (totalStock < 0.5f) return;

        var stockObj = new GameObject("Stockpile");
        stockObj.transform.SetParent(transform);
        stockpileVisual = stockObj.transform;

        Vector3 right = GetRight();
        Vector3 fwd = Vector3.Cross(right, surfaceUp).normalized;

        // Wood pile — stacked logs to the right
        float wood = GetStock(ResourceNode.ResourceType.Wood);
        if (wood >= 1f)
        {
            Vector3 woodArea = transform.position - right * 1.2f;
            int logCount = Mathf.Min(Mathf.FloorToInt(wood), 8);
            for (int i = 0; i < logCount; i++)
            {
                var log = MakePrim(PrimitiveType.Cylinder, stockpileVisual);
                int row = i / 3;
                int col = i % 3;
                log.transform.position = woodArea + fwd * col * 0.15f + surfaceUp * (0.06f + row * 0.11f);
                log.transform.localScale = new Vector3(0.06f, 0.25f, 0.06f);
                log.transform.up = right;
                log.GetComponent<MeshRenderer>().material = woodMat;
            }
        }

        // Stone pile — stacked rocks to the left
        float stone = GetStock(ResourceNode.ResourceType.Stone);
        if (stone >= 1f)
        {
            Vector3 stoneArea = transform.position + right * 1.2f;
            int stoneCount = Mathf.Min(Mathf.FloorToInt(stone), 6);
            for (int i = 0; i < stoneCount; i++)
            {
                var rock = MakePrim(PrimitiveType.Sphere, stockpileVisual);
                float angle = i * 60f * Mathf.Deg2Rad;
                Vector3 offset = (right * Mathf.Cos(angle) + fwd * Mathf.Sin(angle)) * 0.15f * (i > 2 ? 0.5f : 1f);
                rock.transform.position = stoneArea + offset + surfaceUp * (0.08f + (i > 2 ? 0.12f : 0f));
                rock.transform.localScale = Vector3.one * 0.1f;
                rock.GetComponent<MeshRenderer>().material = stoneMat;
            }
        }

        // Food pile — berries near the fire
        float food = GetStock(ResourceNode.ResourceType.Berry);
        if (food >= 1f)
        {
            Vector3 foodArea = transform.position - fwd * 0.6f;
            int berryCount = Mathf.Min(Mathf.FloorToInt(food), 8);
            for (int i = 0; i < berryCount; i++)
            {
                var berry = MakePrim(PrimitiveType.Sphere, stockpileVisual);
                float angle = i * 45f * Mathf.Deg2Rad;
                Vector3 offset = (right * Mathf.Cos(angle) + fwd * Mathf.Sin(angle)) * 0.12f;
                berry.transform.position = foodArea + offset + surfaceUp * 0.05f;
                berry.transform.localScale = Vector3.one * 0.06f;
                berry.GetComponent<MeshRenderer>().material = foodMat;
            }
        }

        // Meat pile — chunks on the far side of the fire
        float meat = GetStock(ResourceNode.ResourceType.Meat);
        if (meat >= 1f)
        {
            Vector3 meatArea = transform.position + fwd * 0.6f;
            int chunkCount = Mathf.Min(Mathf.FloorToInt(meat), 6);
            for (int i = 0; i < chunkCount; i++)
            {
                var chunk = MakePrim(PrimitiveType.Cube, stockpileVisual);
                int row = i / 3;
                int col = i % 3;
                chunk.transform.position = meatArea + right * (col - 1) * 0.12f + surfaceUp * (0.06f + row * 0.08f);
                chunk.transform.up = surfaceUp;
                chunk.transform.localScale = new Vector3(0.09f, 0.05f, 0.09f);
                chunk.GetComponent<MeshRenderer>().material = meatMat;
            }
        }
    }

    GameObject MakePrim(PrimitiveType type, Transform parent)
    {
        var obj = GameObject.CreatePrimitive(type);
        Object.Destroy(obj.GetComponent<Collider>());
        obj.transform.SetParent(parent);
        return obj;
    }

    Vector3 GetRight()
    {
        Vector3 right = Vector3.Cross(surfaceUp, Vector3.forward).normalized;
        if (right.sqrMagnitude < 0.01f) right = Vector3.Cross(surfaceUp, Vector3.right).normalized;
        return right;
    }

    public bool CanUpgrade()
    {
        if (shelterLevel >= 3) return false;
        int next = shelterLevel + 1;
        return GetStock(ResourceNode.ResourceType.Wood) >= woodCost[next]
            && GetStock(ResourceNode.ResourceType.Stone) >= stoneCost[next];
    }

    public bool TryUpgrade()
    {
        if (!CanUpgrade()) return false;
        int next = shelterLevel + 1;
        stockpile[ResourceNode.ResourceType.Wood] -= woodCost[next];
        stockpile[ResourceNode.ResourceType.Stone] -= stoneCost[next];
        shelterLevel = next;
        BuildShelterVisual(); // starts a staged, real-time construction — see ConstructionTimeline
        UpdateStockpileVisual();
        Debug.Log($"[Camp] {ownerName} began building a shelter upgrade to level {shelterLevel}");
        // NOTE: the "completed" story log fires from CreatureBody.OnConstructionComplete once the
        // ConstructionTimeline actually finishes — not here, at the moment it merely begins.
        return true;
    }

    public void RestoreState(int level, float food, float wood, float stone, float meat, bool hasFirePit)
    {
        stockpile[ResourceNode.ResourceType.Berry] = food;
        stockpile[ResourceNode.ResourceType.Wood] = wood;
        stockpile[ResourceNode.ResourceType.Stone] = stone;
        stockpile[ResourceNode.ResourceType.Meat] = meat;
        shelterLevel = Mathf.Clamp(level, 0, 3);
        // A restored save is a continuing world — its shelter/fire already exist complete, no
        // multi-second construction replay.
        if (shelterLevel > 0) BuildShelterVisual(instant: true);
        if (hasFirePit) { HasFirePit = true; BuildFirePitVisual(instant: true); }
        UpdateStockpileVisual();
    }

    public float FoodStock()
    {
        return GetStock(ResourceNode.ResourceType.Berry) + GetStock(ResourceNode.ResourceType.Meat);
    }

    public string GetShelterName()
    {
        return shelterLevel switch
        {
            0 => "open ground",
            1 => "lean-to",
            2 => "hut",
            3 => "house",
            _ => "shelter"
        };
    }

    public void Deposit(ResourceNode.ResourceType type, float amount)
    {
        if (!stockpile.ContainsKey(type))
            stockpile[type] = 0f;
        stockpile[type] += amount;
        UpdateStockpileVisual();
    }

    public float GetStock(ResourceNode.ResourceType type)
    {
        return stockpile.TryGetValue(type, out float val) ? val : 0f;
    }

    public float TotalStock()
    {
        float total = 0f;
        foreach (var kv in stockpile) total += kv.Value;
        return total;
    }

    public float Steal(ResourceNode.ResourceType type, float amount)
    {
        if (!stockpile.ContainsKey(type)) return 0f;
        float taken = Mathf.Min(amount, stockpile[type]);
        stockpile[type] -= taken;
        UpdateStockpileVisual();
        return taken;
    }

    public bool IsOwnerNearby(float range = 15f)
    {
        var creatures = FindObjectsByType<CreatureMind>(FindObjectsSortMode.None);
        foreach (var c in creatures)
        {
            if (c.CreatureName == ownerName)
                return Vector3.Distance(c.transform.position, transform.position) < range;
        }
        return false;
    }
}
