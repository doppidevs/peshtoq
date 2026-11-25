using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Peshtoq.Services;
using static Peshtoq.Native.NativeMethods;

namespace Peshtoq.Views;

public partial class IslandWindow : Window
{
    private enum Tab { Media, Notes, Clip, Sys }

    private sealed record TabDef(Tab Id, string Label, string Glyph, double Width);

    private static readonly TabDef[] Defs =
    {
        new(Tab.Media, "Media", "", 500),
        new(Tab.Notes, "Eslatmalar", "", 540),
        new(Tab.Clip, "Clipboard", "", 620),
        new(Tab.Sys, "Tizim", "", 600),
    };

    // geometry and timing (see docs/design.md)
    private const double PillWidth = 230, PillHeight = 26;
    private const double MinHeight_ = 170, MaxHeight_ = 420;
    private const double SideExtraWidth = 80, SideRailWidth = 136;
    private const double TriggerHalfWidth = 150;
    private const int TriggerHeight = 2; // physical px from the top edge that count as the trigger
    // Dwell keeps a stray sweep across the top from opening; the grace bridges a single missed poll
    // during a fast flick, so the panel still opens when the cursor jumps straight to the edge.
    private const int OpenDwellMs = 90, OpenGraceMs = 60, CloseDelayMs = 350, WheelThrottleMs = 250;

