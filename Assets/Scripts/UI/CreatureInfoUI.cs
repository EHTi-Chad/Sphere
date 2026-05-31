using UnityEngine;
using UnityEngine.InputSystem;

public class CreatureInfoUI : MonoBehaviour
{
    CreatureMind selectedCreature;
    Vector2 rosterScroll;
    Vector2 detailScroll;

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
    bool stylesInit;

    void Update()
    {
        var mouse = Mouse.current;
        var kb = Keyboard.current;
        if (mouse == null || kb == null) return;
        if (MainMenu.Instance != null && MainMenu.Instance.IsOpen) return;

        if (mouse.leftButton.wasPressedThisFrame &&
            !kb.digit1Key.isPressed && !kb.digit2Key.isPressed && !kb.digit3Key.isPressed)
        {
            Ray ray = Camera.main.ScreenPointToRay(mouse.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                var mind = hit.collider.GetComponentInParent<CreatureMind>();
                if (mind != null)
                {
                    selectedCreature = mind;
                    return;
                }
            }
        }

        if (kb.escapeKey.wasPressedThisFrame)
            selectedCreature = null;
    }

    void InitStyles()
    {
        if (stylesInit) return;
        stylesInit = true;

        boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.normal.background = MakeTex(new Color(0f, 0f, 0f, 0.75f));
        boxStyle.padding = new RectOffset(8, 8, 6, 6);

        headerStyle = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 14, fontStyle = FontStyle.Bold };
        headerStyle.normal.textColor = Color.white;

        labelStyle = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true, fontSize = 12 };
        labelStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);

        smallStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 11 };
        smallStyle.normal.textColor = new Color(0.75f, 0.75f, 0.75f);

        italicStyle = new GUIStyle(labelStyle) { fontStyle = FontStyle.Italic };

        barValueStyle = new GUIStyle(GUI.skin.label) { fontSize = 10 };
        barValueStyle.normal.textColor = Color.white;

        miniBarLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 9, alignment = TextAnchor.MiddleCenter };
        miniBarLabelStyle.normal.textColor = Color.white;

        entryNormalStyle = new GUIStyle(GUI.skin.box);
        entryNormalStyle.normal.background = MakeTex(new Color(0.2f, 0.2f, 0.2f, 0.3f));
        entryNormalStyle.padding = new RectOffset(6, 6, 4, 4);
        entryNormalStyle.margin = new RectOffset(0, 0, 1, 1);

        entrySelectedStyle = new GUIStyle(GUI.skin.box);
        entrySelectedStyle.normal.background = MakeTex(new Color(0.3f, 0.5f, 0.8f, 0.4f));
        entrySelectedStyle.padding = new RectOffset(6, 6, 4, 4);
        entrySelectedStyle.margin = new RectOffset(0, 0, 1, 1);

        sectionStyle = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 12, fontStyle = FontStyle.Bold };
        sectionStyle.normal.textColor = new Color(0.5f, 0.8f, 1f);
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
        DrawControls();
        DrawRoster();
        DrawDetailPanel();
    }

    void DrawControls()
    {
        float w = 220f;
        GUILayout.BeginArea(new Rect(Screen.width - w - 10, 10, w, 90), boxStyle);
        GUILayout.Label("<b>God Powers</b> (hold + click)", headerStyle);
        GUILayout.Label("1 Smite   2 Bless   3 Feed", labelStyle);
        GUILayout.Label("RMB Orbit  |  Scroll Zoom", smallStyle);
        GUILayout.EndArea();
    }

    void DrawRoster()
    {
        var creatures = FindObjectsByType<CreatureMind>(FindObjectsSortMode.None);
        if (creatures.Length == 0) return;

        float w = 220f;
        float top = 110f;
        float maxH = Screen.height - top - 20f;

        GUILayout.BeginArea(new Rect(Screen.width - w - 10, top, w, maxH), boxStyle);
        GUILayout.Label("<b>Creatures</b>", headerStyle);
        GUILayout.Space(4);

        rosterScroll = GUILayout.BeginScrollView(rosterScroll);

        foreach (var creature in creatures)
        {
            bool isSelected = creature == selectedCreature;
            var body = creature.GetComponent<CreatureBody>();
            var style = isSelected ? entrySelectedStyle : entryNormalStyle;

            Rect entryRect = GUILayoutUtility.GetRect(0, 0);

            GUILayout.BeginVertical(style);

            GUILayout.Label($"<b>{creature.CreatureName}</b>  <color=#aaaaaa>{creature.Personality}</color>", labelStyle);

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
                selectedCreature = creature;
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
        float h = Mathf.Min(Screen.height * 0.6f, 520f);
        GUILayout.BeginArea(new Rect(10, Screen.height - h - 10, w, h), boxStyle);

        detailScroll = GUILayout.BeginScrollView(detailScroll);

        var body = selectedCreature.GetComponent<CreatureBody>();
        string dayNight = selectedCreature.IsInDaylight ? "<color=#ffdd44>Daytime</color>" : "<color=#4466aa>Nighttime</color>";

        GUILayout.Label($"<b>{selectedCreature.CreatureName}</b> — {selectedCreature.Personality}  {dayNight}", headerStyle);
        GUILayout.Space(6);

        // Needs
        GUILayout.Label("Needs", sectionStyle);
        DrawBar("Hunger", selectedCreature.Hunger, new Color(0.9f, 0.3f, 0.2f));
        DrawBar("Safety", selectedCreature.Safety, new Color(0.9f, 0.8f, 0.2f));
        DrawBar("Social", selectedCreature.Social, new Color(0.2f, 0.7f, 0.9f));
        DrawBar("Awe", selectedCreature.Awe, new Color(0.6f, 0.3f, 0.9f));

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

    void DrawBar(string label, float value, Color color)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, labelStyle, GUILayout.Width(55));

        var rect = GUILayoutUtility.GetRect(200, 16);
        GUI.color = new Color(0.15f, 0.15f, 0.15f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = color;
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(value), rect.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(rect, $" {value:P0}", barValueStyle);

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
            _ => "#cccccc"
        };
    }
}
