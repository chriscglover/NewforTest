// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using NewforTest.Core;

namespace NewforTest.Tests;

/// <summary>
/// Complete byte sequences for known subtitles. If one fails, the bytes on the wire have
/// changed: check the change is intended before updating the expected value.
/// </summary>
/// <remarks>
/// History: the packet framing (CONNECT, CLEAR, BUILD headers and row numbers, REVEAL) was
/// captured from v3.4, whose output matched FAB Subtitler, and is unchanged. In 4.0 the row
/// layout changed at the user's request: the colour code now comes before the Start Box codes
/// (as in broadcast STL files) so the box has one cell of margin each side, and Left/Right
/// justification use the edges of the row. The decoded rows are shown above each case.
/// </remarks>
public class GoldenSequenceTests
{
    private static readonly string[] OneLine = { "THIS IS A SINGLE LINE" };
    private static readonly string[] TwoLines = { "THIS IS LINE ONE", "THIS IS LINE TWO" };
    private static readonly string[] ThreeLines = { "TOP SUBTITLE LINE", "MIDDLE SUBTITLE LINE", "BOTTOM SUBTITLE LINE" };

    public static TheoryData<string, string[], SubtitleStyle, string> Cases => new()
    {
        {
            // row 22: {NH}{White}{SB}{SB}THIS IS A SINGLE LINE{EB}{EB}
            "888", OneLine, new SubtitleStyle(TeletextColor.White, DoubleHeight: false, Boxed: true, VerticalPosition.Lower),
            "0E00D0D0D0988FC702388C070B0B54C849D32049D320C120D349CEC74C45204C49CE458A8AA0A0A0A0A0A0A0A0A0A0A0A0A010"
        },
        {
            // row 20: {DH}{Yellow}{SB}{SB}THIS IS LINE ONE{EB}{EB}
            // row 22: {DH}{Yellow}{SB}{SB}THIS IS LINE TWO{EB}{EB}
            "888", TwoLines, new SubtitleStyle(TeletextColor.Yellow, DoubleHeight: true, Boxed: true, VerticalPosition.Lower),
            "0E00D0D0D0988FC702640D830B0B54C849D32049D3204C49CE45204FCE458A8AA0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0" +
            "8F0202380D830B0B54C849D32049D3204C49CE452054574F8A8AA0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A010"
        },
        {
            // row 2: {NH}{Cyan}TOP SUBTITLE LINE    (unboxed)
            // row 4: {NH}{Cyan}MIDDLE SUBTITLE LINE
            // row 6: {NH}{Cyan}BOTTOM SUBTITLE LINE
            "801", ThreeLines, new SubtitleStyle(TeletextColor.Cyan, DoubleHeight: false, Boxed: false, VerticalPosition.Top),
            "0E00D01502988FC715498C86544FD020D3D5C25449544C45204C49CE45A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0" +
            "8F0215648C86CD49C4C44C4520D3D5C25449544C45204C49CE45A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0" +
            "8F0215388C86C24F54544FCD20D3D5C25449544C45204C49CE45A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A010"
        },
        {
            // row 10: {DH}{Red}{SB}{SB}THIS IS LINE ONE{EB}{EB}
            // row 12: {DH}{Red}{SB}{SB}THIS IS LINE TWO{EB}{EB}
            "150", TwoLines, new SubtitleStyle(TeletextColor.Red, DoubleHeight: true, Boxed: true, VerticalPosition.Middle),
            "0E00027315988FC7158C0D010B0B54C849D32049D3204C49CE45204FCE458A8AA0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0" +
            "8F0215A10D010B0B54C849D32049D3204C49CE452054574F8A8AA0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A010"
        },
        {
            // row 9:  {NH}{Green}{SB}{SB}TOP SUBTITLE LINE{EB}{EB}
            // row 11: {NH}{Green}{SB}{SB}MIDDLE SUBTITLE LINE{EB}{EB}
            // row 13: {NH}{Green}{SB}{SB}BOTTOM SUBTITLE LINE{EB}{EB}
            "888", ThreeLines, new SubtitleStyle(TeletextColor.Green, DoubleHeight: false, Boxed: true, VerticalPosition.Middle),
            "0E00D0D0D0988FC715C78C020B0B544FD020D3D5C25449544C45204C49CE458A8AA0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0A0" +
            "8F02159B8C020B0BCD49C4C44C4520D3D5C25449544C45204C49CE458A8AA0A0A0A0A0A0A0A0A0A0A0A0A0A0" +
            "8F0215B68C020B0BC24F54544FCD20D3D5C25449544C45204C49CE458A8AA0A0A0A0A0A0A0A0A0A0A0A0A0A010"
        },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Subtitle_sequence_matches(string page, string[] lines, SubtitleStyle style, string expectedHex)
    {
        var packets = NewforPackets.Subtitle(PageNumber.Parse(page), lines, style);

        Assert.Equal(expectedHex, Hex(packets));
    }

    [Fact]
    public void Packet_framing_is_unchanged_from_v3_4()
    {
        // CONNECT 888, CLEAR, BUILD header for row 22 with the clear bit (FAB sends C7), REVEAL
        var packets = NewforPackets.Subtitle(PageNumber.Parse("888"), OneLine, new SubtitleStyle());
        Assert.Equal("0E00D0D0D0", Hex(packets[0]));
        Assert.Equal("98", Hex(packets[1]));
        Assert.Equal("8FC70238", Hex(packets[2])[..8]);
        Assert.Equal(44, packets[2].Bytes.Length);
        Assert.Equal("10", Hex(packets[3]));
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
