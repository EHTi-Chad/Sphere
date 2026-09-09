using System.Collections.Generic;
using UnityEngine;

public class CreatureMind : MonoBehaviour
{
    // Live registry so UI/overlays don't allocate via FindObjectsByType every frame.
    public static readonly List<CreatureMind> All = new List<CreatureMind>();
    void OnEnable() { All.Add(this); BirthTime = Time.time; }
    void OnDisable() { All.Remove(this); }

    /// <summary>When this creature entered the world — lets the idle camera tour prioritize showing
    /// off fresh arrivals (see OrbitalCamera.PickInterestingCreature).</summary>
    public float BirthTime { get; private set; }

    // "Reload Domain" is disabled for fast/hang-free play iteration, so statics survive between play
    // sessions — clear this registry at the start of each one so we never carry stale entries.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() => All.Clear();

    [Header("Identity")]
    [SerializeField] string creatureName;
    [SerializeField] PersonalityType personality;

    [Header("Needs (0-1, 1 = desperate)")]
    [SerializeField] float hunger = 0.12f;
    [SerializeField] float thirst = 0.12f;
    [SerializeField] float safety = 0.1f;
    [SerializeField] float social = 0.2f;
    [SerializeField] float awe = 0.1f;

    [Header("Tuning")]
    [SerializeField] float hungerRate = 0.006f;
    [SerializeField] float thirstRate = 0.008f;
    [SerializeField] float socialDecayRate = 0.005f;
    [SerializeField] float safetyDecayRate = 0.01f;

    string currentBelief = "The world is new and uncertain.";
    string lastSay = "";
    string lastReasoning = "";
    List<string> memories = new List<string>();
    const int MaxMemories = 20;

    // Relationship tracking
    Dictionary<string, float> relationships = new Dictionary<string, float>();

    public string CreatureName => creatureName;
    public PersonalityType Personality => personality;
    public float Hunger => hunger;
    public float Thirst => thirst;
    public float Safety => safety;
    public float Social => social;
    public float Awe => awe;
    public string CurrentBelief => currentBelief;
    public string LastSay => lastSay;
    public string LastReasoning => lastReasoning;
    public IReadOnlyList<string> Memories => memories;
    public bool IsInDaylight { get; private set; } = true;
    public float LastThinkTime { get; set; }
    public float LastSayTime { get; private set; }

    /// <summary>Set a short spoken line — shows as a bubble above the creature's head.</summary>
    public void Say(string text)
    {
        lastSay = text;
        LastSayTime = Time.time;
    }

