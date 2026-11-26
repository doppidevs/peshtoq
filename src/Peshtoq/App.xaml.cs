using System.IO;
using System.Windows;
using Peshtoq.Services;
using Peshtoq.Services.Link;
using Peshtoq.Views;

namespace Peshtoq;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private HeaderWindow? _header;
    private IslandWindow? _island;
    private TrayIcon? _tray;
    private SettingsWindow? _settings;

    private IdentityService? _identity;
    private PairedStore? _paired;
    private LinkService? _link;
    private InputController? _input;

    private readonly UpdateService _update = new();
    private Velopack.UpdateInfo? _pendingUpdate;

    public UpdateService Update => _update;
    public Velopack.UpdateInfo? PendingUpdate => _pendingUpdate;

    /// <summary>The live Link service, or null when the feature is off. Read by SettingsWindow.</summary>
    public LinkService? Link => _link;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Opt-in loopback verification of Peshtoq Link; bypasses the single-instance mutex so it
        // can run while the real app is open. See Services/Link/LinkSelfTest.cs.
        if (e.Args.Contains("--link-selftest"))
        {
            var logPath = Path.Combine(AppPaths.DataDir, "link-selftest.log");
            // Run off the UI thread: awaits inside must not try to resume on a thread we are blocking.
            try { Task.Run(() => Services.Link.LinkSelfTest.RunAsync(logPath)).GetAwaiter().GetResult(); }
            catch (Exception ex) { LogError(ex); }
            Shutdown();
            return;
        }

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

        _tray = new TrayIcon(onSettings: ShowSettings, onExit: Shutdown);

        Settings.Changed += OnSettingsChanged;
        if (Settings.Current.LinkEnabled) StartLink();

        _ = CheckForUpdatesAsync(announce: true);
    }

    /// <summary>Background update check + download. On success a restart applies it (never mid-use).</summary>
    public async Task<string> CheckForUpdatesAsync(bool announce)
    {
        try
        {
            if (!_update.IsInstalled) return "portable";
            var info = await _update.CheckAsync();
            if (info is null) return "uptodate";
            await _update.DownloadAsync(info);
            _pendingUpdate = info;
            if (announce)
                _ = Dispatcher.InvokeAsync(() => _tray?.ShowToast("Peshtoq",
                    $"Yangi versiya tayyor: {info.TargetFullRelease.Version}. Sozlamalardan qayta ishga tushiring."));
            return "ready";
        }
        catch { return "error"; }
    }

    public void ApplyPendingUpdate()
    {
        if (_pendingUpdate is not null) _update.ApplyAndRestart(_pendingUpdate);
    }

    private void OnSettingsChanged()
    {
        if (Settings.Current.LinkEnabled) StartLink();
        else StopLink();
    }

    private void StartLink()
    {
        if (_link is not null) return;
        _identity ??= new IdentityService();
        _paired ??= new PairedStore();
        _paired.Load();
        _link = new LinkService(_identity, _paired) { ConfirmPairing = PromptPairing };
        _link.ClipTextReceived += OnRemoteClipReceived;
        if (_island is not null) _island.Clipboard.TextCopied += OnLocalClipCopied;

        Services.Link.LinkLog.Enabled = true; // diagnostic: writes %AppData%\Peshtoq\link.log
        Services.Link.LinkLog.Write("=== Link started ===");
        _input = new InputController(_link);
        _input.ControllingChanged += _ => UpdateLinkIndicator();
        _input.BeingControlledChanged += _ => UpdateLinkIndicator();
        _input.Toast += msg => Dispatcher.InvokeAsync(() => _tray?.ShowToast("Peshtoq Link", msg));

        _link.Start();
    }

    private void StopLink()
    {
        if (_island is not null) _island.Clipboard.TextCopied -= OnLocalClipCopied;
        if (_link is not null) _link.ClipTextReceived -= OnRemoteClipReceived;
        _input?.Dispose();
        _input = null;
        _link?.Dispose();
        _link = null;
        UpdateLinkIndicator();
    }

    private void UpdateLinkIndicator() => Dispatcher.InvokeAsync(() =>
        _header?.SetLinkIndicator(
            _input is null ? null :
            _input.Controlling ? "→ Link: boshqaryapsiz" :
            _input.BeingControlled ? "⇄ Link: boshqarilyapti" : null));

    // Clipboard sync: local copy -> peer; peer's text -> local clipboard + history (on the UI thread).
    private void OnLocalClipCopied(string text) => _link?.SendClipText(text);
    private void OnRemoteClipReceived(string text) =>
        Dispatcher.InvokeAsync(() => _island?.Clipboard.ApplyRemote(text));

    /// <summary>Show the SAS dialog on the UI thread and report whether the user confirmed a match.</summary>
    private Task<bool> PromptPairing(PairingPrompt prompt) =>
        Dispatcher.InvokeAsync(() => new PairingDialog(prompt).ShowDialog() == true).Task;

    private void ShowSettings()
    {
        if (_settings is null)
        {
            _settings = new SettingsWindow();
            _settings.Closed += (_, _) => _settings = null;
            _settings.Show();
        }
        else
        {
            if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
            _settings.Activate();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        StopLink();
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
