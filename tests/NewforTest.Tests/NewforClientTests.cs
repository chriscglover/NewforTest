// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Net;
using System.Net.Sockets;
using NewforTest.Core;

namespace NewforTest.Tests;

public class NewforPacketsTests
{
    [Fact]
    public void Build_subtitle_data_byte_encodes_clear_bit_and_row_count()
    {
        var data = SubtitleRowBuilder.Build("X", new SubtitleStyle());

        Assert.Equal(0xC7, NewforPackets.Build(22, data, clearScreen: true).Bytes[1]); // H(9): clear + 1 row (FAB sends C7)
        Assert.Equal(0x02, NewforPackets.Build(22, data, clearScreen: false).Bytes[1]); // H(1): 1 row
    }

    [Theory]
    [InlineData(22, 0x02, 0x38)] // 0x16 -> H(1) H(6)
    [InlineData(20, 0x02, 0x64)] // 0x14 -> H(1) H(4)
    [InlineData(2, 0x15, 0x49)] // 0x02 -> H(0) H(2)
    [InlineData(10, 0x15, 0x8C)] // 0x0A -> H(0) H(A)
    public void Build_row_number_is_sent_as_hex_nibbles(int row, int high, int low)
    {
        var packet = NewforPackets.Build(row, SubtitleRowBuilder.Build("X", new SubtitleStyle()), clearScreen: false);
        Assert.Equal(new[] { (byte)high, (byte)low }, packet.Bytes[2..4]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(24)]
    public void Build_rejects_rows_outside_1_to_23(int row) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NewforPackets.Build(row, new byte[Teletext.RowLength], clearScreen: false));

    [Fact]
    public void Build_rejects_wrong_length_data() =>
        Assert.Throws<ArgumentException>(() => NewforPackets.Build(22, new byte[39], clearScreen: false));

    [Fact]
    public void Subtitle_validates_every_line_before_returning_any_packets()
    {
        string tooLong = new('X', 40);
        Assert.Throws<ArgumentException>(() =>
            NewforPackets.Subtitle(PageNumber.Parse("888"), new[] { "FINE", tooLong }, new SubtitleStyle()));
    }
}

public sealed class NewforClientTests : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    public NewforClientTests() => _listener.Start();

    private int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public void Dispose() => _listener.Stop();

    [Fact]
    public void Sends_exactly_the_packet_bytes_and_raises_PacketSent()
    {
        var page = PageNumber.Parse("888");
        string[] lines = { "LINE ONE", "LINE TWO" };
        var style = new SubtitleStyle(TeletextColor.Yellow, DoubleHeight: true);
        var sent = new List<NewforPacket>();

        using (var client = new NewforClient("127.0.0.1", Port))
        {
            client.PacketSent += sent.Add;
            client.Connect();
            using var server = _listener.AcceptTcpClient();

            client.SendSubtitle(page, lines, style);
            client.Clear();
            client.Disconnect();
            Assert.False(client.IsConnected);

            var received = new MemoryStream();
            server.GetStream().CopyTo(received);

            var expected = NewforPackets.Subtitle(page, lines, style)
                .Append(NewforPackets.Clear())
                .Append(NewforPackets.Disconnect())
                .ToList();

            Assert.Equal(GoldenSequenceTests.Hex(expected), Convert.ToHexString(received.ToArray()));
            Assert.Equal(expected.Select(p => p.Type), sent.Select(p => p.Type));
        }
    }

    [Fact]
    public void Writing_when_not_connected_throws_NewforConnectionException()
    {
        using var client = new NewforClient("127.0.0.1", Port);
        Assert.False(client.IsConnected);
        Assert.Throws<NewforConnectionException>(client.Clear);
    }

    [Fact]
    public void Connection_refused_throws_NewforConnectionException()
    {
        int port = Port;
        _listener.Stop(); // nothing listening now

        using var client = new NewforClient("127.0.0.1", port);
        var ex = Assert.Throws<NewforConnectionException>(() => client.Connect(TimeSpan.FromSeconds(5)));
        Assert.Contains($"127.0.0.1:{port}", ex.Message);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public void Disconnect_when_not_connected_does_nothing()
    {
        using var client = new NewforClient("127.0.0.1", Port);
        client.Disconnect();
        Assert.False(client.IsConnected);
    }
}
