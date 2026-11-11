// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Globalization;
using System.Text;
using NewforTest.Core;

// Writes the soak test as an EBU STL file, then checks it with the app's own parser.
//
// For every text colour on every box colour, then every text colour with the box off, in single
// height then double height: the 1, 2 and 3 line subtitles at the 9 positions in reading order
// (top left, top centre, top right, middle ..., bottom right), one every interval, each replacing
// the last; then a clear for one interval. Every subtitle says what should be on screen.
//
// --sample writes 20 subtitles instead, one of each kind: every text and box colour, box off,
// single and double height, 1, 2 and 3 lines, all 9 positions. For playouts that limit how much
// of a file they show (e.g. a demo version), and for a quick check.
//
// Usage: dotnet run --project tools/SoakStlGenerator -- [out.stl] [--sample] [--interval <seconds>] [--gap <frames>] [--cues <list.tsv>]
//   out.stl     default NewforTest-soak.stl, or NewforTest-soak-sample.stl with --sample
//   --interval  seconds from one subtitle to the next (default 5), rounded to whole frames at 25 fps
//   --gap       frames cleared before the next subtitle (default 6, the minimum FAB Subtitler's
//               file check accepts; 0 for back to back)
//   --cues      also write a tab-separated list of the cues and what each should look like

string? outPath = null;
string? cuesPath = null;
bool sample = false;
int gapFrames = 6;
double intervalSeconds = 5;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--interval":
            intervalSeconds = double.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--cues":
            cuesPath = args[++i];
            break;
        case "--gap":
            gapFrames = int.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--sample":
            sample = true;
            break;
        default:
            outPath = args[i];
            break;
    }
}

const int Fps = 25;
int intervalFrames = (int)Math.Round(intervalSeconds * Fps);
if (intervalFrames < 1 || gapFrames < 0 || gapFrames >= intervalFrames)
{
    Console.Error.WriteLine("The interval must be at least one frame (0.04 s), and the gap shorter than the interval.");
    return 2;
}

outPath ??= sample ? "NewforTest-soak-sample.stl" : "NewforTest-soak.stl";
var expected = sample ? SoakTest.BuildSample(intervalFrames) : SoakTest.Build(intervalFrames);
byte[] file = SoakTest.WriteStl(expected, intervalFrames, gapFrames, Fps, sample ? "NewforTest soak test sample" : "NewforTest soak test");
File.WriteAllBytes(outPath, file);

TimeSpan passLength = TimeSpan.FromSeconds((double)SoakTest.PassFrames(expected, intervalFrames) / Fps);
Console.WriteLine($"{outPath}: {expected.Count} subtitles, one every {intervalFrames / (double)Fps:0.###} s with a {gapFrames}-frame gap, " +
                  $"{passLength:h\\:mm\\:ss} per pass.");

if (cuesPath is not null)
{
    var tsv = new StringBuilder("Number\tIn\tText colour\tBox colour\tHeight\tPosition\tJustification\tText\n");
    foreach (var c in expected)
        tsv.Append($"{c.Number}\t{Timecode.Format(TimeSpan.FromSeconds((double)c.InFrames / Fps), Fps)}\t{c.Text}\t" +
                   $"{c.Box?.ToString() ?? "none"}\t{(c.DoubleHeight ? "double" : "single")}\t{c.Position}\t{c.Justification}\t" +
                   $"{string.Join(" | ", c.Lines)}\n");
    File.WriteAllText(cuesPath, tsv.ToString());
    Console.WriteLine($"Cue list: {cuesPath}");
}

int errors = SoakTest.Verify(SubtitleFile.Load(outPath), expected, intervalFrames - gapFrames, Fps);
Console.WriteLine(errors == 0 ? "Checked with the NewforTest STL parser: every subtitle shows as labelled." : $"{errors} problems found.");
return errors == 0 ? 0 : 1;

