using System.Windows;
using System.Windows.Threading;

namespace Peshtoq;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow()
    {
        InitializeComponent();

        // TODO: register as AppBar (SHAppBarMessage) so maximized windows start below the header.
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;

        _clock.Tick += (_, _) => Clock.Text = DateTime.Now.ToString("HH:mm");
        _clock.Start();
        Clock.Text = DateTime.Now.ToString("HH:mm");
    }
}
