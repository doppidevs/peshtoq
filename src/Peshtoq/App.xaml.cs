using System.IO;
using System.Windows;
using Peshtoq.Services;
using Peshtoq.Views;

namespace Peshtoq;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private HeaderWindow? _header;
    private IslandWindow? _island;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(true, "Peshtoq.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            LogError(args.Exception);
            args.Handled = true; // keep running: a crash would leave the reserved AppBar strip behind
        };

        Settings.Load();

        _header = new HeaderWindow();
        _header.Closed += (_, _) => Shutdown(); // e.g. taskkill without /F: exit cleanly and free the AppBar strip
        _header.Show();

        _island = new IslandWindow { Owner = _header };
        _island.Show();

        _header.FullscreenChanged += fullscreen =>
        {
            if (fullscreen) { _island.Collapse(); _island.Hide(); _header.Hide(); }
            else { _header.Show(); _island.Show(); }
        };

        _tray = new TrayIcon(
            sideTabs: Settings.Current.SideTabs,
            onSideTabsChanged: side =>
            {
                Settings.Current.SideTabs = side;
                Settings.Save();
                _island.SetSideTabs(side);
            },
            onExit: Shutdown);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _island?.Shutdown();
        _header?.Shutdown();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private static void LogError(Exception ex)
    {
        try
        {
            File.AppendAllText(Path.Combine(AppPaths.DataDir, "error.log"), $"[{DateTime.Now:O}] {ex}\n\n");
        }
        catch { }
    }
}
