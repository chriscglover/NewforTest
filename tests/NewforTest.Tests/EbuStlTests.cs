// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Text;
using NewforTest.Core;

namespace NewforTest.Tests;

/// <summary>Builds EBU STL files byte by byte for tests.</summary>
internal sealed class StlBuilder
{
    private readonly List<byte[]> _blocks = new();

    public string Dfc { get; set; } = "STL25.01";
    public char Dsc { get; set; } = '1';
    public string Cct { get; set; } = "00";
    public string Tcp { get; set; } = "10000000";
    public string Title { get; set; } = "Test Programme";
    public int? DeclaredBlocks { get; set; }

    /// <summary>Adds one TTI block. Text is TF bytes; unused space is filled with 0x8F.</summary>
    public StlBuilder Block(
        int sn, byte[] text, string tci = "10:00:01:00", string tco = "10:00:03:00",
        int vp = 22, int jc = 0, byte ebn = 0xFF, int cs = 0, int cf = 0, int sgn = 0)
    {
        var block = new byte[128];
        block[0] = (byte)sgn;
        block[1] = (byte)(sn & 0xFF);
        block[2] = (byte)(sn >> 8);
        block[3] = ebn;
        block[4] = (byte)cs;
        Timecode(tci).CopyTo(block, 5);
        Timecode(tco).CopyTo(block, 9);
        block[13] = (byte)vp;
        block[14] = (byte)jc;
        block[15] = (byte)cf;
        Array.Fill(block, (byte)0x8F, 16, 112);
        text.CopyTo(block, 16);
        _blocks.Add(block);
        return this;
    }

    public StlBuilder Text(int sn, string text, string tci = "10:00:01:00", string tco = "10:00:03:00", int vp = 22, int jc = 0, int cs = 0, int cf = 0, int sgn = 0) =>
        Block(sn, Tf(text), tci, tco, vp, jc, cs: cs, cf: cf, sgn: sgn);

    /// <summary>
    /// TF bytes from a readable string: {DH} {SB} {EB} {NH} {Yellow} etc. for control codes,
    /// {CRLF} for 0x8A, {xx} for any hex byte; everything else as ASCII.
    /// </summary>
    public static byte[] Tf(string text)
    {
        var codes = new Dictionary<string, byte>
        {
            ["DH"] = 0x0D, ["NH"] = 0x0C, ["SB"] = 0x0B, ["EB"] = 0x0A, ["CRLF"] = 0x8A,
            ["Red"] = 1, ["Green"] = 2, ["Yellow"] = 3, ["Blue"] = 4, ["Magenta"] = 5, ["Cyan"] = 6, ["White"] = 7,
        };
        var bytes = new List<byte>();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                int close = text.IndexOf('}', i);
                string name = text[(i + 1)..close];
                bytes.Add(codes.TryGetValue(name, out byte code) ? code : Convert.FromHexString(name)[0]);
                i = close;
            }
            else
                bytes.Add((byte)text[i]);
        }
        return bytes.ToArray();
    }

    public byte[] Build()
    {
        var gsi = Enumerable.Repeat((byte)' ', 1024).ToArray();
        void Put(int offset, string value) => Encoding.ASCII.GetBytes(value).CopyTo(gsi, offset);
        Put(0, "850");
        Put(3, Dfc);
        gsi[11] = (byte)Dsc;
        Put(12, Cct);
        Put(14, "09");
        Put(16, Title);
        Put(238, (DeclaredBlocks ?? _blocks.Count).ToString("00000"));
        Put(256, Tcp);
        return gsi.Concat(_blocks.SelectMany(b => b)).ToArray();
    }

    private static byte[] Timecode(string tc) => tc.Split(':').Select(byte.Parse).ToArray();
}

public class EbuStlParserTests
{
    private static SubtitleDocument Parse(StlBuilder stl) => EbuStlParser.Parse(stl.Build());

    /// <summary>Row text as the decoder shows it, e.g. "{DH}{SB}{SB}HELLO{EB}{EB}".</summary>
    private static string Row(SubtitleLine line) =>
        NewforPacketDecoder.DescribeRow(SubtitleRowBuilder.Build(line, new SubtitleStyle())).Split("  [")[0];

