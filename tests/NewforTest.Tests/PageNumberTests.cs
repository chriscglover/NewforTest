// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using NewforTest.Core;

namespace NewforTest.Tests;

public class PageNumberTests
{
    [Theory]
    [InlineData("888", 8, 8, 8)]
    [InlineData("100", 1, 0, 0)]
    [InlineData("150", 1, 5, 0)]
    [InlineData("801", 8, 0, 1)]
    [InlineData("1FF", 1, 0xF, 0xF)]
    [InlineData("1ff", 1, 0xF, 0xF)]
    [InlineData(" 888 ", 8, 8, 8)]
    public void Parses_valid_pages(string text, int magazine, int tens, int units)
    {
        Assert.True(PageNumber.TryParse(text, out var page, out _));
        Assert.Equal((magazine, tens, units), (page.Magazine, page.Tens, page.Units));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("88")]
    [InlineData("1234")] // v3.4 silently sent this to magazine 2
    [InlineData("088")] // magazine 0
    [InlineData("999")] // magazine 9: reserved, DISCONNECT uses it
    [InlineData("8G0")]
    [InlineData("8-1")]
    [InlineData("abc")]
    public void Rejects_invalid_pages_with_a_message(string? text)
    {
        Assert.False(PageNumber.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Parse_throws_FormatException_for_invalid_page() =>
        Assert.Throws<FormatException>(() => PageNumber.Parse("999"));

    [Theory]
    [InlineData("888", "888")]
    [InlineData("1ff", "1FF")]
    public void ToString_round_trips(string text, string expected) =>
        Assert.Equal(expected, PageNumber.Parse(text).ToString());

    [Fact]
    public void Create_validates_ranges()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PageNumber.Create(0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PageNumber.Create(9, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PageNumber.Create(1, 16, 0));
        Assert.Equal("8A5", PageNumber.Create(8, 0xA, 5).ToString());
    }
}