    private readonly Dictionary<Tab, RadioButton> _tabButtons = new();
    private readonly Dictionary<Tab, TextBlock> _tabIcons = new();
    private readonly Dictionary<Tab, ScrollViewer> _panes;
    private readonly DispatcherTimer _poll = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(30) };
    private readonly DispatcherTimer _second = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private readonly MediaService _media;
    private readonly ClipboardService _clipboard = new();
    private readonly NotesStore _notes = new();
    private readonly SystemInfo _system = new();

    private IntPtr _hwnd;
    private Tab _current = Tab.Media;
    private bool _open, _side;
    private long _zoneSince = -1, _zoneExitAt = -1, _leftSince = -1, _lastWheel;
    private int _seconds;

    public IslandWindow()
    {
        InitializeComponent();
        _media = new MediaService(Dispatcher);
        _panes = new()
        {
            [Tab.Media] = MediaPane,
            [Tab.Notes] = NotesPane,
            [Tab.Clip] = ClipPane,
            [Tab.Sys] = SysPane,
        };
        foreach (var pane in _panes.Values) pane.RenderTransform = new TranslateTransform();

        BuildTabs();
        SetSideTabs(Settings.Current.SideTabs);

        _notes.Load();
        NotesList.ItemsSource = _notes.Items;
        ClipList.ItemsSource = _clipboard.Items;
        _clipboard.Items.CollectionChanged += (_, _) => OnClipboardItemsChanged();
        OnClipboardItemsChanged();

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        MakeToolWindow(_hwnd, noActivate: false); // notes need keyboard focus
        PositionWindow();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(PositionWindow);
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(PositionWindow);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _poll.Tick += (_, _) => Poll();
        _poll.Start();
        _second.Tick += (_, _) => OnSecond();
        _second.Start();

        _clipboard.Start();
        _media.Changed += UpdateMedia;
        UpdateMedia();
        UpdateSystem();
        await _media.InitAsync();
    }

    /// <summary>Center the window at the top of the primary screen, in physical pixels.</summary>
    private void PositionWindow()
    {
        if (_hwnd == IntPtr.Zero) return;
        double scale = GetDpiForWindow(_hwnd) / 96.0;
        int w = (int)Math.Round(760 * scale), h = (int)Math.Round(470 * scale);
        int screenWidth = GetSystemMetrics(SM_CXSCREEN);
        SetWindowPos(_hwnd, HWND_TOPMOST, (screenWidth - w) / 2, 0, w, h, SWP_NOACTIVATE);
    }

    // ---------------------------------------------------------------- tabs

    private void BuildTabs()
    {
        foreach (var def in Defs)
        {
            var icon = new TextBlock
            {
                Text = def.Glyph,
                FontFamily = (FontFamily)FindResource("IconFont"),
                FontSize = 14,
                Margin = new Thickness(0, 0, 9, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var label = new TextBlock { Text = def.Label, VerticalAlignment = VerticalAlignment.Center };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(icon);
            content.Children.Add(label);

            var button = new RadioButton
            {
                Style = (Style)FindResource("TabButton"),
                GroupName = "IslandTabs",
                Content = content,
                IsChecked = def.Id == _current,
            };
            var id = def.Id;
            button.Checked += (_, _) => { if (_current != id) SetTab(id); };
            _tabButtons[id] = button;
            _tabIcons[id] = icon;
            Tabs.Children.Add(button);
        }
    }

    public void SetSideTabs(bool side)
    {
        _side = side;
        if (side)
        {
            Grid.SetRow(TabsHost, 0); Grid.SetRowSpan(TabsHost, 2);
            Grid.SetColumn(TabsHost, 0); Grid.SetColumnSpan(TabsHost, 1);
            Grid.SetRow(Panes, 0); Grid.SetRowSpan(Panes, 2);
            Grid.SetColumn(Panes, 1); Grid.SetColumnSpan(Panes, 1);
            TabsHost.Width = SideRailWidth;
            TabsHost.BorderThickness = new Thickness(0, 0, 1, 0);
            TabsHost.Padding = new Thickness(0, 0, 12, 0);
            Tabs.Orientation = Orientation.Vertical;
            Tabs.HorizontalAlignment = HorizontalAlignment.Stretch;
            Panes.Margin = new Thickness(16, 0, 0, 0);
            Full.Margin = new Thickness(12, 14, 16, 14);
        }
        else
        {
            Grid.SetRow(TabsHost, 0); Grid.SetRowSpan(TabsHost, 1);
            Grid.SetColumn(TabsHost, 0); Grid.SetColumnSpan(TabsHost, 2);
            Grid.SetRow(Panes, 1); Grid.SetRowSpan(Panes, 1);
            Grid.SetColumn(Panes, 0); Grid.SetColumnSpan(Panes, 2);
            TabsHost.Width = double.NaN;
            TabsHost.BorderThickness = new Thickness(0);
            TabsHost.Padding = new Thickness(0);
            Tabs.Orientation = Orientation.Horizontal;
            Tabs.HorizontalAlignment = HorizontalAlignment.Center;
            Panes.Margin = new Thickness(0, 12, 0, 0);
            Full.Margin = new Thickness(16, 14, 16, 16);
        }

        foreach (var (id, button) in _tabButtons)
        {
            button.Tag = new CornerRadius(side ? 10 : 14);
            button.Padding = side ? new Thickness(10, 8, 10, 8) : new Thickness(12, 5, 12, 5);
            button.Margin = side ? new Thickness(0, 0, 0, 3) : new Thickness(2, 0, 2, 0);
            button.HorizontalContentAlignment = side ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            _tabIcons[id].Visibility = side ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_open) ApplySize();
    }

    private void SetTab(Tab tab)
    {
        _current = tab;
        _tabButtons[tab].IsChecked = true;
        foreach (var (id, pane) in _panes)
            pane.Visibility = id == tab ? Visibility.Visible : Visibility.Collapsed;

        var current = _panes[tab];
        current.ScrollToTop();
        var shift = (TranslateTransform)current.RenderTransform;
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 };
        var dur = TimeSpan.FromMilliseconds(320);
        current.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur));
        shift.BeginAnimation(_side ? TranslateTransform.XProperty : TranslateTransform.YProperty,
            new DoubleAnimation(_side ? 8 : 6, 0, dur) { EasingFunction = ease });

        if (tab == Tab.Sys) UpdateSystem();
        if (_open) ApplySize();
    }

    // ---------------------------------------------------------------- open / close

    private void Poll()
    {
        if (!IsVisible || _hwnd == IntPtr.Zero) return;
        GetCursorPos(out var p);
        double scale = GetDpiForWindow(_hwnd) / 96.0;
        int screenWidth = GetSystemMetrics(SM_CXSCREEN);

        bool inZone = p.Y <= TriggerHeight * scale && Math.Abs(p.X - screenWidth / 2.0) <= TriggerHalfWidth * scale;
        Rect shell = ShellScreenRect();
        shell.Inflate(6 * scale, 6 * scale);
        bool overShell = shell.Contains(p.X, p.Y);
        long now = _clock.ElapsedMilliseconds;

        if (!_open)
        {
            if (inZone || overShell)
            {
                _zoneExitAt = -1;
                if (_zoneSince < 0) _zoneSince = now;
                if (now - _zoneSince >= OpenDwellMs) Expand();
            }
            else
            {
                // Don't drop the dwell the instant a fast flick's cursor is sampled mid-air;
                // only give up after a short grace out of the zone.
                if (_zoneExitAt < 0) _zoneExitAt = now;
                if (now - _zoneExitAt >= OpenGraceMs) _zoneSince = -1;
            }
        }
        else
        {
            if (overShell || inZone) _leftSince = -1;
            else if (_leftSince < 0) _leftSince = now;
            else if (now - _leftSince >= CloseDelayMs) Collapse();
        }
    }

    private Rect ShellScreenRect()
    {
        try
        {
            var topLeft = Shell.PointToScreen(new Point(0, 0));
            var bottomRight = Shell.PointToScreen(new Point(Shell.ActualWidth, Shell.ActualHeight));
            return new Rect(topLeft, bottomRight);
        }
        catch (InvalidOperationException)
        {
            return Rect.Empty;
        }
    }

    public void Expand()
    {
        if (_open) return;
        _open = true;
        _zoneSince = _zoneExitAt = _leftSince = -1;

        Shell.CornerRadius = new CornerRadius(22);
        Mini.IsHitTestVisible = false;
        Full.IsHitTestVisible = true;

        Mini.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(100)));
        Full.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(250)) { BeginTime = TimeSpan.FromMilliseconds(160) });
        FullShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(400))
        {
            BeginTime = TimeSpan.FromMilliseconds(120),
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 },
        });
        Shadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, new DoubleAnimation(0.55, TimeSpan.FromMilliseconds(300)));

        if (_current == Tab.Sys) UpdateSystem();
        UpdateProgress();
        ApplySize();
    }

    public void Collapse()
    {
        if (!_open) return;
        _open = false;
        _zoneSince = _zoneExitAt = _leftSince = -1;

        Full.IsHitTestVisible = false;
        Mini.IsHitTestVisible = true;
        Full.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(100)));
        Mini.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)) { BeginTime = TimeSpan.FromMilliseconds(120) });
        Shadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));

        var width = AnimateSize(PillWidth, PillHeight, opening: false);
        width.Completed += (_, _) => { if (!_open) Shell.CornerRadius = new CornerRadius(13); };
        Keyboard.ClearFocus();
    }

    /// <summary>Size the panel to what the current tab needs, clamped to [170, 420] px high.</summary>
    private void ApplySize()
    {
        var def = Defs.First(d => d.Id == _current);
        double width = Math.Min(def.Width + (_side ? SideExtraWidth : 0), Width - 24);

        double contentWidth = _side
            ? width - Full.Margin.Left - Full.Margin.Right - SideRailWidth - Panes.Margin.Left - 2
            : width - Full.Margin.Left - Full.Margin.Right - 2;

        var content = (FrameworkElement)_panes[_current].Content;
        content.Measure(new Size(Math.Max(contentWidth, 50), double.PositiveInfinity));
        double contentHeight = content.DesiredSize.Height;

        TabsHost.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double tabsHeight = TabsHost.DesiredSize.Height;

        double raw = _side
            ? Full.Margin.Top + Full.Margin.Bottom + Math.Max(contentHeight, tabsHeight)
            : Full.Margin.Top + tabsHeight + Panes.Margin.Top + contentHeight + Full.Margin.Bottom;
        double height = Math.Clamp(Math.Ceiling(raw) + 2, MinHeight_, MaxHeight_);

        AnimateSize(width, height, opening: true);
    }

    private DoubleAnimation AnimateSize(double width, double height, bool opening)
    {
        var duration = TimeSpan.FromMilliseconds(opening ? 460 : 240);
        // Gentler overshoot than the content springs: a big size jump (e.g. Media -> Tizim) must not
        // overshoot enough to clip its own content for a frame.
        IEasingFunction ease = opening
            ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.12 }
            : new CubicEase { EasingMode = EasingMode.EaseIn };
        var w = new DoubleAnimation(width, duration) { EasingFunction = ease };
        var h = new DoubleAnimation(height, duration) { EasingFunction = ease };
        Shell.BeginAnimation(WidthProperty, w);
        Shell.BeginAnimation(HeightProperty, h);
        return w;
    }

    private void Shell_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_open) Expand();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Collapse();
    }

    /// <summary>Wheel switches tabs, unless the content under the cursor can still scroll that way.</summary>
    private void Shell_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_open) return;
        var pane = _panes[_current];
        bool down = e.Delta < 0;
        if (pane.IsMouseOver && pane.ScrollableHeight > 0)
        {
            bool canScroll = down ? pane.VerticalOffset < pane.ScrollableHeight - 0.5 : pane.VerticalOffset > 0.5;
            if (canScroll) return;
        }

        e.Handled = true;
        long now = _clock.ElapsedMilliseconds;
        if (now - _lastWheel < WheelThrottleMs) return;
        _lastWheel = now;

        int i = Array.FindIndex(Defs, d => d.Id == _current);
        int next = Math.Clamp(i + (down ? 1 : -1), 0, Defs.Length - 1);
        if (next != i) SetTab(Defs[next].Id);
    }

    // ---------------------------------------------------------------- periodic

    private void OnSecond()
    {
        _seconds++;
        UpdateProgress();
        if (_open && _current == Tab.Sys) UpdateSystem();
        if (_seconds % 30 == 0) foreach (var item in _clipboard.Items) item.RefreshMeta();
        if (_seconds % 3 == 0 && IsVisible && _hwnd != IntPtr.Zero) KeepTopmost(_hwnd);
    }

    // ---------------------------------------------------------------- media

    private void UpdateMedia()
    {
        bool has = _media.HasSession && !string.IsNullOrEmpty(_media.Title);
        MediaHas.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        MediaEmpty.Visibility = has ? Visibility.Collapsed : Visibility.Visible;

        MediaTitle.Text = _media.Title;
        MediaArtist.Text = string.IsNullOrEmpty(_media.Artist) ? _media.Source : $"{_media.Artist} · {_media.Source}";

        Brush cover = _media.Cover is { } bmp
            ? new ImageBrush(bmp) { Stretch = Stretch.UniformToFill }
            : (Brush)FindResource("CoverFallback");
        Cover.Background = cover;
        MiniCover.Background = cover;

        MiniTitle.Text = has
            ? (string.IsNullOrEmpty(_media.Artist) ? _media.Title : $"{_media.Title} · {_media.Artist}")
            : "Peshtoq";
        MiniTitle.Foreground = has ? (Brush)FindResource("FgBrush") : (Brush)FindResource("MutedBrush");
        MiniCover.Visibility = has ? Visibility.Visible : Visibility.Collapsed;

        PlayIcon.Text = _media.IsPlaying ? "" : "";
        SourceText.Text = _media.Source;
        SourceButton.Visibility = _media.SessionCount > 1 ? Visibility.Visible : Visibility.Collapsed;

        SetEqualizer(has && _media.IsPlaying);
        UpdateProgress();
        if (_open && _current == Tab.Media) ApplySize();
    }

    private void SetEqualizer(bool playing)
    {
        Eq.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        int i = 0;
        foreach (Rectangle bar in Eq.Children)
        {
            if (playing)
            {
                var anim = new DoubleAnimation(3, 12, TimeSpan.FromMilliseconds(500))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromMilliseconds(new[] { 0, 300, 150, 450 }[i]),
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                bar.BeginAnimation(HeightProperty, anim);
            }
            else
            {
                bar.BeginAnimation(HeightProperty, null);
                bar.Height = 3;
            }
            i++;
        }
    }

    private void UpdateProgress()
    {
        var (pos, dur) = _media.Timeline();
        bool known = dur > TimeSpan.Zero;
        ProgressTrackHost.Visibility = known ? Visibility.Visible : Visibility.Collapsed;
        TimesRow.Visibility = ProgressTrackHost.Visibility;
        TimeCurrent.Text = known ? Format(pos) : "";
        TimeTotal.Text = known ? Format(dur) : "";
        ProgressFill.Width = known && ProgressTrack.ActualWidth > 0
            ? ProgressTrack.ActualWidth * (pos.TotalSeconds / dur.TotalSeconds)
            : 0;
    }

    private static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    private void PlayPause_Click(object sender, RoutedEventArgs e) => _media.TogglePlayPause();
    private void Next_Click(object sender, RoutedEventArgs e) => _media.Next();
    private void Prev_Click(object sender, RoutedEventArgs e) => _media.Previous();
    private void Source_Click(object sender, RoutedEventArgs e) => _media.CycleSource();

    private void Progress_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ProgressTrack.ActualWidth <= 0) return;
        _media.Seek(e.GetPosition(ProgressTrack).X / ProgressTrack.ActualWidth);
        e.Handled = true;
    }

    // ---------------------------------------------------------------- notes

    private void NewNote_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(NewNote.Text)) return;
        _notes.Add(NewNote.Text);
        NewNote.Clear();
        e.Handled = true;
        Dispatcher.BeginInvoke(ApplySize, DispatcherPriority.Loaded);
    }

    private void NewNote_TextChanged(object sender, TextChangedEventArgs e) =>
        NewNotePlaceholder.Visibility = NewNote.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void DeleteNote_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is NoteItem item)
        {
            _notes.Remove(item);
            Dispatcher.BeginInvoke(ApplySize, DispatcherPriority.Loaded);
        }
    }

    // ---------------------------------------------------------------- clipboard

    private void OnClipboardItemsChanged()
    {
        ClipEmpty.Visibility = _clipboard.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_open && _current == Tab.Clip) Dispatcher.BeginInvoke(ApplySize, DispatcherPriority.Loaded);
    }

    private void ClipItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ClipItem item) return;
        bool ok = _clipboard.Copy(item);
        ClipToast.Text = ok ? "Nusxalandi" : "Clipboard band, qayta urinib ko'ring";
        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1300))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1600))));
        ClipToast.BeginAnimation(OpacityProperty, fade);
    }

    // ---------------------------------------------------------------- system

    private void UpdateSystem()
    {
        double cpu = _system.Cpu();
        CpuText.Text = $"{cpu:0}%";
        SetBar(CpuBar, cpu / 100);

        var ram = _system.Ram();
        RamText.Text = $"{ram.UsedGb:0.0} / {ram.TotalGb:0} GB";
        SetBar(RamBar, ram.Percent / 100);

        var batt = _system.Battery();
        BattText.Text = batt.HasBattery ? $"{batt.Percent}%" + (batt.Charging ? " ⚡" : "") : "Tarmoqdan";
        SetBar(BattBar, batt.HasBattery ? batt.Percent / 100.0 : 1);

        var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
        UptimeText.Text = $"Kompyuter {(int)up.TotalDays} kun {up.Hours} soat {up.Minutes} daqiqadan beri yoniq";
    }

    private static void SetBar(Border bar, double fraction)
    {
        var scale = (ScaleTransform)bar.RenderTransform;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(Math.Clamp(fraction, 0, 1), TimeSpan.FromMilliseconds(600))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.2 },
        });
    }

    public void Shutdown()
    {
        _poll.Stop();
        _second.Stop();
        _clipboard.Dispose();
    }
}
