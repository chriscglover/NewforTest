// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

namespace NewforTest.Core;

public enum NewforPacketType
{
    Connect,
    Build,
    Reveal,
    Clear,
    Disconnect,
}

/// <summary>One Newfor packet, ready to write to the socket.</summary>
public sealed record NewforPacket(NewforPacketType Type, byte[] Bytes)
{
    public override string ToString() => $"{Type,-10} {Convert.ToHexString(Bytes)}";
}

/// <summary>
/// Builds Newfor protocol packets. Pure functions with no I/O, so every byte can be unit tested.
/// </summary>
/// <remarks>
/// Packet formats (command codes include their odd-parity bit):
/// <list type="bullet">
/// <item>CONNECT 0x0E: <c>0E 00 H(magazine) H(tens) H(units)</c> (H = Hamming 8/4). Selects the page.</item>
/// <item>BUILD 0x8F (0x0F + parity): <c>8F H(subtitle data) {H(row high nibble) H(row low nibble) [40 data bytes]}</c>.
///   Subtitle data: bit 3 = clear screen first, bits 2-0 = number of rows in the packet.
///   Row numbers are sent as hex nibbles, e.g. row 22 = 0x16 → H(1) H(6) = 02 38.</item>
/// <item>REVEAL 0x10: single byte. Displays the rows built so far.</item>
/// <item>CLEAR 0x98 (0x18 + parity): single byte. Clears the display.</item>
/// <item>DISCONNECT: a CONNECT to the illegal page 999 (<c>0E 00 C7 C7 C7</c>).</item>
/// </list>
/// </remarks>
public static class NewforPackets
{
    public const byte ConnectCode = 0x0E;
    public const byte BuildCode = 0x8F;
    public const byte RevealCode = 0x10;
    public const byte ClearCode = 0x98;

    /// <summary>Rows that may be addressed in a BUILD packet.</summary>
    public const int MinRow = 1, MaxRow = 23;

    public static NewforPacket Connect(PageNumber page) =>
        new(NewforPacketType.Connect, ConnectBytes(page.Magazine, page.Tens, page.Units));

    public static NewforPacket Disconnect() =>
        new(NewforPacketType.Disconnect, ConnectBytes(9, 9, 9));

    public static NewforPacket Reveal() => new(NewforPacketType.Reveal, new[] { RevealCode });

    public static NewforPacket Clear() => new(NewforPacketType.Clear, new[] { ClearCode });

    /// <summary>A BUILD packet carrying a single row.</summary>
    /// <param name="clearScreen">Clear the display before adding this row (set on a subtitle's first row only).</param>
    public static NewforPacket Build(int row, byte[] data, bool clearScreen)
    {
        if (row is < MinRow or > MaxRow)
            throw new ArgumentOutOfRangeException(nameof(row), row, $"Row must be {MinRow}-{MaxRow}.");
        if (data.Length != Teletext.RowLength)
            throw new ArgumentException($"Row data must be exactly {Teletext.RowLength} bytes, got {data.Length}.", nameof(data));

        const int rowCount = 1;
        int subtitleData = rowCount | (clearScreen ? 0x08 : 0x00);

        var bytes = new byte[2 + 2 + Teletext.RowLength];
        bytes[0] = BuildCode;
        bytes[1] = Teletext.Hamming84(subtitleData);
        bytes[2] = Teletext.Hamming84(row >> 4);
        bytes[3] = Teletext.Hamming84(row & 0x0F);
        data.CopyTo(bytes, 4);
        return new NewforPacket(NewforPacketType.Build, bytes);
    }

    /// <summary>
    /// The full packet sequence for one subtitle, matching FAB Subtitler:
    /// CONNECT → CLEAR → BUILD (one per row, first with the clear bit) → REVEAL.
    /// </summary>
    /// <exception cref="ArgumentException">Too many lines, or a line too long for one row.</exception>
    public static IReadOnlyList<NewforPacket> Subtitle(PageNumber page, IReadOnlyList<string> lines, SubtitleStyle style)
    {
        if (!SubtitleText.TryValidate(lines, style, out string? error))
            throw new ArgumentException(error, nameof(lines));

        int[] rows = SubtitleLayout.GetRows(lines.Count, style.Position);

        var packets = new List<NewforPacket>(lines.Count + 3) { Connect(page), Clear() };
        for (int i = 0; i < lines.Count; i++)
            packets.Add(Build(rows[i], SubtitleRowBuilder.Build(lines[i], style), clearScreen: i == 0));
        packets.Add(Reveal());

        return packets;
    }

    private static byte[] ConnectBytes(int magazine, int tens, int units) => new[]
    {
        ConnectCode,
        (byte)0x00,
        Teletext.Hamming84(magazine),
        Teletext.Hamming84(tens),
        Teletext.Hamming84(units),
    };
}
