using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// God Powers: a click-to-arm toolbar (see CreatureInfoUI's DrawControls) backed by a Favor economy.
/// Select a power (button or number key 1-7), then a single left-click in the world casts it — no
/// more holding a key while clicking. Clicks over any UI panel are ignored (see UIPointerGuard), and
/// every cast attempt reports back through LastFeedbackMessage so a miss or an empty purse is never
/// silent. Runs before other Escape-consuming scripts (DefaultExecutionOrder) so cancelling an armed
/// power takes priority over the pause menu / creature-deselect on the same keypress.
/// </summary>
[DefaultExecutionOrder(-100)]
public class GodEventBus : MonoBehaviour
{
    public static GodEventBus Instance { get; private set; }

    public event Action<GodEvent> OnGodEvent;

    [Header("Favor economy")]
    [SerializeField] float maxFavor = 100f;
    [SerializeField] float favorRegenPerSecond = 1.5f;
    [Tooltip("Extra favor regen per second, scaled by the total Awe of every creature in the world — worship feeds the god's power.")]
    [SerializeField] float aweRegenMultiplier = 0.04f;

    [Header("Smite")]
    [SerializeField] float eventRadius = 30f;
    [SerializeField] float smiteDamage = 0.5f; // fear/threat only — Smite doesn't directly kill

    [Header("Bless")]
    [SerializeField] float blessingAmount = 0.3f;

    [Header("Feed")]
    [SerializeField] float feedAmount = 0.4f;

    [Header("Meteor — Smite's lethal escalation")]
    [SerializeField] float meteorRadius = 20f;
    [SerializeField] float meteorHealthDamage = 0.4f; // real health damage, unlike Smite

    [Header("Ward — temporary safe zone")]
    [SerializeField] float wardRadius = 22f;
    [SerializeField] float wardDuration = 60f;

    [Header("Fertility")]
    [SerializeField] float fertilityRadius = 25f;
    [SerializeField] float fertilityReductionSeconds = 600f; // ~10 real minutes off the next-birth timer

    [Header("Shrine — permanent world-shaping")]
    [SerializeField] int maxShrines = 6;

    float favor;
    GodPowerType armedPower = GodPowerType.None;
    float aweSumCached;
    float aweSumTimer;

    List<WardZone> wardZones = new List<WardZone>();

    List<GodVfx> activeVfx = new List<GodVfx>();

    static int escapeConsumedFrame = -1;
    public static bool WasEscapeConsumedThisFrame => escapeConsumedFrame == Time.frameCount;

    public GodPowerType ArmedPower => armedPower;
    public float Favor => favor;
    public float MaxFavor => maxFavor;
    public string LastFeedbackMessage { get; private set; } = "";
    public float LastFeedbackTime { get; private set; } = -10f;

    /// <summary>All powers in toolbar/hotkey order — used by both this class and the UI.</summary>
    public static readonly GodPowerType[] AllPowers =
    {
        GodPowerType.Smite, GodPowerType.Bless, GodPowerType.Feed,
        GodPowerType.Meteor, GodPowerType.Ward, GodPowerType.Fertility, GodPowerType.Shrine
    };

    public float GetCost(GodPowerType type) => type switch
    {
        GodPowerType.Smite => 12f,
        GodPowerType.Bless => 10f,
        GodPowerType.Feed => 8f,
        GodPowerType.Meteor => 30f,
        GodPowerType.Ward => 20f,
        GodPowerType.Fertility => 25f,
        GodPowerType.Shrine => 35f,
        _ => 0f
    };

    void Awake()
    {
        Instance = this;
        favor = maxFavor; // start full so the player can experiment immediately
    }

    /// <summary>Select (or, if already selected, deselect) a power. Toolbar buttons and quick-select keys both call this.</summary>
    public void ArmPower(GodPowerType type)
    {
        armedPower = (armedPower == type) ? GodPowerType.None : type;
    }

    /// <summary>For direct creature-target UI actions (rename/gift/prophecy) that act on an already-selected
    /// creature rather than a world raycast. Returns false (and spends nothing) if favor is insufficient.</summary>
    public bool TrySpendFavor(float amount)
    {
        if (favor < amount) return false;
        favor -= amount;
        return true;
    }

