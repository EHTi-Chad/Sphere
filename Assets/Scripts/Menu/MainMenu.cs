using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

public class MainMenu : MonoBehaviour
{
    public static MainMenu Instance { get; private set; }

    enum MenuState { Main, NewGame, Settings, Video, Audio, Controls, AI, FirstRun, ModelSelect, Paused, SaveGame, LoadGame }
    MenuState state = MenuState.Main;
    bool isOpen = true;
    bool gameActive;

    // Where Settings / Save / Load return to (Main or Paused)
    MenuState returnState = MenuState.Main;

    // Save/Load UI
    string saveName = "";
    string saveMessage = "";
    float saveMessageUntil;
    Vector2 saveScroll;

    [Header("New Game")]
    [SerializeField] int seed = 42;
    [SerializeField] float worldRadius = 150f;
    [SerializeField] int creatureCount = 20;

    // Set while GameBootstrap's generation coroutine is running (New Game or Load Game) — drives
    // the progress bar and blocks re-entering the menu's normal controls until it completes.
    bool generatingWorld;
    GameBootstrap generatingBootstrap;

    GUIStyle titleStyle;
    GUIStyle menuButtonStyle;
    GUIStyle backButtonStyle;
    GUIStyle sliderLabelStyle;
    GUIStyle infoStyle;
    GUIStyle overlayStyle;
    Texture2D darkBg;
    Texture2D buttonNormal;
    Texture2D buttonHover;
    bool stylesInit;

    // AI
    Vector2 modelScroll;
    int selectedModelIndex = -1;

    // Model setup
    ModelFileBrowser modelBrowser;
    MenuState modelSelectReturn = MenuState.Main;

    // settings
    float masterVolume = 1f;
    float musicVolume = 0.7f;
    float sfxVolume = 0.8f;
    int qualityLevel;
    bool fullscreen;
    int resolutionIndex;
    Resolution[] resolutions;
    float mouseSensitivity = 0.3f;

