// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

namespace NewforTest.Core;

/// <summary>A piece of text in one colour. A null colour means the style's colour.</summary>
public readonly record struct TextRun(string Text, TeletextColor? Color = null);

/// <summary>
/// One line of a subtitle. Either text (one or more coloured runs, laid out by
/// <see cref="SubtitleRowBuilder"/>) or a raw teletext row as authored (e.g. from an EBU STL
/// file), which is sent as-is apart from parity and padding.
/// </summary>
public sealed record SubtitleLine
{
    private SubtitleLine(IReadOnlyList<TextRun> runs, byte[]? raw)
    {
        Runs = runs;
        Raw = raw;
    }

    /// <summary>The text runs. Empty for a raw line.</summary>
    public IReadOnlyList<TextRun> Runs { get; }

    /// <summary>7-bit teletext codes including control codes, for a raw line; otherwise null.</summary>
    public byte[]? Raw { get; }

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

    public static SubtitleLine FromRaw(byte[] codes) =>
        new(Array.Empty<TextRun>(), codes.Select(b => (byte)(b & 0x7F)).ToArray());

    /// <summary>The line as plain text, without colours or control codes.</summary>
    public string ToPlainText(TeletextCharacterSet? characterSet = null)
    {
        if (Raw is null)
            return string.Concat(Runs.Select(r => r.Text));

        characterSet ??= TeletextCharacterSet.English;
        return new string(Raw.Select(characterSet.Decode).ToArray()).Trim();
    }

    public override string ToString() => ToPlainText();
}