    void Update()
    {
        hunger = Mathf.Clamp01(hunger + hungerRate * Time.deltaTime);
        thirst = Mathf.Clamp01(thirst + thirstRate * Time.deltaTime);
        social = Mathf.Clamp01(social + socialDecayRate * Time.deltaTime);

        if (DayNightCycle.Instance != null)
        {
            // Day/night is LOCAL to where the creature stands on the globe: the half facing the sun
            // lives its day while the far half has its night, both at the same time. (Previously this
            // used the global IsDay flag, which flipped every creature on the planet at once.)
            IsInDaylight = DayNightCycle.Instance.IsDaytime(transform.position);

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

    /// <summary>Player-driven rename (God Powers UI) — keeps personality, just changes the display name.</summary>
    public void Rename(string newName)
    {
        if (!string.IsNullOrWhiteSpace(newName))
            creatureName = newName.Trim();
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
        AdjustRelationship(from, 0.05f);
    }

    public void SatisfyHunger(float amount)
    {
        hunger = Mathf.Clamp01(hunger - amount);
    }

    public void Quench(float amount)
    {
        thirst = Mathf.Clamp01(thirst - amount);
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

    public void AdjustRelationship(string otherName, float delta)
    {
        if (!relationships.ContainsKey(otherName))
            relationships[otherName] = 0f;
        relationships[otherName] = Mathf.Clamp(relationships[otherName] + delta, -1f, 1f);
    }

    public float GetRelationship(string otherName)
    {
        return relationships.TryGetValue(otherName, out float val) ? val : 0f;
    }

    // ---- Reputation (social emergence) ----
    public int TimesShared { get; private set; }
    public int TimesStolen { get; private set; }
    public int TimesTraded { get; private set; }

    public string ReputationLabel
    {
        get
        {
            if (TimesStolen >= 2 && TimesStolen > TimesShared) return "thief";
            if (TimesShared >= 2 && TimesShared > TimesStolen) return "generous";
            if (TimesStolen >= 1) return "untrustworthy";
            if (TimesShared >= 1) return "kind";
            return "unknown";
        }
    }

    public void RegisterShare(string toName)
    {
        TimesShared++;
        AdjustRelationship(toName, 0.2f);
        AddMemory($"I shared food with {toName}.");
    }

    public void ReceiveGift(string fromName)
    {
        AdjustRelationship(fromName, 0.25f);
        AddMemory($"{fromName} shared food with me — they are generous.");
    }

    public void RegisterTheftCommitted()
    {
        TimesStolen++;
    }

    public void RegisterTraded(string withName)
    {
        TimesTraded++;
        AdjustRelationship(withName, 0.15f);
        AddMemory($"I traded resources with {withName}.");
    }

    // ---- Save / load ----
    public void ExportInto(CreatureSave cs)
    {
        cs.name = creatureName;
        cs.personality = (int)personality;
        cs.hunger = hunger; cs.thirst = thirst; cs.safety = safety; cs.social = social; cs.awe = awe;
        cs.belief = currentBelief;
        cs.lastSay = lastSay;
        cs.memories = new List<string>(memories);
        foreach (var kv in relationships) { cs.relNames.Add(kv.Key); cs.relValues.Add(kv.Value); }
        cs.timesShared = TimesShared; cs.timesStolen = TimesStolen; cs.timesTraded = TimesTraded;
    }

    public void ApplySave(CreatureSave cs)
    {
        hunger = cs.hunger; thirst = cs.thirst; safety = cs.safety; social = cs.social; awe = cs.awe;
        currentBelief = cs.belief;
        lastSay = cs.lastSay;
        memories = new List<string>(cs.memories);
        relationships.Clear();
        for (int i = 0; i < cs.relNames.Count && i < cs.relValues.Count; i++)
            relationships[cs.relNames[i]] = cs.relValues[i];
        TimesShared = cs.timesShared; TimesStolen = cs.timesStolen; TimesTraded = cs.timesTraded;
    }

    public void ApplyLLMResponse(string reasoning, string belief, string say, CreatureGoal goal, float intensity)
    {
        lastReasoning = reasoning;
        currentBelief = belief;
        lastSay = say;
        LastThinkTime = Time.time;
        if (!string.IsNullOrEmpty(say)) LastSayTime = Time.time;
        GetComponent<CreatureBody>().SetGoal(goal, intensity);
    }

    public string BuildPrompt()
    {
        string memoryBlock = memories.Count > 0
            ? string.Join("\n", memories.GetRange(Mathf.Max(0, memories.Count - 6), Mathf.Min(6, memories.Count)))
            : "(no memories yet)";

        string timeOfDay = IsInDaylight
            ? "DAY — the sun warms the land. Good time to forage, explore, and socialize."
            : "NIGHT — darkness and cold. Most creatures rest near home. Dangers lurk.";

        var body = GetComponent<CreatureBody>();

        string stageLine = body.Stage switch
        {
            CreatureLifeStage.Orienting => "You have JUST ARRIVED in this world. Take it in — observe your surroundings and assess your needs. Don't rush.",
            CreatureLifeStage.Securing => "You have no proper home yet. Your priority is SECURITY: gather wood and stone, BUILD a shelter, then BUILD a fire pit. Seek food and water only if you grow desperate.",
            _ => "Your shelter and fire are established. Live well: find food and water, improve your home, and tend to others."
        };

        // Build situation awareness
        string situation = "";
        if (body.NearestCreature != null && body.NearestCreatureDistance < 20f)
        {
            var otherMind = body.NearestCreature.GetComponent<CreatureMind>();
            if (otherMind != null)
            {
                float rel = GetRelationship(otherMind.CreatureName);
                string relDesc = rel > 0.3f ? "friendly" : rel < -0.3f ? "hostile" : "neutral";
                string otherState = otherMind.Hunger > 0.7f ? " They look STARVING." : otherMind.Hunger > 0.4f ? " They look hungry." : "";
                situation += $"\nNEARBY: {otherMind.CreatureName} ({otherMind.Personality}, known as {otherMind.ReputationLabel}) is {body.NearestCreatureDistance:F0} units away. Your relationship: {relDesc} ({rel:F1}).{otherState}";
            }
        }

        if (body.HasHome)
        {
            float distHome = Vector3.Distance(transform.position, body.HomePoint);
            string shelterName = body.Camp != null ? body.Camp.GetShelterName() : "open ground";
            int shelterLvl = body.Camp != null ? body.Camp.ShelterLevel : 0;
            situation += $"\nHOME: Your {shelterName} is {distHome:F0} units away. (shelter level {shelterLvl}/3)";

            if (body.Camp != null)
            {
                float food = body.Camp.GetStock(ResourceNode.ResourceType.Berry);
                float meat = body.Camp.GetStock(ResourceNode.ResourceType.Meat);
                float wood = body.Camp.GetStock(ResourceNode.ResourceType.Wood);
                float stone = body.Camp.GetStock(ResourceNode.ResourceType.Stone);
                situation += $"\nCAMP STOCKPILE: Berries:{food:F0}  Meat:{meat:F0}  Wood:{wood:F0}  Stone:{stone:F0}";

                if (body.Camp.CanUpgrade())
                    situation += "\n** You have enough resources to UPGRADE your shelter! Use BUILD. **";
                else if (shelterLvl < 3)
                {
                    // Reads the actual cost from CreatureCamp (the one real source of truth) instead of
                    // a hand-copied literal table — the previous copy here had drifted out of sync with
                    // the real costs (3/8/15 vs the actual 4/10/18), silently telling creatures they
                    // needed less material than CanUpgrade() actually required.
                    float needWood = Mathf.Max(0, body.Camp.NextUpgradeWoodCost - wood);
                    float needStone = Mathf.Max(0, body.Camp.NextUpgradeStoneCost - stone);
                    if (needWood > 0 || needStone > 0)
                        situation += $"\n  To upgrade: need {needWood:F0} more wood, {needStone:F0} more stone.";
                }
            }
        }

        if (body.TotalCarried > 0f)
        {
            string carrying = "";
            if (body.CarriedFood > 0) carrying += $"Berries:{body.CarriedFood:F0} ";
            if (body.CarriedMeat > 0) carrying += $"Meat:{body.CarriedMeat:F0} ";
            if (body.CarriedWood > 0) carrying += $"Wood:{body.CarriedWood:F0} ";
            if (body.CarriedStone > 0) carrying += $"Stone:{body.CarriedStone:F0} ";
            situation += $"\nCARRYING: {carrying.Trim()}";
        }

        // Wildlife awareness (checked at think-time, not every frame)
        var critters = Critter.All;
        float nearestCritter = float.MaxValue;
        for (int ci = 0; ci < critters.Count; ci++)
        {
            var c = critters[ci];
            if (c == null || !c.IsAlive) continue;
            float d = Vector3.Distance(transform.position, c.transform.position);
            if (d < nearestCritter) nearestCritter = d;
        }
        if (nearestCritter < 40f)
            situation += $"\nWILDLIFE: wild animals graze {nearestCritter:F0} units away — you could HUNT them for food.";

        // Predator awareness — the "watch out for" tier.
        var predators = Predator.All;
        float nearestPredator = float.MaxValue;
        for (int pi = 0; pi < predators.Count; pi++)
        {
            var p = predators[pi];
            if (p == null) continue;
            float d = Vector3.Distance(transform.position, p.transform.position);
            if (d < nearestPredator) nearestPredator = d;
        }
        if (nearestPredator < 35f)
            situation += $"\n** DANGER: a PREDATOR prowls {nearestPredator:F0} units away. FLEE to safety or keep well clear! **";

        // Water awareness
        var terr = SphericalWorld.Instance != null ? SphericalWorld.Instance.Terrain : null;
        if (terr != null && terr.FindNearestWater(transform.position, 60f, out Vector3 waterPt))
        {
            float wd = Vector3.Distance(transform.position, waterPt);
            situation += $"\nWATER: there is water {wd:F0} units away — DRINK there when thirsty.";
        }

        // Climate awareness — latitude/altitude temperature shapes survival strategy.
        if (terr != null)
        {
            float temp = terr.TemperatureAt(transform.position);
            if (temp < 0.3f)
                situation += "\nCLIMATE: it is FREEZING here (polar / high mountains) — shelter and a lit fire are vital to survive.";
            else if (temp > 0.78f)
                situation += "\nCLIMATE: it is hot here (near the equator) — you will grow thirsty faster, keep water close.";
        }

        // Weather awareness
        if (WeatherSystem.Instance != null && WeatherSystem.Instance.IsRaining)
            situation += WeatherSystem.Instance.IsStormy
                ? "\nWEATHER: a STORM rages — get to shelter, the cold and rain are dangerous."
                : "\nWEATHER: it is raining — you are getting wet and cold; shelter helps.";

        situation += $"\nHEALTH: {body.Health:F2}{(body.Health < 0.5f ? " [INJURED]" : "")}";
        situation += $"\nEXPOSURE: {body.Exposure:F2}{(body.Exposure > 0.5f ? " [DANGEROUS — find shelter!]" : "")}";

        string personalityDesc = personality switch
        {
            PersonalityType.Cautious => "Cautious — you avoid risk, prefer safety, and are wary of strangers. At night you always seek shelter.",
            PersonalityType.Zealous => "Zealous — you are passionate and aggressive. You may hunt at night if hungry. You feel strongly about the god.",
            PersonalityType.Skeptical => "Skeptical — you question everything, including the god. You rely on yourself and trust few others.",
            PersonalityType.Social => "Social — you crave companionship. Being alone at night terrifies you. You seek others and share freely.",
            PersonalityType.Pragmatic => "Pragmatic — you do what's necessary to survive. You will hunt if starving. You trade and cooperate when useful.",
            _ => personality.ToString()
        };

        return $@"You are {creatureName}, a small creature living on a spherical world. A god watches from above.

PERSONALITY: {personalityDesc}

TIME: {timeOfDay}

LIFE STAGE: {stageLine}

CURRENT NEEDS (0=satisfied, 1=desperate):
- Hunger: {hunger:F2} {(hunger > 0.7f ? "[STARVING]" : hunger > 0.4f ? "[hungry]" : "")}
- Thirst: {thirst:F2} {(thirst > 0.7f ? "[PARCHED]" : thirst > 0.4f ? "[thirsty]" : "")}
- Safety: {safety:F2} {(safety > 0.7f ? "[TERRIFIED]" : safety > 0.4f ? "[anxious]" : "")}
- Social: {social:F2} {(social > 0.7f ? "[LONELY]" : social > 0.4f ? "[wanting company]" : "")}
- Awe: {awe:F2}
{situation}

CURRENT BELIEF ABOUT THE GOD: {currentBelief}

RECENT MEMORIES:
{memoryBlock}

RULES:
- DRINK: go to the nearest river, lake, or shore and drink to quench your thirst.
- FORAGE: gather food (berries). Eat some, carry the rest home.
- GATHER: collect wood or stone and bring them to your camp. Needed to build shelter.
- BUILD: go home and upgrade your shelter (lean-to → hut → house). Needs enough wood and stone.
- REST: return home and sleep. Recovers health. Better shelter = more protection at night.
- HUNT: chase and catch wild animals for food — this is the normal way to hunt. Only the desperate or aggressive turn HUNT on other creatures, which is also how you take revenge on someone who wronged you.
- SEEK_OTHERS: approach nearby creatures to socialize.
- SHARE: give some of your food to a nearby creature in need. Builds friendship and a generous reputation.
- TRADE: swap resources you have spare for ones you lack with a nearby creature (e.g. your wood for their stone).
- HUDDLE: stay close to others for safety at night.
- FLEE: run from danger.

PRIORITIES:
- Survival first: if parched, DRINK. If starving, FORAGE or HUNT. If health is low, REST.
- Shelter matters: exposure at night without shelter damages health. BUILD when you have resources.
- During DAY: good time to FORAGE, GATHER resources, BUILD, explore, and socialize.
- During NIGHT: REST at home unless desperate. Better shelter = safer night.
- Your personality shapes HOW you prioritize — a cautious creature builds early, a social one seeks company first, a pragmatic one stockpiles.
- Social ties matter: SHARE food and TRADE to build trust and a good reputation. Generous creatures are loved; thieves are shunned. Don't share or trade with someone you distrust, and remember who wronged you — you may seek revenge.

Decide what to do next based on your situation.
Respond in JSON only:
{{
  ""reasoning"": ""brief inner thought about your situation and plan"",
  ""goal"": ""WANDER|FORAGE|DRINK|GATHER|BUILD|FLEE|SEEK_OTHERS|SHARE|TRADE|HUDDLE|WORSHIP|REST|HUNT"",
  ""intensity"": 0.0-1.0,
  ""say"": ""what you say aloud to nearby creatures (or empty string)"",
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
