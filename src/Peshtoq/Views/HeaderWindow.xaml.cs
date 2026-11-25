using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Peshtoq.Native;
using Peshtoq.Services;
using Windows.Networking.Connectivity;
using static Peshtoq.Native.NativeMethods;

namespace Peshtoq.Views;

public partial class HeaderWindow : Window
{
    public const double HeaderHeight = 34;

    private static readonly string[] Days = { "Yak", "Dush", "Sesh", "Chor", "Pay", "Jum", "Shan" };
    private static readonly string[] Months = { "yan", "fev", "mar", "apr", "may", "iyun", "iyul", "avg", "sen", "okt", "noy", "dek" };

    private readonly VolumeService _volume = new();
    private readonly SystemInfo _system = new();
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _foreground = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly Dictionary<uint, string> _appNames = new();
    private AppBar? _appBar;
    private IntPtr _hwnd;
    private int _ticks;

    public event Action<bool>? FullscreenChanged;

    public HeaderWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closing += (_, _) => _appBar?.Dispose(); // release the strip while the window handle is still alive
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        MakeToolWindow(_hwnd, noActivate: true);

        // Acrylic fills the whole header window, so let the blur show: a transparent border on success,
        // the solid fallback fill when the OS refuses it (older Windows).
        if (Acrylic.Enable(this, System.Windows.Media.Color.FromArgb(0xB0, 0x10, 0x12, 0x18), bottomBorder: false))
            RootBorder.Background = System.Windows.Media.Brushes.Transparent;

        _appBar = new AppBar(this, HeaderHeight);
        _appBar.FullscreenChanged += fs => FullscreenChanged?.Invoke(fs);
        _appBar.Register();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _tick.Tick += (_, _) => OnTick();
        _foreground.Tick += (_, _) => UpdateForeground();
        _tick.Start();
        _foreground.Start();
        NetworkInformation.NetworkStatusChanged += _ => Dispatcher.InvokeAsync(UpdateNetwork);

