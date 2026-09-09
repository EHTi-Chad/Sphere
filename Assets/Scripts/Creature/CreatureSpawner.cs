using System.Collections.Generic;
using UnityEngine;

public class CreatureSpawner : MonoBehaviour
{
    [SerializeField] int creatureCount = 10;
    [SerializeField] float creatureScale = 1f;
    [SerializeField] Color[] creatureColors;

    [Header("Arrival pacing")]
    [Tooltip("How many creatures appear immediately on a new game — enough that the world isn't empty, few enough that it doesn't feel instantly 'fully staffed'.")]
    [SerializeField] int foundingCount = 3;
    [Tooltip("The rest trickle in one at a time, spread out over roughly this many seconds — a new world eases into existence instead of dropping the player into a fully populated, fully active society all at once.")]
    [SerializeField] float targetArrivalWindowSeconds = 280f;
    [SerializeField] float minArrivalGap = 8f;
    [SerializeField] float maxArrivalGap = 35f;

    static readonly string[] Names = {
        "Sena", "Mira", "Tomas", "Liora", "Bohl",
        "Kael", "Yuna", "Drex", "Vala", "Fen",
        "Nira", "Orik", "Zaya", "Pim", "Asha",
        "Rhen", "Dova", "Luk", "Thessa", "Grin"
    };

    public static CreatureSpawner Instance { get; private set; }
    int childSerial;

    struct PendingArrival
    {
        public string name;
        public PersonalityType personality;
        public Color color;
        public Vector3 position;
    }
    readonly Queue<PendingArrival> pendingArrivals = new Queue<PendingArrival>();
    float arrivalTimer;
    float arrivalInterval;

    public void SetCount(int c) { creatureCount = Mathf.Max(1, c); }

    void Awake() { Instance = this; }

    /// <summary>Spawn a newborn at a parent's camp. Reuses the normal creature setup.</summary>
    public void SpawnChild(Vector3 position, PersonalityType personality, Color color, string parentName)
    {
        SpawnCreature(GenerateChildName(parentName), personality, color, position);
    }

