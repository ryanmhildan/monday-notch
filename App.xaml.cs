using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using MondayNotch.Services;

namespace MondayNotch;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private bool _ownsMutex;
    private MainWindow? _window;
    private TrayIcon? _tray;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\MondayNotch.SingleInstance", out _ownsMutex);
        if (!_ownsMutex)
        {
            // Already running: the existing notch stays, this copy quits.
            Shutdown();
            return;
        }

        ThemeManager.Apply(AppSettings.Theme);

        // Developer aid: `MondayNotch.exe --snapshot out.png [--no-media]` renders every state to images and exits.
        int snapshotIndex = Array.IndexOf(e.Args, "--snapshot");
        bool snapshot = snapshotIndex >= 0 && snapshotIndex + 1 < e.Args.Length;

        // `--no-media` keeps the music card empty, so screenshots don't include whatever happens to be playing.
        _window = new MainWindow(showGreeting: !snapshot, followMedia: Array.IndexOf(e.Args, "--no-media") < 0);
        _window.Show();

        if (snapshot)
        {
            await _window.SaveSnapshotsAsync(e.Args[snapshotIndex + 1]);
            Shutdown();
            return;
        }

        _tray = new TrayIcon(_window, Shutdown);
    }

    /// <summary>Starts a fresh copy of the app and closes this one (Settings > Advanced > Restart notch).</summary>
    public void Restart()
    {
        // Let go of the single-instance lock first, or the new copy would think we are still running.
        ReleaseSingleInstance();
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false });
        }
        catch (Exception)
        {
        }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _window?.DisposeServices();
        ReleaseSingleInstance();
        base.OnExit(e);
    }

    private void ReleaseSingleInstance()
    {
        if (_ownsMutex)
            _singleInstance?.ReleaseMutex();
        _ownsMutex = false;
        _singleInstance?.Dispose();
        _singleInstance = null;
    }
}
