// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

namespace NewforTest.Core;

public enum VerticalPosition
{
    /// <summary>First line on row 2 (row 1 is the page header).</summary>
    Top,

    /// <summary>Lines centred around row 12.</summary>
    Middle,

    /// <summary>Last line on row 22.</summary>
    Lower,
}

/// <summary>How a subtitle is drawn: colour, height, boxing and screen position.</summary>
public sealed record SubtitleStyle(
    TeletextColor Color = TeletextColor.White,
    bool DoubleHeight = false,
    bool Boxed = true,
    VerticalPosition Position = VerticalPosition.Lower);
