using System;
using System.Collections.Generic;

[Serializable]
public class LLMResponse
{
    public string reasoning;
    public string goal;
    public float intensity;
    public string say;
    public string belief;

    public CreatureGoal ParseGoal()
    {
        if (Enum.TryParse<CreatureGoal>(goal, true, out var parsed))
            return parsed;
        return CreatureGoal.WANDER;
    }
}

/// <summary>
/// Abstraction layer for LLM inference backends.
/// Implementation: LlamaCppBackend (in-process llama.cpp via LLMUnity).
/// </summary>
public interface ILLMBackend
{
    string BackendName { get; }
    string CurrentModel { get; }
    bool IsReady { get; }

    void Initialize(string modelPath, Action onReady, Action<string> onError);
    void Shutdown();
    void SetModel(string modelNameOrPath);
    void RequestCompletion(string prompt, Action<LLMResponse> onComplete, Action<string> onError);
    void ListAvailableModels(Action<List<ModelInfo>> onResult);
}

[Serializable]
public class ModelInfo
{
    public string name;
    public string path;
    public string size;
    public string quantization;
    public string family;
}
