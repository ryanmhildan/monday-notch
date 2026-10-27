using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MondayNotch;

/// <summary>A small battery drawing with a lightning bolt in front of it while the charger is connected.</summary>
public sealed class BatteryIcon : StackPanel
{
    private const double InnerWidth = 15; // body width minus border and padding

    private static readonly Brush ChargingBrush = Frozen(Color.FromRgb(0x4A, 0xDE, 0x80));
    private static readonly Brush LowBrush = Frozen(Color.FromRgb(0xFF, 0x5A, 0x5A));

    private readonly Border _fill;
    private readonly Path _bolt;

    public BatteryIcon()
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;

        _bolt = new Path
        {
            Data = Geometry.Parse("M4.5,0 L0,6.5 H3.2 L2.5,11 L7,4.5 H3.8 Z"),
            Fill = ChargingBrush,
            Width = 7,
            Height = 11,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 3, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        _fill = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(1.5),
            Width = InnerWidth,
        };
        var body = new Border
        {
            Width = 20,
            Height = 11,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(1.5),
            Child = _fill,
        };
        var tip = new Border
        {
            Width = 1.5,
            Height = 4,
            Margin = new Thickness(1, 0, 0, 0),
            CornerRadius = new CornerRadius(0, 1, 1, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        // Outline and normal fill follow the theme (white on dark, near-black on light).
        body.SetResourceReference(Border.BorderBrushProperty, "MutedBrush");
        tip.SetResourceReference(Border.BackgroundProperty, "MutedBrush");
        _fill.SetResourceReference(Border.BackgroundProperty, "TextBrush");

        Children.Add(_bolt);
        Children.Add(body);
        Children.Add(tip);
    }

    /// <param name="level">Charge from 0 to 1.</param>
    public void Update(double level, bool charging)
    {
        _fill.Width = Math.Max(1.5, InnerWidth * level);
        if (charging)
            _fill.Background = ChargingBrush;
        else if (level <= 0.2)
            _fill.Background = LowBrush;
        else
            _fill.SetResourceReference(Border.BackgroundProperty, "TextBrush");
        _bolt.Visibility = charging ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
