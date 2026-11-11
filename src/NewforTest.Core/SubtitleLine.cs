// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

namespace NewforTest.Core;

/// <summary>A piece of text in one colour. A null colour means the style's colour.</summary>
public readonly record struct TextRun(string Text, TeletextColor? Color = null);

/// <summary>
/// One line of a subtitle. Either text (one or more coloured runs, laid out by
/// <see cref="SubtitleRowBuilder"/>) or a raw teletext row as authored (e.g. from an EBU STL
/// file), whose control codes and spacing are kept as they are.
/// </summary>
public sealed record SubtitleLine
{
    /// <summary>
    /// In <see cref="Raw"/>, characters U+E000 to U+E07F stand for the exact 7-bit teletext code
    /// (char - U+E000): control codes, or bytes that must be sent unchanged.
    /// </summary>
    public const char ExactCodeBase = '';

    private SubtitleLine(IReadOnlyList<TextRun> runs, string? raw)
    {
        Runs = runs;
        Raw = raw;
    }

    /// <summary>The text runs. Empty for a raw line.</summary>
    public IReadOnlyList<TextRun> Runs { get; }

    /// <summary>
    /// For a raw line: the row as authored. Ordinary characters are text (encoded with the
    /// style's character set when sent); U+E000-U+E07F are exact codes (see <see cref="ExactCodeBase"/>).
    /// Null for a text line.
    /// </summary>
    public string? Raw { get; }

    /// <summary>Display row (1-23). Null to let the style's vertical position decide.</summary>
    public int? Row { get; init; }

    /// <summary>
    /// Overrides the style's justification for this line. For a raw line, null means keep the
    /// layout exactly as authored.
    /// </summary>
    public Justification? Justification { get; init; }

    public bool IsRaw => Raw is not null;

    public static SubtitleLine Text(string text, TeletextColor? color = null) =>
        new(new[] { new TextRun(text, color) }, null);

    public static SubtitleLine FromRuns(IEnumerable<TextRun> runs) => new(runs.ToArray(), null);

    /// <summary>A raw line of exact 7-bit teletext codes, sent unchanged whatever the character set.</summary>
    public static SubtitleLine FromRaw(byte[] codes) =>
        new(Array.Empty<TextRun>(), new string(codes.Select(b => ExactCode((byte)(b & 0x7F))).ToArray()));

    /// <summary>A raw line mixing text and exact codes (see <see cref="Raw"/>).</summary>
    public static SubtitleLine FromTeletext(string raw) => new(Array.Empty<TextRun>(), raw);

    /// <summary>The character standing for an exact teletext code in <see cref="Raw"/>.</summary>
    public static char ExactCode(byte code) => (char)(ExactCodeBase + (code & 0x7F));

    public static bool IsExactCode(char c) => c >= ExactCodeBase && c < ExactCodeBase + 0x80;

    /// <summary>The line as plain text, without colours or control codes.</summary>
    public string ToPlainText(TeletextCharacterSet? characterSet = null)
    {
        if (Raw is null)
            return string.Concat(Runs.Select(r => r.Text));

        characterSet ??= TeletextCharacterSet.English;
        var chars = Raw.Select(c => IsExactCode(c) ? characterSet.Decode((byte)(c - ExactCodeBase)) : c);
        return new string(chars.ToArray()).Trim();
    }

    public override string ToString() => ToPlainText();
}