    [Fact]
    public void Reads_header_timing_rows_and_text()
    {
        var doc = Parse(new StlBuilder().Text(1, "{DH}{SB}{SB}HELLO{EB}{EB}", "10:00:01:05", "10:00:03:10", vp: 20));

        Assert.Equal("EBU STL", doc.Format);
        Assert.Equal(25, doc.FrameRate);
        Assert.Equal("Test Programme", doc.Title);
        Assert.Equal(TimeSpan.FromHours(10), doc.ProgrammeStart);
        Assert.Empty(doc.Warnings);

        var cue = doc.Cues.Single();
        Assert.Equal(1, cue.Number);
        Assert.Equal(Core.Timecode.FromFrames(10, 0, 1, 5, 25), cue.Start);
        Assert.Equal(Core.Timecode.FromFrames(10, 0, 3, 10, 25), cue.End);
        Assert.Equal(20, cue.Lines.Single().Row);
        Assert.Equal("{DH}{SB}{SB}HELLO{EB}{EB}", Row(cue.Lines[0]));
    }

    [Fact]
    public void Bbc_sample_subtitle_is_sent_as_authored_and_centred()
    {
        // TTI block from the BBC EBU-TT sample's embedded STL (subtitle 809): VP 22, JC 2 (centred),
        // double height, 7 spaces, yellow, start box, text, end box
        byte[] tf = Convert.FromHexString("0D2020202020202003" + "0B0B") .Concat(Encoding.ASCII.GetBytes("Paint it brighter.")).Concat(new byte[] { 0x0A, 0x0A }).ToArray();
        var doc = Parse(new StlBuilder().Block(809, tf, "10:03:29:20", "10:03:31:07", vp: 22, jc: 2));

        var line = doc.Cues.Single().Lines.Single();
        Assert.Equal(22, line.Row);
        Assert.Equal(Justification.Centre, line.Justification);

        byte[] row = SubtitleRowBuilder.Build(line, new SubtitleStyle());
        string text = new(row.Select(b => (char)(b & 0x7F)).ToArray());
        int start = text.IndexOf("Paint", StringComparison.Ordinal);
        Assert.Equal((40 - 18) / 2, start); // centred, as the authored spacing already was
        Assert.Equal("{DH}{Yellow}{SB}{SB}Paint it brighter.{EB}{EB}", Row(line).Trim());
    }

    [Fact]
    public void Justification_zero_keeps_the_authored_spacing()
    {
        var doc = Parse(new StlBuilder().Text(1, "     {DH}{SB}{SB}HI{EB}{EB}", jc: 0));
        Assert.Equal("     {DH}{SB}{SB}HI{EB}{EB}", Row(doc.Cues[0].Lines[0]));
        Assert.Null(doc.Cues[0].Lines[0].Justification);
    }

    [Theory]
    [InlineData(1, Justification.Left)]
    [InlineData(2, Justification.Centre)]
    [InlineData(3, Justification.Right)]
    public void Justification_codes_map(int jc, Justification expected) =>
        Assert.Equal(expected, Parse(new StlBuilder().Text(1, "X", jc: jc)).Cues[0].Lines[0].Justification);

    [Fact]
    public void Each_crlf_moves_down_a_row_so_double_height_lines_are_two_apart()
    {
        var doc = Parse(new StlBuilder().Text(1, "{DH}{SB}{SB}ONE{EB}{EB}{CRLF}{CRLF}{DH}{SB}{SB}TWO{EB}{EB}", vp: 20));
        Assert.Equal(new int?[] { 20, 22 }, doc.Cues[0].Lines.Select(l => l.Row));

        doc = Parse(new StlBuilder().Text(1, "{SB}{SB}ONE{EB}{CRLF}{SB}{SB}TWO{EB}", vp: 21));
        Assert.Equal(new int?[] { 21, 22 }, doc.Cues[0].Lines.Select(l => l.Row));
    }

    [Fact]
    public void Rows_below_the_screen_are_dropped_with_a_warning()
    {
        var doc = Parse(new StlBuilder().Text(1, "A{CRLF}{CRLF}B", vp: 22));
        Assert.Equal("A", doc.Cues[0].Lines.Single().ToPlainText());
        Assert.Contains(doc.Warnings, w => w.Contains("row 24"));
    }

    [Fact]
    public void Extension_blocks_are_joined()
    {
        var stl = new StlBuilder()
            .Block(1, StlBuilder.Tf("{SB}{SB}FIRST PART "), ebn: 0x00)
            .Block(1, StlBuilder.Tf("SECOND PART{EB}"), ebn: 0xFF);

        var doc = Parse(stl);

        Assert.Equal("FIRST PART SECOND PART", doc.Cues.Single().Lines.Single().ToPlainText());
    }

