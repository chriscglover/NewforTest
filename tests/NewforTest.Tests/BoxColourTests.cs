// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using NewforTest.Core;

namespace NewforTest.Tests;

public class BoxColourTests
{
    private static SubtitleStyle Box(TeletextColor box, TeletextColor text = TeletextColor.White, bool boxed = true,
        Justification justification = Justification.Left) =>
        new SubtitleStyle(text, Boxed: boxed, Justification: justification) { BoxColor = box };

    [Fact]
    public void Black_is_the_default_and_adds_no_codes()
    {
        Assert.Equal(TeletextColor.Black, new SubtitleStyle().BoxColor);
        Assert.Equal(
            SubtitleRowBuilder.Build("HELLO", new SubtitleStyle()),
            SubtitleRowBuilder.Build("HELLO", Box(TeletextColor.Black)));
    }

    [Fact]
    public void Coloured_box_sends_box_colour_then_new_background_then_text_colour()
    {
        byte[] row = SubtitleRowBuilder.Build("HI", Box(TeletextColor.Blue, TeletextColor.Yellow));

        // [NH][Blue][NewBg][Yellow][SB][SB]HI[EB][EB]
        Assert.Equal(
            new byte[] { Teletext.NormalHeight, (byte)TeletextColor.Blue, Teletext.NewBackground, (byte)TeletextColor.Yellow,
                         Teletext.StartBox, Teletext.StartBox, (byte)'H', (byte)'I', Teletext.EndBox, Teletext.EndBox },
            row[..10].Select(b => (byte)(b & 0x7F)));
    }

    [Fact]
    public void Decoded_box_has_the_colour_and_one_cell_of_margin()
    {
        var cells = TeletextScreen.DecodeRow(SubtitleRowBuilder.Build("HI", Box(TeletextColor.Red, TeletextColor.Yellow)));

        var boxed = cells.Where(c => c.Boxed).ToArray();
        Assert.All(boxed, c => Assert.Equal((byte)TeletextColor.Red, c.Background));
        Assert.Equal(" HI ", new string(boxed.Select(c => c.Char).ToArray()));

        var h = cells.First(c => c.Char == 'H');
        Assert.Equal((byte)TeletextColor.Yellow, h.Foreground);
    }

    [Fact]
    public void Coloured_box_costs_two_cells_of_line_length()
    {
        Assert.Equal(34, SubtitleRowBuilder.MaxTextLength(Box(TeletextColor.Black)));
        Assert.Equal(32, SubtitleRowBuilder.MaxTextLength(Box(TeletextColor.Cyan)));
        Assert.Equal(38, SubtitleRowBuilder.MaxTextLength(Box(TeletextColor.Cyan, boxed: false))); // ignored unboxed

        Assert.True(SubtitleRowBuilder.TryBuild(SubtitleLine.Text(new string('X', 32)), Box(TeletextColor.Cyan), out _, out _));
        Assert.False(SubtitleRowBuilder.TryBuild(SubtitleLine.Text(new string('X', 33)), Box(TeletextColor.Cyan), out _, out var error));
        Assert.Contains("at most 32 fit on a row with a coloured box", error);
    }

    [Fact]
    public void Box_colour_is_ignored_without_a_box()
    {
        byte[] row = SubtitleRowBuilder.Build("HI", Box(TeletextColor.Blue, boxed: false));
        Assert.DoesNotContain(row, b => (b & 0x7F) == Teletext.NewBackground);
    }

    [Fact]
    public void Coloured_box_still_reaches_both_edges()
    {
        var left = TeletextScreen.DecodeRow(SubtitleRowBuilder.Build("HELLO", Box(TeletextColor.Green, justification: Justification.Left)));
        var right = TeletextScreen.DecodeRow(SubtitleRowBuilder.Build("HELLO", Box(TeletextColor.Green, justification: Justification.Right)));

        Assert.Equal(5, Array.FindIndex(left, c => c.Boxed));        // after [NH][Green][NewBg][White][SB]
        Assert.Equal(38, Array.FindLastIndex(right, c => c.Boxed));  // first End Box in column 38
    }

    [Fact]
    public void Raw_stl_rows_keep_their_authored_colours()
    {
        var line = SubtitleLine.FromRaw(new byte[] { 0x0B, 0x0B, (byte)'X', 0x0A, 0x0A });
        Assert.Equal(
            SubtitleRowBuilder.Build(line, new SubtitleStyle()),
            SubtitleRowBuilder.Build(line, Box(TeletextColor.Magenta)));
    }
}
