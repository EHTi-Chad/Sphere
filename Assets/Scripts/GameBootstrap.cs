using UnityEngine;

public class GameBootstrap : MonoBehaviour
{
    [Header("World")]
    [SerializeField] int seed = 42;
    [SerializeField] float worldRadius = 50f;

    [Header("Creatures")]
    [SerializeField] int creatureCount = 10;

    [Header("Lighting")]
    [SerializeField] Color ambientColor = new Color(0.3f, 0.35f, 0.4f);

    GameObject worldObj;
    GameObject systemsObj;
    bool worldGenerated;

    void Awake()
    {
        SetupCamera();
        SetupLight();
        SetupStarfield();
        SetupMenu();

        Time.timeScale = 0f;
    }

    void SetupMenu()
    {
        var menuObj = new GameObject("_Menu");
        menuObj.AddComponent<MainMenu>();
    }

    public void GenerateWorld(int newSeed, float newRadius = 50f)
    {
        seed = newSeed;
        worldRadius = newRadius;

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
        terrain.Generate();

        systemsObj = new GameObject("_Systems");
        systemsObj.AddComponent<OllamaClient>();
        systemsObj.AddComponent<CognitionScheduler>();
        systemsObj.AddComponent<GodEventBus>();
        systemsObj.AddComponent<CreatureSpawner>();

        worldGenerated = true;
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
        cam.gameObject.AddComponent<DebugOverlay>();
        cam.farClipPlane = 2000f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
    }

    void SetupStarfield()
    {
        var starObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
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
