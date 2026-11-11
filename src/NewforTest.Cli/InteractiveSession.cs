// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using NewforTest.Core;

namespace NewforTest.Cli;

/// <summary>
/// The keyboard-driven test console. The screen is redrawn in place: the menu and status at the
/// top, then a scrollable log, with the bottom line kept free for prompts. Nothing scrolls the
/// console itself, so the status line can't be overwritten or pushed off screen.
/// </summary>
/// <remarks>
/// Threading: keys are handled on the main thread. The subtitle player raises events on its own
/// thread; those only append to the log (which is locked) and mark the screen for redrawing.
/// </remarks>
internal sealed class InteractiveSession
{
    private const int MaxLogEntries = 5000;
    private const string DetailIndent = "     ";
    private static readonly TimeSpan PlayingRedrawInterval = TimeSpan.FromMilliseconds(100);

    private static readonly string Version =
        typeof(InteractiveSession).Assembly.GetName().Version?.ToString(2) ?? "?";

    private static readonly Dictionary<ConsoleKey, TeletextColor> ColorKeys = new()
    {
        [ConsoleKey.R] = TeletextColor.Red,
        [ConsoleKey.G] = TeletextColor.Green,
        [ConsoleKey.Y] = TeletextColor.Yellow,
        [ConsoleKey.B] = TeletextColor.Blue,
        [ConsoleKey.M] = TeletextColor.Magenta,
        [ConsoleKey.A] = TeletextColor.Cyan,
        [ConsoleKey.W] = TeletextColor.White,
    };

    private readonly NewforClient _client;
    private readonly CliOptions _options;
    private readonly List<string> _log = new();
    private PageNumber _page;
    private SubtitleStyle _style;
    private bool _hexDump;
    private string[]? _customLines;
    private SubtitlePlayer? _player;

    private volatile bool _dirty = true;
    private bool _clearScreen = true;
    private (int Width, int Height) _lastSize;
    private int _logScroll; // lines scrolled back from the newest entry
    private int _logPageSize = 10;

    public InteractiveSession(NewforClient client, PageNumber page, CliOptions options)
    {
        _client = client;
        _options = options;
        _page = page;
        _style = options.Style;
        _hexDump = options.HexDump;
        _client.PacketSent += OnPacketSent;
    }

    public void Run()
    {
        bool previousTreatControlC = Console.TreatControlCAsInput;
        Console.TreatControlCAsInput = true; // Ctrl+C arrives as a key, so we can clear and send DISCONNECT

        try
        {
            Reconnect();
            if (_options.File is not null)
                LoadFile(_options.File, _options.Loop, _options.Start);

            var lastRender = DateTime.MinValue;
            while (true)
            {
                if (Console.KeyAvailable)
                {
                    var keyInfo = Console.ReadKey(intercept: true);
                    if (IsQuitKey(keyInfo))
                        break;
                    HandleKey(keyInfo);
                    _dirty = true;
                    continue; // handle any queued keys before redrawing
                }

                bool playing = _player?.State == PlaybackState.Playing;
                if (_dirty || (playing && DateTime.UtcNow - lastRender >= PlayingRedrawInterval))
                {
                    _dirty = false;
                    Render();
                    lastRender = DateTime.UtcNow;
                }
                else
                {
                    Thread.Sleep(15);
                }
            }
        }
        finally
        {
            // Also reached on an unexpected exception: always try to leave the inserter cleanly
            CloseFile();

            if (_client.IsConnected)
            {
                try
                {
                    _client.Disconnect();
                    Log("DISCONNECT sent.");
                }
                catch (NewforConnectionException ex)
                {
                    Log($"ERROR sending DISCONNECT: {ex.Message}");
                }
            }

            Render();
            Console.WriteLine();
            Console.TreatControlCAsInput = previousTreatControlC;
        }
    }

