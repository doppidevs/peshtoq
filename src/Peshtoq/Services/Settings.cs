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

    private static string FilePath => Path.Combine(AppPaths.DataDir, "settings.json");

    public static Settings Current { get; private set; } = new();

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
