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
    /// <summary>Against the left edge: box from column 3, text from column 4 (column 2 unboxed).</summary>
    Left,

    /// <summary>Box (or unboxed text) centred on the row.</summary>
    Centre,

    /// <summary>Against the right edge: text ends in column 37, End Box codes in 38-39.</summary>
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

    /// <summary>
    /// Background colour inside the box (boxed subtitles only). Black needs no extra codes;
    /// any other colour costs two cells, so at most 32 characters fit instead of 34.
    /// </summary>
    public TeletextColor BoxColor { get; init; } = TeletextColor.Black;

    /// <summary>True if the box has a background colour other than the default black.</summary>
    public bool HasColouredBox => Boxed && BoxColor != TeletextColor.Black;
}
