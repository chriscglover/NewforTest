// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Diagnostics.CodeAnalysis;

namespace NewforTest.Core;

/// <summary>Subtitle text entered by a user: lines separated by '|', e.g. "LINE ONE|LINE TWO".</summary>
public static class SubtitleText
{
    public const char LineSeparator = '|';

    public static string[] Split(string text) => text.Split(LineSeparator);

    public static string Join(IEnumerable<string> lines) => string.Join(LineSeparator, lines);

    public static string Join(IEnumerable<SubtitleLine> lines) => Join(lines.Select(l => l.ToPlainText()));

    public static IReadOnlyList<SubtitleLine> ToLines(IEnumerable<string> lines) =>
        lines.Select(text => SubtitleLine.Text(text)).ToArray();

    /// <summary>Checks plain text lines will fit on screen with the given style.</summary>
    public static bool TryValidate(
        IReadOnlyList<string> lines,
        SubtitleStyle style,
        [NotNullWhen(false)] out string? error) =>
        TryValidate(ToLines(lines), style, out error);

    /// <summary>Checks the lines will fit on screen with the given style.</summary>
    public static bool TryValidate(
        IReadOnlyList<SubtitleLine> lines,
        SubtitleStyle style,
        [NotNullWhen(false)] out string? error)
    {
        if (!SubtitleLayout.TryGetRows(lines, style.Position, out _, out error))
            return false;

        for (int i = 0; i < lines.Count; i++)
        {
            if (!SubtitleRowBuilder.TryBuild(lines[i], style, out _, out string? rowError))
            {
                error = lines.Count == 1 ? rowError : $"Line {i + 1}: {rowError}";
                return false;
            }
        }

        error = null;
        return true;
    }
}
