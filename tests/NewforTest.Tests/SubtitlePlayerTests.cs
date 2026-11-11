// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using NewforTest.Core;

namespace NewforTest.Tests;

/// <summary>
/// Real-time tests against a loopback socket. Cue times are short (tens of ms) and the
/// tolerances generous, so they stay reliable on a busy machine.
/// </summary>
public sealed class SubtitlePlayerTests : IDisposable
{
    private static readonly PageNumber Page = PageNumber.Parse("888");

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly NewforClient _client;
    private readonly TcpClient _server;
    private readonly ConcurrentQueue<(NewforPacket Packet, TimeSpan At)> _sent = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public SubtitlePlayerTests()
    {
        _listener.Start();
        _client = new NewforClient("127.0.0.1", ((IPEndPoint)_listener.LocalEndpoint).Port);
        _client.Connect();
        _server = _listener.AcceptTcpClient();
        _client.PacketSent += p => _sent.Enqueue((p, _clock.Elapsed));
        Task.Run(DrainServer); // keep the socket buffer empty
    }

    public void Dispose()
    {
        _client.Dispose();
        _server.Dispose();
        _listener.Stop();
    }

    private void DrainServer()
    {
        var buffer = new byte[4096];
        try
        {
            while (_server.GetStream().Read(buffer) > 0) { }
        }
        catch (Exception)
        {
            // closed
        }
    }

    private static SubtitleDocument Document(params (double StartMs, double EndMs, string Text)[] cues) =>
        new("Test", cues.Select((c, i) => new SubtitleCue(
            i + 1,
            TimeSpan.FromMilliseconds(c.StartMs),
            TimeSpan.FromMilliseconds(c.EndMs),
            SubtitleText.ToLines(SubtitleText.Split(c.Text)))).ToArray(), Array.Empty<string>());

    /// <summary>Records what reached the screen, in order: each cue's text, or "CLEAR".</summary>
    private static ConcurrentQueue<string> Record(SubtitlePlayer player)
    {
        var events = new ConcurrentQueue<string>();
        player.CueShown += e => events.Enqueue(e.Cue.Text);
        player.CueCleared += _ => events.Enqueue("CLEAR");
        return events;
    }

    private static void WaitFor(Func<bool> condition, int timeoutMs = 3000) =>
        Assert.True(SpinWait.SpinUntil(condition, timeoutMs), "Timed out waiting for the player.");

    [Fact]
    public void Plays_cues_in_order_with_clears_in_the_gaps()
    {
        var doc = Document((50, 150, "ONE"), (300, 400, "TWO"));
        var shown = new ConcurrentQueue<CueShownEventArgs>();
        using var finished = new ManualResetEventSlim();

        using var player = new SubtitlePlayer(_client, doc, Page, new SubtitleStyle());

        var screen = Record(player);
        player.CueShown += shown.Enqueue;
        player.Finished += finished.Set;
        player.Play();

        Assert.True(finished.Wait(3000));
        Assert.Equal(new[] { 1, 2 }, shown.Select(s => s.Cue.Number));
        Assert.Equal(new[] { "ONE", "CLEAR", "TWO", "CLEAR" }, screen.ToArray());
        Assert.Equal(PlaybackState.Stopped, player.State);
    }

    [Fact]
    public void Cues_go_out_close_to_their_start_time()
    {
        var doc = Document((100, 200, "A"), (300, 400, "B"), (500, 600, "C"));
        var shown = new ConcurrentQueue<CueShownEventArgs>();
        using var finished = new ManualResetEventSlim();

        using var player = new SubtitlePlayer(_client, doc, Page, new SubtitleStyle());

        var screen = Record(player);
        player.CueShown += shown.Enqueue;
        player.Finished += finished.Set;
        player.Play();

        Assert.True(finished.Wait(3000));
        // Typically well under 1 ms; allow plenty for a loaded CI machine
        Assert.All(shown, s => Assert.InRange(s.Lateness.TotalMilliseconds, -1, 25));
    }

    [Fact]
    public void Back_to_back_cues_are_not_cleared_between()
    {
        var doc = Document((50, 150, "ONE"), (160, 250, "TWO")); // 10 ms gap < MinimumGap
        using var finished = new ManualResetEventSlim();

        using var player = new SubtitlePlayer(_client, doc, Page, new SubtitleStyle());

        var screen = Record(player);
        player.Finished += finished.Set;
        player.Play();

        Assert.True(finished.Wait(3000));
        Assert.Equal(new[] { "ONE", "TWO", "CLEAR" }, screen.ToArray());
    }

    [Fact]
    public void Stop_clears_the_screen_before_returning()
    {
        var doc = Document((0, 10_000, "LONG"));
        using var player = new SubtitlePlayer(_client, doc, Page, new SubtitleStyle());
        var screen = Record(player);
        player.Play();
        WaitFor(() => player.CurrentCueIndex == 0);

        player.Stop();

        Assert.Equal(-1, player.CurrentCueIndex);
        Assert.Equal(NewforPacketType.Clear, _sent.Last().Packet.Type);
        Assert.Equal(PlaybackState.Stopped, player.State);
    }

    [Fact]
    public void Pause_keeps_the_subtitle_and_the_position()
    {
        var doc = Document((0, 10_000, "HELD"));
        using var player = new SubtitlePlayer(_client, doc, Page, new SubtitleStyle());
        var screen = Record(player);
        player.Play();
        WaitFor(() => player.CurrentCueIndex == 0);

        player.Pause();
        var position = player.Position;
        Thread.Sleep(100);

        Assert.Equal(position, player.Position);
        Assert.Equal(0, player.CurrentCueIndex);
        Assert.NotEqual(NewforPacketType.Clear, _sent.Last().Packet.Type);
    }

