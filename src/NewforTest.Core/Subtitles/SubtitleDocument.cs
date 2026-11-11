// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

namespace NewforTest.Core;

/// <summary>One timed subtitle from a file.</summary>
public sealed record SubtitleCue(int Number, TimeSpan Start, TimeSpan End, IReadOnlyList<SubtitleLine> Lines)
{
    /// <summary>Overrides the style's vertical position (e.g. SRT <c>{\an8}</c>).</summary>
    public VerticalPosition? Position { get; init; }

    /// <summary>Overrides the style's double height (e.g. from an EBU STL file).</summary>
    public bool? DoubleHeight { get; init; }

    /// <summary>Overrides the style's boxing (e.g. from an EBU STL file).</summary>
    public bool? Boxed { get; init; }

    public TimeSpan Duration => End - Start;

    /// <summary>The playback style for this cue: the user's style with this cue's overrides.</summary>
    public SubtitleStyle ApplyTo(SubtitleStyle style) => style with
    {
        Position = Position ?? style.Position,
        DoubleHeight = DoubleHeight ?? style.DoubleHeight,
        Boxed = Boxed ?? style.Boxed,
    };

    /// <summary>The cue's text with '|' between lines.</summary>
    public string Text => SubtitleText.Join(Lines);

    public override string ToString() => $"#{Number} {Start} {Text}";
}

/// <summary>A parsed subtitle file.</summary>
public sealed record SubtitleDocument(string Format, IReadOnlyList<SubtitleCue> Cues, IReadOnlyList<string> Warnings)
{
    /// <summary>File name (no folder), or null if not loaded from a file.</summary>
    public string? Name { get; init; }

    /// <summary>Frame rate used for timecodes. SRT has none, so 25 is assumed.</summary>
    public double FrameRate { get; init; } = 25;

    /// <summary>
    /// Where playback starts by default. Zero for SRT; for EBU STL the programme start
    /// timecode, which is often 10:00:00:00.
    /// </summary>
    public TimeSpan ProgrammeStart { get; init; }

    public string? Title { get; init; }

    /// <summary>
    /// Length of the programme from <see cref="ProgrammeStart"/>, if known. A looping player
    /// waits until then before starting again; otherwise it loops straight after the last cue.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    public override string ToString() => $"{Name ?? Format} ({Cues.Count} cues)";
}
