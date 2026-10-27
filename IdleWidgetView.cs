using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MondayNotch.Services;

namespace MondayNotch;

/// <summary>Everything an idle widget may need to draw itself, gathered once per second.</summary>
public sealed class IdleSnapshot
{
    public DateTime Now { get; init; }
    public bool HasBattery { get; init; }
    public double BatteryLevel { get; init; }
    public bool Charging { get; init; }
    public int OpenTodos { get; init; }
    public string? Temperature { get; init; }
    public string WeatherSymbol { get; init; } = "☁";
    public long? PingMs { get; init; }
    public int? Fps { get; init; }
}

/// <summary>Draws one idle widget into a slot of the closed notch (also used by the live preview in Settings).</summary>
public static class IdleWidgetView
{
    private static readonly CultureInfo English = new("en-US");

    public static string Name(IdleWidget widget) => widget switch
    {
        IdleWidget.Todo => "To-Do",
        IdleWidget.Fps => "FPS",
        _ => widget.ToString(),
    };

    public static void Render(Panel host, IdleWidget widget, IdleSnapshot s)
    {
        host.Children.Clear();
        switch (widget)
        {
            case IdleWidget.Clock:
                host.Children.Add(Text(s.Now.ToString(@"HH\:mm", English), 14));
                break;
            case IdleWidget.Date:
                host.Children.Add(Text(s.Now.ToString("ddd d", English)));
                break;
            case IdleWidget.Battery:
                if (!s.HasBattery)
                {
                    host.Children.Add(Muted("–"));
                    break;
                }
                var battery = new BatteryIcon();
                battery.Update(s.BatteryLevel, s.Charging);
                host.Children.Add(battery);
                break;
            case IdleWidget.Todo:
                host.Children.Add(Glyph(""));
                host.Children.Add(Text(s.OpenTodos.ToString(English)));
                break;
            case IdleWidget.Weather:
                if (s.Temperature is null)
                {
                    host.Children.Add(Muted("–°"));
                    break;
                }
                host.Children.Add(Text(s.WeatherSymbol + " " + s.Temperature));
                break;
            case IdleWidget.Ping:
                host.Children.Add(s.PingMs is null ? Muted("– ms") : Text($"{s.PingMs} ms"));
                break;
            case IdleWidget.Fps:
                host.Children.Add(s.Fps is null ? Muted("– fps") : Text($"{s.Fps} fps"));
                break;
        }
    }

    private static TextBlock Text(string text, double size = 12) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = FontWeights.SemiBold,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBlock Muted(string text)
    {
        TextBlock block = Text(text);
        block.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return block;
    }

    private static TextBlock Glyph(string glyph)
    {
        var block = new TextBlock
        {
            Text = glyph,
            FontSize = 10,
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        block.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        return block;
    }
}
