using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A player-placed shrine (God Powers: Consecrate). Creatures that wander within range slowly gain
/// Awe and remember discovering it once. Feeds back into the god's Favor regen via total world Awe
/// (see GodEventBus), so a well-placed shrine is a long-term investment, not just a one-off effect.
/// </summary>
public class Shrine : MonoBehaviour
{
    public static readonly List<Shrine> All = new List<Shrine>();
    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    // "Reload Domain" is disabled for fast/hang-free play iteration, so statics survive between play
    // sessions — clear this registry at the start of each one so we never carry stale entries.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() => All.Clear();

    [SerializeField] float radius = 12f;
    [SerializeField] float aweRatePerSecond = 0.02f;

    float tickTimer;
    readonly HashSet<CreatureMind> discoveredBy = new HashSet<CreatureMind>();

    void Update()
    {
        tickTimer -= Time.deltaTime;
        if (tickTimer > 0f) return;
        tickTimer = 2f;

        var all = CreatureMind.All;
        for (int i = 0; i < all.Count; i++)
        {
            var m = all[i];
            if (m == null) continue;
            if (Vector3.Distance(transform.position, m.transform.position) > radius) continue;

            m.FeelAwe(aweRatePerSecond * 2f); // 2f = tick interval, so this is a steady per-second rate
            if (discoveredBy.Add(m))
                m.AddMemory("I found a shrine to the god — it fills me with wonder.");
        }
    }
}