    void Awake()
    {
        Instance = this;
        qualityLevel = QualitySettings.GetQualityLevel();
        fullscreen = Screen.fullScreen;
        resolutions = Screen.resolutions;
        resolutionIndex = resolutions.Length - 1;

        // First launch → walk the user through choosing a local model.
        var cfg = GameConfig.Instance;
        if (cfg != null && !cfg.firstRunComplete)
            state = MenuState.FirstRun;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame && !isOpen)
        {
            // GodEventBus (runs first via DefaultExecutionOrder) claims Escape to cancel an armed
            // power — don't also open the pause menu on that same keypress.
            if (GodEventBus.WasEscapeConsumedThisFrame) return;

            // If a creature is focused, the first Escape just unfocuses (handled by camera/UI).
            if (OrbitalCamera.Instance != null && OrbitalCamera.Instance.IsFocused) return;

            isOpen = true;
            state = gameActive ? MenuState.Paused : MenuState.Main;
            Time.timeScale = 0f;
        }
    }

    public bool IsOpen => isOpen;

    public void Close()
    {
        isOpen = false;
        Time.timeScale = 1f;
    }

    Texture2D MakeTex(Color col)
    {
        var tex = new Texture2D(2, 2);
        var pixels = new Color[] { col, col, col, col };
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    void InitStyles()
    {
        if (stylesInit) return;
        stylesInit = true;

        darkBg = MakeTex(new Color(0f, 0f, 0f, 0.85f));
        buttonNormal = MakeTex(new Color(0.15f, 0.15f, 0.15f, 0.9f));
        buttonHover = MakeTex(new Color(0.25f, 0.35f, 0.5f, 0.9f));

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 72,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.85f, 0.9f, 0.95f) }
        };

        menuButtonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 20,
            alignment = TextAnchor.MiddleCenter,
            fixedHeight = 44,
            normal = { background = buttonNormal, textColor = new Color(0.8f, 0.8f, 0.8f) },
            hover = { background = buttonHover, textColor = Color.white },
            active = { background = buttonHover, textColor = Color.white },
            margin = new RectOffset(0, 0, 4, 4),
            padding = new RectOffset(20, 20, 8, 8)
        };

        backButtonStyle = new GUIStyle(menuButtonStyle)
        {
            fontSize = 16,
            fixedHeight = 36
        };

        sliderLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            normal = { textColor = new Color(0.8f, 0.8f, 0.8f) }
        };

        infoStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            wordWrap = true,
            normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
        };

        overlayStyle = new GUIStyle { normal = { background = darkBg } };
    }

    void OnGUI()
    {
        if (!isOpen) return;

        InitStyles();

        GUI.Box(new Rect(0, 0, Screen.width, Screen.height), GUIContent.none, overlayStyle);

        bool wideScreen = state == MenuState.FirstRun || state == MenuState.ModelSelect
                       || state == MenuState.SaveGame || state == MenuState.LoadGame;
        float menuW = wideScreen ? 560f : 320f;
        float centerX = (Screen.width - menuW) * 0.5f;
        float titleY = Screen.height * 0.22f;

        GUI.Label(new Rect(0, titleY, Screen.width, 90), "Sphere", titleStyle);

        float menuY = titleY + 110f;

        GUILayout.BeginArea(new Rect(centerX, menuY, menuW, Screen.height - menuY - 40));

        switch (state)
        {
            case MenuState.Main: DrawMain(); break;
            case MenuState.NewGame: DrawNewGame(); break;
            case MenuState.Settings: DrawSettings(); break;
            case MenuState.Video: DrawVideo(); break;
            case MenuState.Audio: DrawAudio(); break;
            case MenuState.Controls: DrawControls(); break;
            case MenuState.AI: DrawAI(); break;
            case MenuState.FirstRun: DrawFirstRun(); break;
            case MenuState.ModelSelect: DrawModelSelect(); break;
            case MenuState.Paused: DrawPaused(); break;
            case MenuState.SaveGame: DrawSaveGame(); break;
            case MenuState.LoadGame: DrawLoadGame(); break;
        }

        GUILayout.EndArea();
    }

    void DrawMain()
    {
        if (GUILayout.Button("New Game", menuButtonStyle))
            state = MenuState.NewGame;

        GUILayout.Space(4);

        if (SaveSystem.ListSaves().Count > 0)
        {
            if (GUILayout.Button("Load Game", menuButtonStyle))
            {
                returnState = MenuState.Main;
                state = MenuState.LoadGame;
            }
            GUILayout.Space(4);
        }

        if (GUILayout.Button("Settings", menuButtonStyle))
        {
            returnState = MenuState.Main;
            state = MenuState.Settings;
        }

        GUILayout.Space(4);

        if (GUILayout.Button("Exit Game", menuButtonStyle))
            QuitToDesktop();
    }

    void DrawPaused()
    {
        if (GUILayout.Button("Resume", menuButtonStyle))
        {
            Close();
            return;
        }

        GUILayout.Space(4);

        if (GUILayout.Button("Save Game", menuButtonStyle))
        {
            returnState = MenuState.Paused;
            if (string.IsNullOrEmpty(saveName)) saveName = DefaultSaveName();
            state = MenuState.SaveGame;
        }

        GUILayout.Space(4);

        if (GUILayout.Button("Load Game", menuButtonStyle))
        {
            returnState = MenuState.Paused;
            state = MenuState.LoadGame;
        }

        GUILayout.Space(4);

        if (GUILayout.Button("Settings", menuButtonStyle))
        {
            returnState = MenuState.Paused;
            state = MenuState.Settings;
        }

        GUILayout.Space(4);

        if (GUILayout.Button("Quit to Main Menu", menuButtonStyle))
        {
            var bootstrap = FindFirstObjectByType<GameBootstrap>();
            if (bootstrap != null) bootstrap.UnloadWorld();
            gameActive = false;
            Time.timeScale = 0f;
            state = MenuState.Main;
        }

        GUILayout.Space(4);

        if (GUILayout.Button("Quit to Desktop", menuButtonStyle))
            QuitToDesktop();
    }

    void DrawSaveGame()
    {
        GUILayout.Label("Save Game", new GUIStyle(sliderLabelStyle) { fontSize = 20, fontStyle = FontStyle.Bold });
        GUILayout.Space(8);

        GUILayout.Label("Save name", sliderLabelStyle);
        saveName = GUILayout.TextField(saveName, new GUIStyle(GUI.skin.textField)
        {
            fontSize = 16,
            fixedHeight = 34,
            alignment = TextAnchor.MiddleLeft
        });

        GUILayout.Space(8);

        if (GUILayout.Button("Save", menuButtonStyle))
        {
            string slot = Sanitize(saveName);
            if (!string.IsNullOrEmpty(slot))
            {
                bool ok = SaveSystem.Save(slot);
                ShowMessage(ok ? $"Saved \"{slot}\"" : "Save failed");
            }
        }

        // Existing saves (tap to overwrite the name)
        var saves = SaveSystem.ListSaves();
        if (saves.Count > 0)
        {
            GUILayout.Space(10);
            GUILayout.Label("Existing saves (tap to overwrite):", infoStyle);
            saveScroll = GUILayout.BeginScrollView(saveScroll, GUILayout.Height(140));
            foreach (var s in saves)
                if (GUILayout.Button(s, backButtonStyle))
                    saveName = s;
            GUILayout.EndScrollView();
        }

        DrawSaveMessage();

        GUILayout.Space(10);
        if (GUILayout.Button("Back", backButtonStyle))
            state = returnState;
    }

    void DrawLoadGame()
    {
        GUILayout.Label("Load Game", new GUIStyle(sliderLabelStyle) { fontSize = 20, fontStyle = FontStyle.Bold });
        GUILayout.Space(8);

        if (DrawGeneratingProgress()) return;

        var saves = SaveSystem.ListSaves();
        if (saves.Count == 0)
        {
            GUILayout.Label("No saved worlds found.", infoStyle);
        }
        else
        {
            saveScroll = GUILayout.BeginScrollView(saveScroll, GUILayout.Height(220));
            foreach (var s in saves)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(s, menuButtonStyle))
                {
                    var data = SaveSystem.LoadData(s);
                    if (data != null)
                    {
                        var bootstrap = FindFirstObjectByType<GameBootstrap>();
                        if (bootstrap != null)
                        {
                            generatingBootstrap = bootstrap;
                            generatingWorld = true;
                            bootstrap.LoadGame(data);
                            GUILayout.EndHorizontal();
                            GUILayout.EndScrollView();
                            return;
                        }
                    }
                    ShowMessage("Load failed");
                }
                if (GUILayout.Button("X", backButtonStyle, GUILayout.Width(36)))
                {
                    SaveSystem.Delete(s);
                    ShowMessage($"Deleted \"{s}\"");
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        DrawSaveMessage();

        GUILayout.Space(10);
        if (GUILayout.Button("Back", backButtonStyle))
            state = returnState;
    }

    /// <summary>Draws the "generating world" status label + bar, and closes the menu into the game
    /// once GameBootstrap reports it's done. Returns true while generation is in progress, so a
    /// caller can skip drawing its normal controls that frame.</summary>
    bool DrawGeneratingProgress()
    {
        if (!generatingWorld) return false;

        if (generatingBootstrap == null || !generatingBootstrap.IsGenerating)
        {
            generatingWorld = false;
            gameActive = true;
            Close();
            return true;
        }

        GUILayout.Space(8);
        GUILayout.Label(generatingBootstrap.GenerationStatus, sliderLabelStyle);
        GUILayout.Space(4);

        var rect = GUILayoutUtility.GetRect(280, 18);
        GUI.color = new Color(0.12f, 0.12f, 0.12f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = new Color(0.4f, 0.75f, 0.95f);
        float fill = Mathf.Clamp01(generatingBootstrap.GenerationProgress01);
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * fill, rect.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        return true;
    }

    void DrawSaveMessage()
    {
        if (!string.IsNullOrEmpty(saveMessage) && Time.unscaledTime < saveMessageUntil)
        {
            GUILayout.Space(6);
            GUILayout.Label(saveMessage, new GUIStyle(sliderLabelStyle) { normal = { textColor = new Color(0.5f, 0.9f, 0.6f) } });
        }
    }

    void ShowMessage(string msg)
    {
        saveMessage = msg;
        saveMessageUntil = Time.unscaledTime + 3f;
    }

    string DefaultSaveName()
    {
        var bootstrap = FindFirstObjectByType<GameBootstrap>();
        int s = bootstrap != null ? bootstrap.Seed : seed;
        return $"sphere-{s}";
    }

    static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(c, '-');
        return name.Trim();
    }

    void QuitToDesktop()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void DrawNewGame()
    {
        GUI.enabled = !generatingWorld;

        GUILayout.Label("Seed", sliderLabelStyle);
        string seedStr = GUILayout.TextField(seed.ToString(), new GUIStyle(GUI.skin.textField)
        {
            fontSize = 18,
            fixedHeight = 36,
            alignment = TextAnchor.MiddleCenter
        });
        if (int.TryParse(seedStr, out int parsed))
            seed = parsed;

        GUILayout.Space(4);

        if (GUILayout.Button("Randomize Seed", backButtonStyle))
            seed = Random.Range(0, 99999);

        GUILayout.Space(12);

        GUILayout.Label($"World Radius: {worldRadius:F0}", sliderLabelStyle);
        worldRadius = GUILayout.HorizontalSlider(worldRadius, 20f, 1000f);

        string sizeLabel = worldRadius < 40f ? "(small)" : worldRadius < 80f ? "(medium)" : worldRadius < 160f ? "(large)" : worldRadius < 300f ? "(massive)" : worldRadius < 600f ? "(colossal)" : "(titanic)";
        GUILayout.Label($"  {sizeLabel}", sliderLabelStyle);

        GUILayout.Space(12);

        GUILayout.Label($"Starting Creatures: {creatureCount}", sliderLabelStyle);
        creatureCount = Mathf.RoundToInt(GUILayout.HorizontalSlider(creatureCount, 4f, 500f));

        GUILayout.Space(12);

        if (GUILayout.Button("Generate Planet", menuButtonStyle))
        {
            StartGame();
        }

        GUI.enabled = true;
        if (DrawGeneratingProgress()) return;

        GUILayout.Space(20);

        if (GUILayout.Button("Back", backButtonStyle))
            state = MenuState.Main;
    }

    void DrawSettings()
    {
        if (GUILayout.Button("Video", menuButtonStyle))
            state = MenuState.Video;

        GUILayout.Space(4);

        if (GUILayout.Button("Audio", menuButtonStyle))
            state = MenuState.Audio;

        GUILayout.Space(4);

        if (GUILayout.Button("Controls", menuButtonStyle))
            state = MenuState.Controls;

        GUILayout.Space(4);

        if (GUILayout.Button("AI / Model", menuButtonStyle))
        {
            state = MenuState.AI;
            if (OllamaClient.Instance != null)
                OllamaClient.Instance.RefreshModelList();
        }

        GUILayout.Space(20);

        if (GUILayout.Button("Back", backButtonStyle))
            state = returnState;
    }

    void DrawVideo()
    {
        string[] qualityNames = QualitySettings.names;
        GUILayout.Label($"Quality: {qualityNames[qualityLevel]}", sliderLabelStyle);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("<", backButtonStyle, GUILayout.Width(40)))
            qualityLevel = Mathf.Max(0, qualityLevel - 1);
        if (GUILayout.Button(">", backButtonStyle, GUILayout.Width(40)))
            qualityLevel = Mathf.Min(qualityNames.Length - 1, qualityLevel + 1);
        GUILayout.EndHorizontal();

        GUILayout.Space(8);

        bool newFullscreen = GUILayout.Toggle(fullscreen, "  Fullscreen", new GUIStyle(GUI.skin.toggle)
        {
            fontSize = 16,
            normal = { textColor = new Color(0.8f, 0.8f, 0.8f) }
        });
        if (newFullscreen != fullscreen)
        {
            fullscreen = newFullscreen;
            Screen.fullScreen = fullscreen;
        }

        GUILayout.Space(8);

        if (GUILayout.Button("Apply", backButtonStyle))
            QualitySettings.SetQualityLevel(qualityLevel);

        GUILayout.Space(20);

        if (GUILayout.Button("Back", backButtonStyle))
            state = MenuState.Settings;
    }

    void DrawAudio()
    {
        GUILayout.Label($"Master Volume: {masterVolume:P0}", sliderLabelStyle);
        masterVolume = GUILayout.HorizontalSlider(masterVolume, 0f, 1f);
        AudioListener.volume = masterVolume;

        GUILayout.Space(12);

        GUILayout.Label($"Music: {musicVolume:P0}", sliderLabelStyle);
        musicVolume = GUILayout.HorizontalSlider(musicVolume, 0f, 1f);

        GUILayout.Space(12);

        GUILayout.Label($"SFX: {sfxVolume:P0}", sliderLabelStyle);
        sfxVolume = GUILayout.HorizontalSlider(sfxVolume, 0f, 1f);

        GUILayout.Space(20);

        if (GUILayout.Button("Back", backButtonStyle))
            state = MenuState.Settings;
    }

    void DrawControls()
    {
        GUILayout.Label($"Mouse Sensitivity: {mouseSensitivity:F2}", sliderLabelStyle);
        mouseSensitivity = GUILayout.HorizontalSlider(mouseSensitivity, 0.05f, 1f);

        GUILayout.Space(12);

        GUILayout.Label("RMB Drag — Orbit Camera", sliderLabelStyle);
        GUILayout.Label("Scroll — Zoom", sliderLabelStyle);
        GUILayout.Label("1-7 — Arm a God Power, then Click to cast", sliderLabelStyle);
        GUILayout.Label("ESC — Cancel Power / Menu", sliderLabelStyle);

        GUILayout.Space(20);

        if (GUILayout.Button("Back", backButtonStyle))
            state = MenuState.Settings;
    }

    void DrawFirstRun()
    {
        GUILayout.Label("Welcome to Sphere", new GUIStyle(sliderLabelStyle) { fontSize = 24, fontStyle = FontStyle.Bold });
        GUILayout.Space(8);

        GUILayout.Label(
            "Each creature in Sphere is driven by an AI model that runs entirely on your own computer — " +
            "nothing is sent over the internet. You choose which model to use, so you can match it to your hardware.",
            infoStyle);

        GUILayout.Space(12);
        GUILayout.Label($"Your GPU: {GameConfig.GpuInfo()}", sliderLabelStyle);
        GUILayout.Label($"Recommended model: {GameConfig.GpuRecommendation()}", sliderLabelStyle);

        GUILayout.Space(12);
        GUILayout.Label(
            "You'll need a model file in GGUF format. Download one from Hugging Face — search for a " +
            "\"Qwen2.5\" or \"Llama 3.2\" GGUF model and pick a Q4_K_M file sized for your GPU above. " +
            "Then point Sphere to it below.",
            infoStyle);

        GUILayout.Space(16);

        if (GUILayout.Button("Select Model File (.gguf)", menuButtonStyle))
        {
            modelSelectReturn = MenuState.Main;
            if (modelBrowser == null) modelBrowser = new ModelFileBrowser();
            state = MenuState.ModelSelect;
        }

        GUILayout.Space(6);

        if (GUILayout.Button("Skip for now (creatures run on basic AI)", backButtonStyle))
        {
            var cfg = GameConfig.Instance;
            cfg.firstRunComplete = true;
            cfg.Save();
            state = MenuState.Main;
        }
    }

    void DrawModelSelect()
    {
        GUILayout.Label("Select a Model File", new GUIStyle(sliderLabelStyle) { fontSize = 20, fontStyle = FontStyle.Bold });
        GUILayout.Label($"GPU: {GameConfig.GpuInfo()}   —   Recommended: {GameConfig.GpuRecommendation()}", infoStyle);
        GUILayout.Space(8);

        if (modelBrowser == null) modelBrowser = new ModelFileBrowser();
        modelBrowser.Draw(sliderLabelStyle, backButtonStyle, infoStyle);

        GUILayout.Space(8);
        string sel = modelBrowser.SelectedFile;
        GUILayout.Label(string.IsNullOrEmpty(sel) ? "No file selected." : $"Selected: {Path.GetFileName(sel)}", sliderLabelStyle);

        GUILayout.Space(8);

        GUI.enabled = !string.IsNullOrEmpty(sel);
        if (GUILayout.Button("Use This Model", menuButtonStyle))
        {
            var cfg = GameConfig.Instance;
            cfg.backendMode = "gguf";
            cfg.modelPath = sel;
            cfg.firstRunComplete = true;
            cfg.Save();
            GUI.enabled = true;
            state = modelSelectReturn;
            return;
        }
        GUI.enabled = true;

        GUILayout.Space(6);
        if (GUILayout.Button("Back", backButtonStyle))
        {
            bool fromFirstRun = modelSelectReturn == MenuState.Main && !GameConfig.Instance.firstRunComplete;
            state = fromFirstRun ? MenuState.FirstRun : modelSelectReturn;
        }
    }

    void DrawAI()
    {
        var client = OllamaClient.Instance;

        GUILayout.Label($"Backend: {(client != null ? client.BackendName : "None")}", sliderLabelStyle);
        GUILayout.Label($"Current: {(client != null ? client.ModelName : "—")}", sliderLabelStyle);
        GUILayout.Label($"Status: {(client != null && client.IsReady ? "Connected" : "Not connected")}", sliderLabelStyle);

        GUILayout.Space(8);

        var gcfg = GameConfig.Instance;
        GUILayout.Label($"Model file: {(gcfg != null && gcfg.HasValidModel ? Path.GetFileName(gcfg.modelPath) : "none (basic AI)")}", infoStyle);
        if (GUILayout.Button("Change Model File...", backButtonStyle))
        {
            modelSelectReturn = MenuState.AI;
            if (modelBrowser == null) modelBrowser = new ModelFileBrowser();
            state = MenuState.ModelSelect;
        }
        GUILayout.Label("(applies the next time you generate a planet)", infoStyle);

        GUILayout.Space(8);
        GUILayout.Label("Available Models", sliderLabelStyle);

        if (client != null && client.AvailableModels.Count > 0)
        {
            modelScroll = GUILayout.BeginScrollView(modelScroll, GUILayout.Height(200));

            for (int i = 0; i < client.AvailableModels.Count; i++)
            {
                var m = client.AvailableModels[i];
                bool isCurrent = m.name == client.ModelName;
                bool isSelected = i == selectedModelIndex;

                string label = isCurrent ? $"► {m.name}  [{m.size}]" : $"   {m.name}  [{m.size}]";
                if (!string.IsNullOrEmpty(m.family))
                    label += $"  ({m.family})";
                if (!string.IsNullOrEmpty(m.quantization))
                    label += $"  {m.quantization}";

                var style = isCurrent || isSelected ? menuButtonStyle : backButtonStyle;
                if (GUILayout.Button(label, style))
                    selectedModelIndex = i;
            }

            GUILayout.EndScrollView();

            GUILayout.Space(4);

            if (selectedModelIndex >= 0 && selectedModelIndex < client.AvailableModels.Count)
            {
                var selected = client.AvailableModels[selectedModelIndex];
                if (selected.name != client.ModelName)
                {
                    if (GUILayout.Button($"Switch to {selected.name}", menuButtonStyle))
                    {
                        // Use .path, not .name — the local backend resolves by full file path,
                        // and for Ollama .path == the model tag, so this is correct for both.
                        client.SwitchModel(selected.path);
                        selectedModelIndex = -1;
                    }
                }
            }
        }
        else
        {
            GUILayout.Label("No models found. Pull models with:", sliderLabelStyle);
            GUILayout.Label("  ollama pull qwen2.5:0.5b", sliderLabelStyle);
            GUILayout.Label("  ollama pull qwen2.5:3b", sliderLabelStyle);
            GUILayout.Label("  ollama pull llama3.2:1b", sliderLabelStyle);

            GUILayout.Space(4);
            if (GUILayout.Button("Refresh", backButtonStyle))
                client?.RefreshModelList();
        }

        GUILayout.Space(8);

        GUILayout.Label("Tips:", sliderLabelStyle);
        GUILayout.Label("• Smaller models (0.5-1B) = faster, less VRAM", new GUIStyle(sliderLabelStyle) { fontSize = 13 });
        GUILayout.Label("• Larger models (3-7B) = smarter creatures", new GUIStyle(sliderLabelStyle) { fontSize = 13 });
        GUILayout.Label("• Drop .gguf files in Models/ for direct loading", new GUIStyle(sliderLabelStyle) { fontSize = 13 });

        GUILayout.Space(20);

        if (GUILayout.Button("Back", backButtonStyle))
            state = MenuState.Settings;
    }

    void StartGame()
    {
        var bootstrap = FindFirstObjectByType<GameBootstrap>();
        if (bootstrap == null) return;

        generatingBootstrap = bootstrap;
        generatingWorld = true;
        bootstrap.GenerateWorld(seed, worldRadius, creatureCount);
        // Stays open showing DrawGeneratingProgress() until GameBootstrap reports IsGenerating ==
        // false — gameActive/Close() happen there once the coroutine actually finishes, not here.
    }
}
