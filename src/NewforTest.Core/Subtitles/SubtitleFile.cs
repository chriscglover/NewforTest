// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Text;

namespace NewforTest.Core;

/// <summary>Opens subtitle files of any supported format.</summary>
public static class SubtitleFile
{
    /// <summary>File extensions that can be loaded, for open-file dialogs.</summary>
    public static IReadOnlyList<string> Extensions { get; } = new[] { ".srt", ".stl" };

    /// <summary>Loads a subtitle file, choosing the parser from its contents (then its extension).</summary>
    /// <exception cref="NotSupportedException">Not a recognised subtitle format.</exception>
    /// <exception cref="FormatException">Looks like a known format but is badly damaged.</exception>
    public static SubtitleDocument Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        string name = Path.GetFileName(path);

        if (EbuStlParser.LooksLikeStl(bytes) || Path.GetExtension(path).Equals(".stl", StringComparison.OrdinalIgnoreCase))
            return EbuStlParser.Parse(bytes) with { Name = name };

        string text = DecodeText(bytes);
        if (text.Contains("-->"))
            return SrtParser.Parse(text) with { Name = name };

        throw new NotSupportedException($"{name} is not a recognised subtitle file (supported: {string.Join(", ", Extensions)}).");
    }

    /// <summary>Reads a text file; see <see cref="DecodeText"/>.</summary>
    public static string ReadText(string path) => DecodeText(File.ReadAllBytes(path));

    /// <summary>
    /// Decodes text: UTF-8 or UTF-16 if it has a byte order mark, otherwise UTF-8 if it's valid
    /// UTF-8, otherwise Windows Latin-1 (common for older subtitle files).
    /// </summary>
    public static string DecodeText(byte[] bytes)
    {
        using (var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false, throwOnInvalidBytes: true), detectEncodingFromByteOrderMarks: true))
        {
            try
            {
                return reader.ReadToEnd();
            }
            catch (DecoderFallbackException)
            {
                // not UTF-8
            }
        }

        return Encoding.Latin1.GetString(bytes);
    }
}
