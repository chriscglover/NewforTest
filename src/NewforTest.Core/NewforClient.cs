// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

using System.Net.Sockets;

namespace NewforTest.Core;

/// <summary>The connection to the inserter failed or was lost.</summary>
public sealed class NewforConnectionException : Exception
{
    public NewforConnectionException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Sends Newfor packets to a subtitle inserter over TCP. Reports failures by throwing
/// <see cref="NewforConnectionException"/>; writes nothing to the console. Thread-safe:
/// each call's packets go out together.
/// </summary>
public sealed class NewforClient : INewforOutput, IDisposable
{
    public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(5);

    private readonly object _sync = new();
    private TcpClient? _tcp;
    private NetworkStream? _stream;

    public NewforClient(string host, int port)
    {
        Host = host;
        Port = port;
    }

    public string Host { get; }
    public int Port { get; }

    /// <summary>True from a successful <see cref="Connect"/> until disconnect, dispose or a write failure.</summary>
    public bool IsConnected => _stream is not null;

    /// <summary>Raised after each packet is written (for hex dumps and logging).</summary>
    public event Action<NewforPacket>? PacketSent;

    /// <summary>Opens the TCP connection, closing any existing one first.</summary>
    /// <exception cref="NewforConnectionException">Connection refused, unreachable or timed out.</exception>
    public void Connect(TimeSpan? timeout = null)
    {
        lock (_sync)
            ConnectLocked(timeout);
    }

    private void ConnectLocked(TimeSpan? timeout)
    {
        Close();

        var tcp = new TcpClient { NoDelay = true }; // packets are tiny and timing matters: don't let Nagle batch them
        try
        {
            using var cts = new CancellationTokenSource(timeout ?? DefaultConnectTimeout);
            tcp.ConnectAsync(Host, Port, cts.Token).AsTask().GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            tcp.Dispose();
            throw new NewforConnectionException($"Timed out connecting to {Host}:{Port}.");
        }
        catch (SocketException ex)
        {
            tcp.Dispose();
            throw new NewforConnectionException($"Could not connect to {Host}:{Port}: {ex.Message}", ex);
        }

        _tcp = tcp;
        _stream = tcp.GetStream();
    }

    /// <summary>
    /// Sends a complete subtitle. All packets are built (and validated) before anything is
    /// written, so bad input never leaves a half-sent subtitle on the inserter.
    /// </summary>
    public void SendSubtitle(PageNumber page, IReadOnlyList<string> lines, SubtitleStyle style) =>
        Send(NewforPackets.Subtitle(page, lines, style));

    /// <inheritdoc cref="SendSubtitle(PageNumber, IReadOnlyList{string}, SubtitleStyle)"/>
    public void SendSubtitle(PageNumber page, IReadOnlyList<SubtitleLine> lines, SubtitleStyle style) =>
        Send(NewforPackets.Subtitle(page, lines, style));

    /// <summary>
    /// Writes a sequence of packets with nothing else in between, even if another thread
    /// (e.g. a <see cref="SubtitlePlayer"/>) is sending at the same time.
    /// </summary>
    public void Send(IEnumerable<NewforPacket> packets)
    {
        lock (_sync)
        {
            foreach (var packet in packets)
                Write(packet);
        }
    }

    public void Clear() => Send(new[] { NewforPackets.Clear() });

    /// <summary>Sends DISCONNECT and closes the connection. Does nothing if not connected.</summary>
    public void Disconnect()
    {
        lock (_sync)
        {
            if (!IsConnected)
                return;
            try
            {
                Write(NewforPackets.Disconnect());
            }
            finally
            {
                Close();
            }
        }
    }

    /// <summary>Writes one packet. Exposed so callers can send individual packets for protocol testing.</summary>
    public void Write(NewforPacket packet)
    {
        lock (_sync)
        {
            if (_stream is null)
                throw new NewforConnectionException("Not connected.");

            try
            {
                _stream.Write(packet.Bytes);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                Close();
                throw new NewforConnectionException($"Connection to {Host}:{Port} lost: {ex.Message}", ex);
            }

            PacketSent?.Invoke(packet);
        }
    }

    /// <summary>Closes the socket without sending DISCONNECT.</summary>
    public void Dispose()
    {
        lock (_sync)
            Close();
    }

    private void Close()
    {
        _stream?.Dispose();
        _tcp?.Dispose();
        _stream = null;
        _tcp = null;
    }
}
