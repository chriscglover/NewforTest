using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

namespace NewforCli
{
    public static class Wst
    {
        public const byte NAK = 0x15;
        public const byte ETX = 0x14;
        public const byte StartBox = 0x0B;
        public const byte EndBox = 0x0C;
        public const byte DoubleHeight = 0x0D;

        // Map keys to WST Colors
        public static readonly Dictionary<ConsoleKey, (string Name, byte Code)> ColorMap = new() {
            { ConsoleKey.W, ("White", 0x07) },
            { ConsoleKey.Y, ("Yellow", 0x03) },
            { ConsoleKey.G, ("Green", 0x02) },
            { ConsoleKey.R, ("Red", 0x01) },
            { ConsoleKey.B, ("Blue", 0x04) },
            { ConsoleKey.M, ("Magenta", 0x05) },
            { ConsoleKey.A, ("Cyan", 0x06) }
        };
    }

    class Program
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
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss} | [CLEAR SENT]");
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
        private TcpClient _tcp;
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
                if (boxed) data.Add(Wst.StartBox);
                data.Add(color);
                if (dh) data.Add(Wst.DoubleHeight);
                data.AddRange(Encoding.ASCII.GetBytes(lines[i]));
                if (boxed) data.Add(Wst.EndBox);

                WritePacket(page, (byte)(startRow + (i * spacing)), data.ToArray());
            }
        }

        public void Clear(string page)
        {
            for (byte r = 18; r <= 23; r++)
                WritePacket(page, r, Encoding.ASCII.GetBytes("                                        "));
        }

        private void WritePacket(string page, byte row, byte[] data)
        {
            try
            {
                var stream = _tcp.GetStream();
                var pkt = new List<byte> { Wst.NAK };
                pkt.AddRange(Encoding.ASCII.GetBytes(page.PadLeft(3, '0')));
                pkt.AddRange(Encoding.ASCII.GetBytes(row.ToString("D2")));
                pkt.AddRange(data);
                pkt.Add(Wst.ETX);
                stream.Write(pkt.ToArray(), 0, pkt.Count);
            }
            catch { }
        }

        public void Dispose() => _tcp?.Dispose();
    }
}