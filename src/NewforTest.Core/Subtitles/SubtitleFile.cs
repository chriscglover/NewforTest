// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Text;

namespace NewforTest.Core;

/// <summary>Opens subtitle files of any supported format.</summary>
public static class SubtitleFile
{
    /// <summary>File extensions that can be loaded, for open-file dialogs.</summary>
    public static IReadOnlyList<string> Extensions { get; } = new[] { ".srt" };

    /// <summary>Loads a subtitle file, choosing the parser from its contents and extension.</summary>
    /// <exception cref="NotSupportedException">Not a recognised subtitle format.</exception>
    public static SubtitleDocument Load(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".srt")
            return SrtParser.Load(path);

        string text = ReadText(path);
        if (text.Contains("-->"))
            return SrtParser.Parse(text) with { Name = Path.GetFileName(path) };

        throw new NotSupportedException($"{Path.GetFileName(path)} is not a recognised subtitle file (supported: {string.Join(", ", Extensions)}).");
    }

    /// <summary>
    /// Reads a text file: UTF-8 or UTF-16 if it has a byte order mark, otherwise UTF-8 if it's
    /// valid UTF-8, otherwise Windows Latin-1 (common for older subtitle files).
    /// </summary>
    public static string ReadText(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);

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
