using System;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Self-contained IMGUI file browser for picking a .gguf model file.
/// No native plugins — pure System.IO + GUILayout, works on every platform.
/// </summary>
public class ModelFileBrowser
{
    string currentDir;
    Vector2 scroll;
    string selectedFile;

    public string SelectedFile => selectedFile;

    public ModelFileBrowser()
    {
        currentDir = GetStartDir();
    }

    static string GetStartDir()
    {
        try
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home) && Directory.Exists(home)) return home;
        }
        catch { }
        try { return Directory.GetCurrentDirectory(); } catch { return "/"; }
    }

    public void Draw(GUIStyle pathStyle, GUIStyle buttonStyle, GUIStyle infoStyle)
    {
        GUILayout.Label($"Folder: {Truncate(currentDir, 70)}", infoStyle);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("↑ Up", buttonStyle, GUILayout.Width(70)))
            GoUp();
        foreach (var drive in GetDrives())
            if (GUILayout.Button(drive, buttonStyle, GUILayout.Width(50)))
                Navigate(drive);
        GUILayout.EndHorizontal();

        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(230));

        foreach (var dir in SafeDirectories(currentDir))
        {
            string name = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(name)) name = dir;
            if (GUILayout.Button($"📁  {name}", buttonStyle))
                Navigate(dir);
        }

        var files = SafeFiles(currentDir, "*.gguf");
        foreach (var file in files)
        {
            string name = Path.GetFileName(file);
            string label = $"{(file == selectedFile ? "► " : "    ")}{name}   ({FormatBytes(SafeSize(file))})";
            if (GUILayout.Button(label, buttonStyle))
                selectedFile = file;
        }

        if (!SafeDirectories(currentDir).Any() && files.Length == 0)
            GUILayout.Label("  (no sub-folders or .gguf files here)", infoStyle);

        GUILayout.EndScrollView();
    }

    void GoUp()
    {
        try
        {
            var parent = Directory.GetParent(currentDir);
            if (parent != null) Navigate(parent.FullName);
        }
        catch { }
    }

    void Navigate(string dir)
    {
        currentDir = dir;
        scroll = Vector2.zero;
    }

    static string[] GetDrives()
    {
        try { return DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.Name).ToArray(); }
        catch { return Array.Empty<string>(); }
    }

    static string[] SafeDirectories(string dir)
    {
        try { return Directory.GetDirectories(dir).OrderBy(d => d).ToArray(); }
        catch { return Array.Empty<string>(); }
    }

    static string[] SafeFiles(string dir, string pattern)
    {
        try { return Directory.GetFiles(dir, pattern).OrderBy(f => f).ToArray(); }
        catch { return Array.Empty<string>(); }
    }

    static long SafeSize(string file)
    {
        try { return new FileInfo(file).Length; } catch { return 0; }
    }

    static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L) return $"{bytes / (1024f * 1024f * 1024f):F1} GB";
        if (bytes >= 1024L * 1024L) return $"{bytes / (1024f * 1024f):F0} MB";
        return $"{bytes / 1024f:F0} KB";
    }

    static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
        return "..." + s.Substring(s.Length - (max - 3));
    }
}
