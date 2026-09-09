using System.Collections;
using UnityEngine;

public class GameBootstrap : MonoBehaviour
{
    [Header("World")]
    [SerializeField] int seed = 42;
    [SerializeField] float worldRadius = 120f;

    [Header("Creatures")]
    [SerializeField] int creatureCount = 12;

    [Header("Lighting")]
    [SerializeField] Color ambientColor = new Color(0.3f, 0.35f, 0.4f);

    GameObject worldObj;
    GameObject systemsObj;
    GameObject starfieldObj;
    bool worldGenerated;

    public bool IsWorldGenerated => worldGenerated;
    public int Seed => seed;
    public float WorldRadius => worldRadius;

    // Polled by MainMenu to drive the "generating world" progress bar. Generation runs as a
    // coroutine (not a plain call) purely so the menu gets a chance to render a status label/bar
    // between stages — terrain.Generate() itself is still one big blocking call internally (that's
    // the risky mesh-gen hot path that caused the subdivision hang earlier; not touching it here).
    public bool IsGenerating { get; private set; }
    public string GenerationStatus { get; private set; } = "";
    public float GenerationProgress01 { get; private set; }

    void Awake()
    {
        SetupCamera();

        try { SetupLight(); }
        catch (System.Exception e) { Debug.LogError($"[Bootstrap] SetupLight failed: {e.Message}"); }

        try { SetupStarfield(); }
        catch (System.Exception e) { Debug.LogError($"[Bootstrap] SetupStarfield failed: {e.Message}"); }

        SetupMenu();

        Time.timeScale = 0f;
    }

    void SetupMenu()
    {
        var menuObj = new GameObject("_Menu");
        menuObj.AddComponent<MainMenu>();
    }

    public void GenerateWorld(int newSeed, float newRadius = 120f, int newCreatureCount = 12)
    {
        StartCoroutine(GenerateWorldRoutine(newSeed, newRadius, newCreatureCount));
    }

    IEnumerator GenerateWorldRoutine(int newSeed, float newRadius, int newCreatureCount)
    {
        IsGenerating = true;
        GenerationStatus = "Preparing...";
        GenerationProgress01 = 0.02f;
        yield return null; // let the menu draw this state before any heavy work starts

        seed = newSeed;
        worldRadius = newRadius;
        creatureCount = newCreatureCount;

        if (worldGenerated)
        {
            if (worldObj != null) Destroy(worldObj);
            if (systemsObj != null) Destroy(systemsObj);

            var oldCreatures = FindObjectsByType<CreatureMind>(FindObjectsSortMode.None);
            foreach (var c in oldCreatures)
                Destroy(c.gameObject);
        }

        worldObj = new GameObject("World");
        worldObj.transform.position = Vector3.zero;

        var world = worldObj.AddComponent<SphericalWorld>();
        world.SetRadius(worldRadius);

        var terrain = worldObj.AddComponent<SphericalTerrain>();
        terrain.SetRadius(worldRadius);

        // The single most expensive step (mesh subdivision + noise sampling + hydrology) — one
        // blocking call, no sub-progress inside it. Setting status/progress and yielding first at
        // least guarantees the menu renders "Building terrain..." before the freeze, instead of
        // going straight from a click to silence.
        GenerationStatus = "Building terrain...";
        GenerationProgress01 = 0.1f;
        yield return null;
        terrain.Generate();

        GenerationStatus = "Growing vegetation...";
        GenerationProgress01 = 0.55f;
        yield return null;
        var decorator = worldObj.AddComponent<SurfaceDecorator>();
        decorator.Generate(terrain, seed);

        GenerationStatus = "Placing resources...";
        GenerationProgress01 = 0.68f;
        yield return null;
        var resources = worldObj.AddComponent<ResourceSpawner>();
        resources.Generate(terrain, seed);

        GenerationStatus = "Releasing wildlife...";
        GenerationProgress01 = 0.78f;
        yield return null;
        var critters = worldObj.AddComponent<CritterSpawner>();
        critters.Generate(terrain, seed);

        var predators = worldObj.AddComponent<PredatorSpawner>();
        predators.Generate(terrain, seed);

        var flyers = worldObj.AddComponent<FlyerSpawner>();
        flyers.Generate(terrain, seed);

        var swimmers = worldObj.AddComponent<SwimmerSpawner>();
        swimmers.Generate(terrain, seed);

        GenerationStatus = "Waking systems...";
        GenerationProgress01 = 0.9f;
        yield return null;

        // Persistent LLM backend — survives Quit-to-Menu / New Game (expensive to load, and disposing
        // it mid-inference is a native crash). Created once, on its own object, reused every game.
        if (OllamaClient.Instance == null)
        {
            var llmObj = new GameObject("_LLM");
            llmObj.AddComponent<OllamaClient>();
        }

        systemsObj = new GameObject("_Systems");
        systemsObj.AddComponent<CognitionScheduler>();
        systemsObj.AddComponent<GodEventBus>();
        systemsObj.AddComponent<WeatherSystem>();
        systemsObj.AddComponent<StoryDirector>();
        var spawner = systemsObj.AddComponent<CreatureSpawner>();
        spawner.SetCount(creatureCount);

        GenerationStatus = "Finalizing...";
        GenerationProgress01 = 0.97f;
        yield return null;

        // Scale the camera's zoom range to the planet size so it never clips inside.
        if (OrbitalCamera.Instance != null)
            OrbitalCamera.Instance.ConfigureForRadius(worldRadius);

        // Scale the sky to the planet: sun far out (distant-star look), starfield beyond the sun,
        // far-clip beyond that — so the sun stays inside the stars and nothing gets clipped.
        if (DayNightCycle.Instance != null)
            DayNightCycle.Instance.ConfigureForRadius(worldRadius);

        float sunDist = DayNightCycle.Instance != null ? DayNightCycle.Instance.SunDistance : 10000f;
        if (starfieldObj != null)
            starfieldObj.transform.localScale = Vector3.one * (sunDist * 2.8f); // starfield radius = sunDist * 1.4
        if (Camera.main != null)
            Camera.main.farClipPlane = Mathf.Max(2000f, sunDist * 2.5f);

        worldGenerated = true;
        GenerationProgress01 = 1f;
        IsGenerating = false;
    }

