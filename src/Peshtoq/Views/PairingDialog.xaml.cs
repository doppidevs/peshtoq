using System.Windows;
using System.Windows.Interop;
using Peshtoq.Native;
using Peshtoq.Services.Link;

namespace Peshtoq.Views;

public partial class PairingDialog : Window
{
    public PairingDialog(PairingPrompt prompt)
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Heading.Text = prompt.Incoming
            ? $"\"{prompt.PeerName}\" bu qurilmaga ulanmoqchi"
            : $"\"{prompt.PeerName}\" bilan juftlash";
        SasText.Text = prompt.Sas;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int on = 1;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void Reject_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
