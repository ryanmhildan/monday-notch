using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using MondayNotch.Services;
using Windows.Devices.Radios;

namespace MondayNotch.Pages;

/// <summary>Quick switches (Wi-Fi, Bluetooth, hotspot, power mode), session actions, brightness and volume.</summary>
public partial class ControlCenterPage : UserControl
{
    private static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(4);

    private readonly DispatcherTimer _brightnessThrottle = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _confirmTimer = new() { Interval = ConfirmWindow };
    private bool _updatingSliders;
    private double _pendingBrightness;
    private Button? _armed; // restart/shut down button waiting for its confirming second click
    private PowerMode _powerMode = PowerMode.Balanced;

    /// <summary>Asks the notch to close, e.g. before a screenshot or locking the PC.</summary>
    public event Action? CollapseRequested;

    public ControlCenterPage()
    {
        InitializeComponent();

        // WMI calls take tens of milliseconds, so brightness is applied at most every 120 ms while dragging.
        _brightnessThrottle.Tick += (_, _) =>
        {
            _brightnessThrottle.Stop();
            double level = _pendingBrightness;
            Task.Run(() => SystemControls.SetBrightness(level));
        };
        _confirmTimer.Tick += (_, _) => Disarm();
    }

    /// <summary>Reads the current state of everything. Call when the page comes into view.</summary>
    public async void Refresh()
    {
        RefreshVolume();

        double? brightness = await Task.Run(SystemControls.GetBrightness);
        _updatingSliders = true;
        BrightnessSlider.IsEnabled = brightness is not null;
        BrightnessSlider.Value = brightness ?? 0;
        BrightnessSlider.ToolTip = brightness is null ? "This display doesn't support brightness control" : null;
        BrightnessText.Text = brightness is null ? "–" : Percent(brightness.Value);
        _updatingSliders = false;

        PowerMode? mode = SystemControls.GetPowerMode();
        PowerTile.IsEnabled = mode is not null;
        _powerMode = mode ?? PowerMode.Balanced;
        ShowPowerMode();

        await RefreshTogglesAsync();
    }

    /// <summary>Volume can change from the keyboard at any time, so the notch re-reads it every second while open.</summary>
    public void RefreshVolume()
    {
        double? volume = AudioVolume.Get();
        _updatingSliders = true;
        VolumeSlider.IsEnabled = volume is not null;
        VolumeSlider.Value = volume ?? 0;
        VolumeText.Text = volume is null ? "–" : Percent(volume.Value);
        VolumeGlyph.Text = volume is null or 0 ? "" : "";
        _updatingSliders = false;
    }

    private async Task RefreshTogglesAsync()
    {
        bool? wifi = await SystemControls.GetRadioAsync(RadioKind.WiFi);
        WifiTile.IsChecked = wifi == true;
        string? network = wifi == true ? SystemControls.ConnectedWifiName() : null;
        WifiLabel.Text = network ?? "Wi-Fi";
        WifiTile.ToolTip = network is null ? "Wi-Fi" : $"Connected to {network}";

        bool? bluetooth = await SystemControls.GetRadioAsync(RadioKind.Bluetooth);
        BluetoothTile.IsChecked = bluetooth == true;
        BluetoothTile.IsEnabled = bluetooth is not null;

        HotspotTile.IsChecked = SystemControls.GetHotspot() == true;
    }

    // ---- Switches --------------------------------------------------------------------------

    private async void WifiTile_Click(object sender, RoutedEventArgs e) =>
        await ToggleRadio(RadioKind.WiFi, WifiTile, "ms-settings:network-wifi");

    private async void BluetoothTile_Click(object sender, RoutedEventArgs e) =>
        await ToggleRadio(RadioKind.Bluetooth, BluetoothTile, "ms-settings:bluetooth");

    private async Task ToggleRadio(RadioKind kind, ToggleButton tile, string settingsPage)
    {
        bool wanted = tile.IsChecked == true;
        if (!await SystemControls.SetRadioAsync(kind, wanted))
        {
            // Windows refused (policy, no radio, or access denied): hand over to the Settings app.
            SystemControls.OpenUri(settingsPage);
            CollapseRequested?.Invoke();
        }
        await RefreshTogglesAsync();
    }

