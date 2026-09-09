using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lets IMGUI panels register the screen Rect they occupy each frame, so world-click handlers
/// (god powers, creature selection) can check "is the pointer over UI?" before raycasting into the
/// 3D scene. IMGUI has no EventSystem to ask (that's a uGUI concept), so this is the simplest
/// equivalent: panels call Register(rect) from OnGUI, click-handlers call IsPointerOverUI(mousePos)
/// from Update(). One frame of lag between registration and use is fine — panel layout is stable.
/// </summary>
public static class UIPointerGuard
{
    static readonly List<Rect> blockingRects = new List<Rect>();
    static int clearedFrame = -1;

    /// <summary>Call once per OnGUI from each panel, passing the Rect passed to GUILayout.BeginArea.</summary>
    public static void Register(Rect screenRectTopLeftOrigin)
    {
        // OnGUI can run multiple times per frame (Layout/Repaint); clear once per frame so
        // Register calls accumulate correctly instead of growing forever.
        if (clearedFrame != Time.frameCount)
        {
            blockingRects.Clear();
            clearedFrame = Time.frameCount;
        }
        blockingRects.Add(screenRectTopLeftOrigin);
    }

    /// <summary>Pass mouse.position.ReadValue() from the Input System (bottom-left origin) — this
    /// flips it to match IMGUI's top-left-origin Rects internally.</summary>
    public static bool IsPointerOverUI(Vector2 inputSystemScreenPos)
    {
        Vector2 guiPos = new Vector2(inputSystemScreenPos.x, Screen.height - inputSystemScreenPos.y);
        for (int i = 0; i < blockingRects.Count; i++)
            if (blockingRects[i].Contains(guiPos)) return true;
        return false;
    }
}
