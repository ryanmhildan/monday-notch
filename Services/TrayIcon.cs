using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MondayNotch.Services;

/// <summary>Icon next to the taskbar clock with the app's few switches.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _image;

    public TrayIcon(MainWindow notch, Action exit)
    {
        _image = CreateIcon();

        var title = new ToolStripMenuItem("Monday Notch") { Enabled = false };

        var show = new ToolStripMenuItem("Show notch") { Checked = true, CheckOnClick = true };
        show.CheckedChanged += (_, _) =>
            notch.Visibility = show.Checked ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;

        var lyrics = new ToolStripMenuItem("Show lyrics") { CheckOnClick = true };
        lyrics.Click += (_, _) => notch.LyricsVisible = lyrics.Checked;

        var startup = new ToolStripMenuItem("Start with Windows") { Checked = StartupManager.IsEnabled, CheckOnClick = true };
        startup.CheckedChanged += (_, _) =>
        {
            try
            {
                StartupManager.SetEnabled(startup.Checked);
            }
            catch (Exception)
            {
                startup.Checked = StartupManager.IsEnabled;
            }
        };

        var settings = new ToolStripMenuItem("Settings…");
        settings.Click += (_, _) => notch.OpenSettings();

        var quit = new ToolStripMenuItem("Exit");
        quit.Click += (_, _) => exit();

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[] { title, new ToolStripSeparator(), show, lyrics, startup, settings, new ToolStripSeparator(), quit });
        // Lyrics can also be switched from the notch itself, so read the state each time the menu opens.
        menu.Opening += (_, _) => lyrics.Checked = notch.LyricsVisible;

        _icon = new NotifyIcon
        {
            Icon = _image,
            Text = "Monday Notch",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => show.Checked = !show.Checked;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _image.Dispose();
    }

    /// <summary>A white pill with two little eyes, drawn at runtime so no .ico file is needed.</summary>
    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var pill = new GraphicsPath();
            var bounds = new RectangleF(2, 9, 28, 14);
            float d = bounds.Height;
            pill.AddArc(bounds.X, bounds.Y, d, d, 90, 180);
            pill.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 180);
            pill.CloseFigure();
            g.FillPath(Brushes.White, pill);

            g.FillEllipse(Brushes.Black, 10, 13, 4, 6);
            g.FillEllipse(Brushes.Black, 18, 13, 4, 6);
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using Icon temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
