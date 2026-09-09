using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

[Serializable]
public class CreatureSave
{
    public string name;
    public int personality;

    public float posX, posY, posZ;

    public float hunger, thirst, safety, social, awe;
    public float health, exposure;

    public string belief;
    public string lastSay;
    public List<string> memories = new List<string>();

    // relationships stored as parallel lists (JsonUtility can't serialize dictionaries)
    public List<string> relNames = new List<string>();
    public List<float> relValues = new List<float>();

    public int timesShared, timesStolen, timesTraded;

    public float carriedFood, carriedWood, carriedStone, carriedMeat;

    // life stage
    public int lifeStage;

    // camp
    public float campX, campY, campZ;
    public int shelterLevel;
    public bool campHasFirePit;
    public float campFood, campWood, campStone, campMeat;
}

[Serializable]
public class SaveData
{
    public int seed;
    public float radius;
    public float timeOfDay;
    public string savedAtUtc;
    public List<CreatureSave> creatures = new List<CreatureSave>();
}

/// <summary>
/// Saves/loads the whole sphere to JSON in the OS user-data folder.
/// The world (terrain, decoration, resources) is regenerated from the seed;
/// only the dynamic state (creatures, camps, time of day) is serialized.
/// </summary>
public static class SaveSystem
{
    // Set just before a world is generated; consumed by CreatureSpawner to restore creatures.
    public static SaveData PendingLoad;

    static string Dir => Path.Combine(Application.persistentDataPath, "saves");
    public static string SlotPath(string slot) => Path.Combine(Dir, slot + ".json");

    public static List<string> ListSaves()
    {
        try
        {
            if (Directory.Exists(Dir))
                return Directory.GetFiles(Dir, "*.json")
                                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                                .Select(Path.GetFileNameWithoutExtension)
                                .ToList();
        }
        catch (Exception e) { Debug.LogWarning($"[Save] List failed: {e.Message}"); }
        return new List<string>();
    }

    public static bool Save(string slot)
    {
        var data = Gather();
        if (data == null) { Debug.LogWarning("[Save] No active world to save."); return false; }
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(SlotPath(slot), JsonUtility.ToJson(data, true));
            Debug.Log($"[Save] Wrote {SlotPath(slot)} ({data.creatures.Count} creatures)");
            return true;
        }
        catch (Exception e) { Debug.LogWarning($"[Save] Write failed: {e.Message}"); return false; }
    }

    public static SaveData LoadData(string slot)
    {
        try
        {
            if (File.Exists(SlotPath(slot)))
                return JsonUtility.FromJson<SaveData>(File.ReadAllText(SlotPath(slot)));
        }
        catch (Exception e) { Debug.LogWarning($"[Save] Read failed: {e.Message}"); }
        return null;
    }

    public static void Delete(string slot)
    {
        try { if (File.Exists(SlotPath(slot))) File.Delete(SlotPath(slot)); }
        catch (Exception e) { Debug.LogWarning($"[Save] Delete failed: {e.Message}"); }
    }

    static SaveData Gather()
    {
        var bootstrap = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
        if (bootstrap == null || !bootstrap.IsWorldGenerated) return null;

        var data = new SaveData
        {
            seed = bootstrap.Seed,
            radius = bootstrap.WorldRadius,
            timeOfDay = DayNightCycle.Instance != null ? DayNightCycle.Instance.TimeOfDay : 0.25f,
            savedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm")
        };

        foreach (var body in UnityEngine.Object.FindObjectsByType<CreatureBody>(FindObjectsSortMode.None))
            data.creatures.Add(body.ToSave());

        return data;
    }
}
