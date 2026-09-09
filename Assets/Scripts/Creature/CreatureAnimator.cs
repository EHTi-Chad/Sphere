using UnityEngine;

/// <summary>
/// Procedural "alive" animation for creatures — no rig required. Drives the existing
/// capsule body + sphere head and adds eyes that blink and glance toward what the
/// creature is focused on. Runs in LateUpdate so it layers on top of CreatureBody's
/// surface snapping / movement without fighting it.
///
/// What it does:
///  - Walk: bounce hop with squash-and-stretch, timed to actual ground speed.
///  - Idle: slow breathing (subtle scale pulse) and a tiny sway.
///  - Lean: leans into movement, and rolls into turns.
///  - Eyes: white sclera + pupils that track the nearest creature / facing, plus blinks.
/// </summary>
[RequireComponent(typeof(CreatureBody))]
public class CreatureAnimator : MonoBehaviour
{
    [Header("Walk")]
    [SerializeField] float strideFrequency = 9f;   // step cadence at full speed
    [SerializeField] float hopHeight = 0.18f;       // how high each bounce lifts the body
    [SerializeField] float squashAmount = 0.22f;    // squash/stretch intensity
    [SerializeField] float leanAmount = 14f;        // forward lean (deg) at full speed
    [SerializeField] float turnRoll = 18f;          // bank into turns (deg)

    [Header("Idle")]
    [SerializeField] float breatheSpeed = 1.6f;
    [SerializeField] float breatheAmount = 0.04f;

    [Header("Eyes")]
    [SerializeField] float eyeSizeRatio = 0.6f;     // eye diameter as a fraction of head radius
    [SerializeField] float blinkMin = 2.2f;
    [SerializeField] float blinkMax = 6f;
    [SerializeField] float pupilTrack = 0.3f;       // how far pupils slide toward focus

    Transform body, head;
    Transform leftEye, rightEye, leftPupil, rightPupil;
    Vector3 bodyBaseScale, headBasePos, headBaseScale;

    CreatureBody cb;

    Vector3 lastPos;
    float walkAmt;       // smoothed 0..1 movement amount
    float phase;         // stride phase
    float prevYaw;
    float turnVel;       // smoothed angular velocity (for banking)

    float blinkTimer;
    float blink;         // 1 = open, 0 = shut

    static Material whiteMat, pupilMat;

    void Start()
    {
        cb = GetComponent<CreatureBody>();

        // Identify the body (capsule) and head (highest child).
        float bestY = float.NegativeInfinity;
        for (int i = 0; i < transform.childCount; i++)
        {
            var c = transform.GetChild(i);
            if (c.GetComponent<MeshFilter>() == null) continue;
            if (c.localPosition.y > bestY) { bestY = c.localPosition.y; head = c; }
        }
        for (int i = 0; i < transform.childCount; i++)
        {
            var c = transform.GetChild(i);
            if (c == head || c.GetComponent<MeshFilter>() == null) continue;
            body = c; break;
        }
        if (body == null || head == null) { enabled = false; return; }

        bodyBaseScale = body.localScale;
        headBasePos = head.localPosition;
        headBaseScale = head.localScale;

        BuildEyes();

        lastPos = transform.position;
        prevYaw = transform.eulerAngles.y;
        blinkTimer = Random.Range(blinkMin, blinkMax);
        blink = 1f;
        phase = Random.value * 10f; // desync the herd
    }

    void EnsureMaterials()
    {
        if (whiteMat != null) return;
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        whiteMat = new Material(shader) { color = new Color(0.97f, 0.97f, 0.95f) };
        pupilMat = new Material(shader) { color = new Color(0.05f, 0.05f, 0.08f) };
    }

    void BuildEyes()
    {
        EnsureMaterials();

        float headR = headBaseScale.x;             // sphere head radius-ish
        float eyeSize = headR * eyeSizeRatio;
        float pupilSize = eyeSize * 0.6f;
        // Place eyes on the front (+z) face of the head, spread horizontally, a touch up.
        float fwd = headR * 0.62f;
        float side = headR * 0.32f;
        float up = headR * 0.18f;

        leftEye = MakeEyeSphere("EyeL", new Vector3(-side, up, fwd), eyeSize, whiteMat, head);
        rightEye = MakeEyeSphere("EyeR", new Vector3(side, up, fwd), eyeSize, whiteMat, head);
        leftPupil = MakeEyeSphere("PupilL", new Vector3(0f, 0f, eyeSize * 0.55f), pupilSize / eyeSize, pupilMat, leftEye);
        rightPupil = MakeEyeSphere("PupilR", new Vector3(0f, 0f, eyeSize * 0.55f), pupilSize / eyeSize, pupilMat, rightEye);
    }

