using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Apex predator — prowls the surface, stalks the nearest creature (or a critter if none is near),
/// chases it down, and mauls what it catches. The "watch out for" tier above passive Critters.
/// Creatures detect it and flee; repeated maulings can kill. Land animal — avoids water like critters.
/// </summary>
public class Predator : MonoBehaviour
{
    public static readonly List<Predator> All = new List<Predator>();
    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    // "Reload Domain" is disabled for fast/hang-free play iteration, so statics survive between play
    // sessions — clear this registry at the start of each one so we never carry stale entries.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() => All.Clear();

    // Scaled down by the same ~25% as CreatureBody.moveSpeed (part of the broader slowdown pass) so
    // the flee-vs-chase ratio that was tuned earlier is preserved exactly — a fleeing creature (now
    // ~4.8) is still just as escapable relative to this.
    [SerializeField] float wanderSpeed = 2.25f;
    [SerializeField] float chaseSpeed = 4.0f;   // under a fleeing creature (~4.8) — losable in open ground
    [SerializeField] float detectRange = 30f;
    [SerializeField] float attackRange = 1.8f;
    [SerializeField] float attackCooldown = 3f;
    [SerializeField] float attackDamage = 0.12f; // not an instant kill — wounds, so prey can escape & heal

    [Header("Give up & rest")]
    [Tooltip("Stop chasing creatures after this long of sustained pursuit without landing a hit — a real predator tires.")]
    [SerializeField] float giveUpChaseTime = 9f;
    [Tooltip("Also give up if prey manages to pull this far away.")]
    [SerializeField] float loseInterestRange = 45f;
    [Tooltip("After giving up, leave creatures alone for this long (still may hunt critters) so the population gets a real break.")]
    [SerializeField] float restDuration = 18f;

    [SerializeField] float heightOffset = 0.45f;

    SphericalWorld world;
    Vector3 wanderTarget;
    float wanderTimer;
    Vector3 forward;
    float scanTimer;
    float attackTimer;
    float chaseTimer;  // sustained time spent pursuing creature-type prey, across target switches
    float restTimer;   // while > 0, won't acquire new creature prey (still may hunt critters)

    Transform prey;        // a creature, the preferred quarry
    Critter preyCritter;   // fallback small prey when no creature is in range

    public bool IsHunting => prey != null || preyCritter != null;

    /// <summary>
    /// Called by the spawner right after creation to keep a freshly-spawned predator passive (still
    /// wandering / hunting critters, just ignoring player creatures) for the world's opening grace
    /// period — so danger doesn't arrive in the same instant as the player.
    /// </summary>
    public void SetInitialGrace(float seconds) => restTimer = Mathf.Max(restTimer, seconds);

    void Start()
    {
        world = SphericalWorld.Instance;
        if (world == null) return;

        transform.position = world.SnapToSurface(transform.position, heightOffset);
        forward = Vector3.ProjectOnPlane(transform.forward, world.GetSurfaceNormal(transform.position)).normalized;
        PickWander();
    }

    void Update()
    {
        if (world == null) return;
        Vector3 up = world.GetSurfaceNormal(transform.position);

        restTimer -= Time.deltaTime;
        scanTimer -= Time.deltaTime;
        if (scanTimer <= 0f) { scanTimer = 0.3f; ScanPrey(); }
        attackTimer -= Time.deltaTime;

        // Drop a stale critter target that wandered off / was eaten.
        if (preyCritter != null && !preyCritter.IsAlive) preyCritter = null;

        // A real predator tires: give up a chase that's dragged on too long or where the prey pulled
        // far enough away, then rest (leaving creatures alone) before hunting them again. Without this
        // predators locked onto whichever creature was nearest forever and, once one escaped, instantly
        // retargeted the next-nearest — grinding down the whole population with no recovery window.
        if (prey != null)
        {
            chaseTimer += Time.deltaTime;
            float distToPrey = Vector3.Distance(transform.position, prey.position);

            // During StoryDirector's Crisis stage, predators hunt far more relentlessly — longer
            // chases, farther pursuit, shorter recovery — a real, mechanical hardship rather than
            // just a label on the screen.
            bool crisis = StoryDirector.Instance != null && StoryDirector.Instance.IsCrisisActive;
            float effGiveUp = crisis ? giveUpChaseTime * 2f : giveUpChaseTime;
            float effLoseInterest = crisis ? loseInterestRange * 1.4f : loseInterestRange;

            if (chaseTimer > effGiveUp || distToPrey > effLoseInterest)
            {
                prey = null;
                chaseTimer = 0f;
                restTimer = crisis ? restDuration * 0.4f : restDuration;
            }
        }
        else
        {
            chaseTimer = 0f;
        }

        bool chasing = prey != null || preyCritter != null;
        Vector3 targetPos = prey != null ? prey.position
                          : preyCritter != null ? preyCritter.transform.position
                          : wanderTarget;

        Vector3 dir;
        if (chasing)
        {
            dir = Vector3.ProjectOnPlane(targetPos - transform.position, up).normalized;
            if (Vector3.Distance(transform.position, targetPos) < attackRange && attackTimer <= 0f)
                Attack();
        }
        else
        {
            wanderTimer -= Time.deltaTime;
            if (wanderTimer <= 0f || Vector3.Distance(transform.position, wanderTarget) < 2f)
                PickWander();
            dir = Vector3.ProjectOnPlane(wanderTarget - transform.position, up).normalized;
        }

        float s = chasing ? chaseSpeed : wanderSpeed;
        Vector3 next = transform.position + dir * s * Time.deltaTime;
        if (world.IsLand(next))
        {
            transform.position = next;
            forward = Vector3.Slerp(forward, dir, Time.deltaTime * 5f);
        }
        else
        {
            PickWander();
        }

        transform.position = world.SnapToSurface(transform.position, heightOffset);
        transform.rotation = world.GetSurfaceRotation(transform.position, forward);
    }

    void ScanPrey()
    {
        prey = null;
        preyCritter = null;

        // While resting after a given-up chase, leave creatures alone entirely (still may hunt
        // critters, so the predator keeps behaving like a live animal rather than idling).
        if (restTimer <= 0f)
        {
            var nearestCreature = CreatureGrid.FindNearest(transform.position, null, detectRange, out _);
            // A God Powers Ward makes a spot sanctuary — predators simply can't acquire prey standing in one.
            if (nearestCreature != null && !GodEventBus.IsWarded(nearestCreature.transform.position))
            {
                prey = nearestCreature.transform;
                return; // creatures are the priority quarry
            }
        }

        // No creature in range (or resting) — stalk the nearest critter instead.
        float best = detectRange;
        var critters = Critter.All;
        for (int i = 0; i < critters.Count; i++)
        {
            var cr = critters[i];
            if (cr == null || !cr.IsAlive) continue;
            float d = Vector3.Distance(transform.position, cr.transform.position);
            if (d < best) { best = d; preyCritter = cr; }
        }
    }

    void Attack()
    {
        attackTimer = attackCooldown;

        if (prey != null)
        {
            var body = prey.GetComponent<CreatureBody>();
            if (body != null) body.TakePredatorAttack(attackDamage, transform.position);
        }
        else if (preyCritter != null)
        {
            preyCritter.Catch();
            preyCritter = null;
        }
    }

    void PickWander()
    {
        wanderTarget = world.SnapToSurface(world.GetRandomLandPoint());
        wanderTimer = Random.Range(4f, 9f);
    }
}
