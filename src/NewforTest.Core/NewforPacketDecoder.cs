// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Text;

namespace NewforTest.Core;

/// <summary>
/// Turns raw Newfor bytes back into readable text, for hex dumps and protocol debugging.
/// Works from the bytes alone, so it can also be pointed at captures from other software.
/// </summary>
public static class NewforPacketDecoder
{
    /// <summary>
    /// Describes one packet. The first line is a summary; BUILD packets add one line per row
    /// showing its text, with control codes in braces, e.g. <c>{DH} {SB}{SB}{Yellow} HELLO{EB}{EB}</c>.
    /// </summary>
    public static IReadOnlyList<string> Describe(ReadOnlySpan<byte> packet, TeletextCharacterSet? characterSet = null)
    {
        if (packet.IsEmpty)
            return new[] { "(empty packet)" };

        return packet[0] switch
        {
            NewforPackets.ConnectCode => new[] { DescribeConnect(packet) },
            NewforPackets.BuildCode or 0x0F => DescribeBuild(packet, characterSet),
            NewforPackets.RevealCode => new[] { "REVEAL" + Extra(packet, 1) },
            NewforPackets.ClearCode or 0x18 => new[] { "CLEAR" + Extra(packet, 1) },
            _ => new[] { $"Unknown packet type 0x{packet[0]:X2} ({packet.Length} bytes)" },
        };
    }

    /// <summary>
    /// Shows a row's 40 bytes as text with control codes in braces. Trailing spaces are dropped.
    /// Bytes with even parity (which a receiver would treat as errors) are counted at the end.
    /// Characters are shown as they'd appear with the given national option (default English).
    /// </summary>
    public static string DescribeRow(ReadOnlySpan<byte> data, TeletextCharacterSet? characterSet = null)
    {
        characterSet ??= TeletextCharacterSet.English;
        var text = new StringBuilder();
        int evenParity = 0;

        foreach (byte b in data)
        {
            if (!Teletext.HasOddParity(b))
                evenParity++;

            int code = b & 0x7F;
            text.Append(code >= 0x20 ? characterSet.Decode((byte)code).ToString() : $"{{{ControlName(code)}}}");
        }

        string result = text.ToString().TrimEnd();
        return evenParity == 0 ? result : $"{result}  [{evenParity} bytes even parity]";
    }

    private static string DescribeConnect(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 5)
            return $"CONNECT (truncated: {packet.Length} of 5 bytes)";

        if (!Teletext.TryDecodeHamming84(packet[2], out int magazine) ||
            !Teletext.TryDecodeHamming84(packet[3], out int tens) ||
            !Teletext.TryDecodeHamming84(packet[4], out int units))
            return "CONNECT (page bytes are not valid Hamming 8/4)" + Extra(packet, 5);

        string zero = packet[1] == 0x00 ? "" : $" (byte 2 is 0x{packet[1]:X2}, expected 00)";

        if (magazine == 9 && tens == 9 && units == 9)
            return "DISCONNECT" + zero + Extra(packet, 5);

        return $"CONNECT page {magazine}{tens:X}{units:X}" + zero + Extra(packet, 5);
    }

    private static string[] DescribeBuild(ReadOnlySpan<byte> packet, TeletextCharacterSet? characterSet)
    {
        if (packet.Length < 2 || !Teletext.TryDecodeHamming84(packet[1], out int subtitleData))
            return new[] { "BUILD (subtitle data byte missing or not valid Hamming 8/4)" };

        int rowCount = subtitleData & 0x07;
        bool clear = (subtitleData & 0x08) != 0;
        const int rowSize = 2 + Teletext.RowLength;
        int expectedLength = 2 + rowCount * rowSize;

        var lines = new List<string>(rowCount + 1);
        var rowNumbers = new List<string>();
        var rowTexts = new List<string>();

        for (int i = 0; i < rowCount; i++)
        {
            int start = 2 + i * rowSize;
            if (packet.Length < start + 2)
                break;

            string rowName = Teletext.TryDecodeHamming84(packet[start], out int high) &&
                             Teletext.TryDecodeHamming84(packet[start + 1], out int low)
                ? ((high << 4) | low).ToString()
                : "??";
            rowNumbers.Add(rowName);

            if (packet.Length < start + rowSize)
            {
                rowTexts.Add($"row {rowName,2}: (data truncated)");
                break;
            }

            rowTexts.Add($"row {rowName,2}: {DescribeRow(packet.Slice(start + 2, Teletext.RowLength), characterSet)}");
        }

        string rows = rowCount == 1 ? $"row {string.Join(", ", rowNumbers)}" : $"{rowCount} rows ({string.Join(", ", rowNumbers)})";
        string summary = $"BUILD {rows}, {(clear ? "clear screen" : "add to screen")}";

        if (packet.Length < expectedLength)
            summary += $" (truncated: {packet.Length} of {expectedLength} bytes)";
        else
            summary += Extra(packet, expectedLength);

        lines.Add(summary);
        lines.AddRange(rowTexts);
        return lines.ToArray();
    }

    private static string Extra(ReadOnlySpan<byte> packet, int expectedLength) =>
        packet.Length > expectedLength ? $" (+{packet.Length - expectedLength} unexpected bytes)" : "";

    private static string ControlName(int code) => code switch
    {
        0x00 => "Black",
        >= 0x01 and <= 0x07 => ((TeletextColor)code).ToString(),
        0x08 => "Flash",
        0x09 => "Steady",
        Teletext.EndBox => "EB",
        Teletext.StartBox => "SB",
        Teletext.NormalHeight => "NH",
        Teletext.DoubleHeight => "DH",
        0x1C => "BlackBg",
        0x1D => "NewBg",
        _ => $"0x{code:X2}",
    };
}
