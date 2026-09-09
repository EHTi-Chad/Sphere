#if LLM_UNITY_AVAILABLE
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using LLMUnity;
using UndreamAI.LlamaLib; // for LlamaLib (GPU library selection)
// Both LLMUnity and UndreamAI.LlamaLib define a type named `LLM`; alias resolves the conflict.
using LLM = LLMUnity.LLM;
using LLMCharacter = LLMUnity.LLMCharacter;

/// <summary>
/// llama.cpp backend via LLMUnity. Loads .gguf files from the Models/ directory.
/// No external tools needed — inference runs natively inside the game.
/// </summary>
public class LlamaCppBackend : MonoBehaviour, ILLMBackend
{
    LLM llm;
    LLMCharacter character;
    GameObject llmHolder;
    bool isReady;
    string currentModel;
    string modelsFolder;

    // JSON-schema grammar: constrains output to exactly the object we parse (and valid goals only),
    // so the model can't emit prose or malformed JSON. LLMUnity's SetGrammar accepts JSON schema.
    const string GoalJsonSchema =
        @"{""type"":""object"",""properties"":{" +
        @"""reasoning"":{""type"":""string""}," +
        @"""goal"":{""type"":""string"",""enum"":[""WANDER"",""FORAGE"",""DRINK"",""GATHER"",""BUILD"",""FLEE"",""SEEK_OTHERS"",""SHARE"",""TRADE"",""HUDDLE"",""WORSHIP"",""REST"",""HUNT""]}," +
        @"""intensity"":{""type"":""number""}," +
        @"""say"":{""type"":""string""}," +
        @"""belief"":{""type"":""string""}}," +
        @"""required"":[""reasoning"",""goal"",""intensity"",""say"",""belief""]}";

    public string BackendName => "llama.cpp (Local)";
    public string CurrentModel => currentModel ?? "none";
    public bool IsReady => isReady;

    void Awake()
    {
        // Models folder sits next to the game executable, or in project root during dev
        modelsFolder = Path.Combine(Application.dataPath, "..", "Models");
        if (!Directory.Exists(modelsFolder))
            Directory.CreateDirectory(modelsFolder);
    }

    public void Initialize(string modelPath, Action onReady, Action<string> onError)
    {
        if (string.IsNullOrEmpty(modelPath))
        {
            // Auto-detect first .gguf in Models/
            var files = Directory.GetFiles(modelsFolder, "*.gguf");
            if (files.Length == 0)
            {
                onError?.Invoke("No .gguf models found in Models/ folder");
                return;
            }
            modelPath = files[0];
        }

        // If just a filename, look in Models/
        if (!Path.IsPathRooted(modelPath))
        {
            string fullPath = Path.Combine(modelsFolder, modelPath);
            if (File.Exists(fullPath))
                modelPath = fullPath;
        }

        if (!File.Exists(modelPath))
        {
            onError?.Invoke($"Model not found: {modelPath}");
            return;
        }

        currentModel = Path.GetFileName(modelPath);
        SetupLLM(modelPath, onReady, onError);
    }

    async void SetupLLM(string modelPath, Action onReady, Action<string> onError)
    {
        try
        {
            // LLM.Awake() auto-starts using the configured model, and it runs the moment the
            // GameObject becomes active. So we build everything on an INACTIVE object first,
            // configure it, then activate — otherwise it would try to start with no model set.
            llmHolder = new GameObject("LLMUnity");
            llmHolder.transform.SetParent(transform);
            llmHolder.SetActive(false);

            llm = llmHolder.AddComponent<LLM>();
            llm.SetModel(modelPath);            // absolute BYO path resolves at runtime
            llm.numGPULayers = ChooseGpuLayers(modelPath); // adapt offload to THIS machine's VRAM
            llm.contextSize = 2048;
            llm.numThreads = -1;                // auto
            // NOTE: don't touch llm.reasoning here — its setter calls into the native service,
            // which doesn't exist until the GameObject is activated below. It defaults to false
            // and is applied at service start, so we (re)assert it after WaitUntilReady() instead.

            character = llmHolder.AddComponent<LLMCharacter>();
            character.llm = llm;
            character.temperature = 0.7f;
            character.topP = 0.9f;
            character.numPredict = 256;
            // Grammar intentionally NOT set: LlamaLib v2.0.5's JSON-schema grammar returns empty
            // responses with Qwen3. We rely on the prompt + lenient JSON extraction instead.
            // (GoalJsonSchema kept for if/when we move to a proper GBNF grammar.)
            character.SetPrompt("You are a creature in a survival world. Reply ONLY with a single JSON object and nothing else.", false);

            // Prefer GPU inference. LLMUnity defaults CUBLAS off, which excludes the CUDA library and
            // makes LlamaLib load the CPU-only "tinyblas" build — fatal for a 14B model (runs on CPU,
            // eats system RAM, hangs). Excluding tinyblas instead makes LlamaLib try the GPU libraries
            // first (cublas → vulkan) and fall through to CPU only if no GPU library loads, so this is
            // safe on any machine. Set it before SetActive() so it's in effect when the service starts.
            EnableGpuInference();

            llmHolder.SetActive(true);          // now Awake runs with everything configured

            await llm.WaitUntilReady();

            if (llm.failed)
            {
                onError?.Invoke("LLM failed to start (see console)");
                return;
            }

            // Now the native service exists, so this is safe. Qwen3 etc. emit <think> blocks
            // by default — turn reasoning off so the output is clean JSON for our grammar.
            llm.reasoning = false;

            isReady = true;
            Debug.Log($"[llama.cpp] Model loaded: {currentModel}");
            onReady?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogError($"[llama.cpp] Failed to load model: {e}"); // full stack trace, not just the message
            onError?.Invoke(e.Message);
        }
    }

    /// <summary>
    /// Selects the GPU inference library. The bundled LlamaLib (v2.0.5) ships CUDA kernels that
    /// predate Blackwell (RTX 50xx / sm_120): they load and run briefly, then hard-crash. Vulkan is
    /// vendor-neutral and stable on current GPUs, so we steer LlamaLib to it by excluding the CUDA
    /// (cublas) and CPU-BLAS (tinyblas / avx*) architectures. If this build doesn't ship Vulkan,
    /// startup fails cleanly (caught in SetupLLM) instead of crashing — see notes for the real fix.
    /// </summary>
    void EnableGpuInference()
    {
        // LLMUnity's own startup (LLMUnitySetup.InitializeOnLoad) rebuilds libraryExclusion from its
        // persisted CUBLAS PlayerPref: when CUBLAS is on it excludes "tinyblas" and KEEPS the CUDA
        // library. On Blackwell (RTX 50xx / sm_120) the bundled CUDA kernels hard-crash the GPU
        // (D3D12 device removal → Unity dies), so we must guarantee CUDA never loads:
        //   1. Force CUBLAS off and persist it, so LLMUnity's own exclusion also drops cublas.
        //   2. Exclude every CUDA arch keyword ("cublas" and "cuda") ourselves, right before the
        //      service starts, so Vulkan is preferred and the CPU builds remain as fallback.
        // This machine is an RTX 3070 (Ampere): the bundled CUDA kernels run fast and stably here.
        // The blanket CUDA-disable was a guard for newer Blackwell / sm_120 cards — not this one — and
        // it forced slow CPU (tinyblas) inference, which both lagged the creatures' reactions AND
        // stalled Unity's domain reload on Stop (the reload waits for in-flight native inference, and
        // a CPU thought takes seconds). Prefer the GPU: enable CUBLAS and exclude only the CPU build,
        // so LlamaLib loads cublas (→ vulkan → CPU only if no GPU library is available).
        LLMUnity.LLMUnitySetup.SetCUBLAS(true);
        LlamaLib.libraryExclusion = new List<string> { "tinyblas" };
    }

    /// <summary>
    /// Picks how many transformer layers to offload to the GPU, based on this machine's VRAM
    /// and the chosen model's real size. The same build then runs on a 6 GB laptop (partial /
    /// CPU offload) and a 32 GB workstation (full offload) without OOM-crashing on the small one.
    /// </summary>
    int ChooseGpuLayers(string modelPath)
    {
        int vramMB = SystemInfo.graphicsMemorySize; // approximate total VRAM
        float modelMB = 0f;
        try { modelMB = new FileInfo(modelPath).Length / (1024f * 1024f); } catch { }

        // Unknown VRAM or unreadable file → stay on CPU (slow but never crashes).
        if (vramMB <= 0 || modelMB <= 0f)
        {
            Debug.LogWarning($"[llama.cpp] VRAM/model size unknown (vram={vramMB}MB, model={modelMB:F0}MB) — running on CPU.");
            return 0;
        }

        int layerCount = ReadLayerCount(modelPath);
        if (layerCount <= 0) layerCount = 32; // safe fallback if metadata is missing

        // Reserve headroom for the KV cache, compute buffers, and the game's own rendering.
        float budgetMB = vramMB * 0.6f;

        int layers;
        if (budgetMB >= modelMB)
            layers = layerCount + 1;                                          // fits comfortably → offload all
        else
            layers = Mathf.Clamp(Mathf.FloorToInt(layerCount * budgetMB / modelMB), 0, layerCount);

        Debug.Log($"[llama.cpp] GPU offload: {layers}/{layerCount} layers (vram={vramMB}MB, model={modelMB:F0}MB, budget={budgetMB:F0}MB)");
        return layers;
    }

    /// <summary>Reads the transformer block (layer) count from the GGUF header, or 0 if unavailable.</summary>
    int ReadLayerCount(string modelPath)
    {
        try
        {
            var reader = new GGUFReader(modelPath);
            string arch = reader.GetStringField("general.architecture");
            if (!string.IsNullOrEmpty(arch))
                return reader.GetIntField($"{arch}.block_count");
        }
        catch (Exception e) { Debug.LogWarning($"[llama.cpp] Could not read layer count: {e.Message}"); }
        return 0;
    }

    public void Shutdown()
    {
        isReady = false;

        // CRITICAL: dispose the native llama.cpp service SYNCHRONOUSLY here. Unity's domain reload
        // on play-exit cannot complete until every native inference thread has joined, so if we only
        // queued a deferred Destroy(llmHolder) the editor would hang at "reloading domain" while a
        // thought was still running. CancelRequests() aborts any in-flight Chat; llm.Destroy() then
        // disposes the native service under its own lock before we tear down the GameObject.
        try { character?.CancelRequests(); } catch (Exception e) { Debug.LogWarning($"[llama.cpp] CancelRequests: {e.Message}"); }
        try { llm?.Destroy(); }            catch (Exception e) { Debug.LogWarning($"[llama.cpp] Destroy: {e.Message}"); }

        if (llmHolder != null)
        {
            Destroy(llmHolder);
            llmHolder = null;
        }
        llm = null;
        character = null;
    }

    // Stop the native LLM service promptly when the game/editor stops, so Unity shuts down cleanly
    // (an unclean exit is what triggers the "recovered backup" prompt on next launch, and a still-
    // running native thread is what hangs the domain reload).
    void OnDisable() => Shutdown();
    void OnApplicationQuit() => Shutdown();

    public void SetModel(string modelNameOrPath)
    {
        Shutdown();
        Initialize(modelNameOrPath, null, err => Debug.LogWarning($"[llama.cpp] Switch failed: {err}"));
    }

    public async void RequestCompletion(string prompt, Action<LLMResponse> onComplete, Action<string> onError)
    {
        if (!isReady || character == null)
        {
            onError?.Invoke("Model not loaded");
            return;
        }

        try
        {
            // "/no_think" forces Qwen3 out of thinking mode so it answers directly with JSON
            // (otherwise it spends its token budget inside <think> and returns nothing usable).
            // Stateless one-shot: full prompt each time, don't accumulate chat history.
            string result = await character.Chat(prompt + "\n/no_think", null, null, false);

            string json = ExtractJson(result);
            if (string.IsNullOrEmpty(json))
            {
                onError?.Invoke("Empty response");
                return;
            }

            var llmResponse = JsonUtility.FromJson<LLMResponse>(json);
            onComplete?.Invoke(llmResponse);
        }
        catch (Exception e)
        {
            onError?.Invoke($"Inference error: {e.Message}");
        }
    }

    // Pulls the first {...} object out of the reply — tolerates stray text or leftover
    // <think> tags around the JSON so parsing doesn't fail on an otherwise-good response.
    static string ExtractJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        int a = s.IndexOf('{');
        int b = s.LastIndexOf('}');
        return (a >= 0 && b > a) ? s.Substring(a, b - a + 1) : null;
    }

    public void ListAvailableModels(Action<List<ModelInfo>> onResult)
    {
        var models = new List<ModelInfo>();

        if (Directory.Exists(modelsFolder))
        {
            var files = Directory.GetFiles(modelsFolder, "*.gguf");
            foreach (var file in files)
            {
                var info = new FileInfo(file);
                models.Add(new ModelInfo
                {
                    name = Path.GetFileNameWithoutExtension(file),
                    path = file,
                    size = FormatBytes(info.Length),
                    quantization = GuessQuantization(info.Name),
                    family = ""
                });
            }
        }

        onResult?.Invoke(models);
    }

    string FormatBytes(long bytes)
    {
        if (bytes > 1024L * 1024L * 1024L)
            return $"{bytes / (1024f * 1024f * 1024f):F1} GB";
        return $"{bytes / (1024f * 1024f):F0} MB";
    }

    string GuessQuantization(string filename)
    {
        filename = filename.ToUpperInvariant();
        if (filename.Contains("Q4_K_M")) return "Q4_K_M";
        if (filename.Contains("Q4_K_S")) return "Q4_K_S";
        if (filename.Contains("Q5_K_M")) return "Q5_K_M";
        if (filename.Contains("Q8_0")) return "Q8_0";
        if (filename.Contains("Q4_0")) return "Q4_0";
        if (filename.Contains("Q6_K")) return "Q6_K";
        if (filename.Contains("F16")) return "F16";
        return "";
    }
}
#endif
