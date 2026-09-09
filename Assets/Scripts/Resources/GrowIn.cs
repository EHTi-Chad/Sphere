using UnityEngine;

/// <summary>
/// Animates a just-built structure scaling up from its base, so you see it "rise" as it's
/// constructed instead of popping in instantly. Removes itself when done.
/// Requires the object's pivot to sit at the structure's base (set transform.position before adding).
/// </summary>
public class GrowIn : MonoBehaviour
{
    [SerializeField] float duration = 1.4f;

    Vector3 targetScale;
    float t;

    void Start()
    {
        targetScale = transform.localScale;
        transform.localScale = targetScale * 0.02f;
    }

    void Update()
    {
        t += Time.deltaTime / duration;
        float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
        // slight overshoot for a satisfying "settle"
        float scale = e < 1f ? e * (1f + 0.08f * (1f - e)) : 1f;
        transform.localScale = Vector3.Lerp(targetScale * 0.02f, targetScale, scale);
        if (t >= 1f)
        {
            transform.localScale = targetScale;
            Destroy(this);
        }
    }
}
