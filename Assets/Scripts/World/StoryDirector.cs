using System.Collections.Generic;
using UnityEngine;

public enum CivilizationEra { Arrival, Settlement, Society, Crisis, Legacy }

/// <summary>
/// Drives the session's story arc: Arrival -> Settlement -> Society -> Crisis -> Legacy, then loops
/// back into a fresh Society/Crisis cycle for long sessions (each numbered as a new "Age"). Stage
/// transitions are milestone-gated (population, shelter level, time-in-stage) rather than pure
/// timers, so pacing adapts to how the playthrough actually unfolds instead of firing on a clock
/// regardless of what's happening. Each transition is announced (read by CreatureInfoUI's chapter
/// banner + era indicator) and logged for the Legacy/history panel.
/// </summary>
public class StoryDirector : MonoBehaviour
{
    public static StoryDirector Instance { get; private set; }

    [Header("Milestones")]
    [Tooltip("Population + at least one shelter (level 1) needed to leave Arrival.")]
    [SerializeField] int settlementPopulation = 5;
    [Tooltip("Population + at least one bigger shelter (level 2) needed to reach Society.")]
    [SerializeField] int societyPopulation = 10;
    [Tooltip("Minimum time spent in Society before a Crisis can begin — stops population racing straight into hardship.")]
    [SerializeField] float minSocietyDurationBeforeCrisis = 300f; // 5 min
    [SerializeField] float crisisDuration = 240f; // 4 min of real, mechanical hardship
    [Tooltip("Calm time in Legacy before the next Society/Crisis cycle (a new 'Age') can begin, for sessions that keep running.")]
    [SerializeField] float legacyCooldownBeforeNextCycle = 420f; // 7 min

    public CivilizationEra CurrentEra { get; private set; } = CivilizationEra.Arrival;
    public string EraAnnouncement { get; private set; } = "";
    public float EraAnnouncementTime { get; private set; } = -10f;
    public bool IsCrisisActive => CurrentEra == CivilizationEra.Crisis;
    public int CycleCount { get; private set; } // completed Society->Crisis->Legacy loops, for "Age N" labeling

    float eraStartTime;
    float worldStartTime;

    readonly List<string> notableEvents = new List<string>();
    public IReadOnlyList<string> NotableEvents => notableEvents;

    public float ElapsedSeconds => Time.time - worldStartTime;

    public string EraDisplayName => CurrentEra switch
    {
        CivilizationEra.Arrival => "Arrival",
        CivilizationEra.Settlement => "Settlement",
        CivilizationEra.Society => CycleCount > 0 ? $"Society — Age {CycleCount + 1}" : "Society",
        CivilizationEra.Crisis => "Crisis",
        CivilizationEra.Legacy => CycleCount > 0 ? $"Legacy — Age {CycleCount + 1}" : "Legacy",
        _ => ""
    };

    void Awake()
    {
        Instance = this;
        worldStartTime = Time.time;
        eraStartTime = Time.time;
    }

    /// <summary>Any system can call this to add a line to the running history (deaths, births, shrines, big builds, era changes).</summary>
    public void LogEvent(string text)
    {
        notableEvents.Add($"[{FormatElapsed(ElapsedSeconds)}] {text}");
        if (notableEvents.Count > 60) notableEvents.RemoveAt(0);
    }

    static string FormatElapsed(float seconds)
    {
        int m = Mathf.FloorToInt(seconds / 60f);
        int s = Mathf.FloorToInt(seconds % 60f);
        return $"{m}:{s:D2}";
    }

    void Update()
    {
        switch (CurrentEra)
        {
            case CivilizationEra.Arrival: CheckArrival(); break;
            case CivilizationEra.Settlement: CheckSettlement(); break;
            case CivilizationEra.Society: CheckSociety(); break;
            case CivilizationEra.Crisis: CheckCrisis(); break;
            case CivilizationEra.Legacy: CheckLegacy(); break;
        }
    }

    int PopulationCount() => CreatureMind.All.Count;

    bool AnyCampAtLevel(int level)
    {
        var bodies = CreatureBody.All;
        for (int i = 0; i < bodies.Count; i++)
        {
            var b = bodies[i];
            if (b != null && b.Camp != null && b.Camp.ShelterLevel >= level) return true;
        }
        return false;
    }

    void CheckArrival()
    {
        if (PopulationCount() >= settlementPopulation && AnyCampAtLevel(1))
            AdvanceTo(CivilizationEra.Settlement, "The first shelter stands — a settlement begins to take root.");
    }

    void CheckSettlement()
    {
        if (PopulationCount() >= societyPopulation && AnyCampAtLevel(2))
            AdvanceTo(CivilizationEra.Society, "The settlement has grown into a true society.");
    }

    void CheckSociety()
    {
        if (Time.time - eraStartTime < minSocietyDurationBeforeCrisis) return;
        TriggerCrisis();
    }

    void TriggerCrisis()
    {
        AdvanceTo(CivilizationEra.Crisis, CycleCount == 0
            ? "A great storm gathers, and danger rises — the first true test of what has been built."
            : "Once again the sky darkens, testing what this new age has built.");

        WeatherSystem.Instance?.ForceStorm(crisisDuration);
    }

    void CheckCrisis()
    {
        if (Time.time - eraStartTime >= crisisDuration)
            AdvanceTo(CivilizationEra.Legacy, "The storm has passed. What remains stands as this age's legacy.");
    }

    void CheckLegacy()
    {
        if (Time.time - eraStartTime >= legacyCooldownBeforeNextCycle)
        {
            CycleCount++;
            AdvanceTo(CivilizationEra.Society, "A new age of growth begins.");
        }
    }

    void AdvanceTo(CivilizationEra era, string announcement)
    {
        CurrentEra = era;
        eraStartTime = Time.time;
        EraAnnouncement = announcement;
        EraAnnouncementTime = Time.time;
        LogEvent(announcement);
        Debug.Log($"[Story] {era}: {announcement}");
    }
}
