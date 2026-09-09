using UnityEngine;
using UnityEngine.InputSystem;

public class CreatureInfoUI : MonoBehaviour
{
    CreatureMind selectedCreature;
    Vector2 rosterScroll;
    Vector2 detailScroll;
    bool rosterMinimized;
    string renameInput = "";
    string prophecyInput = "";

    // Set by DrawControls each frame so DrawRoster can start exactly where the (variable-height) God
    // Powers panel ends, instead of a hardcoded offset that silently goes stale whenever that panel's
    // content changes (which is exactly what caused the two panels to overlap).
    float controlsBottom = 148f;
    float eraIndicatorBottom = 80f;

    bool showHistory;
    Vector2 historyScroll;

    GUIStyle boxStyle;
    GUIStyle headerStyle;
    GUIStyle labelStyle;
    GUIStyle smallStyle;
    GUIStyle italicStyle;
    GUIStyle barValueStyle;
    GUIStyle miniBarLabelStyle;
    GUIStyle entryNormalStyle;
    GUIStyle entrySelectedStyle;
    GUIStyle sectionStyle;
    GUIStyle armedButtonStyle;
    GUIStyle chapterTitleStyle;
    GUIStyle chapterSubStyle;
    GUIStyle chapterBoxStyle;
    bool stylesInit;

    void Update()
    {
        var mouse = Mouse.current;
        var kb = Keyboard.current;
        if (mouse == null || kb == null) return;
        if (MainMenu.Instance != null && MainMenu.Instance.IsOpen) return;

        bool powerArmed = GodEventBus.Instance != null && GodEventBus.Instance.ArmedPower != GodPowerType.None;

        // Skip creature-select while a god power is armed (that click is for casting instead), and
        // skip it entirely when the click is over a UI panel (previously this could raycast into the
        // world from behind the panel and do something unexpected).
        if (mouse.leftButton.wasPressedThisFrame && !powerArmed
            && !UIPointerGuard.IsPointerOverUI(mouse.position.ReadValue()) && Camera.main != null)
        {
            Ray ray = Camera.main.ScreenPointToRay(mouse.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                var mind = hit.collider.GetComponentInParent<CreatureMind>();
                if (mind != null)
                {
                    SelectCreature(mind);
                    return;
                }
            }
        }

        // GodEventBus runs first (DefaultExecutionOrder) and claims Escape when a power is armed —
        // don't also deselect the creature on that same keypress.
        if (kb.escapeKey.wasPressedThisFrame && !GodEventBus.WasEscapeConsumedThisFrame)
            DeselectCreature();

        // Sync with camera — if camera unfocused via scroll-out, deselect
        if (selectedCreature != null && OrbitalCamera.Instance != null && !OrbitalCamera.Instance.IsFocused)
            selectedCreature = null;
    }

    void SelectCreature(CreatureMind creature)
    {
        selectedCreature = creature;
        renameInput = "";
        prophecyInput = "";
        if (OrbitalCamera.Instance != null)
            OrbitalCamera.Instance.FocusOn(creature.transform);
    }

    void DeselectCreature()
    {
        selectedCreature = null;
        if (OrbitalCamera.Instance != null)
            OrbitalCamera.Instance.Unfocus();
    }

    void InitStyles()
    {
        if (stylesInit) return;
        stylesInit = true;

        // Clean dark-slate panel with breathing room.
        boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.normal.background = MakeTex(new Color(0.07f, 0.08f, 0.11f, 0.94f));
        boxStyle.padding = new RectOffset(12, 12, 11, 11);

        headerStyle = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15, fontStyle = FontStyle.Bold };
        headerStyle.normal.textColor = new Color(0.96f, 0.97f, 1f);

