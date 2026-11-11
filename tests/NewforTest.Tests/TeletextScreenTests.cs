// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using NewforTest.Core;

namespace NewforTest.Tests;

public class TeletextScreenTests
{
    private static readonly PageNumber Page = PageNumber.Parse("888");

    private static TeletextScreen Send(TeletextScreen screen, IEnumerable<NewforPacket> packets)
    {
        foreach (var p in packets)
            screen.Apply(p);
        return screen;
    }

    private static TeletextScreen Show(string text, SubtitleStyle? style = null) =>
        Send(new TeletextScreen(), NewforPackets.Subtitle(Page, SubtitleText.Split(text), style ?? new SubtitleStyle()));

    private static string RowText(TeletextCell[] row) => new string(row.Select(c => c.Char).ToArray()).Trim();

    [Fact]
    public void Shows_a_subtitle_on_its_rows_after_reveal()
    {
        var screen = Show("HELLO|WORLD");
        var cells = screen.Render();

        Assert.Equal(Page, screen.Page);
        Assert.Equal("HELLO", RowText(cells[20]));
        Assert.Equal("WORLD", RowText(cells[22]));
        Assert.Equal("", RowText(cells[21]));
    }

    [Fact]
    public void Nothing_shows_until_reveal()
    {
        var screen = new TeletextScreen();
        var packets = NewforPackets.Subtitle(Page, new[] { "HELLO" }, new SubtitleStyle());
        Send(screen, packets.Take(packets.Count - 1)); // everything but REVEAL

        Assert.False(screen.HasContent);
        screen.Apply(packets[^1]);
        Assert.True(screen.HasContent);
    }

    [Fact]
    public void Clear_and_disconnect_blank_the_screen()
    {
        var screen = Show("HELLO");
        screen.Apply(NewforPackets.Clear());
        Assert.False(screen.HasContent);

        screen = Show("HELLO");
        screen.Apply(NewforPackets.Disconnect());
        Assert.False(screen.HasContent);
        Assert.Null(screen.Page);
    }

    [Fact]
    public void Next_subtitle_replaces_the_previous_one()
    {
        var screen = Show("ONE|TWO");
        Send(screen, NewforPackets.Subtitle(Page, new[] { "THREE" }, new SubtitleStyle()));

        var cells = screen.Render();
        Assert.Equal("THREE", RowText(cells[22]));
        Assert.Equal("", RowText(cells[20]));
    }

    [Fact]
    public void Colour_applies_after_the_colour_code()
    {
        var cells = Show("HI", new SubtitleStyle(TeletextColor.Yellow)).Render()[22];
        var h = cells.First(c => c.Char == 'H');
        Assert.Equal((byte)TeletextColor.Yellow, h.Foreground);
    }

    [Fact]
    public void Box_has_one_cell_of_margin_each_side_of_the_text()
    {
        // Start Box and End Box both take effect after their own cell (confirmed on the live
        // receiver), so with the colour before the box it covers SB2, the text, and EB1
        var row = Show("HI").Render()[22];
        int first = Array.FindIndex(row, c => c.Boxed);
        int last = Array.FindLastIndex(row, c => c.Boxed);
        int text = Array.FindIndex(row, c => c.Char == 'H');

        Assert.Equal(text - 1, first);
        Assert.Equal(text + 2, last); // "HI" then the first End Box cell
    }

    [Fact]
    public void Unboxed_text_is_not_boxed()
    {
        var row = Show("HI", new SubtitleStyle(Boxed: false)).Render()[22];
        Assert.DoesNotContain(row, c => c.Boxed);
        Assert.Contains(row, c => c.Char == 'H');
    }

    [Fact]
    public void Double_height_fills_the_row_below_with_lower_halves()
    {
        var cells = Show("HI", new SubtitleStyle(DoubleHeight: true)).Render();

        int col = Array.FindIndex(cells[22], c => c.Char == 'H');
        Assert.Equal(CellHeight.DoubleTop, cells[22][col].Height);
        Assert.Equal(CellHeight.DoubleBottom, cells[23][col].Height);
        Assert.Equal('H', cells[23][col].Char);
        Assert.True(cells[23][col].Boxed);
    }

    [Fact]
    public void New_background_uses_the_current_foreground_colour()
    {
        // As in the IRT "black on white" test file: {White}{NewBg}{Black} text
        var line = SubtitleLine.FromRaw(new byte[] { 0x07, 0x1D, 0x00, 0x0B, 0x0B, (byte)'X', 0x0A, 0x0A });
        var row = TeletextScreen.DecodeRow(SubtitleRowBuilder.Build(line, new SubtitleStyle()));

        var x = row.First(c => c.Char == 'X');
        Assert.Equal(0, x.Foreground);
        Assert.Equal(7, x.Background);
    }

    [Fact]
    public void Characters_follow_the_national_option()
    {
        var screen = Show("£5");
        Assert.Equal("£5", RowText(screen.Render()[22]));

        screen.CharacterSet = TeletextCharacterSet.German;
        Assert.Equal("#5", RowText(screen.Render()[22])); // the same code is # in German
    }

    [Fact]
    public void Changed_is_raised_on_reveal_and_clear()
    {
        var screen = new TeletextScreen();
        int changes = 0;
        screen.Changed += () => changes++;

        Send(screen, NewforPackets.Subtitle(Page, new[] { "X" }, new SubtitleStyle()));
        Assert.True(changes >= 2); // CLEAR then REVEAL
    }

    [Fact]
    public void Malformed_packets_are_ignored()
    {
        var screen = new TeletextScreen();
        screen.Apply(Array.Empty<byte>());
        screen.Apply(new byte[] { 0x8F });
        screen.Apply(new byte[] { 0x8F, 0xC7, 0x02 });
        screen.Apply(new byte[] { 0x0E, 0x00 });
        Assert.False(screen.HasContent);
    }
}

public class OfflineOutputTests
{
    [Fact]
    public void Raises_PacketSent_for_each_packet()
    {
        var output = new OfflineOutput();
        var sent = new List<NewforPacketType>();
        output.PacketSent += p => sent.Add(p.Type);

        output.Send(NewforPackets.Subtitle(PageNumber.Parse("888"), new[] { "A" }, new SubtitleStyle()));

        Assert.Equal(new[] { NewforPacketType.Connect, NewforPacketType.Clear, NewforPacketType.Build, NewforPacketType.Reveal }, sent);
    }

    [Fact]
    public void Player_can_rehearse_into_a_preview_without_a_connection()
    {
        var output = new OfflineOutput();
        var screen = new TeletextScreen();
        output.PacketSent += screen.Apply;

        var doc = new SubtitleDocument("Test", new[]
        {
            new SubtitleCue(1, TimeSpan.Zero, TimeSpan.FromSeconds(10), SubtitleText.ToLines(new[] { "REHEARSAL" })),
        }, Array.Empty<string>());

        using var player = new SubtitlePlayer(output, doc, PageNumber.Parse("888"), new SubtitleStyle());
        player.Play();
        Assert.True(SpinWait.SpinUntil(() => player.CurrentCueIndex == 0, 2000));
        player.Stop();

        Assert.False(screen.HasContent);
    }
}
