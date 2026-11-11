// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Globalization;
using System.Text;

namespace NewforTest.Core;

/// <summary>
/// Reads EBU STL (EBU Tech 3264) subtitle files: a 1024-byte General Subtitle Information (GSI)
/// block followed by 128-byte Text and Timing Information (TTI) blocks.
/// </summary>
/// <remarks>
/// <para><b>Teletext files</b> (display standard 1 or 2): each row keeps its authored control
/// codes (colours, double height, boxing) and its row number (vertical position plus one per
/// CR/LF, so double-height lines separated by two CR/LFs land two rows apart). Text is decoded
/// from the file's character table and re-encoded for the inserter's teletext character set
/// when sent. Justification 0 keeps the authored layout; 1-3 re-justify left/centre/right.</para>
/// <para><b>Open subtitle files</b> (display standard 0 or blank): text only, laid out with the
/// current style.</para>
/// <para>Extension blocks are joined; user data, reserved and comment blocks are skipped;
/// cumulative subtitles carry the rows of the earlier ones in their set; "subtitle zero"
/// (programme information at 00:00:00:00) is skipped.</para>
/// </remarks>
public static class EbuStlParser
{
    private const int GsiLength = 1024;
    private const int TtiLength = 128;
    private const int TextFieldLength = 112;

    private const byte CrLf = 0x8A;
    private const byte UnusedSpace = 0x8F;

    public static SubtitleDocument Load(string path) =>
        Parse(File.ReadAllBytes(path)) with { Name = Path.GetFileName(path) };

    /// <summary>True if the bytes start like an EBU STL file.</summary>
    public static bool LooksLikeStl(ReadOnlySpan<byte> data) =>
        data.Length >= 11 && data[3] == (byte)'S' && data[4] == (byte)'T' && data[5] == (byte)'L';

