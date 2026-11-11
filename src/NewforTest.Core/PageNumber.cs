// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace NewforTest.Core;

/// <summary>
/// A teletext page number: magazine 1-8 followed by two hex digits (e.g. 888, 150, 1FF).
/// </summary>
public readonly record struct PageNumber
{
    private PageNumber(int magazine, int tens, int units)
    {
        Magazine = magazine;
        Tens = tens;
        Units = units;
    }

    public int Magazine { get; }
    public int Tens { get; }
    public int Units { get; }

    public static PageNumber Create(int magazine, int tens, int units)
    {
        if (magazine is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(magazine), magazine, "Magazine must be 1-8.");
        if (tens is < 0 or > 0xF)
            throw new ArgumentOutOfRangeException(nameof(tens), tens, "Page tens digit must be 0-F.");
        if (units is < 0 or > 0xF)
            throw new ArgumentOutOfRangeException(nameof(units), units, "Page units digit must be 0-F.");
        return new PageNumber(magazine, tens, units);
    }

    public static PageNumber Parse(string text) =>
        TryParse(text, out var page, out var error) ? page : throw new FormatException(error);

    public static bool TryParse(
        [NotNullWhen(true)] string? text,
        out PageNumber page,
        [NotNullWhen(false)] out string? error)
    {
        page = default;
        text = text?.Trim();

        if (string.IsNullOrEmpty(text) || text.Length != 3)
        {
            error = $"'{text}' is not a valid page: expected 3 characters, e.g. 888.";
            return false;
        }

        if (text[0] is < '1' or > '8')
        {
            error = $"'{text}' is not a valid page: magazine (first digit) must be 1-8.";
            return false;
        }

        if (!int.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int sub))
        {
            error = $"'{text}' is not a valid page: last two digits must be 0-9 or A-F.";
            return false;
        }

        page = new PageNumber(text[0] - '0', sub >> 4, sub & 0x0F);
        error = null;
        return true;
    }

    public override string ToString() => $"{Magazine}{Tens:X}{Units:X}";
}