    Transform MakeEyeSphere(string name, Vector3 localPos, float scale, Material mat, Transform parent)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = Vector3.one * scale;
        return go.transform;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // --- movement amount from real ground speed ---
        float speed = (transform.position - lastPos).magnitude / dt;
        lastPos = transform.position;
        float target = Mathf.Clamp01(speed / 4f); // ~moveSpeed
        walkAmt = Mathf.Lerp(walkAmt, target, dt * 8f);

        // --- turn velocity for banking ---
        float yaw = transform.eulerAngles.y;
        float dYaw = Mathf.DeltaAngle(prevYaw, yaw);
        prevYaw = yaw;
        turnVel = Mathf.Lerp(turnVel, Mathf.Clamp(dYaw / dt, -180f, 180f) / 180f, dt * 6f);

        // --- stride phase ---
        phase += dt * strideFrequency * (0.35f + walkAmt);

        // bounce: 0 at footfall, 1 at apex
        float bounce = Mathf.Abs(Mathf.Sin(phase));
        float hop = bounce * hopHeight * walkAmt;

        // squash/stretch: stretched at apex, squashed at footfall
        float s = (bounce - 0.5f) * 2f; // -1..1
        float breathe = Mathf.Sin(Time.time * breatheSpeed) * breatheAmount * (1f - walkAmt);
        float stretchY = 1f + s * squashAmount * walkAmt + breathe;
        float squashXZ = 1f - s * squashAmount * 0.6f * walkAmt - breathe;

        // apply to body
        body.localScale = new Vector3(bodyBaseScale.x * squashXZ, bodyBaseScale.y * stretchY, bodyBaseScale.z * squashXZ);

        // head rides the hop and bobs slightly behind the body for a sense of weight
        float headBob = Mathf.Sin(phase - 0.6f) * 0.04f * walkAmt;
        head.localPosition = headBasePos + new Vector3(0f, hop + headBob, 0f);
        head.localScale = new Vector3(headBaseScale.x * squashXZ, headBaseScale.y * (1f + breathe), headBaseScale.z * squashXZ);

        // lift the whole body with the hop too (children move together visually)
        body.localPosition = new Vector3(body.localPosition.x, hop, body.localPosition.z);

        // --- lean into movement + bank into turns ---
        float pitch = leanAmount * walkAmt;                 // forward lean
        float roll = -turnRoll * turnVel;                   // bank
        Quaternion lean = Quaternion.Euler(pitch, 0f, roll);
        body.localRotation = lean;
        head.localRotation = Quaternion.Euler(pitch * 0.5f, 0f, roll * 0.5f);

        // --- blink ---
        blinkTimer -= dt;
        if (blinkTimer <= 0f) { blinkTimer = Random.Range(blinkMin, blinkMax); blink = 0f; }
        blink = Mathf.MoveTowards(blink, 1f, dt * 9f); // snap shut, ease open
        float lid = Mathf.SmoothStep(0.08f, 1f, blink);
        SetEyeOpen(leftEye, lid);
        SetEyeOpen(rightEye, lid);

        // --- pupils glance toward focus ---
        UpdateGaze();
    }

    void SetEyeOpen(Transform eye, float open)
    {
        if (eye == null) return;
        var sc = eye.localScale;
        // eyes keep base x/z; only the vertical lid closes
        float baseSize = headBaseScale.x * eyeSizeRatio;
        eye.localScale = new Vector3(baseSize, baseSize * open, baseSize);
    }

    void UpdateGaze()
    {
        if (leftPupil == null) return;

        Vector3 focus;
        if (cb != null && cb.NearestCreature != null && cb.NearestCreatureDistance < 14f)
            focus = cb.NearestCreature.transform.position + Vector3.up * 1f;
        else
            focus = transform.position + transform.forward * 6f + transform.up * 1f;

        ApplyGaze(leftEye, leftPupil, focus);
        ApplyGaze(rightEye, rightPupil, focus);
    }

    void ApplyGaze(Transform eye, Transform pupil, Vector3 worldFocus)
    {
        Vector3 local = eye.InverseTransformPoint(worldFocus).normalized;
        // slide pupil across the eye surface toward the focus direction
        Vector3 baseFwd = new Vector3(0f, 0f, 0.55f);
        Vector3 offset = new Vector3(local.x, local.y, 0f) * pupilTrack;
        pupil.localPosition = baseFwd + offset;
    }
}