    void Update()
    {
        var mouse = Mouse.current;
        var kb = Keyboard.current;
        if (mouse == null || kb == null) return;
        if (MainMenu.Instance != null && MainMenu.Instance.IsOpen) return;

        // Escape cancels an armed power first (this script runs before MainMenu/CreatureInfoUI thanks
        // to DefaultExecutionOrder, so they can check WasEscapeConsumedThisFrame and skip their own
        // Escape handling this same frame instead of also opening the pause menu / deselecting).
        if (kb.escapeKey.wasPressedThisFrame && armedPower != GodPowerType.None)
        {
            armedPower = GodPowerType.None;
            escapeConsumedFrame = Time.frameCount;
        }
        else if (mouse.leftButton.wasPressedThisFrame && armedPower != GodPowerType.None
                 && !UIPointerGuard.IsPointerOverUI(mouse.position.ReadValue()))
        {
            TryCast(armedPower, mouse.position.ReadValue());
        }

        CheckQuickSelectHotkeys(kb);
        RegenFavor();
        PruneWardZones();
        UpdateVfx();
    }

    void CheckQuickSelectHotkeys(Keyboard kb)
    {
        // Tap (not hold) 1-7 to arm a power — no more chorded hold-key-and-click.
        if (kb.digit1Key.wasPressedThisFrame) ArmPower(GodPowerType.Smite);
        else if (kb.digit2Key.wasPressedThisFrame) ArmPower(GodPowerType.Bless);
        else if (kb.digit3Key.wasPressedThisFrame) ArmPower(GodPowerType.Feed);
        else if (kb.digit4Key.wasPressedThisFrame) ArmPower(GodPowerType.Meteor);
        else if (kb.digit5Key.wasPressedThisFrame) ArmPower(GodPowerType.Ward);
        else if (kb.digit6Key.wasPressedThisFrame) ArmPower(GodPowerType.Fertility);
        else if (kb.digit7Key.wasPressedThisFrame) ArmPower(GodPowerType.Shrine);
    }

    void RegenFavor()
    {
        // Total world Awe is cheap enough to resum every frame at realistic population sizes, but at
        // the very top end (hundreds of creatures) a small tick keeps this from being wasted work.
        aweSumTimer -= Time.deltaTime;
        if (aweSumTimer <= 0f)
        {
            aweSumTimer = 1f;
            float sum = 0f;
            var all = CreatureMind.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null) sum += all[i].Awe;
            aweSumCached = sum;
        }

