// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using NewforTest.Core;

namespace NewforTest.Tests;

public class TeletextCharacterSetTests
{
    public static TheoryData<string> SetNames => new(TeletextCharacterSet.All.Select(s => s.Name));

    private static byte[] Encode(TeletextCharacterSet set, string text, List<char>? unsupported = null) =>
        set.Encode(text, unsupported);

    [Theory]
    [InlineData('£', 0x23)]
    [InlineData('$', 0x24)]
    [InlineData('@', 0x40)]
    [InlineData('←', 0x5B)]
    [InlineData('½', 0x5C)]
    [InlineData('→', 0x5D)]
    [InlineData('↑', 0x5E)]
    [InlineData('#', 0x5F)]
    [InlineData('―', 0x60)]
    [InlineData('¼', 0x7B)]
    [InlineData('‖', 0x7C)]
    [InlineData('¾', 0x7D)]
    [InlineData('÷', 0x7E)]
    [InlineData('A', 0x41)]
    [InlineData('z', 0x7A)]
    public void English_national_option_positions(char c, int code)
    {
        Assert.True(TeletextCharacterSet.English.TryEncode(c, out byte encoded));
        Assert.Equal((byte)code, encoded);
        Assert.Equal(c, TeletextCharacterSet.English.Decode((byte)code));
    }

    [Theory]
    [InlineData('Ä', 0x5B)]
    [InlineData('Ö', 0x5C)]
    [InlineData('Ü', 0x5D)]
    [InlineData('ä', 0x7B)]
    [InlineData('ö', 0x7C)]
    [InlineData('ü', 0x7D)]
    [InlineData('ß', 0x7E)]
    [InlineData('#', 0x23)]
    public void German_national_option_positions(char c, int code) =>
        Assert.Equal(new[] { (byte)code }, Encode(TeletextCharacterSet.German, c.ToString()));

    [Theory]
    [MemberData(nameof(SetNames))]
    public void Every_code_decodes_and_encodes_back_to_itself(string name)
    {
        var set = TeletextCharacterSet.All.Single(s => s.Name == name);
        for (byte code = 0x20; code < 0x80; code++)
        {
            Assert.True(set.TryEncode(set.Decode(code), out byte encoded), $"{name} 0x{code:X2}");
            Assert.Equal(code, encoded);
        }
    }

    [Theory]
    [InlineData("It’s “quoted”…", "It's \"quoted\"...")]
    [InlineData("A – B", "A - B")]
    [InlineData("café", "cafe")] // é isn't in English: falls back to e
    [InlineData("Ångström", "Angstrom")]
    [InlineData("[sic]", "(sic)")]
    public void English_simplifies_typographic_and_accented_characters(string text, string expected)
    {
        var unsupported = new List<char>();
        Assert.Equal(expected, Decode(TeletextCharacterSet.English, Encode(TeletextCharacterSet.English, text, unsupported)));
        Assert.Empty(unsupported);
    }

    [Fact]
    public void Em_dash_uses_the_horizontal_bar_where_the_set_has_one()
    {
        Assert.Equal(new byte[] { 0x60 }, Encode(TeletextCharacterSet.English, "—"));
        Assert.Equal(new[] { (byte)'-' }, Encode(TeletextCharacterSet.German, "—"));
    }

    [Fact]
    public void Accented_letters_in_the_set_are_kept()
    {
        Assert.Equal("é", Decode(TeletextCharacterSet.French, Encode(TeletextCharacterSet.French, "é")));
        Assert.Equal("ñ", Decode(TeletextCharacterSet.PortugueseSpanish, Encode(TeletextCharacterSet.PortugueseSpanish, "ñ")));
    }

    [Fact]
    public void Characters_with_no_equivalent_become_question_marks_and_are_reported()
    {
        var unsupported = new List<char>();
        byte[] codes = Encode(TeletextCharacterSet.English, "a€b✓", unsupported);

        Assert.Equal("a?b?", Decode(TeletextCharacterSet.English, codes));
        Assert.Equal(new[] { '€', '✓' }, unsupported);
    }

    [Fact]
    public void Control_characters_never_reach_the_row_as_attributes()
    {
        byte[] codes = Encode(TeletextCharacterSet.English, "a\u000Bb\tc");
        Assert.All(codes, code => Assert.True(code >= 0x20));
    }

    [Theory]
    [InlineData("english", "English")]
    [InlineData("GERMAN", "German")]
    [InlineData("spanish", "Portuguese/Spanish")]
    [InlineData("Swedish", "Swedish/Finnish/Hungarian")]
    public void FromName_matches_any_part_ignoring_case(string name, string expected) =>
        Assert.Equal(expected, TeletextCharacterSet.FromName(name)?.Name);