    string GenerateChildName(string parentName)
    {
        // Prefer an unused base name so newborns feel like distinct individuals.
        foreach (var n in Names)
        {
            bool used = false;
            var all = CreatureMind.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].CreatureName == n) { used = true; break; }
            if (!used) return n;
        }
        childSerial++;
        return childSerial > 1 ? $"{parentName} Jr. {childSerial}" : $"{parentName} Jr.";
    }

    void Start()
    {
        var world = SphericalWorld.Instance;
        if (world == null)
        {
            Debug.LogError("SphericalWorld not found!");
            return;
        }

        EnsureColors();

        if (SaveSystem.PendingLoad != null)
        {
            var data = SaveSystem.PendingLoad;
            SaveSystem.PendingLoad = null;
            SpawnFromSave(data);
        }
        else
        {
            SpawnRandom(world);
        }
    }

    void Update()
    {
        if (pendingArrivals.Count == 0) return;

        arrivalTimer -= Time.deltaTime;
        if (arrivalTimer > 0f) return;
        arrivalTimer = arrivalInterval;

        var next = pendingArrivals.Dequeue();
        var go = SpawnCreature(next.name, next.personality, next.color, next.position);

        // Announce the arrival the same way creatures announce any big life event — a speech bubble
        // everyone can see, plus a strong pull on the idle camera tour (see
        // OrbitalCamera.PickInterestingCreature, which weighs BirthTime) — something for the player
        // to actually notice and follow, instead of a new dot just quietly appearing on the globe.
        go.GetComponent<CreatureMind>()?.Say("I have arrived in this world...");
    }

    void EnsureColors()
    {
        if (creatureColors == null || creatureColors.Length == 0)
            creatureColors = new[] {
                new Color(0.9f, 0.4f, 0.3f),
                new Color(0.3f, 0.7f, 0.9f),
                new Color(0.4f, 0.9f, 0.4f),
                new Color(0.9f, 0.8f, 0.3f),
                new Color(0.7f, 0.4f, 0.9f)
            };
    }

    void SpawnRandom(SphericalWorld world)
    {
        int total = creatureCount;
        int founding = Mathf.Clamp(foundingCount, 1, total);

        // The roster (who, and where they land) is decided up front — still fully seed-deterministic —
        // but only the founding wave is instantiated right now. Everyone else queues up to arrive
        // gradually via Update() above.
        var recipes = new List<PendingArrival>(total);
        int personalityCount = System.Enum.GetValues(typeof(PersonalityType)).Length;
        for (int i = 0; i < total; i++)
        {
            recipes.Add(new PendingArrival
            {
                name = Names[i % Names.Length],
                personality = (PersonalityType)(i % personalityCount),
                color = creatureColors[i % creatureColors.Length],
                position = world.GetRandomLandPoint()
            });
        }

        for (int i = 0; i < founding; i++)
            SpawnCreature(recipes[i].name, recipes[i].personality, recipes[i].color, recipes[i].position);

        for (int i = founding; i < total; i++)
            pendingArrivals.Enqueue(recipes[i]);

        int remaining = total - founding;
        arrivalInterval = remaining > 0
            ? Mathf.Clamp(targetArrivalWindowSeconds / remaining, minArrivalGap, maxArrivalGap)
            : 0f;
        arrivalTimer = arrivalInterval;
    }

    void SpawnFromSave(SaveData data)
    {
        for (int i = 0; i < data.creatures.Count; i++)
        {
            var cs = data.creatures[i];
            Vector3 pos = new Vector3(cs.posX, cs.posY, cs.posZ);
            // A restored save is a continuing world, not a new one — everyone reappears at once,
            // instantly (no materialize-in animation, no staggering).
            var creature = SpawnCreature(cs.name, (PersonalityType)cs.personality, creatureColors[i % creatureColors.Length], pos, animateIn: false);

            var mind = creature.GetComponent<CreatureMind>();
            var body = creature.GetComponent<CreatureBody>();
            if (mind != null) mind.ApplySave(cs);
            if (body != null) body.PrimeFromSave(cs);
        }

        Debug.Log($"[Spawner] Restored {data.creatures.Count} creatures from save");
    }

    GameObject SpawnCreature(string name, PersonalityType personality, Color color, Vector3 position, bool animateIn = true)
    {
        Vector3 up = SphericalWorld.Instance.GetSurfaceNormal(position);

        GameObject creature = CreateCreatureMesh();
        creature.name = name;
        creature.transform.position = position;
        creature.transform.up = up;
        creature.transform.localScale = Vector3.one * creatureScale;

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader != null)
        {
            // Colour the whole creature (body + head), not just the first renderer found.
            var mat = new Material(shader) { color = color };
            foreach (var r in creature.GetComponentsInChildren<MeshRenderer>())
                r.sharedMaterial = mat;
        }

        // Materialize in rather than instantly popping into existence — a visible "arriving" moment.
        if (animateIn) creature.AddComponent<GrowIn>();

        var mind = creature.AddComponent<CreatureMind>();
        mind.SetIdentity(name, personality);

        var body = creature.AddComponent<CreatureBody>();
        creature.AddComponent<CreatureAnimator>();
        creature.AddComponent<CreatureCarryVisual>();

        if (CognitionScheduler.Instance != null)
            CognitionScheduler.Instance.Register(mind, body);

        return creature;
    }

    GameObject CreateCreatureMesh()
    {
        var root = new GameObject();

        var bodyObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        bodyObj.transform.SetParent(root.transform);
        bodyObj.transform.localPosition = Vector3.zero;
        bodyObj.transform.localScale = new Vector3(0.6f, 1.0f, 0.6f);

        var headObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        headObj.transform.SetParent(root.transform);
        headObj.transform.localPosition = new Vector3(0f, 1.25f, 0.2f);
        headObj.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);

        return root;
    }
}
