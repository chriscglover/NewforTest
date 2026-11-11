// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using NewforTest.Core;

namespace NewforTest.Tests;

public class NewforPacketDecoderTests
{
    private static IReadOnlyList<string> Describe(NewforPacket packet) => NewforPacketDecoder.Describe(packet.Bytes);

    private static IReadOnlyList<string> Describe(string hex) => NewforPacketDecoder.Describe(Convert.FromHexString(hex));

    [Theory]
    [InlineData("888")]
    [InlineData("150")]
    [InlineData("1FF")]
    public void Connect_shows_page(string page) =>
        Assert.Equal($"CONNECT page {page}", Describe(NewforPackets.Connect(PageNumber.Parse(page))).Single());

    [Fact]
    public void Simple_packets_are_named()
    {
        Assert.Equal("DISCONNECT", Describe(NewforPackets.Disconnect()).Single());
        Assert.Equal("CLEAR", Describe(NewforPackets.Clear()).Single());
        Assert.Equal("REVEAL", Describe(NewforPackets.Reveal()).Single());
    }

    [Fact]
    public void Build_shows_row_clear_bit_and_text_with_control_codes()
    {
        var style = new SubtitleStyle(TeletextColor.Yellow, DoubleHeight: true, Boxed: true);
        var packet = NewforPackets.Build(20, SubtitleRowBuilder.Build("HELLO", style), clearScreen: true);

        var lines = Describe(packet);

        Assert.Equal("BUILD row 20, clear screen", lines[0]);
        Assert.StartsWith("row 20: {DH}     {SB}{SB}{Yellow} HELLO{EB}{EB}", lines[1]);
    }

    [Fact]
    public void Build_without_clear_bit_says_add_to_screen()
    {
        var packet = NewforPackets.Build(22, SubtitleRowBuilder.Build("X", new SubtitleStyle()), clearScreen: false);
        Assert.Equal("BUILD row 22, add to screen", Describe(packet)[0]);
    }

    [Fact]
    public void Padding_byte_is_reported_as_even_parity()
    {
        // 0xA0 padding has even parity - the decoder makes that visible
        var row = SubtitleRowBuilder.Build(new string('X', 20), new SubtitleStyle(Boxed: true));
        int paddingBytes = row.Count(b => b == Teletext.Padding);

        string text = NewforPacketDecoder.DescribeRow(row);

        Assert.EndsWith($"[{paddingBytes} bytes even parity]", text);
    }

    [Fact]
    public void Row_with_all_odd_parity_has_no_parity_note()
    {
        var row = SubtitleRowBuilder.Build(new string('X', SubtitleRowBuilder.MaxTextLength(true)), new SubtitleStyle());
        Assert.DoesNotContain("parity", NewforPacketDecoder.DescribeRow(row));
    }

    [Fact]
    public void Decodes_every_packet_of_a_golden_sequence()
    {
        var packets = NewforPackets.Subtitle(
            PageNumber.Parse("801"),
            new[] { "TOP SUBTITLE LINE", "MIDDLE SUBTITLE LINE", "BOTTOM SUBTITLE LINE" },
            new SubtitleStyle(TeletextColor.Cyan, Boxed: false, Position: VerticalPosition.Top));

        var summaries = packets.Select(p => Describe(p)[0]).ToArray();

        Assert.Equal(
            new[]
            {
                "CONNECT page 801", "CLEAR",
                "BUILD row 2, clear screen", "BUILD row 4, add to screen", "BUILD row 6, add to screen",
                "REVEAL",
            },
            summaries);
    }

    [Fact]
    public void Multi_row_build_lists_each_row()
    {
        // BUILD carrying 2 rows in one packet (allowed by the spec; other software may send it)
        var row = SubtitleRowBuilder.Build("A", new SubtitleStyle());
        var bytes = new List<byte> { NewforPackets.BuildCode, Teletext.Hamming84(0x08 | 2) };
        bytes.AddRange(new[] { Teletext.Hamming84(1), Teletext.Hamming84(4) });
        bytes.AddRange(row);
        bytes.AddRange(new[] { Teletext.Hamming84(1), Teletext.Hamming84(6) });
        bytes.AddRange(row);

        var lines = NewforPacketDecoder.Describe(bytes.ToArray());

        Assert.Equal("BUILD 2 rows (20, 22), clear screen", lines[0]);
        Assert.StartsWith("row 20:", lines[1]);
        Assert.StartsWith("row 22:", lines[2]);
    }

