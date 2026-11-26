using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Peshtoq.Native;
using Peshtoq.Services;
using Peshtoq.Services.Link;

namespace Peshtoq.Views;

public partial class SettingsWindow : Window
{
    private bool _loading;
    private readonly DispatcherTimer _linkRefresh = new() { Interval = TimeSpan.FromMilliseconds(1200) };

    public SettingsWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Load();
        Wire();
        _linkRefresh.Tick += (_, _) => RefreshLink();
        _linkRefresh.Start();
        RefreshLink();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // Dark title bar so the system chrome matches the window.
        var hwnd = new WindowInteropHelper(this).Handle;
        int on = 1;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
    }

    private void Load()
    {
        _loading = true;
        var s = Settings.Current;

        SideLeft.IsChecked = s.SideTabs;
        SideTop.IsChecked = !s.SideTabs;

        TriggerSlider.Value = s.TriggerWidth;
        TriggerValue.Text = $"{s.TriggerWidth:0} px";
        DwellSlider.Value = s.OpenDwellMs;
        DwellValue.Text = $"{s.OpenDwellMs} ms";

        foreach (RadioButton swatch in Swatches.Children)
            swatch.IsChecked = string.Equals((string)swatch.Tag, s.Accent, StringComparison.OrdinalIgnoreCase);

        TabMedia.IsChecked = s.ShowMedia;
        TabNotes.IsChecked = s.ShowNotes;
        TabClip.IsChecked = s.ShowClip;
        TabSys.IsChecked = s.ShowSys;

        StartupCheck.IsChecked = StartupManager.IsEnabled;
        LinkCheck.IsChecked = s.LinkEnabled;
        _loading = false;
    }

    private void Wire()
    {
        SideLeft.Checked += (_, _) => Edit(s => s.SideTabs = true);
        SideTop.Checked += (_, _) => Edit(s => s.SideTabs = false);

        TriggerSlider.ValueChanged += (_, _) =>
        {
            TriggerValue.Text = $"{TriggerSlider.Value:0} px";
            Edit(s => s.TriggerWidth = TriggerSlider.Value);
        };
        DwellSlider.ValueChanged += (_, _) =>
        {
            DwellValue.Text = $"{DwellSlider.Value:0} ms";
            Edit(s => s.OpenDwellMs = (int)DwellSlider.Value);
        };

        foreach (RadioButton swatch in Swatches.Children)
            swatch.Checked += (sender, _) => Edit(s => s.Accent = (string)((RadioButton)sender).Tag);

        TabMedia.Checked += (_, _) => Edit(s => s.ShowMedia = true);
        TabMedia.Unchecked += (_, _) => TryDisableTab(TabMedia, s => s.ShowMedia = false);
        TabNotes.Checked += (_, _) => Edit(s => s.ShowNotes = true);
        TabNotes.Unchecked += (_, _) => TryDisableTab(TabNotes, s => s.ShowNotes = false);
        TabClip.Checked += (_, _) => Edit(s => s.ShowClip = true);
        TabClip.Unchecked += (_, _) => TryDisableTab(TabClip, s => s.ShowClip = false);
        TabSys.Checked += (_, _) => Edit(s => s.ShowSys = true);
        TabSys.Unchecked += (_, _) => TryDisableTab(TabSys, s => s.ShowSys = false);

        StartupCheck.Checked += (_, _) => { if (!_loading) StartupManager.Set(true); };
        StartupCheck.Unchecked += (_, _) => { if (!_loading) StartupManager.Set(false); };

        LinkCheck.Checked += (_, _) => { Edit(s => s.LinkEnabled = true); RefreshLink(); };
        LinkCheck.Unchecked += (_, _) => { Edit(s => s.LinkEnabled = false); RefreshLink(); };
    }

    private static App? AppHost => Application.Current as App;

    private void RefreshLink()
    {
        var link = AppHost?.Link;
        if (link is null)
        {
            LinkStatus.Text = "O'chirilgan. Yoqilganda bu qurilma LANda ko'rinadi va juftlanadi.";
            PeerList.ItemsSource = null;
            PairedList.ItemsSource = null;
            PeersHeader.Visibility = PairedHeader.Visibility = Visibility.Collapsed;
            return;
        }

        PeersHeader.Visibility = PairedHeader.Visibility = Visibility.Visible;
        LinkStatus.Text = $"{StateLabel(link.State)}  ·  {link.LastStatus}";

        var peers = link.Peers.OrderBy(p => p.Host).ToList();
        PeerList.ItemsSource = peers.Count == 0
            ? new[] { Muted("— hali qurilma topilmadi") }
            : peers.Select(p => Row($"{p.Host}  ({p.Address})", "Ulanish", () => _ = link.ConnectAsync(p))).ToArray();

        var paired = link.Paired;
        PairedList.ItemsSource = paired.Count == 0
            ? new[] { Muted("— juftlangan qurilma yo'q") }
            : paired.Select(d => Row(d.Name, "O'chirish", () => { link.Unpair(d.SpkiHash); RefreshLink(); })).ToArray();
    }

    private static string StateLabel(LinkState s) => s switch
    {
        LinkState.Listening => "Tayyor",
        LinkState.Handshaking => "Ulanmoqda…",
        LinkState.Pairing => "Juftlanmoqda…",
        LinkState.Connected => "Ulangan",
        LinkState.Disconnected => "Uzilgan",
        _ => "—",
    };

    private UIElement Muted(string text) => new TextBlock
    {
        Text = text,
        Foreground = (Brush)FindResource("MutedBrush"),
        Margin = new Thickness(0, 2, 0, 2),
    };

    private UIElement Row(string text, string button, Action onClick)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 3, 0, 3), LastChildFill = true };
        var btn = new Button
        {
            Content = button,
            Padding = new Thickness(12, 4, 12, 4),
            Background = (Brush)FindResource("AccentBrush"),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        btn.Click += (_, _) => onClick();
        DockPanel.SetDock(btn, Dock.Right);
        dock.Children.Add(btn);
        dock.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = (Brush)FindResource("FgBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        return dock;
    }

    private void Edit(Action<Settings> change)
    {
        if (_loading) return;
        change(Settings.Current);
        Settings.NotifyChanged();
    }

    /// <summary>Allow disabling a tab only while at least one stays on; otherwise re-check it.</summary>
    private void TryDisableTab(CheckBox box, Action<Settings> disable)
    {
        if (_loading) return;
        var snapshot = Settings.Current;
        disable(snapshot);
        if (snapshot.EnabledTabCount == 0)
        {
            // undo: a panel with no tabs is not allowed
            _loading = true;
            box.IsChecked = true;
            _loading = false;
            SetTabFlag(box, true);
            return;
        }
        Settings.NotifyChanged();
    }

    private static void SetTabFlag(CheckBox box, bool on)
    {
        var s = Settings.Current;
        switch (box.Name)
        {
            case "TabMedia": s.ShowMedia = on; break;
            case "TabNotes": s.ShowNotes = on; break;
            case "TabClip": s.ShowClip = on; break;
            case "TabSys": s.ShowSys = on; break;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _linkRefresh.Stop();
        Settings.Save();
        base.OnClosed(e);
    }
}