    [Fact]
    public void FromName_returns_null_for_unknown() => Assert.Null(TeletextCharacterSet.FromName("klingon"));

    private static string Decode(TeletextCharacterSet set, byte[] codes) => new(codes.Select(set.Decode).ToArray());
}

public class RowLayoutTests
{
    private static int TextStart(byte[] row, string text)
    {
        var plain = row.Select(b => (char)(b & 0x7F)).ToArray();
        return new string(plain).IndexOf(text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, 10)]
    [InlineData(false, 8)]
    public void Left_justified_short_text_keeps_the_v3_4_column(bool boxed, int column) =>
        Assert.Equal(column, TextStart(SubtitleRowBuilder.Build("HELLO", new SubtitleStyle(Boxed: boxed)), "HELLO"));

    [Theory]
    [InlineData("HELLO")]
    [InlineData("A MUCH LONGER LINE OF TEXT")]
    public void Centre_puts_the_text_in_the_middle(string text)
    {
        byte[] row = SubtitleRowBuilder.Build(text, new SubtitleStyle(Justification: Justification.Centre));
        int start = TextStart(row, text);
        int before = start, after = Teletext.RowLength - start - text.Length;
        Assert.InRange(before - after, -1, 1);
    }

    [Fact]
    public void Right_mirrors_left()
    {
        // Boxed left: box starts 8 columns in, so boxed right ends 8 columns from the edge
        byte[] row = SubtitleRowBuilder.Build("HELLO", new SubtitleStyle(Justification: Justification.Right));
        int start = TextStart(row, "HELLO");
        Assert.Equal(30 - 5, start);
        Assert.Equal(Teletext.OddParity(Teletext.EndBox), row[start + 5]);
        Assert.Equal(Teletext.OddParity(Teletext.EndBox), row[start + 6]);
        Assert.All(row[(start + 7)..], b => Assert.Equal(Teletext.Padding, b));
    }

    [Fact]
    public void Long_text_uses_up_the_leading_spaces_before_failing()
    {
        string text = new('X', 34);
        byte[] row = SubtitleRowBuilder.Build(text, new SubtitleStyle(Boxed: true));

        // [height] [SB][SB] [colour] text [EB][EB]: no spaces left
        Assert.Equal(Teletext.NormalHeight, row[0] & 0x7F);
        Assert.Equal(Teletext.StartBox, row[1] & 0x7F);
        Assert.Equal(4, TextStart(row, text));
    }

    [Fact]
    public void Mid_line_colour_change_reuses_the_space_before_it()
    {
        var line = SubtitleLine.FromRuns(new[]
        {
            new TextRun("HELLO ", TeletextColor.Yellow),
            new TextRun("WORLD", TeletextColor.Cyan),
        });

        byte[] cells = SubtitleRowBuilder.EncodeText(line, new SubtitleStyle(), out var lineColor);

        Assert.Equal(TeletextColor.Yellow, lineColor);
        Assert.Equal("HELLO", new string(cells[..5].Select(b => (char)b).ToArray()));
        Assert.Equal((byte)TeletextColor.Cyan, cells[5]);
        Assert.Equal(11, cells.Length); // same length as "HELLO WORLD"
    }

    [Fact]
    public void Mid_line_colour_change_without_a_space_takes_a_cell()
    {
        var line = SubtitleLine.FromRuns(new[] { new TextRun("AB"), new TextRun("CD", TeletextColor.Red) });
        byte[] cells = SubtitleRowBuilder.EncodeText(line, new SubtitleStyle(), out var lineColor);

        Assert.Equal(TeletextColor.White, lineColor);
        Assert.Equal(new byte[] { 0x41, 0x42, (byte)TeletextColor.Red, 0x43, 0x44 }, cells);
    }

    [Fact]
    public void Run_without_colour_returns_to_the_style_colour()
    {
        var line = SubtitleLine.FromRuns(new[] { new TextRun("A ", TeletextColor.Red), new TextRun("B") });
        byte[] cells = SubtitleRowBuilder.EncodeText(line, new SubtitleStyle(Color: TeletextColor.Green), out _);
        Assert.Equal(new byte[] { 0x41, (byte)TeletextColor.Green, 0x42 }, cells);
    }

    [Fact]
    public void Line_justification_overrides_the_style()
    {
        var line = SubtitleLine.Text("HELLO") with { Justification = Justification.Centre };
        byte[] row = SubtitleRowBuilder.Build(line, new SubtitleStyle(Justification: Justification.Left));
        Assert.NotEqual(10, TextStart(row, "HELLO"));
    }

