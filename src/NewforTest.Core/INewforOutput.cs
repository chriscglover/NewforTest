// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

namespace NewforTest.Core;

/// <summary>Somewhere Newfor packets can be sent: a real inserter, or a preview with no connection.</summary>
public interface INewforOutput
{
    /// <summary>Raised after each packet is sent.</summary>
    event Action<NewforPacket>? PacketSent;

    /// <summary>Sends packets with nothing else in between.</summary>
    /// <exception cref="NewforConnectionException">The connection failed (real outputs only).</exception>
    void Send(IEnumerable<NewforPacket> packets);
}

/// <summary>
/// An output with no connection: packets only raise <see cref="PacketSent"/>. Used to rehearse
/// subtitles and files in a preview without a receiver.
/// </summary>
public sealed class OfflineOutput : INewforOutput
{
    public event Action<NewforPacket>? PacketSent;

    public void Send(IEnumerable<NewforPacket> packets)
    {
        foreach (var packet in packets)
            PacketSent?.Invoke(packet);
    }
}
