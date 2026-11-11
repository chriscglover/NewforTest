// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

namespace NewforTest.Core;

/// <summary>Works out which display rows a subtitle's lines go on.</summary>
public static class SubtitleLayout
{
    /// <summary>
    /// Rows between successive subtitle lines. Two rows leaves room for the bottom half of a
    /// double-height line, and a blank row between single-height lines.
    /// </summary>
    public const int LineSpacing = 2;

    /// <summary>First usable row (row 1 is the page header).</summary>
    public const int TopRow = 2;

    /// <summary>Row that Middle-positioned subtitles are centred around.</summary>
    public const int MiddleRow = 12;

    /// <summary>
    /// Last row a line may start on. A double-height line here also fills row 23; single height
    /// also stops at 22 because some receivers don't show subtitles on row 23 (fastext).
    /// </summary>
    public const int BottomRow = 22;

    /// <summary>Most lines that fit on screen at <see cref="LineSpacing"/>.</summary>
    public const int MaxLines = (BottomRow - TopRow) / LineSpacing + 1;

    /// <summary>Returns the display row for each line, top to bottom.</summary>
    /// <example>3 lines at Lower → 18, 20, 22. 2 lines at Middle → 10, 12.</example>
    public static int[] GetRows(int lineCount, VerticalPosition position)
    {
        if (lineCount is < 1 or > MaxLines)
            throw new ArgumentOutOfRangeException(nameof(lineCount), lineCount, $"A subtitle must have 1-{MaxLines} lines.");

        int startRow = position switch
        {
            VerticalPosition.Top => TopRow,
            VerticalPosition.Middle => MiddleRow - lineCount * LineSpacing / 2,
            _ => BottomRow - (lineCount - 1) * LineSpacing,
        };

        var rows = new int[lineCount];
        for (int i = 0; i < lineCount; i++)
            rows[i] = startRow + i * LineSpacing;
        return rows;
    }
}
