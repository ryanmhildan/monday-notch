using System;
using System.Drawing;
using System.IO;
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

    /// <summary>The app icon (assets/icon.ico, embedded as a resource) at the size the tray asks for.</summary>
    private static Icon CreateIcon()
    {
        using Stream stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/assets/icon.ico")).Stream;
        return new Icon(stream, SystemInformation.SmallIconSize);
    }
}