        labelStyle = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true, fontSize = 12 };
        labelStyle.normal.textColor = new Color(0.86f, 0.88f, 0.92f);

        smallStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 11 };
        smallStyle.normal.textColor = new Color(0.62f, 0.66f, 0.72f);

        italicStyle = new GUIStyle(labelStyle) { fontStyle = FontStyle.Italic };
        italicStyle.normal.textColor = new Color(0.74f, 0.78f, 0.85f);

        barValueStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.MiddleRight };
        barValueStyle.normal.textColor = new Color(0.9f, 0.92f, 0.95f);

        miniBarLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 9, alignment = TextAnchor.MiddleCenter };
        miniBarLabelStyle.normal.textColor = Color.white;

        entryNormalStyle = new GUIStyle(GUI.skin.box);
        entryNormalStyle.normal.background = MakeTex(new Color(1f, 1f, 1f, 0.04f));
        entryNormalStyle.padding = new RectOffset(8, 8, 6, 6);
        entryNormalStyle.margin = new RectOffset(0, 0, 2, 2);

        entrySelectedStyle = new GUIStyle(GUI.skin.box);
        entrySelectedStyle.normal.background = MakeTex(new Color(0.27f, 0.5f, 0.85f, 0.5f));
        entrySelectedStyle.padding = new RectOffset(8, 8, 6, 6);
        entrySelectedStyle.margin = new RectOffset(0, 0, 2, 2);

        sectionStyle = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 11, fontStyle = FontStyle.Bold };
        sectionStyle.normal.textColor = new Color(0.45f, 0.72f, 0.95f);

        armedButtonStyle = new GUIStyle(GUI.skin.button);
        armedButtonStyle.normal.background = MakeTex(new Color(0.32f, 0.55f, 0.92f, 0.95f));
        armedButtonStyle.normal.textColor = Color.white;
        armedButtonStyle.hover.background = armedButtonStyle.normal.background;
        armedButtonStyle.hover.textColor = Color.white;
        armedButtonStyle.fontStyle = FontStyle.Bold;

        chapterTitleStyle = new GUIStyle(GUI.skin.label)
        {
            richText = true, fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter
        };
        chapterTitleStyle.normal.textColor = Color.white;

        chapterSubStyle = new GUIStyle(GUI.skin.label)
        {
            richText = true, wordWrap = true, fontSize = 14, alignment = TextAnchor.MiddleCenter
        };
        chapterSubStyle.normal.textColor = new Color(0.88f, 0.9f, 0.95f);

        chapterBoxStyle = new GUIStyle(GUI.skin.box);
        chapterBoxStyle.normal.background = MakeTex(new Color(0.05f, 0.05f, 0.08f, 0.75f));
    }

    Texture2D MakeTex(Color col)
    {
        var tex = new Texture2D(2, 2);
        var pixels = new Color[] { col, col, col, col };
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    void OnGUI()
    {
        if (MainMenu.Instance != null && MainMenu.Instance.IsOpen) return;

        InitStyles();
        DrawEraIndicator();
        DrawHistoryPanel();
        DrawEraBanner();
        DrawControls();
        DrawRoster();
        DrawDetailPanel();
    }

    /// <summary>Small persistent "where are we in the story" readout, top-left, always visible.</summary>
    void DrawEraIndicator()
    {
        var story = StoryDirector.Instance;
        if (story == null) return;

        var rect = new Rect(10, 10, 230, 60);
        UIPointerGuard.Register(rect);
        eraIndicatorBottom = rect.y + rect.height + 10f;

        GUILayout.BeginArea(rect, boxStyle);
        int totalMin = Mathf.FloorToInt(story.ElapsedSeconds / 60f);
        int totalSec = Mathf.FloorToInt(story.ElapsedSeconds % 60f);
        GUILayout.Label($"<b>{story.EraDisplayName}</b>", headerStyle);
        GUILayout.Label($"{totalMin}:{totalSec:D2} elapsed" + (story.IsCrisisActive ? "  <color=#ff6666><b>CRISIS</b></color>" : ""), smallStyle);
        if (GUILayout.Button(showHistory ? "Hide History" : "History", GUILayout.Width(90)))
            showHistory = !showHistory;
        GUILayout.EndArea();
    }

    /// <summary>Big centered chapter-title card that fades in/holds/fades out whenever the story
    /// advances to a new era — the main "something to follow along with" cue.</summary>
    void DrawEraBanner()
    {
        var story = StoryDirector.Instance;
        if (story == null || story.EraAnnouncementTime < 0f) return;

        float age = Time.time - story.EraAnnouncementTime;
        const float holdTime = 4.5f;
        const float fadeTime = 1f;
        float totalDuration = fadeTime + holdTime + fadeTime;
        if (age < 0f || age > totalDuration) return;

        float alpha = age < fadeTime ? age / fadeTime
                    : age > fadeTime + holdTime ? 1f - (age - fadeTime - holdTime) / fadeTime
                    : 1f;

        float w = Mathf.Min(700f, Screen.width * 0.7f);
        var rect = new Rect((Screen.width - w) / 2f, Screen.height * 0.14f, w, 110f);
        UIPointerGuard.Register(rect); // every other panel registers — this one was missed, letting clicks on the banner leak through to cast a power or select whatever's behind it

        var prevColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha);

        GUI.Box(rect, "", chapterBoxStyle);

        GUILayout.BeginArea(rect);
        GUILayout.FlexibleSpace();
        GUILayout.Label(story.EraDisplayName.ToUpperInvariant(), chapterTitleStyle);
        GUILayout.Label(story.EraAnnouncement, chapterSubStyle);
        GUILayout.FlexibleSpace();
        GUILayout.EndArea();

        GUI.color = prevColor;
    }

    void DrawHistoryPanel()
    {
        if (!showHistory) return;
        var story = StoryDirector.Instance;
        if (story == null) return;

        float w = 320f;
        float h = 260f;
        var rect = new Rect(10, eraIndicatorBottom, w, h);
        UIPointerGuard.Register(rect);

        GUILayout.BeginArea(rect, boxStyle);
        GUILayout.Label("<b>History</b>", headerStyle);
        historyScroll = GUILayout.BeginScrollView(historyScroll);

        var events = story.NotableEvents;
        if (events.Count == 0)
        {
            GUILayout.Label("Nothing notable has happened yet.", smallStyle);
        }
        else
        {
            for (int i = events.Count - 1; i >= 0; i--)
                GUILayout.Label(events[i], smallStyle);
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    void DrawControls()
    {
        var god = GodEventBus.Instance;
        float w = 240f;
        float h = god != null ? 300f : 130f;
        var rect = new Rect(Screen.width - w - 10, 10, w, h);
        UIPointerGuard.Register(rect);

        controlsBottom = rect.y + rect.height + 10f;

        GUILayout.BeginArea(rect, boxStyle);
        GUILayout.Label("<b>God Powers</b>", headerStyle);

        if (god != null)
        {
            DrawFavorBar(god.Favor, god.MaxFavor);
            GUILayout.Space(4);

            foreach (var power in GodEventBus.AllPowers)
            {
                bool isArmed = god.ArmedPower == power;
                float cost = god.GetCost(power);
                bool affordable = god.Favor >= cost;

                var style = isArmed ? armedButtonStyle : GUI.skin.button;
                GUI.enabled = affordable || isArmed; // always allow un-arming even if you can't afford it anymore
                if (GUILayout.Button($"{(int)power}  {power}   ({cost:F0})", style))
                    god.ArmPower(power);
                GUI.enabled = true;
            }

            GUILayout.Space(4);
            if (god.ArmedPower != GodPowerType.None)
                GUILayout.Label($"<color=#ffdd66>{god.ArmedPower} armed — click the world to cast (Esc cancels)</color>", smallStyle);
            else if (Time.time - god.LastFeedbackTime < 3f && !string.IsNullOrEmpty(god.LastFeedbackMessage))
                GUILayout.Label($"<color=#9fd0ff>{god.LastFeedbackMessage}</color>", smallStyle);
        }

        GUILayout.Space(4);
        GUILayout.Label("RMB Orbit  |  Scroll Zoom", smallStyle);
        GUILayout.Label("ESC Cancel Power / Pause", smallStyle);
        if (WeatherSystem.Instance != null)
            GUILayout.Label($"Weather: <b>{WeatherSystem.Instance.Label}</b>", labelStyle);

        GUILayout.EndArea();
    }

    void DrawFavorBar(float value, float max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("Favor", labelStyle, GUILayout.Width(46));
        var rect = GUILayoutUtility.GetRect(150, 14);
        GUI.color = new Color(0.13f, 0.14f, 0.17f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = new Color(0.55f, 0.75f, 1f);
        GUI.DrawTexture(new Rect(rect.x + 1, rect.y + 1, (rect.width - 2) * Mathf.Clamp01(value / max), rect.height - 2), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x, rect.y - 1, rect.width - 5, rect.height), $"{value:F0}/{max:F0}", barValueStyle);
        GUILayout.EndHorizontal();
    }

    void DrawRoster()
    {
        var creatures = CreatureMind.All;
        if (creatures.Count == 0) return;

        float w = 220f;
        float top = controlsBottom;

        if (rosterMinimized)
        {
            var minRect = new Rect(Screen.width - w - 10, top, w, 36);
            UIPointerGuard.Register(minRect);

            GUILayout.BeginArea(minRect, boxStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>Creatures</b> ({creatures.Count})", headerStyle);
            if (GUILayout.Button("+", GUILayout.Width(26))) rosterMinimized = false;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            return;
        }

        float maxH = Screen.height - top - 20f;
        var rect = new Rect(Screen.width - w - 10, top, w, maxH);
        UIPointerGuard.Register(rect);

        GUILayout.BeginArea(rect, boxStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label("<b>Creatures</b>", headerStyle);
        if (GUILayout.Button("–", GUILayout.Width(26))) rosterMinimized = true;
        GUILayout.EndHorizontal();
        GUILayout.Space(4);

        rosterScroll = GUILayout.BeginScrollView(rosterScroll);

        foreach (var creature in creatures)
        {
            bool isSelected = creature == selectedCreature;
            var body = creature.GetComponent<CreatureBody>();
            var style = isSelected ? entrySelectedStyle : entryNormalStyle;

            GUILayout.BeginVertical(style);

            string repTag = creature.ReputationLabel != "unknown" ? $"  <color={GetReputationColor(creature.ReputationLabel)}>{creature.ReputationLabel}</color>" : "";
            GUILayout.Label($"<b>{creature.CreatureName}</b>  <color=#aaaaaa>{creature.Personality}</color>{repTag}", labelStyle);

            GUILayout.BeginHorizontal();
            DrawMiniBar("H", creature.Hunger, new Color(0.9f, 0.3f, 0.2f));
            DrawMiniBar("S", creature.Safety, new Color(0.9f, 0.8f, 0.2f));
            DrawMiniBar("So", creature.Social, new Color(0.2f, 0.7f, 0.9f));
            DrawMiniBar("A", creature.Awe, new Color(0.6f, 0.3f, 0.9f));
            GUILayout.EndHorizontal();

            string goalColor = GetGoalColor(body.CurrentGoal);
            string dayIcon = creature.IsInDaylight ? "<color=#ffdd44>Day</color>" : "<color=#4466aa>Night</color>";
            GUILayout.Label($"<color={goalColor}>{body.CurrentGoal}</color>  {dayIcon}", labelStyle);

            if (!string.IsNullOrEmpty(creature.LastSay))
                GUILayout.Label($"\"{creature.LastSay}\"", italicStyle);

            GUILayout.EndVertical();

            Rect fullRect = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.MouseDown && fullRect.Contains(Event.current.mousePosition))
            {
                SelectCreature(creature);
                Event.current.Use();
            }
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    void DrawDetailPanel()
    {
        if (selectedCreature == null) return;

        float w = 360f;
        float h = Mathf.Min(Screen.height * 0.6f, 560f);
        var rect = new Rect(10, Screen.height - h - 10, w, h);
        UIPointerGuard.Register(rect);

        GUILayout.BeginArea(rect, boxStyle);

        detailScroll = GUILayout.BeginScrollView(detailScroll);

        var body = selectedCreature.GetComponent<CreatureBody>();
        string dayNight = selectedCreature.IsInDaylight ? "<color=#ffdd44>Daytime</color>" : "<color=#4466aa>Nighttime</color>";

        GUILayout.Label($"<b>{selectedCreature.CreatureName}</b> — {selectedCreature.Personality}  {dayNight}", headerStyle);
        GUILayout.Label($"Reputation: <color={GetReputationColor(selectedCreature.ReputationLabel)}>{selectedCreature.ReputationLabel}</color>   (shared {selectedCreature.TimesShared} · stolen {selectedCreature.TimesStolen} · traded {selectedCreature.TimesTraded})", smallStyle);
        GUILayout.Label($"Life stage: <b>{body.Stage}</b>", smallStyle);
        GUILayout.Space(6);

        DrawGodActions(body);

        // Vitals
        GUILayout.Label("Vitals", sectionStyle);
        DrawBar("Health", body.Health, body.Health > 0.5f ? Color.green : new Color(0.9f, 0.2f, 0.1f));
        DrawBar("Exposure", body.Exposure, body.Exposure > 0.5f ? new Color(0.9f, 0.2f, 0.1f) : new Color(0.4f, 0.6f, 0.8f));

        GUILayout.Space(4);

        // Needs
        GUILayout.Label("Needs", sectionStyle);
        DrawBar("Hunger", selectedCreature.Hunger, new Color(0.9f, 0.3f, 0.2f));
        DrawBar("Thirst", selectedCreature.Thirst, new Color(0.3f, 0.6f, 0.95f));
        DrawBar("Safety", selectedCreature.Safety, new Color(0.9f, 0.8f, 0.2f));
        DrawBar("Social", selectedCreature.Social, new Color(0.2f, 0.7f, 0.9f));
        DrawBar("Awe", selectedCreature.Awe, new Color(0.6f, 0.3f, 0.9f));

        GUILayout.Space(4);

        // Shelter
        if (body.Camp != null)
        {
            string shelterName = body.Camp.GetShelterName();
            int lvl = body.Camp.ShelterLevel;
            GUILayout.Label($"Shelter: <b>{shelterName}</b> (level {lvl}/3)", labelStyle);
        }

        GUILayout.Space(8);

        // Intent
        GUILayout.Label("Intent", sectionStyle);
        string goalColor = GetGoalColor(body.CurrentGoal);
        GUILayout.Label($"<color={goalColor}><b>{body.CurrentGoal}</b></color>  intensity: {body.GoalIntensity:F1}", labelStyle);

        GUILayout.Space(8);

        // Inner thought
        if (!string.IsNullOrEmpty(selectedCreature.LastReasoning))
        {
            GUILayout.Label("Thinking", sectionStyle);
            GUILayout.Label(selectedCreature.LastReasoning, italicStyle);
            float elapsed = Time.time - selectedCreature.LastThinkTime;
            GUILayout.Label($"  ({elapsed:F0}s ago)", smallStyle);
            GUILayout.Space(4);
        }

        // Speech
        if (!string.IsNullOrEmpty(selectedCreature.LastSay))
        {
            GUILayout.Label("Speaking", sectionStyle);
            GUILayout.Label($"\"{selectedCreature.LastSay}\"", labelStyle);
            GUILayout.Space(4);
        }

        // Resources
        if (body.TotalCarried > 0f || (body.Camp != null && body.Camp.TotalStock() > 0f))
        {
            GUILayout.Label("Resources", sectionStyle);
            if (body.TotalCarried > 0f)
            {
                string carrying = "";
                if (body.CarriedFood > 0f) carrying += $"Berries:{body.CarriedFood:F0} ";
                if (body.CarriedMeat > 0f) carrying += $"Meat:{body.CarriedMeat:F0} ";
                if (body.CarriedStone > 0f) carrying += $"Stone:{body.CarriedStone:F0} ";
                if (body.CarriedWood > 0f) carrying += $"Wood:{body.CarriedWood:F0} ";
                GUILayout.Label($"  Carrying: {carrying.Trim()}", labelStyle);
            }
            if (body.Camp != null)
            {
                float food = body.Camp.GetStock(ResourceNode.ResourceType.Berry);
                float meat = body.Camp.GetStock(ResourceNode.ResourceType.Meat);
                float stone = body.Camp.GetStock(ResourceNode.ResourceType.Stone);
                float wood = body.Camp.GetStock(ResourceNode.ResourceType.Wood);
                if (food + meat + stone + wood > 0f)
                    GUILayout.Label($"  Camp: Berries:{food:F0}  Meat:{meat:F0}  Stone:{stone:F0}  Wood:{wood:F0}", labelStyle);
            }
            GUILayout.Space(4);
        }

        // Belief
        GUILayout.Label("Belief about the God", sectionStyle);
        GUILayout.Label(selectedCreature.CurrentBelief, labelStyle);

        GUILayout.Space(8);

        // Memories
        GUILayout.Label("Memories", sectionStyle);
        var memories = selectedCreature.Memories;
        if (memories.Count == 0)
        {
            GUILayout.Label("  (no memories yet)", smallStyle);
        }
        else
        {
            for (int i = memories.Count - 1; i >= Mathf.Max(0, memories.Count - 8); i--)
            {
                bool isGodEvent = memories[i].StartsWith("[GOD EVENT]");
                var style = isGodEvent ? labelStyle : smallStyle;
                string prefix = isGodEvent ? "<color=#ff8844>!</color> " : "  - ";
                GUILayout.Label($"{prefix}{memories[i]}", style);
            }
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    /// <summary>Direct, per-creature god actions — rename (free), gift supplies, and speak a prophecy
    /// (both cost favor and feed into the creature's memory/beliefs via the existing LLM prompt).</summary>
    void DrawGodActions(CreatureBody body)
    {
        var god = GodEventBus.Instance;

        GUILayout.Label("God Actions", sectionStyle);

        GUILayout.BeginHorizontal();
        renameInput = GUILayout.TextField(renameInput, GUILayout.Width(210));
        if (GUILayout.Button("Rename", GUILayout.Width(80)) && !string.IsNullOrWhiteSpace(renameInput))
        {
            selectedCreature.Rename(renameInput);
            renameInput = "";
        }
        GUILayout.EndHorizontal();

        if (god != null)
        {
            const float giftCost = 5f;
            const float prophecyCost = 8f;

            GUI.enabled = god.Favor >= giftCost;
            if (GUILayout.Button($"Gift Supplies  ({giftCost:F0} favor)"))
            {
                if (god.TrySpendFavor(giftCost))
                    body.ReceiveGodGift(3f, 3f, 2f, 2f);
            }
            GUI.enabled = true;

            GUILayout.BeginHorizontal();
            prophecyInput = GUILayout.TextField(prophecyInput, GUILayout.Width(210));
            GUI.enabled = god.Favor >= prophecyCost && !string.IsNullOrWhiteSpace(prophecyInput);
            if (GUILayout.Button("Speak", GUILayout.Width(80)))
            {
                if (god.TrySpendFavor(prophecyCost))
                {
                    selectedCreature.AddGodMemory($"A voice from above spoke to me: \"{prophecyInput}\"");
                    selectedCreature.FeelAwe(0.15f);
                    prophecyInput = "";
                }
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(6);
    }

    void DrawBar(string label, float value, Color color)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, labelStyle, GUILayout.Width(58));

        var rect = GUILayoutUtility.GetRect(200, 14);
        GUI.color = new Color(0.13f, 0.14f, 0.17f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = color;
        GUI.DrawTexture(new Rect(rect.x + 1, rect.y + 1, (rect.width - 2) * Mathf.Clamp01(value), rect.height - 2), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x, rect.y - 1, rect.width - 5, rect.height), $"{value:P0}", barValueStyle);

        GUILayout.EndHorizontal();
    }

    void DrawMiniBar(string label, float value, Color color)
    {
        var rect = GUILayoutUtility.GetRect(42, 12);
        GUI.color = new Color(0.1f, 0.1f, 0.1f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = color;
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(value), rect.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(rect, label, miniBarLabelStyle);
    }

    string GetReputationColor(string label)
    {
        return label switch
        {
            "generous" => "#66dd88",
            "kind" => "#aaddaa",
            "thief" => "#ff5555",
            "untrustworthy" => "#ddaa44",
            _ => "#999999"
        };
    }

    string GetGoalColor(CreatureGoal goal)
    {
        return goal switch
        {
            CreatureGoal.FLEE => "#ff4444",
            CreatureGoal.FORAGE => "#88cc44",
            CreatureGoal.SEEK_OTHERS => "#44aaff",
            CreatureGoal.HUDDLE => "#44cccc",
            CreatureGoal.WORSHIP => "#bb66ff",
            CreatureGoal.REST => "#888888",
            CreatureGoal.HUNT => "#ff6600",
            CreatureGoal.GATHER => "#ddaa22",
            CreatureGoal.BUILD => "#22bb88",
            CreatureGoal.SHARE => "#66dd88",
            CreatureGoal.TRADE => "#cc99ff",
            CreatureGoal.DRINK => "#3aa0e0",
            _ => "#cccccc"
        };
    }
}
