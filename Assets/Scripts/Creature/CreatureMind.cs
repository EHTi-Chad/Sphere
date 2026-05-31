using System.Collections.Generic;
using UnityEngine;

public class CreatureMind : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] string creatureName;
    [SerializeField] PersonalityType personality;

    [Header("Needs (0-1, 1 = desperate)")]
    [SerializeField] float hunger = 0.3f;
    [SerializeField] float safety = 0.2f;
    [SerializeField] float social = 0.4f;
    [SerializeField] float awe = 0.1f;

    [Header("Tuning")]
    [SerializeField] float hungerRate = 0.02f;
    [SerializeField] float socialDecayRate = 0.01f;
    [SerializeField] float safetyDecayRate = 0.015f;

    string currentBelief = "The world is new and uncertain.";
    string lastSay = "";
    string lastReasoning = "";
    List<string> memories = new List<string>();
    const int MaxMemories = 20;

    public string CreatureName => creatureName;
    public PersonalityType Personality => personality;
    public float Hunger => hunger;
    public float Safety => safety;
    public float Social => social;
    public float Awe => awe;
    public string CurrentBelief => currentBelief;
    public string LastSay => lastSay;
    public string LastReasoning => lastReasoning;
    public IReadOnlyList<string> Memories => memories;
    public bool IsInDaylight { get; private set; } = true;
    public float LastThinkTime { get; set; }

    void Update()
    {
        hunger = Mathf.Clamp01(hunger + hungerRate * Time.deltaTime);
        social = Mathf.Clamp01(social + socialDecayRate * Time.deltaTime);

        if (DayNightCycle.Instance != null)
        {
            float sunExposure = DayNightCycle.Instance.GetSunExposure(transform.position);
            IsInDaylight = sunExposure > 0.3f;

            if (IsInDaylight)
            {
                safety = Mathf.Clamp01(safety - safetyDecayRate * 0.5f * Time.deltaTime);
            }
            else
            {
                float nightAnxiety = safetyDecayRate * (2f + safety);
                safety = Mathf.Clamp01(safety + nightAnxiety * Time.deltaTime);
            }
        }
        else
        {
            safety = Mathf.Clamp01(safety + safetyDecayRate * Time.deltaTime);
        }
    }

    public void SetIdentity(string name, PersonalityType type)
    {
        creatureName = name;
        personality = type;
    }

    public void AddMemory(string memory)
    {
        memories.Add(memory);
        if (memories.Count > MaxMemories)
            memories.RemoveAt(0);
    }

    public void AddGodMemory(string memory)
    {
        memories.Insert(0, "[GOD EVENT] " + memory);
        if (memories.Count > MaxMemories)
            memories.RemoveAt(memories.Count - 1);
    }

    public void HearGossip(string from, string message)
    {
        AddMemory($"{from} told me: \"{message}\"");
    }

    public void SatisfyHunger(float amount)
    {
        hunger = Mathf.Clamp01(hunger - amount);
    }

    public void FeelThreat(float amount)
    {
        safety = Mathf.Clamp01(safety + amount);
    }

    public void FeelAwe(float amount)
    {
        awe = Mathf.Clamp01(awe + amount);
    }

    public void FeelSocial(float amount)
    {
        social = Mathf.Clamp01(social - amount);
    }

    public void ApplyLLMResponse(string reasoning, string belief, string say, CreatureGoal goal, float intensity)
    {
        lastReasoning = reasoning;
        currentBelief = belief;
        lastSay = say;
        LastThinkTime = Time.time;
        GetComponent<CreatureBody>().SetGoal(goal, intensity);
    }

    public string BuildPrompt()
    {
        string memoryBlock = memories.Count > 0
            ? string.Join("\n", memories.GetRange(Mathf.Max(0, memories.Count - 6), Mathf.Min(6, memories.Count)))
            : "(no memories yet)";

        string timeOfDay = IsInDaylight ? "DAY — the sun is shining on you" : "NIGHT — darkness surrounds you, the world feels dangerous";

        return $@"You are {creatureName}, a small creature living on a spherical world. A god watches from above.

PERSONALITY: {personality} — this deeply shapes how you interpret events and make decisions.

TIME: {timeOfDay}

CURRENT NEEDS (0=satisfied, 1=desperate):
- Hunger: {hunger:F2}
- Safety: {safety:F2}
- Social: {social:F2}
- Awe: {awe:F2}

CURRENT BELIEF ABOUT THE GOD: {currentBelief}

RECENT MEMORIES:
{memoryBlock}

Weigh your needs through your personality and beliefs. Decide what to do next.
Respond in JSON only:
{{
  ""reasoning"": ""brief inner thought"",
  ""goal"": ""WANDER|FORAGE|FLEE|SEEK_OTHERS|HUDDLE|WORSHIP|REST"",
  ""intensity"": 0.0-1.0,
  ""say"": ""what you say aloud (or empty string)"",
  ""belief"": ""your current belief about the god""
}}";
    }
}

public enum PersonalityType
{
    Cautious,
    Zealous,
    Skeptical,
    Social,
    Pragmatic
}
