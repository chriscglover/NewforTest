// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Diagnostics.CodeAnalysis;

namespace NewforTest.Core;

/// <summary>Subtitle text entered by a user: lines separated by '|', e.g. "LINE ONE|LINE TWO".</summary>
public static class SubtitleText
{
    public const char LineSeparator = '|';

    public static string[] Split(string text) => text.Split(LineSeparator);

    public static string Join(IEnumerable<string> lines) => string.Join(LineSeparator, lines);

    /// <summary>Checks the lines will fit on screen with the given style.</summary>
    public static bool TryValidate(
        IReadOnlyList<string> lines,
        SubtitleStyle style,
        [NotNullWhen(false)] out string? error)
    {
        if (lines.Count is < 1 or > SubtitleLayout.MaxLines)
        {
            error = $"A subtitle must have 1-{SubtitleLayout.MaxLines} lines, not {lines.Count}.";
            return false;
        }

        int max = SubtitleRowBuilder.MaxTextLength(style.Boxed);
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Length > max)
            {
                error = $"Line {i + 1} is {lines[i].Length} characters; at most {max} fit on " +
                        $"{(style.Boxed ? "a boxed" : "an unboxed")} row.";
                return false;
            }
        }

        error = null;
        return true;
    }
}
