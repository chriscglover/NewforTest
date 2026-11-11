// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using NewforTest.Core;

namespace NewforTest.Cli;

/// <summary>
/// The non-interactive modes (--send, --clear, --play, --soak), for scripts and CI. Logs to
/// stdout, errors to stderr; nothing is prompted for, so missing settings use their defaults.
/// </summary>
internal static class HeadlessRunner
{
    private static readonly object ConsoleLock = new();

    public static int Run(CliOptions options)
    {
        string host = options.Host ?? CliOptions.DefaultHost;
        int port = options.Port ?? CliOptions.DefaultPort;
        PageNumber page = options.Page ?? PageNumber.Parse(CliOptions.DefaultPage);
        SubtitleStyle style = options.Style;
        Log($"NewforTest {AppVersion.Display}");

        // Check everything we can before connecting, so bad input never sends anything
        string[]? sendLines = null;
        SubtitleDocument? document = null;
        TimeSpan? start = null;

        switch (options.Mode)
        {
            case RunMode.Send:
                sendLines = SubtitleText.Split(options.SendText!);
                if (!SubtitleText.TryValidate(sendLines, style, out string? error))
                    return Fail(error, ExitCodes.BadInput);
                WarnUnsupported(options.SendText!, style);
                break;

            case RunMode.Play:
                try
                {
                    document = SubtitleFile.Load(options.File!);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FormatException)
                {
                    return Fail(ex.Message, ExitCodes.BadInput);
                }

                foreach (string warning in document.Warnings)
                    Log($"Warning: {warning}");

                if (options.Start is not null)
                {
                    if (!Timecode.TryParse(options.Start, document.FrameRate, out var parsed))
                        return Fail($"'{options.Start}' is not a timecode: use HH:MM:SS:FF, HH:MM:SS.mmm or seconds.", ExitCodes.BadInput);
                    start = parsed;
                }

                int bad = ReportUnplayableCues(document, style);
                Log($"Loaded {document}{(bad > 0 ? $", {bad} cannot be shown and will be skipped" : "")}.");
                break;

            case RunMode.Soak:
                document = TestSubtitles.CreateSoakTest(TimeSpan.FromSeconds(options.SoakSeconds));
                break;
        }

        using var client = new NewforClient(host, port);
        if (options.HexDump)
            client.PacketSent += packet => LogPacket(packet, style.CharacterSet);

        try
        {
            client.Connect();
        }
        catch (NewforConnectionException ex)
        {
            return Fail(ex.Message, ExitCodes.ConnectionFailed);
        }

        Log($"Connected to {host}:{port}, page {page}.");

        try
        {
            switch (options.Mode)
            {
                case RunMode.Send:
                    client.SendSubtitle(page, sendLines!, style);
                    Log($"Sent: {SubtitleText.Join(sendLines!)}");
                    if (options.HoldSeconds is not 0)
                    {
                        Hold(options.HoldSeconds);
                        client.Clear();
                        Log("CLEAR sent.");
                    }
                    return ExitCodes.Success;

                case RunMode.Clear:
                    client.Clear();
                    Log("CLEAR sent.");
                    return ExitCodes.Success;

                default:
                    return Play(client, document!, page, style, options.Loop || options.Mode == RunMode.Soak, start);
            }
        }
        catch (NewforConnectionException ex)
        {
            return Fail(ex.Message, ExitCodes.ConnectionFailed);
        }
        finally
        {
            if (client.IsConnected)
            {
                try
                {
                    client.Disconnect();
                    Log("DISCONNECT sent.");
                }
                catch (NewforConnectionException)
                {
                    // already gone
                }
            }
        }
    }