/// <summary>One soak test subtitle: what it is and what it says.</summary>
internal sealed record SoakCue(
    int Number, long InFrames, TeletextColor Text, TeletextColor? Box, bool DoubleHeight,
    VerticalPosition Position, Justification Justification, string[] Lines);

internal static class SoakTest
{
    private const long ProgrammeStart = 10L * 3600 * 25; // 10:00:00:00 in frames
    private const int TextFieldLength = 112;

    private static readonly TeletextColor[] TextColours =
    {
        TeletextColor.White, TeletextColor.Yellow, TeletextColor.Cyan, TeletextColor.Green,
        TeletextColor.Magenta, TeletextColor.Red, TeletextColor.Blue,
    };

    private static readonly VerticalPosition[] Positions = { VerticalPosition.Top, VerticalPosition.Middle, VerticalPosition.Lower };
    private static readonly Justification[] Justifications = { Justification.Left, Justification.Centre, Justification.Right };

    /// <summary>Every subtitle in order, with its in time (frames from 00:00:00:00).</summary>
    public static List<SoakCue> Build(int intervalFrames)
    {
        // Colour pairs: every text colour on every other box colour (black, the default, first),
        // then every text colour with the box off
        var pairs = new List<(TeletextColor Text, TeletextColor? Box)>();
        foreach (var box in new[] { TeletextColor.Black }.Concat(TextColours))
            foreach (var text in TextColours)
                if (text != box)
                    pairs.Add((text, box));
        foreach (var text in TextColours)
            pairs.Add((text, null));

        var cues = new List<SoakCue>();
        long t = ProgrammeStart + intervalFrames; // a clear first, so looping keeps the gap

        foreach (var (text, box) in pairs)
        {
            foreach (bool doubleHeight in new[] { false, true })
            {
                for (int lineCount = 1; lineCount <= 3; lineCount++)
                    foreach (var position in Positions)
                        foreach (var justification in Justifications)
                        {
                            cues.Add(new SoakCue(cues.Count + 1, t, text, box, doubleHeight, position, justification,
                                Label(text, box, doubleHeight, position, justification, lineCount)));
                            t += intervalFrames;
                        }

                t += intervalFrames; // clear after each sequence
            }
        }

        return cues;
    }

    /// <summary>
    /// 20 subtitles, one of each kind: every text colour and every box colour, box off, single and
    /// double height, 1, 2 and 3 lines, all 9 positions, and the longest one-line labels. Back to
    /// back, with a clear before the first and after the last.
    /// </summary>
    public static List<SoakCue> BuildSample(int intervalFrames)
    {
        const VerticalPosition Top = VerticalPosition.Top, Middle = VerticalPosition.Middle, Bottom = VerticalPosition.Lower;
        const Justification Left = Justification.Left, Centre = Justification.Centre, Right = Justification.Right;
        const TeletextColor White = TeletextColor.White, Yellow = TeletextColor.Yellow, Cyan = TeletextColor.Cyan,
            Green = TeletextColor.Green, Magenta = TeletextColor.Magenta, Red = TeletextColor.Red,
            Blue = TeletextColor.Blue, Black = TeletextColor.Black;

        var specs = new (TeletextColor Text, TeletextColor? Box, bool DoubleHeight, int Lines, VerticalPosition Position, Justification Justification)[]
        {
            // Single height, black box
            (White, Black, false, 1, Bottom, Left),
            (Yellow, Black, false, 2, Bottom, Centre),
            (Cyan, Black, false, 3, Bottom, Right),
            // Single height, coloured boxes
            (Red, Blue, false, 1, Top, Left),
            (Yellow, Blue, false, 2, Top, Centre),
            (White, Red, false, 3, Top, Right),
            (Magenta, Yellow, false, 1, Middle, Centre), // the longest label
            (Green, Magenta, false, 2, Middle, Left),
            (Blue, White, false, 3, Middle, Right),
            // Double height
            (White, Black, true, 1, Bottom, Right),
            (Yellow, Blue, true, 2, Bottom, Left),
            (Cyan, Red, true, 3, Bottom, Centre),
            (Yellow, Magenta, true, 1, Top, Right),     // the longest double-height label
            (White, Green, true, 2, Top, Left),
            (Red, Cyan, true, 3, Middle, Centre),
            // Box off
            (White, null, false, 1, Bottom, Centre),
            (Yellow, null, false, 2, Middle, Right),
            (Cyan, null, false, 3, Top, Left),
            (Green, null, true, 2, Bottom, Right),
            (Magenta, null, true, 1, Top, Centre),
        };

        long t = ProgrammeStart + intervalFrames;
        return specs.Select((s, i) => new SoakCue(i + 1, t + (long)intervalFrames * i, s.Text, s.Box, s.DoubleHeight,
                s.Position, s.Justification, Label(s.Text, s.Box, s.DoubleHeight, s.Position, s.Justification, s.Lines)))
            .ToList();
    }

