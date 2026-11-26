using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using static Peshtoq.Native.NativeMethods;

namespace Peshtoq.Services;

public sealed partial class ClipItem : INotifyPropertyChanged
{
    public ClipItem(string text)
    {
        Text = text;
        At = DateTime.Now;
        var trimmed = text.Trim();
        if (HexColor().IsMatch(trimmed))
        {
            Kind = "RANG";
            try { Swatch = (Brush)new BrushConverter().ConvertFromString(trimmed)!; } catch { }
        }
        else if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
            Kind = "HAVOLA";
        else
            Kind = "MATN";
    }

    public string Text { get; }
    public DateTime At { get; }
    public string Kind { get; }
    public Brush? Swatch { get; }
    public bool HasSwatch => Swatch is not null;

    /// <summary>Single line for the card.</summary>
    public string Preview => Regex.Replace(Text.Trim(), @"\s+", " ");

    public string Meta
    {
        get
        {
            var ago = DateTime.Now - At;
            string when = ago.TotalSeconds < 60 ? "hozir"
                : ago.TotalMinutes < 60 ? $"{(int)ago.TotalMinutes} daq"
                : $"{(int)ago.TotalHours} soat";
            return $"{Kind} · {when}";
        }
    }

    public void RefreshMeta() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Meta)));

    public event PropertyChangedEventHandler? PropertyChanged;

    [GeneratedRegex(@"^#([0-9a-fA-F]{6}|[0-9a-fA-F]{8}|[0-9a-fA-F]{3})$")]
    private static partial Regex HexColor();
}

/// <summary>
/// Keeps the last 20 text items copied to the clipboard. History is kept in memory only
/// and is gone when the app exits, so passwords copied by accident are not written to disk.
/// </summary>
public sealed class ClipboardService : IDisposable
{
    private const int MaxItems = 20;
    private HwndSource? _source;
    private bool _ignoreNext;
    private string? _remoteApplyOnce; // text just applied from a peer: show it, but don't re-broadcast

    public ObservableCollection<ClipItem> Items { get; } = new();

    /// <summary>Raised when text is copied locally (not when a peer's text is applied). Used by Link.</summary>
    public event Action<string>? TextCopied;

    public void Start()
    {
        var p = new HwndSourceParameters("Peshtoq.Clipboard")
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE: an invisible message-only window
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        };
        _source = new HwndSource(p);
        _source.AddHook(Hook);
        AddClipboardFormatListener(_source.Handle);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE) OnClipboardChanged();
        return IntPtr.Zero;
    }

    private void OnClipboardChanged()
    {
        if (_ignoreNext) { _ignoreNext = false; return; }

        string? text = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Clipboard.ContainsText()) text = Clipboard.GetText();
                break;
            }
            catch (COMException)
            {
                Thread.Sleep(30); // another app still holds the clipboard
            }
        }
        if (string.IsNullOrWhiteSpace(text)) return;
        if (text.Length > 4000) text = text[..4000];

        var existing = Items.FirstOrDefault(i => i.Text == text);
        if (existing is not null) Items.Remove(existing);
        Items.Insert(0, new ClipItem(text));
        while (Items.Count > MaxItems) Items.RemoveAt(Items.Count - 1);

        if (_remoteApplyOnce == text)
            _remoteApplyOnce = null; // came from a peer — shown above, but do not echo it back
        else
            TextCopied?.Invoke(text); // locally copied — let Link forward it
    }

    /// <summary>
    /// Place a peer's clipboard text onto this machine's clipboard and into the history, without
    /// forwarding it back. Must run on the UI thread (the clipboard is STA-bound).
    /// </summary>
    public void ApplyRemote(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (text.Length > 4000) text = text[..4000];
        try
        {
            _remoteApplyOnce = text;
            _ignoreNext = false; // let OnClipboardChanged run so the item appears in the list
            Clipboard.SetText(text);
        }
        catch (COMException)
        {
            _remoteApplyOnce = null;
        }
    }

    public bool Copy(ClipItem item)
    {
        try
        {
            _ignoreNext = true;
            Clipboard.SetText(item.Text);
            return true;
        }
        catch (COMException)
        {
            _ignoreNext = false;
            return false;
        }
    }

    public void Dispose()
    {
        if (_source is null) return;
        RemoveClipboardFormatListener(_source.Handle);
        _source.Dispose();
        _source = null;
    }
}
