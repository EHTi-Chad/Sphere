using UnityEngine;
using UnityEngine.InputSystem;

public class OrbitalCamera : MonoBehaviour
{
    [Header("Orbit")]
    [SerializeField] float distance = 150f;
    [SerializeField] float minDistance = 60f;
    [SerializeField] float maxDistance = 400f;
    [SerializeField] float orbitSpeed = 0.3f;
    [SerializeField] float zoomSpeed = 20f;
    [SerializeField] float zoomSmoothing = 8f;

    [Header("Tilt")]
    [SerializeField] float minTilt = 10f;
    [SerializeField] float maxTilt = 85f;

    float yaw;
    float pitch = 45f;
    float targetDistance;
    Transform target;

    void Start()
    {
        targetDistance = distance;
        if (SphericalWorld.Instance != null)
            target = SphericalWorld.Instance.transform;

        ApplyPosition();
    }

    void LateUpdate()
    {
        if (target == null)
        {
            if (SphericalWorld.Instance != null)
                target = SphericalWorld.Instance.transform;
            return;
        }

        if (MainMenu.Instance != null && MainMenu.Instance.IsOpen) return;

        var mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.rightButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue();
            yaw += delta.x * orbitSpeed;
            pitch -= delta.y * orbitSpeed;
            pitch = Mathf.Clamp(pitch, minTilt, maxTilt);
        }

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
            targetDistance -= scroll * zoomSpeed * 0.01f;
        targetDistance = Mathf.Clamp(targetDistance, minDistance, maxDistance);

        distance = Mathf.Lerp(distance, targetDistance, Time.unscaledDeltaTime * zoomSmoothing);

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 position = target.position + rotation * new Vector3(0f, 0f, -distance);

        transform.position = position;
        transform.LookAt(target.position);
    }

    void ApplyPosition()
    {
        if (target == null) return;
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        transform.position = target.position + rotation * new Vector3(0f, 0f, -distance);
        transform.LookAt(target.position);
    }
}
