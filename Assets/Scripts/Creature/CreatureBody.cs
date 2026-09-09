using System.Collections.Generic;
using UnityEngine;

public class CreatureBody : MonoBehaviour
{
    // Live registry so per-frame scans don't allocate via FindObjectsByType (avoids GC hitches).
    public static readonly List<CreatureBody> All = new List<CreatureBody>();
    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    // "Reload Domain" is disabled for fast/hang-free play iteration, so statics survive between play
    // sessions — clear this registry at the start of each one so we never carry stale entries.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() => All.Clear();

    // Slowed ~25% (was 4) as part of the broader "make it drawn out" pass — a calmer base walking
    // pace so wandering/gathering/building treks take longer and read as deliberate rather than
    // scurrying. FLEE/HUNT still force a speed floor + multiplier (see ApplyMovement), so predators
    // (scaled down by the same ratio, see Predator.cs) remain exactly as escapable as before.
    [SerializeField] float moveSpeed = 3f;
    [SerializeField] float rotationSpeed = 8f;
    [SerializeField] float heightOffset = 0.5f;

    [Header("Perception")]
    [SerializeField] float sightRange = 40f;
    [SerializeField] float encounterRange = 5f;
    [SerializeField] float homeRadius = 8f;

    Vector3 moveDirection;
    Vector3 currentForward;
    CreatureGoal currentGoal = CreatureGoal.WANDER;
    float goalIntensity = 0.5f;

    float wanderTimer;
    Vector3 wanderTarget;
    Vector3 fleeFrom;
    float fleeTimer;

    // Home territory
    Vector3 homePoint;
    bool hasHome;
    CreatureCamp camp;

    // Encounter tracking
    float encounterCooldown;

    // Resource gathering
    float carriedFood;
    float carriedStone;
    float carriedWood;
    float carriedMeat;
    ResourceNode targetResource;
    float harvestCooldown;
    ResourceNode.ResourceType gatherTarget = ResourceNode.ResourceType.Berry;

    // Building — stay at the site while camp.IsUnderConstruction, then react once it finishes.
    bool awaitingConstruction;
    bool awaitingFirePit;

    // Drinking
    Vector3 drinkTarget;
    bool hasDrinkTarget;
    float drinkTimer;

    // Health
    float health = 1f;
    float exposure; // builds up at night without shelter

    // Life stage: arrive & orient → secure shelter+fire → sustain (food/water/social)
    [SerializeField] float orientationDuration = 6f;
    CreatureLifeStage stage = CreatureLifeStage.Orienting;
    float orientTimer;
    public CreatureLifeStage Stage => stage;

    // Reproduction — a thriving, secured, well-fed creature brings new life to its camp, but rarely:
    // roughly once every reproGestationDays in-game days, not every real-time minute. The very FIRST
    // birth for a creature uses a much shorter timer instead — otherwise, with the full gestation
    // period running 25-40+ real minutes, a player could easily never witness the mechanic at all in
    // a normal session. Every birth after that first one goes back to the slow, rare rate.
    [SerializeField] float reproGestationDays = 10f;    // steady-state gap between a creature's children
    [SerializeField] float firstBirthMinMinutes = 4f;   // first child can arrive this soon once settled...
    [SerializeField] float firstBirthMaxMinutes = 7f;   // ...but no later than this
    float reproTimer = 30f;          // set properly in Start once the day length is known
    const int PopulationCap = 150;

    // A full gestation in real seconds, scaled to the world's day length.
    float GestationSeconds()
    {
        float dayLen = DayNightCycle.Instance != null ? DayNightCycle.Instance.DayLengthSeconds : 240f;
        return reproGestationDays * dayLen;
    }

    SphericalWorld world;
    CreatureMind mind;

    // Set before Start when loading from a save.
    CreatureSave pendingSave;
    public void PrimeFromSave(CreatureSave cs) { pendingSave = cs; }

    public CreatureGoal CurrentGoal => currentGoal;
    public float GoalIntensity => goalIntensity;
    public Vector3 HomePoint => homePoint;
    public bool HasHome => hasHome;
    public CreatureCamp Camp => camp;
    public float CarriedFood => carriedFood;
    public float CarriedStone => carriedStone;
    public float CarriedWood => carriedWood;
    public float CarriedMeat => carriedMeat;
    public float CarriedEdible => carriedFood + carriedMeat;
    public float TotalCarried => carriedFood + carriedStone + carriedWood + carriedMeat;

    // Zealous/Pragmatic creatures hunt rather than forage/flee when desperate — this exact check was
    // written out independently four separate times across the goal-decision logic; extracted here so
    // a future personality-tuning change can't silently drift between the copies.
    bool IsAggressive => mind != null && (mind.Personality == PersonalityType.Zealous || mind.Personality == PersonalityType.Pragmatic);

    // Consume carried food for sharing/eating — meat first, then berries.
    void TakeEdible(float amount)
    {
        float m = Mathf.Min(carriedMeat, amount);
        carriedMeat -= m; amount -= m;
        carriedFood = Mathf.Max(0f, carriedFood - amount);
    }
    public float Health => health;
    public float Exposure => exposure;
    public CreatureBody NearestCreature { get; private set; }
    public float NearestCreatureDistance { get; private set; }

    void Start()
    {
        world = SphericalWorld.Instance;
        mind = GetComponent<CreatureMind>();
        if (world == null) return;

        transform.position = world.SnapToSurface(transform.position, heightOffset);
        currentForward = Vector3.ProjectOnPlane(transform.forward, world.GetSurfaceNormal(transform.position)).normalized;

        Vector3 campPoint;
        bool loaded = pendingSave != null;
        if (loaded)
        {
            health = pendingSave.health;
            exposure = pendingSave.exposure;
            carriedFood = pendingSave.carriedFood;
            carriedWood = pendingSave.carriedWood;
            carriedStone = pendingSave.carriedStone;
            carriedMeat = pendingSave.carriedMeat;
            campPoint = new Vector3(pendingSave.campX, pendingSave.campY, pendingSave.campZ);
        }
        else
        {
            campPoint = transform.position;
        }

        homePoint = campPoint;
        hasHome = true;

        var campObj = new GameObject($"Camp_{mind.CreatureName}");
        camp = campObj.AddComponent<CreatureCamp>();
        Vector3 up = world.GetSurfaceNormal(homePoint);
        camp.Init(mind.CreatureName, homePoint, up);

        if (loaded)
        {
            camp.RestoreState(pendingSave.shelterLevel, pendingSave.campFood, pendingSave.campWood,
                              pendingSave.campStone, pendingSave.campMeat, pendingSave.campHasFirePit);
            stage = (CreatureLifeStage)pendingSave.lifeStage; // resume where they left off
        }
        else
        {
            stage = CreatureLifeStage.Orienting;
            orientTimer = orientationDuration;
        }
        pendingSave = null;

        // First birth is fast-tracked (a few minutes) so the mechanic is actually witnessed — it still
        // won't fire until the creature is Sustaining/healthy/fed/sheltered regardless, so this floor
        // rarely ends up being the binding constraint anyway. Subsequent births use the full, rare
        // gestation period (set after a birth lands, below).
        reproTimer = Random.Range(firstBirthMinMinutes, firstBirthMaxMinutes) * 60f;

        PickNewWanderTarget();
    }

