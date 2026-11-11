// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using NewforTest.Core;

namespace NewforTest.Cli;

/// <summary>
/// The keyboard-driven test console. The screen is redrawn in place after every key: the menu
/// and status at the top, then as many recent log lines as fit, with the bottom line kept free
/// for prompts. Nothing scrolls, so the status line can't be overwritten or pushed off screen.
/// </summary>
internal sealed class InteractiveSession
{
    private const int MaxLogEntries = 500;

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
    private bool _clearScreen = true;
    private (int Width, int Height) _lastSize;

    public InteractiveSession(NewforClient client, PageNumber page)
    {
        _client = client;
        _page = page;
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
                SendTestSubtitle(1);
                break;
            case ConsoleKey.D2 or ConsoleKey.NumPad2:
                SendTestSubtitle(2);
                break;
            case ConsoleKey.D3 or ConsoleKey.NumPad3:
                SendTestSubtitle(3);
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
        }
    }

    private void SendTestSubtitle(int lineCount)
    {
        var lines = TestSubtitles[lineCount - 1];
        var style = _style;
        var page = _page;
        Execute(
            () => _client.SendSubtitle(page, lines, style),
            $"Sent {lineCount}-line subtitle to page {page} ({Describe(style)}).");
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
        _clearScreen = true; // the Enter key may have scrolled the window
        return input;
    }

    private void Log(string message)
    {
        _log.Add($"{DateTime.Now:HH:mm:ss} | {message}");
        if (_log.Count > MaxLogEntries)
            _log.RemoveRange(0, _log.Count - MaxLogEntries);
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
        string thinRule = new('-', 63);
        string connection = _client.IsConnected ? "CONNECTED" : "DISCONNECTED";

        var lines = new List<string>
        {
            $" NEWFOR INJECTOR v{Version}  |  (c) 2026 Christopher Glover",
            rule,
            $" Target: {_client.Host}:{_client.Port} [{connection}]  |  Page: {_page}",
            rule,
            "",
            " COLOR:      [R] Red     [G] Green   [Y] Yellow  [B] Blue",
            "             [M] Magenta [A] Cyan    [W] White",
            " OPTIONS:    [X] Toggle Box   [H] Toggle Double Height",
            " POSITION:   [T] Top     [N] Middle  [L] Lower",
            " SUBTITLES:  [1] Single Line  [2] Two Lines  [3] Three Lines",
            " ACTIONS:    [C] Clear  [P] Change Page  [O] Reconnect  [Q] Quit",
            "",
            thinRule,
            $" Color: {_style.Color,-7} | Box: {(_style.Boxed ? "ON" : "OFF"),-3} | " +
            $"Height: {(_style.DoubleHeight ? "DOUBLE" : "SINGLE"),-6} | " +
            $"Position: {_style.Position.ToString().ToUpperInvariant(),-6}",
            thinRule,
        };

        // Fill the rest of the screen (minus the prompt line) with the newest log entries
        int logRows = Math.Max(0, height - 1 - lines.Count);
        lines.AddRange(_log.Skip(Math.Max(0, _log.Count - logRows)));

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
