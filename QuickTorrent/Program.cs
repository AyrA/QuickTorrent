using MonoTorrent;
using MonoTorrent.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace QuickTorrent
{
    class Program
    {
        private static List<TorrentHandler> Handler;

        private struct RET
        {
            /// <summary>
            /// The application executed successfully
            /// </summary>
            public const int SUCCESS = 0;
            /// <summary>
            /// Help was shown
            /// </summary>
            public const int HELP = 255;
            /// <summary>
            /// Invalid argument
            /// </summary>
            public const int ARGUMENT_ERROR = 1;
            /// <summary>
            /// Invalid executable name for a hash
            /// </summary>
            public const int NAME_ERROR = 2;
        }

        public static int Main(string[] args)
        {
#if DEBUG
            args = new string[] { "40F90995A1C16A1BF454D09907F57700F3E8BD64" };
#endif
            Handler = new List<TorrentHandler>();
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
                            return RET.ARGUMENT_ERROR;
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
                    return RET.HELP;
                }
            }
            else
            {
                var Hash = Process.GetCurrentProcess().MainModule.FileName.Split(Path.DirectorySeparatorChar).Last().Split('.')[0];
                if (ValidHex(Hash))
                {
                    Handler.Add(new TorrentHandler(ParseHash(Hash)));
                }
                else
                {
                    Console.Error.WriteLine("No arguments given and file name not valid Infohash");
                    return RET.NAME_ERROR;
                }
            }
            foreach (var H in Handler.Where(m => m != null))
            {
                H.Start();
            }

            bool cont = true;
            Thread T = new Thread(delegate ()
            {
                const int NAMELENGTH = 20;
                while (cont)
                {
                    Console.SetCursorPosition(0, 0);
                    foreach (var H in Handler.Where(m => m != null))
                    {
                        string Name = H.TorrentName == null ? "" : H.TorrentName;
                        //Subtraction is name length, percentage and the two spaces
                        var Map = new string(StretchMap(H.Map, Console.BufferWidth - NAMELENGTH - 6).Select(m => m ? '█' : '░').ToArray());
                        if (Name.Length > NAMELENGTH)
                        {
                            Name = Name.Substring(0, NAMELENGTH);
                        }
                        switch (H.State)
                        {
                            case TorrentState.Metadata:
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                break;
                            case TorrentState.Hashing:
                            case TorrentState.Downloading:
                                Console.ForegroundColor = ConsoleColor.Green;
                                break;
                            case TorrentState.Seeding:
                                Console.ForegroundColor = ConsoleColor.Blue;
                                break;
                            default:
                                Console.ForegroundColor = ConsoleColor.Red;
                                break;
                        }

                        Console.Write("{0,-" + NAMELENGTH + "} {1,3}% {2}", Name, (int)H.Progress, Map);
                    }
                    Console.ResetColor();
                    Thread.Sleep(500);
                }
            })
            { IsBackground = true, Name = "Status" };
            T.Start();

            while (Console.ReadKey(true).Key != ConsoleKey.Escape) ;
            cont = false;
            T.Join();
            return RET.SUCCESS;
        }

        private static bool[] StretchMap(bool[] Map, int Count)
        {
            bool[] Ret = new bool[Count];
            if (Map != null)
            {
                for (double i = 0; i < Count; i++)
                {
                    Ret[(int)i] = Map[(int)(Map.Length * i / Count)];
                }
            }
            return Ret;
        }

        private static MagnetLink ParseLink(string Arg)
        {
            return new MagnetLink(Arg);
        }

        private static InfoHash ParseHash(string Arg)
        {
            return ValidHex(Arg) ? new InfoHash(s2b(Arg)) : null;
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
                //Torrent hashes are 20 bytes. A valid hex string has double the size.
                return hexString.Length == 40;
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

        /*
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
        //*/
    }
}
