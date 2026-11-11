// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Numerics;

namespace NewforTest.Core;

/// <summary>Alphanumeric foreground colours (WST spacing attributes 0x01-0x07).</summary>
public enum TeletextColor : byte
{
    Red = 0x01,
    Green = 0x02,
    Yellow = 0x03,
    Blue = 0x04,
    Magenta = 0x05,
    Cyan = 0x06,
    White = 0x07,
}

/// <summary>
/// Low-level World System Teletext (WST) encoding: control codes, Hamming 8/4 and odd parity.
/// </summary>
public static class Teletext
{
    /// <summary>Number of character bytes in a teletext display row.</summary>
    public const int RowLength = 40;

    // WST spacing attributes (before parity is applied)
    public const byte EndBox = 0x0A;
    public const byte StartBox = 0x0B;
    public const byte NormalHeight = 0x0C;
    public const byte DoubleHeight = 0x0D;
    public const byte Space = 0x20;

    /// <summary>
    /// Byte used to pad rows out to 40 bytes. Note this is 0x20 with bit 7 set, which gives it
    /// EVEN parity (odd-parity space is plain 0x20). Kept as-is because v3.4 sent it and that
    /// output was verified against FAB Subtitler; see the golden-byte tests before changing it.
    /// </summary>
    public const byte Padding = 0xA0;

    // Hamming 8/4 code for each data nibble 0x0-0xF (ETS 300 706, see https://pdc.ro.nu/hamming.html)
    private static readonly byte[] Hamming84Table =
    {
        0x15, 0x02, 0x49, 0x5E, 0x64, 0x73, 0x38, 0x2F,
        0xD0, 0xC7, 0x8C, 0x9B, 0xA1, 0xB6, 0xFD, 0xEA,
    };

    /// <summary>Hamming 8/4 encodes a 4-bit value (0x0-0xF).</summary>
    public static byte Hamming84(int nibble)
    {
        if (nibble is < 0 or > 0x0F)
            throw new ArgumentOutOfRangeException(nameof(nibble), nibble, "Hamming 8/4 encodes values 0x0-0xF only.");
        return Hamming84Table[nibble];
    }

    /// <summary>
    /// Decodes a Hamming 8/4 byte. Only exact code words are accepted (no single-bit error
    /// correction), since this is for checking what we send, not receiving off-air.
    /// </summary>
    public static bool TryDecodeHamming84(byte value, out int nibble)
    {
        nibble = Array.IndexOf(Hamming84Table, value);
        return nibble >= 0;
    }

    /// <summary>True if the byte has odd parity, as every WST display byte should.</summary>
    public static bool HasOddParity(byte value) => BitOperations.PopCount(value) % 2 == 1;

    /// <summary>Sets bit 7 where needed so the byte has an odd number of 1 bits.</summary>
    public static byte OddParity(byte value)
    {
        int data = value & 0x7F;
        return (byte)(BitOperations.PopCount((uint)data) % 2 == 0 ? data | 0x80 : data);
    }
}
