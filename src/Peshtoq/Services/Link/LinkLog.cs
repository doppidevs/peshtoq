using System.IO;

namespace Peshtoq.Services.Link;

/// <summary>
/// Best-effort diagnostic log for the Link input relay, at %AppData%\Peshtoq\link.log.
/// Off unless Enabled is set (toggled from Settings), so normal runs write nothing.
/// </summary>
public static class LinkLog
{
    private static readonly object Gate = new();
    public static bool Enabled { get; set; }

    private static string Path => System.IO.Path.Combine(Peshtoq.Services.AppPaths.DataDir, "link.log");

    public static void Write(string msg)
    {
        if (!Enabled) return;
        try
        {
            lock (Gate)
                File.AppendAllText(Path, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
    }
}
