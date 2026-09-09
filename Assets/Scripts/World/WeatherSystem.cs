using UnityEngine;

/// <summary>
/// Drives planetary weather: cycles Clear → Cloudy → Rain → Storm over time, dims the sunlight
/// when overcast, flashes lightning during storms, falls rain near the camera, and exposes
/// Wetness/IsStormy so creatures (exposure + AI) react. Self-contained; created by GameBootstrap.
/// </summary>
public class WeatherSystem : MonoBehaviour
{
    public static WeatherSystem Instance { get; private set; }

    public enum Weather { Clear, Cloudy, Rain, Storm }
    public Weather Current { get; private set; } = Weather.Clear;

    [SerializeField] float minStateSeconds = 45f;
    [SerializeField] float maxStateSeconds = 110f;

    float stateTimer;
    float dim = 1f;        // overcast dimming (lerped)
    float flash;           // lightning flash (decays 1→0)
    float lightningTimer;

    ParticleSystem rain;
    GameObject rainObj;

    // Localized rain "cell" — a patch over one region of the surface that drifts as it rains.
    [SerializeField] float patchSize = 42f;   // width of the downpour patch (world units)
    [SerializeField] float cellHeight = 26f;  // how high above the surface rain starts
    [SerializeField] float driftSpeed = 4f;   // deg/sec the storm moves across the planet
    Vector3 cellDir = Vector3.up;             // surface direction the cell sits over
    Vector3 driftAxis = Vector3.forward;

    // ---- Read by other systems ----
    /// 0 = dry, up to 1 = soaked (storm). Raises creature exposure when unsheltered.
    public float Wetness => Current == Weather.Storm ? 1f : Current == Weather.Rain ? 0.6f : 0f;
    public bool IsStormy => Current == Weather.Storm;
    public bool IsRaining => Current == Weather.Rain || Current == Weather.Storm;
    /// Multiplier DayNightCycle applies to sun/ambient (dimmer when overcast, bright spike on lightning).
    public float LightMultiplier => Mathf.Clamp(dim + flash * 1.4f, 0.2f, 2.2f);

    public string Label => Current switch
    {
        Weather.Clear => "Clear",
        Weather.Cloudy => "Cloudy",
        Weather.Rain => "Rain",
        Weather.Storm => "Storm",
        _ => "Clear"
    };

    void Awake()
    {
        Instance = this;
        CreateRain();
        PickNextWeather(true);
    }

    void OnDestroy()
    {
        // Rain is parented to the camera, so it must be cleaned up explicitly on regenerate.
        if (rainObj != null) Destroy(rainObj);
    }

    /// <summary>StoryDirector's Crisis stage calls this to force a real storm for a set duration,
    /// overriding the normal random cycle — a dramatic, mechanically-real hardship, not just a label.</summary>
    public void ForceStorm(float duration)
    {
        Current = Weather.Storm;
        stateTimer = duration;
        if (lightningTimer <= 0f) lightningTimer = Random.Range(2f, 6f);

        var cam = Camera.main;
        cellDir = cam != null ? cam.transform.position.normalized : Random.onUnitSphere;
        if (cellDir.sqrMagnitude < 0.0001f) cellDir = Random.onUnitSphere;
        driftAxis = Vector3.Cross(cellDir, Random.onUnitSphere).normalized;
    }

    void Update()
    {
        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f) PickNextWeather(false);

        // Smoothly approach this weather's brightness.
        float target = Current switch
        {
            Weather.Clear => 1f,
            Weather.Cloudy => 0.72f,
            Weather.Rain => 0.55f,
            Weather.Storm => 0.4f,
            _ => 1f
        };
        dim = Mathf.Lerp(dim, target, Time.deltaTime * 0.4f);

        // Lightning flashes during storms.
        flash = Mathf.Max(0f, flash - Time.deltaTime * 6f);
        if (Current == Weather.Storm)
        {
            lightningTimer -= Time.deltaTime;
            if (lightningTimer <= 0f)
            {
                flash = 1f;
                lightningTimer = Random.Range(3f, 10f);
            }
        }

        UpdateRainCell();
    }

    // Positions the rain patch over a drifting point on the surface and aims it down at the ground,
    // so rain falls over one region instead of the whole view.
    void UpdateRainCell()
    {
        if (rain == null) return;
        var emission = rain.emission;

        if (!IsRaining)
        {
            emission.rateOverTime = 0f;
            return;
        }

        float r = SphericalWorld.Instance != null ? SphericalWorld.Instance.Radius : 50f;

        if (driftAxis.sqrMagnitude < 0.0001f) driftAxis = Vector3.up;
        cellDir = (Quaternion.AngleAxis(driftSpeed * Time.deltaTime, driftAxis) * cellDir).normalized;

        rainObj.transform.position = cellDir * (r + cellHeight);
        rainObj.transform.rotation = Quaternion.LookRotation(-cellDir); // emit down toward the surface

        emission.rateOverTime = Current == Weather.Storm ? 1600f : 700f;
    }

    void PickNextWeather(bool first)
    {
        // A freshly-started world gets a longer guaranteed clear spell — matches the predators'
        // opening grace period, so the very first minutes aren't storm + danger at once.
        stateTimer = first ? Random.Range(180f, 260f) : Random.Range(minStateSeconds, maxStateSeconds);
        if (first) { Current = Weather.Clear; return; }

        // Weighted pick — clear/cloudy common, storms rare.
        float r = Random.value;
        Current = r < 0.40f ? Weather.Clear
                : r < 0.72f ? Weather.Cloudy
                : r < 0.90f ? Weather.Rain
                : Weather.Storm;

        if (lightningTimer <= 0f) lightningTimer = Random.Range(2f, 6f);

        // When rain begins, drop the storm cell over the region currently in view, then let it drift.
        if (IsRaining)
        {
            var cam = Camera.main;
            cellDir = cam != null ? cam.transform.position.normalized : Random.onUnitSphere;
            if (cellDir.sqrMagnitude < 0.0001f) cellDir = Random.onUnitSphere;
            driftAxis = Vector3.Cross(cellDir, Random.onUnitSphere).normalized;
        }
    }

    void CreateRain()
    {
        // World-space cell — positioned over a drifting surface point each frame (see UpdateRainCell).
        rainObj = new GameObject("RainFX");

        rain = rainObj.AddComponent<ParticleSystem>();

        var main = rain.main;
        main.loop = true;
        main.startLifetime = 1.6f;
        main.startSpeed = 28f;
        main.startSize = 0.18f;
        main.startColor = new Color(0.72f, 0.8f, 0.95f, 0.5f);
        main.maxParticles = 6000;
        main.simulationSpace = ParticleSystemSimulationSpace.World; // rain streaks in world space as it falls
        main.gravityModifier = 0f;

        var emission = rain.emission;
        emission.rateOverTime = 0f;

        var shape = rain.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(patchSize, patchSize, 1f); // a localized patch, not the whole view

        var renderer = rain.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 3.5f;
        renderer.velocityScale = 0.12f;

        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                  ?? Shader.Find("Sprites/Default");
        if (shader != null)
        {
            var mat = new Material(shader) { color = new Color(0.72f, 0.8f, 0.95f, 0.5f) };
            mat.SetFloat("_Surface", 1f); // transparent so rain streaks blend instead of being solid quads
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;
            renderer.material = mat;
        }
    }
}