    /// <exception cref="FormatException">Not an EBU STL file.</exception>
    public static SubtitleDocument Parse(byte[] data)
    {
        if (data.Length < GsiLength || !LooksLikeStl(data))
            throw new FormatException("Not an EBU STL file: it doesn't start with a valid GSI block.");

        var warnings = new List<string>();
        string Field(int offset, int length) => Encoding.Latin1.GetString(data, offset, length).Trim(' ', '\0');

        // GSI
        string dfc = Field(3, 8);
        char dsc = (char)data[11];
        string cct = Field(12, 2);
        string title = Field(16, 32);
        string tcp = Field(256, 8);
        string tnbText = Field(238, 5);

        double fps = ParseFrameRate(dfc, warnings);
        bool teletext = dsc is '1' or '2';
        if (!teletext)
            warnings.Add($"Display standard '{(dsc == ' ' ? "blank" : dsc)}' is not teletext: rows and colours are not in the file, so the current style is used.");

        if (cct is not ("00" or ""))
            warnings.Add($"Character code table {cct} ({CodeTableName(cct)}) is not supported: only Latin (00). Non-ASCII characters will be wrong.");

        int remainder = (data.Length - GsiLength) % TtiLength;
        if (remainder != 0)
            warnings.Add($"File length isn't a whole number of 128-byte TTI blocks; the last {remainder} bytes are ignored.");
        int blockCount = (data.Length - GsiLength) / TtiLength;
        if (int.TryParse(tnbText, NumberStyles.None, CultureInfo.InvariantCulture, out int tnb) && tnb != blockCount)
            warnings.Add($"Header says {tnb} TTI blocks but the file has {blockCount}.");

        // TTI blocks: join extension blocks until EBN 0xFF
        var subtitles = new List<RawSubtitle>();
        RawSubtitle? pending = null;
        int comments = 0, userData = 0;
        var groups = new HashSet<int>();

        for (int b = 0; b < blockCount; b++)
        {
            var block = data.AsSpan(GsiLength + b * TtiLength, TtiLength);
            int sgn = block[0];
            int sn = block[1] | block[2] << 8;
            byte ebn = block[3];

            if (ebn == 0xFE)
            {
                userData++;
                continue;
            }
            if (ebn is >= 0xF0 and < 0xFE)
                continue; // reserved

            if (pending is not null && pending.Number != sn)
            {
                warnings.Add($"Subtitle {pending.Number} has extension blocks but no final block; used what there was.");
                subtitles.Add(pending);
                pending = null;
            }

            pending ??= new RawSubtitle
            {
                Group = sgn,
                Number = sn,
                CumulativeStatus = block[4],
                In = (block[5], block[6], block[7], block[8]),
                Out = (block[9], block[10], block[11], block[12]),
                VerticalPosition = block[13],
                JustificationCode = block[14],
                Comment = block[15] == 1,
            };

            var tf = block.Slice(16, TextFieldLength);
            int end = tf.IndexOf(UnusedSpace);
            pending.Text.AddRange((end < 0 ? tf : tf[..end]).ToArray());

            if (ebn == 0xFF)
            {
                if (pending.Comment)
                    comments++;
                else
                {
                    subtitles.Add(pending);
                    groups.Add(sgn);
                }
                pending = null;
            }
        }

        if (pending is not null)
        {
            warnings.Add($"Subtitle {pending.Number} at the end of the file has no final block; used what there was.");
            subtitles.Add(pending);
        }

        int? playedGroup = null;
        if (groups.Count > 1)
        {
            playedGroup = subtitles[0].Group;
            warnings.Add($"The file has {groups.Count} subtitle groups (usually languages); only group {playedGroup} is played.");
        }

        // Timecodes
        TimeSpan? programmeStart = ParseTcp(tcp, fps);
        if (programmeStart is null && tcp.Length > 0)
            warnings.Add($"Programme start timecode '{tcp}' is not valid.");

        var cues = new List<SubtitleCue>();
        Dictionary<int, SubtitleLine>? cumulativeRows = null;

        foreach (var sub in subtitles.OrderBy(s => s.Number))
        {
            if (playedGroup is not null && sub.Group != playedGroup)
                continue;

            if (!TryTime(sub.In, fps, out var start) || !TryTime(sub.Out, fps, out var end))
            {
                warnings.Add($"Subtitle {sub.Number}: invalid timecode; skipped.");
                continue;
            }

            if (sub.Number == 0 && start == TimeSpan.Zero && programmeStart > TimeSpan.Zero)
                continue; // subtitle zero: programme information, not for display

            if (end <= start)
            {
                warnings.Add($"Subtitle {sub.Number}: ends at or before it starts; skipped.");
                continue;
            }

            Justification? justification = sub.JustificationCode switch
            {
                1 => Justification.Left,
                2 => Justification.Centre,
                3 => Justification.Right,
                _ => null,
            };

            List<SubtitleLine> lines;
            if (teletext)
            {
                lines = TeletextLines(sub, justification, warnings);

                // Cumulative sets build up on screen: carry earlier rows forward
                if (sub.CumulativeStatus is 2 or 3 && cumulativeRows is not null)
                {
                    foreach (var line in lines)
                        cumulativeRows[line.Row!.Value] = line;
                    lines = cumulativeRows.OrderBy(r => r.Key).Select(r => r.Value).ToList();
                }

                cumulativeRows = sub.CumulativeStatus is 1 or 2
                    ? lines.ToDictionary(l => l.Row!.Value)
                    : null;
            }
            else
            {
                lines = OpenLines(sub, justification);
            }

            if (lines.Count == 0)
                continue; // nothing visible

            cues.Add(new SubtitleCue(sub.Number, start, end, lines));
        }

        if (comments > 0)
            warnings.Add($"{comments} comment subtitle{(comments == 1 ? " was" : "s were")} not played.");
        if (userData > 0)
            warnings.Add($"{userData} user data block{(userData == 1 ? " was" : "s were")} ignored.");

        // Where playback starts: the programme start, unless that's missing or far from the subtitles
        TimeSpan startPosition = programmeStart ?? TimeSpan.Zero;
        if (cues.Count > 0 && (cues[0].Start < startPosition || cues[0].Start - startPosition > TimeSpan.FromHours(1)))
        {
            var adjusted = TimeSpan.FromTicks(Math.Max(0, (cues[0].Start - TimeSpan.FromSeconds(2)).Ticks));
            warnings.Add($"Programme start {(programmeStart is null ? "missing" : Timecode.Format(startPosition, fps))} " +
                         $"doesn't match the subtitles; playback starts at {Timecode.Format(adjusted, fps)}.");
            startPosition = adjusted;
        }

        return new SubtitleDocument("EBU STL", cues, warnings)
        {
            FrameRate = fps,
            ProgrammeStart = startPosition,
            Title = title.Length > 0 ? title : null,
        };
    }

