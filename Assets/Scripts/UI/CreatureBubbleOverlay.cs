using UnityEngine;

/// <summary>
/// Draws short speech bubbles above creatures' heads when they speak or interact.
/// Reads CreatureMind.LastSay / LastSayTime — bubbles fade out after a few seconds.
/// </summary>
public class CreatureBubbleOverlay : MonoBehaviour
{
    [SerializeField] float bubbleDuration = 6f; // creatures speak less often now (slower think cadence), so give it longer to actually be read
    [SerializeField] float headOffset = 1.9f;
    [SerializeField] float maxScreenDepth = 140f; // hide bubbles for far-away creatures

    GUIStyle bubbleStyle;
    Texture2D bubbleBg;
    bool init;

    void InitStyle()
    {
        if (init) return;
        init = true;

        bubbleBg = MakeTex(new Color(0.05f, 0.05f, 0.08f, 0.8f));
        bubbleStyle = new GUIStyle(GUI.skin.box)
        {
            wordWrap = true,
            fontSize = 11,
            alignment = TextAnchor.MiddleCenter,
            richText = true,
            padding = new RectOffset(7, 7, 5, 5)
        };
        bubbleStyle.normal.background = bubbleBg;
        bubbleStyle.normal.textColor = Color.white;
    }

    Texture2D MakeTex(Color c)
    {
        var t = new Texture2D(2, 2);
        t.SetPixels(new[] { c, c, c, c });
        t.Apply();
        return t;
    }

    void OnGUI()
    {
        if (MainMenu.Instance != null && MainMenu.Instance.IsOpen) return;

        var cam = Camera.main;
        if (cam == null) return;

        InitStyle();

        const float w = 160f;
        var minds = CreatureMind.All;
        for (int mi = 0; mi < minds.Count; mi++)
        {
            var m = minds[mi];
            if (string.IsNullOrEmpty(m.LastSay)) continue;
            if (Time.time - m.LastSayTime > bubbleDuration) continue;

            Vector3 up = m.transform.position.normalized; // planet centered at origin
            Vector3 headWorld = m.transform.position + up * headOffset;
            Vector3 sp = cam.WorldToScreenPoint(headWorld);
            if (sp.z <= 0f || sp.z > maxScreenDepth) continue; // behind camera or too far

            var content = new GUIContent(m.LastSay);
            float h = bubbleStyle.CalcHeight(content, w);
            float x = Mathf.Clamp(sp.x - w * 0.5f, 4f, Screen.width - w - 4f);
            float y = Screen.height - sp.y - h - 8f;

            GUI.Label(new Rect(x, y, w, h), content, bubbleStyle);
        }
    }
}