    [Theory]
    [InlineData("", "(empty packet)")]
    [InlineData("42", "Unknown packet type 0x42 (1 bytes)")]
    [InlineData("0E00D0", "CONNECT (truncated: 3 of 5 bytes)")]
    [InlineData("0E00D0D0FF", "CONNECT (page bytes are not valid Hamming 8/4)")]
    [InlineData("0E01D0D0D0", "CONNECT page 888 (byte 2 is 0x01, expected 00)")]
    [InlineData("1000", "REVEAL (+1 unexpected bytes)")]
    [InlineData("8F", "BUILD (subtitle data byte missing or not valid Hamming 8/4)")]
    [InlineData("8FC70238", "BUILD row 22, clear screen (truncated: 4 of 44 bytes)")]
    public void Malformed_packets_are_described_not_thrown(string hex, string expected) =>
        Assert.Equal(expected, Describe(hex)[0]);
}

public class HexDumpTests
{
    [Fact]
    public void Formats_offset_hex_and_parity_stripped_ascii()
    {
        // "HI" with odd parity: H=0x48 (2 bits) -> C8, I=0x49 (3 bits) -> 49
        var lines = HexDump.Format(new byte[] { 0x8F, 0xC8, 0x49, 0x20 }).ToList();

        // 4 bytes shown, 12 empty hex slots (3 chars each) plus group/column gaps, then the ASCII
        Assert.Equal("0000  8F C8 49 20" + new string(' ', 1 + 12 * 3 + 2) + ".HI", lines.Single());
    }

    [Fact]
    public void Splits_into_16_byte_lines_with_a_gap_after_8()
    {
        var bytes = Enumerable.Range(0x41, 20).Select(i => (byte)i).ToArray();

        var lines = HexDump.Format(bytes).ToList();

        Assert.Equal(2, lines.Count);
        Assert.Equal("0000  41 42 43 44 45 46 47 48  49 4A 4B 4C 4D 4E 4F 50  ABCDEFGHIJKLMNOP", lines[0]);
        Assert.StartsWith("0010  51 52 53 54", lines[1]);
        Assert.EndsWith("QRST", lines[1]);
    }

    [Fact]
    public void Full_build_packet_line_fits_an_80_column_console_with_indent()
    {
        var packet = NewforPackets.Build(22, SubtitleRowBuilder.Build("HELLO", new SubtitleStyle()), clearScreen: true);
        Assert.All(HexDump.Format(packet.Bytes), line => Assert.True(line.Length + 5 < 80, line));
    }

    [Fact]
    public void Empty_input_gives_no_lines() => Assert.Empty(HexDump.Format(Array.Empty<byte>()));
}

public class SubtitleTextTests
{
    [Theory]
    [InlineData("ONE", new[] { "ONE" })]
    [InlineData("ONE|TWO", new[] { "ONE", "TWO" })]
    [InlineData("ONE||THREE", new[] { "ONE", "", "THREE" })]
    public void Splits_on_pipe(string text, string[] expected) => Assert.Equal(expected, SubtitleText.Split(text));

    [Fact]
    public void Join_is_the_inverse_of_Split() =>
        Assert.Equal("A|B|C", SubtitleText.Join(SubtitleText.Split("A|B|C")));

    [Fact]
    public void Accepts_lines_that_fit()
    {
        var lines = new[] { new string('X', 28), "SHORT" };
        Assert.True(SubtitleText.TryValidate(lines, new SubtitleStyle(Boxed: true), out var error));
        Assert.Null(error);
    }

    [Fact]
    public void Reports_which_line_is_too_long()
    {
        var lines = new[] { "FINE", new string('X', 29) };
        Assert.False(SubtitleText.TryValidate(lines, new SubtitleStyle(Boxed: true), out var error));
        Assert.Equal("Line 2 is 29 characters; at most 28 fit on a boxed row.", error);
    }

    [Fact]
    public void Unboxed_rows_allow_32_characters()
    {
        var lines = new[] { new string('X', 32) };
        Assert.False(SubtitleText.TryValidate(lines, new SubtitleStyle(Boxed: true), out _));
        Assert.True(SubtitleText.TryValidate(lines, new SubtitleStyle(Boxed: false), out _));
    }

    [Fact]
    public void Rejects_too_many_lines()
    {
        var lines = Enumerable.Repeat("X", SubtitleLayout.MaxLines + 1).ToArray();
        Assert.False(SubtitleText.TryValidate(lines, new SubtitleStyle(), out var error));
        Assert.Contains($"1-{SubtitleLayout.MaxLines} lines", error);
    }
}

public class HammingDecodeTests
{
    [Fact]
    public void Decodes_every_code_word_back_to_its_nibble()
    {
        for (int nibble = 0; nibble < 16; nibble++)
        {
            Assert.True(Teletext.TryDecodeHamming84(Teletext.Hamming84(nibble), out int decoded));
            Assert.Equal(nibble, decoded);
        }
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0xFF)]
    [InlineData(0x14)] // 0x15 with one bit flipped
    public void Rejects_bytes_that_are_not_code_words(int value) =>
        Assert.False(Teletext.TryDecodeHamming84((byte)value, out _));
}
