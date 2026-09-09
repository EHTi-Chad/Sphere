using UnityEngine;

public class DayNightCycle : MonoBehaviour
{
    public static DayNightCycle Instance { get; private set; }

    [Header("Cycle")]
    [SerializeField] float dayLengthSeconds = 300f; // 5 min/day — a real day/night rhythm across a ~30-45 min session
    // Start just after dawn (not noon) so newly-arrived creatures get a full day of light to
    // gather, build a shelter, and light a fire before their first dangerous night.
    [SerializeField] float startTimeOfDay = 0.05f;

    [Header("Sun Visual")]
    [SerializeField] float sunDistance = 600f;
    [SerializeField] float sunSize = 40f;

    [Header("Lighting")]
    [SerializeField] float dayIntensity = 1.5f;
    [SerializeField] float nightIntensity = 0.02f;
    [SerializeField] Color dayAmbient = new Color(0.35f, 0.38f, 0.45f);
    [SerializeField] Color nightAmbient = new Color(0.02f, 0.02f, 0.05f);
    [SerializeField] float axialTilt = 23.5f;

    float timeOfDay;
    Light sunLight;
    GameObject sunVisual;
    Vector3 sunDirection;

    float daylight = 1f; // 0 = night, 1 = full day (shared by scene lighting + creature labels)

    public float DayLengthSeconds => dayLengthSeconds;
    public float TimeOfDay => timeOfDay;
    public Vector3 SunDirection => sunDirection;
    public float DaylightFactor => daylight;
    public bool IsDay => daylight > 0.35f;

    public void SetTimeOfDay(float t)
    {
        timeOfDay = Mathf.Repeat(t, 1f);
        UpdateCycle();
    }

    void Awake()
    {
        Instance = this;
        timeOfDay = startTimeOfDay;
    }

    public void Setup(Light directionalLight)
    {
        sunLight = directionalLight;
        CreateSunVisual();
        UpdateCycle();
    }

    public float SunDistance => sunDistance;

    /// <summary>
    /// Place the sun far out so it reads as a distant star rather than a lamp over the planet.
    /// Size scales with distance to keep a constant, small angular size (~1.4°, sun-like). The
    /// starfield and camera far-clip (set by GameBootstrap) are sized off SunDistance to contain it.
    /// </summary>
    public void ConfigureForRadius(float r)
    {
        sunDistance = Mathf.Max(10000f, r * 10f);
        sunSize = sunDistance * 0.025f;
        if (sunVisual != null) sunVisual.transform.localScale = Vector3.one * sunSize;
        UpdateCycle();
    }

    void CreateSunVisual()
    {
        sunVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sunVisual.name = "Sun";
        Object.Destroy(sunVisual.GetComponent<Collider>());
        sunVisual.transform.localScale = Vector3.one * sunSize;

        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader != null)
        {
            var sunMaterial = new Material(shader);
            sunMaterial.color = new Color(1f, 0.95f, 0.7f);
            sunVisual.GetComponent<MeshRenderer>().material = sunMaterial;
        }

        var glowObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        glowObj.name = "SunGlow";
        Object.Destroy(glowObj.GetComponent<Collider>());
        glowObj.transform.SetParent(sunVisual.transform);
        glowObj.transform.localPosition = Vector3.zero;
        glowObj.transform.localScale = Vector3.one * 2.5f;

        if (shader != null)
        {
            var glowMaterial = new Material(shader);
            glowMaterial.color = new Color(1f, 0.9f, 0.5f, 0.15f);
            glowMaterial.SetFloat("_Surface", 1f);
            glowMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            glowMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            glowMaterial.SetInt("_ZWrite", 0);
            glowMaterial.renderQueue = 3000;
            glowObj.GetComponent<MeshRenderer>().material = glowMaterial;
        }
    }

    void Update()
    {
        timeOfDay += Time.deltaTime / dayLengthSeconds;
        if (timeOfDay >= 1f) timeOfDay -= 1f;

        UpdateCycle();
    }

    void UpdateCycle()
    {
        if (sunLight == null) return;

        float angle = timeOfDay * Mathf.PI * 2f;
        float tiltRad = axialTilt * Mathf.Deg2Rad;

        sunDirection = new Vector3(
            Mathf.Cos(angle),
            Mathf.Sin(angle),
            Mathf.Sin(angle) * Mathf.Sin(tiltRad)
        ).normalized;

        Vector3 sunPos = sunDirection * sunDistance;

        sunLight.transform.position = sunPos;
        sunLight.transform.rotation = Quaternion.LookRotation(-sunDirection);

        float sunHeight = sunDirection.y;

        float t;
        if (sunHeight > 0.1f)
            t = 1f;
        else if (sunHeight < -0.1f)
            t = 0f;
        else
            t = (sunHeight + 0.1f) / 0.2f;

        daylight = t;
        float weather = WeatherSystem.Instance != null ? WeatherSystem.Instance.LightMultiplier : 1f;
        sunLight.intensity = Mathf.Lerp(nightIntensity, dayIntensity, t) * weather;

        Color sunColor;
        if (t > 0.5f)
            sunColor = Color.Lerp(new Color(1f, 0.7f, 0.3f), new Color(1f, 0.95f, 0.85f), (t - 0.5f) * 2f);
        else
            sunColor = Color.Lerp(new Color(0.3f, 0.2f, 0.15f), new Color(1f, 0.7f, 0.3f), t * 2f);
        sunLight.color = sunColor;

        RenderSettings.ambientLight = Color.Lerp(nightAmbient, dayAmbient, t) * weather;

        if (sunVisual != null)
        {
            sunVisual.transform.position = sunPos;
            sunVisual.SetActive(sunHeight > -0.3f);
        }
    }

    public float GetSunExposure(Vector3 worldPosition)
    {
        Vector3 surfaceNormal = SphericalWorld.Instance != null
            ? SphericalWorld.Instance.GetSurfaceNormal(worldPosition)
            : worldPosition.normalized;
        float dot = Vector3.Dot(surfaceNormal, sunDirection);
        return Mathf.Clamp01((dot + 0.15f) / 0.65f);
    }

    public bool IsDaytime(Vector3 worldPosition)
    {
        return GetSunExposure(worldPosition) > 0.3f;
    }
}
