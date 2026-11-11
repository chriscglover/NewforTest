// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Diagnostics.CodeAnalysis;

namespace NewforTest.Core;

/// <summary>Builds the 40 bytes of display data for one subtitle row.</summary>
/// <remarks>
/// Text row layout (all bytes odd parity apart from the padding):
/// <code>
/// [height] [space] [leading spaces] ([SB][SB]) [colour] [space] text ([EB][EB]) [padding...]
/// </code>
/// The text's start column comes from the justification. Short Left-justified text gets
/// exactly the v3.4 layout (4 leading spaces). Longer text eats into the leading spaces, then
/// the two optional single spaces, so up to 34 characters fit boxed (38 unboxed).
/// Mid-line colour changes take one cell each; a space next to the change is reused for it.
/// </remarks>
public static class SubtitleRowBuilder
{
    private const int PreferredLeadingSpaces = 4;

    /// <summary>Longest text (in cells) that fits on one row: 34 boxed, 38 unboxed.</summary>
    public static int MaxTextLength(bool boxed) => Teletext.RowLength - MinimumPrefix(boxed) - Suffix(boxed);

    /// <summary>Shortens <paramref name="text"/> to fit on a row if needed.</summary>
    public static string FitText(string text, bool boxed)
    {
        int max = MaxTextLength(boxed);
        return text.Length <= max ? text : text[..max];
    }

    /// <exception cref="ArgumentException">The text doesn't fit on a row.</exception>
    public static byte[] Build(string text, SubtitleStyle style) => Build(SubtitleLine.Text(text), style);

    /// <exception cref="ArgumentException">The line doesn't fit on a row.</exception>
    public static byte[] Build(SubtitleLine line, SubtitleStyle style) =>
        TryBuild(line, style, out var row, out var error) ? row : throw new ArgumentException(error);

    public static bool TryBuild(
        SubtitleLine line,
        SubtitleStyle style,
        [NotNullWhen(true)] out byte[]? row,
        [NotNullWhen(false)] out string? error)
    {
        return line.Raw is not null
            ? TryBuildRaw(line.Raw, line.Justification, out row, out error)
            : TryBuildText(line, style, out row, out error);
    }

    /// <summary>
    /// The cells a text line occupies: characters plus any mid-line colour changes.
    /// <paramref name="lineColor"/> is the colour the line starts in.
    /// </summary>
    public static byte[] EncodeText(
        SubtitleLine line,
        SubtitleStyle style,
        out TeletextColor lineColor,
        ICollection<char>? unsupported = null)
    {
        var cells = new List<byte>();
        lineColor = style.Color;
        TeletextColor current = style.Color;
        bool first = true;

        foreach (var run in line.Runs)
        {
            if (run.Text.Length == 0)
                continue;

            byte[] codes = style.CharacterSet.Encode(run.Text, unsupported);
            TeletextColor color = run.Color ?? style.Color;

            if (first)
            {
                lineColor = current = color;
                first = false;
            }
            else if (color != current)
            {
                // The colour attribute shows as a space, so reuse an adjacent space where there is one
                if (cells.Count > 0 && cells[^1] == Teletext.Space)
                    cells[^1] = (byte)color;
                else if (codes[0] == Teletext.Space)
                    codes[0] = (byte)color;
                else
                    cells.Add((byte)color);
                current = color;
            }

            cells.AddRange(codes);
        }

        return cells.ToArray();
    }

