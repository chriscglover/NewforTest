// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace NewforTest.Core;

/// <summary>
/// Reads SubRip (.srt) files. Tolerant of the usual variations: missing or wrong cue numbers,
/// '.' instead of ',' before the milliseconds, extra text after the timing, no blank line
/// between cues, and CR/LF or LF line ends.
/// </summary>
/// <remarks>
/// Markup: <c>&lt;font color="..."&gt;</c> becomes a teletext colour (named or #RRGGBB, matched
/// to the nearest of the 7 teletext colours); <c>{\anN}</c> sets position and justification;
/// <c>&lt;i&gt;</c>, <c>&lt;b&gt;</c>, <c>&lt;u&gt;</c> and other tags are dropped because
/// teletext can't show them. HTML entities are decoded.
/// </remarks>
public static partial class SrtParser
{
    public static SubtitleDocument Load(string path) =>
        Parse(SubtitleFile.ReadText(path)) with { Name = Path.GetFileName(path) };

    public static SubtitleDocument Parse(string text)
    {
        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var cues = new List<SubtitleCue>();
        var warnings = new List<string>();
        int i = 0;

        while (i < lines.Length)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                i++;
                continue;
            }

            int blockLine = i + 1;
            int? number = null;

            // Optional cue number, then the timing line
            if (!lines[i].Contains("-->") && i + 1 < lines.Length && lines[i + 1].Contains("-->"))
            {
                if (int.TryParse(lines[i].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int n))
                    number = n;
                else
                    warnings.Add($"Line {i + 1}: expected a cue number, found \"{Shorten(lines[i])}\".");
                i++;
            }

            var timing = TimingPattern().Match(lines[i]);
            if (!timing.Success)
            {
                warnings.Add($"Line {i + 1}: expected a timing line like 00:00:01,000 --> 00:00:03,000; skipped \"{Shorten(lines[i])}\".");
                i = SkipBlock(lines, i);
                continue;
            }

            TimeSpan start = ParseTime(timing, "1"), end = ParseTime(timing, "2");
            i++;