    [Fact]
    public void Raw_row_is_sent_as_authored_with_parity_and_padding()
    {
        byte[] codes = { 0x20, 0x0D, 0x0B, 0x0B, 0x03, (byte)'H', (byte)'I', 0x0A, 0x0A };
        byte[] row = SubtitleRowBuilder.Build(SubtitleLine.FromRaw(codes), new SubtitleStyle());

        Assert.Equal(codes.Select(Teletext.OddParity), row[..codes.Length]);
        Assert.All(row[codes.Length..], b => Assert.Equal(Teletext.Padding, b));
    }

    [Fact]
    public void Raw_row_ignores_the_style()
    {
        var line = SubtitleLine.FromRaw(new[] { (byte)'X' });
        Assert.Equal(
            SubtitleRowBuilder.Build(line, new SubtitleStyle()),
            SubtitleRowBuilder.Build(line, new SubtitleStyle(TeletextColor.Red, true, false, VerticalPosition.Top, Justification.Right)));
    }

    [Fact]
    public void Raw_row_can_be_re_centred()
    {
        byte[] codes = { 0x20, 0x20, 0x0B, 0x0B, (byte)'H', (byte)'I', 0x0A, 0x0A, 0x20 };
        var line = SubtitleLine.FromRaw(codes) with { Justification = Justification.Centre };

        byte[] row = SubtitleRowBuilder.Build(line, new SubtitleStyle());

        int start = Array.FindIndex(row, b => (b & 0x7F) != 0x20);
        Assert.Equal((40 - 6) / 2, start);
    }

    [Fact]
    public void Raw_row_longer_than_40_is_rejected_unless_the_excess_is_spaces()
    {
        var spaces = SubtitleLine.FromRaw(Enumerable.Repeat((byte)'X', 40).Concat(new byte[] { 0x20, 0x20 }).ToArray());
        Assert.True(SubtitleRowBuilder.TryBuild(spaces, new SubtitleStyle(), out _, out _));

        var tooLong = SubtitleLine.FromRaw(Enumerable.Repeat((byte)'X', 41).ToArray());
        Assert.False(SubtitleRowBuilder.TryBuild(tooLong, new SubtitleStyle(), out _, out var error));
        Assert.Contains("41 bytes", error);
    }
}

public class ExplicitRowLayoutTests
{
    [Fact]
    public void Lines_with_rows_use_them_regardless_of_position()
    {
        var lines = new[] { SubtitleLine.Text("A") with { Row = 19 }, SubtitleLine.Text("B") with { Row = 20 } };
        Assert.True(SubtitleLayout.TryGetRows(lines, VerticalPosition.Top, out var rows, out _));
        Assert.Equal(new[] { 19, 20 }, rows);
    }

    [Fact]
    public void Lines_without_rows_use_the_position()
    {
        var lines = SubtitleText.ToLines(new[] { "A", "B" });
        Assert.True(SubtitleLayout.TryGetRows(lines, VerticalPosition.Lower, out var rows, out _));
        Assert.Equal(new[] { 20, 22 }, rows);
    }

    [Theory]
    [InlineData(0, 1, "off screen")]
    [InlineData(23, 24, "off screen")]
    [InlineData(20, 20, "go down the screen")]
    [InlineData(21, 20, "go down the screen")]
    public void Invalid_explicit_rows_are_rejected(int first, int second, string message)
    {
        var lines = new[] { SubtitleLine.Text("A") with { Row = first }, SubtitleLine.Text("B") with { Row = second } };
        Assert.False(SubtitleLayout.TryGetRows(lines, VerticalPosition.Lower, out _, out var error));
        Assert.Contains(message, error);
    }

    [Fact]
    public void Mixing_lines_with_and_without_rows_is_rejected()
    {
        var lines = new[] { SubtitleLine.Text("A") with { Row = 20 }, SubtitleLine.Text("B") };
        Assert.False(SubtitleLayout.TryGetRows(lines, VerticalPosition.Lower, out _, out _));
    }

    [Fact]
    public void Packets_use_explicit_rows()
    {
        var lines = new[] { SubtitleLine.Text("A") with { Row = 5 }, SubtitleLine.Text("B") with { Row = 6 } };
        var packets = NewforPackets.Subtitle(PageNumber.Parse("888"), lines, new SubtitleStyle());

        var summaries = packets.Where(p => p.Type == NewforPacketType.Build)
            .Select(p => NewforPacketDecoder.Describe(p.Bytes)[0]);
        Assert.Equal(new[] { "BUILD row 5, clear screen", "BUILD row 6, add to screen" }, summaries);
    }
}