    /// <summary>Length of one pass from the programme start, including the clears.</summary>
    public static long PassFrames(List<SoakCue> cues, int intervalFrames) =>
        cues[^1].InFrames + intervalFrames * 2 - ProgrammeStart;

    /// <summary>
    /// What a subtitle says. Three lines: "Red on Blue" / "Double height" / "Top Left"; two:
    /// "Red on Blue" / "Double height, Top Left"; one line has room only for "Red/Blue DH Top Left".
    /// </summary>
    private static string[] Label(TeletextColor text, TeletextColor? box, bool doubleHeight,
        VerticalPosition position, Justification justification, int lineCount)
    {
        string where = $"{(position == VerticalPosition.Lower ? "Bottom" : position.ToString())} {justification}";
        string colours = box is { } b ? $"{text} on {b}" : $"{text}, no box";
        string height = doubleHeight ? "Double height" : "Single height";
        string shortHeight = doubleHeight ? "DH" : "SH";

        return lineCount switch
        {
            1 => new[] { box is { } b1 ? $"{text}/{b1} {shortHeight} {where}" : $"{text}, no box, {shortHeight} {where}" },
            2 => new[] { colours, $"{height}, {where}" },
            _ => new[] { colours, height, where },
        };
    }

    /// <summary>
    /// The STL file: level-1 teletext, 25 fps, programme start 10:00:00:00. Rows use the app's own
    /// layout, left-justified with no padding; the JC field does the justification, as in broadcast
    /// files. Single-height rows leave out the normal height code (rows start in normal height), so
    /// left-justified text sits a column further left.
    /// </summary>
    public static byte[] WriteStl(List<SoakCue> cues, int intervalFrames, int gapFrames, int fps, string title)
    {
        var output = new MemoryStream();
        output.Write(Gsi(cues, intervalFrames, fps, title));

        foreach (var cue in cues)
        {
            var style = new SubtitleStyle(cue.Text, cue.DoubleHeight, Boxed: cue.Box is not null, cue.Position, Justification.Left)
            {
                BoxColor = cue.Box ?? TeletextColor.Black,
            };
            int[] rows = SubtitleLayout.GetRows(cue.Lines.Length, cue.Position);

            var text = new List<byte>(TextFieldLength);
            for (int l = 0; l < cue.Lines.Length; l++)
            {
                if (l > 0)
                    text.AddRange(Enumerable.Repeat((byte)0x8A, rows[l] - rows[l - 1])); // CR/LF per row

                var row = SubtitleRowBuilder.Build(cue.Lines[l], style).Select(b => (byte)(b & 0x7F)).ToList();
                if (!cue.DoubleHeight && row[0] == Teletext.NormalHeight)
                    row.RemoveAt(0);
                while (row.Count > 0 && row[^1] == Teletext.Space)
                    row.RemoveAt(row.Count - 1);
                text.AddRange(row);
            }

            if (text.Count > TextFieldLength)
                throw new InvalidOperationException($"Subtitle {cue.Number} needs {text.Count} bytes; the text field holds {TextFieldLength}.");
            text.AddRange(Enumerable.Repeat((byte)0x8F, TextFieldLength - text.Count)); // unused space

            var tti = new byte[128];
            tti[0] = 0;                                 // SGN: subtitle group
            tti[1] = (byte)(cue.Number & 0xFF);         // SN, little-endian
            tti[2] = (byte)(cue.Number >> 8);
            tti[3] = 0xFF;                              // EBN: the last (only) block
            tti[4] = 0;                                 // CS: not cumulative
            Timecode(cue.InFrames, fps).CopyTo(tti, 5);                  // TCI
            Timecode(cue.InFrames + intervalFrames - gapFrames, fps).CopyTo(tti, 9); // TCO: a gap before the next
            tti[13] = (byte)rows[0];                    // VP: first row
            tti[14] = (byte)(1 + Array.IndexOf(Justifications, cue.Justification)); // JC: 1 left, 2 centre, 3 right
            tti[15] = 0;                                // CF: not a comment
            text.CopyTo(tti, 16);
            output.Write(tti);
        }

        return output.ToArray();
    }

