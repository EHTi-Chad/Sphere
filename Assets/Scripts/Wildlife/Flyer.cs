using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ambient flying wildlife — soars in slow, wide loops well above the terrain, banking through its
/// turns and flapping its wings. Never touches the ground, so it crosses land and water alike (unlike
/// Critter/Predator, which are surface-bound). Gives the sky life the way Critters give the ground life.
/// </summary>
public class Flyer : MonoBehaviour
{
    public static readonly List<Flyer> All = new List<Flyer>();
    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    // "Reload Domain" is disabled for fast/hang-free play iteration, so statics survive between play
    // sessions — clear this registry at the start of each one so we never carry stale entries.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() => All.Clear();

    [SerializeField] float speed = 5.5f;
    [SerializeField] float turnSpeed = 1.0f;
    [SerializeField] float altitudeMin = 9f;
    [SerializeField] float altitudeMax = 20f;
    [SerializeField] float bobAmount = 1.4f;
    [SerializeField] float bobSpeed = 0.5f;
    [SerializeField] float bankAmount = 40f;
    [SerializeField] float wingFlapSpeed = 7f;
    [SerializeField] float wingFlapAngle = 50f;

    SphericalWorld world;
    Vector3 wanderTarget;   // a direction (on the unit sphere) we're currently steering toward
    float wanderTimer;
    Vector3 forward;
    float baseAltitude;
    float bobPhase;
    float flapPhase;

    Transform leftWing;
    Transform rightWing;

    /// <summary>Wired up by the spawner right after creation so wing-flap animation has something to drive.</summary>
    public void SetWings(Transform left, Transform right)
    {
        leftWing = left;
        rightWing = right;
    }

    void Start()
    {
        world = SphericalWorld.Instance;
        if (world == null) return;

        baseAltitude = Random.Range(altitudeMin, altitudeMax);
        bobPhase = Random.Range(0f, 100f);
        flapPhase = Random.Range(0f, 100f);

        Vector3 up = world.GetSurfaceNormal(transform.position);
        forward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
        if (forward.sqrMagnitude < 0.01f)
            forward = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;

        PickTarget();
    }

    void Update()
    {
        if (world == null) return;

        Vector3 up = world.GetSurfaceNormal(transform.position);

        wanderTimer -= Time.deltaTime;
        if (wanderTimer <= 0f || Vector3.Angle(up, wanderTarget) < 5f)
            PickTarget();

        Vector3 steer = Vector3.ProjectOnPlane(wanderTarget - up, up).normalized;
        Vector3 prevForward = forward;
        forward = Vector3.Slerp(forward, steer, Time.deltaTime * turnSpeed).normalized;

        Vector3 next = transform.position + forward * speed * Time.deltaTime;

        bobPhase += Time.deltaTime * bobSpeed;
        float altitude = baseAltitude + Mathf.Sin(bobPhase) * bobAmount;
        transform.position = world.SnapToSurface(next, altitude);

        Quaternion levelRot = world.GetSurfaceRotation(transform.position, forward);

        // Bank into turns — roll around the forward axis proportional to how hard we're steering.
        float turnAmount = Vector3.SignedAngle(prevForward, forward, up);
        float bank = Mathf.Clamp(turnAmount * 6f, -bankAmount, bankAmount);
        transform.rotation = levelRot * Quaternion.Euler(0f, 0f, -bank);

        flapPhase += Time.deltaTime * wingFlapSpeed;
        float flap = Mathf.Sin(flapPhase) * wingFlapAngle;
        if (leftWing != null) leftWing.localRotation = Quaternion.Euler(0f, 0f, flap);
        if (rightWing != null) rightWing.localRotation = Quaternion.Euler(0f, 0f, -flap);
    }

    void PickTarget()
    {
        wanderTarget = Random.onUnitSphere;
        wanderTimer = Random.Range(6f, 14f);
    }
}
