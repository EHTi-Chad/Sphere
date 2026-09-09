using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only toggle for whether the in-process LLM loads during Play in the Editor.
/// Off by default: while iterating on code, creatures run on the fast-layer AI and the native
/// llama.cpp library is never loaded — so script recompiles can't deadlock the domain reload.
/// Turn it ON (Sphere menu) only when you specifically want to test the LLM. Builds always load it.
/// </summary>
public static class SphereLLMMenu
{
    public const string Key = "Sphere.LoadLLMInEditor";
    const string MenuPath = "Sphere/Load LLM In Editor";

    [MenuItem(MenuPath)]
    static void Toggle()
    {
        bool now = !EditorPrefs.GetBool(Key, true);
        EditorPrefs.SetBool(Key, now);
        Debug.Log($"[Sphere] Load LLM In Editor: {(now ? "ON" : "OFF")}" +
                  (now ? " — model will load on Play (force-quit + reopen instead of hot-recompiling while it's loaded)."
                       : " — creatures use fast-layer AI; recompiles are safe."));
    }

    [MenuItem(MenuPath, true)]
    static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, EditorPrefs.GetBool(Key, true));
        return true;
    }
}
