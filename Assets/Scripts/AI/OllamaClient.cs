using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Central LLM manager. Runs entirely in-process via LLMUnity (llama.cpp) against a local .gguf —
/// no external services. Model resolution order:
///   1. The model picked in Settings → AI (GameConfig)
///   2. A .gguf bundled in StreamingAssets (travels with the project and into builds — portable)
///   3. A .gguf in a Models/ folder next to the project/executable (dev convenience)
/// If no model is found, creatures fall back to the fast-layer AI only.
/// (Class name is legacy — there is no Ollama code anymore.)
/// </summary>
public class OllamaClient : MonoBehaviour
{
    public static OllamaClient Instance { get; private set; }

    ILLMBackend backend;
    List<ModelInfo> availableModels = new List<ModelInfo>();

    public int RequestsSent { get; private set; }
    public int ResponsesReceived { get; private set; }
    public int Errors { get; private set; }
    public int ActiveRequests { get; private set; }
    public float LastResponseTime { get; private set; }
    public string LastError { get; private set; } = "";
    public string ModelName => backend?.CurrentModel ?? "none";
    public string BackendName => backend?.BackendName ?? "No backend";
    public bool IsReady => backend?.IsReady ?? false;
    public IReadOnlyList<ModelInfo> AvailableModels => availableModels;

    void Awake()
    {
        // Persist across world loads. The LLM is expensive to load and UNSAFE to dispose while an
        // inference is running (freeing the native service mid-completion is a hard crash — that's
        // what "Quit to Main Menu" hit). So it lives on its own object that UnloadWorld never touches,
        // and is reused for every New Game rather than recreated.
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        InitializeBackend();
    }

    void InitializeBackend()
    {
#if UNITY_EDITOR
        // Skip the LLM in the Editor unless explicitly enabled (Sphere → Load LLM In Editor).
        // Keeps the native library out of the process during code iteration so recompiles can't
        // deadlock the domain reload. Builds are unaffected (UNITY_EDITOR is false there).
        if (!UnityEditor.EditorPrefs.GetBool("Sphere.LoadLLMInEditor", true)) // matches SphereLLMMenu.Key (default ON)
        {
            Debug.Log("[LLM] Not loaded in Editor (enable via Sphere → Load LLM In Editor to test AI). " +
                      "Creatures use fast-layer AI.");
            return;
        }
#endif
#if LLM_UNITY_AVAILABLE
        string modelPath = ResolveModelPath();
        if (string.IsNullOrEmpty(modelPath))
        {
            Debug.LogWarning("[LLM] No .gguf model found. Pick one in Settings → AI, or drop one in " +
                             "Assets/StreamingAssets. Creatures will use fast-layer AI only for now.");
            return;
        }

        Debug.Log($"[LLM] Loading model: {modelPath}");
        var llamaBackend = gameObject.AddComponent<LlamaCppBackend>();
        backend = llamaBackend;
        backend.Initialize(modelPath,
            () => {
                Debug.Log($"[LLM] llama.cpp ready: {backend.CurrentModel}");
                RefreshModelList();
            },
            err => Debug.LogWarning($"[LLM] Model failed to load: {err}. Creatures will use fast-layer AI only."));
#else
        Debug.LogWarning("[LLM] LLMUnity not available (LLM_UNITY_AVAILABLE not defined). " +
                         "Creatures will use fast-layer AI only.");
#endif
    }

    /// <summary>Finds the .gguf to load — user choice first, then in-project (portable), then a dev folder.</summary>
    string ResolveModelPath()
    {
        var cfg = GameConfig.Instance;
        if (cfg != null && cfg.HasValidModel)
            return cfg.modelPath;

        // Bundled in the project — travels with it and into builds.
        string sa = Application.streamingAssetsPath;
        string found = FirstGguf(sa) ?? FirstGguf(Path.Combine(sa, "Models"));
        if (found != null) return found;

        // A Models/ folder next to the project root / executable.
        return FirstGguf(Path.Combine(Application.dataPath, "..", "Models"));
    }

    static string FirstGguf(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                var files = Directory.GetFiles(dir, "*.gguf");
                if (files.Length > 0) return files[0];
            }
        }
        catch { }
        return null;
    }

    public void RefreshModelList()
    {
        backend?.ListAvailableModels(models =>
        {
            availableModels = models ?? new List<ModelInfo>();
            Debug.Log($"[LLM] Found {availableModels.Count} available models");
        });
    }

    public void SwitchModel(string modelNameOrPath)
    {
        backend?.SetModel(modelNameOrPath);
        Debug.Log($"[LLM] Model switched to: {modelNameOrPath}");
    }

    public void RequestThought(string prompt, Action<LLMResponse> onComplete, Action<string> onError = null)
    {
        if (backend == null || !backend.IsReady)
        {
            onError?.Invoke("Backend not ready");
            return;
        }

        RequestsSent++;
        ActiveRequests++;
        float startTime = Time.realtimeSinceStartup;

        backend.RequestCompletion(prompt,
            response =>
            {
                LastResponseTime = Time.realtimeSinceStartup - startTime;
                ResponsesReceived++;
                ActiveRequests--;
                onComplete?.Invoke(response);
            },
            error =>
            {
                Errors++;
                ActiveRequests--;
                LastError = error;
                onError?.Invoke(error);
            }
        );
    }
}
