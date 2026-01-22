/*
 MIT License

Copyright (c) 2026 Christopher Glover

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

 */

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

namespace NewforCli
{
    public static class Globals
    {
        public const string Version = "2.5";
    }

    public static class Wst
    {
        // Newfor protocol packet type codes (per official specification)
        public const byte PKT_CONNECT = 0x0E; // CONNECT - establishes connection and sets page
        public const byte PKT_BUILD = 0x0F; // BUILD - sends subtitle data
        public const byte PKT_REVEAL = 0x10; // REVEAL - displays the subtitle
        public const byte PKT_CLEAR = 0x18; // CLEAR - clears the subtitle display

        // WST control codes
        public const byte StartBox = 0x0B;
        public const byte EndBox = 0x0A;
        public const byte DoubleHeight = 0x0D;
        public const byte Space = 0x20;
        public const byte Padding = 0xA0; // Space (0x20) with odd parity bit set

        // Map keys to WST Colors (Alphanumeric mode)
        public static readonly Dictionary<ConsoleKey, (string Name, byte Code)> ColorMap =
            new()
            {
                { ConsoleKey.R, ("Red", 0x01) },
                { ConsoleKey.G, ("Green", 0x02) },
                { ConsoleKey.Y, ("Yellow", 0x03) },
                { ConsoleKey.B, ("Blue", 0x04) },
                { ConsoleKey.M, ("Magenta", 0x05) },
                { ConsoleKey.A, ("Cyan", 0x06) },
                { ConsoleKey.W, ("White", 0x07) }
            };
    }

    public enum VerticalPosition
    {
        Top, // Rows 2-4
        Middle, // Rows 11-13
        Lower // Rows 21-23 (default)
    }

    class NewforTest
    {
        // App State
        static byte _currentColor = 0x07; // White
        static string _colorName = "White";
        static bool _useBox = true;
        static bool _useDouble = false;
        static VerticalPosition _position = VerticalPosition.Lower;

        static void Main(string[] args)
        {
            string? ip = null;
            int? port = null;
            string? page = null;

            // Argument Overrides
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--ip" && i + 1 < args.Length)
                    ip = args[++i];
                if (args[i] == "--port" && i + 1 < args.Length)
                    port = int.Parse(args[++i]);
                if (args[i] == "--page" && i + 1 < args.Length)
                    page = args[++i];
            }

            // Prompt for missing parameters
            if (ip == null)
            {
                Console.Write("Enter IP address (default 127.0.0.1): ");
                string? input = Console.ReadLine();
                ip = string.IsNullOrWhiteSpace(input) ? "127.0.0.1" : input;
            }

            if (port == null)
            {
                Console.Write("Enter port (default 1234): ");
                string? input = Console.ReadLine();
                port = string.IsNullOrWhiteSpace(input) ? 1234 : int.Parse(input);
            }

            if (page == null)
            {
                Console.Write("Enter page (default 888): ");
                string? input = Console.ReadLine();
                page = string.IsNullOrWhiteSpace(input) ? "888" : input;
            }

            // At this point, all values are guaranteed to be non-null
            using var client = new NewforClient(ip, port.Value);
            if (!client.Connect())
                return;

            PrintDashboard(ip, port.Value, page);

