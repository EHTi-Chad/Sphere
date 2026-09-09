using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spatial hash over creature positions so "who's near me?" is ~O(1) per query instead of scanning
/// the whole population. The per-frame nearest-creature scan used to be O(n²) (every creature checked
/// every other, every frame), which is what capped the population at a few dozen. The grid is rebuilt
/// once per frame from <see cref="CreatureBody.All"/>; queries only touch the 3×3×3 cells around a point.
/// </summary>
public static class CreatureGrid
{
    // A touch larger than creature sightRange (40) so the 3×3×3 neighbour block fully covers a query.
    const float CellSize = 48f;

    static readonly Dictionary<(int, int, int), List<CreatureBody>> cells = new Dictionary<(int, int, int), List<CreatureBody>>();
    static readonly Stack<List<CreatureBody>> pool = new Stack<List<CreatureBody>>();
    static int builtFrame = -1;

    // Domain reload is disabled, so wipe static state at the start of each play session.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { cells.Clear(); pool.Clear(); builtFrame = -1; }

    static (int, int, int) Key(Vector3 p) => (
        Mathf.FloorToInt(p.x / CellSize),
        Mathf.FloorToInt(p.y / CellSize),
        Mathf.FloorToInt(p.z / CellSize));

    static void EnsureBuilt()
    {
        if (builtFrame == Time.frameCount) return;
        builtFrame = Time.frameCount;

        foreach (var kv in cells) { kv.Value.Clear(); pool.Push(kv.Value); }
        cells.Clear();

        var all = CreatureBody.All;
        for (int i = 0; i < all.Count; i++)
        {
            var c = all[i];
            if (c == null) continue;
            var key = Key(c.transform.position);
            if (!cells.TryGetValue(key, out var list))
            {
                list = pool.Count > 0 ? pool.Pop() : new List<CreatureBody>(8);
                cells[key] = list;
            }
            list.Add(c);
        }
    }

    /// <summary>Nearest creature to <paramref name="pos"/> within <paramref name="maxRange"/> (≤ CellSize), excluding one.</summary>
    public static CreatureBody FindNearest(Vector3 pos, CreatureBody exclude, float maxRange, out float dist)
    {
        EnsureBuilt();
        CreatureBody best = null;
        float bestSqr = maxRange * maxRange;
        var k = Key(pos);
        for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                {
                    if (!cells.TryGetValue((k.Item1 + x, k.Item2 + y, k.Item3 + z), out var list)) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var c = list[i];
                        if (c == exclude || c == null) continue;
                        float d2 = (c.transform.position - pos).sqrMagnitude;
                        if (d2 < bestSqr) { bestSqr = d2; best = c; }
                    }
                }
        dist = best != null ? Mathf.Sqrt(bestSqr) : float.MaxValue;
        return best;
    }

    /// <summary>Fills <paramref name="results"/> with every creature within <paramref name="range"/> (≤ CellSize).</summary>
    public static void QueryNeighbors(Vector3 pos, float range, List<CreatureBody> results)
    {
        EnsureBuilt();
        results.Clear();
        float r2 = range * range;
        var k = Key(pos);
        for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                {
                    if (!cells.TryGetValue((k.Item1 + x, k.Item2 + y, k.Item3 + z), out var list)) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var c = list[i];
                        if (c != null && (c.transform.position - pos).sqrMagnitude <= r2) results.Add(c);
                    }
                }
    }
}
