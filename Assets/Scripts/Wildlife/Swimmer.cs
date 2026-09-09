using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ambient water wildlife — fish that swim just beneath the ocean surface, confined to water the same
/// way Critter is confined to land. Wanders loosely and darts away from anything that gets close.
/// </summary>
public class Swimmer : MonoBehaviour
{
    public static readonly List<Swimmer> All = new List<Swimmer>();
    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    // "Reload Domain" is disabled for fast/hang-free play iteration, so statics survive between play
    // sessions — clear this registry at the start of each one so we never carry stale entries.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() => All.Clear();

    [SerializeField] float speed = 3.2f;
    [SerializeField] float fleeRange = 7f;
    [SerializeField] float fleeMultiplier = 1.8f;
    [SerializeField] float depthMin = 0.4f;
    [SerializeField] float depthMax = 1.6f;
    [SerializeField] float bobAmount = 0.15f;
    [SerializeField] float bobSpeed = 1.2f;

    SphericalWorld world;
    SphericalTerrain terrain;
    Vector3 wanderTarget;
    float wanderTimer;
    Vector3 forward;
    float depth;
    float bobPhase;
    float threatScanTimer;
    Transform threat;
    float threatDist;

    public bool IsAlive { get; private set; } = true;

    /// <summary>Caught by a hunter — removes the fish. Returns the food value gained.</summary>
    public float Catch()
    {
        if (!IsAlive) return 0f;
        IsAlive = false;
        Destroy(gameObject);
        return 1f;
    }

    void Start()
    {
        world = SphericalWorld.Instance;
        if (world == null) return;
        terrain = world.Terrain;

        depth = Random.Range(depthMin, depthMax);
        bobPhase = Random.Range(0f, 100f);

        Vector3 up = world.GetSurfaceNormal(transform.position);
        forward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
        if (forward.sqrMagnitude < 0.01f)
            forward = Vector3.ProjectOnPlane(Vector3.right, up).normalized;

        PickTarget();
    }

    void Update()
    {
        if (world == null) return;
        Vector3 up = world.GetSurfaceNormal(transform.position);

        threatScanTimer -= Time.deltaTime;
        if (threatScanTimer <= 0f)
        {
            threatScanTimer = 0.3f;
            ScanThreat();
        }

        bool fleeing = threat != null && threatDist < fleeRange;

        Vector3 dir;
        if (fleeing)
        {
            dir = Vector3.ProjectOnPlane(transform.position - threat.position, up).normalized;
        }
        else
        {
            wanderTimer -= Time.deltaTime;
            if (wanderTimer <= 0f || Vector3.Distance(transform.position, wanderTarget) < 2f)
                PickTarget();
            dir = Vector3.ProjectOnPlane(wanderTarget - transform.position, up).normalized;
        }

        float s = speed * (fleeing ? fleeMultiplier : 1f);
        Vector3 next = transform.position + dir * s * Time.deltaTime;
        Vector3 nextDir = (next - world.Center).normalized;

        if (IsWater(nextDir))
        {
            transform.position = next;
            forward = Vector3.Slerp(forward, dir, Time.deltaTime * 5f);
        }
        else
        {
            PickTarget(); // hit the shoreline — turn back into open water
        }

        bobPhase += Time.deltaTime * bobSpeed;
        float wobble = Mathf.Sin(bobPhase) * bobAmount;
        Vector3 curDir = (transform.position - world.Center).normalized;
        transform.position = world.Center + curDir * (world.Radius - depth + wobble);
        transform.rotation = world.GetSurfaceRotation(transform.position, forward);
    }

    bool IsWater(Vector3 dir)
    {
        if (terrain == null) return true;
        return terrain.GetHeightAtDirection(dir) <= terrain.WaterLevel;
    }

    void ScanThreat()
    {
        threat = null;
        threatDist = float.MaxValue;
        var creatures = CreatureBody.All;
        for (int i = 0; i < creatures.Count; i++)
        {
            var c = creatures[i];
            float d = Vector3.Distance(transform.position, c.transform.position);
            if (d < threatDist)
            {
                threatDist = d;
                threat = c.transform;
            }
        }
    }

    void PickTarget()
    {
        wanderTarget = RandomWaterPoint();
        wanderTimer = Random.Range(3f, 8f);
    }

    Vector3 RandomWaterPoint()
    {
        if (world == null) return transform.position;
        if (terrain == null) return world.GetRandomSurfacePoint();

        for (int i = 0; i < 50; i++)
        {
            Vector3 dir = Random.onUnitSphere;
            if (IsWater(dir))
                return world.Center + dir * world.Radius;
        }
        return transform.position;
    }
}
