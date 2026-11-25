using System.Drawing;
using System.Drawing.Drawing2D;
using Peshtoq.Services;
using WF = System.Windows.Forms;

namespace Peshtoq;

/// <summary>Notification-area icon with the app's settings and Exit.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly WF.NotifyIcon _icon;
    private readonly Icon _image;

    public TrayIcon(Action onSettings, Action onExit)
    {
        _image = DrawIcon();

        var settings = new WF.ToolStripMenuItem("Sozlamalar…");
        settings.Click += (_, _) => onSettings();

        var exit = new WF.ToolStripMenuItem("Chiqish");
        exit.Click += (_, _) => onExit();

        var menu = new WF.ContextMenuStrip();
        menu.Items.Add(new WF.ToolStripLabel("Peshtoq 0.2") { ForeColor = Color.Gray });
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add(settings);
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add(exit);

        // Open settings on a left double-click too.
        _icon = new WF.NotifyIcon
        {
            Icon = _image,
            Text = "Peshtoq",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => onSettings();
    }

    /// <summary>A dark rounded square with a white pill: the island in miniature.</summary>
    private static Icon DrawIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var bg = new SolidBrush(Color.FromArgb(255, 22, 25, 32));
            using var fg = new SolidBrush(Color.White);
            FillRounded(g, bg, new RectangleF(1, 1, 30, 30), 8);
            FillRounded(g, fg, new RectangleF(7, 7, 18, 7), 3.5f);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    private static void FillRounded(Graphics g, Brush brush, RectangleF r, float radius)
    {
        using var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
    }
}
