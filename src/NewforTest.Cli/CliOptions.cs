// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using NewforTest.Core;

namespace NewforTest.Cli;

/// <summary>Command-line arguments. Values left null are prompted for interactively.</summary>
internal sealed record CliOptions(string? Host, int? Port, PageNumber? Page, bool HexDump, bool ShowHelp)
{
    public const string DefaultHost = "127.0.0.1";
    public const int DefaultPort = 1234;
    public const string DefaultPage = "888";

    public static bool TryParse(
        string[] args,
        [NotNullWhen(true)] out CliOptions? options,
        [NotNullWhen(false)] out string? error)
    {
        options = null;
        string? host = null;
        int? port = null;
        PageNumber? page = null;
        bool help = false;
        bool hexDump = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (arg is "-h" or "--help" or "/?")
            {
                help = true;
                continue;
            }

            if (arg == "--hex")
            {
                hexDump = true;
                continue;
            }

            if (arg is not ("--ip" or "--host" or "--port" or "--page"))
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
                    host = value;
                    break;

                case "--port":
                    if (!TryParsePort(value, out int p, out error))
                        return false;
                    port = p;
                    break;

                case "--page":
                    if (!PageNumber.TryParse(value, out var pg, out error))
                        return false;
                    page = pg;
                    break;
            }
        }

        options = new CliOptions(host, port, page, hexDump, help);
        error = null;
        return true;
    }

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

    public static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("Usage: NewforTest [--ip <address>] [--port <port>] [--page <page>] [--hex]");
        writer.WriteLine();
        writer.WriteLine($"  --ip, --host <address>  Subtitle inserter address    (default {DefaultHost})");
        writer.WriteLine($"  --port <port>           TCP port, 1-65535             (default {DefaultPort})");
        writer.WriteLine($"  --page <page>           Teletext page, e.g. 888, 1FF  (default {DefaultPage})");
        writer.WriteLine("  --hex                   Start with the packet hex dump on ([V] toggles it)");
        writer.WriteLine("  -h, --help              Show this help");
        writer.WriteLine();
        writer.WriteLine("Anything not given is prompted for.");
    }
}
