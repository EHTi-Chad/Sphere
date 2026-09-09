using UnityEngine;
using UnityEngine.InputSystem;

public class OrbitalCamera : MonoBehaviour
{
    public static OrbitalCamera Instance { get; private set; }

    [Header("Orbit")]
    [SerializeField] float distance = 150f;
    [SerializeField] float minDistance = 60f;
    [SerializeField] float maxDistance = 400f;
    [SerializeField] float orbitSpeed = 0.3f;
    [SerializeField] float zoomSpeed = 80f;
    [SerializeField] float zoomSmoothing = 10f;
    [SerializeField] float autoRotateSpeed = 2.5f; // deg/sec idle drift — the "planet slowly rotating" feel

    [Header("Tilt")]
    [SerializeField] float minTilt = 10f;
    [SerializeField] float maxTilt = 85f;

    [Header("Focus")]
    [SerializeField] float focusDistance = 18f;
    [SerializeField] float focusPitch = 25f;
    [SerializeField] float focusTransitionSpeed = 3f;
    [SerializeField] float followSmoothTime = 0.12f;
    Vector3 followVelocity;
    [SerializeField] float focusOrbitSpeed = 0.15f;
    [SerializeField] float focusMinDistance = 10f;
    [SerializeField] float focusMaxDistance = 40f;
    [SerializeField] float focusFrameTurnSpeed = 90f; // deg/sec cap on how fast the orbit basis can re-align to the subject's heading

    // The camera's orbit basis is built "relative to the subject's facing" — but a fleeing/erratic
    // creature can spin its heading almost instantly, which used to whip the camera around with it.
    // This is a separately slew-rate-limited copy of that heading: it always catches up, just never
    // faster than focusFrameTurnSpeed, so a sudden flip smooths into a pan instead of a snap.
    Vector3 smoothedFocusForward;
    bool hasSmoothedFocusForward;

    [Header("Cinematic idle tour")]
    [Tooltip("After this many seconds without input, the camera swoops down and tours the creatures on its own.")]
    [SerializeField] bool cinematicIdle = true;
    [SerializeField] float idleBeforeTour = 12f;
    [SerializeField] float tourHoldTime = 18f;      // seconds spent framing each creature before moving on
    [SerializeField] float tourDriftSpeed = 8f;     // slow orbit drift while watching (deg/sec)

    float yaw;
    float pitch = 45f;
    float targetDistance;
    Transform worldTarget;

    // Focus state
    Transform focusTarget;
    bool isFocused;
    float focusBlend;
    float savedDistance;
    float savedPitch;
    float savedYaw;

    // Cinematic tour state
    bool isTouring;
    float idleTimer;
    float tourTimer;
    CreatureMind tourCreature;

    public bool IsFocused => isFocused;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        targetDistance = distance;
        if (SphericalWorld.Instance != null)
            worldTarget = SphericalWorld.Instance.transform;

