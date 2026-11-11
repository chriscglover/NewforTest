// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewforTest.Core;

namespace NewforTest.Gui.ViewModels;

/// <summary>
/// Everything behind the main window. Sends go to the inserter when connected, otherwise to the
/// preview only, so subtitles and files can be rehearsed without a receiver. The preview is
/// always built from the packets actually sent.
/// </summary>
/// <remarks>
/// Threading: properties and collections are only touched on the UI thread. Packet and player
/// events arrive on other threads and are posted to the UI thread.
/// </remarks>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private const int MaxLogLines = 5000;

    private readonly OfflineOutput _offline = new();
    private readonly RoutedOutput _output;
    private readonly DispatcherTimer _positionTimer;
    private NewforClient? _client;
    private SubtitlePlayer? _player;
    private SubtitlePlayer? _soakPlayer;
    private PageNumber? _page;
    private bool _disposed;

    public MainViewModel(GuiSettings settings)
    {
        _output = new RoutedOutput(() => _client is { IsConnected: true } client ? client : _offline);
        _offline.PacketSent += OnPacketSent;

        _host = settings.Host;
        _portText = settings.Port.ToString();
        _pageText = settings.Page;
        _page = PageNumber.TryParse(settings.Page, out var page, out _) ? page : PageNumber.Parse("888");
        _selectedColor = ColorOption.All.FirstOrDefault(c => c.Color == settings.Color) ?? ColorOption.All[0];
        _boxed = settings.Boxed;
        _selectedBoxColor = ColorOption.BoxColors.FirstOrDefault(c => c.Color == settings.BoxColor) ?? ColorOption.BoxColors[0];
        _doubleHeight = settings.DoubleHeight;
        _position = settings.Position;
        _justification = settings.Justification;
        _characterSet = TeletextCharacterSet.FromName(settings.CharacterSet) ?? TeletextCharacterSet.English;
        _soakSeconds = settings.SoakSeconds;
        _hexDump = settings.HexDump;
        _customText = settings.CustomText;
        LastFolder = settings.LastFolder;

        Screen.CharacterSet = _characterSet;
        ValidateCustomText();

        _positionTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => UpdatePosition());

        AddLog($"Newfor Test {AppVersion.Display}");
        AddLog("Not connected: anything sent goes to the preview only. Enter the inserter's address and press Connect.");
    }

    // ---------------------------------------------------------------- Shared state

    /// <summary>What the inserter has been sent, for the preview.</summary>
    public TeletextScreen Screen { get; } = new();

    public ObservableCollection<string> Log { get; } = new();

    public ObservableCollection<CueItem> Cues { get; } = new();

    public IReadOnlyList<ColorOption> Colors { get; } = ColorOption.All;

    public IReadOnlyList<ColorOption> BoxColors { get; } = ColorOption.BoxColors;

    public IReadOnlyList<VerticalPosition> Positions { get; } = Enum.GetValues<VerticalPosition>();

    public IReadOnlyList<Justification> Justifications { get; } = Enum.GetValues<Justification>();

    public IReadOnlyList<TeletextCharacterSet> CharacterSets { get; } = TeletextCharacterSet.All;

    /// <summary>Set by the view: shows an open-file dialog and returns the chosen path.</summary>
    public Func<Task<string?>>? PickFile { get; set; }

    /// <summary>Set by the view: copies text to the clipboard.</summary>
    public Func<string, Task>? CopyToClipboard { get; set; }

    public string? LastFolder { get; private set; }

    public SubtitleStyle Style => new(SelectedColor.Color, DoubleHeight, Boxed, Position, Justification)
    {
        CharacterSet = CharacterSet,
        BoxColor = SelectedBoxColor.Color,
    };

    // ---------------------------------------------------------------- Connection

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectButtonText), nameof(StatusBrush), nameof(CanEditAddress))]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectButtonText), nameof(CanEditAddress))]
    private bool _isConnecting;

    [ObservableProperty] private string _host;
    [ObservableProperty] private string _portText;
    [ObservableProperty] private string _pageText;
    [ObservableProperty] private string? _pageError;
    [ObservableProperty] private string _connectionStatus = "Not connected: preview only";

    public string ConnectButtonText => IsConnecting ? "Connecting..." : IsConnected ? "Disconnect" : "Connect";

    public bool CanEditAddress => !IsConnected && !IsConnecting;

    public IBrush StatusBrush => IsConnected ? Brushes.LimeGreen : Brushes.Gray;

    [RelayCommand]
    private async Task ToggleConnectionAsync()
    {
        if (IsConnecting)
            return;

        if (IsConnected)
        {
            Disconnect();
            return;
        }

        if (!int.TryParse(PortText, out int port) || port is < 1 or > 65535)
        {
            AddLog($"ERROR: '{PortText}' is not a valid port (1-65535).");
            return;
        }

        string host = Host.Trim();
        var client = new NewforClient(host, port);
        IsConnecting = true;
        ConnectionStatus = $"Connecting to {host}:{port}...";

        try
        {
            await Task.Run(() => client.Connect());
        }
        catch (NewforConnectionException ex)
        {
            client.Dispose();
            IsConnecting = false;
            ConnectionStatus = "Connection failed: preview only";
            AddLog($"ERROR: {ex.Message}");
            return;
        }

        client.PacketSent += OnPacketSent;
        _client?.Dispose();
        _client = client;
        IsConnecting = false;
        IsConnected = true;
        ConnectionStatus = $"Connected to {host}:{port}";
        AddLog($"Connected to {host}:{port}. Subtitles now go to the inserter.");
    }

    private void Disconnect()
    {
        if (_client is null)
            return;

        try
        {
            _client.Disconnect();
            AddLog("DISCONNECT sent. Anything sent now goes to the preview only.");
        }
        catch (NewforConnectionException ex)
        {
            AddLog($"ERROR sending DISCONNECT: {ex.Message}");
        }

        MarkDisconnected("Not connected: preview only");
    }

    private void MarkDisconnected(string status)
    {
        IsConnected = false;
        ConnectionStatus = status;
        Screen.Reset(); // the receiver clears when the session ends
    }

    partial void OnPageTextChanged(string value)
    {
        if (PageNumber.TryParse(value, out var page, out string? error))
        {
            _page = page;
            PageError = null;
            if (_player is not null)
                _player.Page = page;
            if (_soakPlayer is not null)
                _soakPlayer.Page = page;
        }
        else
        {
            // Nothing is sent until the page is valid again (a running player keeps its last page)
            _page = null;
            PageError = error;
        }
    }

    // ---------------------------------------------------------------- Style

    [ObservableProperty] private ColorOption _selectedColor;
    [ObservableProperty] private bool _boxed;
    [ObservableProperty] private ColorOption _selectedBoxColor;
    [ObservableProperty] private bool _doubleHeight;
    [ObservableProperty] private VerticalPosition _position;
    [ObservableProperty] private Justification _justification;
    [ObservableProperty] private TeletextCharacterSet _characterSet;

    partial void OnSelectedColorChanged(ColorOption value) => StyleChanged();
    partial void OnBoxedChanged(bool value) => StyleChanged();
    partial void OnSelectedBoxColorChanged(ColorOption value) => StyleChanged();
    partial void OnDoubleHeightChanged(bool value) => StyleChanged();
    partial void OnPositionChanged(VerticalPosition value) => StyleChanged();
    partial void OnJustificationChanged(Justification value) => StyleChanged();

    partial void OnCharacterSetChanged(TeletextCharacterSet value)
    {
        Screen.CharacterSet = value;
        StyleChanged();
    }

    private void StyleChanged()
    {
        var style = Style;
        if (_player is not null)
            _player.Style = style; // applies from the next cue
        if (_soakPlayer is not null)
            _soakPlayer.Style = style;

        foreach (var cue in Cues)
            cue.Validate(style);

        ValidateCustomText();
    }

    // ---------------------------------------------------------------- Sending

    [ObservableProperty] private string _customText;
    [ObservableProperty] private string? _customTextError;
    [ObservableProperty] private string? _customTextInfo;

    partial void OnCustomTextChanged(string value) => ValidateCustomText();

    private string[] CustomLines() =>
        CustomText.Replace("\r", "").TrimEnd('\n').Split('\n');

    private void ValidateCustomText()
    {
        if (string.IsNullOrWhiteSpace(CustomText))
        {
            CustomTextError = null;
            CustomTextInfo = $"Up to {SubtitleRowBuilder.MaxTextLength(Style)} characters per line.";
            return;
        }

        string[] lines = CustomLines();
        if (SubtitleText.TryValidate(lines, Style, out string? error))
        {
            CustomTextError = null;
            int longest = lines.Max(l => l.Length);
            CustomTextInfo = $"{lines.Length} line{(lines.Length == 1 ? "" : "s")}, longest {longest} of {SubtitleRowBuilder.MaxTextLength(Style)} characters.";
        }
        else
        {
            CustomTextError = error;
            CustomTextInfo = null;
        }
    }

    /// <summary>Sends one of the built-in test subtitles (1, 2 or 3 lines).</summary>
    [RelayCommand]
    private void SendTest(string lineCount) =>
        SendSubtitle(TestSubtitles.All[int.Parse(lineCount) - 1]);

    [RelayCommand]
    private void SendCustom()
    {
        if (string.IsNullOrWhiteSpace(CustomText))
        {
            AddLog("Type some text to send first.");
            return;
        }

        var unsupported = new HashSet<char>();
        CharacterSet.Encode(CustomText.Replace("\r", "").Replace("\n", ""), unsupported);
        if (unsupported.Count > 0)
            AddLog($"Warning: not in the {CharacterSet} teletext set, sent as '?': {string.Join(" ", unsupported)}");

        SendSubtitle(CustomLines());
    }

    [RelayCommand]
    private void ClearScreen() => Send(new[] { NewforPackets.Clear() }, "CLEAR sent.");

    private void SendSubtitle(IReadOnlyList<string> lines)
    {
        if (_page is not { } page)
        {
            AddLog($"ERROR: {PageError}");
            return;
        }

        IReadOnlyList<NewforPacket> packets;
        try
        {
            packets = NewforPackets.Subtitle(page, lines, Style);
        }
        catch (ArgumentException ex)
        {
            AddLog($"ERROR: {ex.Message}");
            return;
        }

        Send(packets, $"Sent to page {page}: {SubtitleText.Join(lines)}");
    }

    private void Send(IReadOnlyList<NewforPacket> packets, string description)
    {
        bool live = IsConnected;
        try
        {
            _output.Send(packets);
            AddLog(live ? description : $"{description} (preview only)");
        }
        catch (NewforConnectionException ex)
        {
            AddLog($"ERROR: {ex.Message}");
            MarkDisconnected("Connection lost: preview only");
        }
    }

    // ---------------------------------------------------------------- Soak test

    [ObservableProperty] private decimal? _soakSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SoakButtonText))]
    private bool _isSoakRunning;

    public string SoakButtonText => IsSoakRunning ? "Stop soak test" : "Start soak test";

    [RelayCommand]
    private void ToggleSoak()
    {
        if (IsSoakRunning)
        {
            StopSoak();
            return;
        }

        double seconds = (double)(SoakSeconds ?? 5);
        if (seconds < 0.1)
        {
            AddLog("ERROR: the soak test interval must be at least 0.1 s.");
            return;
        }

        _player?.Pause();
        var player = new SubtitlePlayer(_output, TestSubtitles.CreateSoakTest(TimeSpan.FromSeconds(seconds)), _page ?? PageNumber.Parse("888"), Style)
        {
            Loop = true,
        };
        player.LoopCompleted += n => Post(() => AddLog($"Soak test: {n * TestSubtitles.All.Count} subtitles sent."));
        player.ConnectionFailed += ex => Post(() =>
        {
            AddLog($"ERROR: {ex.Message} Soak test stopped.");
            MarkDisconnected("Connection lost: preview only");
            StopSoak();
        });

        _soakPlayer = player;
        IsSoakRunning = true;
        player.Play();
        AddLog($"Soak test started: a test subtitle every {seconds:0.###} s{(IsConnected ? "" : " (preview only)")}.");
    }

    private void StopSoak()
    {
        if (_soakPlayer is null)
            return;
        _soakPlayer.Stop();
        _soakPlayer.Dispose();
        _soakPlayer = null;
        IsSoakRunning = false;
        AddLog("Soak test stopped.");
    }

    // ---------------------------------------------------------------- File playback

    [ObservableProperty] private string _fileInfo = "No file loaded. Open an .srt or .stl file, or drop one on the window.";
    [ObservableProperty] private bool _hasFile;
    [ObservableProperty] private bool _loop;
    [ObservableProperty] private bool _followPlayback = true;
    [ObservableProperty] private string _positionText = "--:--:--:--";
    [ObservableProperty] private CueItem? _selectedCue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseText))]
    private PlaybackState _playbackState;

    public string PlayPauseText => PlaybackState == PlaybackState.Playing ? "Pause" : "Play";

    partial void OnLoopChanged(bool value)
    {
        if (_player is not null)
            _player.Loop = value;
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        if (PickFile is null)
            return;
        string? path = await PickFile();
        if (path is not null)
            LoadFile(path);
    }

    public void LoadFile(string path)
    {
        SubtitleDocument document;
        try
        {
            document = SubtitleFile.Load(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FormatException)
        {
            AddLog($"ERROR: could not load {Path.GetFileName(path)}: {ex.Message}");
            return;
        }

        LastFolder = Path.GetDirectoryName(path);
        ClosePlayer();

        foreach (string warning in document.Warnings.Take(20))
            AddLog($"Warning: {warning}");
        if (document.Warnings.Count > 20)
            AddLog($"Warning: ... and {document.Warnings.Count - 20} more.");

        double fps = document.FrameRate;
        var style = Style;
        Cues.Clear();
        for (int i = 0; i < document.Cues.Count; i++)
        {
            var item = new CueItem(document.Cues[i], i, fps);
            item.Validate(style);
            Cues.Add(item);
        }

        var player = new SubtitlePlayer(_output, document, _page ?? PageNumber.Parse("888"), style) { Loop = Loop };
        player.CueShown += e => Post(() => OnCueShown(e));
        player.CueCleared += cue => Post(() => SetCurrentCue(-1));
        player.CueFailed += (cue, ex) => Post(() => AddLog($"#{cue.Number} SKIPPED: {ex.Message}"));
        player.StateChanged += state => Post(() =>
        {
            PlaybackState = state;
            UpdatePosition();
            if (state == PlaybackState.Playing)
                _positionTimer.Start();
            else
                _positionTimer.Stop();
        });
        player.LoopCompleted += n => Post(() => AddLog($"Loop {n} complete, starting again."));
        player.Finished += () => Post(() => AddLog("Playback finished."));
        player.ConnectionFailed += ex => Post(() =>
        {
            AddLog($"ERROR: {ex.Message} Playback stopped.");
            MarkDisconnected("Connection lost: preview only");
        });
        _player = player;

        int bad = Cues.Count(c => !c.IsPlayable);
        string span = document.Cues.Count > 0
            ? $"{Timecode.Format(document.Cues[0].Start, fps)} to {Timecode.Format(document.Cues[^1].End, fps)}"
            : "no subtitles";
        FileInfo = $"{document.Name}: {document.Format}, {document.Cues.Count} cues, {span}" +
                   (bad > 0 ? $", {bad} can't be shown" : "") +
                   (document.Title is { } title ? $"  ({title})" : "");
        HasFile = true;
        PlaybackState = PlaybackState.Stopped;
        UpdatePosition();
        AddLog($"Loaded {document}.");
    }

    [RelayCommand]
    private void PlayPause()
    {
        if (_player is null)
            return;
        if (IsSoakRunning)
            StopSoak();
        _player.TogglePause();
    }

    [RelayCommand]
    private void Stop() => _player?.Stop();

    [RelayCommand]
    private void NextCue() => _player?.NextCue();

    [RelayCommand]
    private void PreviousCue() => _player?.PreviousCue();

    /// <summary>Jumps to a cue (shows it straight away, and plays on from there if playing).</summary>
    [RelayCommand]
    private void GoToCue(CueItem? cue)
    {
        if (cue is not null)
            _player?.Seek(cue.Cue.Start);
    }

    private void OnCueShown(CueShownEventArgs e)
    {
        SetCurrentCue(e.Index);
        if (FollowPlayback && e.Index < Cues.Count)
            SelectedCue = Cues[e.Index];

        string timing = e.Lateness > TimeSpan.FromSeconds(1) ? "joined late" : $"+{e.Lateness.TotalMilliseconds:0.0} ms";
        AddLog($"#{e.Cue.Number} {Timecode.Format(e.Cue.Start, _player?.Document.FrameRate ?? 25)} ({timing}){(IsConnected ? "" : " (preview only)")}: {e.Cue.Text}");
    }

    private void SetCurrentCue(int index)
    {
        foreach (var cue in Cues)
            cue.IsCurrent = cue.Index == index;
    }

    private void UpdatePosition()
    {
        if (_player is { } player)
            PositionText = Timecode.Format(player.Position, player.Document.FrameRate);
    }

    private void ClosePlayer()
    {
        _positionTimer.Stop();
        if (_player is null)
            return;
        _player.Stop();
        _player.Dispose();
        _player = null;
        HasFile = false;
    }

    // ---------------------------------------------------------------- Log

    [ObservableProperty] private bool _hexDump;

    partial void OnHexDumpChanged(bool value) => AddLog($"Hex dump {(value ? "on" : "off")}.");

    [RelayCommand]
    private void ClearLog() => Log.Clear();

    [RelayCommand]
    private async Task CopyLogAsync()
    {
        if (CopyToClipboard is not null)
            await CopyToClipboard(string.Join(Environment.NewLine, Log));
    }

    public void AddLog(string message) => AddLogLine($"{DateTime.Now:HH:mm:ss.fff}  {message}");

    private void AddLogLine(string line)
    {
        Log.Add(line);
        if (Log.Count > MaxLogLines)
        {
            for (int i = 0; i < 500; i++)
                Log.RemoveAt(0);
        }
    }

    /// <summary>Every packet sent (to the inserter or the preview) updates the preview and the hex dump.</summary>
    private void OnPacketSent(NewforPacket packet) => Post(() =>
    {
        Screen.Apply(packet);
        if (!HexDump)
            return;

        var description = NewforPacketDecoder.Describe(packet.Bytes, CharacterSet);
        AddLog($"TX {description[0]}");
        foreach (string line in description.Skip(1).Concat(Core.HexDump.Format(packet.Bytes)))
            AddLogLine($"              {line}");
    });

    private static void Post(Action action) => Dispatcher.UIThread.Post(action);

    // ---------------------------------------------------------------- Lifetime

    public GuiSettings CaptureSettings() => new()
    {
        Host = Host,
        Port = int.TryParse(PortText, out int port) ? port : 1234,
        Page = _page?.ToString() ?? "888",
        Color = SelectedColor.Color,
        Boxed = Boxed,
        BoxColor = SelectedBoxColor.Color,
        DoubleHeight = DoubleHeight,
        Position = Position,
        Justification = Justification,
        CharacterSet = CharacterSet.Name,
        SoakSeconds = SoakSeconds ?? 5,
        HexDump = HexDump,
        CustomText = CustomText,
        LastFolder = LastFolder,
    };

    /// <summary>Stops playback (clearing the screen) and disconnects cleanly.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _positionTimer.Stop();
        _soakPlayer?.Stop();
        _soakPlayer?.Dispose();
        ClosePlayer();

        if (_client is { IsConnected: true })
        {
            try
            {
                _client.Disconnect();
            }
            catch (NewforConnectionException)
            {
                // already gone
            }
        }
        _client?.Dispose();
    }

    /// <summary>Sends to whichever output is current: the inserter when connected, else the preview.</summary>
    private sealed class RoutedOutput : INewforOutput
    {
        private readonly Func<INewforOutput> _target;

        public RoutedOutput(Func<INewforOutput> target) => _target = target;

        // The view model listens to the real outputs directly
        public event Action<NewforPacket>? PacketSent
        {
            add { }
            remove { }
        }

        public void Send(IEnumerable<NewforPacket> packets) => _target().Send(packets);
    }
}
