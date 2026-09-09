using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Persistent game configuration stored as JSON in the OS user-data folder.
/// Holds the user's chosen LLM backend and model file (bring-your-own-model).
/// </summary>
[Serializable]
public class GameConfig
{
    public bool firstRunComplete = false;
    public string backendMode = "gguf";   // model source (kept for forward-compat)
    public string modelPath = "";          // absolute path to the chosen .gguf

    static GameConfig _instance;
    public static GameConfig Instance
    {
        get
        {
            if (_instance == null) _instance = Load();
            return _instance;
        }
    }

    static string ConfigPath => Path.Combine(Application.persistentDataPath, "config.json");

    public static GameConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var cfg = JsonUtility.FromJson<GameConfig>(File.ReadAllText(ConfigPath));
                if (cfg != null) return cfg;
            }
        }
        catch (Exception e) { Debug.LogWarning($"[Config] Load failed: {e.Message}"); }
        return new GameConfig();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(ConfigPath, JsonUtility.ToJson(this, true));
            Debug.Log($"[Config] Saved to {ConfigPath}");
        }
        catch (Exception e) { Debug.LogWarning($"[Config] Save failed: {e.Message}"); }
    }

    public bool HasValidModel =>
        backendMode == "gguf" &&
        !string.IsNullOrEmpty(modelPath) &&
        modelPath.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) &&
        File.Exists(modelPath);

    // ---- GPU-aware guidance for the setup screen ----

    public static string GpuInfo()
    {
        int vram = SystemInfo.graphicsMemorySize; // MB (approximate)
        string name = SystemInfo.graphicsDeviceName;
        return vram > 0 ? $"{name} ({vram} MB VRAM)" : name;
    }

    public static string GpuRecommendation()
    {
        int vram = SystemInfo.graphicsMemorySize; // MB
        if (vram <= 0)    return "unknown VRAM — start with a 1B model (Q4)";
        if (vram < 4000)  return "0.5B–1B model (Q4)";
        if (vram < 6500)  return "1.5B–3B model (Q4)";
        if (vram < 10000) return "3B model (Q4)";
        if (vram < 14000) return "7–8B model (Q4)";
        if (vram < 20000) return "8–14B model (Q4)";
        if (vram < 28000) return "14–32B model (Q4)";
        return "up to 32B model (Q4)";
    }
}