    private static byte[] Gsi(List<SoakCue> cues, int intervalFrames, int fps, string title)
    {
        var gsi = Enumerable.Repeat((byte)' ', 1024).ToArray();
        void Put(int offset, int length, string value) =>
            Encoding.ASCII.GetBytes(value.PadRight(length)[..length]).CopyTo(gsi, offset);
        string date = DateTime.UtcNow.ToString("yyMMdd", CultureInfo.InvariantCulture);
        string count = cues.Count.ToString("00000", CultureInfo.InvariantCulture);

        Put(0, 3, "850");                               // CPN: code page
        Put(3, 8, $"STL{fps}.01");                      // DFC: frame rate
        Put(11, 1, "1");                                // DSC: level-1 teletext
        Put(12, 2, "00");                               // CCT: Latin
        Put(14, 2, "09");                               // LC: English
        Put(16, 32, title);                             // OPT: programme title
        Put(80, 32, $"{intervalFrames / (double)fps:0.###} s per subtitle"); // TPT: episode title
        Put(176, 32, "NewforTest");                     // TCD: translator's contact details
        Put(224, 6, date);                              // CD: created
        Put(230, 6, date);                              // RD: revised
        Put(236, 2, "00");                              // RN: revision
        Put(238, 5, count);                             // TNB: TTI blocks
        Put(243, 5, count);                             // TNS: subtitles
        Put(248, 3, "001");                             // TNG: subtitle groups
        Put(251, 2, "40");                              // MNC: characters per row
        Put(253, 2, "23");                              // MNR: rows
        Put(255, 1, "1");                               // TCS: timecodes intended for use
        Put(256, 8, TimecodeText(ProgrammeStart, fps)); // TCP: programme start
        Put(264, 8, TimecodeText(cues[0].InFrames, fps)); // TCF: first in-cue
        Put(272, 1, "1");                               // TND: disks
        Put(273, 1, "1");                               // DSN: disk number
        Put(274, 3, "GBR");                             // CO: country of origin
        return gsi;
    }

    private static byte[] Timecode(long frames, int fps)
    {
        long seconds = frames / fps;
        return new[] { (byte)(seconds / 3600), (byte)(seconds / 60 % 60), (byte)(seconds % 60), (byte)(frames % fps) };
    }

    private static string TimecodeText(long frames, int fps) =>
        string.Concat(Timecode(frames, fps).Select(b => b.ToString("00", CultureInfo.InvariantCulture)));

