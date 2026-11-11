// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Diagnostics;

namespace NewforTest.Core;

public enum PlaybackState
{
    Stopped,
    Playing,
    Paused,
}

/// <summary>A cue was sent. <see cref="Lateness"/> is how far after its start time it went out.</summary>
public sealed record CueShownEventArgs(SubtitleCue Cue, int Index, TimeSpan Lateness);

/// <summary>
/// Plays a list of cues to a <see cref="NewforClient"/> in real time, on its own thread.
/// </summary>
/// <remarks>
/// <para>Timing: the thread sleeps until about 20 ms before each event, then spins, so cues go out
/// within a millisecond or so of their time rather than the 15.6 ms Windows timer resolution.
/// Each <see cref="CueShown"/> reports the actual lateness.</para>
/// <para>A cue is sent at its start time. At its end time a CLEAR is sent, unless the next cue
/// starts within <see cref="MinimumGap"/>, in which case the next cue simply replaces it.</para>
/// <para>The page and style are read as each cue is sent, so changes take effect on the next cue.</para>
/// <para>All events are raised on the playback thread.</para>
/// </remarks>
public sealed class SubtitlePlayer : IDisposable
{
    private static readonly TimeSpan SpinThreshold = TimeSpan.FromMilliseconds(20);

    private readonly NewforClient _client;
    private readonly SubtitleCue[] _cues;
    private readonly object _sync = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _thread;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private PlaybackState _state = PlaybackState.Stopped;
    private TimeSpan _anchorPosition; // media position at _anchorClock
    private TimeSpan _anchorClock;
    private int _onScreen = -1; // index of the cue currently on screen
    private bool _disposed;

    public SubtitlePlayer(NewforClient client, SubtitleDocument document, PageNumber page, SubtitleStyle style)
    {
        _client = client;
        Document = document;
        _cues = document.Cues.OrderBy(c => c.Start).ToArray();
        Page = page;
        Style = style;
        StartPosition = document.ProgrammeStart;
        _anchorPosition = StartPosition;

        // Build the first cue's packets once now, so the first real send isn't slowed by JIT compilation
        if (_cues.Length > 0)
            SubtitleText.TryValidate(_cues[0].Lines, _cues[0].ApplyTo(style), out _);

        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Subtitle playback",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
    }

    public SubtitleDocument Document { get; }

    public IReadOnlyList<SubtitleCue> Cues => _cues;

    public PageNumber Page { get; set; }

    public SubtitleStyle Style { get; set; }

    /// <summary>Start again from <see cref="StartPosition"/> after the last cue.</summary>
    public bool Loop { get; set; }

    /// <summary>Where Play starts after Stop, and where a loop restarts. Defaults to the programme start.</summary>
    public TimeSpan StartPosition { get; set; }

    /// <summary>Cues closer together than this are joined: no CLEAR is sent between them.</summary>
    public TimeSpan MinimumGap { get; set; } = TimeSpan.FromMilliseconds(40);

    /// <summary>Times the cue list has been played through to the end.</summary>
    public int CompletedLoops { get; private set; }

    public PlaybackState State
    {
        get { lock (_sync) return _state; }
    }

    /// <summary>The current media time.</summary>
    public TimeSpan Position
    {
        get { lock (_sync) return PositionLocked; }
    }

    /// <summary>Index of the cue on screen, or -1.</summary>
    public int CurrentCueIndex
    {
        get { lock (_sync) return _onScreen; }
    }

    public event Action<CueShownEventArgs>? CueShown;
    public event Action<SubtitleCue>? CueCleared;
    public event Action<SubtitleCue, Exception>? CueFailed;
    public event Action<PlaybackState>? StateChanged;
    public event Action<int>? LoopCompleted;

    /// <summary>Playback stopped because the connection failed.</summary>
    public event Action<NewforConnectionException>? ConnectionFailed;

    /// <summary>Reached the end without looping.</summary>
    public event Action? Finished;

    private TimeSpan PositionLocked =>
        _state == PlaybackState.Playing ? _anchorPosition + (_clock.Elapsed - _anchorClock) : _anchorPosition;