    [Fact]
    public void User_data_reserved_and_comment_blocks_are_skipped()
    {
        var stl = new StlBuilder()
            .Block(1, Encoding.ASCII.GetBytes("user data"), ebn: 0xFE)
            .Block(2, Encoding.ASCII.GetBytes("reserved"), ebn: 0xF5)
            .Text(3, "A COMMENT", cf: 1)
            .Text(4, "SHOWN");

        var doc = Parse(stl);

        Assert.Equal("SHOWN", doc.Cues.Single().Text);
        Assert.Contains(doc.Warnings, w => w.Contains("1 comment subtitle"));
        Assert.Contains(doc.Warnings, w => w.Contains("1 user data block"));
    }

    [Fact]
    public void Subtitle_zero_programme_information_is_skipped()
    {
        var doc = Parse(new StlBuilder()
            .Text(0, "PROGRAMME TITLE", "00:00:00:00", "00:00:00:02")
            .Text(1, "FIRST REAL SUBTITLE"));

        Assert.Equal("FIRST REAL SUBTITLE", doc.Cues.Single().Text);
    }

    [Fact]
    public void Cumulative_subtitles_carry_earlier_rows_forward()
    {
        var doc = Parse(new StlBuilder()
            .Text(1, "ONE", "10:00:01:00", "10:00:05:00", vp: 18, cs: 1)
            .Text(2, "TWO", "10:00:02:00", "10:00:05:00", vp: 20, cs: 2)
            .Text(3, "THREE", "10:00:03:00", "10:00:05:00", vp: 22, cs: 3)
            .Text(4, "ALONE", "10:00:06:00", "10:00:07:00", vp: 22, cs: 0));

        Assert.Equal(new[] { "ONE", "ONE|TWO", "ONE|TWO|THREE", "ALONE" }, doc.Cues.Select(c => c.Text));
        Assert.Equal(new int?[] { 18, 20, 22 }, doc.Cues[2].Lines.Select(l => l.Row));
    }

    [Fact]
    public void Iso6937_accents_are_decoded_then_sent_in_the_teletext_set()
    {
        // 0xC2 = acute accent prefix, 0xC8 = diaeresis prefix; 0xA3 = £
        var doc = Parse(new StlBuilder().Block(1, StlBuilder.Tf("caf{C2}e {C8}u {A3}5")));
        var line = doc.Cues[0].Lines[0];

        Assert.Equal("café ü £5", line.ToPlainText());

        byte[] english = SubtitleRowBuilder.EncodeRaw(line.Raw!, new SubtitleStyle());
        byte[] german = SubtitleRowBuilder.EncodeRaw(line.Raw!, new SubtitleStyle { CharacterSet = TeletextCharacterSet.German });
        Assert.Equal("cafe u #5", new string(english.Select(b => (char)b).ToArray()));   // English: no é/ü; £ is 0x23
        Assert.Equal(0x7D, german[5]);                                                      // German ü
        Assert.Equal((byte)'?', german[7]);                                                 // German has no £
    }

    [Fact]
    public void Open_subtitles_become_plain_text_laid_out_by_the_style()
    {
        var stl = new StlBuilder { Dsc = '0' }.Text(1, "{80}Italic{81} line one{CRLF}line two", vp: 5);

        var doc = Parse(stl);

        var cue = doc.Cues.Single();
        Assert.Equal("Italic line one|line two", cue.Text);
        Assert.All(cue.Lines, l => Assert.False(l.IsRaw));
        Assert.All(cue.Lines, l => Assert.Null(l.Row));
        Assert.Contains(doc.Warnings, w => w.Contains("not teletext"));
    }

    [Theory]
    [InlineData("STL25.01", 25, false)]
    [InlineData("STL30.01", 30, true)]
    [InlineData("STL50.01", 50, true)]
    [InlineData("STLXX.01", 25, true)]
    public void Frame_rate_comes_from_the_disk_format_code(string dfc, double fps, bool warns)
    {
        var doc = Parse(new StlBuilder { Dfc = dfc }.Text(1, "X"));
        Assert.Equal(fps, doc.FrameRate);
        Assert.Equal(warns, doc.Warnings.Any(w => w.Contains(dfc)));
    }

