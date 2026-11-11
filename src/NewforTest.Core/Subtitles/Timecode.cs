// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Globalization;
using System.Text.RegularExpressions;

namespace NewforTest.Core;

/// <summary>Converts between <see cref="TimeSpan"/> and HH:MM:SS:FF / HH:MM:SS.mmm text.</summary>
public static partial class Timecode
{
    /// <summary>Formats as HH:MM:SS:FF, rounding down to the frame.</summary>
    public static string Format(TimeSpan time, double frameRate)
    {
        string sign = time < TimeSpan.Zero ? "-" : "";
        time = time.Duration();
        long wholeSeconds = time.Ticks / TimeSpan.TicksPerSecond;
        // The small tolerance stops a frame stored as rounded-down ticks (e.g. 1/30 s) reading as the frame before
        int frame = (int)((time.Ticks % TimeSpan.TicksPerSecond) * frameRate / TimeSpan.TicksPerSecond + 1e-4);
        return $"{sign}{wholeSeconds / 3600:00}:{wholeSeconds / 60 % 60:00}:{wholeSeconds % 60:00}:{frame:00}";
    }

    /// <summary>A time from hours, minutes, seconds and frames.</summary>
    public static TimeSpan FromFrames(int hours, int minutes, int seconds, int frames, double frameRate) =>
        new TimeSpan(hours, minutes, seconds) + TimeSpan.FromTicks((long)Math.Round(frames * TimeSpan.TicksPerSecond / frameRate));

    /// <summary>
    /// Parses HH:MM:SS:FF (frames), HH:MM:SS.mmm or HH:MM:SS,mmm (milliseconds), MM:SS or plain
    /// seconds ("90", "12.5").
    /// </summary>
    public static bool TryParse(string? text, double frameRate, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var match = TimecodePattern().Match(text.Trim());
        if (!match.Success)
            return false;

        int Part(string name) => match.Groups[name].Success ? int.Parse(match.Groups[name].Value, CultureInfo.InvariantCulture) : 0;

        int hours = Part("h"), minutes = Part("m"), seconds = Part("s");
        if (minutes > 59 && match.Groups["h"].Success || seconds > 59 && match.Groups["m"].Success)
            return false;

        time = new TimeSpan(0, hours, minutes, seconds);

        if (match.Groups["f"].Success)
        {
            int frames = Part("f");
            if (frames >= Math.Ceiling(frameRate))
                return false;
            time += TimeSpan.FromTicks((long)Math.Round(frames * TimeSpan.TicksPerSecond / frameRate));
        }
        else if (match.Groups["ms"].Success)
        {
            string fraction = match.Groups["ms"].Value.PadRight(7, '0')[..7];
            time += TimeSpan.FromTicks(long.Parse(fraction, CultureInfo.InvariantCulture));
        }

        return true;
    }

    [GeneratedRegex(@"^(?:(?:(?<h>\d{1,2}):)?(?<m>\d{1,2}):)?(?<s>\d+)(?:[:;](?<f>\d{1,2})|[.,](?<ms>\d{1,7}))?$")]
    private static partial Regex TimecodePattern();
}