    private static bool IsQuitKey(ConsoleKeyInfo keyInfo) =>
        keyInfo.Key == ConsoleKey.Q ||
        (keyInfo.Key == ConsoleKey.C && keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control));

    private void HandleKey(ConsoleKeyInfo keyInfo)
    {
        if (ColorKeys.TryGetValue(keyInfo.Key, out var color))
        {
            SetStyle(_style with { Color = color });
            return;
        }

        switch (keyInfo.Key)
        {
            // Style
            case ConsoleKey.X:
                SetStyle(_style with { Boxed = !_style.Boxed });
                break;
            case ConsoleKey.H:
                SetStyle(_style with { DoubleHeight = !_style.DoubleHeight });
                break;
            case ConsoleKey.J:
                SetStyle(_style with { Justification = Next(_style.Justification) });
                break;
            case ConsoleKey.I:
                var sets = TeletextCharacterSet.All;
                var next = sets[(sets.ToList().IndexOf(_style.CharacterSet) + 1) % sets.Count];
                SetStyle(_style with { CharacterSet = next });
                Log($"Character set: {next}. It must match the inserter's national option.");
                break;
            case ConsoleKey.T:
                SetStyle(_style with { Position = VerticalPosition.Top });
                break;
            case ConsoleKey.N:
                SetStyle(_style with { Position = VerticalPosition.Middle });
                break;
            case ConsoleKey.L:
                SetStyle(_style with { Position = VerticalPosition.Lower });
                break;

            // Send
            case ConsoleKey.D1 or ConsoleKey.NumPad1:
                SendSubtitle(TestSubtitles.All[0]);
                break;
            case ConsoleKey.D2 or ConsoleKey.NumPad2:
                SendSubtitle(TestSubtitles.All[1]);
                break;
            case ConsoleKey.D3 or ConsoleKey.NumPad3:
                SendSubtitle(TestSubtitles.All[2]);
                break;
            case ConsoleKey.E:
                EnterCustomText();
                break;
            case ConsoleKey.D4 or ConsoleKey.NumPad4:
                if (_customLines is null)
                    Log("No custom text yet. Press [E] to enter some.");
                else
                    SendSubtitle(_customLines);
                break;
            case ConsoleKey.C:
                Execute(_client.Clear, "CLEAR sent.");
                break;

            // File playback
            case ConsoleKey.F:
                string? path = PromptLine(" Subtitle file (or drag it here), Enter to cancel: ");
                if (!string.IsNullOrWhiteSpace(path))
                    LoadFile(path.Trim().Trim('"', '\''), loop: _player?.Loop ?? false, start: null);
                break;
            case ConsoleKey.U:
                StartSoakTest();
                break;
            case ConsoleKey.Spacebar:
                WithPlayer(p =>
                {
                    if (!_client.IsConnected)
                        Log("Not connected. Press [O] to reconnect.");
                    else
                        p.TogglePause();
                });
                break;
            case ConsoleKey.S:
                WithPlayer(p => p.Stop());
                break;
            case ConsoleKey.RightArrow:
                WithPlayer(p => p.NextCue());
                break;
            case ConsoleKey.LeftArrow:
                WithPlayer(p => p.PreviousCue());
                break;
            case ConsoleKey.G:
                WithPlayer(GoTo);
                break;
            case ConsoleKey.K:
                WithPlayer(p =>
                {
                    p.Loop = !p.Loop;
                    Log($"Loop {(p.Loop ? "on" : "off")}.");
                });
                break;

            // Other
            case ConsoleKey.P:
                ChangePage();
                break;
            case ConsoleKey.O:
                Reconnect();
                break;
            case ConsoleKey.V:
                _hexDump = !_hexDump;
                Log($"Hex dump {(_hexDump ? "on" : "off")}.");
                break;

            // Log scrolling
            case ConsoleKey.PageUp:
                _logScroll += _logPageSize;
                break;
            case ConsoleKey.PageDown:
                _logScroll = Math.Max(0, _logScroll - _logPageSize);
                break;
            case ConsoleKey.Home:
                _logScroll = int.MaxValue; // clamped when rendering
                break;
            case ConsoleKey.End:
                _logScroll = 0;
                break;
        }
    }

    private static Justification Next(Justification j) => j switch
    {
        Justification.Left => Justification.Centre,
        Justification.Centre => Justification.Right,
        _ => Justification.Left,
    };

    private void SetStyle(SubtitleStyle style)
    {
        _style = style;
        if (_player is not null)
            _player.Style = style; // applies from the next cue
    }

    private void SendSubtitle(IReadOnlyList<string> lines)
    {
        var style = _style;
        var page = _page;
        Execute(
            () => _client.SendSubtitle(page, lines, style),
            $"Sent to page {page} ({Describe(style)}): {SubtitleText.Join(lines)}");
    }

    /// <summary>Runs a send, logging success or the reason it failed.</summary>
    private void Execute(Action send, string successMessage)
    {
        if (!_client.IsConnected)
        {
            Log("Not connected. Press [O] to reconnect.");
            return;
        }

        try
        {
            send();
            Log(successMessage);
        }
        catch (NewforConnectionException ex)
        {
            Log($"ERROR: {ex.Message} Press [O] to reconnect.");
        }
        catch (ArgumentException ex)
        {
            Log($"ERROR: {ex.Message}");
        }
    }

    private void EnterCustomText()
    {
        while (true)
        {
            int max = SubtitleRowBuilder.MaxTextLength(_style.Boxed);
            string? input = PromptLine($" Text ({SubtitleText.LineSeparator} = new line, max {max}): ");

            if (string.IsNullOrWhiteSpace(input))
            {
                Log("Custom text cancelled.");
                return;
            }

            string[] lines = SubtitleText.Split(input);
            if (!SubtitleText.TryValidate(lines, _style, out string? error))
            {
                Log($"ERROR: {error} Try again, or Enter to cancel.");
                Render();
                continue;
            }

            var unsupported = new HashSet<char>();
            _style.CharacterSet.Encode(input.Replace(SubtitleText.LineSeparator.ToString(), ""), unsupported);
            if (unsupported.Count > 0)
                Log($"Warning: not in the {_style.CharacterSet} teletext set, sent as '?': {string.Join(" ", unsupported)}");

            _customLines = lines;
            SendSubtitle(lines);
            return;
        }
    }

    private void LoadFile(string path, bool loop, string? start)
    {
        SubtitleDocument document;
        try
        {
            document = SubtitleFile.Load(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FormatException or ArgumentException)
        {
            Log($"ERROR: could not load {path}: {ex.Message}");
            return;
        }

        foreach (string warning in document.Warnings.Take(10))
            Log($"Warning: {warning}");
        if (document.Warnings.Count > 10)
            Log($"Warning: ... and {document.Warnings.Count - 10} more.");

        if (document.Cues.Count == 0)
        {
            Log($"ERROR: {document.Name} has no subtitles.");
            return;
        }

        int bad = HeadlessRunner.ReportUnplayableCues(document, _style, Log);

        TimeSpan? startPosition = null;
        if (start is not null)
        {
            if (Timecode.TryParse(start, document.FrameRate, out var parsed))
                startPosition = parsed;
            else
                Log($"ERROR: '{start}' is not a timecode; starting from the beginning.");
        }

        OpenPlayer(document, loop, startPosition);

        double fps = document.FrameRate;
        Log($"Loaded {document}: {Timecode.Format(document.Cues[0].Start, fps)} to " +
            $"{Timecode.Format(document.Cues[^1].End, fps)}{(bad > 0 ? $", {bad} cannot be shown" : "")}. " +
            "[Space] to play.");
    }

    private void StartSoakTest()
    {
        string? input = PromptLine($" Soak test: seconds between subtitles (default {CliOptions.DefaultSoakSeconds}): ");
        double seconds = CliOptions.DefaultSoakSeconds;
        if (!string.IsNullOrWhiteSpace(input) &&
            (!double.TryParse(input, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seconds) || seconds < 0.1))
        {
            Log("ERROR: enter a number of seconds, at least 0.1.");
            return;
        }

        if (!_client.IsConnected)
        {
            Log("Not connected. Press [O] to reconnect.");
            return;
        }

        OpenPlayer(TestSubtitles.CreateSoakTest(TimeSpan.FromSeconds(seconds)), loop: true, start: null);
        _player!.Play();
        Log($"Soak test started: a test subtitle every {seconds:0.###} s, looping. [S] to stop.");
    }

    private void OpenPlayer(SubtitleDocument document, bool loop, TimeSpan? start)
    {
        CloseFile();

        var player = new SubtitlePlayer(_client, document, _page, _style) { Loop = loop };
        if (start is not null)
            player.StartPosition = start.Value;

        double fps = document.FrameRate;
        player.CueShown += e => Log($"#{e.Cue.Number} {Timecode.Format(e.Cue.Start, fps)} {HeadlessRunner.FormatLateness(e.Lateness)}: {e.Cue.Text}");
        player.CueFailed += (cue, ex) => Log($"#{cue.Number} SKIPPED: {ex.Message}");
        player.LoopCompleted += n => Log($"Loop {n} complete, starting again.");
        player.Finished += () => Log("Playback finished.");
        player.ConnectionFailed += ex => Log($"ERROR: {ex.Message} Playback stopped. Press [O] to reconnect.");
        player.StateChanged += _ => _dirty = true;

        _player = player;
    }

    private void CloseFile()
    {
        if (_player is null)
            return;

        var player = _player;
        _player = null;
        if (_client.IsConnected)
            player.Stop(); // clears the screen
        player.Dispose();
    }

    private void WithPlayer(Action<SubtitlePlayer> action)
    {
        if (_player is null)
            Log("No file loaded. Press [F] to load one, or [U] for a soak test.");
        else
            action(_player);
    }

    private void GoTo(SubtitlePlayer player)
    {
        double fps = player.Document.FrameRate;
        string? input = PromptLine($" Go to timecode or #cue (now {Timecode.Format(player.Position, fps)}): ");
        if (string.IsNullOrWhiteSpace(input))
            return;

        input = input.Trim();
        if (input.StartsWith('#') && int.TryParse(input[1..], out int number))
        {
            var cue = player.Cues.FirstOrDefault(c => c.Number == number);
            if (cue is null)
                Log($"ERROR: there is no cue #{number}.");
            else
                player.Seek(cue.Start);
        }
        else if (Timecode.TryParse(input, fps, out var position))
            player.Seek(position);
        else
            Log($"ERROR: '{input}' is not a timecode (HH:MM:SS:FF) or cue number (#123).");
    }

    private void Reconnect()
    {
        if (_client.IsConnected)
        {
            try
            {
                _client.Disconnect();
                Log("DISCONNECT sent.");
            }
            catch (NewforConnectionException)
            {
                // Already dead; carry on and reconnect
            }
        }

        Log($"Connecting to {_client.Host}:{_client.Port}...");
        Render();

        try
        {
            _client.Connect();
            Log("Connected.");
        }
        catch (NewforConnectionException ex)
        {
            Log($"ERROR: {ex.Message} Press [O] to retry.");
        }
    }

    private void ChangePage()
    {
        string? input = PromptLine($" Enter new page number (current {_page}, Enter to cancel): ");

        if (string.IsNullOrWhiteSpace(input))
            Log("Page unchanged.");
        else if (PageNumber.TryParse(input, out var page, out var error))
        {
            _page = page;
            if (_player is not null)
                _player.Page = page;
            Log($"Page changed to {_page}.");
        }
        else
            Log($"ERROR: {error}");
    }

    private string? PromptLine(string prompt)
    {
        Render();
        Console.SetCursorPosition(0, Math.Max(0, Console.WindowHeight - 1));
        Console.Write(prompt);
        string? input = Console.ReadLine();
        _clearScreen = true; // the Enter key, or a long entry wrapping, may have scrolled the window
        return input;
    }

    private void OnPacketSent(NewforPacket packet)
    {
        if (!_hexDump)
            return;

        var description = NewforPacketDecoder.Describe(packet.Bytes, _style.CharacterSet);
        lock (_log)
        {
            Log($"TX {description[0]}");
            foreach (string line in description.Skip(1).Concat(HexDump.Format(packet.Bytes)))
                AddLogLine(DetailIndent + line);
        }
    }

    private void Log(string message) => AddLogLine($"{DateTime.Now:HH:mm:ss} | {message}");

    private void AddLogLine(string line)
    {
        lock (_log)
        {
            _log.Add(line);
            if (_log.Count > MaxLogEntries)
                _log.RemoveRange(0, _log.Count - MaxLogEntries);
            _logScroll = 0; // jump back to the newest entries
        }
        _dirty = true;
    }

    private void Render()
    {
        int width = Math.Max(Console.WindowWidth - 1, 20); // -1 so a full line never wraps
        int height = Math.Max(Console.WindowHeight, 2);

        if (_clearScreen || _lastSize != (width, height) || Console.WindowTop != 0)
        {
            Console.Clear();
            _clearScreen = false;
            _lastSize = (width, height);
        }

        string connection = _client.IsConnected ? "CONNECTED" : "DISCONNECTED";
        string custom = _customLines is null ? "(none)" : SubtitleText.Join(_customLines);

        var lines = new List<string>
        {
            $" NEWFOR INJECTOR v{Version}  |  (c) 2026 Christopher Glover",
            $" Target: {_client.Host}:{_client.Port} [{connection}]  |  Page: {_page}  |  Charset: {_style.CharacterSet}",
            new string('=', 78),
            " COLOR     [R]ed [G]reen [Y]ellow [B]lue [M]agenta [A] Cyan [W]hite",
            " STYLE     [X] Box  [H] Double height  [J] Justify  [I] Charset",
            " POSITION  [T]op  [N] Middle  [L]ower",
            " SEND      [1] [2] [3] Test lines  [E] Enter text  [4] Resend  [C] Clear",
            " FILE      [F] Load  [Space] Play/Pause  [S] Stop  [<] [>] Prev/Next cue",
            "           [G] Go to  [K] Loop  [U] Soak test",
            " OTHER     [P] Page  [O] Reconnect  [V] Hex dump  [Q] Quit",
            new string('-', 78),
            $" Color: {_style.Color,-7}  Box: {(_style.Boxed ? "ON" : "OFF"),-3}  " +
            $"Height: {(_style.DoubleHeight ? "DOUBLE" : "SINGLE"),-6}  " +
            $"Pos: {_style.Position.ToString().ToUpperInvariant(),-6}  " +
            $"Justify: {_style.Justification.ToString().ToUpperInvariant(),-6}  Hex: {(_hexDump ? "ON" : "OFF")}",
            $" Custom: {custom}",
            PlayerStatus(),
        };

        // The rest of the screen, minus the log title and the prompt line, shows the log
        int logRows = Math.Max(0, height - 2 - lines.Count);
        List<string> logLines;
        lock (_log)
        {
            _logPageSize = Math.Max(1, logRows - 1);
            _logScroll = Math.Clamp(_logScroll, 0, Math.Max(0, _log.Count - logRows));

            lines.Add(_logScroll == 0
                ? "--- LOG ([PgUp]/[PgDn] scroll) " + new string('-', 47)
                : $"--- LOG (back {_logScroll} lines, [End] for latest) " + new string('-', 30));

            int first = Math.Max(0, _log.Count - logRows - _logScroll);
            logLines = _log.Skip(first).Take(logRows).ToList();
        }
        lines.AddRange(logLines);

        for (int row = 0; row < height; row++)
        {
            string text = row < height - 1 && row < lines.Count ? lines[row] : "";
            Console.SetCursorPosition(0, row);
            Console.Write(text.Length > width ? text[..width] : text.PadRight(width));
        }

        Console.SetCursorPosition(0, height - 1);
    }

    private string PlayerStatus()
    {
        var player = _player;
        if (player is null)
            return " File: (none)";

        double fps = player.Document.FrameRate;
        int current = player.CurrentCueIndex;
        string cue = current >= 0 ? $"#{player.Cues[current].Number}" : "-";
        string loops = player.CompletedLoops > 0 ? $" ({player.CompletedLoops} done)" : "";

        return $" File: {player.Document.Name}  {player.State.ToString().ToUpperInvariant(),-7} " +
               $"{Timecode.Format(player.Position, fps)}  Cue: {cue}/{player.Cues.Count}  " +
               $"Loop: {(player.Loop ? "ON" : "OFF")}{loops}";
    }

    private static string Describe(SubtitleStyle style) =>
        $"{style.Color}, {(style.Boxed ? "boxed" : "unboxed")}, " +
        $"{(style.DoubleHeight ? "double" : "single")} height, {style.Position.ToString().ToLowerInvariant()}, " +
        $"{style.Justification.ToString().ToLowerInvariant()}";
}
