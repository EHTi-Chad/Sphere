using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;

public class OllamaClient : MonoBehaviour
{
    public static OllamaClient Instance { get; private set; }

    [SerializeField] string ollamaUrl = "http://localhost:11434/api/generate";
    [SerializeField] string model = "qwen2.5:0.5b";

    public int RequestsSent { get; private set; }
    public int ResponsesReceived { get; private set; }
    public int Errors { get; private set; }
    public int ActiveRequests { get; private set; }
    public float LastResponseTime { get; private set; }
    public string LastError { get; private set; } = "";
    public string ModelName => model;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        StartCoroutine(PingOllama());
    }

    IEnumerator PingOllama()
    {
        var request = new UnityWebRequest("http://localhost:11434/api/tags", "GET");
        request.downloadHandler = new DownloadHandlerBuffer();
        request.timeout = 5;
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
            Debug.Log("[Ollama] Connected — available models: " + request.downloadHandler.text);
        else
            Debug.LogWarning("[Ollama] Not reachable: " + request.error);

        request.Dispose();
    }

    public void RequestThought(string prompt, Action<LLMResponse> onComplete, Action<string> onError = null)
    {
        RequestsSent++;
        ActiveRequests++;
        StartCoroutine(SendRequest(prompt, onComplete, onError));
    }

    IEnumerator SendRequest(string prompt, Action<LLMResponse> onComplete, Action<string> onError)
    {
        var requestBody = new OllamaRequest
        {
            model = model,
            prompt = prompt,
            stream = false,
            format = "json"
        };

        string json = JsonUtility.ToJson(requestBody);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        float startTime = Time.realtimeSinceStartup;

        var request = new UnityWebRequest(ollamaUrl, "POST");
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = 30;

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Errors++;
            ActiveRequests--;
            LastError = request.error;
            Debug.LogWarning($"[Ollama] Request failed: {request.error}");
            onError?.Invoke(request.error);
            request.Dispose();
            yield break;
        }

        try
        {
            LastResponseTime = Time.realtimeSinceStartup - startTime;
            var ollamaResponse = JsonUtility.FromJson<OllamaResponse>(request.downloadHandler.text);
            var llmResponse = JsonUtility.FromJson<LLMResponse>(ollamaResponse.response);
            ResponsesReceived++;
            ActiveRequests--;
            onComplete?.Invoke(llmResponse);
        }
        catch (Exception e)
        {
            Errors++;
            ActiveRequests--;
            LastError = e.Message;
            Debug.LogWarning($"[Ollama] Parse error: {e.Message}\nRaw: {request.downloadHandler.text}");
            onError?.Invoke($"Parse error: {e.Message}");
        }

        request.Dispose();
    }

    [Serializable]
    class OllamaRequest
    {
        public string model;
        public string prompt;
        public bool stream;
        public string format;
    }

    [Serializable]
    class OllamaResponse
    {
        public string response;
    }
}

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
