using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MondayNotch.Services;

/// <summary>Picks two colours from album art for the notch gradient, toned so the theme's text stays readable.</summary>
public static class ArtworkPalette
{
    private const int SampleSize = 32;
    private const double MaxLuminance = 0.30;      // dark theme: dark enough for white text
    private const double MinLightLuminance = 0.80; // light theme: light enough for dark text
    private const double MinSecondDistance = 70;   // RGB distance for the second colour to count as different

    public static (Color Start, Color End)? FromImage(byte[]? bytes, bool light)
    {
        if (bytes is null || bytes.Length == 0)
            return null;

        byte[] pixels;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = SampleSize;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();

            var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            int stride = bgra.PixelWidth * 4;
            pixels = new byte[stride * bgra.PixelHeight];
            bgra.CopyPixels(pixels, stride, 0);
        }
        catch (Exception)
        {
            return null;
        }

        // Group similar pixels into coarse buckets (4 bits per channel) and average each bucket.
        var buckets = new Dictionary<int, (long R, long G, long B, int Count)>();
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2], a = pixels[i + 3];
            if (a < 128 || Math.Max(r, Math.Max(g, b)) < 30)
                continue; // transparent or near-black pixels say nothing about the art's colour

            int key = (r >> 4) << 8 | (g >> 4) << 4 | (b >> 4);
            buckets.TryGetValue(key, out var sum);
            buckets[key] = (sum.R + r, sum.G + g, sum.B + b, sum.Count + 1);
        }
        if (buckets.Count == 0)
            return null;

        // Favour colours that are both common and colourful, so a grey background loses to the subject.
        var ranked = buckets.Values
            .Select(s =>
            {
                Color color = Color.FromRgb((byte)(s.R / s.Count), (byte)(s.G / s.Count), (byte)(s.B / s.Count));
                return (Color: color, Score: s.Count * (0.25 + Saturation(color)));
            })
            .OrderByDescending(c => c.Score)
            .Select(c => c.Color)
            .ToList();

        Color first = ranked[0];
        Color second = ranked.Skip(1).FirstOrDefault(c => Distance(c, first) >= MinSecondDistance, Scale(first, 0.45));
        return (Tone(first, light), Tone(second, light));
    }

    private static double Saturation(Color c)
    {
        int max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
        return max == 0 ? 0 : (max - min) / (double)max;
    }

    private static double Distance(Color a, Color b) =>
        Math.Sqrt(Math.Pow(a.R - b.R, 2) + Math.Pow(a.G - b.G, 2) + Math.Pow(a.B - b.B, 2));

    /// <summary>
    /// Makes a colour a little more vivid (darkening alone turns pastels grey), then darkens it until its
    /// luminance is at most <see cref="MaxLuminance"/> (dark theme, white text) or lightens it until it is at
    /// least <see cref="MinLightLuminance"/> (light theme, dark text).
    /// </summary>
    private static Color Tone(Color c, bool light)
    {
        const double vividness = 1.4;
        double grey = Luminance(c) * 255;
        byte Push(byte channel) => (byte)Math.Clamp(grey + (channel - grey) * vividness, 0, 255);
        c = Color.FromRgb(Push(c.R), Push(c.G), Push(c.B));

        double luminance = Luminance(c);
        if (light)
        {
            if (luminance >= MinLightLuminance)
                return c;
            // Mix with white just enough to reach the target brightness.
            double k = (MinLightLuminance - luminance) / (1 - luminance);
            byte Lift(byte channel) => (byte)Math.Round(channel + (255 - channel) * k);
            return Color.FromRgb(Lift(c.R), Lift(c.G), Lift(c.B));
        }
        return luminance <= MaxLuminance ? c : Scale(c, MaxLuminance / luminance);
    }

    private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;

    private static Color Scale(Color c, double factor) =>
        Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));
}
