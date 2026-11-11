// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using NewforTest.Core;

namespace NewforTest.Cli;

/// <summary>
/// The keyboard-driven test console. The screen is redrawn in place after every key: the menu
/// and status at the top, then a scrollable log, with the bottom line kept free for prompts.
/// Nothing scrolls the console itself, so the status line can't be overwritten or pushed off screen.
/// </summary>
internal sealed class InteractiveSession
{
    private const int MaxLogEntries = 5000;
    private const string DetailIndent = "     ";

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

    private static readonly string[][] TestSubtitles =
    {
        new[] { "THIS IS A SINGLE LINE" },
        new[] { "THIS IS LINE ONE", "THIS IS LINE TWO" },
        new[] { "TOP SUBTITLE LINE", "MIDDLE SUBTITLE LINE", "BOTTOM SUBTITLE LINE" },
    };

    private readonly NewforClient _client;
    private readonly List<string> _log = new();
    private PageNumber _page;
    private SubtitleStyle _style = new();
    private bool _hexDump;
    private string[]? _customLines;

    private bool _clearScreen = true;
    private (int Width, int Height) _lastSize;
    private int _logScroll; // lines scrolled back from the newest entry
    private int _logPageSize = 10;

    public InteractiveSession(NewforClient client, PageNumber page, bool hexDump)
    {
        _client = client;
        _page = page;
        _hexDump = hexDump;
        _client.PacketSent += OnPacketSent;
    }

    public void Run()
    {
        bool previousTreatControlC = Console.TreatControlCAsInput;
        Console.TreatControlCAsInput = true; // Ctrl+C arrives as a key, so we can send DISCONNECT

        try
        {
            Reconnect();
            while (true)
            {
                Render();
                var keyInfo = Console.ReadKey(intercept: true);
                if (IsQuitKey(keyInfo))
                    break;
                HandleKey(keyInfo);
            }
        }
        finally
        {
            // Also reached on an unexpected exception: always try to leave the inserter cleanly
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
            _style = _style with { Color = color };
            return;
        }

        switch (keyInfo.Key)
        {
            case ConsoleKey.X:
                _style = _style with { Boxed = !_style.Boxed };
                break;
            case ConsoleKey.H:
                _style = _style with { DoubleHeight = !_style.DoubleHeight };
                break;
            case ConsoleKey.V:
                _hexDump = !_hexDump;
                Log($"Hex dump {(_hexDump ? "on" : "off")}.");
                break;

            case ConsoleKey.T:
                _style = _style with { Position = VerticalPosition.Top };
                break;
            case ConsoleKey.N:
                _style = _style with { Position = VerticalPosition.Middle };
                break;
            case ConsoleKey.L:
                _style = _style with { Position = VerticalPosition.Lower };
                break;

            case ConsoleKey.D1 or ConsoleKey.NumPad1:
                SendSubtitle(TestSubtitles[0]);
                break;
            case ConsoleKey.D2 or ConsoleKey.NumPad2:
                SendSubtitle(TestSubtitles[1]);
                break;
            case ConsoleKey.D3 or ConsoleKey.NumPad3:
                SendSubtitle(TestSubtitles[2]);
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
            case ConsoleKey.P:
                ChangePage();
                break;
            case ConsoleKey.O:
                Reconnect();
                break;

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

    private void SendSubtitle(string[] lines)
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

            var unsupported = input
                .Where(c => c != SubtitleText.LineSeparator && c != '?' && Teletext.EncodeCharacter(c) == '?')
                .Distinct()
                .ToArray();
            if (unsupported.Length > 0)
                Log($"Warning: not in the teletext character set, sent as '?': {string.Join(" ", unsupported)}");

            _customLines = lines;
            SendSubtitle(lines);
            return;
        }
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
            Log($"Page changed to {_page}.");
        }
        else
            Log($"ERROR: {error}");
    }

    private string? PromptLine(string prompt)
    {
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

        var description = NewforPacketDecoder.Describe(packet.Bytes);
        Log($"TX {description[0]}");
        foreach (string line in description.Skip(1).Concat(HexDump.Format(packet.Bytes)))
            AddLogLine(DetailIndent + line);
    }

    private void Log(string message) => AddLogLine($"{DateTime.Now:HH:mm:ss} | {message}");

    private void AddLogLine(string line)
    {
        _log.Add(line);
        if (_log.Count > MaxLogEntries)
            _log.RemoveRange(0, _log.Count - MaxLogEntries);
        _logScroll = 0; // jump back to the newest entries
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

        string rule = new('=', 63);
        string connection = _client.IsConnected ? "CONNECTED" : "DISCONNECTED";
        string custom = _customLines is null ? "(none)" : SubtitleText.Join(_customLines);

        var lines = new List<string>
        {
            $" NEWFOR INJECTOR v{Version}  |  (c) 2026 Christopher Glover",
            rule,
            $" Target: {_client.Host}:{_client.Port} [{connection}]  |  Page: {_page}",
            rule,
            " COLOR:      [R] Red     [G] Green   [Y] Yellow  [B] Blue",
            "             [M] Magenta [A] Cyan    [W] White",
            " OPTIONS:    [X] Toggle Box  [H] Toggle Double Height  [V] Toggle Hex Dump",
            " POSITION:   [T] Top     [N] Middle  [L] Lower",
            " SUBTITLES:  [1] Single Line  [2] Two Lines  [3] Three Lines",
            $"             [E] Enter Text   [4] Resend: {custom}",
            " ACTIONS:    [C] Clear  [P] Change Page  [O] Reconnect  [Q] Quit",
            new string('-', 63),
            $" Color: {_style.Color,-7} | Box: {(_style.Boxed ? "ON" : "OFF"),-3} | " +
            $"Height: {(_style.DoubleHeight ? "DOUBLE" : "SINGLE"),-6} | " +
            $"Position: {_style.Position.ToString().ToUpperInvariant(),-6} | Hex: {(_hexDump ? "ON" : "OFF")}",
        };

        // The rest of the screen, minus the log title and the prompt line, shows the log
        int logRows = Math.Max(0, height - 2 - lines.Count);
        _logPageSize = Math.Max(1, logRows - 1);
        _logScroll = Math.Clamp(_logScroll, 0, Math.Max(0, _log.Count - logRows));

        lines.Add(_logScroll == 0
            ? "--- LOG ([PgUp]/[PgDn] scroll) " + new string('-', 32)
            : $"--- LOG (back {_logScroll} lines, [End] for latest) " + new string('-', 20));

        int first = Math.Max(0, _log.Count - logRows - _logScroll);
        lines.AddRange(_log.Skip(first).Take(logRows));

        for (int row = 0; row < height; row++)
        {
            string text = row < height - 1 && row < lines.Count ? lines[row] : "";
            Console.SetCursorPosition(0, row);
            Console.Write(text.Length > width ? text[..width] : text.PadRight(width));
        }

        Console.SetCursorPosition(0, height - 1);
    }

    private static string Describe(SubtitleStyle style) =>
        $"{style.Color}, {(style.Boxed ? "boxed" : "unboxed")}, " +
        $"{(style.DoubleHeight ? "double" : "single")} height, {style.Position.ToString().ToLowerInvariant()}";
}
