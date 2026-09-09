using System.Diagnostics;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.InputSystem;

public class DebugOverlay : MonoBehaviour
{
    bool visible = true;
    bool collapsed = true; // start minimized — just FPS; F2 to expand the full GPU/AI/memory panel
    float updateInterval = 2f;
    float timer;
    volatile bool gpuQueryRunning;

    // FPS
    int frameCount;
    float fpsTimer;
    float currentFps;

    // GPU info from nvidia-smi
    string gpuName = "...";
    int gpuUtil;
    int gpuMemUsed;
    int gpuMemTotal;
    int gpuTemp;

    // Ollama
    int ollamaMemMB;
    bool ollamaRunning;

    // Unity
    long unityAllocated;
    long unityReserved;
    long gfxAllocated;

    GUIStyle bgStyle;
    GUIStyle headerStyle;
    GUIStyle labelStyle;
    GUIStyle valueStyle;
    GUIStyle fpsStyle;
    GUIStyle footerStyle;
    Texture2D bgTex;
    bool stylesInit;

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.f3Key.wasPressedThisFrame)
            visible = !visible;
        if (kb != null && kb.f2Key.wasPressedThisFrame)
            collapsed = !collapsed;

        frameCount++;
        fpsTimer += Time.unscaledDeltaTime;
        if (fpsTimer >= 0.5f)
        {
            currentFps = frameCount / fpsTimer;
            frameCount = 0;
            fpsTimer = 0f;
        }

        timer += Time.unscaledDeltaTime;
        if (timer >= updateInterval)
        {
            timer = 0f;
            QueryUnityMemory(); // Unity Profiler API — main thread only

            // nvidia-smi spawns external processes; run them off the main thread so they never hitch the frame.
            if (!gpuQueryRunning)
            {
                gpuQueryRunning = true;
                System.Threading.Tasks.Task.Run(() =>
                {
                    try { QueryNvidiaSmi(); QueryOllama(); }
                    catch { }
                    finally { gpuQueryRunning = false; }
                });
            }
        }
    }

    void QueryNvidiaSmi()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=name,utilization.gpu,memory.used,memory.total,temperature.gpu --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(2000);

            if (!string.IsNullOrEmpty(output))
            {
                var parts = output.Trim().Split(',');
                if (parts.Length >= 5)
                {
                    gpuName = parts[0].Trim();
                    int.TryParse(parts[1].Trim(), out gpuUtil);
                    int.TryParse(parts[2].Trim(), out gpuMemUsed);
                    int.TryParse(parts[3].Trim(), out gpuMemTotal);
                    int.TryParse(parts[4].Trim(), out gpuTemp);
                }
            }
        }
        catch { }
    }

    void QueryOllama()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-compute-apps=pid,used_memory --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(2000);

            ollamaMemMB = 0;
            ollamaRunning = false;

            if (!string.IsNullOrEmpty(output))
            {
                var lines = output.Trim().Split('\n');
                foreach (var line in lines)
                {
                    try
                    {
                        var parts = line.Trim().Split(',');
                        if (parts.Length >= 2)
                        {
                            int pid = int.Parse(parts[0].Trim());
                            int mem = int.Parse(parts[1].Trim());
                            using var p = Process.GetProcessById(pid);
                            string name = p.ProcessName.ToLowerInvariant();
                            if (name.Contains("ollama") || name.Contains("llama") || name.Contains("ggml") || name.Contains("llm"))
                            {
                                ollamaMemMB += mem;
                                ollamaRunning = true;
                            }
                        }
                    }
                    catch { }
                }
            }

            if (!ollamaRunning)
            {
                // Every element also needs disposing (GetProcesses() hands back a live handle per entry,
                // not just the array) — this loop runs every `updateInterval` for the whole session.
                var ollProcs = Process.GetProcesses();
                foreach (var p in ollProcs)
                {
                    try
                    {
                        string name = p.ProcessName.ToLowerInvariant();
                        if (name.Contains("ollama") || name.Contains("llama"))
                        {
                            ollamaRunning = true;
                            break;
                        }
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
        }
        catch { }
    }

    void QueryUnityMemory()
    {
        unityAllocated = Profiler.GetTotalAllocatedMemoryLong();
        unityReserved = Profiler.GetTotalReservedMemoryLong();
        gfxAllocated = Profiler.GetAllocatedMemoryForGraphicsDriver();
    }

    Texture2D MakeTex(Color col)
    {
        var tex = new Texture2D(2, 2);
        var c = new Color[] { col, col, col, col };
        tex.SetPixels(c);
        tex.Apply();
        return tex;
    }

    void InitStyles()
    {
        if (stylesInit) return;
        stylesInit = true;

        bgTex = MakeTex(new Color(0f, 0f, 0f, 0.7f));
        bgStyle = new GUIStyle { normal = { background = bgTex }, padding = new RectOffset(10, 10, 8, 8) };

        headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.5f, 0.8f, 1f) }
        };

        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            normal = { textColor = new Color(0.6f, 0.6f, 0.6f) }
        };

        valueStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            normal = { textColor = new Color(0.9f, 0.9f, 0.9f) },
            alignment = TextAnchor.MiddleRight
        };

        fpsStyle = new GUIStyle(GUI.skin.label)
        {
            richText = true,
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };

        footerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.4f, 0.4f, 0.4f) }
        };
    }

    void OnGUI()
    {
        if (!visible) return;
        if (MainMenu.Instance != null && MainMenu.Instance.IsOpen) return;

        InitStyles();

        Color fpsColor = currentFps >= 55 ? Color.green : currentFps >= 30 ? Color.yellow : Color.red;

        // Minimized: just a small FPS chip.
        if (collapsed)
        {
            GUILayout.BeginArea(new Rect(10, 10, 150, 30), bgStyle);
            GUILayout.Label($"<color=#{ColorUtility.ToHtmlStringRGB(fpsColor)}>{currentFps:F0} FPS</color>   <size=9>F2</size>", fpsStyle);
            GUILayout.EndArea();
            return;
        }

        GUILayout.BeginArea(new Rect(10, 10, 280, 420), bgStyle);

        // FPS
        GUILayout.Label($"<color=#{ColorUtility.ToHtmlStringRGB(fpsColor)}>{currentFps:F0} FPS</color>  ({Time.unscaledDeltaTime * 1000f:F1} ms)", fpsStyle);

        GUILayout.Space(6);

        // GPU
        GUILayout.Label("GPU", headerStyle);
        DrawRow("Device", gpuName);
        DrawRow("Utilization", $"{gpuUtil}%");
        DrawBar(gpuUtil / 100f, GetUtilColor(gpuUtil));
        DrawRow("VRAM", $"{gpuMemUsed} / {gpuMemTotal} MB");
        DrawBar(gpuMemTotal > 0 ? (float)gpuMemUsed / gpuMemTotal : 0f, GetUtilColor(gpuMemTotal > 0 ? gpuMemUsed * 100 / gpuMemTotal : 0));
        DrawRow("Temperature", $"{gpuTemp} °C");

        GUILayout.Space(6);

        // AI
        GUILayout.Label("AI (llama.cpp)", headerStyle);
        var client = OllamaClient.Instance;
        if (client != null)
        {
            string status;
            if (client.ActiveRequests > 0)
                status = $"Thinking ({client.ActiveRequests} active)";
            else if (client.ResponsesReceived > 0)
                status = "Ready";
            else if (client.Errors > 0)
                status = "Error";
            else
                status = "Waiting...";
            DrawRow("Status", status);
            DrawRow("Backend", client.BackendName);
            DrawRow("Model", client.ModelName);
            DrawRow("Requests", $"{client.RequestsSent} sent");
            DrawRow("Responses", $"{client.ResponsesReceived} ok / {client.Errors} err");
            if (client.LastResponseTime > 0)
                DrawRow("Last think", $"{client.LastResponseTime:F1}s");
            if (client.Errors > 0 && !string.IsNullOrEmpty(client.LastError))
                DrawRow("Last error", client.LastError.Length > 25 ? client.LastError.Substring(0, 25) + "..." : client.LastError);
        }
        else
        {
            DrawRow("Status", "Not initialized");
        }

        if (ollamaRunning && ollamaMemMB > 0)
        {
            DrawRow("VRAM", $"{ollamaMemMB} MB");
            DrawBar(gpuMemTotal > 0 ? (float)ollamaMemMB / gpuMemTotal : 0f, new Color(0.6f, 0.3f, 0.9f));
        }

        GUILayout.Space(6);

        // Unity
        GUILayout.Label("Unity Engine", headerStyle);
        DrawRow("Allocated", $"{unityAllocated / (1024 * 1024)} MB");
        DrawRow("GFX Driver", $"{gfxAllocated / (1024 * 1024)} MB");
        DrawRow("Reserved", $"{unityReserved / (1024 * 1024)} MB");

        GUILayout.Space(4);
        GUILayout.Label("F2 minimize  ·  F3 hide", footerStyle);

        GUILayout.EndArea();
    }

    void DrawRow(string label, string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, labelStyle, GUILayout.Width(100));
        GUILayout.Label(value, valueStyle);
        GUILayout.EndHorizontal();
    }

    void DrawBar(float fill, Color color)
    {
        var rect = GUILayoutUtility.GetRect(260, 6);
        GUI.color = new Color(0.15f, 0.15f, 0.15f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = color;
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fill), rect.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    Color GetUtilColor(int percent)
    {
        if (percent < 50) return Color.green;
        if (percent < 80) return Color.yellow;
        return new Color(1f, 0.3f, 0.2f);
    }
}
