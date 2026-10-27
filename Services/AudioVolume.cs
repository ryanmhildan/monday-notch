using System;
using System.Runtime.InteropServices;

namespace MondayNotch.Services;

/// <summary>Master volume of the default playback device, through the Windows Core Audio API.</summary>
public static class AudioVolume
{
    /// <summary>Volume from 0 to 1, or null when there is no playback device.</summary>
    public static double? Get()
    {
        try
        {
            IAudioEndpointVolume? endpoint = DefaultEndpoint();
            if (endpoint is null)
                return null;
            endpoint.GetMasterVolumeLevelScalar(out float level);
            Marshal.ReleaseComObject(endpoint);
            return level;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void Set(double level)
    {
        try
        {
            IAudioEndpointVolume? endpoint = DefaultEndpoint();
            if (endpoint is null)
                return;
            Guid context = Guid.Empty;
            endpoint.SetMasterVolumeLevelScalar((float)Math.Clamp(level, 0, 1), ref context);
            // Dragging the slider up from zero should be audible again.
            if (level > 0)
                endpoint.SetMute(false, ref context);
            Marshal.ReleaseComObject(endpoint);
        }
        catch (Exception)
        {
        }
    }

    // The default device can change (headphones plugged in), so it is looked up on every call.
    private static IAudioEndpointVolume? DefaultEndpoint()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        try
        {
            enumerator.GetDefaultAudioEndpoint(EDataFlowRender, ERoleMultimedia, out IMMDevice device);
            Guid iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out object endpoint);
            Marshal.ReleaseComObject(device);
            return (IAudioEndpointVolume)endpoint;
        }
        catch (COMException)
        {
            return null; // no playback device
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    private const int EDataFlowRender = 0;
    private const int ERoleMultimedia = 1;
    private const int ClsCtxAll = 0x17;

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject
    {
    }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        void Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(IntPtr notify);
        void UnregisterControlChangeNotify(IntPtr notify);
        void GetChannelCount(out uint count);
        void SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        void SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        void GetMasterVolumeLevel(out float levelDb);
        void GetMasterVolumeLevelScalar(out float level);
        void SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
        void SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        void GetChannelVolumeLevel(uint channel, out float levelDb);
        void GetChannelVolumeLevelScalar(uint channel, out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