    /// <summary>Regenerate the world from a save, then restore creatures/time.</summary>
    public void LoadGame(SaveData data)
    {
        if (data == null) return;

        SaveSystem.PendingLoad = data; // CreatureSpawner consumes this instead of random-spawning
        StartCoroutine(LoadGameRoutine(data));
    }

    IEnumerator LoadGameRoutine(SaveData data)
    {
        // Must wait for the world to actually finish generating before restoring time-of-day —
        // GenerateWorldRoutine now spans several frames instead of returning immediately.
        yield return GenerateWorldRoutine(data.seed, data.radius, 12);

        if (DayNightCycle.Instance != null)
            DayNightCycle.Instance.SetTimeOfDay(data.timeOfDay);
    }

    /// <summary>Tear down the current world and return to a clean main menu.</summary>
    public void UnloadWorld()
    {
        if (worldObj != null) Destroy(worldObj);
        if (systemsObj != null) Destroy(systemsObj);

        foreach (var c in FindObjectsByType<CreatureMind>(FindObjectsSortMode.None))
            Destroy(c.gameObject);
        foreach (var camp in FindObjectsByType<CreatureCamp>(FindObjectsSortMode.None))
            Destroy(camp.gameObject);

        worldGenerated = false;
    }

    void SetupCamera()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            var camObj = new GameObject("Main Camera");
            cam = camObj.AddComponent<Camera>();
            camObj.tag = "MainCamera";
        }
        cam.gameObject.AddComponent<OrbitalCamera>();
        cam.gameObject.AddComponent<CreatureInfoUI>();
        cam.gameObject.AddComponent<CreatureBubbleOverlay>();
        cam.gameObject.AddComponent<DebugOverlay>();
        cam.farClipPlane = 2000f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
    }

    void SetupStarfield()
    {
        var starObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        starfieldObj = starObj;
        starObj.name = "Starfield";
        Object.Destroy(starObj.GetComponent<Collider>());
        starObj.transform.position = Vector3.zero;
        starObj.transform.localScale = Vector3.one * 1500f;

        var shader = Shader.Find("Custom/Starfield");
        if (shader != null)
        {
            var mat = new Material(shader);
            starObj.GetComponent<MeshRenderer>().material = mat;
        }
    }

    void SetupLight()
    {
        Light light;
        var existing = FindFirstObjectByType<Light>();
        if (existing == null)
        {
            var lightObj = new GameObject("SunLight");
            light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.color = new Color(1f, 0.95f, 0.85f);
            lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }
        else
        {
            light = existing;
        }

        light.shadows = LightShadows.Soft;

        var cycleObj = new GameObject("_DayNightCycle");
        var cycle = cycleObj.AddComponent<DayNightCycle>();
        cycle.Setup(light);

        RenderSettings.ambientLight = ambientColor;
    }
}