    private static bool TryBuildText(
        SubtitleLine line,
        SubtitleStyle style,
        [NotNullWhen(true)] out byte[]? row,
        [NotNullWhen(false)] out string? error)
    {
        byte[] cells = EncodeText(line, style, out var lineColor);
        bool boxed = style.Boxed;
        int minPrefix = MinimumPrefix(boxed);
        int latestStart = Teletext.RowLength - Suffix(boxed) - cells.Length;

        if (latestStart < minPrefix)
        {
            row = null;
            error = $"\"{line.ToPlainText()}\" is {cells.Length} characters; at most {MaxTextLength(boxed)} " +
                    $"fit on {(boxed ? "a boxed" : "an unboxed")} row.";
            return false;
        }

        int leftStart = minPrefix + 2 + PreferredLeadingSpaces; // 10 boxed, 8 unboxed: the v3.4 layout
        int desiredStart = (line.Justification ?? style.Justification) switch
        {
            Justification.Centre => (Teletext.RowLength - cells.Length) / 2,
            Justification.Right => Teletext.RowLength - leftStart - cells.Length,
            _ => leftStart,
        };

        int start = Math.Clamp(desiredStart, minPrefix, latestStart);
        int extra = start - minPrefix;
        bool spaceAfterColour = extra >= 1;
        bool spaceAfterHeight = extra >= 2;
        int leadingSpaces = extra - (spaceAfterColour ? 1 : 0) - (spaceAfterHeight ? 1 : 0);

        var bytes = new List<byte>(Teletext.RowLength);

        // Height must come first: it applies from this position to the end of the row
        bytes.Add(style.DoubleHeight ? Teletext.DoubleHeight : Teletext.NormalHeight);
        if (spaceAfterHeight)
            bytes.Add(Teletext.Space);
        bytes.AddRange(Enumerable.Repeat(Teletext.Space, leadingSpaces));

        // Start Box is sent twice, as WST requires
        if (boxed)
            bytes.AddRange(new[] { Teletext.StartBox, Teletext.StartBox });

        bytes.Add((byte)lineColor);
        if (spaceAfterColour)
            bytes.Add(Teletext.Space);

        bytes.AddRange(cells);

        if (boxed)
            bytes.AddRange(new[] { Teletext.EndBox, Teletext.EndBox });

        row = Finish(bytes);
        error = null;
        return true;
    }

    private static bool TryBuildRaw(
        byte[] codes,
        Justification? justification,
        [NotNullWhen(true)] out byte[]? row,
        [NotNullWhen(false)] out string? error)
    {
        IEnumerable<byte> content = codes;
        int leading = 0;

        if (justification is not null)
        {
            // Re-justify: drop the authored spacing at both ends, then position the content
            int first = Array.FindIndex(codes, b => b != Teletext.Space);
            int last = Array.FindLastIndex(codes, b => b != Teletext.Space);
            content = first < 0 ? Array.Empty<byte>() : codes[first..(last + 1)];
            int length = content.Count();

            leading = justification switch
            {
                Justification.Centre => (Teletext.RowLength - length) / 2,
                Justification.Right => Teletext.RowLength - 2 - length,
                _ => 2,
            };
            leading = Math.Clamp(leading, 0, Math.Max(0, Teletext.RowLength - length));
        }

        var bytes = Enumerable.Repeat(Teletext.Space, leading).Concat(content).ToList();
        if (bytes.Count > Teletext.RowLength)
        {
            // Trailing spaces beyond column 40 are harmless; anything else is an authoring error
            while (bytes.Count > Teletext.RowLength && bytes[^1] == Teletext.Space)
                bytes.RemoveAt(bytes.Count - 1);

            if (bytes.Count > Teletext.RowLength)
            {
                row = null;
                error = $"Row is {bytes.Count} bytes; a teletext row holds {Teletext.RowLength}.";
                return false;
            }
        }

        row = Finish(bytes);
        error = null;
        return true;
    }

    private static byte[] Finish(List<byte> bytes)
    {
        var row = new byte[Teletext.RowLength];
        for (int i = 0; i < bytes.Count; i++)
            row[i] = Teletext.OddParity(bytes[i]);
        Array.Fill(row, Teletext.Padding, bytes.Count, row.Length - bytes.Count);
        return row;
    }

    // Before the text: height code, two Start Box codes if boxed, the colour code
    private static int MinimumPrefix(bool boxed) => 1 + (boxed ? 2 : 0) + 1;

    // After the text: two End Box codes if boxed
    private static int Suffix(bool boxed) => boxed ? 2 : 0;
}