    private async void HotspotTile_Click(object sender, RoutedEventArgs e)
    {
        bool wanted = HotspotTile.IsChecked == true;
        ShowStatus(wanted ? "Starting hotspot…" : "Stopping hotspot…");
        if (!await SystemControls.SetHotspotAsync(wanted))
        {
            SystemControls.OpenUri("ms-settings:network-mobilehotspot");
            CollapseRequested?.Invoke();
        }
        ShowStatus("");
        await RefreshTogglesAsync();
    }

    private void PowerTile_Click(object sender, RoutedEventArgs e)
    {
        PowerMode next = _powerMode switch
        {
            PowerMode.BestEfficiency => PowerMode.Balanced,
            PowerMode.Balanced => PowerMode.BestPerformance,
            _ => PowerMode.BestEfficiency,
        };
        if (SystemControls.SetPowerMode(next))
            _powerMode = SystemControls.GetPowerMode() ?? next;
        ShowPowerMode();
    }

    private void ShowPowerMode()
    {
        PowerLabel.Text = _powerMode switch
        {
            PowerMode.BestEfficiency => "Efficiency",
            PowerMode.BestPerformance => "Performance",
            _ => "Balanced",
        };
        PowerTile.IsChecked = _powerMode != PowerMode.Balanced;
        PowerTile.ToolTip = "Power mode — click to switch";
    }

    private void WifiTile_RightClick(object sender, MouseButtonEventArgs e) => OpenSettings("ms-settings:network-wifi");
    private void BluetoothTile_RightClick(object sender, MouseButtonEventArgs e) => OpenSettings("ms-settings:bluetooth");
    private void HotspotTile_RightClick(object sender, MouseButtonEventArgs e) => OpenSettings("ms-settings:network-mobilehotspot");
    private void PowerTile_RightClick(object sender, MouseButtonEventArgs e) => OpenSettings("ms-settings:powersleep");

    private void OpenSettings(string uri)
    {
        SystemControls.OpenUri(uri);
        CollapseRequested?.Invoke();
    }

    // ---- Actions -----------------------------------------------------------------------------

    private void Lock_Click(object sender, RoutedEventArgs e)
    {
        CollapseRequested?.Invoke();
        SystemControls.Lock();
    }

    private void Sleep_Click(object sender, RoutedEventArgs e)
    {
        CollapseRequested?.Invoke();
        SystemControls.Sleep();
    }

    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (Confirm(RestartBtn, "Click again to restart"))
            SystemControls.Restart();
    }

    private void ShutDown_Click(object sender, RoutedEventArgs e)
    {
        if (Confirm(ShutDownBtn, "Click again to shut down"))
            SystemControls.ShutDown();
    }

    private async void Snip_Click(object sender, RoutedEventArgs e)
    {
        // Close first so the notch isn't in the screenshot.
        CollapseRequested?.Invoke();
        await Task.Delay(350);
        SystemControls.Snip();
    }

    /// <summary>First click arms the button (red, with a hint); a second click within a few seconds confirms.</summary>
    private bool Confirm(Button button, string hint)
    {
        if (_armed == button)
        {
            Disarm();
            return true;
        }
        Disarm();
        _armed = button;
        button.SetResourceReference(BackgroundProperty, "DangerBrush");
        ShowStatus(hint);
        _confirmTimer.Start();
        return false;
    }

    private void Disarm()
    {
        _confirmTimer.Stop();
        _armed?.ClearValue(BackgroundProperty);
        _armed = null;
        ShowStatus("");
    }

    private void ShowStatus(string text) => StatusText.Text = text;

    // ---- Sliders -----------------------------------------------------------------------------

    private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        BrightnessText.Text = Percent(e.NewValue);
        if (_updatingSliders)
            return;
        _pendingBrightness = e.NewValue;
        if (!_brightnessThrottle.IsEnabled)
            _brightnessThrottle.Start();
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        VolumeText.Text = Percent(e.NewValue);
        VolumeGlyph.Text = e.NewValue <= 0 ? "" : "";
        if (!_updatingSliders)
            AudioVolume.Set(e.NewValue);
    }

    private static string Percent(double level) => $"{Math.Round(level * 100)}%";
}
