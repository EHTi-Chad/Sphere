using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ambient wildlife. Wanders the surface and flees when a creature gets close.
/// Gives the world a sense of life; can be wired into the HUNT goal later.
/// </summary>
public class Critter : MonoBehaviour
{
    public static readonly List<Critter> All = new List<Critter>();
    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    // "Reload Domain" is disabled for fast/hang-free play iteration, so statics survive between play
    // sessions — clear this registry at the start of each one so we never carry stale entries.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() => All.Clear();

    [SerializeField] float speed = 1.95f; // scaled down ~25% with the rest of the slowdown pass
    [SerializeField] float fleeRange = 9f;
    [SerializeField] float heightOffset = 0.3f;
    [SerializeField] float fleeMultiplier = 1.6f;

    SphericalWorld world;
    Vector3 wanderTarget;
    float wanderTimer;
    Vector3 forward;
    float threatScanTimer;
    Transform threat;
    float threatDist;

    public bool IsAlive { get; private set; } = true;

    /// <summary>Caught by a hunter — removes the critter. Returns the food value gained.</summary>
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

        transform.position = world.SnapToSurface(transform.position, heightOffset);
        forward = Vector3.ProjectOnPlane(transform.forward, world.GetSurfaceNormal(transform.position)).normalized;
        PickTarget();
    }

    void Update()
    {
        if (world == null) return;

        Vector3 up = world.GetSurfaceNormal(transform.position);

        // Re-scan for nearby creatures a few times a second (not every frame).
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

        if (world.IsLand(next))
        {
            transform.position = next;
            forward = Vector3.Slerp(forward, dir, Time.deltaTime * 6f);
        }
        else
        {
            PickTarget();
        }

        transform.position = world.SnapToSurface(transform.position, heightOffset);
        transform.rotation = world.GetSurfaceRotation(transform.position, forward);
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
        wanderTarget = world.SnapToSurface(world.GetRandomLandPoint());
        wanderTimer = Random.Range(3f, 8f);
    }
}