    void Update()
    {
        if (world == null || dead) return;

        encounterCooldown -= Time.deltaTime;
        harvestCooldown -= Time.deltaTime;
        ScanForNearby();
        CheckEncounters();
        UpdateExposure();
        if (dead) return; // exposure/starvation/thirst may have just killed it

        CheckPredatorThreat(); // a prowling predator overrides everything — run!

        // Newly-arrived creatures get their bearings before doing anything (unless fleeing a predator).
        if (stage == CreatureLifeStage.Orienting)
        {
            if (currentGoal == CreatureGoal.FLEE) UpdateFlee(); else UpdateObserve();
            ApplyMovement();
            return;
        }

        // When the LLM sets GATHER, pick the right resource type
        if (currentGoal == CreatureGoal.GATHER && gatherTarget == ResourceNode.ResourceType.Berry)
            PickGatherTarget();

        // Fast-layer drives behavior when no LLM goal is active
        if (mind != null)
            FastLayerDecision();

        switch (currentGoal)
        {
            case CreatureGoal.WANDER: UpdateWander(); break;
            case CreatureGoal.FORAGE: UpdateGather(ResourceNode.ResourceType.Berry); break;
            case CreatureGoal.GATHER: UpdateGather(gatherTarget); break;
            case CreatureGoal.FLEE: UpdateFlee(); break;
            case CreatureGoal.SEEK_OTHERS: UpdateSeekOthers(); break;
            case CreatureGoal.HUDDLE: UpdateHuddle(); break;
            case CreatureGoal.REST: UpdateRest(); break;
            case CreatureGoal.HUNT: UpdateHunt(); break;
            case CreatureGoal.BUILD: UpdateBuild(); break;
            case CreatureGoal.SHARE: UpdateShare(); break;
            case CreatureGoal.TRADE: UpdateTrade(); break;
            case CreatureGoal.DRINK: UpdateDrink(); break;
            case CreatureGoal.WORSHIP: UpdateWander(); break;
            default: UpdateWander(); break;
        }

        UpdateReproduction();
        ApplyMovement();
    }

    void UpdateReproduction()
    {
        reproTimer -= Time.deltaTime;
        if (reproTimer > 0f) return;
        reproTimer = 6f; // recheck cadence

        // Only thriving, settled creatures parent: secured home, healthy, fed, hydrated, by daylight,
        // with a food surplus to feed the newborn, and only while the world isn't already full.
        if (stage != CreatureLifeStage.Sustaining) return;
        if (mind == null || camp == null || CreatureSpawner.Instance == null) return;
        if (All.Count >= PopulationCap) return;
        if (camp.ShelterLevel < 1) return;
        if (health < 0.7f) return;
        if (mind.Hunger > 0.35f || mind.Thirst > 0.35f) return;
        if (!mind.IsInDaylight) return;
        if (camp.FoodStock() < 5f) return;
        if (Vector3.Distance(transform.position, homePoint) > homeRadius) return;

        // A child is born — it costs stored food and forces a long recovery before parenting again.
        float fed = camp.Steal(ResourceNode.ResourceType.Berry, 3f);
        if (fed < 3f) camp.Steal(ResourceNode.ResourceType.Meat, 3f - fed);

        // Pick a spot near the camp to place the newborn — retried a few times against IsLand so a
        // coastal or lakeside camp (common, since creatures like water nearby) doesn't have a chance of
        // dropping the child straight into the water on an unlucky direction.
        Vector3 up = world.GetSurfaceNormal(homePoint);
        Vector3 childPos = homePoint;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector3 tangent = Vector3.ProjectOnPlane(Random.onUnitSphere, up).normalized;
            Vector3 candidate = homePoint + tangent * Random.Range(2.5f, 4f);
            bool nearRiverOrLake = world.Terrain != null && world.Terrain.IsWaterNear(candidate, 2f);
            // Plain IsLand has no elevation margin (anything a hair above sea level counts) — a coastal
            // camp's random direction can land right at the shoreline, same class of bug fixed in
            // GetRandomLandPoint. Require the same real clearance here.
            Vector3 candidateDir = (candidate - world.Center).normalized;
            bool clearOfShore = world.Terrain == null || world.Terrain.GetLandHeight(candidateDir) >= 0.1f;
            if (clearOfShore && !nearRiverOrLake)
            {
                childPos = world.SnapToSurface(candidate);
                break;
            }
        }

        Color childColor = Color.white;
        var rend = GetComponentInChildren<MeshRenderer>();
        if (rend != null && rend.sharedMaterial != null) childColor = rend.sharedMaterial.color;

        CreatureSpawner.Instance.SpawnChild(childPos, mind.Personality, childColor, mind.CreatureName);

