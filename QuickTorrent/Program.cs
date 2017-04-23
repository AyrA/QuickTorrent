using MonoTorrent;
using MonoTorrent.Common;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace QuickTorrent
{
    class Program
    {
        private static List<TorrentHandler> Handler;

        static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                if (!args.Contains("/?"))
                {
                    foreach (var arg in args)
                    {
                        if (arg.ToLower().StartsWith("magnet:"))
                        {
                            Handler.Add(new TorrentHandler(ParseLink(arg)));
                        }
                        else if (File.Exists(arg))
                        {
                            Handler.Add(new TorrentHandler(ParseTorrent(arg)));
                        }
                        else if (ValidHex(arg))
                        {
                            Handler.Add(new TorrentHandler(ParseHash(arg)));
                        }
                        else
                        {
                            Console.Error.WriteLine("Invalid Argument. Only file names, magnet links and hashes are supported.");
                        }
                    }
                }
                else
                {
                    Console.Error.WriteLine(@"QuickTorrent.exe [TorrentFile [...]] [MagnetLink [...]] [InfoHash [...]]

TorrentFile  - Torrent file to download
MagnetLink   - Magnet link to download that can be found in the DHT network
InfoHash     - SHA1 hash of a torrent that can be found in the DHT network

Without arguments the application tries to interpret its file name as a hash.");
                }
            }
            else
            {
            }
        }

        private static MagnetLink ParseLink(string Arg)
        {
            return new MagnetLink(Arg);
        }

        private static InfoHash ParseHash(string Arg)
        {
            return new InfoHash(s2b(Arg));
        }

        private static Torrent ParseTorrent(string Arg)
        {
            return Torrent.Load(File.ReadAllBytes(Arg));
        }

        private static bool ValidHex(string hexString)
        {
            try
            {
                s2b(hexString);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static byte[] s2b(string hexString)
        {
            if (hexString.Length % 2 != 0)
            {
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "The binary key cannot have an odd number of digits: {0}", hexString));
            }

            byte[] HexAsBytes = new byte[hexString.Length / 2];
            for (int index = 0; index < HexAsBytes.Length; index++)
            {
                string byteValue = hexString.Substring(index * 2, 2);
                HexAsBytes[index] = byte.Parse(byteValue, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            return HexAsBytes;
        }

        private static void TH_PiecemapUpdate(object Sender, PiecemapEventArgs Args)
        {
            const char EMPTY = '░';
            const char FULL = '█';
            //const string TOP = "╔═╗";
            //const char LR = '║';
            //const string BOTTOM = "╚═╝";
            var Handler = (TorrentHandler)Sender;

            if (Args.Piecemap != null)
            {
                double count = Console.BufferWidth * (Console.WindowHeight - 1);
                var SB = new StringBuilder((int)count);
                for (double d = 0; d < count; d++)
                {
                    SB.Append(Args.Piecemap[(int)(Args.Piecemap.Length * d / count)] ? FULL : EMPTY);
                }
                lock ("PIECE_PRINT")
                {
                    var ProgressText = $"QuickTorrent {Handler.TorrentName}; {Math.Round(Handler.Progress, 1)}%; State: {Handler.State}; S/L: {Handler.Seeds}/{Handler.Leechs}";
                    if (Console.Title != ProgressText)
                    {
                        Console.Title = ProgressText;
                    }
                    var CurrentMap = SB.ToString();
                    Console.SetCursorPosition(0, 0);
                    Console.Write(SB.ToString());
                }
            }
        }
    }
}
