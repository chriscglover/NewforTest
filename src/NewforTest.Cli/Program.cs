// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using NewforTest.Core;

namespace NewforTest.Cli;

internal static class Program
{
    private delegate bool TryParser<T>(string text, out T value, out string? error);

    private static int Main(string[] args)
    {
        // Subtitle text is full of curly quotes, accents and £ signs: don't let the console mangle them
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (!CliOptions.TryParse(args, out var options, out var error))
        {
            Console.Error.WriteLine($"Error: {error}");
            Console.Error.WriteLine("Run NewforTest --help for usage.");
            return ExitCodes.BadInput;
        }

        if (options.ShowHelp)
        {
            CliOptions.PrintUsage(Console.Out);
            return ExitCodes.Success;
        }

        if (options.ShowVersion)
        {
            Console.WriteLine($"NewforTest {AppVersion.Display}");
            return ExitCodes.Success;
        }

        if (options.Mode != RunMode.Interactive)
            return HeadlessRunner.Run(options);

        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            Console.Error.WriteLine("The interactive console needs a terminal. For scripts use --send, --clear, --play or --soak.");
            return ExitCodes.BadInput;
        }

        // Prompt for anything not given on the command line
        string host = options.Host ?? Prompt("IP address", CliOptions.DefaultHost,
            (string s, out string v, out string? e) => { v = s; e = null; return true; });

        int port = options.Port ?? Prompt("port", CliOptions.DefaultPort.ToString(),
            (string s, out int v, out string? e) => CliOptions.TryParsePort(s, out v, out e));

        PageNumber page = options.Page ?? Prompt("page", CliOptions.DefaultPage,
            (string s, out PageNumber v, out string? e) => PageNumber.TryParse(s, out v, out e));

        using var client = new NewforClient(host, port);
        new InteractiveSession(client, page, options).Run();
        return ExitCodes.Success;
    }

    /// <summary>Asks for a value until a valid one is entered; Enter alone takes the default.</summary>
    private static T Prompt<T>(string label, string defaultValue, TryParser<T> tryParse)
    {
        while (true)
        {
            Console.Write($"Enter {label} (default {defaultValue}): ");
            string? input = Console.ReadLine();
            string text = string.IsNullOrWhiteSpace(input) ? defaultValue : input.Trim();

            if (tryParse(text, out T value, out string? error))
                return value;

            Console.WriteLine($"  {error}");
        }
    }
}

internal static class ExitCodes
{
    public const int Success = 0;
    public const int ConnectionFailed = 1;
    public const int BadInput = 2;
}
