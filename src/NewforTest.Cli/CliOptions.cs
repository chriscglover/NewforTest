// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using NewforTest.Core;

namespace NewforTest.Cli;

internal enum RunMode
{
    /// <summary>Keyboard-driven console (the default).</summary>
    Interactive,

    /// <summary>--send: send one subtitle and exit.</summary>
    Send,

    /// <summary>--clear: send CLEAR and exit.</summary>
    Clear,

    /// <summary>--play: play a subtitle file without the interactive console.</summary>
    Play,

    /// <summary>--soak: cycle the test subtitles until stopped.</summary>
    Soak,
}

/// <summary>Command-line arguments. Host, port and page left null are prompted for in interactive mode.</summary>
internal sealed record CliOptions
{
    public const string DefaultHost = "127.0.0.1";
    public const int DefaultPort = 1234;
    public const string DefaultPage = "888";
    public const double DefaultSoakSeconds = 5;

    public string? Host { get; init; }
    public int? Port { get; init; }
    public PageNumber? Page { get; init; }
    public bool HexDump { get; init; }
    public bool ShowHelp { get; init; }
    public SubtitleStyle Style { get; init; } = new();

    public RunMode Mode { get; init; }

    /// <summary>--send text, lines separated by '|'.</summary>
    public string? SendText { get; init; }

    /// <summary>File for --play (headless) or --load (interactive).</summary>
    public string? File { get; init; }

    public bool Loop { get; init; }

    /// <summary>--start timecode, parsed once the file's frame rate is known.</summary>
    public string? Start { get; init; }

    public double SoakSeconds { get; init; } = DefaultSoakSeconds;

    /// <summary>
    /// --hold: seconds to keep a --send subtitle up before clearing and disconnecting.
    /// Null means until Ctrl+C. Many receivers clear the subtitle when the connection closes.
    /// </summary>
    public double? HoldSeconds { get; init; }

    private static readonly HashSet<string> ValueOptions = new()
    {
        "--ip", "--host", "--port", "--page", "--color", "--colour", "--pos", "--position",
        "--justify", "--charset", "--send", "--play", "--load", "--start", "--soak", "--hold",
    };

    public static bool TryParse(
        string[] args,
        [NotNullWhen(true)] out CliOptions? options,
        [NotNullWhen(false)] out string? error)
    {
        var o = new CliOptions();
        options = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            switch (arg)
            {
                case "-h" or "--help" or "/?":
                    o = o with { ShowHelp = true };
                    continue;
                case "--hex":
                    o = o with { HexDump = true };
                    continue;
                case "--double":
                    o = o with { Style = o.Style with { DoubleHeight = true } };
                    continue;
                case "--no-box":
                    o = o with { Style = o.Style with { Boxed = false } };
                    continue;
                case "--loop":
                    o = o with { Loop = true };
                    continue;
                case "--clear":
                    if (!TrySetMode(ref o, RunMode.Clear, out error))
                        return false;
                    continue;
            }

            if (!ValueOptions.Contains(arg))
            {
                error = $"Unknown argument '{arg}'.";
                return false;
            }

            if (i + 1 >= args.Length)
            {
                error = $"{arg} needs a value.";
                return false;
            }

            string value = args[++i];
            switch (arg)
            {
                case "--ip" or "--host":
                    o = o with { Host = value };
                    break;

                case "--port":
                    if (!TryParsePort(value, out int port, out error))
                        return false;
                    o = o with { Port = port };
                    break;

                case "--page":
                    if (!PageNumber.TryParse(value, out var page, out error))
                        return false;
                    o = o with { Page = page };
                    break;

                case "--color" or "--colour":
                    if (!TryParseColor(value, out var color))
                    {
                        error = $"'{value}' is not a colour: use {string.Join(", ", Enum.GetNames<TeletextColor>()).ToLowerInvariant()}.";
                        return false;
                    }
                    o = o with { Style = o.Style with { Color = color } };
                    break;

                case "--pos" or "--position":
                    VerticalPosition? position = value.ToLowerInvariant() switch
                    {
                        "top" => VerticalPosition.Top,
                        "middle" or "centre" or "center" => VerticalPosition.Middle,
                        "lower" or "bottom" => VerticalPosition.Lower,
                        _ => null,
                    };
                    if (position is null)
                    {
                        error = $"'{value}' is not a position: use top, middle or lower.";
                        return false;
                    }
                    o = o with { Style = o.Style with { Position = position.Value } };
                    break;

                case "--justify":
                    Justification? justification = value.ToLowerInvariant() switch
                    {
                        "left" => Justification.Left,
                        "centre" or "center" => Justification.Centre,
                        "right" => Justification.Right,
                        _ => null,
                    };
                    if (justification is null)
                    {
                        error = $"'{value}' is not a justification: use left, centre or right.";
                        return false;
                    }
                    o = o with { Style = o.Style with { Justification = justification.Value } };
                    break;

                case "--charset":
                    var characterSet = TeletextCharacterSet.FromName(value);
                    if (characterSet is null)
                    {
                        error = $"'{value}' is not a character set: use {string.Join(", ", TeletextCharacterSet.All)}.";
                        return false;
                    }
                    o = o with { Style = o.Style with { CharacterSet = characterSet } };
                    break;

                case "--send":
                    if (!TrySetMode(ref o, RunMode.Send, out error))
                        return false;
                    o = o with { SendText = value };
                    break;

                case "--play":
                    if (!TrySetMode(ref o, RunMode.Play, out error))
                        return false;
                    o = o with { File = value };
                    break;

                case "--load":
                    o = o with { File = value };
                    break;

                case "--hold":
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double hold) || hold < 0)
                    {
                        error = $"'{value}' is not a valid hold time: expected seconds, 0 or more.";
                        return false;
                    }
                    o = o with { HoldSeconds = hold };
                    break;

