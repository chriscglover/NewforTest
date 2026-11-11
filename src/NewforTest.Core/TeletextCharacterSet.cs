// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Globalization;
using System.Text;

namespace NewforTest.Core;

/// <summary>
/// A teletext G0 Latin character set with one of the national option subsets (ETS 300 706,
/// table 36). The national option is configured on the inserter/page, not sent by Newfor, so
/// pick the one that matches the inserter's setting.
/// </summary>
public sealed class TeletextCharacterSet
{
    // The 13 G0 positions whose character depends on the national option
    private static readonly byte[] NationalPositions =
        { 0x23, 0x24, 0x40, 0x5B, 0x5C, 0x5D, 0x5E, 0x5F, 0x60, 0x7B, 0x7C, 0x7D, 0x7E };

    /// <summary>Typographic characters common in subtitle files, tried in order when not in the set.</summary>
    private static readonly Dictionary<char, string[]> Alternatives = new()
    {
        ['‘'] = new[] { "'" },
        ['’'] = new[] { "'" },
        ['‚'] = new[] { "'" },
        ['`'] = new[] { "'" },
        ['´'] = new[] { "'" },
        ['“'] = new[] { "\"" },
        ['”'] = new[] { "\"" },
        ['„'] = new[] { "\"" },
        ['«'] = new[] { "\"" },
        ['»'] = new[] { "\"" },
        ['…'] = new[] { "..." },
        ['–'] = new[] { "-" },
        ['—'] = new[] { "―", "-" },
        ['―'] = new[] { "—", "-" },
        [' '] = new[] { " " },
        ['\t'] = new[] { " " },
        ['['] = new[] { "(" },
        [']'] = new[] { ")" },
        ['{'] = new[] { "(" },
        ['}'] = new[] { ")" },
        ['_'] = new[] { "-" },
        ['~'] = new[] { "-" },
        ['\\'] = new[] { "/" },
        ['ß'] = new[] { "ss" },
    };

    public static readonly TeletextCharacterSet English = new("English", "£$@←½→↑#―¼‖¾÷");
    public static readonly TeletextCharacterSet French = new("French", "éïàëêùî#èâôûç");
    public static readonly TeletextCharacterSet German = new("German", "#$§ÄÖÜ^_°äöüß");
    public static readonly TeletextCharacterSet Italian = new("Italian", "£$é°ç→↑#ùàòèì");
    public static readonly TeletextCharacterSet PortugueseSpanish = new("Portuguese/Spanish", "ç$¡áéíóú¿üñèà");
    public static readonly TeletextCharacterSet SwedishFinnishHungarian = new("Swedish/Finnish/Hungarian", "#¤ÉÄÖÅÜ_éäöåü");

    public static IReadOnlyList<TeletextCharacterSet> All { get; } = new[]
    {
        English, French, German, Italian, PortugueseSpanish, SwedishFinnishHungarian,
    };

    private readonly char[] _decode = new char[128];
    private readonly Dictionary<char, byte> _encode = new();

    private TeletextCharacterSet(string name, string nationalCharacters)
    {
        Name = name;

        for (int code = 0; code < 0x20; code++)
            _decode[code] = ' '; // control codes display as spaces
        for (int code = 0x20; code < 0x7F; code++)
            _decode[code] = (char)code;
        _decode[0x7F] = '■';

        for (int i = 0; i < NationalPositions.Length; i++)
            _decode[NationalPositions[i]] = nationalCharacters[i];

        for (int code = 0x20; code < 0x80; code++)
            _encode.TryAdd(_decode[code], (byte)code);
    }

    public string Name { get; }

    public override string ToString() => Name;

    /// <summary>Finds a set by name, ignoring case; any part of a combined name works ("spanish", "swedish").</summary>
    public static TeletextCharacterSet? FromName(string name) =>
        All.FirstOrDefault(set => set.Name.Split('/').Prepend(set.Name)
            .Any(part => part.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)));

    /// <summary>The character a decoder shows for a 7-bit code (control codes show as spaces).</summary>
    public char Decode(byte code) => _decode[code & 0x7F];

    /// <summary>The 7-bit code for a character, if it's in this set exactly.</summary>
    public bool TryEncode(char c, out byte code) => _encode.TryGetValue(c, out code);

    /// <summary>
    /// Converts text to 7-bit teletext codes. Characters not in the set are replaced, in order of
    /// preference, by a typographic equivalent (curly quotes, ellipsis, dashes), the unaccented
    /// letter, or '?'. Replaced characters are added to <paramref name="unsupported"/> if given.
    /// </summary>
    public byte[] Encode(string text, ICollection<char>? unsupported = null)
    {
        var codes = new List<byte>(text.Length);

        foreach (char c in text)
        {
            if (_encode.TryGetValue(c, out byte code))
            {
                codes.Add(code);
                continue;
            }

            if (Alternatives.TryGetValue(c, out var alternatives) && TryEncodeAlternative(alternatives, codes))
                continue;

            if (TryEncodeUnaccented(c, out code))
            {
                codes.Add(code);
                continue;
            }

            unsupported?.Add(c);
            codes.Add((byte)'?');
        }

        return codes.ToArray();
    }

    private bool TryEncodeAlternative(string[] alternatives, List<byte> codes)
    {
        foreach (string alternative in alternatives)
        {
            if (alternative.All(_encode.ContainsKey))
            {
                codes.AddRange(alternative.Select(ch => _encode[ch]));
                return true;
            }
        }
        return false;
    }

    private bool TryEncodeUnaccented(char c, out byte code)
    {
        string decomposed = c.ToString().Normalize(NormalizationForm.FormD);
        char baseChar = decomposed[0];
        code = 0;
        return decomposed.Length > 1 &&
               CharUnicodeInfo.GetUnicodeCategory(decomposed[1]) == UnicodeCategory.NonSpacingMark &&
               _encode.TryGetValue(baseChar, out code);
    }
}
