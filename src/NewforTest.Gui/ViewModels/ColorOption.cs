// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using Avalonia.Media;
using NewforTest.Core;

namespace NewforTest.Gui.ViewModels;

/// <summary>A teletext text colour, with a swatch for the colour picker.</summary>
public sealed record ColorOption(TeletextColor Color, IBrush Brush)
{
    public static IReadOnlyList<ColorOption> All { get; } = new[]
    {
        new ColorOption(TeletextColor.White, Brushes.White),
        new ColorOption(TeletextColor.Yellow, Brushes.Yellow),
        new ColorOption(TeletextColor.Cyan, Brushes.Cyan),
        new ColorOption(TeletextColor.Green, Brushes.Lime),
        new ColorOption(TeletextColor.Magenta, Brushes.Magenta),
        new ColorOption(TeletextColor.Red, Brushes.Red),
        new ColorOption(TeletextColor.Blue, Brushes.Blue),
    };

    public string Name => Color.ToString();
}
