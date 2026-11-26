using Velopack;

namespace Peshtoq;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run before anything else: Velopack launches the app with special arguments during
        // install / update / uninstall, handles them here, and exits. A normal launch returns and
        // continues to WPF below. See docs/updates.md.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
