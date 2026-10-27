using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Devices.Radios;
using Windows.Networking.Connectivity;
using Windows.Networking.NetworkOperators;

namespace MondayNotch.Services;

public enum PowerMode
{
    BestEfficiency,
    Balanced,
    BestPerformance,
}

/// <summary>
/// Thin wrappers over the Windows features the Control Center switches. Every call can fail on some
/// machines (no Bluetooth radio, hotspot blocked by policy, external monitor without brightness control),
/// so they report failure instead of throwing, and callers fall back to opening the matching Settings page.
/// </summary>
public static class SystemControls
{
    // ---- Wi-Fi and Bluetooth radios ---------------------------------------------------------

    /// <summary>On/off, or null when this PC has no such radio or access to it is denied.</summary>
    public static async Task<bool?> GetRadioAsync(RadioKind kind)
    {
        Radio? radio = await FindRadioAsync(kind);
        return radio is null ? null : radio.State == RadioState.On;
    }

    /// <returns>True when the radio was switched.</returns>
    public static async Task<bool> SetRadioAsync(RadioKind kind, bool on)
    {
        Radio? radio = await FindRadioAsync(kind);
        if (radio is null)
            return false;
        try
        {
            return await radio.SetStateAsync(on ? RadioState.On : RadioState.Off) == RadioAccessStatus.Allowed;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task<Radio?> FindRadioAsync(RadioKind kind)
    {
        try
        {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed)
                return null;
            return (await Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == kind);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Name of the Wi-Fi network the PC is connected to, if any.</summary>
    public static string? ConnectedWifiName()
    {
        try
        {
            ConnectionProfile? profile = NetworkInformation.GetInternetConnectionProfile();
            return profile is { IsWlanConnectionProfile: true } ? profile.WlanConnectionProfileDetails.GetConnectedSsid() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ---- Mobile hotspot ---------------------------------------------------------------------

    /// <summary>On/off, or null when the current connection cannot be shared.</summary>
    public static bool? GetHotspot()
    {
        try
        {
            NetworkOperatorTetheringManager? manager = HotspotManager();
            return manager is null ? null : manager.TetheringOperationalState == TetheringOperationalState.On;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static async Task<bool> SetHotspotAsync(bool on)
    {
        try
        {
            NetworkOperatorTetheringManager? manager = HotspotManager();
            if (manager is null)
                return false;
            NetworkOperatorTetheringOperationResult result = on
                ? await manager.StartTetheringAsync()
                : await manager.StopTetheringAsync();
            return result.Status == TetheringOperationStatus.Success;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static NetworkOperatorTetheringManager? HotspotManager()
    {
        ConnectionProfile? profile = NetworkInformation.GetInternetConnectionProfile();
        return profile is null ? null : NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile);
    }

    // ---- Power mode (Settings > System > Power > Power mode) ---------------------------------

    private static readonly Guid BestEfficiencyOverlay = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    private static readonly Guid BalancedOverlay = Guid.Empty;
    private static readonly Guid BestPerformanceOverlay = new("ded574b5-45a0-4f42-8737-46345c09c238");

    public static PowerMode? GetPowerMode()
    {
        try
        {
            if (PowerGetEffectiveOverlayScheme(out Guid overlay) != 0)
                return null;
            if (overlay == BestEfficiencyOverlay)
                return PowerMode.BestEfficiency;
            if (overlay == BestPerformanceOverlay)
                return PowerMode.BestPerformance;
            return PowerMode.Balanced;
        }
        catch (Exception)
        {
            return null; // powrprof without power-mode support (older Windows 10)
        }
    }

    public static bool SetPowerMode(PowerMode mode)
    {
        Guid overlay = mode switch
        {
            PowerMode.BestEfficiency => BestEfficiencyOverlay,
            PowerMode.BestPerformance => BestPerformanceOverlay,
            _ => BalancedOverlay,
        };
        try
        {
            return PowerSetActiveOverlayScheme(overlay) == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ---- Screen brightness (built-in laptop displays) --------------------------------------

    /// <summary>Brightness from 0 to 1, or null when the display does not support it (most external monitors).</summary>
    public static double? GetBrightness()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementBaseObject monitor in searcher.Get())
            {
                using (monitor)
                    return Convert.ToDouble(monitor["CurrentBrightness"]) / 100.0;
            }
        }
        catch (Exception)
        {
        }
        return null;
    }

    public static void SetBrightness(double level)
    {
        try
        {
            byte percent = (byte)Math.Round(Math.Clamp(level, 0, 1) * 100);
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (ManagementObject monitor in searcher.Get().Cast<ManagementObject>())
            {
                using (monitor)
                    monitor.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, percent });
            }
        }
        catch (Exception)
        {
        }
    }

    // ---- Session actions -------------------------------------------------------------------

    public static void Lock() => LockWorkStation();

    public static void Sleep() => SetSuspendState(false, false, false);

    public static void Restart() => RunHidden("shutdown", "/r /t 0");

    public static void ShutDown() => RunHidden("shutdown", "/s /t 0");

    /// <summary>Opens the Windows snipping overlay (same as Win+Shift+S).</summary>
    public static void Snip() => OpenUri("ms-screenclip:");

    /// <summary>Opens a page of the Settings app, e.g. "ms-settings:bluetooth".</summary>
    public static void OpenUri(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception)
        {
        }
    }

    private static void RunHidden(string file, string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file, arguments) { CreateNoWindow = true, UseShellExecute = false });
        }
        catch (Exception)
        {
        }
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetEffectiveOverlayScheme(out Guid overlaySchemeGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveOverlayScheme(Guid overlaySchemeGuid);

    [DllImport("powrprof.dll")]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    [DllImport("user32.dll")]
    private static extern bool LockWorkStation();
}
