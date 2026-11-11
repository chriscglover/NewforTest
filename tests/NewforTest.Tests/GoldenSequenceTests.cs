// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using NewforTest.Core;

namespace NewforTest.Tests;

/// <summary>
/// Complete byte sequences captured from v3.4 (whose output matched FAB Subtitler) by running
/// its NewforClient against a loopback listener. If one of these fails, the bytes on the wire
/// have changed: check that the change is intended before updating the expected value.
/// </summary>
public class GoldenSequenceTests
{
    private static readonly string[] OneLine = { "THIS IS A SINGLE LINE" };
    private static readonly string[] TwoLines = { "THIS IS LINE ONE", "THIS IS LINE TWO" };
    private static readonly string[] ThreeLines = { "TOP SUBTITLE LINE", "MIDDLE SUBTITLE LINE", "BOTTOM SUBTITLE LINE" };

    public static TheoryData<string, string[], SubtitleStyle, string> Cases => new()
    {
        {
            "888", OneLine, new SubtitleStyle(TeletextColor.White, DoubleHeight: false, Boxed: true, VerticalPosition.Lower),
            "0E00D0D0D0988FC702388C20202020200B0B072054C849D32049D320C120D349CEC74C45204C49CE458A8AA0A0A0A0A0A0A010"
        },
        {
            "888", TwoLines, new SubtitleStyle(TeletextColor.Yellow, DoubleHeight: true, Boxed: true, VerticalPosition.Lower),
            "0E00D0D0D0988FC702640D20202020200B0B832054C849D32049D3204C49CE45204FCE458A8AA0A0A0A0A0A0A0A0A0A0A0A0" +
            "8F0202380D20202020200B0B832054C849D32049D3204C49CE452054574F8A8AA0A0A0A0A0A0A0A0A0A0A0A010"
        },
        {
            "801", ThreeLines, new SubtitleStyle(TeletextColor.Cyan, DoubleHeight: false, Boxed: false, VerticalPosition.Top),
            "0E00D01502988FC715498C20202020208620544FD020D3D5C25449544C45204C49CE45A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0" +
            "8F0215648C20202020208620CD49C4C44C4520D3D5C25449544C45204C49CE45A0A0A0A0A0A0A0A0A0A0A0A0" +
            "8F0215388C20202020208620C24F54544FCD20D3D5C25449544C45204C49CE45A0A0A0A0A0A0A0A0A0A0A0A010"
        },
        {
            "150", TwoLines, new SubtitleStyle(TeletextColor.Red, DoubleHeight: true, Boxed: true, VerticalPosition.Middle),
            "0E00027315988FC7158C0D20202020200B0B012054C849D32049D3204C49CE45204FCE458A8AA0A0A0A0A0A0A0A0A0A0A0A0" +
            "8F0215A10D20202020200B0B012054C849D32049D3204C49CE452054574F8A8AA0A0A0A0A0A0A0A0A0A0A0A010"
        },
        {
            "888", ThreeLines, new SubtitleStyle(TeletextColor.Green, DoubleHeight: false, Boxed: true, VerticalPosition.Middle),
            "0E00D0D0D0988FC715C78C20202020200B0B0220544FD020D3D5C25449544C45204C49CE458A8AA0A0A0A0A0A0A0A0A0A0A0" +
            "8F02159B8C20202020200B0B0220CD49C4C44C4520D3D5C25449544C45204C49CE458A8AA0A0A0A0A0A0A0A0" +
            "8F0215B68C20202020200B0B0220C24F54544FCD20D3D5C25449544C45204C49CE458A8AA0A0A0A0A0A0A0A010"
        },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Subtitle_sequence_matches_v3_4(string page, string[] lines, SubtitleStyle style, string expectedHex)
    {
        var packets = NewforPackets.Subtitle(PageNumber.Parse(page), lines, style);

        Assert.Equal(expectedHex, Hex(packets));
    }

    [Fact]
    public void Subtitle_sequence_is_connect_clear_builds_reveal()
    {
        var packets = NewforPackets.Subtitle(PageNumber.Parse("888"), ThreeLines, new SubtitleStyle());

        Assert.Equal(
            new[]
            {
                NewforPacketType.Connect, NewforPacketType.Clear,
                NewforPacketType.Build, NewforPacketType.Build, NewforPacketType.Build,
                NewforPacketType.Reveal,
            },
            packets.Select(p => p.Type));
    }

    [Fact]
    public void Clear_matches_v3_4() => Assert.Equal("98", Hex(NewforPackets.Clear()));

    [Fact]
    public void Disconnect_matches_v3_4() => Assert.Equal("0E00C7C7C7", Hex(NewforPackets.Disconnect()));

    internal static string Hex(params NewforPacket[] packets) => Hex((IEnumerable<NewforPacket>)packets);

    internal static string Hex(IEnumerable<NewforPacket> packets) =>
        Convert.ToHexString(packets.SelectMany(p => p.Bytes).ToArray());
}
