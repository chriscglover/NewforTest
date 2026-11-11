// Copyright (c) 2026 Christopher Glover. MIT License - see LICENSE.txt.

namespace NewforTest.Core;

public enum CellHeight
{
    Normal,

    /// <summary>Top half of a double-height character (drawn over this row and the next).</summary>
    DoubleTop,

    /// <summary>Bottom half of the double-height character in the row above.</summary>
    DoubleBottom,
}

/// <summary>One character cell as a decoder would show it.</summary>
/// <param name="Foreground">Colour 0-7 (black, red, green, yellow, blue, magenta, cyan, white).</param>
/// <param name="Background">Colour 0-7.</param>
/// <param name="Boxed">Inside a box. Subtitle decoders show the background only for boxed cells.</param>
public readonly record struct TeletextCell(char Char, byte Foreground, byte Background, bool Boxed, CellHeight Height, bool Flash)
{
    public static readonly TeletextCell Blank = new(' ', 7, 0, false, CellHeight.Normal, false);

    public bool IsBlank => Char == ' ' && !Boxed;
}

/// <summary>
/// A model of what a Newfor inserter puts on screen, built from the packets actually sent:
/// BUILD writes rows to a buffer, REVEAL shows the buffer, CLEAR blanks the screen and
/// DISCONNECT ends the session. <see cref="Render"/> applies teletext (WST level 1) attribute
/// rules to give each cell's character, colours, boxing and height.
/// </summary>
/// <remarks>Not thread-safe: apply packets and render on one thread (e.g. the UI thread).</remarks>
public sealed class TeletextScreen
{
    public const int Rows = 25;
    public const int Columns = Teletext.RowLength;

    private readonly byte[]?[] _buffer = new byte[]?[Rows];
    private readonly byte[]?[] _display = new byte[]?[Rows];

    /// <summary>Page from the last CONNECT, or null after DISCONNECT.</summary>
    public PageNumber? Page { get; private set; }

    /// <summary>The national option used to show characters.</summary>
    public TeletextCharacterSet CharacterSet
    {
        get => _characterSet;
        set
        {
            if (_characterSet == value)
                return;
            _characterSet = value;
            Changed?.Invoke();
        }
    }

    private TeletextCharacterSet _characterSet = TeletextCharacterSet.English;

    /// <summary>True if any row is on screen.</summary>
    public bool HasContent => _display.Any(r => r is not null);

    /// <summary>Raised when what's on screen may have changed.</summary>
    public event Action? Changed;

    public void Apply(NewforPacket packet) => Apply(packet.Bytes);

    /// <summary>Applies one packet. Malformed packets are ignored.</summary>
    public void Apply(ReadOnlySpan<byte> packet)
    {
        if (packet.IsEmpty)
            return;

        switch (packet[0])
        {
            case NewforPackets.ConnectCode when packet.Length >= 5:
                if (Teletext.TryDecodeHamming84(packet[2], out int m) &&
                    Teletext.TryDecodeHamming84(packet[3], out int t) &&
                    Teletext.TryDecodeHamming84(packet[4], out int u))
                {
                    if (m == 9 && t == 9 && u == 9)
                        Reset(); // DISCONNECT
                    else if (m is >= 1 and <= 8)
                        Page = PageNumber.Create(m, t, u);
                }
                break;

            case NewforPackets.BuildCode or 0x0F:
                ApplyBuild(packet);
                break;

            case NewforPackets.RevealCode:
                for (int r = 0; r < Rows; r++)
                    _display[r] = _buffer[r]?.ToArray();
                Changed?.Invoke();
                break;

            case NewforPackets.ClearCode or 0x18:
                Array.Clear(_display);
                Changed?.Invoke();
                break;
        }
    }

    /// <summary>Clears everything, as at the end of a session.</summary>
    public void Reset()
    {
        Array.Clear(_buffer);
        Array.Clear(_display);
        Page = null;
        Changed?.Invoke();
    }