    /// <summary>Starts or resumes playback, optionally from a given position.</summary>
    public void Play(TimeSpan? from = null)
    {
        bool changed;
        lock (_sync)
        {
            ThrowIfDisposed();
            _anchorPosition = from ?? (_state == PlaybackState.Stopped ? StartPosition : PositionLocked);
            _anchorClock = _clock.Elapsed;
            changed = SetState(PlaybackState.Playing);
        }
        _wake.Set();
        RaiseIf(changed, PlaybackState.Playing);
    }

    /// <summary>Pauses; whatever is on screen stays there.</summary>
    public void Pause()
    {
        lock (_sync)
        {
            if (_state != PlaybackState.Playing)
                return;
            _anchorPosition = PositionLocked;
            SetState(PlaybackState.Paused);
        }
        _wake.Set();
        RaiseIf(true, PlaybackState.Paused);
    }

    public void TogglePause()
    {
        if (State == PlaybackState.Playing)
            Pause();
        else
            Play();
    }

    /// <summary>Stops, clears the screen and goes back to the start position. Returns once the screen is cleared.</summary>
    public void Stop()
    {
        bool changed;
        lock (_sync)
        {
            if (_state == PlaybackState.Stopped && _onScreen < 0)
                return;
            _anchorPosition = StartPosition;
            changed = SetState(PlaybackState.Stopped);
        }
        _wake.Set();
        RaiseIf(changed, PlaybackState.Stopped);

        // Return once the playback thread has cleared the screen, so callers can safely disconnect
        if (Thread.CurrentThread != _thread)
            SpinWait.SpinUntil(() => CurrentCueIndex < 0 || State != PlaybackState.Stopped, TimeSpan.FromSeconds(2));
    }

    /// <summary>Moves to a media time. If playing, carries on from there; the screen updates either way.</summary>
    public void Seek(TimeSpan position)
    {
        bool changed = false;
        lock (_sync)
        {
            _anchorPosition = position;
            _anchorClock = _clock.Elapsed;
            if (_state == PlaybackState.Stopped)
                changed = SetState(PlaybackState.Paused); // so the cue at the new position is shown
        }
        _wake.Set();
        RaiseIf(changed, PlaybackState.Paused);
    }

    /// <summary>Jumps to the start of the cue after the current position.</summary>
    public void NextCue()
    {
        TimeSpan now = Position;
        var next = _cues.FirstOrDefault(c => c.Start > now + TimeSpan.FromMilliseconds(1));
        if (next is not null)
            Seek(next.Start);
    }

    /// <summary>Jumps to the start of the current cue, or the previous one if already near its start.</summary>
    public void PreviousCue()
    {
        TimeSpan now = Position;
        var previous = _cues.LastOrDefault(c => c.Start < now - TimeSpan.FromMilliseconds(500));
        Seek(previous?.Start ?? StartPosition);
    }

    /// <summary>Index of the cue that should be on screen at <paramref name="position"/>, or -1.</summary>
    public int CueIndexAt(TimeSpan position)
    {
        int index = LastStartedIndex(position);
        return index >= 0 && position < EffectiveEnd(index) ? index : -1;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        _wake.Set();
        _thread.Join(TimeSpan.FromSeconds(2));
        _wake.Dispose();
    }

    private void Run()
    {
        while (true)
        {
            int target;
            TimeSpan position;
            PlaybackState state;
            int onScreen;

            lock (_sync)
            {
                if (_disposed)
                    return;
                state = _state;
                position = PositionLocked;
                onScreen = _onScreen;
                target = state == PlaybackState.Stopped ? -1 : CueIndexAt(position);
            }

            if (target != onScreen)
            {
                if (!Show(target, onScreen, position))
                    continue; // connection failed: state is now Stopped
            }

            TimeSpan? nextEvent = state == PlaybackState.Playing ? NextEventAfter(position, target) : null;

            // A document with a set duration (e.g. a soak test) runs to its end before looping
            if (state == PlaybackState.Playing && nextEvent is null && Document.Duration is { } duration &&
                position < StartPosition + duration)
                nextEvent = StartPosition + duration;

            if (state == PlaybackState.Playing && nextEvent is null)
            {
                // Past the last cue
                if (Loop && _cues.Length > 0 && StartPosition < EffectiveEnd(_cues.Length - 1))
                {
                    CompletedLoops++;
                    LoopCompleted?.Invoke(CompletedLoops);
                    lock (_sync)
                    {
                        _anchorPosition = StartPosition;
                        _anchorClock = _clock.Elapsed;
                    }
                }
                else
                {
                    lock (_sync)
                    {
                        _anchorPosition = StartPosition;
                        SetState(PlaybackState.Stopped);
                    }
                    RaiseIf(true, PlaybackState.Stopped);
                    Finished?.Invoke();
                }
                continue;
            }

            WaitUntil(nextEvent);
        }
    }