        reproTimer = GestationSeconds() * Random.Range(0.9f, 1.3f);
        mind.Say("New life joins us!");
        mind.AddMemory("I brought a little one into the world.");
        Debug.Log($"[Birth] {mind.CreatureName} had a child near their camp.");
        StoryDirector.Instance?.LogEvent($"{mind.CreatureName} welcomed a new child into the world.");
    }

    void UpdateExposure()
    {
        bool isNight = mind != null && !mind.IsInDaylight;
        bool atHome = hasHome && Vector3.Distance(transform.position, homePoint) < homeRadius;
        float protection = camp != null ? camp.ShelterProtection : 0f;

        if (isNight && !atHome)
        {
            exposure += 0.03f * Time.deltaTime;
        }
        else if (isNight && atHome)
        {
            exposure += 0.03f * (1f - protection) * Time.deltaTime;
            exposure -= 0.01f * protection * Time.deltaTime;
        }
        else
        {
            exposure -= 0.05f * Time.deltaTime;
        }

        // Rain/storms chill unsheltered creatures (any time of day) — drives them to seek cover.
        float wetness = WeatherSystem.Instance != null ? WeatherSystem.Instance.Wetness : 0f;
        if (wetness > 0f)
            exposure += wetness * 0.03f * (1f - protection) * Time.deltaTime;

        // Cold climate (poles / high mountains) chills creatures. Shelter and a lit fire keep them warm,
        // so life near the caps demands securing both — life at the equator is far more forgiving.
        if (world != null && world.Terrain != null)
        {
            float temp = world.Terrain.TemperatureAt(transform.position);
            if (temp < 0.35f)
            {
                float warmth = protection;
                if (atHome && camp != null && camp.HasFirePit) warmth = Mathf.Clamp01(warmth + 0.45f);
                exposure += (0.35f - temp) * 0.10f * (1f - warmth) * Time.deltaTime;
            }
        }

        exposure = Mathf.Clamp01(exposure);

        // Exposure damages health — gentler now. Creatures still securing their first home get a
        // grace discount so the unavoidable first night doesn't simply wipe out the starting group.
        if (exposure > 0.5f)
        {
            float exposureDamage = (exposure - 0.5f) * 0.006f;
            if (stage != CreatureLifeStage.Sustaining) exposureDamage *= 0.4f; // newcomer grace
            health -= exposureDamage * Time.deltaTime;
        }

        // Starvation damages health
        if (mind != null && mind.Hunger > 0.9f)
            health -= 0.005f * Time.deltaTime;

        // Dehydration damages health
        if (mind != null && mind.Thirst > 0.9f)
            health -= 0.008f * Time.deltaTime;

        // Resting safely at home — fed and hydrated — heals fast.
        if (currentGoal == CreatureGoal.REST && atHome && mind != null && mind.Hunger < 0.6f && mind.Thirst < 0.6f)
            health += 0.03f * Time.deltaTime;
        // Otherwise a fed, hydrated, unexposed creature still mends slowly through the day, so a
        // rough night is a setback to recover from rather than the start of a death spiral.
        else if (mind != null && !isNight && health < 1f && exposure < 0.5f && mind.Hunger < 0.6f && mind.Thirst < 0.6f)
            health += 0.008f * Time.deltaTime;

        health = Mathf.Clamp01(health);

        if (health <= 0f) Die();
    }

    bool dead;

    void Die()
    {
        if (dead) return;
        dead = true;

        string who = mind != null ? mind.CreatureName : "A creature";
        Debug.Log($"[Death] {who} has died.");
        StoryDirector.Instance?.LogEvent($"{who} has died.");

        // Nearby creatures remember the death (feeds gossip/grief/belief).
        var minds = CreatureMind.All;
        for (int i = 0; i < minds.Count; i++)
        {
            var m = minds[i];
            if (m == mind || m == null) continue;
            if (Vector3.Distance(transform.position, m.transform.position) < sightRange)
                m.AddMemory($"{who} has died.");
        }

        if (mind != null) CognitionScheduler.Instance?.Unregister(mind);

        // Their camp stays behind as abandoned ruins; remove the creature itself.
        Destroy(gameObject);
    }

    float fastLayerTimer;

    void FastLayerDecision()
    {
        // Don't override flee, hunt, or active LLM goals too quickly
        if (currentGoal == CreatureGoal.FLEE || currentGoal == CreatureGoal.HUNT)
            return;

        // A staged construction is under way at camp — never override BUILD while it's in progress.
        // shelterLevel/HasFirePit already reflect the TARGET state the instant construction began (see
        // CreatureCamp.TryUpgrade/TryBuildFirePit), so CanUpgrade()/stock checks below would otherwise
        // see "already done" and send the creature off gathering for the NEXT tier while the current
        // one is still visibly going up.
        if (camp != null && camp.IsUnderConstruction) return;

        // Re-evaluate every few seconds, not every frame
        fastLayerTimer -= Time.deltaTime;
        if (fastLayerTimer > 0f && currentGoal != CreatureGoal.WANDER) return;
        fastLayerTimer = 3f;

        bool isNight = !mind.IsInDaylight;

        // EMERGENCY: health critical — rest immediately
        if (health < 0.3f && hasHome)
        {
            SetGoal(CreatureGoal.REST, 1f);
            return;
        }

        // Thirsty — go find water (day or night)
        if (mind.Thirst > 0.75f)
        {
            SetGoal(CreatureGoal.DRINK, mind.Thirst);
            return;
        }

        // Near-starvation emergency (overrides even securing so they don't die building)
        if (mind.Hunger > 0.85f)
        {
            SetGoal(IsAggressive && FindNearestCritter(sightRange) != null ? CreatureGoal.HUNT : CreatureGoal.FORAGE, 1f);
            return;
        }

        // SECURING: secure shelter + fire before settling into normal life
        if (stage == CreatureLifeStage.Securing)
        {
            if (DriveSecuring()) return;
            stage = CreatureLifeStage.Sustaining;
            mind?.AddMemory("My shelter and fire are ready. Now to truly live.");
            mind?.Say("My camp is secure. Now for food and water.");
        }

        if (isNight)
        {
            bool starving = mind.Hunger > 0.8f;

            if (starving && IsAggressive)
                SetGoal(CreatureGoal.HUNT, mind.Hunger);
            else if (starving)
                SetGoal(CreatureGoal.FORAGE, 0.7f);
            else if (mind.Safety > 0.6f && NearestCreature != null && NearestCreatureDistance < sightRange)
                SetGoal(CreatureGoal.HUDDLE, mind.Safety);
            else
                SetGoal(CreatureGoal.REST, 0.4f);
            return;
        }

        // DAYTIME PRIORITIES (in order):

        // 1. If carrying stuff, go deposit it
        if (TotalCarried > 2f && currentGoal != CreatureGoal.FORAGE && currentGoal != CreatureGoal.GATHER)
        {
            // Force a deposit run — UpdateGather handles this
            SetGoal(CreatureGoal.GATHER, 0.6f);
            return;
        }

        // 2. Can upgrade shelter? Do it!
        if (camp != null && camp.CanUpgrade())
        {
            SetGoal(CreatureGoal.BUILD, 0.9f);
            return;
        }

        // 3. Hungry? Get food first
        if (mind.Hunger > 0.4f)
        {
            // Eat from camp stockpile if at home and have food (meat first, then berries)
            if (camp != null && camp.FoodStock() > 0f
                && Vector3.Distance(transform.position, homePoint) < homeRadius)
            {
                float eaten = camp.Steal(ResourceNode.ResourceType.Meat, 1f);
                if (eaten <= 0f) eaten = camp.Steal(ResourceNode.ResourceType.Berry, 1f);
                mind.SatisfyHunger(0.2f * eaten);
                mind.AddMemory("I ate from my stockpile.");
            }
            else
            {
                // Aggressive creatures hunt nearby wildlife for food; others forage.
                if (IsAggressive && FindNearestCritter(sightRange) != null)
                    SetGoal(CreatureGoal.HUNT, mind.Hunger);
                else
                    SetGoal(CreatureGoal.FORAGE, mind.Hunger);
                return;
            }
        }

        // 4. Need materials for next shelter upgrade
        if (camp != null && camp.ShelterLevel < 3)
        {
            float woodNeeded = GetNextWoodCost() - camp.GetStock(ResourceNode.ResourceType.Wood);
            float stoneNeeded = GetNextStoneCost() - camp.GetStock(ResourceNode.ResourceType.Stone);

            if (woodNeeded > 0f)
            {
                SetGather(ResourceNode.ResourceType.Wood, 0.7f);
                return;
            }
            if (stoneNeeded > 0f)
            {
                SetGather(ResourceNode.ResourceType.Stone, 0.6f);
                return;
            }
        }

        // 5. Stockpile extra food for safety
        if (camp != null && camp.FoodStock() < 5f)
        {
            SetGoal(CreatureGoal.FORAGE, 0.5f);
            return;
        }

        // 6. Generous creatures share surplus food with the hungry
        if (mind.Personality == PersonalityType.Social && CarriedEdible >= 2f && FindNeediestNearby() != null)
        {
            SetGoal(CreatureGoal.SHARE, 0.7f);
            return;
        }

        // 7. Socialize if lonely
        if (mind.Social > 0.5f && NearestCreature != null && NearestCreatureDistance < sightRange)
        {
            SetGoal(CreatureGoal.SEEK_OTHERS, mind.Social);
            return;
        }

        // 8. Default: wander and explore
        if (currentGoal != CreatureGoal.WANDER)
            SetGoal(CreatureGoal.WANDER, 0.4f);
    }

    void PickGatherTarget()
    {
        if (camp == null) return;
        float woodNeeded = GetNextWoodCost() - camp.GetStock(ResourceNode.ResourceType.Wood);
        float stoneNeeded = GetNextStoneCost() - camp.GetStock(ResourceNode.ResourceType.Stone);

        if (woodNeeded > stoneNeeded)
            gatherTarget = ResourceNode.ResourceType.Wood;
        else if (stoneNeeded > 0)
            gatherTarget = ResourceNode.ResourceType.Stone;
        else
            gatherTarget = ResourceNode.ResourceType.Wood; // default to wood
    }

    public void SetGather(ResourceNode.ResourceType type, float intensity)
    {
        currentGoal = CreatureGoal.GATHER;
        gatherTarget = type;
        goalIntensity = Mathf.Clamp01(intensity);
        targetResource = null;
    }

    void ScanForNearby()
    {
        // Spatial-grid lookup instead of scanning the whole population (was O(n²) across all creatures
        // every frame). Bounded to sightRange — anything farther never mattered to behaviour anyway.
        NearestCreature = CreatureGrid.FindNearest(transform.position, this, sightRange, out float d);
        NearestCreatureDistance = d;
    }

    void CheckEncounters()
    {
        if (encounterCooldown > 0f) return;
        if (NearestCreature == null || NearestCreatureDistance > encounterRange) return;

        var otherMind = NearestCreature.GetComponent<CreatureMind>();
        if (otherMind == null) return;

        if (mind != null)
        {
            mind.AddMemory($"I encountered {otherMind.CreatureName} nearby.");
            mind.FeelSocial(0.15f);
            mind.AdjustRelationship(otherMind.CreatureName, 0.05f);

            float rel = mind.GetRelationship(otherMind.CreatureName);
            mind.Say(rel < -0.3f ? $"You again, {otherMind.CreatureName}..." : $"Hello, {otherMind.CreatureName}!");
        }

        encounterCooldown = 8f;
        CheckForTheft();
    }

    void CheckForTheft()
    {
        if (mind == null) return;

        bool wouldSteal = (mind.Hunger > 0.7f) ||
                          (mind.Personality == PersonalityType.Pragmatic && mind.Hunger > 0.4f) ||
                          (mind.Personality == PersonalityType.Zealous && mind.Hunger > 0.5f);

        if (!wouldSteal) return;

        var camps = FindObjectsByType<CreatureCamp>(FindObjectsSortMode.None);
        foreach (var otherCamp in camps)
        {
            if (otherCamp == camp) continue;
            float dist = Vector3.Distance(transform.position, otherCamp.transform.position);
            if (dist > 8f) continue;
            if (otherCamp.IsOwnerNearby()) continue;

            float stolen = otherCamp.Steal(ResourceNode.ResourceType.Berry, 1f);
            if (stolen > 0f)
            {
                mind.SatisfyHunger(0.2f);
                mind.RegisterTheftCommitted();
                mind.AddMemory($"I stole food from {otherCamp.OwnerName}'s camp while they were away!");
                mind.AdjustRelationship(otherCamp.OwnerName, -0.3f);

                var ownerMinds = FindObjectsByType<CreatureMind>(FindObjectsSortMode.None);
                foreach (var m in ownerMinds)
                {
                    if (m.CreatureName == otherCamp.OwnerName)
                        m.AddMemory($"My camp seems lighter... someone may have stolen from me.");
                }
                break;
            }
        }
    }

    void ApplyMovement()
    {
        Vector3 up = world.GetSurfaceNormal(transform.position);

        if (moveDirection.sqrMagnitude > 0.001f)
        {
            // HUNT and FLEE always sprint — don't let low goal-intensity make a predator
            // slower than its fleeing prey.
            float effIntensity = goalIntensity;
            if (currentGoal == CreatureGoal.HUNT || currentGoal == CreatureGoal.FLEE)
                effIntensity = Mathf.Max(goalIntensity, 0.85f);

            float speed = moveSpeed * effIntensity;
            if (currentGoal == CreatureGoal.REST) speed *= 0.1f;
            if (currentGoal == CreatureGoal.HUNT) speed *= 1.45f;
            if (currentGoal == CreatureGoal.FLEE) speed *= 1.6f;
            if (currentGoal == CreatureGoal.GATHER) speed *= 0.9f;

            // Fording — wade slowly through rivers and lakes (but never blocked, so no one gets stranded)
            if (currentGoal != CreatureGoal.DRINK && world.Terrain != null && world.Terrain.IsWaterNear(transform.position, 1.3f))
                speed *= 0.5f;

            Vector3 tangentMove = Vector3.ProjectOnPlane(moveDirection, up).normalized;
            Vector3 nextPos = transform.position + tangentMove * speed * Time.deltaTime;

            if (world.IsLand(nextPos))
            {
                transform.position = nextPos;
                currentForward = Vector3.Slerp(currentForward, tangentMove, Time.deltaTime * rotationSpeed);
            }
            else
            {
                // Blocked by water — slide along the shoreline rather than freezing, so a fleeing
                // creature can't be pinned against the coast by a predator.
                bool slid = false;
                float[] tries = { 45f, -45f, 90f, -90f, 135f, -135f };
                for (int t = 0; t < tries.Length; t++)
                {
                    Vector3 altDir = Quaternion.AngleAxis(tries[t], up) * tangentMove;
                    Vector3 altPos = transform.position + altDir * speed * Time.deltaTime;
                    if (world.IsLand(altPos))
                    {
                        transform.position = altPos;
                        currentForward = Vector3.Slerp(currentForward, altDir, Time.deltaTime * rotationSpeed);
                        slid = true;
                        break;
                    }
                }
                if (!slid) PickNewWanderTarget();
            }
        }

        transform.position = world.SnapToSurface(transform.position, heightOffset);
        transform.rotation = world.GetSurfaceRotation(transform.position, currentForward);
    }

    void UpdateWander()
    {
        wanderTimer -= Time.deltaTime;
        if (wanderTimer <= 0f) PickNewWanderTarget();
        moveDirection = (wanderTarget - transform.position).normalized;
        if (Vector3.Distance(transform.position, wanderTarget) < 2f) PickNewWanderTarget();
    }

    void UpdateGather(ResourceNode.ResourceType type)
    {
        // If carrying enough, head home to deposit
        if (TotalCarried >= 3f)
        {
            if (hasHome)
            {
                float distHome = Vector3.Distance(transform.position, homePoint);
                if (distHome > 3f)
                {
                    moveDirection = (homePoint - transform.position).normalized;
                    return;
                }
                else
                {
                    DepositAtCamp();
                    return;
                }
            }
        }

        // Find a resource node
        if (targetResource == null || targetResource.IsEmpty)
        {
            if (ResourceSpawner.Instance != null)
                targetResource = ResourceSpawner.Instance.FindNearest(transform.position, type, sightRange);

            // Fallback to any resource
            if (targetResource == null && ResourceSpawner.Instance != null)
                targetResource = ResourceSpawner.Instance.FindNearestAny(transform.position, sightRange);
        }

        if (targetResource != null && !targetResource.IsEmpty)
        {
            float dist = Vector3.Distance(transform.position, targetResource.transform.position);
            if (dist > 2f)
            {
                moveDirection = (targetResource.transform.position - transform.position).normalized;
            }
            else
            {
                moveDirection = Vector3.zero;
                if (harvestCooldown <= 0f)
                {
                    HarvestResource(targetResource);
                    harvestCooldown = 2.2f; // a beat longer between harvests — more trips, less tap-tap-tap
                }
            }
        }
        else
        {
            UpdateWander();
        }
    }

    void HarvestResource(ResourceNode node)
    {
        float taken = node.Harvest(1f);
        if (taken <= 0f) return;

        switch (node.Type)
        {
            case ResourceNode.ResourceType.Berry:
                carriedFood += taken;
                mind?.SatisfyHunger(0.1f);
                mind?.AddMemory("I gathered berries.");
                break;
            case ResourceNode.ResourceType.Stone:
                carriedStone += taken;
                mind?.AddMemory("I collected stones.");
                break;
            case ResourceNode.ResourceType.Wood:
                carriedWood += taken;
                mind?.AddMemory("I gathered wood.");
                break;
        }
    }

    void DepositAtCamp()
    {
        if (camp == null) return;

        if (carriedFood > 0f)
        {
            camp.Deposit(ResourceNode.ResourceType.Berry, carriedFood);
            mind?.AddMemory($"I stored {carriedFood:F0} food at camp.");
            carriedFood = 0f;
        }
        if (carriedMeat > 0f)
        {
            camp.Deposit(ResourceNode.ResourceType.Meat, carriedMeat);
            mind?.AddMemory($"I stored {carriedMeat:F0} meat at camp.");
            carriedMeat = 0f;
        }
        if (carriedStone > 0f)
        {
            camp.Deposit(ResourceNode.ResourceType.Stone, carriedStone);
            mind?.AddMemory($"I stored {carriedStone:F0} stone at camp.");
            carriedStone = 0f;
        }
        if (carriedWood > 0f)
        {
            camp.Deposit(ResourceNode.ResourceType.Wood, carriedWood);
            mind?.AddMemory($"I stored {carriedWood:F0} wood at camp.");
            carriedWood = 0f;
        }

        targetResource = null;

        // After depositing, check if we should build
        if (camp.CanUpgrade())
            SetGoal(CreatureGoal.BUILD, 0.8f);
        else if (mind != null && mind.Hunger < 0.3f)
            SetGoal(CreatureGoal.WANDER, 0.4f);
    }

    void UpdateBuild()
    {
        if (camp == null) { SetGoal(CreatureGoal.WANDER, 0.4f); return; }

        // Construction already under way — stand at the site and wait it out (this is the whole
        // point: a house takes real time to go up, not one instant action). React the moment it
        // actually finishes rather than the moment it merely began.
        if (awaitingConstruction)
        {
            moveDirection = Vector3.zero;
            if (!camp.IsUnderConstruction)
            {
                awaitingConstruction = false;
                OnConstructionComplete(awaitingFirePit);
            }
            return;
        }

        // What are we building? During securing: a shelter first, then the fire pit.
        bool needFire = camp.ShelterLevel >= 1 && !camp.HasFirePit;
        bool buildFire = needFire && camp.CanBuildFirePit();
        bool buildShelter = camp.CanUpgrade() && !(stage == CreatureLifeStage.Securing && needFire);

        if (!buildFire && !buildShelter)
        {
            // Missing materials — go gather what's short.
            float woodCost = needFire ? camp.FirePitWoodCost : GetNextWoodCost();
            float stoneCost = needFire ? camp.FirePitStoneCost : GetNextStoneCost();

            if (Mathf.Max(0, woodCost - camp.GetStock(ResourceNode.ResourceType.Wood)) > 0)
                SetGather(ResourceNode.ResourceType.Wood, 0.7f);
            else if (Mathf.Max(0, stoneCost - camp.GetStock(ResourceNode.ResourceType.Stone)) > 0)
                SetGather(ResourceNode.ResourceType.Stone, 0.7f);
            else
                SetGoal(CreatureGoal.WANDER, 0.4f);
            return;
        }

        // Go home to build.
        float distHome = Vector3.Distance(transform.position, homePoint);
        if (distHome > 3f)
        {
            moveDirection = (homePoint - transform.position).normalized;
            return;
        }

        moveDirection = Vector3.zero;

        if (buildFire && camp.TryBuildFirePit())
        {
            awaitingConstruction = true;
            awaitingFirePit = true;
            mind?.AddMemory("I've started building a fire pit...");
            mind?.Say("Let's get this fire going.");
            return;
        }

        if (buildShelter && camp.TryUpgrade())
        {
            awaitingConstruction = true;
            awaitingFirePit = false;
            mind?.AddMemory($"I've started building a {camp.GetShelterName()}...");
            mind?.Say($"Time to build my {camp.GetShelterName()}.");
        }
    }

    /// <summary>Fires once a staged construction (see ConstructionTimeline) actually finishes — the
    /// completion reactions that used to happen the instant a build action started.</summary>
    void OnConstructionComplete(bool wasFirePit)
    {
        if (wasFirePit)
        {
            mind?.AddMemory("I finished building a fire pit — warmth and safety at last.");
            mind?.FeelThreat(-0.2f);
            mind?.Say("A fire to keep me safe.");
            SetGoal(CreatureGoal.WANDER, 0.4f);
            return;
        }

        string shelterName = camp.GetShelterName();
        mind?.AddMemory($"I finished building a {shelterName}! I feel safer now.");
        mind?.FeelThreat(-0.3f);

        if (camp.ShelterLevel >= 3 && mind != null)
            StoryDirector.Instance?.LogEvent($"{mind.CreatureName} completed a proper house.");

        // Spread the word
        var others = CreatureMind.All;
        for (int i = 0; i < others.Count; i++)
        {
            var other = others[i];
            if (other == mind) continue;
            if (Vector3.Distance(transform.position, other.transform.position) < sightRange)
                other.AddMemory($"{mind.CreatureName} built a {shelterName} at their camp.");
        }

        SetGoal(CreatureGoal.WANDER, 0.4f);
    }

    float GetNextWoodCost() => camp != null ? camp.NextUpgradeWoodCost : 0f;
    float GetNextStoneCost() => camp != null ? camp.NextUpgradeStoneCost : 0f;

    void UpdateFlee()
    {
        fleeTimer -= Time.deltaTime;
        Vector3 up = world.GetSurfaceNormal(transform.position);
        Vector3 awayDir = (transform.position - fleeFrom).normalized;
        moveDirection = Vector3.ProjectOnPlane(awayDir, up).normalized;

        if (fleeTimer <= 0f)
            SetGoal(CreatureGoal.WANDER, 0.5f);
    }

    void UpdateSeekOthers()
    {
        if (NearestCreature == null || NearestCreatureDistance > sightRange)
        {
            UpdateWander();
            return;
        }

        if (NearestCreatureDistance > encounterRange)
            moveDirection = (NearestCreature.transform.position - transform.position).normalized;
        else
            moveDirection = Vector3.zero;
    }

    void UpdateHuddle()
    {
        if (NearestCreature != null && NearestCreatureDistance < sightRange)
        {
            if (NearestCreatureDistance > 3f)
                moveDirection = (NearestCreature.transform.position - transform.position).normalized;
            else
                moveDirection = Vector3.zero;
        }
        else if (hasHome)
        {
            float distHome = Vector3.Distance(transform.position, homePoint);
            if (distHome > homeRadius)
                moveDirection = (homePoint - transform.position).normalized;
            else
                moveDirection = Vector3.zero;
        }
        else
        {
            moveDirection = Vector3.zero;
        }
    }

    void UpdateRest()
    {
        if (hasHome)
        {
            float distHome = Vector3.Distance(transform.position, homePoint);
            if (distHome > homeRadius)
            {
                moveDirection = (homePoint - transform.position).normalized;
                goalIntensity = 0.3f;
            }
            else
            {
                moveDirection = Vector3.zero;
                if (mind != null)
                    mind.FeelThreat(-0.01f * Time.deltaTime * 60f);
            }
        }
        else
        {
            moveDirection = Vector3.zero;
        }
    }

    Critter targetPrey;

    void UpdateHunt()
    {
        // 1. Prefer hunting wildlife — the normal use of HUNT.
        if (targetPrey == null || !targetPrey.IsAlive)
            targetPrey = FindNearestCritter(sightRange);

        if (targetPrey != null && targetPrey.IsAlive)
        {
            float d = Vector3.Distance(transform.position, targetPrey.transform.position);
            if (d > 1.6f)
            {
                moveDirection = (targetPrey.transform.position - transform.position).normalized;
            }
            else
            {
                moveDirection = Vector3.zero;
                float caught = targetPrey.Catch();
                targetPrey = null;
                if (caught > 0f && mind != null)
                {
                    carriedMeat += 3f;            // a kill yields meat to haul home
                    mind.SatisfyHunger(0.2f);     // a quick bite on the spot
                    mind.AddMemory("I caught a wild animal — carrying the meat back to camp.");
                    mind.Say("Caught one!");
                    SetGoal(CreatureGoal.GATHER, 0.7f); // haul the meat home to deposit
                }
            }
            return;
        }

        // 2. No wildlife around — only the desperate AND aggressive turn on other creatures.
        bool desperate = mind != null && mind.Hunger > 0.8f;

        if (desperate && IsAggressive && NearestCreature != null && NearestCreatureDistance < sightRange)
        {
            moveDirection = (NearestCreature.transform.position - transform.position).normalized;

            if (NearestCreatureDistance < 2f)
            {
                mind.SatisfyHunger(0.3f);
                mind.AddMemory($"I attacked {NearestCreature.GetComponent<CreatureMind>()?.CreatureName ?? "another creature"} out of desperation.");
                mind.Say("I'm sorry, I had no choice...");

                var preyMind = NearestCreature.GetComponent<CreatureMind>();
                if (preyMind != null)
                {
                    preyMind.FeelThreat(0.4f);
                    preyMind.AddMemory($"{mind.CreatureName} attacked me!");
                    preyMind.AdjustRelationship(mind.CreatureName, -0.5f);
                    NearestCreature.FleeFrom(transform.position);
                }

                if (mind.Hunger < 0.4f)
                    SetGoal(CreatureGoal.REST, 0.4f);
            }
            return;
        }

        // Nothing to hunt — give up and wander.
        UpdateWander();
    }

    void UpdateDrink()
    {
        var terrain = world != null ? world.Terrain : null;
        if (terrain == null)
        {
            UpdateWander();
            return;
        }

        if (!hasDrinkTarget)
        {
            if (terrain.FindNearestWater(transform.position, 300f, out Vector3 wp))
            {
                drinkTarget = wp;
                hasDrinkTarget = true;
                drinkTimer = 25f; // give up if we can't reach it
            }
            else
            {
                UpdateWander();
                return;
            }
        }

        drinkTimer -= Time.deltaTime;
        if (drinkTimer <= 0f)
        {
            hasDrinkTarget = false; // unreachable — re-scan next time
            UpdateWander();
            return;
        }

        float dist = Vector3.Distance(transform.position, drinkTarget);
        if (dist > 2.5f)
        {
            moveDirection = (drinkTarget - transform.position).normalized;
            return;
        }

        moveDirection = Vector3.zero;
        if (harvestCooldown <= 0f)
        {
            harvestCooldown = 1f;
            mind?.Quench(0.6f);
            mind?.AddMemory("I drank from the water.");
            mind?.Say("Ahh, fresh water.");
            hasDrinkTarget = false;
            if (mind != null && mind.Thirst < 0.3f)
                SetGoal(CreatureGoal.WANDER, 0.4f);
        }
    }

    Critter FindNearestCritter(float range)
    {
        Critter best = null;
        float bestDist = range;
        var critters = Critter.All;
        for (int i = 0; i < critters.Count; i++)
        {
            var c = critters[i];
            if (c == null || !c.IsAlive) continue;
            float d = Vector3.Distance(transform.position, c.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = c;
            }
        }
        return best;
    }

    void UpdateShare()
    {
        // Need food in hand to give — pull from camp if home, otherwise go gather some.
        if (CarriedEdible < 1f)
        {
            bool atHome = hasHome && Vector3.Distance(transform.position, homePoint) < homeRadius;
            if (camp != null && atHome && camp.FoodStock() >= 2f)
            {
                carriedMeat += camp.Steal(ResourceNode.ResourceType.Meat, 2f);
                if (CarriedEdible < 2f)
                    carriedFood += camp.Steal(ResourceNode.ResourceType.Berry, 2f - CarriedEdible);
            }
            else
            {
                UpdateGather(ResourceNode.ResourceType.Berry);
                return;
            }
        }

        var target = FindNeediestNearby();
        if (target == null)
        {
            UpdateWander();
            return;
        }

        float dist = Vector3.Distance(transform.position, target.transform.position);
        if (dist > 2f)
        {
            moveDirection = (target.transform.position - transform.position).normalized;
            return;
        }

        moveDirection = Vector3.zero;
        if (harvestCooldown > 0f) return;
        harvestCooldown = 2f;

        float give = Mathf.Min(CarriedEdible, 2f);
        TakeEdible(give);

        var targetMind = target.GetComponent<CreatureMind>();
        if (targetMind != null && mind != null)
        {
            targetMind.SatisfyHunger(0.2f * give);
            targetMind.ReceiveGift(mind.CreatureName);
            mind.RegisterShare(targetMind.CreatureName);
            mind.FeelSocial(0.1f);
            mind.Say($"Here, {targetMind.CreatureName}, take some food.");
            targetMind.Say("Thank you!");
        }

        SetGoal(CreatureGoal.WANDER, 0.4f);
    }

    void UpdateTrade()
    {
        var partner = FindTradePartner();
        if (partner == null)
        {
            UpdateWander();
            return;
        }

        float dist = Vector3.Distance(transform.position, partner.transform.position);
        if (dist > 2f)
        {
            moveDirection = (partner.transform.position - transform.position).normalized;
            return;
        }

        moveDirection = Vector3.zero;
        if (harvestCooldown > 0f) return;

        const float amt = 2f;
        bool traded = false;

        if (carriedWood >= amt && partner.CarriedStone >= amt)
        {
            AdjustCarried(0f, -amt, amt);
            partner.AdjustCarried(0f, amt, -amt);
            traded = true;
        }
        else if (carriedStone >= amt && partner.CarriedWood >= amt)
        {
            AdjustCarried(0f, amt, -amt);
            partner.AdjustCarried(0f, -amt, amt);
            traded = true;
        }

        if (traded)
        {
            harvestCooldown = 2f;
            var partnerMind = partner.GetComponent<CreatureMind>();
            if (mind != null && partnerMind != null)
            {
                mind.RegisterTraded(partnerMind.CreatureName);
                partnerMind.RegisterTraded(mind.CreatureName);
                mind.Say("Let's trade.");
                partnerMind.Say("Deal!");
            }
        }

        SetGoal(CreatureGoal.WANDER, 0.4f);
    }

    static readonly List<CreatureBody> s_neighborBuf = new List<CreatureBody>();

    CreatureBody FindNeediestNearby()
    {
        CreatureGrid.QueryNeighbors(transform.position, sightRange, s_neighborBuf);
        CreatureBody best = null;
        float bestHunger = 0.5f; // only bother helping the actually-hungry
        for (int i = 0; i < s_neighborBuf.Count; i++)
        {
            var o = s_neighborBuf[i];
            if (o == this) continue;
            var om = o.GetComponent<CreatureMind>();
            if (om != null && om.Hunger > bestHunger)
            {
                bestHunger = om.Hunger;
                best = o;
            }
        }
        return best;
    }

    CreatureBody FindTradePartner()
    {
        CreatureGrid.QueryNeighbors(transform.position, sightRange, s_neighborBuf);
        for (int i = 0; i < s_neighborBuf.Count; i++)
        {
            var o = s_neighborBuf[i];
            if (o == this) continue;

            bool complementary = (carriedWood >= 2f && o.CarriedStone >= 2f)
                              || (carriedStone >= 2f && o.CarriedWood >= 2f);
            if (complementary) return o;
        }
        return null;
    }

    public void AdjustCarried(float food, float wood, float stone)
    {
        carriedFood = Mathf.Max(0f, carriedFood + food);
        carriedWood = Mathf.Max(0f, carriedWood + wood);
        carriedStone = Mathf.Max(0f, carriedStone + stone);
    }

    /// <summary>God Powers UI — gift supplies to a specific creature. Deposits into their camp
    /// stockpile if they have one, otherwise adds straight to what they're carrying.</summary>
    public void ReceiveGodGift(float berries, float wood, float stone, float meat)
    {
        if (camp != null)
        {
            if (berries > 0f) camp.Deposit(ResourceNode.ResourceType.Berry, berries);
            if (wood > 0f) camp.Deposit(ResourceNode.ResourceType.Wood, wood);
            if (stone > 0f) camp.Deposit(ResourceNode.ResourceType.Stone, stone);
            if (meat > 0f) camp.Deposit(ResourceNode.ResourceType.Meat, meat);
        }
        else
        {
            carriedFood += berries;
            carriedWood += wood;
            carriedStone += stone;
            carriedMeat += meat;
        }

        mind?.AddGodMemory("Supplies appeared before me — a gift from the god.");
        mind?.FeelAwe(0.15f);
    }

    /// <summary>God Powers UI — Fertility blessing: fast-forwards this creature's next-birth timer.</summary>
    public void ApplyFertilityBlessing(float reductionSeconds)
    {
        reproTimer = Mathf.Max(0f, reproTimer - reductionSeconds);
        mind?.AddGodMemory("A blessing of new life washed over me.");
        mind?.FeelAwe(0.2f);
    }

    // Just-arrived creatures stand and slowly look around, taking in the world.
    void UpdateObserve()
    {
        moveDirection = Vector3.zero;
        Vector3 up = world.GetSurfaceNormal(transform.position);
        currentForward = (Quaternion.AngleAxis(35f * Time.deltaTime, up) * currentForward).normalized;

        orientTimer -= Time.deltaTime;
        if (orientTimer <= 0f)
        {
            stage = CreatureLifeStage.Securing;
            mind?.AddMemory("I've taken in my surroundings. First I need shelter and a fire.");
            mind?.Say("Where am I... I should find shelter.");
        }
    }

    // Returns true while still working toward shelter + fire; false once secured.
    bool DriveSecuring()
    {
        if (camp == null) return false;
        if (camp.ShelterLevel >= 1 && camp.HasFirePit) return false; // secured

        // Drop a full load at camp first.
        if (TotalCarried > 3f) { SetGoal(CreatureGoal.GATHER, 0.7f); return true; }

        // Phase 1: build a shelter (at least a lean-to).
        if (camp.ShelterLevel < 1)
        {
            if (camp.CanUpgrade()) SetGoal(CreatureGoal.BUILD, 0.9f);
            else GatherForCost(GetNextWoodCost(), GetNextStoneCost());
            return true;
        }

        // Phase 2: build the fire pit.
        if (!camp.HasFirePit)
        {
            if (camp.CanBuildFirePit()) SetGoal(CreatureGoal.BUILD, 0.9f);
            else GatherForCost(camp.FirePitWoodCost, camp.FirePitStoneCost);
            return true;
        }

        return false;
    }

    void GatherForCost(float woodCost, float stoneCost)
    {
        if (camp.GetStock(ResourceNode.ResourceType.Wood) < woodCost)
            SetGather(ResourceNode.ResourceType.Wood, 0.8f);
        else if (camp.GetStock(ResourceNode.ResourceType.Stone) < stoneCost)
            SetGather(ResourceNode.ResourceType.Stone, 0.7f);
        else
            SetGoal(CreatureGoal.BUILD, 0.9f);
    }

    void PickNewWanderTarget()
    {
        if (mind != null && !mind.IsInDaylight && hasHome)
        {
            // This offset had NO land check at all — if the camp sits anywhere near a coast or lake
            // (common; creatures deliberately settle near water), a random point within homeRadius could
            // land squarely in it, and SnapToSurface would place the creature there regardless. Retry a
            // few times against IsLand AND IsWaterNear (IsLand alone doesn't know rivers/lakes exist —
            // they're carved in after the raw height check IsLand uses, the same gap already closed in
            // GetRandomLandPoint and the birth-placement loop; this spot was missed in that pass).
            // homePoint itself (always valid — the creature already lives there) is the guaranteed-safe
            // fallback if every attempt fails.
            Vector3 candidate = homePoint;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                Vector3 test = homePoint + Random.onUnitSphere * homeRadius;
                bool nearRiverOrLake = world.Terrain != null && world.Terrain.IsWaterNear(test, 2f);
                // Same zero-margin gap as GetRandomLandPoint/birth-placement: IsLand alone accepts a
                // point a hair above sea level, which reads as standing in the water at the shoreline.
                Vector3 testDir = (test - world.Center).normalized;
                bool clearOfShore = world.Terrain == null || world.Terrain.GetLandHeight(testDir) >= 0.1f;
                if (clearOfShore && !nearRiverOrLake) { candidate = test; break; }
            }
            wanderTarget = candidate;
        }
        else
        {
            wanderTarget = world.GetRandomLandPoint();
        }

        wanderTarget = world.SnapToSurface(wanderTarget);
        wanderTimer = Random.Range(4f, 10f);
    }

    public void SetGoal(CreatureGoal goal, float intensity)
    {
        currentGoal = goal;
        goalIntensity = Mathf.Clamp01(intensity);
    }

    float predatorScanTimer;
    const float PredatorScaredRange = 14f;

    void CheckPredatorThreat()
    {
        predatorScanTimer -= Time.deltaTime;
        if (predatorScanTimer > 0f) return;
        predatorScanTimer = 0.3f;

        var preds = Predator.All;
        if (preds.Count == 0) return;

        Transform nearest = null;
        float nd = PredatorScaredRange;
        for (int i = 0; i < preds.Count; i++)
        {
            var p = preds[i];
            if (p == null) continue;
            float d = Vector3.Distance(transform.position, p.transform.position);
            if (d < nd) { nd = d; nearest = p.transform; }
        }

        if (nearest != null)
        {
            // Always run directly away from the predator's CURRENT position — refreshing this each
            // scan stops the creature from fleeing a stale spot while the predator tracks it live.
            bool alreadyFleeing = currentGoal == CreatureGoal.FLEE;
            FleeFrom(nearest.position);
            if (!alreadyFleeing && mind != null)
            {
                mind.FeelThreat(0.3f);
                mind.AddMemory("A predator was prowling nearby — I ran for my life.");
                if (Random.value < 0.4f) mind.Say("A predator! Run!");
            }
        }
    }

    float lastBeastSayTime = -10f;

    /// <summary>A predator landed a hit: take damage, panic, and flee. Repeated maulings can kill.</summary>
    public void TakePredatorAttack(float dmg, Vector3 fromPos)
    {
        if (dead) return;
        health = Mathf.Clamp01(health - dmg);
        if (mind != null)
        {
            mind.FeelThreat(0.5f);
            mind.AddMemory("A predator attacked me!");
            if (Time.time - lastBeastSayTime > 3f) { mind.Say("Aaah! A beast!"); lastBeastSayTime = Time.time; }
        }
        FleeFrom(fromPos);
        if (health <= 0f) Die();
    }

    /// <summary>God Powers UI — Meteor: real health damage (unlike Smite, which is fear-only).
    /// Memory/flavor text is added by the caller (GodEventBus), not here.</summary>
    public void TakeGodDamage(float dmg)
    {
        if (dead) return;
        health = Mathf.Clamp01(health - dmg);
        if (health <= 0f) Die();
    }

    public void FleeFrom(Vector3 position)
    {
        currentGoal = CreatureGoal.FLEE;
        goalIntensity = 1f;
        fleeFrom = position;
        fleeTimer = Random.Range(3f, 6f);
        moveDirection = (transform.position - position).normalized;
    }

    public void SetHome(Vector3 position)
    {
        homePoint = world.SnapToSurface(position);
        hasHome = true;
    }

    public CreatureSave ToSave()
    {
        var cs = new CreatureSave();
        if (mind != null) mind.ExportInto(cs);

        cs.posX = transform.position.x;
        cs.posY = transform.position.y;
        cs.posZ = transform.position.z;

        cs.health = health;
        cs.exposure = exposure;
        cs.carriedFood = carriedFood;
        cs.carriedWood = carriedWood;
        cs.carriedStone = carriedStone;
        cs.carriedMeat = carriedMeat;

        if (camp != null)
        {
            cs.campX = camp.transform.position.x;
            cs.campY = camp.transform.position.y;
            cs.campZ = camp.transform.position.z;
            cs.shelterLevel = camp.ShelterLevel;
            cs.campFood = camp.GetStock(ResourceNode.ResourceType.Berry);
            cs.campWood = camp.GetStock(ResourceNode.ResourceType.Wood);
            cs.campStone = camp.GetStock(ResourceNode.ResourceType.Stone);
            cs.campMeat = camp.GetStock(ResourceNode.ResourceType.Meat);
            cs.campHasFirePit = camp.HasFirePit;
        }

        cs.lifeStage = (int)stage;

        return cs;
    }
}

public enum CreatureGoal
{
    WANDER,
    FORAGE,
    FLEE,
    SEEK_OTHERS,
    HUDDLE,
    WORSHIP,
    REST,
    HUNT,
    GATHER,
    BUILD,
    SHARE,
    TRADE,
    DRINK
}

public enum CreatureLifeStage
{
    Orienting,
    Securing,
    Sustaining
}