            while (true)
            {
                var keyInfo = Console.ReadKey(true);
                var key = keyInfo.Key;

                if (key == ConsoleKey.Q)
                {
                    // Send DISCONNECT before exiting
                    client.Disconnect();
                    Console.WriteLine($"\n{DateTime.Now:HH:mm:ss} | [DISCONNECT SENT]");
                    break;
                }

                // 1. Toggle Colors
                if (Wst.ColorMap.ContainsKey(key))
                {
                    _currentColor = Wst.ColorMap[key].Code;
                    _colorName = Wst.ColorMap[key].Name;
                    UpdateStatusLine();
                }
                // 2. Toggle Box
                else if (key == ConsoleKey.X)
                {
                    _useBox = !_useBox;
                    UpdateStatusLine();
                }
                // 3. Toggle Height
                else if (key == ConsoleKey.H)
                {
                    _useDouble = !_useDouble;
                    UpdateStatusLine();
                }
                // 4. Position Control
                else if (key == ConsoleKey.T)
                {
                    _position = VerticalPosition.Top;
                    UpdateStatusLine();
                }
                else if (key == ConsoleKey.N)
                {
                    _position = VerticalPosition.Middle;
                    UpdateStatusLine();
                }
                else if (key == ConsoleKey.L)
                {
                    _position = VerticalPosition.Lower;
                    UpdateStatusLine();
                }
                // 5. Clear
                else if (key == ConsoleKey.C)
                {
                    client.Clear(page);
                    Console.WriteLine($"\n{DateTime.Now:HH:mm:ss} | [CLEAR SENT]");
                }
                // 6. Change Page
                else if (key == ConsoleKey.P)
                {
                    Console.Write(
                        $"\n{DateTime.Now:HH:mm:ss} | Enter new page number (current: {page}): "
                    );
                    string? input = Console.ReadLine();
                    if (!string.IsNullOrWhiteSpace(input))
                    {
                        page = input;
                        Console.WriteLine($"{DateTime.Now:HH:mm:ss} | Page changed to {page}");
                        PrintDashboard(ip, port.Value, page);
                    }
                    else
                    {
                        Console.WriteLine($"{DateTime.Now:HH:mm:ss} | Page unchanged");
                        UpdateStatusLine();
                    }
                }
                // 7. Send Subtitles
                else if (key == ConsoleKey.D1)
                {
                    client.Send(
                        page,
                        new[] { "THIS IS A SINGLE LINE" },
                        _currentColor,
                        _useDouble,
                        _useBox,
                        _position
                    );
                }
                else if (key == ConsoleKey.D2)
                {
                    client.Send(
                        page,
                        new[] { "THIS IS LINE ONE", "THIS IS LINE TWO" },
                        _currentColor,
                        _useDouble,
                        _useBox,
                        _position
                    );
                }
                else if (key == ConsoleKey.D3)
                {
                    client.Send(
                        page,
                        new[]
                        {
                            "TOP SUBTITLE LINE",
                            "MIDDLE SUBTITLE LINE",
                            "BOTTOM SUBTITLE LINE"
                        },
                        _currentColor,
                        _useDouble,
                        _useBox,
                        _position
                    );
                }
            }
        }

        static void PrintDashboard(string ip, int port, string page)
        {
            Console.Clear();
            Console.WriteLine("(c) 2026 Christopher Glover");
            Console.WriteLine("===============================================================");
            Console.WriteLine(
                $" NEWFOR INJECTOR  v{Globals.Version} | Target: {ip}:{port} | Page: {page}"
            );
            Console.WriteLine("===============================================================");
            Console.WriteLine();
            Console.WriteLine("COLOR SELECTION:");
            Console.WriteLine("  [R] Red     [G] Green   [Y] Yellow  [B] Blue");
            Console.WriteLine("  [M] Magenta [A] Cyan    [W] White");
            Console.WriteLine();
            Console.WriteLine("OPTIONS:");
            Console.WriteLine("  [X] Toggle Box    [H] Toggle Double Height");
            Console.WriteLine();
            Console.WriteLine("POSITION:");
            Console.WriteLine("  [T] Top     [N] Middle  [L] Lower (Default)");
            Console.WriteLine();
            Console.WriteLine("SUBTITLES:");
            Console.WriteLine("  [1] Single Line  [2] Two Lines  [3] Three Lines");
            Console.WriteLine();
            Console.WriteLine("ACTIONS:");
            Console.WriteLine("  [C] Clear Screen  [P] Change Page  [Q] Quit");
            Console.WriteLine();
            UpdateStatusLine();
        }

