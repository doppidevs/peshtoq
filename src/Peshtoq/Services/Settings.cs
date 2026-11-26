using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace Peshtoq.Services;

internal static class AppPaths
{
    public static string DataDir
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Peshtoq");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}

internal sealed class Settings
{
    public bool SideTabs { get; set; } = true;

    /// <summary>Half-width (px) of the top-edge trigger zone, each side of centre.</summary>
    public double TriggerWidth { get; set; } = 150;

    /// <summary>How long the cursor must rest at the edge before the panel opens (ms).</summary>
    public int OpenDwellMs { get; set; } = 90;

    /// <summary>Accent colour as a hex string.</summary>
    public string Accent { get; set; } = "#5AA2FF";

    public bool ShowMedia { get; set; } = true;
    public bool ShowNotes { get; set; } = true;
    public bool ShowClip { get; set; } = true;
    public bool ShowSys { get; set; } = true;

    /// <summary>Peshtoq Link (cross-device control over the LAN). Off by default — opt-in.</summary>
    public bool LinkEnabled { get; set; }

    /// <summary>At least one tab must stay on, or the panel would be empty.</summary>
    public int EnabledTabCount => (ShowMedia ? 1 : 0) + (ShowNotes ? 1 : 0) + (ShowClip ? 1 : 0) + (ShowSys ? 1 : 0);

    private static string FilePath => Path.Combine(AppPaths.DataDir, "settings.json");

    public static Settings Current { get; private set; } = new();

    /// <summary>Raised when a value changes so open windows can apply it live.</summary>
    public static event Action? Changed;

    public static void NotifyChanged() => Changed?.Invoke();

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch
        {
            Current = new Settings();
        }
        if (Current.EnabledTabCount == 0) Current.ShowMedia = true; // never leave it with no tabs
    }

    public static void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(Current)); }
        catch { /* settings are a convenience; never crash over them */ }
    }
}

/// <summary>Start with Windows via the per-user Run key (no admin rights needed).</summary>
internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Peshtoq";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null) return;
        if (enabled && Environment.ProcessPath is { } exe)
            key.SetValue(ValueName, $"\"{exe}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
