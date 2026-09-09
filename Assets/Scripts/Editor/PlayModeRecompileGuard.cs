#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// Forces "Script Changes While Playing" → "Recompile After Finished Playing" on every editor load.
///
/// Recompiling scripts *during* play triggers a domain reload, and a domain reload while the local
/// llama.cpp LLM is running hangs the editor (it waits on the LLM's native threads). The project
/// already disables domain reload on play enter/exit; this closes the last path by deferring ALL
/// in-play script changes until play stops — at which point the reload is clean (no LLM running).
///
/// Enforced here so nobody has to remember to set it in Preferences. To revert: Edit → Preferences →
/// General → "Script Changes While Playing", or delete this file.
/// </summary>
[InitializeOnLoad]
public static class PlayModeRecompileGuard
{
    // Edit > Preferences > General > "Script Changes While Playing":
    //   0 = Recompile And Continue Playing  (the dangerous one — mid-play domain reload)
    //   1 = Recompile After Finished Playing (what we want)
    //   2 = Stop Playing And Recompile
    const string Key = "ScriptCompilationDuringPlay";

    static PlayModeRecompileGuard()
    {
        if (EditorPrefs.GetInt(Key, 0) != 1)
            EditorPrefs.SetInt(Key, 1);
    }
}
#endif
