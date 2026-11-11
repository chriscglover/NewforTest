// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Text;
using NewforTest.Core;

namespace NewforTest.Tests;

public class SrtParserTests
{
    private const string Simple =
        "1\r\n00:00:01,000 --> 00:00:03,500\r\nHELLO\r\nWORLD\r\n\r\n" +
        "2\r\n00:00:04,000 --> 00:00:06,000\r\nSECOND\r\n";

    [Fact]
    public void Parses_numbers_times_and_lines()
    {
        var doc = SrtParser.Parse(Simple);

        Assert.Empty(doc.Warnings);
        Assert.Equal(2, doc.Cues.Count);
        var first = doc.Cues[0];
        Assert.Equal(1, first.Number);
        Assert.Equal(TimeSpan.FromSeconds(1), first.Start);
        Assert.Equal(TimeSpan.FromSeconds(3.5), first.End);
        Assert.Equal("HELLO|WORLD", first.Text);
        Assert.Equal("SECOND", doc.Cues[1].Text);
    }

    [Fact]
    public void Accepts_lf_endings_dot_milliseconds_and_trailing_coordinates()
    {
        var doc = SrtParser.Parse("1\n00:00:01.5 --> 00:00:02.25 X1:10 X2:20\nHI\n");
        Assert.Empty(doc.Warnings);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), doc.Cues[0].Start);
        Assert.Equal(TimeSpan.FromMilliseconds(2250), doc.Cues[0].End);
    }

    [Fact]
    public void Missing_cue_numbers_and_blank_lines_between_cues_are_tolerated()
    {
        var doc = SrtParser.Parse("00:00:01,000 --> 00:00:02,000\nA\n2\n00:00:03,000 --> 00:00:04,000\nB\n00:00:05,000 --> 00:00:06,000\nC");
        Assert.Equal(new[] { "A", "B", "C" }, doc.Cues.Select(c => c.Text));
        Assert.Equal(new[] { 1, 2, 3 }, doc.Cues.Select(c => c.Number));
    }

    [Fact]
    public void Bad_blocks_are_skipped_with_a_warning()
    {
        var doc = SrtParser.Parse("1\nnot a timing\nTEXT\n\n2\n00:00:03,000 --> 00:00:02,000\nBACKWARDS\n\n3\n00:00:05,000 --> 00:00:06,000\nGOOD\n");

        Assert.Equal("GOOD", doc.Cues.Single().Text);
        Assert.Equal(2, doc.Warnings.Count);
        Assert.Contains("timing", doc.Warnings[0]);
        Assert.Contains("ends at or before it starts", doc.Warnings[1]);
    }

    [Fact]
    public void Out_of_order_cues_are_sorted_and_overlaps_reported()
    {
        var doc = SrtParser.Parse("1\n00:00:05,000 --> 00:00:06,000\nB\n\n2\n00:00:01,000 --> 00:00:05,500\nA\n");

        Assert.Equal(new[] { "A", "B" }, doc.Cues.Select(c => c.Text));
        Assert.Contains(doc.Warnings, w => w.Contains("sorted"));
        Assert.Contains(doc.Warnings, w => w.Contains("starts before cue 2 ends"));
    }

    [Fact]
    public void Italic_bold_and_unknown_tags_are_dropped_and_entities_decoded()
    {
        var doc = SrtParser.Parse("1\n00:00:01,000 --> 00:00:02,000\n<i>Tom &amp; Jerry</i> <b>say</b> &lt;hi&gt; {\\pos(10,10)}<u>now</u>\n");
        Assert.Equal("Tom & Jerry say <hi> now", doc.Cues[0].Text);
    }

    [Fact]
    public void Font_colours_become_teletext_colour_runs()
    {
        var doc = SrtParser.Parse("1\n00:00:01,000 --> 00:00:02,000\n<font color=\"yellow\">HELLO</font> <font color=#00FFFF>WORLD</font>\n");
        var runs = doc.Cues[0].Lines[0].Runs;

        Assert.Equal(new TextRun("HELLO", TeletextColor.Yellow), runs[0]);
        Assert.Equal(new TextRun(" ", null), runs[1]);
        Assert.Equal(new TextRun("WORLD", TeletextColor.Cyan), runs[2]);
    }

    [Fact]
    public void Font_colour_carries_across_lines_until_closed()
    {
        var doc = SrtParser.Parse("1\n00:00:01,000 --> 00:00:02,000\n<font color=\"red\">ONE\nTWO</font>\n");
        Assert.All(doc.Cues[0].Lines, line => Assert.Equal(TeletextColor.Red, line.Runs.Single().Color));
    }

    [Theory]
    [InlineData("{\\an8}", VerticalPosition.Top, Justification.Centre)]
    [InlineData("{\\an7}", VerticalPosition.Top, Justification.Left)]
    [InlineData("{\\an5}", VerticalPosition.Middle, Justification.Centre)]
    [InlineData("{\\an3}", VerticalPosition.Lower, Justification.Right)]
    public void An_tags_set_position_and_justification(string tag, VerticalPosition position, Justification justification)
    {
        var doc = SrtParser.Parse($"1\n00:00:01,000 --> 00:00:02,000\nA\n{tag}B\n");
        var cue = doc.Cues[0];

        Assert.Equal(position, cue.Position);
        Assert.All(cue.Lines, line => Assert.Equal(justification, line.Justification));
        Assert.Equal("A|B", cue.Text);
    }

    [Fact]
    public void Cue_without_an_tag_has_no_overrides()
    {
        var cue = SrtParser.Parse(Simple).Cues[0];
        Assert.Null(cue.Position);
        Assert.All(cue.Lines, line => Assert.Null(line.Justification));
        Assert.Equal(new SubtitleStyle(), cue.ApplyTo(new SubtitleStyle()));
    }

    [Theory]
    [InlineData("red", TeletextColor.Red)]
    [InlineData("LIME", TeletextColor.Green)]
    [InlineData("#FF0", TeletextColor.Yellow)]
    [InlineData("#0000ff", TeletextColor.Blue)]
    [InlineData("#FFFF00FF", TeletextColor.Magenta)] // with alpha
    [InlineData("#000000", TeletextColor.White)] // black text would vanish
    [InlineData("#C0C0C0", TeletextColor.White)]
    public void Html_colours_map_to_the_nearest_teletext_colour(string value, TeletextColor expected) =>
        Assert.Equal(expected, SrtParser.ParseColor(value));

    [Fact]
    public void Files_are_read_as_utf8_utf16_or_latin1()
    {
        string dir = Path.Combine(Path.GetTempPath(), "NewforTestSrt" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string body = "1\n00:00:01,000 --> 00:00:02,000\nCafé £5\n";
            var files = new Dictionary<string, byte[]>
            {
                ["utf8.srt"] = Encoding.UTF8.GetBytes(body),
                ["utf8bom.srt"] = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(body)).ToArray(),
                ["utf16.srt"] = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(body)).ToArray(),
                ["latin1.srt"] = Encoding.Latin1.GetBytes(body),
            };

            foreach (var (name, bytes) in files)
            {
                string path = Path.Combine(dir, name);
                File.WriteAllBytes(path, bytes);
                var doc = SubtitleFile.Load(path);
                Assert.Equal("Café £5", doc.Cues[0].Text);
                Assert.Equal(name, doc.Name);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Unknown_file_type_is_rejected()
    {
        string path = Path.Combine(Path.GetTempPath(), $"NewforTest{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "just some text");
        try
        {
            Assert.Throws<NotSupportedException>(() => SubtitleFile.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class TimecodeTests
{
    [Theory]
    [InlineData(0, "00:00:00:00")]
    [InlineData(1.04, "00:00:01:01")]
    [InlineData(3723.5, "01:02:03:12")]
    [InlineData(36000, "10:00:00:00")]
    public void Formats_hours_minutes_seconds_frames_at_25fps(double seconds, string expected) =>
        Assert.Equal(expected, Timecode.Format(TimeSpan.FromSeconds(seconds), 25));

    [Theory]
    [InlineData("01:02:03:12", 3723.48)]
    [InlineData("10:00:00:00", 36000)]
    [InlineData("01:02:03.500", 3723.5)]
    [InlineData("01:02:03,5", 3723.5)]
    [InlineData("02:30", 150)]
    [InlineData("90", 90)]
    [InlineData("12.25", 12.25)]
    public void Parses_common_forms(string text, double seconds)
    {
        Assert.True(Timecode.TryParse(text, 25, out var time));
        Assert.Equal(TimeSpan.FromSeconds(seconds), time);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("00:00:00:25")] // only frames 0-24 at 25 fps
    [InlineData("00:61:00")]
    [InlineData("1:2:3:4:5")]
    public void Rejects_invalid(string text) => Assert.False(Timecode.TryParse(text, 25, out _));

    [Fact]
    public void Frames_round_trip_at_25_and_30_fps()
    {
        foreach (double fps in new[] { 25.0, 30.0 })
        {
            for (int frame = 0; frame < fps; frame++)
            {
                var time = Timecode.FromFrames(10, 0, 0, frame, fps);
                Assert.Equal($"10:00:00:{frame:00}", Timecode.Format(time, fps));
            }
        }
    }
}