    [Fact]
    public void Programme_start_far_from_the_subtitles_is_corrected()
    {
        var doc = Parse(new StlBuilder { Tcp = "00000000" }.Text(1, "X", "10:00:10:00", "10:00:12:00"));
        Assert.Equal(TimeSpan.FromHours(10) + TimeSpan.FromSeconds(8), doc.ProgrammeStart);
        Assert.Contains(doc.Warnings, w => w.Contains("Programme start"));
    }

    [Fact]
    public void Bad_timecodes_and_reversed_times_are_skipped()
    {
        var doc = Parse(new StlBuilder()
            .Text(1, "BAD FRAMES", "10:00:01:30", "10:00:02:00")
            .Text(2, "BACKWARDS", "10:00:05:00", "10:00:04:00")
            .Text(3, "GOOD"));

        Assert.Equal("GOOD", doc.Cues.Single().Text);
        Assert.Contains(doc.Warnings, w => w.Contains("Subtitle 1") && w.Contains("timecode"));
        Assert.Contains(doc.Warnings, w => w.Contains("Subtitle 2") && w.Contains("ends at or before"));
    }

    [Fact]
    public void Only_the_first_subtitle_group_is_played()
    {
        var doc = Parse(new StlBuilder().Text(1, "ENGLISH", sgn: 0).Text(2, "WELSH", sgn: 1));
        Assert.Equal("ENGLISH", doc.Cues.Single().Text);
        Assert.Contains(doc.Warnings, w => w.Contains("2 subtitle groups"));
    }

    [Fact]
    public void Header_block_count_mismatch_and_partial_blocks_are_reported()
    {
        byte[] data = new StlBuilder { DeclaredBlocks = 5 }.Text(1, "X").Build().Concat(new byte[10]).ToArray();
        var doc = EbuStlParser.Parse(data);

        Assert.Single(doc.Cues);
        Assert.Contains(doc.Warnings, w => w.Contains("says 5 TTI blocks"));
        Assert.Contains(doc.Warnings, w => w.Contains("last 10 bytes"));
    }

    [Fact]
    public void Non_stl_data_is_rejected()
    {
        Assert.Throws<FormatException>(() => EbuStlParser.Parse(new byte[100]));
        Assert.Throws<FormatException>(() => EbuStlParser.Parse(new byte[2048]));
    }

    [Fact]
    public void Every_cue_in_a_typical_file_builds_valid_packets()
    {
        var doc = Parse(new StlBuilder()
            .Text(1, "{DH}{Yellow}{SB}{SB}ONE{EB}{EB}{CRLF}{CRLF}{DH}{Cyan}{SB}{SB}TWO{EB}{EB}", vp: 20, jc: 2)
            .Text(2, "{DH}{SB}{SB}THREE{EB}{EB}", "10:00:04:00", "10:00:06:00", jc: 1));

        foreach (var cue in doc.Cues)
        {
            var packets = NewforPackets.Subtitle(PageNumber.Parse("888"), cue.Lines, cue.ApplyTo(new SubtitleStyle()));
            Assert.Equal(NewforPacketType.Reveal, packets[^1].Type);
        }
    }

    [Fact]
    public void Stl_files_are_detected_by_content_as_well_as_extension()
    {
        string path = Path.Combine(Path.GetTempPath(), $"NewforTest{Guid.NewGuid():N}.dat");
        File.WriteAllBytes(path, new StlBuilder().Text(1, "X").Build());
        try
        {
            var doc = SubtitleFile.Load(path);
            Assert.Equal("EBU STL", doc.Format);
            Assert.Equal(Path.GetFileName(path), doc.Name);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class Iso6937Tests
{
    [Theory]
    [InlineData("41", "A")]
    [InlineData("C265", "é")]
    [InlineData("C141", "À")]
    [InlineData("C863", "c")] // no c-diaeresis: accent dropped
    [InlineData("CB63", "ç")]
    [InlineData("CF73", "š")]
    [InlineData("A3", "£")]
    [InlineData("A4", "$")]
    [InlineData("24", "¤")]
    [InlineData("A9", "‘")]
    [InlineData("D5", "♪")]
    [InlineData("FB", "ß")]
    [InlineData("A0", " ")]
    public void Decodes_iso_6937(string hex, string expected) =>
        Assert.Equal(expected, Iso6937.Decode(Convert.FromHexString(hex)));

    [Fact]
    public void Undefined_bytes_become_question_marks() =>
        Assert.Equal("a?b", Iso6937.Decode(new byte[] { 0x61, 0xC9, 0x62 }[..1].Concat(new byte[] { 0x80, 0x62 }).ToArray()));
}