        OnTick();
        UpdateNetwork();
        UpdateForeground();
    }

    private void OnTick()
    {
        UpdateClock();
        UpdateVolume();
        if (_ticks % 5 == 0) UpdateBattery();
        if (_ticks % 15 == 0) UpdateNetwork();
        if (_ticks % 3 == 0 && IsVisible) KeepTopmost(_hwnd);
        _ticks++;
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockText.Text = $"{Days[(int)now.DayOfWeek]}, {now.Day}-{Months[now.Month - 1]}   {now:HH:mm}";
    }

    private void UpdateVolume()
    {
        var v = _volume.Get();
        if (v is null)
        {
            VolumeIcon.Text = "";
            VolumeButton.ToolTip = "Ovoz qurilmasi topilmadi";
            return;
        }
        var (pct, muted) = v.Value;
        VolumeIcon.Text = muted || pct == 0 ? "" : pct < 34 ? "" : pct < 67 ? "" : "";
        VolumeButton.ToolTip = muted ? $"Ovoz o'chirilgan ({pct}%)" : $"Ovoz: {pct}%  ·  g'ildirak bilan o'zgartiring";
    }

    private void UpdateBattery()
    {
        var b = _system.Battery();
        BatteryButton.Visibility = b.HasBattery ? Visibility.Visible : Visibility.Collapsed;
        if (!b.HasBattery) return;
        int step = Math.Clamp((int)Math.Round(b.Percent / 10.0), 0, 10);
        BatteryIcon.Text = char.ConvertFromUtf32((b.Charging ? 0xEBAB : 0xEBA0) + step);
        BatteryText.Text = $"{b.Percent}%";
        BatteryButton.ToolTip = b.Charging ? $"Quvvatlanmoqda · {b.Percent}%" : $"Batareya · {b.Percent}%";
    }

    private void UpdateNetwork()
    {
        // The WinRT networking calls can block briefly, so resolve them off the UI thread.
        Task.Run(SystemInfo.Network).ContinueWith(t =>
        {
            var n = t.Result;
            NetworkIcon.Text = n.Glyph;
            NetworkIcon.Opacity = n.Online ? 1 : 0.45;
            NetworkButton.ToolTip = n.Tooltip;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void UpdateForeground()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == Environment.ProcessId) return; // our own windows: keep showing the previous app

        var cls = new StringBuilder(256);
        GetClassName(hwnd, cls, cls.Capacity);
        string className = cls.ToString();
        if (className is "Progman" or "WorkerW")
        {
            SetActive("Ish stoli", "");
            return;
        }
        if (className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return;

        var title = new StringBuilder(512);
        GetWindowText(hwnd, title, title.Capacity);
        string windowTitle = title.ToString();
        string app = AppNameFor(pid);

        // UWP apps (Settings, Calculator) run inside ApplicationFrameHost; their title is the real name.
        if (app == "ApplicationFrameHost" || app == "Application Frame Host")
        {
            SetActive(windowTitle, "");
            return;
        }
        SetActive(app, windowTitle);
    }

    private void SetActive(string app, string title)
    {
        AppName.Text = app;
        bool redundant = string.IsNullOrWhiteSpace(title) || title == app || title.EndsWith(app, StringComparison.Ordinal) && title.Length <= app.Length + 3;
        // "Document - Microsoft Word": drop the trailing app name, it is already shown in bold
        if (!redundant && title.EndsWith(" - " + app, StringComparison.Ordinal)) title = title[..^(app.Length + 3)];
        if (!redundant && title.EndsWith(" — " + app, StringComparison.Ordinal)) title = title[..^(app.Length + 3)];
        WindowTitle.Text = redundant ? "" : title;
        WindowTitle.Visibility = redundant ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// The app's display name for a pid. Returns at once — a cached name, or the exe file name while a
    /// background task reads the file's description. Reading version info on the UI thread was what put a
    /// brief busy cursor over the header when the foreground app changed.
    /// </summary>
    private string AppNameFor(uint pid)
    {
        if (_appNames.TryGetValue(pid, out var cached)) return cached;

        string path = ProcessPath(pid);
        string immediate = path.Length == 0 ? "Ilova" : Path.GetFileNameWithoutExtension(path);
        if (_appNames.Count > 200) _appNames.Clear();
        _appNames[pid] = immediate;

        if (path.Length > 0)
        {
            Task.Run(() =>
            {
                try { return FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim(); }
                catch { return null; }
            }).ContinueWith(t =>
            {
                string nice = string.IsNullOrWhiteSpace(t.Result) ? immediate : t.Result!;
                if (nice == immediate) return;
                _appNames[pid] = nice;
                if (IsVisible) UpdateForeground(); // refresh the label if that app is still in front
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        return immediate;
    }

    private static string ProcessPath(uint pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return "";
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : "";
        }
        finally { CloseHandle(h); }
    }

    private static void Open(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { }
    }

    private void Launcher_Click(object sender, RoutedEventArgs e)
    {
        // Ctrl+Esc opens the Start menu reliably; a lone synthetic Win keypress is often ignored.
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        keybd_event(VK_ESCAPE, 0, 0, UIntPtr.Zero);
        keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private void ActionCenter_Click(object sender, RoutedEventArgs e) => Open("ms-actioncenter:");
    private void Network_Click(object sender, RoutedEventArgs e) => Open("ms-availablenetworks:");
    private void Battery_Click(object sender, RoutedEventArgs e) => Open("ms-settings:batterysaver");

    private void Volume_Click(object sender, RoutedEventArgs e)
    {
        _volume.ToggleMute();
        UpdateVolume();
    }

    private void Volume_Wheel(object sender, MouseWheelEventArgs e)
    {
        _volume.Change(e.Delta > 0 ? 2 : -2);
        UpdateVolume();
        e.Handled = true;
    }

    public void Shutdown()
    {
        _tick.Stop();
        _foreground.Stop();
        _appBar?.Dispose();
        _volume.Dispose();
    }
}
