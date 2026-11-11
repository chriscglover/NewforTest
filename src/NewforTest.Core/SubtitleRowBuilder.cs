// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Diagnostics.CodeAnalysis;

namespace NewforTest.Core;

/// <summary>Builds the 40 bytes of display data for one subtitle row.</summary>
/// <remarks>
/// Text row layout (all bytes odd parity apart from the padding):
/// <code>
/// [height] [spaces] [colour] ([SB][SB]) text ([EB][EB]) [padding...]
/// </code>
/// The spaces position the text: none for Left (box against the left edge, text in column 4),
/// as many as fit for Right (End Box codes in columns 38-39), and enough to centre the box for
/// Centre. The box shows one cell of margin each side of the text. Up to 34 characters fit
/// boxed (38 unboxed). Mid-line colour changes take one cell each; a space next to the change
/// is reused for it.
/// </remarks>
public static class SubtitleRowBuilder
{
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
            ? TryBuildRaw(line.Raw, line.Justification, style, out row, out error)
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

        // Left puts the box against the left edge, Right against the right edge; Centre centres
        // the visible box (second Start Box cell to first End Box cell), or the text if unboxed.
        int desiredStart = (line.Justification ?? style.Justification) switch
        {
            Justification.Centre when boxed => (Teletext.RowLength - (cells.Length + 2)) / 2 + 1,
            Justification.Centre => (Teletext.RowLength - cells.Length) / 2,
            Justification.Right => latestStart,
            _ => minPrefix,
        };

        int start = Math.Clamp(desiredStart, minPrefix, latestStart);

        var bytes = new List<byte>(Teletext.RowLength);

        // Height must come first: it applies from this position to the end of the row
        bytes.Add(style.DoubleHeight ? Teletext.DoubleHeight : Teletext.NormalHeight);
        bytes.AddRange(Enumerable.Repeat(Teletext.Space, start - minPrefix));

        // Colour before the box, as broadcast STL files do, so the box has one cell of margin
        // each side: Start Box and End Box both take effect after their own cell
        bytes.Add((byte)lineColor);
        if (boxed)
            bytes.AddRange(new[] { Teletext.StartBox, Teletext.StartBox }); // sent twice, as WST requires

        bytes.AddRange(cells);

        if (boxed)
            bytes.AddRange(new[] { Teletext.EndBox, Teletext.EndBox });

        row = Finish(bytes);
        error = null;
        return true;
    }

    /// <summary>
    /// The cells of a raw line: exact codes as they are, text encoded with the style's
    /// character set (which may expand a character, e.g. an ellipsis to three dots).
    /// </summary>
    public static byte[] EncodeRaw(string raw, SubtitleStyle style, ICollection<char>? unsupported = null)
    {
        var cells = new List<byte>(raw.Length);
        foreach (char c in raw)
        {
            if (SubtitleLine.IsExactCode(c))
                cells.Add((byte)(c - SubtitleLine.ExactCodeBase));
            else
                cells.AddRange(style.CharacterSet.Encode(c.ToString(), unsupported));
        }
        return cells.ToArray();
    }

    private static bool TryBuildRaw(
        string raw,
        Justification? justification,
        SubtitleStyle style,
        [NotNullWhen(true)] out byte[]? row,
        [NotNullWhen(false)] out string? error)
    {
        byte[] cells = EncodeRaw(raw, style);
        List<byte> bytes;

        int firstText = Array.FindIndex(cells, b => b > Teletext.Space);
        if (justification is null || firstText < 0)
        {
            // As authored
            bytes = cells.ToList();
        }
        else
        {
            // Re-justify the visible text. Control codes around it keep their order and stay next
            // to the text; the authored spacing at both ends is dropped.
            int lastText = Array.FindLastIndex(cells, b => b > Teletext.Space);
            var lead = cells[..firstText].Where(b => b < Teletext.Space).ToList();
            var trail = cells[(lastText + 1)..].Where(b => b < Teletext.Space).ToList();
            var text = cells[firstText..(lastText + 1)];

            // As for text lines: Left and Right go to the edges of the row
            int textStart = justification switch
            {
                Justification.Centre => (Teletext.RowLength - text.Length) / 2,
                Justification.Right => Teletext.RowLength - trail.Count - text.Length,
                _ => lead.Count,
            };
            textStart = Math.Clamp(textStart, lead.Count, Math.Max(lead.Count, Teletext.RowLength - trail.Count - text.Length));

            bytes = Enumerable.Repeat(Teletext.Space, textStart - lead.Count)
                .Concat(lead).Concat(text).Concat(trail).ToList();
        }

        // Trailing spaces or control codes beyond column 40 change nothing visible (a box ends at the
        // edge of the row anyway), and real STL files often have a second End Box in column 41.
        // Only text that would be cut off is an error.
        while (bytes.Count > Teletext.RowLength && bytes[^1] <= Teletext.Space)
            bytes.RemoveAt(bytes.Count - 1);

        if (bytes.Count > Teletext.RowLength)
        {
            row = null;
            error = $"Row is {bytes.Count} characters including control codes; a teletext row holds {Teletext.RowLength}.";
            return false;
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
