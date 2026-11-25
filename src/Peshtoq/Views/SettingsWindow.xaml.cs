using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Peshtoq.Native;
using Peshtoq.Services;

namespace Peshtoq.Views;

public partial class SettingsWindow : Window
{
    private bool _loading;

    public SettingsWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Load();
        Wire();
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
        Settings.Save();
        base.OnClosed(e);
    }
}
