// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

namespace NewforTest.Core;

/// <summary>Builds the 40 bytes of display data for one subtitle row.</summary>
/// <remarks>
/// Row layout (all bytes odd parity apart from the padding):
/// <code>
/// [height] [space] [space x4] ([start box] [start box]) [colour] [space] text ([end box] [end box]) [padding...]
/// </code>
/// </remarks>
public static class SubtitleRowBuilder
{
    private const int LeadingSpaces = 4;

    /// <summary>Longest text that fits on one row with the given boxing (28 boxed, 32 unboxed).</summary>
    public static int MaxTextLength(bool boxed) =>
        Teletext.RowLength - (2 + LeadingSpaces + 2 + (boxed ? 4 : 0));

    /// <summary>Shortens <paramref name="text"/> to fit on a row if needed.</summary>
    public static string FitText(string text, bool boxed)
    {
        int max = MaxTextLength(boxed);
        return text.Length <= max ? text : text[..max];
    }

    /// <exception cref="ArgumentException">The text is longer than <see cref="MaxTextLength"/>.</exception>
    public static byte[] Build(string text, SubtitleStyle style)
    {
        int max = MaxTextLength(style.Boxed);
        if (text.Length > max)
            throw new ArgumentException(
                $"\"{text}\" is {text.Length} characters; at most {max} fit on a {(style.Boxed ? "boxed" : "unboxed")} row.",
                nameof(text));

        var row = new byte[Teletext.RowLength];
        int n = 0;
        void Add(byte b) => row[n++] = Teletext.OddParity(b);

        // Height must come first: it applies from this position to the end of the row
        Add(style.DoubleHeight ? Teletext.DoubleHeight : Teletext.NormalHeight);
        Add(Teletext.Space);

        for (int i = 0; i < LeadingSpaces; i++)
            Add(Teletext.Space);

        // Start Box is sent twice, as WST requires
        if (style.Boxed)
        {
            Add(Teletext.StartBox);
            Add(Teletext.StartBox);
        }

        Add((byte)style.Color);
        Add(Teletext.Space);

        foreach (char c in text)
            Add(Teletext.EncodeCharacter(c));

        if (style.Boxed)
        {
            Add(Teletext.EndBox);
            Add(Teletext.EndBox);
        }

        Array.Fill(row, Teletext.Padding, n, row.Length - n);
        return row;
    }
}
