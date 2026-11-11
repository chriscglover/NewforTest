// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using NewforTest.Core;

namespace NewforTest.Gui.ViewModels;

/// <summary>One row in the cue list.</summary>
public sealed partial class CueItem : ObservableObject
{
    public CueItem(SubtitleCue cue, int index, double frameRate)
    {
        Cue = cue;
        Index = index;
        Number = cue.Number.ToString();
        In = Timecode.Format(cue.Start, frameRate);
        Out = Timecode.Format(cue.End, frameRate);
        Text = cue.Text.Replace("|", "  |  ");
    }

    public SubtitleCue Cue { get; }
    public int Index { get; }
    public string Number { get; }
    public string In { get; }
    public string Out { get; }
    public string Text { get; }

    /// <summary>Why this cue can't be shown with the current style, or null if it can.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlayable), nameof(TextBrush))]
    private string? _error;

    /// <summary>On screen now.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextBrush))]
    private bool _isCurrent;

    public bool IsPlayable => Error is null;

    public IBrush TextBrush =>
        IsCurrent ? Brushes.Gold :
        IsPlayable ? Brushes.White :
        new SolidColorBrush(Color.Parse("#FF8A65"));

    public void Validate(SubtitleStyle style) =>
        Error = SubtitleText.TryValidate(Cue.Lines, Cue.ApplyTo(style), out string? error) ? null : error;
}
