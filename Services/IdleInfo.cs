using System;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;

namespace MondayNotch.Services;

/// <summary>
/// Live values for the closed notch's idle widgets that are not simply read from the clock or battery.
/// Each one is only refreshed while a slot shows it, so unused widgets cost nothing (no network, no polling).
/// </summary>
public sealed class IdleInfo
{
    private static readonly TimeSpan WeatherInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(5);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private DateTime _nextWeather = DateTime.MinValue;
    private DateTime _nextPing = DateTime.MinValue;
    private bool _weatherBusy, _pingBusy;
    private ulong _lastFrameCount;
    private long _lastFrameTicks;

    /// <summary>E.g. "27°", or null while unknown (no location access, offline).</summary>
    public string? Temperature { get; private set; }

    /// <summary>A single symbol for the current weather (☀, ☁, ☂, ...).</summary>
    public string WeatherSymbol { get; private set; } = "☁";

    /// <summary>Round trip to 1.1.1.1 in milliseconds, or null when it failed.</summary>
    public long? PingMs { get; private set; }

    /// <summary>Frames the Windows compositor drew in the last second: how fast the screen is actually updating.</summary>
    public int? Fps { get; private set; }

    public event Action? Changed;

    /// <summary>Call once a second from the UI thread with the widgets currently on screen.</summary>
    public void Tick(bool weather, bool ping, bool fps)
    {
        DateTime now = DateTime.Now;
        if (weather && !_weatherBusy && now >= _nextWeather)
        {
            _nextWeather = now + WeatherInterval;
            _ = RefreshWeatherAsync();
        }
        if (ping && !_pingBusy && now >= _nextPing)
        {
            _nextPing = now + PingInterval;
            _ = RefreshPingAsync();
        }
        if (fps)
            SampleFps();
    }

    private async Task RefreshWeatherAsync()
    {
        _weatherBusy = true;
        try
        {
            // Location stays on this PC except for the rounded coordinates sent to Open-Meteo.
            if (await Geolocator.RequestAccessAsync() != GeolocationAccessStatus.Allowed)
            {
                Temperature = null;
                return;
            }
            Geoposition position = await new Geolocator { DesiredAccuracy = PositionAccuracy.Default }
                .GetGeopositionAsync(TimeSpan.FromHours(1), TimeSpan.FromSeconds(10));
            double lat = Math.Round(position.Coordinate.Point.Position.Latitude, 2);
            double lon = Math.Round(position.Coordinate.Point.Position.Longitude, 2);

            string url = FormattableString.Invariant(
                $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,weather_code");
            using JsonDocument json = JsonDocument.Parse(await Http.GetStringAsync(url));
            JsonElement current = json.RootElement.GetProperty("current");
            Temperature = $"{Math.Round(current.GetProperty("temperature_2m").GetDouble())}°";
            WeatherSymbol = SymbolFor(current.GetProperty("weather_code").GetInt32());
        }
        catch (Exception)
        {
            Temperature = null;
            _nextWeather = DateTime.Now + TimeSpan.FromMinutes(5); // try again sooner after a failure
        }
        finally
        {
            _weatherBusy = false;
            Changed?.Invoke();
        }
    }

    /// <summary>WMO weather codes as used by Open-Meteo.</summary>
    private static string SymbolFor(int code) => code switch
    {
        0 => "☀",
        1 or 2 => "⛅",
        >= 51 and <= 67 or >= 80 and <= 82 => "☂",
        >= 71 and <= 77 or 85 or 86 => "❄",
        >= 95 => "⚡",
        _ => "☁",
    };

    private async Task RefreshPingAsync()
    {
        _pingBusy = true;
        try
        {
            using var ping = new Ping();
            PingReply reply = await ping.SendPingAsync("1.1.1.1", 1500);
            PingMs = reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch (Exception)
        {
            PingMs = null;
        }
        finally
        {
            _pingBusy = false;
            Changed?.Invoke();
        }
    }

    private void SampleFps()
    {
        try
        {
            var info = new DwmTimingInfo { cbSize = (uint)Marshal.SizeOf<DwmTimingInfo>() };
            if (DwmGetCompositionTimingInfo(IntPtr.Zero, ref info) != 0)
            {
                Fps = null;
                return;
            }

            long ticks = Environment.TickCount64;
            if (_lastFrameTicks != 0 && info.cFrame >= _lastFrameCount)
            {
                double seconds = (ticks - _lastFrameTicks) / 1000.0;
                Fps = seconds > 0 ? (int)Math.Round((info.cFrame - _lastFrameCount) / seconds) : Fps;
            }
            _lastFrameCount = info.cFrame;
            _lastFrameTicks = ticks;
        }
        catch (Exception)
        {
            Fps = null;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetCompositionTimingInfo(IntPtr hwnd, ref DwmTimingInfo timingInfo);

    // DWM_TIMING_INFO from dwmapi.h, which is declared with 1-byte packing.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct DwmTimingInfo
    {
        public uint cbSize;
        public uint rateRefreshNumerator;
        public uint rateRefreshDenominator;
        public ulong qpcRefreshPeriod;
        public uint rateComposeNumerator;
        public uint rateComposeDenominator;
        public ulong qpcVBlank;
        public ulong cRefresh;
        public uint cDXRefresh;
        public ulong qpcCompose;
        public ulong cFrame;
        public uint cDXPresent;
        public ulong cRefreshFrame;
        public ulong cFrameSubmitted;
        public uint cDXPresentSubmitted;
        public ulong cFrameConfirmed;
        public uint cDXPresentConfirmed;
        public ulong cRefreshConfirmed;
        public uint cDXRefreshConfirmed;
        public ulong cFramesLate;
        public uint cFramesOutstanding;
        public ulong cFrameDisplayed;
        public ulong qpcFrameDisplayed;
        public ulong cRefreshFrameDisplayed;
        public ulong cFrameComplete;
        public ulong qpcFrameComplete;
        public ulong cFramePending;
        public ulong qpcFramePending;
        public ulong cFramesDisplayed;
        public ulong cFramesComplete;
        public ulong cFramesPending;
        public ulong cFramesAvailable;
        public ulong cFramesDropped;
        public ulong cFramesMissed;
        public ulong cRefreshNextDisplayed;
        public ulong cRefreshNextPresented;
        public ulong cRefreshesDisplayed;
        public ulong cRefreshesPresented;
        public ulong cRefreshStarted;
        public ulong cPixelsReceived;
        public ulong cPixelsDrawn;
        public ulong cBuffersEmpty;
    }
}