    [Fact]
    public void Seek_while_stopped_shows_the_cue_at_that_time()
    {
        var doc = Document((1000, 2000, "FIRST"), (5000, 6000, "SECOND"));
        using var player = new SubtitlePlayer(_client, doc, Page, new SubtitleStyle());
        var screen = Record(player);

        player.Seek(TimeSpan.FromMilliseconds(5500));

        WaitFor(() => player.CurrentCueIndex == 1);
        Assert.Equal(PlaybackState.Paused, player.State);
        Assert.Equal(new[] { "SECOND" }, screen.ToArray());
    }

    [Fact]
    public void NextCue_and_PreviousCue_jump_between_starts()
    {
        var doc = Document((1000, 2000, "A"), (3000, 4000, "B"), (5000, 6000, "C"));
        using var player = new SubtitlePlayer(_client, doc, Page, new SubtitleStyle());
        var screen = Record(player);

        player.NextCue();
        WaitFor(() => player.CurrentCueIndex == 0);
        player.NextCue();
        WaitFor(() => player.CurrentCueIndex == 1);
        player.PreviousCue();
        WaitFor(() => player.CurrentCueIndex == 0);
    }

    [Fact]
    public void Loop_starts_again_from_the_start_position()
    {
        var doc = Document((20, 60, "X"));
        int loops = 0;
        using var player = new SubtitlePlayer(_client, doc, Page, new SubtitleStyle()) { Loop = true };
        var screen = Record(player);
        player.LoopCompleted += n => loops = n;

        player.Play();
        WaitFor(() => loops >= 3);
        player.Stop();

        Assert.True(screen.ToArray().Count(e => e == "X") >= 3);
    }

    [Fact]
    public void Soak_test_cycles_one_two_and_three_line_subtitles()
    {
        var doc = TestSubtitles.CreateSoakTest(TimeSpan.FromMilliseconds(100));
        var shown = new ConcurrentQueue<int>();
        using var player = new SubtitlePlayer(_client, doc, Page, new SubtitleStyle()) { Loop = true };
        var screen = Record(player);
        player.CueShown += e => shown.Enqueue(e.Cue.Lines.Count);

        player.Play();
        WaitFor(() => shown.Count >= 5);
        player.Stop();

        Assert.Equal(new[] { 1, 2, 3, 1, 2 }, shown.Take(5));
    }

    [Fact]
    public void Soak_test_waits_a_full_interval_before_looping()
    {
        var doc = TestSubtitles.CreateSoakTest(TimeSpan.FromMilliseconds(100));
        var times = new ConcurrentQueue<TimeSpan>();
        using var player = new SubtitlePlayer(_client, doc, Page, new SubtitleStyle()) { Loop = true };
        var screen = Record(player);
        player.CueShown += _ => times.Enqueue(_clock.Elapsed);

        player.Play();
        WaitFor(() => times.Count >= 4);
        player.Stop();

        var t = times.ToArray();
        // 3rd -> 4th (loop wrap) should be the same 100 ms as 1st -> 2nd, not 25 ms
        Assert.InRange((t[3] - t[2]).TotalMilliseconds, 80, 160);
    }

    [Fact]
    public void Cue_that_does_not_fit_is_skipped_and_playback_continues()
    {
        var doc = Document((20, 60, new string('X', 50)), (100, 150, "OK"));
        var failed = new ConcurrentQueue<int>();
        using var finished = new ManualResetEventSlim();
        using var player = new SubtitlePlayer(_client, doc, Page, new SubtitleStyle());
        var screen = Record(player);
        player.CueFailed += (cue, _) => failed.Enqueue(cue.Number);
        player.Finished += finished.Set;

        player.Play();

        Assert.True(finished.Wait(3000));
        Assert.Equal(new[] { 1 }, failed);
        Assert.Contains("OK", screen.ToArray());
    }

    [Fact]
    public void Style_and_page_changes_apply_to_the_next_cue()
    {
        var doc = Document((0, 50, "A"), (100, 150, "B"));
        using var player = new SubtitlePlayer(_client, doc, Page, new SubtitleStyle());
        var screen = Record(player);
        using var finished = new ManualResetEventSlim();
        player.Finished += finished.Set;
        player.CueShown += e =>
        {
            if (e.Cue.Number == 1)
            {
                player.Page = PageNumber.Parse("150");
                player.Style = new SubtitleStyle(TeletextColor.Red);
            }
        };

        player.Play();
        Assert.True(finished.Wait(3000));

        var connects = _sent.Where(s => s.Packet.Type == NewforPacketType.Connect)
            .Select(s => NewforPacketDecoder.Describe(s.Packet.Bytes)[0]).ToArray();
        Assert.Equal(new[] { "CONNECT page 888", "CONNECT page 150" }, connects);

        var lastBuild = _sent.Last(s => s.Packet.Type == NewforPacketType.Build).Packet;
        Assert.Contains("{Red}", NewforPacketDecoder.DescribeRow(lastBuild.Bytes.AsSpan(4)));
    }

    [Fact]
    public void Connection_loss_stops_playback_and_is_reported()
    {
        var doc = Document((0, 50, "A"), (300, 400, "B"));
        using var failed = new ManualResetEventSlim();
        using var player = new SubtitlePlayer(_client, doc, Page, new SubtitleStyle());
        var screen = Record(player);
        player.ConnectionFailed += _ => failed.Set();

        player.Play();
        WaitFor(() => player.CurrentCueIndex == 0);
        _client.Dispose(); // simulate the link dropping

        Assert.True(failed.Wait(3000));
        Assert.Equal(PlaybackState.Stopped, player.State);
    }
}
