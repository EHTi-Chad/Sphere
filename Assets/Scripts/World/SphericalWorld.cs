using UnityEngine;

public class SphericalWorld : MonoBehaviour
{
    public static SphericalWorld Instance { get; private set; }

    [SerializeField] float radius = 50f;
    [SerializeField] float gravityStrength = 20f;

    SphericalTerrain terrain;

    public float Radius => radius;
    public float GravityStrength => gravityStrength;
    public Vector3 Center => transform.position;
    public SphericalTerrain Terrain => terrain;

    public void SetRadius(float r) { radius = r; }

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        terrain = GetComponent<SphericalTerrain>();
    }

    public Vector3 GetGravity(Vector3 worldPosition)
    {
        return (Center - worldPosition).normalized * gravityStrength;
    }

    public Vector3 GetSurfaceNormal(Vector3 worldPosition)
    {
        return (worldPosition - Center).normalized;
    }

    public Vector3 SnapToSurface(Vector3 worldPosition, float heightOffset = 0f)
    {
        if (terrain != null)
        {
            Vector3 dir = (worldPosition - Center).normalized;
            return terrain.GetSurfacePoint(dir) + dir * heightOffset;
        }
        return Center + GetSurfaceNormal(worldPosition) * (radius + heightOffset);
    }

    public Quaternion GetSurfaceRotation(Vector3 worldPosition, Vector3 forward)
    {
        Vector3 up = GetSurfaceNormal(worldPosition);
        Vector3 projectedForward = Vector3.ProjectOnPlane(forward, up).normalized;
        if (projectedForward.sqrMagnitude < 0.001f)
            projectedForward = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
        return Quaternion.LookRotation(projectedForward, up);
    }

    public Vector3 GetRandomSurfacePoint()
    {
        return SnapToSurface(Center + Random.onUnitSphere * radius);
    }

    public Vector3 GetRandomLandPoint(int maxAttempts = 50)
    {
        if (terrain == null)
            return GetRandomSurfacePoint();

        // Remembers the first plain "at least dry land" point we see, so that if every attempt fails
        // the STRICTER checks below (rare, but happens on ocean-heavy or fragmented-coastline worlds),
        // the fallback is still guaranteed land — not GetRandomSurfacePoint()'s totally unfiltered pick,
        // which is exactly what was making "spawns in water" WORSE the stricter these checks got.
        Vector3? fallbackLandPoint = null;

        for (int i = 0; i < maxAttempts; i++)
        {
            Vector3 dir = Random.onUnitSphere;

            // IsLand alone only requires height > waterLevel — a single unit of elevation counts as
            // fully valid "land" with zero margin, so a camp could land on a sliver right at the
            // shoreline that's geometrically dry but visually indistinguishable from being in the
            // water (the beach color gradient itself runs from landH 0.01 to 0.16 — anything near the
            // bottom of that range reads as wet sand or shallower). Require real clearance above sea
            // level, not just barely-not-underwater.
            if (terrain.GetLandHeight(dir) < minLandHeightMargin) continue;

            Vector3 surfacePoint = terrain.GetSurfacePoint(dir);
            if (fallbackLandPoint == null) fallbackLandPoint = surfacePoint;

            // IsLand only knows the raw noise height, which has no idea rivers/lakes exist — they're
            // carved in by a separate hydrology pass afterward. A lake IS a "land" point in that raw
            // sense (that's exactly why rainfall pooled there), so without this check a creature (and
            // its whole camp) could spawn — and get visually stuck — standing in a lake.
            if (terrain.IsWaterNear(surfacePoint, 3f)) continue;

            // A single vertex can be "land" (just above sea level) while being a lucky speck entirely
            // surrounded by ocean — IsLand only ever checks the one exact point, never how much solid
            // ground actually surrounds it. Without this, a whole camp could spawn on an island smaller
            // than the camp itself. Require a real patch of land around the candidate, not just at it.
            if (!HasLandAround(dir)) continue;

            return surfacePoint;
        }

        return fallbackLandPoint ?? GetRandomSurfacePoint();
    }

    /// <summary>Checks a ring of points ~6 units out from `dir` (a bit more than a camp's own
    /// footprint) and requires all of them to also be land — rejects slivers/tiny islands too small
    /// to actually hold a camp.</summary>
    bool HasLandAround(Vector3 dir)
    {
        const float checkDistance = 6f;
        const int samples = 8;
        float angularSize = (checkDistance / radius) * Mathf.Rad2Deg;

        Vector3 axis = Vector3.Cross(dir, Vector3.up);
        if (axis.sqrMagnitude < 0.0001f) axis = Vector3.Cross(dir, Vector3.right);
        axis.Normalize();

        Vector3 tilted = Quaternion.AngleAxis(angularSize, axis) * dir;

        for (int i = 0; i < samples; i++)
        {
            float spin = i * (360f / samples);
            Vector3 testDir = (Quaternion.AngleAxis(spin, dir) * tilted).normalized;
            if (terrain.GetLandHeight(testDir) < minLandHeightMargin) return false;
        }
        return true;
    }

    // Below this, a "land" point is close enough to sea level that it visually blends into the beach/
    // water gradient (which itself runs landH 0.01-0.16) — see the comment in GetRandomLandPoint.
    const float minLandHeightMargin = 0.1f;

    public bool IsLand(Vector3 worldPosition)
    {
        if (terrain == null) return true;
        return terrain.IsLand(worldPosition);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.1f);
        Gizmos.DrawWireSphere(Center, radius);
    }
}
