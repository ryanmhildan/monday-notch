using System;
using Microsoft.Win32;

namespace MondayNotch.Services;

public enum NotchShape
{
    Anchored,
    Floating,
}

/// <summary>What a slot of the closed notch shows while nothing else (music, timer) needs it.</summary>
public enum IdleWidget
{
    Empty,
    Clock,
    Date,
    Battery,
    Todo,
    Weather,
    Ping,
    Fps,
}

/// <summary>Per-user preferences, kept in HKCU\Software\MondayNotch. Missing values fall back to the defaults here.</summary>
public static class AppSettings
{
    private const string KeyPath = @"Software\MondayNotch";

    public static bool LyricsEnabled
    {
        get => ReadInt("LyricsEnabled", 0) != 0;
        set => WriteInt("LyricsEnabled", value ? 1 : 0);
    }

    // General

    public static bool GreetingEnabled
    {
        get => ReadInt("GreetingEnabled", 1) != 0;
        set => WriteInt("GreetingEnabled", value ? 1 : 0);
    }

    /// <summary>Use the Windows account name in the greeting rather than <see cref="GreetingName"/>.</summary>
    public static bool GreetingNameAuto
    {
        get => ReadInt("GreetingNameAuto", 1) != 0;
        set => WriteInt("GreetingNameAuto", value ? 1 : 0);
    }

    public static string GreetingName
    {
        get => ReadString("GreetingName", "");
        set => WriteString("GreetingName", value);
    }

    /// <summary>Shrink the closed notch to a thin line while a web browser is the active window.</summary>
    public static bool AutoCompact
    {
        get => ReadInt("AutoCompact", 0) != 0;
        set => WriteInt("AutoCompact", value ? 1 : 0);
    }

    // Appearance

    public static NotchShape Shape
    {
        get => ReadEnum("Shape", NotchShape.Anchored);
        set => WriteInt("Shape", (int)value);
    }

    public const int MaxFloatingDistance = 40;

    /// <summary>Gap between the top of the screen and a floating notch, in pixels.</summary>
    public static int FloatingDistance
    {
        get => Math.Clamp(ReadInt("FloatingDistance", 8), 0, MaxFloatingDistance);
        set => WriteInt("FloatingDistance", Math.Clamp(value, 0, MaxFloatingDistance));
    }

    public static ThemeMode Theme
    {
        get => ReadEnum("Theme", ThemeMode.Dark);
        set => WriteInt("Theme", (int)value);
    }

    // Idle

    public static IdleWidget IdleLeft
    {
        get => ReadEnum("IdleLeft", IdleWidget.Empty);
        set => WriteInt("IdleLeft", (int)value);
    }

    public static IdleWidget IdleCenter
    {
        get => ReadEnum("IdleCenter", IdleWidget.Clock);
        set => WriteInt("IdleCenter", (int)value);
    }

    public static IdleWidget IdleRight
    {
        get => ReadEnum("IdleRight", IdleWidget.Battery);
        set => WriteInt("IdleRight", (int)value);
    }

    /// <summary>Removes every stored preference, so all of them go back to their defaults.</summary>
    public static void ResetAll()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
        }
        catch (Exception)
        {
        }
    }

    private static T ReadEnum<T>(string name, T fallback) where T : struct, Enum
    {
        int value = ReadInt(name, Convert.ToInt32(fallback));
        return Enum.IsDefined(typeof(T), value) ? (T)Enum.ToObject(typeof(T), value) : fallback;
    }

    private static int ReadInt(string name, int fallback)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(name) is int value ? value : fallback;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private static string ReadString(string name, string fallback)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(name) as string ?? fallback;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private static void WriteInt(string name, int value)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key.SetValue(name, value, RegistryValueKind.DWord);
        }
        catch (Exception)
        {
            // Not being able to remember a preference is not worth interrupting the user for.
        }
    }

    private static void WriteString(string name, string value)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key.SetValue(name, value, RegistryValueKind.String);
        }
        catch (Exception)
        {
        }
    }
}