    private static int Play(NewforClient client, SubtitleDocument document, PageNumber page, SubtitleStyle style, bool loop, TimeSpan? start)
    {
        double fps = document.FrameRate;
        using var done = new ManualResetEventSlim();
        int exitCode = ExitCodes.Success;

        using var player = new SubtitlePlayer(client, document, page, style) { Loop = loop };
        if (start is not null)
            player.StartPosition = start.Value;

        player.CueShown += e => Log($"#{e.Cue.Number,-5} {Timecode.Format(e.Cue.Start, fps)}  {FormatLateness(e.Lateness)}  {e.Cue.Text}");
        player.CueCleared += cue => Log($"#{cue.Number,-5} {Timecode.Format(cue.End, fps)}  cleared");
        player.CueFailed += (cue, ex) => Log($"#{cue.Number,-5} SKIPPED: {ex.Message}");
        player.LoopCompleted += n => Log($"Loop {n} complete, starting again.");
        player.Finished += () => done.Set();
        player.ConnectionFailed += ex =>
        {
            LogError(ex.Message);
            exitCode = ExitCodes.ConnectionFailed;
            done.Set();
        };

        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true; // stop cleanly: clear the screen and send DISCONNECT
            done.Set();
        };
        Console.CancelKeyPress += onCancel;

        try
        {
            Log($"Playing {document.Name} from {Timecode.Format(player.StartPosition, fps)}{(loop ? ", looping" : "")}. Ctrl+C to stop.");
            var firstCue = document.Cues.FirstOrDefault(c => c.End > player.StartPosition);
            if (firstCue is not null && firstCue.Start - player.StartPosition > TimeSpan.FromSeconds(10))
                Log($"First subtitle is at {Timecode.Format(firstCue.Start, fps)} (use --start to skip ahead).");

            player.Play();
            done.Wait();
            player.Stop();
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }

        Log(exitCode == ExitCodes.Success ? "Playback stopped." : "Playback stopped: connection lost.");
        return exitCode;
    }

    /// <summary>Waits for the given number of seconds, or until Ctrl+C if null.</summary>
    private static void Hold(double? seconds)
    {
        using var done = new ManualResetEventSlim();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            done.Set();
        };
        Console.CancelKeyPress += onCancel;
        try
        {
            if (seconds is null)
            {
                Log("Subtitle is up. Ctrl+C to clear it and disconnect.");
                done.Wait();
            }
            else
            {
                Log($"Holding for {seconds:0.###} s (Ctrl+C to stop early).");
                done.Wait(TimeSpan.FromSeconds(seconds.Value));
            }
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    /// <summary>Lists cues that won't fit with this style. Returns how many.</summary>
    internal static int ReportUnplayableCues(SubtitleDocument document, SubtitleStyle style, Action<string>? log = null, int maxListed = 10)
    {
        log ??= message => Log(message);
        int count = 0;

        foreach (var cue in document.Cues)
        {
            if (SubtitleText.TryValidate(cue.Lines, cue.ApplyTo(style), out string? error))
                continue;

            if (++count <= maxListed)
                log($"Warning: cue {cue.Number} cannot be shown: {error}");
        }

        if (count > maxListed)
            log($"Warning: ... and {count - maxListed} more cues that cannot be shown.");
        return count;
    }

    internal static string FormatLateness(TimeSpan lateness) =>
        lateness > TimeSpan.FromSeconds(1)
            ? $"joined {lateness.TotalSeconds:0.0} s in"
            : $"+{lateness.TotalMilliseconds,5:0.0} ms";

    private static void WarnUnsupported(string text, SubtitleStyle style)
    {
        var unsupported = new HashSet<char>();
        style.CharacterSet.Encode(text.Replace(SubtitleText.LineSeparator.ToString(), ""), unsupported);
        if (unsupported.Count > 0)
            Log($"Warning: not in the {style.CharacterSet} teletext set, sent as '?': {string.Join(" ", unsupported)}");
    }

    private static void LogPacket(NewforPacket packet, TeletextCharacterSet characterSet)
    {
        var description = NewforPacketDecoder.Describe(packet.Bytes, characterSet);
        lock (ConsoleLock)
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} | TX {description[0]}");
            foreach (string line in description.Skip(1).Concat(HexDump.Format(packet.Bytes)))
                Console.WriteLine($"             {line}");
        }
    }

    private static void Log(string message)
    {
        lock (ConsoleLock)
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} | {message}");
    }

    private static void LogError(string message)
    {
        lock (ConsoleLock)
            Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss.fff} | ERROR: {message}");
    }

    private static int Fail(string message, int exitCode)
    {
        LogError(message);
        return exitCode;
    }
}
