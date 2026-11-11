// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Diagnostics.CodeAnalysis;

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

    /// <summary>
    /// Rows for a set of lines: their own <see cref="SubtitleLine.Row"/> if every line has one
    /// (as in EBU STL files), otherwise laid out by the style's vertical position.
    /// </summary>
    public static bool TryGetRows(
        IReadOnlyList<SubtitleLine> lines,
        VerticalPosition position,
        [NotNullWhen(true)] out int[]? rows,
        [NotNullWhen(false)] out string? error)
    {
        rows = null;
        int withRow = lines.Count(l => l.Row is not null);

        if (withRow == 0)
        {
            if (lines.Count is < 1 or > MaxLines)
            {
                error = $"A subtitle must have 1-{MaxLines} lines, not {lines.Count}.";
                return false;
            }

            rows = GetRows(lines.Count, position);
            error = null;
            return true;
        }

        if (withRow != lines.Count)
        {
            error = "Either every line or no line must have a row number.";
            return false;
        }

        rows = lines.Select(l => l.Row!.Value).ToArray();
        for (int i = 0; i < rows.Length; i++)
        {
            if (rows[i] is < NewforPackets.MinRow or > NewforPackets.MaxRow)
            {
                error = $"Row {rows[i]} is off screen; rows must be {NewforPackets.MinRow}-{NewforPackets.MaxRow}.";
                return false;
            }

            if (i > 0 && rows[i] <= rows[i - 1])
            {
                error = $"Rows must go down the screen, but row {rows[i]} follows row {rows[i - 1]}.";
                return false;
            }
        }

        error = null;
        return true;
    }
}