        static void UpdateStatusLine()
        {
            Console.SetCursorPosition(0, Console.WindowHeight - 1);
            string boxStatus = _useBox ? "ON" : "OFF";
            string heightStatus = _useDouble ? "DOUBLE" : "SINGLE";
            string positionName = _position switch
            {
                VerticalPosition.Top => "TOP",
                VerticalPosition.Middle => "MIDDLE",
                VerticalPosition.Lower => "LOWER",
                _ => "LOWER"
            };

            Console.Write(
                $" Color: {_colorName,-7} | Box: {boxStatus,-3} | Height: {heightStatus,-6} | Position: {positionName,-6} "
            );
        }
    }

    /// <summary>
    /// Newfor Client for subtitle injection.
    /// Implements the official Newfor Protocol specification.
    ///
    /// Protocol Overview (5 packet types):
    ///
    /// 1. CONNECT (0x0E) - Establishes connection and sets page number
    ///    Structure: 0x0E 0x00 [magazine_H] [tens_H] [units_H]
    ///    All page components are Hamming 8/4 encoded
    ///
    /// 2. BUILD (0x0F) - Sends subtitle row data (ALL rows in ONE packet)
    ///    Structure: 0x0F [subtitle_data] {[row_tens_H] [row_units_H] [40 bytes]} * N
    ///    Subtitle data byte:
    ///      Bits 7-4: Unused (0000)
    ///      Bit 3: CLEAR bit (1=clear before display, 0=add to existing)
    ///      Bits 2-0: Row count (number of rows in packet, range 0-7)
    ///    Row number: 2 bytes, Hamming 8/4 encoded tens and units digits (row 01-23)
    ///    Data: 40 bytes with odd parity
    ///    IMPORTANT: All rows are sent in a SINGLE BUILD packet, not one packet per row!
    ///
    /// 3. REVEAL (0x10) - Displays the subtitle data
    ///    Structure: 0x10 (single byte)
    ///
    /// 4. CLEAR (0x18) - Clears the subtitle display
    ///    Structure: 0x18 (single byte)
    ///
    /// 5. DISCONNECT (0x0E) - Terminates session
    ///    Structure: 0x0E 0x00 0xC7 0xC7 0xC7 (page 999, all nines Hamming encoded)
    ///    Same as CONNECT but Magazine and Page set to illegal value of 999
    ///
    /// Multi-line Subtitle Workflow:
    ///   CONNECT → BUILD(row1, clear=1) → BUILD(row2, clear=0) → BUILD(row3, clear=0) → REVEAL
    ///
    /// Session Lifecycle:
    ///   CONNECT(page) → [BUILD + REVEAL cycles] → DISCONNECT
    /// </summary>
    class NewforClient : IDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private TcpClient? _tcp;

        public NewforClient(string host, int port)
        {
            _host = host;
            _port = port;
        }

        public bool Connect()
        {
            try
            {
                _tcp = new TcpClient();
                _tcp.Connect(_host, _port);
                Console.WriteLine($"Connected to {_host}:{_port}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Connection failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Sends the CONNECT command which establishes connection and sets the page number.
        /// Per official Newfor spec (page 48):
        /// Byte 1: 0x0E (PACKET TYPE CODE)
        /// Byte 2: 0x00 (ZERO)
        /// Byte 3: Magazine number (Hamming 8/4 encoded)
        /// Byte 4: Page tens (Hamming 8/4 encoded)
        /// Byte 5: Page units (Hamming 8/4 encoded)
        /// </summary>
        private void SendBurstStart(string page)
        {
            try
            {
                if (_tcp == null)
                    throw new InvalidOperationException("TCP client is not connected.");

                var stream = _tcp.GetStream();

                // Parse page number
                int pageNum = int.Parse(page);
                int magazine = (pageNum / 100) % 10;
                int tens = (pageNum / 10) % 10;
                int units = pageNum % 10;

                // Build CONNECT packet per spec
                var pkt = new List<byte>
                {
                    0x0E, // Byte 1: Packet type code
                    0x00, // Byte 2: ZERO
                    EncodeDigit(magazine), // Byte 3: Magazine with Hamming 8/4
                    EncodeDigit(tens), // Byte 4: Tens with Hamming 8/4
                    EncodeDigit(units) // Byte 5: Units with Hamming 8/4
                };

                stream.Write(pkt.ToArray(), 0, pkt.Count);
                stream.Flush();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError sending CONNECT: {ex.Message}");
            }
        }

        /// <summary>
        /// Encodes a single hex digit (0-F) using Hamming 8/4 encoding.
        /// This encoding is used for magazine, tens, and units positions in the Newfor protocol.
        ///
        /// Complete Hamming 8/4 encoding table from Teletext specification:
        /// https://pdc.ro.nu/hamming.html
        ///
        /// Data nibble -> Hammed byte
        /// 0 = 0x15, 1 = 0x02, 2 = 0x49, 3 = 0x5E
        /// 4 = 0x64, 5 = 0x73, 6 = 0x38, 7 = 0x2F
        /// 8 = 0xD0, 9 = 0xC7, A = 0x8C, B = 0x9B
        /// C = 0xA1, D = 0xB6, E = 0xFD, F = 0xEA
        /// </summary>
        private byte EncodeDigit(int digit)
        {
            return digit switch
            {
                0x0 => 0x15, // 0
                0x1 => 0x02, // 1
                0x2 => 0x49, // 2
                0x3 => 0x5E, // 3
                0x4 => 0x64, // 4
                0x5 => 0x73, // 5
                0x6 => 0x38, // 6
                0x7 => 0x2F, // 7
                0x8 => 0xD0, // 8
                0x9 => 0xC7, // 9
                0xA => 0x8C, // A
                0xB => 0x9B, // B
                0xC => 0xA1, // C
                0xD => 0xB6, // D
                0xE => 0xFD, // E
                0xF => 0xEA, // F
                _ => 0x15 // Default to 0
            };
        }

        /// <summary>
        /// Sends subtitle lines to the display.
        /// FIXED: All lines are sent in a single burst without clearing between them.
        /// The burst start clears the page once, then all lines are added before the end marker.
        /// Each row MUST have exactly 40 bytes of data.
        /// CRITICAL: Clear bit is set ONLY on the first row to clear the screen.
        ///           Subsequent rows have clear bit OFF so they add to the display without clearing.
        /// </summary>
        public void Send(
            string page,
            string[] lines,
            byte color,
            bool dh,
            bool boxed,
            VerticalPosition position
        )
        {
            // Send CONNECT packet to establish page
            SendBurstStart(page);

            // Send CLEAR command before BUILD (matching FAB sequence)
            Clear(page);

            int spacing = dh ? 2 : 1;
            int startRow = CalculateStartRow(lines.Length, position, spacing);

            // Build all row data first
            var allRowData = new List<(byte row, byte[] data)>();

            for (int i = 0; i < lines.Length; i++)
            {
                var data = new List<byte>();

                // Double height control if enabled (MUST come first, before everything)
                if (dh)
                {
                    data.Add(AddOddParity(Wst.DoubleHeight));
                    data.Add(AddOddParity(Wst.Space));
                }

                // Add leading spaces (professional format has spaces before box)
                data.Add(AddOddParity(Wst.Space));
                data.Add(AddOddParity(Wst.Space));
                data.Add(AddOddParity(Wst.Space));
                data.Add(AddOddParity(Wst.Space));

                // Start box if enabled
                if (boxed)
                {
                    data.Add(AddOddParity(Wst.StartBox));
                    data.Add(AddOddParity(Wst.StartBox));
                }

                // Color code (without extra space after)
                data.Add(AddOddParity(color));
                data.Add(AddOddParity(Wst.Space));

                // Add text with odd parity
                foreach (char c in lines[i])
                    data.Add(AddOddParity((byte)c));

                // End box if enabled (must come after text, before padding)
                if (boxed)
                {
                    data.Add(AddOddParity(Wst.EndBox));
                    data.Add(AddOddParity(Wst.EndBox));
                }

                // Padding to exactly 40 bytes - CRITICAL for protocol compliance
                while (data.Count < 40)
                    data.Add(Wst.Padding);

                // Truncate if somehow over 40 bytes (shouldn't happen but safety check)
                if (data.Count > 40)
                {
                    Console.WriteLine(
                        $"\nWarning: Row data exceeded 40 bytes ({data.Count}), truncating"
                    );
                    data.RemoveRange(40, data.Count - 40);
                }

                byte rowNum = (byte)(startRow + (i * spacing));
                allRowData.Add((rowNum, data.ToArray()));
            }

            // Send single BUILD packet containing ALL rows
            WriteBuildPacket(allRowData, clearBit: true);

            // Send REVEAL to display the subtitle
            SendEndMarker();
        }

        /// <summary>
        /// Calculates the starting row based on vertical position and number of lines.
        /// Top: Starts at row 2 (row 1 is reserved for page header)
        /// Middle: Centers around row 12
        /// Lower: Ends at row 23 (standard subtitle position)
        /// </summary>
        private int CalculateStartRow(int lineCount, VerticalPosition position, int spacing)
        {
            int totalHeight = lineCount + ((lineCount - 1) * (spacing - 1));

            return position switch
            {
                VerticalPosition.Top => 2, // Start at row 2 (row 1 is page header)
                VerticalPosition.Middle => (12 - totalHeight / 2), // Center around row 12
                VerticalPosition.Lower => (23 - ((lineCount - 1) * spacing)), // Default behavior
                _ => (23 - ((lineCount - 1) * spacing))
            };
        }

        /// <summary>
        /// Clears the subtitle display.
        /// Per official spec (page 49): CLEAR command is a single byte 0x18
        /// FAB sends 0x98 which is 0x18 with odd parity
        /// </summary>
        public void Clear(string page)
        {
            try
            {
                if (_tcp == null)
                    throw new InvalidOperationException("TCP client is not connected.");

                var stream = _tcp.GetStream();

                // Send CLEAR command (0x18 with odd parity = 0x98)
                stream.WriteByte(0x98);
                stream.Flush();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError clearing display: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends DISCONNECT command to terminate the subtitle session.
        /// Per official spec (page 49): Same as CONNECT but with page 999 (illegal value)
        /// Structure: 0x0E 0x00 [mag=9] [tens=9] [units=9]
        /// </summary>
        public void Disconnect()
        {
            try
            {
                if (_tcp == null)
                    return; // Already disconnected

                var stream = _tcp.GetStream();

                // Build DISCONNECT packet (CONNECT with page 999)
                var pkt = new List<byte>
                {
                    0x0E, // Byte 1: Packet type code
                    0x00, // Byte 2: ZERO
                    EncodeDigit(9), // Byte 3: Magazine = 9 (illegal)
                    EncodeDigit(9), // Byte 4: Tens = 9
                    EncodeDigit(9) // Byte 5: Units = 9
                };

                stream.Write(pkt.ToArray(), 0, pkt.Count);
                stream.Flush();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError sending DISCONNECT: {ex.Message}");
            }
        }

        /// <summary>
        /// Writes a BUILD packet containing all subtitle rows per official Newfor spec.
        ///
        /// BUILD Packet Structure (matching FAB subtitler):
        /// Byte 1: 0x8F (BUILD command 0x0F with odd parity)
        /// Byte 2: SUBTITLE DATA byte (Hamming 8/4 encoded)
        ///   - Bit 3: CLEAR bit (1 = clear screen, 0 = don't clear)
        ///   - Bits 2-0: Row count (number of rows in this packet, range 0-7)
        /// Then for EACH row:
        ///   - 2 bytes: Row number (Hamming 8/4 encoded, tens first then units)
        ///   - 40 bytes: Subtitle data (odd parity)
        /// </summary>
        private void WriteBuildPacket(List<(byte row, byte[] data)> rows, bool clearBit)
        {
            try
            {
                if (_tcp == null)
                    throw new InvalidOperationException("TCP client is not connected.");

                var stream = _tcp.GetStream();
                var pkt = new List<byte>();

                // Byte 1: Packet type code for BUILD command with odd parity
                // FAB sends 0x8F which is 0x0F with parity bit set
                pkt.Add(0x8F);

                // Byte 2: SUBTITLE DATA byte - Hamming encoded
                // Bit 3: CLEAR bit (1 = clear, 0 = no clear)
                // Bits 2-0: Row count (number of rows, range 0-7)
                byte subtitleDataValue = (byte)(rows.Count & 0x07);
                if (clearBit)
                    subtitleDataValue |= 0x08; // Set bit 3 for CLEAR
                // Hamming encode the subtitle data byte (FAB sends 0xC7 for value 9 = clear + 1 row)
                pkt.Add(EncodeDigit(subtitleDataValue));

                // Add each row's data: 2-byte row number + 40 bytes of data
                foreach (var (row, data) in rows)
                {
                    // Row number encoded as TWO Hamming 8/4 bytes representing hex nibbles
                    // Row as hex: high nibble first, low nibble second
                    // e.g., row 22 = 0x16: high nibble 1, low nibble 6 → Hamming(1), Hamming(6)
                    // e.g., row 23 = 0x17: high nibble 1, low nibble 7 → Hamming(1), Hamming(7)
                    pkt.Add(EncodeDigit(row >> 4));   // High nibble (row / 16)
                    pkt.Add(EncodeDigit(row & 0x0F)); // Low nibble (row % 16)

                    // 40 bytes of subtitle data
                    if (data.Length != 40)
                        throw new InvalidOperationException(
                            $"Data must be exactly 40 bytes, got {data.Length}"
                        );
                    pkt.AddRange(data);
                }

                // Write packet
                stream.Write(pkt.ToArray(), 0, pkt.Count);
                stream.Flush();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError writing BUILD packet: {ex.Message}");
            }
        }

        // Keep old WritePacket for backwards compatibility but mark obsolete
        [Obsolete("Use WriteBuildPacket instead")]
        private void WritePacket(string page, byte row, byte[] data, bool clearBit, int totalRows)
        {
            try
            {
                if (_tcp == null)
                    throw new InvalidOperationException("TCP client is not connected.");

                var stream = _tcp.GetStream();
                var pkt = new List<byte>();

                // Byte 1: Packet type code for BUILD command with parity
                pkt.Add(0x8F);

                // Byte 2: SUBTITLE DATA byte - Hamming encoded
                // Bit 3: CLEAR bit (1 = clear, 0 = no clear)
                // Bits 2-0: Row count (3 bits, range 0-7)
                byte subtitleDataValue = (byte)(totalRows & 0x07);
                if (clearBit)
                    subtitleDataValue |= 0x08; // Set bit 3 for CLEAR
                pkt.Add(EncodeDigit(subtitleDataValue));

                // Bytes 3-4: Row number encoded as TWO Hamming 8/4 bytes (hex nibbles)
                // High nibble first, low nibble second
                pkt.Add(EncodeDigit(row >> 4));   // High nibble
                pkt.Add(EncodeDigit(row & 0x0F)); // Low nibble

                // Bytes 5-44: Data payload - MUST be exactly 40 bytes
                if (data.Length != 40)
                    throw new InvalidOperationException(
                        $"Data must be exactly 40 bytes, got {data.Length}"
                    );

                pkt.AddRange(data);

                // Write packet
                stream.Write(pkt.ToArray(), 0, pkt.Count);
                stream.Flush();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError writing packet: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends the end-of-burst marker (0x10).
        /// This signals the end of a transmission burst and triggers display of all sent lines.
        /// CRITICAL: This must be sent AFTER all subtitle lines, not between them.
        /// </summary>
        private void SendEndMarker()
        {
            try
            {
                if (_tcp == null)
                    throw new InvalidOperationException("TCP client is not connected.");

                var stream = _tcp.GetStream();

                // Send end-of-transmission marker
                stream.WriteByte(0x10);
                stream.Flush();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError sending end marker: {ex.Message}");
            }
        }

        /// <summary>
        /// Adds odd parity to a byte (sets bit 7 if needed to make total bit count odd).
        /// This is required for WST/Teletext transmission.
        /// </summary>
        private byte AddOddParity(byte value)
        {
            value &= 0x7F; // Clear bit 7 first
            byte count = 0;

            // Count set bits in lower 7 bits
            for (int i = 0; i < 7; i++)
                if ((value & (1 << i)) != 0)
                    count++;

            // Set bit 7 if we have even parity (to make it odd)
            if (count % 2 == 0)
                value |= 0x80;

            return value;
        }

        public void Dispose() => _tcp?.Dispose();
    }
}