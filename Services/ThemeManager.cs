using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace MondayNotch.Services;

public enum ThemeMode
{
    Dark,
    Light,
    Auto,
}

/// <summary>Swaps the theme brushes in the application resources (see App.xaml).</summary>
public static class ThemeManager
{
    public static bool IsLight { get; private set; }

    /// <summary>The notch's resting colour in the current theme.</summary>
    public static Color NotchColor => IsLight ? Light.Notch : Dark.Notch;

    private sealed record Palette(
        Color Notch, Color Text, Color Muted, Color Card, Color Track, Color Input, Color Pill, Color PillActive, Color Tile);

    private static readonly Palette Dark = new(
        Notch: Color.FromRgb(0x00, 0x00, 0x00),
        Text: Color.FromRgb(0xFF, 0xFF, 0xFF),
        Muted: Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF),
        Card: Color.FromArgb(0x17, 0xFF, 0xFF, 0xFF),
        Track: Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF),
        Input: Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF),
        Pill: Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF),
        PillActive: Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF),
        Tile: Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));

    private static readonly Palette Light = new(
        Notch: Color.FromRgb(0xF4, 0xF4, 0xF6),
        Text: Color.FromRgb(0x14, 0x14, 0x16),
        Muted: Color.FromArgb(0x8C, 0x00, 0x00, 0x00),
        Card: Color.FromArgb(0x0F, 0x00, 0x00, 0x00),
        Track: Color.FromArgb(0x22, 0x00, 0x00, 0x00),
        Input: Color.FromArgb(0x0D, 0x00, 0x00, 0x00),
        Pill: Color.FromArgb(0x14, 0x00, 0x00, 0x00),
        PillActive: Color.FromArgb(0x26, 0x00, 0x00, 0x00),
        Tile: Color.FromArgb(0x12, 0x00, 0x00, 0x00));

    public static void Apply(ThemeMode mode)
    {
        IsLight = mode switch
        {
            ThemeMode.Light => true,
            ThemeMode.Dark => false,
            _ => WindowsAppsUseLightTheme(),
        };

        Palette p = IsLight ? Light : Dark;
        ResourceDictionary resources = Application.Current.Resources;
        resources["NotchBrush"] = Frozen(p.Notch);
        resources["TextBrush"] = Frozen(p.Text);
        resources["MutedBrush"] = Frozen(p.Muted);
        resources["CardBrush"] = Frozen(p.Card);
        resources["TrackBrush"] = Frozen(p.Track);
        resources["InputBrush"] = Frozen(p.Input);
        resources["PillBrush"] = Frozen(p.Pill);
        resources["PillActiveBrush"] = Frozen(p.PillActive);
        resources["TileBrush"] = Frozen(p.Tile);
    }

    /// <summary>The "Choose your app mode" setting in Windows personalisation.</summary>
    private static bool WindowsAppsUseLightTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
