// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Net;
using System.Net.Sockets;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NewforTest.Core;
using NewforTest.Gui;
using NewforTest.Gui.ViewModels;

[assembly: AvaloniaTestApplication(typeof(NewforTest.Gui.Tests.TestAppBuilder))]

namespace NewforTest.Gui.Tests;

public static class TestAppBuilder
{
    // Real Skia rendering, so screenshots can be captured
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class MainWindowTests
{
    private static (MainViewModel Vm, MainWindow Window) Open(GuiSettings? settings = null)
    {
        var vm = new MainViewModel(settings ?? new GuiSettings());
        var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (vm, window);
    }

    private static string RowText(TeletextCell[] row) => new string(row.Select(c => c.Char).ToArray()).Trim();

    /// <summary>Pumps the UI thread until the condition holds (player and network events are posted to it).</summary>
    private static void WaitFor(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out");
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
    }

    [AvaloniaFact]
    public void Title_and_log_show_the_version()
    {
        var (vm, window) = Open();

        Assert.Equal($"Newfor Test {AppVersion.Display}", window.Title);
        Assert.Contains(vm.Log, line => line.EndsWith($"Newfor Test {AppVersion.Display}"));

        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Sending_while_not_connected_updates_the_preview_only()
    {
        var (vm, window) = Open();

        vm.SendTestCommand.Execute("2");
        Dispatcher.UIThread.RunJobs();

        var cells = vm.Screen.Render();
        Assert.Equal("THIS IS LINE ONE", RowText(cells[20]));
        Assert.Equal("THIS IS LINE TWO", RowText(cells[22]));
        Assert.Contains(vm.Log, line => line.Contains("(preview only)"));

        vm.ClearScreenCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.Screen.HasContent);

        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Style_changes_apply_to_the_next_subtitle()
    {
        var (vm, window) = Open();
        vm.SelectedColor = vm.Colors.Single(c => c.Color == TeletextColor.Yellow);
        vm.DoubleHeight = true;
        vm.Position = VerticalPosition.Top;

        vm.SendTestCommand.Execute("1");
        Dispatcher.UIThread.RunJobs();

        var row = vm.Screen.Render()[2];
        var t = row.First(c => c.Char == 'T');
        Assert.Equal((byte)TeletextColor.Yellow, t.Foreground);
        Assert.Equal(CellHeight.DoubleTop, t.Height);

        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Box_colour_shows_in_the_preview_and_shortens_the_line_limit()
    {
        var (vm, window) = Open();
        Assert.Equal(TeletextColor.Black, vm.SelectedBoxColor.Color); // default

        vm.SelectedBoxColor = vm.BoxColors.Single(c => c.Color == TeletextColor.Blue);
        vm.SendTestCommand.Execute("1");
        Dispatcher.UIThread.RunJobs();

        var t = vm.Screen.Render()[22].First(c => c.Char == 'T');
        Assert.True(t.Boxed);
        Assert.Equal((byte)TeletextColor.Blue, t.Background);

        vm.CustomText = "";
        Assert.Contains("32 characters", vm.CustomTextInfo);

        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Custom_text_is_checked_as_it_is_typed()
    {
        var (vm, window) = Open();

        vm.CustomText = "SHORT LINE\nANOTHER";
        Assert.Null(vm.CustomTextError);
        Assert.Contains("2 lines", vm.CustomTextInfo);

        vm.CustomText = new string('X', 40);
        Assert.Contains("at most 34", vm.CustomTextError);

        vm.Boxed = false; // unboxed rows fit 38: still too long
        Assert.Contains("at most 38", vm.CustomTextError);

        vm.CustomText = "HELLO\r\nWORLD\r\n";
        vm.SendCustomCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("WORLD", RowText(vm.Screen.Render()[22]));

        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Invalid_page_is_reported_and_blocks_sending()
    {
        var (vm, window) = Open();

        vm.PageText = "999";
        Assert.NotNull(vm.PageError);

        vm.SendTestCommand.Execute("1");
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.Screen.HasContent);

        vm.PageText = "150";
        Assert.Null(vm.PageError);

        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Loaded_file_plays_into_the_preview()
    {
        string path = Path.Combine(Path.GetTempPath(), $"NewforTestGui{Guid.NewGuid():N}.srt");
        File.WriteAllText(path, "1\n00:00:00,050 --> 00:00:00,400\nFIRST CUE\n\n2\n00:00:00,500 --> 00:00:00,900\n" + new string('X', 50) + "\n");
        var (vm, window) = Open();
        try
        {
            vm.LoadFile(path);

            Assert.True(vm.HasFile);
            Assert.Equal(2, vm.Cues.Count);
            Assert.True(vm.Cues[0].IsPlayable);
            Assert.False(vm.Cues[1].IsPlayable); // 50 characters
            Assert.Contains("1 can't be shown", vm.FileInfo);

            vm.PlayPauseCommand.Execute(null);
            WaitFor(() => vm.Screen.HasContent);

            Assert.Equal("FIRST CUE", RowText(vm.Screen.Render()[22]));
            Assert.True(vm.Cues[0].IsCurrent);
            Assert.Same(vm.Cues[0], vm.SelectedCue);
            Assert.Equal("Pause", vm.PlayPauseText);

            vm.StopCommand.Execute(null);
            WaitFor(() => !vm.Screen.HasContent);
        }
        finally
        {
            window.Close();
            vm.Dispose();
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public async Task Connects_sends_to_the_inserter_and_disconnects()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var (vm, window) = Open(new GuiSettings { Host = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port });
        try
        {
            var accept = listener.AcceptTcpClientAsync();
            await vm.ToggleConnectionCommand.ExecuteAsync(null);
            Assert.True(vm.IsConnected);
            Assert.Equal("Disconnect", vm.ConnectButtonText);
            Assert.False(vm.CanEditAddress);

            using var server = await accept;
            vm.SendTestCommand.Execute("1");
            await vm.ToggleConnectionCommand.ExecuteAsync(null); // disconnect
            Assert.False(vm.IsConnected);

            var received = new MemoryStream();
            await server.GetStream().CopyToAsync(received);
            var expected = NewforPackets.Subtitle(PageNumber.Parse("888"), TestSubtitles.All[0], new SubtitleStyle())
                .Append(NewforPackets.Disconnect())
                .SelectMany(p => p.Bytes);
            Assert.Equal(expected, received.ToArray());
        }
        finally
        {
            window.Close();
            vm.Dispose();
            listener.Stop();
        }
    }

    [AvaloniaFact]
    public async Task Failed_connection_stays_in_preview_mode()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop(); // nothing listening

        var (vm, window) = Open(new GuiSettings { Host = "127.0.0.1", Port = port });
        await vm.ToggleConnectionCommand.ExecuteAsync(null);

        Assert.False(vm.IsConnected);
        Assert.Contains("failed", vm.ConnectionStatus);
        Assert.Contains(vm.Log, line => line.Contains("ERROR"));

        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Settings_round_trip()
    {
        string path = Path.Combine(Path.GetTempPath(), $"NewforTestGui{Guid.NewGuid():N}.json");
        var (vm, window) = Open();
        try
        {
            vm.Host = "10.0.0.5";
            vm.PageText = "1FF";
            vm.SelectedColor = vm.Colors.Single(c => c.Color == TeletextColor.Cyan);
            vm.Justification = Justification.Centre;
            vm.CharacterSet = TeletextCharacterSet.German;
            vm.CaptureSettings().Save(path);

            var loaded = GuiSettings.Load(path);
            Assert.Equal("10.0.0.5", loaded.Host);
            Assert.Equal("1FF", loaded.Page);
            Assert.Equal(TeletextColor.Cyan, loaded.Color);
            Assert.Equal(Justification.Centre, loaded.Justification);
            Assert.Equal("German", loaded.CharacterSet);

            Assert.Equal("127.0.0.1", GuiSettings.Load(path + ".missing").Host); // defaults
        }
        finally
        {
            window.Close();
            vm.Dispose();
            File.Delete(path);
        }
    }

    /// <summary>
    /// Saves screenshots when NEWFORTEST_SCREENSHOTS is set to a folder, for checking the layout
    /// by eye. Otherwise just checks that the window renders.
    /// </summary>
    [AvaloniaFact]
    public void Renders_main_window()
    {
        string? folder = Environment.GetEnvironmentVariable("NEWFORTEST_SCREENSHOTS");
        var (vm, window) = Open();

        string srt = Path.Combine(Path.GetTempPath(), $"NewforTestGui{Guid.NewGuid():N}.srt");
        File.WriteAllLines(srt, Enumerable.Range(1, 30).SelectMany(i => new[]
        {
            i.ToString(),
            $"00:00:{i:00},000 --> 00:00:{i:00},800",
            i == 5 ? new string('X', 40) : $"Subtitle number {i}",
            "",
        }));
        vm.LoadFile(srt);
        File.Delete(srt);

        vm.SelectedColor = vm.Colors.Single(c => c.Color == TeletextColor.Yellow);
        vm.DoubleHeight = true;
        vm.Justification = Justification.Centre;
        vm.CustomText = "Custom text line\nSecond line";
        vm.SendTestCommand.Execute("2");
        vm.HexDump = true;
        vm.SendTestCommand.Execute("1");
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.Screen.HasContent);

        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (folder is not null)
        {
            Directory.CreateDirectory(folder);
            frame!.Save(Path.Combine(folder, "main-window.png"));
        }

        window.Close();
        vm.Dispose();
    }
}
