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
        public const string Version = "1.4";
    }

    public static class Wst
    {

        // Softel newfor packet markers (REVISED)
        public const byte PKT_CLEAR = 0x0E;   // Clear/Control packet type
        public const byte PKT_DATA = 0x8F;    // Data packet type
        public const byte PKT_START = 0x15;   // Packet start marker

        // WST control codes
        public const byte StartBox = 0x0B;
        public const byte EndBox = 0x0A;
        public const byte DoubleHeight = 0x0D;
        public const byte Space = 0x20;
        public const byte Padding = 0x8A;      // Padding byte for data packets

        // Map keys to WST Colors (Alphanumeric mode)
        public static readonly Dictionary<ConsoleKey, (string Name, byte Code)> ColorMap = new() {
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
        Top,      // Rows 2-4
        Middle,   // Rows 11-13
        Lower     // Rows 21-23 (default)
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
                if (args[i] == "--ip" && i + 1 < args.Length) ip = args[++i];
                if (args[i] == "--port" && i + 1 < args.Length) port = int.Parse(args[++i]);
                if (args[i] == "--page" && i + 1 < args.Length) page = args[++i];
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
            if (!client.Connect()) return;

            PrintDashboard(ip, port.Value, page);

            while (true)
            {
                var keyInfo = Console.ReadKey(true);
                var key = keyInfo.Key;

                if (key == ConsoleKey.Q) break;

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
                    Console.Write($"\n{DateTime.Now:HH:mm:ss} | Enter new page number (current: {page}): ");
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
                    client.Send(page, new[] { "THIS IS A SINGLE LINE" }, _currentColor, _useDouble, _useBox, _position);
                }
                else if (key == ConsoleKey.D2)
                {
                    client.Send(page, new[] { "THIS IS LINE ONE", "THIS IS LINE TWO" }, _currentColor, _useDouble, _useBox, _position);
                }
                else if (key == ConsoleKey.D3)
                {
                    client.Send(page, new[] { "TOP SUBTITLE LINE", "MIDDLE SUBTITLE LINE", "BOTTOM SUBTITLE LINE" }, _currentColor, _useDouble, _useBox, _position);
                }
            }
        }

        static void PrintDashboard(string ip, int port, string page)
        {
            Console.Clear();
            Console.WriteLine("(c) 2026 Christopher Glover");
            Console.WriteLine("===============================================================");
            Console.WriteLine($" NEWFOR INJECTOR  v{Globals.Version} | Target: {ip}:{port} | Page: {page}");
            Console.WriteLine("===============================================================");
            Console.WriteLine(" [COLORS] W:White Y:Yellow G:Green R:Red B:Blue A:Cyan M:Magenta");
            Console.WriteLine(" [ATTRS]  X:Toggle Box  H:Toggle Double-Height");
            Console.WriteLine(" [POSITION] T:Top  N:Middle  L:Lower");
            Console.WriteLine(" [SEND]   1:Single Line 2:Double Line  3:Triple Line");
            Console.WriteLine(" [ACTION] C:Clear Page  P:Change Page  Q:Quit");
            Console.WriteLine("---------------------------------------------------------");
            UpdateStatusLine();
        }

        static void UpdateStatusLine()
        {
            // Clear current line and write state
            string boxStatus = _useBox ? "[BOX ON]" : "[BOX OFF]";
            string heightStatus = _useDouble ? "[DBL HIGH]" : "[NORMAL]";
            string posStatus = _position switch
            {
                VerticalPosition.Top => "[TOP]",
                VerticalPosition.Middle => "[MIDDLE]",
                VerticalPosition.Lower => "[LOWER]",
                _ => "[LOWER]"
            };
            Console.Write($"\r CURRENT MODE: {_colorName.PadRight(8)} {boxStatus.PadRight(10)} {heightStatus.PadRight(12)} {posStatus.PadRight(10)} ");
        }
    }

    public class NewforClient : IDisposable
    {
        private TcpClient? _tcp;
        private string _ip;
        private int _port;

        public NewforClient(string ip, int port) { _ip = ip; _port = port; }

        public bool Connect()
        {
            try { _tcp = new TcpClient(_ip, _port); return true; }
            catch { Console.WriteLine($"\nError: Could not connect to: {_ip}"); return false; }
        }

        /// <summary>
        /// Sends the burst initialization sequence required before sending data packets.
        /// Sequence: 0e 15 [MAG] [TENS] [UNITS], 0e 15 15 15 15, 98
        /// The last three bytes encode the page number in Softel's proprietary format.
        /// </summary>
        private void SendBurstStart(string page)
        {
            if (_tcp == null)
                throw new InvalidOperationException("TCP client is not connected.");

            var stream = _tcp.GetStream();

            // Encode the page number into Softel format (3 bytes)
            var pageBytes = EncodePageNumber(page);

            // Start of burst sequence with page number encoding
            byte[] startBurst = { 0x0E, 0x15, pageBytes[0], pageBytes[1], pageBytes[2] };
            stream.Write(startBurst, 0, startBurst.Length);

            // Init confirmation
            byte[] initConfirm = { 0x0E, 0x15, 0x15, 0x15, 0x15 };
            stream.Write(initConfirm, 0, initConfirm.Length);

            // Burst marker
            stream.WriteByte(0x98);

            stream.Flush();
        }

        /// <summary>
        /// Encodes the page number for the burst start sequence using Softel's proprietary encoding.
        /// Returns 3 bytes: [Magazine byte, Tens byte, Units byte]
        /// 
        /// Based on packet capture analysis from pages 123, 333, 456, 888, 889:
        /// The same encoding is used for all three positions (magazine, tens, units).
        /// Each digit 0-9 maps to a specific byte value.
        /// </summary>
        private byte[] EncodePageNumber(string page)
        {
            // Parse the page number
            if (!int.TryParse(page, out int pageNum))
                return new byte[] { 0xD0, 0xD0, 0xD0 }; // Default to page 888

            // Extract magazine (hundreds) and page digits
            int magazine = pageNum / 100;
            if (magazine == 8) magazine = 0;  // Magazine 8 is encoded as 0 in teletext

            int tens = (pageNum / 10) % 10;
            int units = pageNum % 10;

            // Use single encoding table for all positions (confirmed via packet captures)
            byte magazineByte = EncodeDigit(magazine);
            byte tensByte = EncodeDigit(tens);
            byte unitsByte = EncodeDigit(units);

            return new byte[] { magazineByte, tensByte, unitsByte };
        }

        /// <summary>
        /// Encodes a single digit (0-9) to Softel format.
        /// This encoding is used consistently for magazine, tens, and units positions.
        /// 
        /// All mappings confirmed from packet captures:
        /// Pages 123, 333, 456, 567, 888, 889
        /// </summary>
        private byte EncodeDigit(int digit)
        {
            return digit switch
            {
                0 => 0xD0,  // Confirmed: pages 888, 889
                1 => 0x02,  // Confirmed: page 123
                2 => 0x49,  // Confirmed: page 123
                3 => 0x5E,  // Confirmed: pages 123, 333
                4 => 0x64,  // Confirmed: page 456
                5 => 0x73,  // Confirmed: pages 456, 567
                6 => 0x38,  // Confirmed: pages 456, 567
                7 => 0x2F,  // Confirmed: page 567
                8 => 0xD0,  // Confirmed: pages 888, 889
                9 => 0xC7,  // Confirmed: page 889
                _ => 0xD0   // Default to 0
            };
        }

        /// <summary>
        /// Sends subtitle lines to the display.
        /// CORRECTED: Sends burst start (clear) once at the beginning, then all subtitle lines as a single burst.
        /// This follows the Softel Newfor Protocol and WST teletext standard.
        /// </summary>
        public void Send(string page, string[] lines, byte color, bool dh, bool boxed, VerticalPosition position)
        {
            // Send burst start ONCE at the beginning to clear the page
            SendBurstStart(page);

            int spacing = dh ? 2 : 1;
            int startRow = CalculateStartRow(lines.Length, position, spacing);

            // Send all subtitle lines in the same burst (no clear between them)
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

                // Padding with 0x8A to match professional format
                while (data.Count < 40)
                    data.Add(Wst.Padding);

                WritePacket(page, (byte)(startRow + (i * spacing)), data.ToArray());
            }

            // Send end marker to complete the burst
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
                VerticalPosition.Top => 2,  // Start at row 2 (row 1 is page header)
                VerticalPosition.Middle => (12 - totalHeight / 2),  // Center around row 12
                VerticalPosition.Lower => (23 - ((lineCount - 1) * spacing)),  // Default behavior
                _ => (23 - ((lineCount - 1) * spacing))
            };
        }

        /// <summary>
        /// Clears the display by sending burst start followed immediately by end marker.
        /// CORRECTED: Now uses proper clear sequence instead of treating init as clear.
        /// </summary>
        public void Clear(string page)
        {
            try
            {
                if (_tcp == null)
                    throw new InvalidOperationException("TCP client is not connected.");

                // Send burst start (this initializes/clears the display)
                SendBurstStart(page);

                // Immediately send end marker (no data packets = clear screen)
                SendEndMarker();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError clearing page: {ex.Message}");
            }
        }

        /// <summary>
        /// Writes a data packet with the correct Newfor protocol structure.
        /// Structure: 8f c7 02 [row with parity] [40 bytes data]
        /// </summary>
        private void WritePacket(string page, byte row, byte[] data)
        {
            try
            {
                if (_tcp == null)
                    throw new InvalidOperationException("TCP client is not connected.");

                var stream = _tcp.GetStream();
                var pkt = new List<byte>();

                // Data packet header (observed from professional capture)
                pkt.Add(Wst.PKT_DATA);  // 0x8F

                // Second byte - consistently 0xC7 in professional captures
                pkt.Add(0xC7);

                // Third byte - consistently 0x02 in professional captures
                pkt.Add(0x02);

                // Row number with parity
                pkt.Add(AddOddParity(row));

                // Data payload (already has parity and padding)
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
        /// This signals the end of a transmission burst.
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
                if ((value & (1 << i)) != 0) count++;

            // Set bit 7 if we have even parity (to make it odd)
            if (count % 2 == 0)
                value |= 0x80;

            return value;
        }

        public void Dispose() => _tcp?.Dispose();
    }
}