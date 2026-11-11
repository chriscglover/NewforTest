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

public enum Justification
{
    /// <summary>Text starts at column 10 (8 unboxed), the v3.4 layout.</summary>
    Left,

    /// <summary>Text centred on the row.</summary>
    Centre,

    /// <summary>Mirror image of Left: the box ends 8 columns from the right edge.</summary>
    Right,
}

/// <summary>How a subtitle is drawn: colour, height, boxing, position and character set.</summary>
public sealed record SubtitleStyle(
    TeletextColor Color = TeletextColor.White,
    bool DoubleHeight = false,
    bool Boxed = true,
    VerticalPosition Position = VerticalPosition.Lower,
    Justification Justification = Justification.Left)
{
    /// <summary>Must match the national option the inserter uses for the page.</summary>
    public TeletextCharacterSet CharacterSet { get; init; } = TeletextCharacterSet.English;
}