    /// <summary>
    /// Plays every cue onto a teletext screen model and checks its rows, text, colours, box,
    /// height and justification, and the timing. Returns the number of problems.
    /// </summary>
    public static int Verify(SubtitleDocument doc, List<SoakCue> expected, int durationFrames, int fps)
    {
        int errors = 0;
        void Fail(string message)
        {
            if (errors++ < 25)
                Console.Error.WriteLine(message);
        }

        foreach (var warning in doc.Warnings)
            Fail($"Parser warning: {warning}");
        if (doc.Cues.Count != expected.Count)
        {
            Fail($"The parser found {doc.Cues.Count} subtitles, not {expected.Count}.");
            return errors;
        }
        if (doc.ProgrammeStart != TimeSpan.FromSeconds((double)ProgrammeStart / fps))
            Fail($"Programme start is {doc.ProgrammeStart}.");

        var page = PageNumber.Parse("888");
        for (int n = 0; n < expected.Count; n++)
        {
            var cue = doc.Cues[n];
            var want = expected[n];
            string name = $"#{want.Number} {string.Join(" | ", want.Lines)}";

            if (cue.Start != TimeSpan.FromSeconds((double)want.InFrames / fps) || cue.Duration != TimeSpan.FromSeconds((double)durationFrames / fps))
                Fail($"{name}: timed {cue.Start}-{cue.End}.");

            var screen = new TeletextScreen();
            foreach (var packet in NewforPackets.Subtitle(page, cue.Lines, cue.ApplyTo(new SubtitleStyle())))
                screen.Apply(packet);
            var cells = screen.Render();

            int[] rows = Enumerable.Range(1, 23)
                .Where(r => cells[r].Any(c => c.Char > ' ' && c.Height != CellHeight.DoubleBottom))
                .ToArray();
            int[] wantRows = SubtitleLayout.GetRows(want.Lines.Length, want.Position);
            if (!rows.SequenceEqual(wantRows))
            {
                Fail($"{name}: on rows {string.Join(",", rows)}, not {string.Join(",", wantRows)}.");
                continue;
            }

            byte fg = (byte)want.Text, bg = (byte)(want.Box ?? TeletextColor.Black);
            bool boxed = want.Box is not null;

            for (int l = 0; l < rows.Length; l++)
            {
                var row = cells[rows[l]];
                string shown = new string(row.Select(c => c.Char).ToArray()).Trim();
                if (shown != want.Lines[l])
                    Fail($"{name}: row {rows[l]} shows \"{shown}\".");

                int first = Array.FindIndex(row, c => c.Char > ' ');
                int last = Array.FindLastIndex(row, c => c.Char > ' ');
                for (int i = first; i <= last; i++)
                {
                    var cell = row[i];
                    if (cell.Background != bg || cell.Boxed != boxed ||
                        cell.Char > ' ' && (cell.Foreground != fg || cell.Height != (want.DoubleHeight ? CellHeight.DoubleTop : CellHeight.Normal)) ||
                        cell.Char > ' ' && want.DoubleHeight && cells[rows[l] + 1][i].Height != CellHeight.DoubleBottom)
                    {
                        Fail($"{name}: row {rows[l]} column {i} is {cell}.");
                        break;
                    }
                }

                if (boxed)
                {
                    // One cell of box margin each side, in the box colour
                    int boxFirst = Array.FindIndex(row, c => c.Boxed), boxLast = Array.FindLastIndex(row, c => c.Boxed);
                    if (boxFirst != first - 1 || boxLast != last + 1 || row[boxFirst].Background != bg || row[boxLast].Background != bg)
                        Fail($"{name}: row {rows[l]} box is columns {boxFirst}-{boxLast} around text {first}-{last}.");
                }

                // Left: as far left as the codes before the text allow. Right: against the right
                // edge (text to column 37 then End Box, or column 39 unboxed). Centre: within a
                // cell or two (long rows have no room to move).
                int leadCodes = (want.DoubleHeight ? 1 : 0) + (want.Box is null ? 1 : want.Box == TeletextColor.Black ? 3 : 5);
                bool placed = want.Justification switch
                {
                    Justification.Left => first == leadCodes,
                    Justification.Right => last == (boxed ? 37 : 39),
                    _ => Math.Abs(first + last - 39) <= 3,
                };
                if (!placed)
                    Fail($"{name}: row {rows[l]} text is in columns {first}-{last}.");
            }
        }

        return errors;
    }
}
