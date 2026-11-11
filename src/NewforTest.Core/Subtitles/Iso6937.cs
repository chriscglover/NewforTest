// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Text;

namespace NewforTest.Core;

/// <summary>
/// Decodes ISO/IEC 6937 text, as used by EBU STL character code table 00 (Latin). Accented
/// letters are two bytes: a non-spacing diacritic (0xC1-0xCF) followed by the base letter.
/// Mapping as in EBU Tech 3264 and IRT's SCF (stl2stlxml).
/// </summary>
public static class Iso6937
{
    private static readonly Dictionary<byte, char> Direct = new()
    {
        [0x24] = '¤',
        [0xA4] = '$',
        [0xA6] = '#',
        [0xA8] = '¤',
        [0xA9] = '‘',
        [0xAA] = '“',
        [0xAC] = '←',
        [0xAD] = '↑',
        [0xAE] = '→',
        [0xAF] = '↓',
        [0xB4] = '×',
        [0xB8] = '÷',
        [0xB9] = '’',
        [0xBA] = '”',
        [0xD0] = '―',
        [0xD1] = '¹',
        [0xD2] = '®',
        [0xD3] = '©',
        [0xD4] = '™',
        [0xD5] = '♪',
        [0xD6] = '¬',
        [0xD7] = '¦',
        [0xDC] = '⅛',
        [0xDD] = '⅜',
        [0xDE] = '⅝',
        [0xDF] = '⅞',
        [0xE0] = 'Ω',
        [0xE1] = 'Æ',
        [0xE2] = 'Đ',
        [0xE3] = 'ª',
        [0xE4] = 'Ħ',
        [0xE6] = 'Ĳ',
        [0xE7] = 'Ŀ',
        [0xE8] = 'Ł',
        [0xE9] = 'Ø',
        [0xEA] = 'Œ',
        [0xEB] = 'º',
        [0xEC] = 'Þ',
        [0xED] = 'Ŧ',
        [0xEE] = 'Ŋ',
        [0xEF] = 'ŉ',
        [0xF0] = 'ĸ',
        [0xF1] = 'æ',
        [0xF2] = 'đ',
        [0xF3] = 'ð',
        [0xF4] = 'ħ',
        [0xF5] = 'ı',
        [0xF6] = 'ĳ',
        [0xF7] = 'ŀ',
        [0xF8] = 'ł',
        [0xF9] = 'ø',
        [0xFA] = 'œ',
        [0xFB] = 'ß',
        [0xFC] = 'þ',
        [0xFD] = 'ŧ',
        [0xFE] = 'ŋ',
        [0xFF] = '­',
    };

    // Same code point as Latin-1
    private static readonly HashSet<byte> SameAsLatin1 = new()
    {
        0xA1, 0xA2, 0xA3, 0xA5, 0xA7, 0xAB, 0xB0, 0xB1, 0xB2, 0xB3, 0xB5, 0xB6, 0xB7, 0xBB, 0xBC, 0xBD, 0xBE, 0xBF,
    };

    private static readonly Dictionary<byte, char> Diacritics = new()
    {
        [0xC1] = '̀', // grave
        [0xC2] = '́', // acute
        [0xC3] = '̂', // circumflex
        [0xC4] = '̃', // tilde
        [0xC5] = '̄', // macron
        [0xC6] = '̆', // breve
        [0xC7] = '̇', // dot above
        [0xC8] = '̈', // diaeresis
        [0xCA] = '̊', // ring
        [0xCB] = '̧', // cedilla
        [0xCD] = '̋', // double acute
        [0xCE] = '̨', // ogonek
        [0xCF] = '̌', // caron
    };

    /// <summary>
    /// Decodes one character starting at <paramref name="index"/>; returns the bytes consumed (1 or
    /// 2), or 0 if the byte isn't a printable ISO 6937 character (e.g. a control code).
    /// </summary>
    public static int DecodeChar(ReadOnlySpan<byte> bytes, int index, out char c)
    {
        byte b = bytes[index];
        c = '\0';

        if (b == 0x20 || b == 0xA0)
        {
            c = ' ';
            return 1;
        }

        if (b is > 0x20 and < 0x7F && b != 0x24)
        {
            c = (char)b;
            return 1;
        }

        if (Direct.TryGetValue(b, out c) || SameAsLatin1.Contains(b) && (c = (char)b) != 0)
            return 1;

        if (Diacritics.TryGetValue(b, out char mark))
        {
            if (index + 1 >= bytes.Length || bytes[index + 1] is < 0x20 or >= 0x7F)
            {
                c = '?'; // stray accent
                return 1;
            }

            string composed = $"{(char)bytes[index + 1]}{mark}".Normalize(NormalizationForm.FormC);
            c = composed.Length == 1 ? composed[0] : (char)bytes[index + 1]; // no such letter: drop the accent
            return 2;
        }

        return 0;
    }

    /// <summary>Decodes printable text, replacing anything that isn't a character with '?'.</summary>
    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        var text = new StringBuilder(bytes.Length);
        for (int i = 0; i < bytes.Length;)
        {
            int used = DecodeChar(bytes, i, out char c);
            text.Append(used > 0 ? c : '?');
            i += Math.Max(used, 1);
        }
        return text.ToString();
    }
}
