// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using NewforTest.Core;

namespace NewforTest.Tests;

public class SubtitleLayoutTests
{
    [Theory]
    [InlineData(1, VerticalPosition.Lower, new[] { 22 })]
    [InlineData(2, VerticalPosition.Lower, new[] { 20, 22 })]
    [InlineData(3, VerticalPosition.Lower, new[] { 18, 20, 22 })]
    [InlineData(1, VerticalPosition.Top, new[] { 2 })]
    [InlineData(3, VerticalPosition.Top, new[] { 2, 4, 6 })]
    [InlineData(1, VerticalPosition.Middle, new[] { 11 })]
    [InlineData(2, VerticalPosition.Middle, new[] { 10, 12 })]
    [InlineData(3, VerticalPosition.Middle, new[] { 9, 11, 13 })]
    public void Rows_match_v3_4_layout(int lineCount, VerticalPosition position, int[] expected) =>
        Assert.Equal(expected, SubtitleLayout.GetRows(lineCount, position));

    [Theory]
    [InlineData(VerticalPosition.Top)]
    [InlineData(VerticalPosition.Middle)]
    [InlineData(VerticalPosition.Lower)]
    public void Every_allowed_line_count_stays_on_screen(VerticalPosition position)
    {
        for (int lines = 1; lines <= SubtitleLayout.MaxLines; lines++)
        {
            int[] rows = SubtitleLayout.GetRows(lines, position);
            Assert.InRange(rows[0], 1, SubtitleLayout.BottomRow);
            Assert.InRange(rows[^1], 1, SubtitleLayout.BottomRow);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(SubtitleLayout.MaxLines + 1)]
    public void Rejects_line_counts_that_do_not_fit(int lineCount) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SubtitleLayout.GetRows(lineCount, VerticalPosition.Lower));
}

public class SubtitleRowBuilderTests
{
    [Theory]
    [InlineData(true, 34)]
    [InlineData(false, 38)]
    public void Max_text_length_depends_on_boxing(bool boxed, int expected) =>
        Assert.Equal(expected, SubtitleRowBuilder.MaxTextLength(boxed));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Longest_allowed_text_fills_row_exactly(bool boxed)
    {
        string text = new('X', SubtitleRowBuilder.MaxTextLength(boxed));
        byte[] row = SubtitleRowBuilder.Build(text, new SubtitleStyle(Boxed: boxed));

        Assert.Equal(Teletext.RowLength, row.Length);
        Assert.DoesNotContain(Teletext.Padding, row);
        if (boxed)
            Assert.Equal(Teletext.OddParity(Teletext.EndBox), row[^1]); // end box survives (v3.4 truncated it away)
    }

    [Fact]
    public void Text_too_long_is_rejected_rather_than_cutting_off_the_end_box()
    {
        string text = new('X', SubtitleRowBuilder.MaxTextLength(boxed: true) + 1);
        var ex = Assert.Throws<ArgumentException>(() => SubtitleRowBuilder.Build(text, new SubtitleStyle(Boxed: true)));
        Assert.Contains("at most 34", ex.Message);
    }

    [Fact]
    public void FitText_shortens_only_when_needed()
    {
        Assert.Equal("SHORT", SubtitleRowBuilder.FitText("SHORT", boxed: true));
        Assert.Equal(34, SubtitleRowBuilder.FitText(new string('X', 40), boxed: true).Length);
        Assert.Equal(38, SubtitleRowBuilder.FitText(new string('X', 40), boxed: false).Length);
    }

    [Fact]
    public void Row_starts_with_height_code_then_colour_after_box()
    {
        byte[] row = SubtitleRowBuilder.Build("HI", new SubtitleStyle(TeletextColor.Yellow, DoubleHeight: true, Boxed: true));

        Assert.Equal(Teletext.DoubleHeight, row[0]);
        Assert.Equal(Teletext.StartBox, row[6]);
        Assert.Equal(Teletext.StartBox, row[7]);
        Assert.Equal(Teletext.OddParity((byte)TeletextColor.Yellow), row[8]);
    }
}
