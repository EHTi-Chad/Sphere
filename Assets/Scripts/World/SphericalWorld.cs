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

        for (int i = 0; i < maxAttempts; i++)
        {
            Vector3 dir = Random.onUnitSphere;
            if (terrain.IsLand(Center + dir * radius))
                return terrain.GetSurfacePoint(dir);
        }
        return GetRandomSurfacePoint();
    }

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
