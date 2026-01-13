using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

namespace NewforCli
{
    public static class Wst
    {
        // Softel newfor packet markers
        public const byte STX = 0x02;  // Start of Text (fixed from 0x15)
        public const byte ETX = 0x03;  // End of Text (fixed from 0x14)

        // WST control codes
        public const byte StartBox = 0x0B;
        public const byte EndBox = 0x0A;        // Fixed from 0x0C
        public const byte DoubleHeight = 0x0D;
        public const byte Space = 0x20;

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

    class NewforTest
    {
        // App State
        static byte _currentColor = 0x07; // White
        static string _colorName = "White";
        static bool _useBox = true;
        static bool _useDouble = false;

        static void Main(string[] args)
        {
            string ip = "127.0.0.1";
            int port = 1234;
            string page = "888";

            // Argument Overrides
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--ip") ip = args[++i];
                if (args[i] == "--port") port = int.Parse(args[++i]);
                if (args[i] == "--page") page = args[++i];
            }

            using var client = new NewforClient(ip, port);
            if (!client.Connect()) return;

            PrintDashboard(ip, port, page);

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
                // 4. Clear
                else if (key == ConsoleKey.C)
                {
                    client.Clear(page);
                    Console.WriteLine($"\n{DateTime.Now:HH:mm:ss} | [CLEAR SENT]");
                }
                // 5. Send Subtitles
                else if (key == ConsoleKey.D1)
                {
                    client.Send(page, new[] { "THIS IS A SINGLE LINE" }, _currentColor, _useDouble, _useBox);
                }
                else if (key == ConsoleKey.D2)
                {
                    client.Send(page, new[] { "THIS IS LINE ONE", "THIS IS LINE TWO" }, _currentColor, _useDouble, _useBox);
                }
                else if (key == ConsoleKey.D3)
                {
                    client.Send(page, new[] { "TOP SUBTITLE LINE", "MIDDLE SUBTITLE LINE", "BOTTOM SUBTITLE LINE" }, _currentColor, _useDouble, _useBox);
                }
            }
        }

        static void PrintDashboard(string ip, int port, string page)
        {
            Console.Clear();
            Console.WriteLine("=========================================================");
            Console.WriteLine($" NEWFOR INJECTOR | Target: {ip}:{port} | Page: {page}");
            Console.WriteLine("=========================================================");
            Console.WriteLine(" [COLORS] W:White Y:Yellow G:Green R:Red B:Blue A:Cyan");
            Console.WriteLine(" [ATTRS]  X:Toggle Box  H:Toggle Double-Height");
            Console.WriteLine(" [SEND]   1:Single Line 2:Double Line  3:Triple Line");
            Console.WriteLine(" [ACTION] C:Clear Page  Q:Quit");
            Console.WriteLine("---------------------------------------------------------");
            UpdateStatusLine();
        }

        static void UpdateStatusLine()
        {
            // Clear current line and write state
            string boxStatus = _useBox ? "[BOX ON]" : "[BOX OFF]";
            string heightStatus = _useDouble ? "[DBL HIGH]" : "[NORMAL]";
            Console.Write($"\r CURRENT MODE: {_colorName.PadRight(8)} {boxStatus.PadRight(10)} {heightStatus.PadRight(12)} ");
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
            catch { Console.WriteLine("\nError: Receiver not found."); return false; }
        }

        public void Send(string page, string[] lines, byte color, bool dh, bool boxed)
        {
            Clear(page);
            int spacing = dh ? 2 : 1;
            int startRow = 23 - ((lines.Length - 1) * spacing);

            for (int i = 0; i < lines.Length; i++)
            {
                var data = new List<byte>();

                // Start box if enabled
                if (boxed) data.Add(AddOddParity(Wst.StartBox));

                // Color code + space (important!)
                data.Add(AddOddParity(color));
                data.Add(AddOddParity(Wst.Space));

                // Double height + space if enabled
                if (dh)
                {
                    data.Add(AddOddParity(Wst.DoubleHeight));
                    data.Add(AddOddParity(Wst.Space));
                }

                // Add text with odd parity
                foreach (char c in lines[i])
                    data.Add(AddOddParity((byte)c));

                // End box if enabled
                if (boxed) data.Add(AddOddParity(Wst.EndBox));

                WritePacket(page, (byte)(startRow + (i * spacing)), data.ToArray());
            }
        }

        public void Clear(string page)
        {
            // Clear subtitle rows with spaces (with parity)
            var spaces = new byte[40];
            for (int j = 0; j < 40; j++)
                spaces[j] = AddOddParity(Wst.Space);

            for (byte r = 18; r <= 23; r++)
                WritePacket(page, r, spaces);
        }

        private void WritePacket(string page, byte row, byte[] data)
        {
            try
            {
                if (_tcp == null)
                    throw new InvalidOperationException("TCP client is not connected.");

                var stream = _tcp.GetStream();
                var pkt = new List<byte> { Wst.STX }; // Start with STX (0x02)

                // Page number (3 ASCII digits)
                byte[] pageBytes = Encoding.ASCII.GetBytes(page.PadLeft(3, '0'));
                pkt.AddRange(pageBytes);

                // Row number with odd parity
                pkt.Add(AddOddParity(row));

                // Data bytes (already have parity from Send method)
                pkt.AddRange(data);

                // Calculate checksum (XOR of all bytes from page through data)
                byte checksum = 0;
                for (int i = 1; i < pkt.Count; i++) // Start from index 1 (skip STX)
                    checksum ^= pkt[i];

                pkt.Add(checksum);
                pkt.Add(Wst.ETX); // End with ETX (0x03)

                stream.Write(pkt.ToArray(), 0, pkt.Count);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError writing packet: {ex.Message}");
            }
        }

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