                case "--start":
                    o = o with { Start = value };
                    break;

                case "--soak":
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) || seconds < 0.1)
                    {
                        error = $"'{value}' is not a valid soak interval: expected seconds, at least 0.1.";
                        return false;
                    }
                    if (!TrySetMode(ref o, RunMode.Soak, out error))
                        return false;
                    o = o with { SoakSeconds = seconds };
                    break;
            }
        }

        if (o.Mode == RunMode.Play && args.Contains("--load"))
        {
            error = "Use --play to play a file without the console, or --load to open it in the console; not both.";
            return false;
        }

        if (o.HoldSeconds is not null && o.Mode != RunMode.Send)
        {
            error = "--hold only applies to --send.";
            return false;
        }

        if ((o.Loop || o.Start is not null) && o.File is null)
        {
            error = "--loop and --start need a file (--play or --load).";
            return false;
        }

        options = o;
        error = null;
        return true;
    }

    private static bool TrySetMode(ref CliOptions o, RunMode mode, [NotNullWhen(false)] out string? error)
    {
        if (o.Mode != RunMode.Interactive && o.Mode != mode)
        {
            error = "Use only one of --send, --clear, --play and --soak.";
            return false;
        }

        o = o with { Mode = mode };
        error = null;
        return true;
    }

    private static bool TryParseColor(string value, out TeletextColor color) =>
        Enum.TryParse(value, ignoreCase: true, out color) && Enum.IsDefined(color) && !int.TryParse(value, out _);

    public static bool TryParsePort(string? text, out int port, [NotNullWhen(false)] out string? error)
    {
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is >= 1 and <= 65535)
        {
            error = null;
            return true;
        }

        error = $"'{text}' is not a valid port: expected a number from 1 to 65535.";
        return false;
    }

    public static void PrintUsage(TextWriter w)
    {
        w.WriteLine("Usage: NewforTest [connection] [style] [mode]");
        w.WriteLine();
        w.WriteLine("Connection:");
        w.WriteLine($"  --ip, --host <address>   Subtitle inserter address   (default {DefaultHost})");
        w.WriteLine($"  --port <port>            TCP port, 1-65535            (default {DefaultPort})");
        w.WriteLine($"  --page <page>            Teletext page, e.g. 888, 1FF (default {DefaultPage})");
        w.WriteLine();
        w.WriteLine("Style:");
        w.WriteLine("  --color <colour>         red, green, yellow, blue, magenta, cyan, white (default white)");
        w.WriteLine("  --double                 Double height");
        w.WriteLine("  --no-box                 No box");
        w.WriteLine("  --pos <position>         top, middle or lower (default lower)");
        w.WriteLine("  --justify <justify>      left, centre or right (default left)");
        w.WriteLine($"  --charset <set>          {string.Join(", ", TeletextCharacterSet.All.Select(s => s.Name.Split('/')[0].ToLowerInvariant()))}");
        w.WriteLine("                           (default english; must match the inserter)");
        w.WriteLine();
        w.WriteLine("Modes (default: interactive console):");
        w.WriteLine("  --send \"<text>\"          Send one subtitle; '|' separates lines. Stays up until Ctrl+C,");
        w.WriteLine("                           then CLEAR and DISCONNECT");
        w.WriteLine("  --hold <seconds>         With --send: keep it up this long, then exit (0 = exit at once;");
        w.WriteLine("                           many receivers clear the subtitle when the connection closes)");
        w.WriteLine("  --clear                  Send CLEAR and exit");
        w.WriteLine("  --play <file>            Play a subtitle file (.srt, .stl) without the console; Ctrl+C stops");
        w.WriteLine("  --load <file>            Open the console with a subtitle file loaded");
        w.WriteLine("  --soak <seconds>         Cycle the test subtitles every N seconds until Ctrl+C");
        w.WriteLine("  --loop                   With --play/--load: repeat from the start");
        w.WriteLine("  --start <timecode>       With --play/--load: start position, e.g. 00:01:30:00 or 90");
        w.WriteLine();
        w.WriteLine("Other:");
        w.WriteLine("  --hex                    Show every packet sent as a decoded hex dump");
        w.WriteLine("  -h, --help               Show this help");
        w.WriteLine();
        w.WriteLine("Examples:");
        w.WriteLine("  NewforTest --ip 10.0.0.5 --page 888 --send \"HELLO|WORLD\" --color yellow --double --hold 10");
        w.WriteLine("  NewforTest --ip 10.0.0.5 --play film.srt --justify centre --loop");
        w.WriteLine();
        w.WriteLine("Exit codes: 0 success, 1 connection failed, 2 bad arguments or file.");
    }
}