    /// <summary>Sends the target cue (or a CLEAR). Returns false if the connection failed.</summary>
    private bool Show(int target, int onScreen, TimeSpan position)
    {
        try
        {
            if (target >= 0)
            {
                var cue = _cues[target];
                var lateness = position - cue.Start;
                try
                {
                    _client.SendSubtitle(Page, cue.Lines, cue.ApplyTo(Style));
                    lateness = Position - cue.Start;
                    SetOnScreen(target);
                    CueShown?.Invoke(new CueShownEventArgs(cue, target, lateness));
                }
                catch (ArgumentException ex)
                {
                    // This cue can't be laid out; don't retry it, but don't stop playback
                    SetOnScreen(target);
                    CueFailed?.Invoke(cue, ex);
                }
            }
            else
            {
                _client.Clear();
                SetOnScreen(-1);
                if (onScreen >= 0)
                    CueCleared?.Invoke(_cues[onScreen]);
            }

            return true;
        }
        catch (NewforConnectionException ex)
        {
            lock (_sync)
            {
                _anchorPosition = PositionLocked;
                _onScreen = -1;
                SetState(PlaybackState.Stopped);
            }
            RaiseIf(true, PlaybackState.Stopped);
            ConnectionFailed?.Invoke(ex);
            return false;
        }
    }

    private void SetOnScreen(int index)
    {
        lock (_sync)
            _onScreen = index;
    }

    /// <summary>The next start or end time after <paramref name="position"/>, or null if there are none.</summary>
    private TimeSpan? NextEventAfter(TimeSpan position, int current)
    {
        TimeSpan? next = null;

        int lastStarted = LastStartedIndex(position);
        if (lastStarted + 1 < _cues.Length)
            next = _cues[lastStarted + 1].Start;

        if (current >= 0)
        {
            TimeSpan end = EffectiveEnd(current);
            if (next is null || end < next)
                next = end;
        }

        return next;
    }

    /// <summary>A cue's end, extended to the next cue's start if the gap is shorter than <see cref="MinimumGap"/>.</summary>
    private TimeSpan EffectiveEnd(int index)
    {
        TimeSpan end = _cues[index].End;
        if (index + 1 < _cues.Length)
        {
            TimeSpan nextStart = _cues[index + 1].Start;
            if (nextStart - end < MinimumGap)
                end = TimeSpan.FromTicks(Math.Max(end.Ticks, nextStart.Ticks));
        }
        return end;
    }

    /// <summary>Index of the last cue starting at or before <paramref name="position"/>, or -1.</summary>
    private int LastStartedIndex(TimeSpan position)
    {
        int lo = 0, hi = _cues.Length - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (_cues[mid].Start <= position)
            {
                found = mid;
                lo = mid + 1;
            }
            else
                hi = mid - 1;
        }
        return found;
    }

    /// <summary>Waits until the media time reaches <paramref name="target"/>, or until woken by a control call.</summary>
    private void WaitUntil(TimeSpan? target)
    {
        if (target is null)
        {
            _wake.WaitOne();
            return;
        }

        while (true)
        {
            TimeSpan remaining = target.Value - Position;
            if (remaining <= TimeSpan.Zero)
                return;

            if (remaining > SpinThreshold)
            {
                if (_wake.WaitOne(remaining - SpinThreshold))
                    return;
            }
            else
            {
                if (_wake.WaitOne(0))
                    return;
                Thread.SpinWait(50);
            }
        }
    }

    /// <summary>Call with the lock held; raise <see cref="StateChanged"/> after releasing it.</summary>
    private bool SetState(PlaybackState state)
    {
        if (_state == state)
            return false;
        _state = state;
        return true;
    }

    private void RaiseIf(bool changed, PlaybackState state)
    {
        if (changed)
            StateChanged?.Invoke(state);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