        favor = Mathf.Min(maxFavor, favor + (favorRegenPerSecond + aweSumCached * aweRegenMultiplier) * Time.deltaTime);
    }

    bool CanCast(GodPowerType type, out string reason)
    {
        float cost = GetCost(type);
        if (favor < cost) { reason = $"Not enough favor ({cost:F0} needed)"; return false; }
        if (type == GodPowerType.Shrine && Shrine.All.Count >= maxShrines) { reason = "Shrine limit reached"; return false; }
        reason = "";
        return true;
    }

    void TryCast(GodPowerType type, Vector2 screenPos)
    {
        if (!CanCast(type, out string reason))
        {
            LastFeedbackMessage = reason;
            LastFeedbackTime = Time.time;
            return;
        }

        var cam = Camera.main;
        if (cam == null) return; // matches the defensive null-check every other Camera.main use in this project already has

        Ray ray = cam.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit hit))
        {
            // Casts used to fail completely silently here — this is the fix for "the god powers don't work".
            LastFeedbackMessage = "No valid target there — aim at the world";
            LastFeedbackTime = Time.time;
            return;
        }

        favor -= GetCost(type);
        PerformGodAction(type, hit.point);
    }

    void PerformGodAction(GodPowerType actionType, Vector3 position)
    {
        if (actionType == GodPowerType.Shrine)
        {
            CastShrine(position);
            return;
        }

        float radius = actionType switch
        {
            GodPowerType.Meteor => meteorRadius,
            GodPowerType.Ward => wardRadius,
            GodPowerType.Fertility => fertilityRadius,
            _ => eventRadius
        };

        var creatures = CreatureMind.All;
        int affected = 0;

        for (int i = 0; i < creatures.Count; i++)
        {
            var creature = creatures[i];
            if (creature == null) continue;
            float dist = Vector3.Distance(creature.transform.position, position);
            if (dist > radius) continue;

            affected++;
            float intensity = 1f - (dist / radius);
            string eventDescription = "";
            var body = creature.GetComponent<CreatureBody>();

            switch (actionType)
            {
                case GodPowerType.Smite:
                    creature.FeelThreat(smiteDamage * intensity);
                    creature.FeelAwe(0.3f * intensity);
                    body?.FleeFrom(position);
                    eventDescription = $"The god struck near me with terrible force! (intensity: {intensity:F1})";
                    break;
                case GodPowerType.Meteor:
                    creature.FeelThreat(smiteDamage * intensity);
                    creature.FeelAwe(0.5f * intensity);
                    body?.FleeFrom(position);
                    body?.TakeGodDamage(meteorHealthDamage * intensity);
                    eventDescription = $"A meteor crashed from the heavens! (intensity: {intensity:F1})";
                    break;
                case GodPowerType.Bless:
                    creature.FeelAwe(blessingAmount * intensity);
                    creature.FeelThreat(-0.2f * intensity);
                    eventDescription = $"A warm light from above washed over me. (intensity: {intensity:F1})";
                    break;
                case GodPowerType.Feed:
                    creature.SatisfyHunger(feedAmount * intensity);
                    creature.FeelAwe(0.1f * intensity);
                    eventDescription = $"Food appeared from nowhere near me! (intensity: {intensity:F1})";
                    break;
                case GodPowerType.Fertility:
                    body?.ApplyFertilityBlessing(fertilityReductionSeconds * intensity);
                    creature.FeelAwe(0.2f * intensity);
                    eventDescription = $"A blessing of new life washed over me. (intensity: {intensity:F1})";
                    break;
                case GodPowerType.Ward:
                    creature.FeelThreat(-0.3f * intensity);
                    eventDescription = $"A protective ward settled over this place. (intensity: {intensity:F1})";
                    break;
            }

            creature.AddGodMemory(eventDescription);
        }

        if (actionType == GodPowerType.Ward)
            wardZones.Add(new WardZone { position = position, radius = wardRadius, expireTime = Time.time + wardDuration });

        Debug.Log($"[God] {actionType} at {position}, affected {affected} creatures");
        SpawnVfx(actionType, position);

        LastFeedbackMessage = affected > 0 || actionType == GodPowerType.Ward
            ? $"{actionType} — {affected} creature(s) affected"
            : $"{actionType} — no creatures in range";
        LastFeedbackTime = Time.time;

        var godEvent = new GodEvent { type = actionType, position = position, radius = radius };
        OnGodEvent?.Invoke(godEvent);
    }

    void CastShrine(Vector3 position)
    {
        var shrineObj = new GameObject("Shrine");
        shrineObj.transform.position = position;
        Vector3 up = position.normalized; // planet is centered at world origin
        shrineObj.transform.up = up;

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        // Stacked stone tiers, tapering upward.
        for (int i = 0; i < 3; i++)
        {
            var tier = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(tier.GetComponent<Collider>());
            tier.transform.SetParent(shrineObj.transform);
            tier.transform.localPosition = new Vector3(0f, 0.3f + i * 0.35f, 0f);
            float w = 1.6f - i * 0.35f;
            tier.transform.localScale = new Vector3(w, 0.18f, w);
            if (shader != null)
                tier.GetComponent<MeshRenderer>().material = new Material(shader) { color = new Color(0.55f, 0.53f, 0.5f) };
        }

        // Glowing orb on top — reads as sacred from a distance.
        var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(orb.GetComponent<Collider>());
        orb.transform.SetParent(shrineObj.transform);
        orb.transform.localPosition = new Vector3(0f, 1.5f, 0f);
        orb.transform.localScale = Vector3.one * 0.5f;
        if (shader != null)
        {
            var orbMat = new Material(shader) { color = new Color(0.6f, 0.85f, 1f) };
            orbMat.EnableKeyword("_EMISSION");
            orbMat.SetColor("_EmissionColor", new Color(0.5f, 0.8f, 1f) * 2.5f);
            orb.GetComponent<MeshRenderer>().material = orbMat;
        }

        shrineObj.AddComponent<Shrine>();
        shrineObj.AddComponent<GrowIn>();

        Debug.Log($"[God] Shrine consecrated at {position}");
        StoryDirector.Instance?.LogEvent("A shrine was consecrated upon the land.");
        LastFeedbackMessage = "A shrine now stands upon the land.";
        LastFeedbackTime = Time.time;
    }

    void PruneWardZones()
    {
        for (int i = wardZones.Count - 1; i >= 0; i--)
            if (Time.time >= wardZones[i].expireTime)
                wardZones.RemoveAt(i);
    }

    /// <summary>Predators (and anything else that wants to respect sanctuary) check this before targeting a creature.</summary>
    public static bool IsWarded(Vector3 pos)
    {
        if (Instance == null) return false;
        var zones = Instance.wardZones;
        for (int i = 0; i < zones.Count; i++)
        {
            if (Time.time >= zones[i].expireTime) continue;
            if (Vector3.Distance(zones[i].position, pos) <= zones[i].radius) return true;
        }
        return false;
    }

    void SpawnVfx(GodPowerType type, Vector3 position)
    {
        Color color = type switch
        {
            GodPowerType.Smite => new Color(1f, 0.3f, 0.1f),
            GodPowerType.Meteor => new Color(1f, 0.45f, 0.05f),
            GodPowerType.Bless => new Color(1f, 0.95f, 0.4f),
            GodPowerType.Feed => new Color(0.3f, 0.9f, 0.3f),
            GodPowerType.Fertility => new Color(1f, 0.6f, 0.85f),
            GodPowerType.Ward => new Color(0.4f, 0.85f, 1f),
            _ => Color.white
        };

        var obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        obj.name = $"GodVfx_{type}";
        Destroy(obj.GetComponent<Collider>());
        obj.transform.position = position;
        float baseScale = type == GodPowerType.Meteor ? 3f : 2f;
        obj.transform.localScale = Vector3.one * baseScale;

        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) { Destroy(obj); return; }
        var mat = new Material(shader);
        mat.color = color;
        mat.SetFloat("_Surface", 1f);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = 3000;
        obj.GetComponent<MeshRenderer>().material = mat;

        activeVfx.Add(new GodVfx
        {
            obj = obj, mat = mat, color = color, duration = type == GodPowerType.Meteor ? 2.2f : 1.5f,
            kind = VfxKind.Impact, baseScale = baseScale
        });

        if (type == GodPowerType.Smite || type == GodPowerType.Meteor)
        {
            // A beam of light — keeps its thin-tall cylinder shape and just fades, doesn't rescale.
            var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pillar.name = "SmitePillar";
            Destroy(pillar.GetComponent<Collider>());
            float pillarScale = type == GodPowerType.Meteor ? 1.6f : 1f;
            pillar.transform.position = position + position.normalized * 15f;
            pillar.transform.localScale = new Vector3(pillarScale, 15f, pillarScale);
            pillar.transform.up = position.normalized;

            var pMat = new Material(mat) { color = new Color(color.r, color.g * 0.8f, color.b * 0.6f, 0.6f) };
            pillar.GetComponent<MeshRenderer>().material = pMat;

            activeVfx.Add(new GodVfx { obj = pillar, mat = pMat, color = pMat.color, duration = 0.8f, kind = VfxKind.Beam });
        }
        else if (type == GodPowerType.Ward)
        {
            // A lingering translucent dome for the ward's actual duration, not just a quick flash.
            var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dome.name = "WardDome";
            Destroy(dome.GetComponent<Collider>());
            dome.transform.position = position;
            dome.transform.localScale = Vector3.one * wardRadius * 2f;

            var domeMat = new Material(mat) { color = new Color(color.r, color.g, color.b, 0.12f) };
            dome.GetComponent<MeshRenderer>().material = domeMat;

            activeVfx.Add(new GodVfx { obj = dome, mat = domeMat, color = domeMat.color, duration = wardDuration, kind = VfxKind.Dome });
        }
    }

    void UpdateVfx()
    {
        for (int i = activeVfx.Count - 1; i >= 0; i--)
        {
            var vfx = activeVfx[i];
            vfx.timer += Time.deltaTime;
            float t = vfx.timer / vfx.duration;

            if (t >= 1f)
            {
                Destroy(vfx.obj);
                activeVfx.RemoveAt(i);
                continue;
            }

            switch (vfx.kind)
            {
                case VfxKind.Impact:
                    // Grows outward from its base scale as a shockwave, fading alpha as it expands.
                    vfx.obj.transform.localScale = Vector3.one * (vfx.baseScale + t * 8f);
                    vfx.mat.color = new Color(vfx.color.r, vfx.color.g, vfx.color.b, 1f - t);
                    break;
                case VfxKind.Beam:
                    // Keeps its already-set shape (a thin tall cylinder) — just fades, no rescaling.
                    vfx.mat.color = new Color(vfx.color.r, vfx.color.g, vfx.color.b, vfx.color.a * (1f - t));
                    break;
                case VfxKind.Dome:
                    // Holds steady at full opacity, then fades only in its last second.
                    float fadeT = Mathf.Clamp01((vfx.duration - vfx.timer) / 1f);
                    vfx.mat.color = new Color(vfx.color.r, vfx.color.g, vfx.color.b, vfx.color.a * fadeT);
                    break;
            }
        }
    }

    struct WardZone { public Vector3 position; public float radius; public float expireTime; }

    enum VfxKind { Impact, Beam, Dome }

    class GodVfx
    {
        public GameObject obj;
        public Material mat;
        public Color color;
        public float timer;
        public float duration;
        public VfxKind kind;
        public float baseScale;
    }
}

public enum GodPowerType { None, Smite, Bless, Feed, Meteor, Ward, Fertility, Shrine }

public struct GodEvent
{
    public GodPowerType type;
    public Vector3 position;
    public float radius;
}