        ApplyPosition();
    }

    /// <summary>Scale orbit zoom limits to the planet radius so the camera never clips inside it.</summary>
    public void ConfigureForRadius(float r)
    {
        minDistance = r + 12f;
        maxDistance = r * 6f;
        distance = r * 1.9f; // closer default — the society shouldn't read as distant specks
        targetDistance = distance;

        if (worldTarget == null && SphericalWorld.Instance != null)
            worldTarget = SphericalWorld.Instance.transform;
        ApplyPosition();
    }

    public void FocusOn(Transform creature)
    {
        EndTour(); // a manual focus supersedes any auto-tour
        if (!isFocused)
        {
            savedDistance = targetDistance;
            savedPitch = pitch;
            savedYaw = yaw;
        }

        focusTarget = creature;
        isFocused = true;
        pitch = focusPitch; // settle to a flattering watching angle
        targetDistance = focusDistance;
        hasSmoothedFocusForward = false; // snap to the new subject's heading, don't pan in from the old one
    }

    public void Unfocus()
    {
        if (!isFocused) return;

        isFocused = false;
        isTouring = false;
        focusTarget = null;
        targetDistance = savedDistance;
        pitch = savedPitch;
        yaw = savedYaw;
    }

    void LateUpdate()
    {
        if (worldTarget == null)
        {
            if (SphericalWorld.Instance != null)
                worldTarget = SphericalWorld.Instance.transform;
            return;
        }

        if (MainMenu.Instance != null && MainMenu.Instance.IsOpen) return;

        var mouse = Mouse.current;
        var kb = Keyboard.current;
        if (mouse == null) return;

        // ESC or right-click unfocuses — but not if GodEventBus (runs first, DefaultExecutionOrder)
        // already claimed this Escape to cancel an armed power; otherwise one keypress both cancelled
        // the power AND silently unfocused the camera, breaking the documented "one keypress, one
        // effect" contract that MainMenu/CreatureInfoUI already respect.
        if (isFocused && kb != null && kb.escapeKey.wasPressedThisFrame && !GodEventBus.WasEscapeConsumedThisFrame)
        {
            Unfocus();
            return;
        }

        if (isTouring)
            UpdateTour(mouse, kb);
        else if (isFocused)
            UpdateFocusMode(mouse);
        else
            UpdateOrbitMode(mouse, kb);
    }

    void UpdateOrbitMode(Mouse mouse, Keyboard kb)
    {
        bool dragging = mouse.rightButton.isPressed;
        float scroll = mouse.scroll.ReadValue().y;
        bool zooming = Mathf.Abs(scroll) > 0.01f;

        if (dragging)
        {
            Vector2 delta = mouse.delta.ReadValue();
            yaw += delta.x * orbitSpeed;
            pitch -= delta.y * orbitSpeed;
            pitch = Mathf.Clamp(pitch, minTilt, maxTilt);
        }
        else
        {
            // Idle: drift slowly so the planet appears to rotate (pauses while you're dragging).
            yaw += autoRotateSpeed * Time.unscaledDeltaTime;
        }

        if (zooming)
            targetDistance -= scroll * zoomSpeed * 0.01f;
        targetDistance = Mathf.Clamp(targetDistance, minDistance, maxDistance);

        distance = Mathf.Lerp(distance, targetDistance, Time.unscaledDeltaTime * zoomSmoothing);

        // Blend back from focus smoothly
        focusBlend = Mathf.Lerp(focusBlend, 0f, Time.deltaTime * focusTransitionSpeed);

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 position = worldTarget.position + rotation * new Vector3(0f, 0f, -distance);

        transform.position = position;
        transform.LookAt(worldTarget.position);

        // --- Cinematic idle: after a stretch of no input, start touring creatures. ---
        bool anyInput = dragging || zooming
            || mouse.leftButton.wasPressedThisFrame
            || (kb != null && kb.anyKey.wasPressedThisFrame);
        if (anyInput) idleTimer = 0f;
        else idleTimer += Time.unscaledDeltaTime;

        if (cinematicIdle && idleTimer >= idleBeforeTour)
        {
            var pick = PickInterestingCreature(null);
            if (pick != null) StartTour(pick);
        }
    }

    void StartTour(CreatureMind c)
    {
        savedDistance = targetDistance;
        savedPitch = pitch;
        savedYaw = yaw;

        isFocused = true;
        isTouring = true;
        tourCreature = c;
        focusTarget = c.transform;
        pitch = focusPitch;
        targetDistance = focusDistance;
        tourTimer = tourHoldTime;
        hasSmoothedFocusForward = false; // snap to the new subject's heading, don't pan in from the old one
    }

    void EndTour()
    {
        if (!isTouring) return;
        isTouring = false;
        idleTimer = 0f;
        Unfocus();
    }

    void UpdateTour(Mouse mouse, Keyboard kb)
    {
        // Any deliberate input hands control straight back to the player.
        bool takeOver = mouse.leftButton.wasPressedThisFrame
            || mouse.rightButton.wasPressedThisFrame
            || Mathf.Abs(mouse.scroll.ReadValue().y) > 0.01f
            || (kb != null && kb.anyKey.wasPressedThisFrame);
        if (takeOver) { EndTour(); return; }

        // Lost the subject (e.g. it died)? Move on immediately.
        if (focusTarget == null || tourCreature == null)
        {
            var next = PickInterestingCreature(tourCreature);
            if (next == null) { EndTour(); return; }
            tourCreature = next; focusTarget = next.transform; tourTimer = tourHoldTime;
            hasSmoothedFocusForward = false;
        }

        // Slow cinematic drift around the subject.
        yaw += tourDriftSpeed * Time.unscaledDeltaTime;

        tourTimer -= Time.unscaledDeltaTime;
        if (tourTimer <= 0f)
        {
            var next = PickInterestingCreature(tourCreature);
            if (next == null) { EndTour(); return; }
            tourCreature = next; focusTarget = next.transform; tourTimer = tourHoldTime;
            hasSmoothedFocusForward = false;
        }

        ApplyFocusFraming();
    }

    void UpdateFocusMode(Mouse mouse)
    {
        if (focusTarget == null)
        {
            Unfocus();
            return;
        }

        // Allow orbiting around the creature
        if (mouse.rightButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue();
            yaw += delta.x * focusOrbitSpeed;
            pitch -= delta.y * focusOrbitSpeed;
            pitch = Mathf.Clamp(pitch, 10f, 70f);
        }

        // Scroll to zoom — scrolling out past max pops back to the orbit view
        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            targetDistance -= scroll * zoomSpeed * 0.01f;
            if (targetDistance > focusMaxDistance)
            {
                Unfocus();
                return;
            }
            targetDistance = Mathf.Clamp(targetDistance, focusMinDistance, focusMaxDistance);
        }

        ApplyFocusFraming();
    }

    /// <summary>Shared smooth framing of focusTarget — used by both player focus and the auto-tour.</summary>
    void ApplyFocusFraming()
    {
        focusBlend = Mathf.Lerp(focusBlend, 1f, Time.deltaTime * focusTransitionSpeed);
        distance = Mathf.Lerp(distance, targetDistance, Time.unscaledDeltaTime * zoomSmoothing);

        // Camera always stays above the surface
        Vector3 creaturePos = focusTarget.position;
        Vector3 surfaceUp = (creaturePos - worldTarget.position).normalized;
        Vector3 focusPos = creaturePos + surfaceUp * 1.5f;

        // Orbit basis is built relative to the subject's facing — but a fleeing/erratic creature can
        // spin its heading almost instantly. Slew-rate-limit our copy of that heading so a sudden flip
        // pans the camera around smoothly instead of whipping it to match in a single frame.
        Vector3 rawForward = Vector3.ProjectOnPlane(focusTarget.forward, surfaceUp);
        if (rawForward.sqrMagnitude < 0.001f) rawForward = Vector3.ProjectOnPlane(Vector3.forward, surfaceUp);
        rawForward.Normalize();

        if (!hasSmoothedFocusForward)
        {
            smoothedFocusForward = rawForward;
            hasSmoothedFocusForward = true;
        }
        else
        {
            smoothedFocusForward = Vector3.RotateTowards(
                smoothedFocusForward, rawForward,
                focusFrameTurnSpeed * Mathf.Deg2Rad * Time.unscaledDeltaTime, 0f);
        }

        // Offset: always push outward from planet center + orbit angle
        Vector3 localRight = Vector3.Cross(surfaceUp, smoothedFocusForward).normalized;
        if (localRight.sqrMagnitude < 0.001f)
            localRight = Vector3.Cross(surfaceUp, Vector3.forward).normalized;
        Vector3 localForward = Vector3.Cross(localRight, surfaceUp).normalized;

        // Yaw rotates around the surface normal, pitch tilts up from horizon
        float yawRad = yaw * Mathf.Deg2Rad;
        float pitchRad = pitch * Mathf.Deg2Rad;

        Vector3 horizontal = localForward * Mathf.Cos(yawRad) + localRight * Mathf.Sin(yawRad);
        Vector3 camDir = -horizontal * Mathf.Cos(pitchRad) + surfaceUp * Mathf.Sin(pitchRad);

        Vector3 targetCamPos = focusPos + camDir.normalized * distance;

        // Ensure camera never goes inside the planet
        float worldRadius = SphericalWorld.Instance != null ? SphericalWorld.Instance.Radius : 50f;
        if (targetCamPos.magnitude < worldRadius + 3f)
            targetCamPos = targetCamPos.normalized * (worldRadius + 3f);

        // Smooth, frame-rate-independent follow (SmoothDamp for position, exponential for rotation).
        float dt = Time.unscaledDeltaTime;
        transform.position = Vector3.SmoothDamp(transform.position, targetCamPos, ref followVelocity, followSmoothTime, Mathf.Infinity, dt);
        Quaternion targetRot = Quaternion.LookRotation(focusPos - transform.position, surfaceUp);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-12f * dt));
    }

    /// <summary>Pick a creature worth watching — prefer ones doing something dramatic, else any, never 'avoid'.</summary>
    CreatureMind PickInterestingCreature(CreatureMind avoid)
    {
        var all = CreatureMind.All;
        if (all.Count == 0) return null;

        CreatureMind best = null;
        float bestScore = -1f;
        for (int i = 0; i < all.Count; i++)
        {
            var m = all[i];
            if (m == null || m == avoid) continue;

            float score = Random.value * 0.5f; // base jitter so the tour varies
            if (!string.IsNullOrEmpty(m.LastSay) && Time.time - m.LastSayTime < 4f) score += 2f; // talking now
            if (Time.time - m.BirthTime < 30f) score += 3f; // a brand-new arrival wins over almost anything else
            var body = m.GetComponent<CreatureBody>();
            if (body != null)
            {
                if (body.CurrentGoal == CreatureGoal.FLEE || body.CurrentGoal == CreatureGoal.HUNT) score += 1.5f;
                if (body.Health < 0.4f) score += 1f;
            }
            score += Mathf.Max(m.Hunger, m.Safety); // the stressed are interesting

            if (score > bestScore) { bestScore = score; best = m; }
        }

        // Fallback: if everyone was excluded, just take the first that isn't 'avoid'.
        if (best == null)
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i] != avoid) return all[i];

        return best;
    }

    void ApplyPosition()
    {
        if (worldTarget == null) return;
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        transform.position = worldTarget.position + rotation * new Vector3(0f, 0f, -distance);
        transform.LookAt(worldTarget.position);
    }
}
