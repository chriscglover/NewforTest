// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

namespace NewforTest.Core;

/// <summary>Built-in test subtitles and soak-test cue lists.</summary>
public static class TestSubtitles
{
    public static IReadOnlyList<IReadOnlyList<string>> All { get; } = new[]
    {
        new[] { "THIS IS A SINGLE LINE" },
        new[] { "THIS IS LINE ONE", "THIS IS LINE TWO" },
        new[] { "TOP SUBTITLE LINE", "MIDDLE SUBTITLE LINE", "BOTTOM SUBTITLE LINE" },
    };

    /// <summary>
    /// A soak test: the 1, 2 and 3 line test subtitles in turn, one every <paramref name="interval"/>,
    /// each shown for three quarters of the interval and then cleared. Play it with Loop on.
    /// </summary>
    public static SubtitleDocument CreateSoakTest(TimeSpan interval)
    {
        if (interval < TimeSpan.FromMilliseconds(100))
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "The soak test interval must be at least 0.1 s.");

        var cues = All.Select((lines, i) => new SubtitleCue(
                i + 1,
                interval * i,
                interval * i + interval * 0.75,
                SubtitleText.ToLines(lines)))
            .ToArray();

        return new SubtitleDocument("Soak test", cues, Array.Empty<string>())
        {
            Name = $"Soak test ({interval.TotalSeconds:0.###} s)",
            Duration = interval * All.Count,
        };
    }
}