    /// <summary>Teletext rows: one line per non-blank row, keeping control codes as exact codes.</summary>
    private static List<SubtitleLine> TeletextLines(RawSubtitle sub, Justification? justification, List<string> warnings)
    {
        var lines = new List<SubtitleLine>();
        int row = sub.VerticalPosition;
        if (row is < NewforPackets.MinRow or > NewforPackets.MaxRow)
        {
            warnings.Add($"Subtitle {sub.Number}: vertical position {row} is not a teletext row (1-23); using 20.");
            row = 20;
        }

        var current = new StringBuilder();
        void EndRow()
        {
            string raw = current.ToString();
            bool visible = raw.Any(c => !SubtitleLine.IsExactCode(c) && c != ' ');
            if (visible)
            {
                if (row > NewforPackets.MaxRow)
                    warnings.Add($"Subtitle {sub.Number}: row {row} is below the screen; dropped.");
                else
                    lines.Add(SubtitleLine.FromTeletext(raw) with { Row = row, Justification = justification });
            }
            current.Clear();
        }

        var text = sub.Text.ToArray().AsSpan();
        for (int i = 0; i < text.Length;)
        {
            byte b = text[i];
            if (b == CrLf)
            {
                EndRow();
                row++;
                i++;
            }
            else if (b < 0x20)
            {
                current.Append(SubtitleLine.ExactCode(b)); // teletext control code, as authored
                i++;
            }
            else if (b is >= 0x80 and <= 0x9F)
            {
                i++; // open-subtitle formatting codes (italics, underline, boxing): not used in teletext
            }
            else
            {
                int used = Iso6937.DecodeChar(text, i, out char c);
                current.Append(used > 0 ? c : '?');
                i += Math.Max(used, 1);
            }
        }
        EndRow();

        return lines;
    }

    /// <summary>Open subtitles: plain text lines; formatting codes are dropped.</summary>
    private static List<SubtitleLine> OpenLines(RawSubtitle sub, Justification? justification)
    {
        var lines = new List<SubtitleLine>();
        var current = new List<byte>();

        void EndLine()
        {
            string text = Iso6937.Decode(current.Where(b => b >= 0x20 && b is < 0x80 or >= 0xA0).ToArray()).Trim();
            if (text.Length > 0)
                lines.Add(SubtitleLine.Text(text) with { Justification = justification });
            current.Clear();
        }

        foreach (byte b in sub.Text)
        {
            if (b == CrLf)
                EndLine();
            else
                current.Add(b);
        }
        EndLine();

        return lines;
    }

    private static double ParseFrameRate(string dfc, List<string> warnings)
    {
        // The spec defines STL25.01 and STL30.01; some tools also write e.g. STL50.01 or STL24.01
        if (dfc.Length == 8 && dfc.EndsWith(".01", StringComparison.Ordinal) &&
            int.TryParse(dfc.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int fps) && fps is >= 20 and <= 60)
        {
            if (fps == 30)
                warnings.Add("STL30.01: timecodes are treated as 30 frames per second (29.97 fps video drifts about 3.6 s per hour).");
            else if (fps != 25)
                warnings.Add($"{dfc} is not a standard EBU disk format code; using {fps} frames per second.");
            return fps;
        }

        warnings.Add($"Unknown disk format code '{dfc}'; assuming 25 frames per second.");
        return 25;
    }

    private static TimeSpan? ParseTcp(string tcp, double fps)
    {
        if (tcp.Length != 8 || !tcp.All(char.IsAsciiDigit))
            return null;

        int Part(int i) => int.Parse(tcp.AsSpan(i, 2), CultureInfo.InvariantCulture);
        return TryTime(((byte)Part(0), (byte)Part(2), (byte)Part(4), (byte)Part(6)), fps, out var time) ? time : null;
    }

    private static bool TryTime((byte H, byte M, byte S, byte F) tc, double fps, out TimeSpan time)
    {
        time = default;
        if (tc.H > 23 || tc.M > 59 || tc.S > 59 || tc.F >= fps)
            return false;
        time = Timecode.FromFrames(tc.H, tc.M, tc.S, tc.F, fps);
        return true;
    }

    private static string CodeTableName(string cct) => cct switch
    {
        "01" => "Cyrillic",
        "02" => "Arabic",
        "03" => "Greek",
        "04" => "Hebrew",
        _ => "unknown",
    };

    private sealed class RawSubtitle
    {
        public int Group { get; init; }
        public int Number { get; init; }
        public byte CumulativeStatus { get; init; }
        public (byte H, byte M, byte S, byte F) In { get; init; }
        public (byte H, byte M, byte S, byte F) Out { get; init; }
        public int VerticalPosition { get; init; }
        public byte JustificationCode { get; init; }
        public bool Comment { get; init; }
        public List<byte> Text { get; } = new();
    }
}
