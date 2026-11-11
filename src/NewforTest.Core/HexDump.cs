// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Text;

namespace NewforTest.Core;

/// <summary>Classic offset / hex / ASCII dump.</summary>
public static class HexDump
{
    /// <summary>
    /// Formats bytes as lines like <c>0000  8F C7 02 38 8C 20 20 20  20 20 0B 0B 07 20 54 C8  ...8.     ... T.</c>
    /// The ASCII column ignores the parity bit, so teletext text is readable.
    /// </summary>
    public static IEnumerable<string> Format(byte[] bytes, int bytesPerLine = 16)
    {
        if (bytesPerLine < 1)
            throw new ArgumentOutOfRangeException(nameof(bytesPerLine));

        for (int offset = 0; offset < bytes.Length; offset += bytesPerLine)
        {
            int count = Math.Min(bytesPerLine, bytes.Length - offset);
            var line = new StringBuilder();
            line.Append($"{offset:X4} ");

            for (int i = 0; i < bytesPerLine; i++)
            {
                if (i % 8 == 0)
                    line.Append(' ');
                line.Append(i < count ? $"{bytes[offset + i]:X2} " : "   ");
            }

            line.Append(' ');
            for (int i = 0; i < count; i++)
            {
                int c = bytes[offset + i] & 0x7F;
                line.Append(c is >= 0x20 and < 0x7F ? (char)c : '.');
            }

            yield return line.ToString().TrimEnd();
        }
    }
}