    /// <summary>The screen as cells, [row][column]. Row 0 is the (unused) header row.</summary>
    public TeletextCell[][] Render()
    {
        var cells = new TeletextCell[Rows][];
        for (int r = 0; r < Rows; r++)
        {
            if (r > 0 && cells[r - 1].Any(c => c.Height == CellHeight.DoubleTop))
            {
                // A row below double height shows the lower halves; its own content isn't displayed
                cells[r] = cells[r - 1].Select(above => above.Height == CellHeight.DoubleTop
                    ? above with { Height = CellHeight.DoubleBottom }
                    : above with { Char = ' ', Height = CellHeight.Normal }).ToArray();
                continue;
            }

            cells[r] = _display[r] is { } data ? DecodeRow(data, CharacterSet) : BlankRow();
        }

        return cells;
    }

    /// <summary>Applies WST level 1 attribute rules to one row.</summary>
    public static TeletextCell[] DecodeRow(ReadOnlySpan<byte> data, TeletextCharacterSet? characterSet = null)
    {
        characterSet ??= TeletextCharacterSet.English;
        var cells = new TeletextCell[Columns];

        byte fg = 7, bg = 0;
        bool boxed = false, doubleHeight = false, flash = false, graphics = false;

        for (int c = 0; c < Columns; c++)
        {
            int code = c < data.Length ? data[c] & 0x7F : 0x20;

            if (code >= 0x20)
            {
                // Mosaic characters aren't used in subtitles; show them as spaces
                char ch = graphics && code is < 0x40 or >= 0x60 ? ' ' : characterSet.Decode((byte)code);
                cells[c] = new TeletextCell(ch, fg, bg, boxed, doubleHeight ? CellHeight.DoubleTop : CellHeight.Normal, flash);
                continue;
            }

            // "Set-at" attributes apply to this cell
            switch (code)
            {
                case 0x09: flash = false; break;            // steady
                case 0x0C: doubleHeight = false; break;     // normal size
                case 0x1C: bg = 0; break;                   // black background
                case 0x1D: bg = fg; break;                  // new background
            }

            // A control code shows as a space
            cells[c] = new TeletextCell(' ', fg, bg, boxed, doubleHeight ? CellHeight.DoubleTop : CellHeight.Normal, flash);

            // "Set-after" attributes apply from the next cell
            switch (code)
            {
                case <= 0x07: fg = (byte)code; graphics = false; break; // alphanumeric colour
                case 0x08: flash = true; break;
                case 0x0A: boxed = false; break;                        // end box
                case 0x0B: boxed = true; break;                         // start box
                case 0x0D or 0x0F: doubleHeight = true; break;          // double height (or size)
                case >= 0x10 and <= 0x17: fg = (byte)(code - 0x10); graphics = true; break;
            }
        }

        // Spaces in double-height mode don't need a lower half of their own
        for (int c = 0; c < Columns; c++)
        {
            if (cells[c].Height == CellHeight.DoubleTop && cells[c].Char == ' ' && !cells[c].Boxed)
                cells[c] = cells[c] with { Height = CellHeight.Normal };
        }

        return cells;
    }

    private void ApplyBuild(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 2 || !Teletext.TryDecodeHamming84(packet[1], out int subtitleData))
            return;

        if ((subtitleData & 0x08) != 0)
            Array.Clear(_buffer);

        int rowCount = subtitleData & 0x07;
        const int rowSize = 2 + Teletext.RowLength;
        for (int i = 0; i < rowCount; i++)
        {
            int start = 2 + i * rowSize;
            if (packet.Length < start + rowSize)
                break;
            if (!Teletext.TryDecodeHamming84(packet[start], out int high) ||
                !Teletext.TryDecodeHamming84(packet[start + 1], out int low))
                continue;

            int row = (high << 4) | low;
            if (row is >= 1 and < Rows)
                _buffer[row] = packet.Slice(start + 2, Teletext.RowLength).ToArray();
        }
    }

    private static TeletextCell[] BlankRow() => Enumerable.Repeat(TeletextCell.Blank, Columns).ToArray();
}
