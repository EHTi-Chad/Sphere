using UnityEngine;

public class CreatureBody : MonoBehaviour
{
    [SerializeField] float moveSpeed = 5f;
    [SerializeField] float rotationSpeed = 8f;
    [SerializeField] float heightOffset = 0.5f;

    Vector3 moveDirection;
    Vector3 currentForward;
    CreatureGoal currentGoal = CreatureGoal.WANDER;
    float goalIntensity = 0.5f;

    float wanderTimer;
    Vector3 wanderTarget;

    SphericalWorld world;

    public CreatureGoal CurrentGoal => currentGoal;
    public float GoalIntensity => goalIntensity;

    void Start()
    {
        world = SphericalWorld.Instance;
        if (world == null) return;

        transform.position = world.SnapToSurface(transform.position, heightOffset);
        currentForward = Vector3.ProjectOnPlane(transform.forward, world.GetSurfaceNormal(transform.position)).normalized;
        PickNewWanderTarget();
    }

    void Update()
    {
        if (world == null) return;

        switch (currentGoal)
        {
            case CreatureGoal.WANDER:
                UpdateWander();
                break;
            case CreatureGoal.FLEE:
                UpdateFlee();
                break;
            case CreatureGoal.FORAGE:
                UpdateWander();
                break;
            case CreatureGoal.SEEK_OTHERS:
                UpdateSeekOthers();
                break;
            case CreatureGoal.REST:
                break;
            default:
                UpdateWander();
                break;
        }

        ApplyMovement();
    }

    void ApplyMovement()
    {
        Vector3 up = world.GetSurfaceNormal(transform.position);

        if (moveDirection.sqrMagnitude > 0.001f)
        {
            Vector3 tangentMove = Vector3.ProjectOnPlane(moveDirection, up).normalized;
            Vector3 nextPos = transform.position + tangentMove * moveSpeed * goalIntensity * Time.deltaTime;

            if (world.IsLand(nextPos))
            {
                transform.position = nextPos;
                currentForward = Vector3.Slerp(currentForward, tangentMove, Time.deltaTime * rotationSpeed);
            }
            else
            {
                PickNewWanderTarget();
            }
        }

        transform.position = world.SnapToSurface(transform.position, heightOffset);
        transform.rotation = world.GetSurfaceRotation(transform.position, currentForward);
    }

    void UpdateWander()
    {
        wanderTimer -= Time.deltaTime;
        if (wanderTimer <= 0f)
            PickNewWanderTarget();

        moveDirection = (wanderTarget - transform.position).normalized;
        float dist = Vector3.Distance(transform.position, wanderTarget);
        if (dist < 2f)
            PickNewWanderTarget();
    }

    void UpdateFlee()
    {
        // Flee from the nearest god event or threat — placeholder flees from origin
        Vector3 threatDir = (transform.position - world.Center).normalized;
        Vector3 randomOffset = Random.onUnitSphere * 0.3f;
        moveDirection = Vector3.ProjectOnPlane(threatDir + randomOffset, world.GetSurfaceNormal(transform.position)).normalized;
    }

    void UpdateSeekOthers()
    {
        CreatureBody[] others = FindObjectsByType<CreatureBody>(FindObjectsSortMode.None);
        CreatureBody nearest = null;
        float nearestDist = float.MaxValue;

        foreach (var other in others)
        {
            if (other == this) continue;
            float d = Vector3.Distance(transform.position, other.transform.position);
            if (d < nearestDist)
            {
                nearestDist = d;
                nearest = other;
            }
        }

        if (nearest != null && nearestDist > 3f)
            moveDirection = (nearest.transform.position - transform.position).normalized;
        else
            moveDirection = Vector3.zero;
    }

    void PickNewWanderTarget()
    {
        wanderTarget = world.GetRandomLandPoint();
        wanderTimer = Random.Range(4f, 10f);
    }

    public void SetGoal(CreatureGoal goal, float intensity)
    {
        currentGoal = goal;
        goalIntensity = Mathf.Clamp01(intensity);
    }

    public void FleeFrom(Vector3 position)
    {
        currentGoal = CreatureGoal.FLEE;
        goalIntensity = 1f;
        moveDirection = (transform.position - position).normalized;
    }
}

public enum CreatureGoal
{
    WANDER,
    FORAGE,
    FLEE,
    SEEK_OTHERS,
    HUDDLE,
    WORSHIP,
    REST
}