            var textLines = new List<string>();
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && !IsCueStart(lines, i))
                textLines.Add(lines[i++]);

            number ??= cues.Count + 1;

            if (textLines.Count == 0)
            {
                warnings.Add($"Cue {number} (line {blockLine}): no text; skipped.");
                continue;
            }

            if (end <= start)
            {
                warnings.Add($"Cue {number} (line {blockLine}): ends at or before it starts; skipped.");
                continue;
            }

            cues.Add(ParseCue(number.Value, start, end, textLines));
        }

        if (cues.Zip(cues.Skip(1)).Any(pair => pair.Second.Start < pair.First.Start))
        {
            warnings.Add("Cues are not in time order; they have been sorted.");
            cues = cues.OrderBy(c => c.Start).ToList();
        }

        foreach (var (first, second) in cues.Zip(cues.Skip(1)))
        {
            if (second.Start < first.End)
                warnings.Add($"Cue {second.Number} starts before cue {first.Number} ends; it will replace it early.");
        }

        return new SubtitleDocument("SRT", cues, warnings);
    }

    private static SubtitleCue ParseCue(int number, TimeSpan start, TimeSpan end, List<string> textLines)
    {
        VerticalPosition? position = null;
        Justification? justification = null;
        var lines = new List<SubtitleLine>();
        var colorStack = new Stack<TeletextColor?>();

        foreach (string textLine in textLines)
        {
            var runs = new List<TextRun>();
            var text = new StringBuilder();

            void Flush()
            {
                if (text.Length > 0)
                    runs.Add(new TextRun(WebUtility.HtmlDecode(text.ToString()), colorStack.Count > 0 ? colorStack.Peek() : null));
                text.Clear();
            }

            foreach (Match token in MarkupPattern().Matches(textLine))
            {
                if (token.Groups["an"].Success)
                {
                    int an = token.Groups["an"].Value[0] - '0';
                    position = an >= 7 ? VerticalPosition.Top : an >= 4 ? VerticalPosition.Middle : VerticalPosition.Lower;
                    justification = (an % 3) switch { 1 => Justification.Left, 0 => Justification.Right, _ => Justification.Centre };
                }
                else if (token.Groups["fontColor"].Success)
                {
                    Flush();
                    colorStack.Push(ParseColor(token.Groups["fontColor"].Value));
                }
                else if (token.Groups["fontOpen"].Success)
                {
                    Flush();
                    colorStack.Push(colorStack.Count > 0 ? colorStack.Peek() : null); // <font> without colour
                }
                else if (token.Groups["fontClose"].Success)
                {
                    Flush();
                    if (colorStack.Count > 0)
                        colorStack.Pop();
                }
                else if (token.Groups["text"].Success)
                {
                    text.Append(token.Groups["text"].Value);
                }
                // any other tag ({\...}, <i>, <b>, <u>, ...) is dropped
            }

            Flush();
            if (runs.Count == 0)
                runs.Add(new TextRun(""));

            lines.Add(SubtitleLine.FromRuns(runs) with { Justification = justification });
        }

        // {\an} may appear on any line but applies to the whole cue
        if (justification is not null)
            lines = lines.Select(l => l with { Justification = justification }).ToList();

        return new SubtitleCue(number, start, end, lines) { Position = position };
    }

    /// <summary>Maps an HTML colour to the nearest teletext colour (black becomes white).</summary>
    internal static TeletextColor? ParseColor(string value)
    {
        value = value.Trim().Trim('"', '\'').ToLowerInvariant();

        TeletextColor? named = value switch
        {
            "red" or "darkred" or "maroon" => TeletextColor.Red,
            "green" or "lime" or "darkgreen" => TeletextColor.Green,
            "yellow" or "gold" or "orange" => TeletextColor.Yellow,
            "blue" or "navy" or "darkblue" => TeletextColor.Blue,
            "magenta" or "fuchsia" or "purple" or "pink" => TeletextColor.Magenta,
            "cyan" or "aqua" or "teal" or "lightblue" => TeletextColor.Cyan,
            "white" or "silver" or "gray" or "grey" or "black" => TeletextColor.White,
            _ => null,
        };
        if (named is not null)
            return named;

        string hex = value.TrimStart('#');
        if (hex.Length == 3)
            hex = string.Concat(hex.Select(c => $"{c}{c}"));
        if (hex.Length == 8)
            hex = hex[2..]; // ARGB / with alpha
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            return null;

        int code = ((rgb >> 16 & 0xFF) >= 0x80 ? 1 : 0)
                 | ((rgb >> 8 & 0xFF) >= 0x80 ? 2 : 0)
                 | ((rgb & 0xFF) >= 0x80 ? 4 : 0);
        return code == 0 ? TeletextColor.White : (TeletextColor)code;
    }

    private static bool IsCueStart(string[] lines, int i) =>
        lines[i].Contains("-->") && TimingPattern().IsMatch(lines[i]) ||
        i + 1 < lines.Length && lines[i].Trim().All(char.IsDigit) && TimingPattern().IsMatch(lines[i + 1]);

    private static int SkipBlock(string[] lines, int i)
    {
        while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]))
            i++;
        return i;
    }

    private static TimeSpan ParseTime(Match match, string which)
    {
        int Part(string name) => int.Parse(match.Groups[name + which].Value, CultureInfo.InvariantCulture);
        string ms = match.Groups["ms" + which].Value.PadRight(3, '0');
        return new TimeSpan(0, Part("h"), Part("m"), Part("s"), int.Parse(ms, CultureInfo.InvariantCulture));
    }

    private static string Shorten(string text) => text.Length <= 40 ? text.Trim() : text[..40].Trim() + "...";

    [GeneratedRegex(@"^\s*(?<h1>\d+):(?<m1>\d{1,2}):(?<s1>\d{1,2})[,.](?<ms1>\d{1,3})\s*-->\s*(?<h2>\d+):(?<m2>\d{1,2}):(?<s2>\d{1,2})[,.](?<ms2>\d{1,3})")]
    private static partial Regex TimingPattern();

    [GeneratedRegex(
        @"\{\\an(?<an>[1-9])\}" +
        @"|<font\s[^>]*?color\s*=\s*(?<fontColor>""[^""]*""|'[^']*'|[^\s>]+)[^>]*>" +
        @"|(?<fontOpen><font\b[^>]*>)" +
        @"|(?<fontClose></font\s*>)" +
        @"|\{\\[^}]*\}" +
        @"|</?[a-zA-Z][^>]*>" +
        @"|(?<text>[^<{]+|[<{])",
        RegexOptions.IgnoreCase)]
    private static partial Regex MarkupPattern();
}
