using UnityEngine;
using UnityEngine.InputSystem;

public class MainMenu : MonoBehaviour
{
    public static MainMenu Instance { get; private set; }

    enum MenuState { Main, NewGame, Settings, Video, Audio, Controls }
    MenuState state = MenuState.Main;
    bool isOpen = true;

    [Header("New Game")]
    [SerializeField] int seed = 42;
    float worldRadius = 50f;

    GUIStyle titleStyle;
    GUIStyle menuButtonStyle;
    GUIStyle backButtonStyle;
    GUIStyle sliderLabelStyle;
    GUIStyle overlayStyle;
    Texture2D darkBg;
    Texture2D buttonNormal;
    Texture2D buttonHover;
    bool stylesInit;

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
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame && !isOpen)
        {
            isOpen = true;
            state = MenuState.Main;
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

        overlayStyle = new GUIStyle { normal = { background = darkBg } };
    }

    void OnGUI()
    {
        if (!isOpen) return;

        InitStyles();

        GUI.Box(new Rect(0, 0, Screen.width, Screen.height), GUIContent.none, overlayStyle);

        float menuW = 320f;
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
        }

        GUILayout.EndArea();
    }

    void DrawMain()
    {
        if (GUILayout.Button("New Game", menuButtonStyle))
            state = MenuState.NewGame;

        GUILayout.Space(4);

        if (GUILayout.Button("Settings", menuButtonStyle))
            state = MenuState.Settings;

        GUILayout.Space(4);

        if (GUILayout.Button("Exit Game", menuButtonStyle))
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }

    void DrawNewGame()
    {
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
        worldRadius = GUILayout.HorizontalSlider(worldRadius, 20f, 150f);

        string sizeLabel = worldRadius < 40f ? "(small)" : worldRadius < 80f ? "(medium)" : worldRadius < 120f ? "(large)" : "(massive)";
        GUILayout.Label($"  {sizeLabel}", sliderLabelStyle);

        GUILayout.Space(12);

        if (GUILayout.Button("Generate Planet", menuButtonStyle))
        {
            StartGame();
        }

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

        GUILayout.Space(20);

        if (GUILayout.Button("Back", backButtonStyle))
            state = MenuState.Main;
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
        GUILayout.Label("1 + Click — Smite", sliderLabelStyle);
        GUILayout.Label("2 + Click — Bless", sliderLabelStyle);
        GUILayout.Label("3 + Click — Feed", sliderLabelStyle);
        GUILayout.Label("ESC — Menu", sliderLabelStyle);

        GUILayout.Space(20);

        if (GUILayout.Button("Back", backButtonStyle))
            state = MenuState.Settings;
    }

    void StartGame()
    {
        var bootstrap = FindFirstObjectByType<GameBootstrap>();
        if (bootstrap != null)
            bootstrap.GenerateWorld(seed, worldRadius);

        Close();
    }
}
