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
        private const int UI_UPDATE_INTERVAL = 1000;

        private static bool LaunchedFromCmd;
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
            /// <summary>
            /// The downloads were transferred to another instance
            /// </summary>
            public const int TRANSFER = 3;
        }

        public static int Main(string[] args)
        {
            LaunchedFromCmd = (ParentProcessUtilities.GetParentProcessName().ToLower() == Environment.ExpandEnvironmentVariables("%COMSPEC%").ToLower());
#if DEBUG
            args = new string[] {
                /* <-- Use two slashes to swap the hashes below

                //Debian DVD images
                "40F90995A1C16A1BF454D09907F57700F3E8BD64",
                "86F72E6B829C7E2D089B5CF0D1DED09122E65CA4",
                "E355364EBEB5838B27705C68F85B20A950110B8F"

                /*/

                //Debian CD images
                "A7055D06E5A8F7F816EC01AC7F7F5243D3CB008F",
                "63515D99E2A99E79526E35C9A39A1DFD843027A0",
                "297ACD1A5D6BA3E1AB881E27ACB73843A6E81430",
                "9E2C34C3FD30D25FED9A23D93FCC69AE546C1D10",
                "A887676B3E790DD28A92772B776199529F477762",
                "7980011D310EB8B6F8F5F593E82F83510901CE1C",
                "90C52B25375F12432D504BAD7F587E5543F22FDA",
                "1DADBB8451DDDF23ADB276D0C887EF60EC49989E"
                //*/
            };
#endif
            Handler = new List<TorrentHandler>();
            //Allows adding torrents via pipe
            Pipe.HashAdded += delegate (string Hash)
            {
                var Entry = GetHandler(Hash);
                if (Entry != null)
                {
                    lock (Handler)
                    {
                        //only add if not existing already
                        if (!Handler.Any(m => m != null && m.InfoHash == Entry.InfoHash))
                        {
                            Handler.Add(Entry);
                        }
                    }
#if !DEBUG
                    Entry.Start();
#endif
                }
            };

            #region Initial Download Creator
            if (args.Length > 0)
            {
                if (!args.Contains("/?"))
                {
                    foreach (var arg in args)
                    {
                        var HandlerEntry = GetHandler(arg);
                        if (HandlerEntry == null)
                        {
                            Console.Error.WriteLine("Invalid Argument. Only file names, magnet links and hashes are supported.");
                            WaitForExit();
                            return RET.ARGUMENT_ERROR;
                        }
                        else
                        {
                            Handler.Add(HandlerEntry);
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
                    WaitForExit();
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
                    WaitForExit();
                    return RET.NAME_ERROR;
                }
            }
            #endregion

            for (int i = 0; i < Handler.Count; i++)
            {
                if (Handler[i] == null)
                {
                    Handler.RemoveAt(i--);
                }
            }
            //If we can't start a pipe it is likely that there is already an instance running
            if (!Pipe.StartPipe())
            {
                //Try to transfer all downloads
                if (!Handler.Select(m => Pipe.SendViaPipe(m.InfoHash)).ToArray().Any(m => m))
                {
                    //We can neither start the pipe nor transfer requests to another client.
                    Console.Error.WriteLine("Unable to start pipe or transfer downloads to other instance. Adding torrents at Runtime will be unavailable");
                    Thread.Sleep(5000);
                }
                else
                {
                    //Requests transferred. Exit application
                    return RET.TRANSFER;
                }
            }

#if !DEBUG
            TorrentHandler.StartAll();
#endif
            //Thread will exit if set to false
            bool cont = true;
            //Exists the Wait loops and updates the screen once
            bool update = false;
            //Completely redraws the screen once
            bool redraw = false;
            //Selected Torrent
            int Selected = 0;
            //True to render selected torrent detail, false for list
            bool RenderDetail = false;
            Thread T = new Thread(delegate ()
            {
                #region Torrent Loop
                const int NAMELENGTH = 30;
                while (cont && Handler.Count(m => m != null) > 0)
                {
                    Console.Title = $"QuickTorrent: {Handler.Count(m => m != null)} transfers";

                    int CurrentSelected = Selected;
                    if (redraw)
                    {
                        Console.Clear();
                        redraw = false;
                    }
                    else
                    {
                        Console.SetCursorPosition(0, 0);
                    }
                    if (!RenderDetail)
                    {
                        lock (Handler)
                        {
                            for (int j = 0; j < Handler.Count; j++)
                            {
                                var H = Handler[j];
                                string Name = H.TorrentName == null ? "" : H.TorrentName;
                                //Subtraction is: name length, percentage and the 4 spaces
                                var Map = new string(StretchMap(H.Map, Console.BufferWidth - NAMELENGTH - 8).Select(m => m ? '█' : '░').ToArray());
                                if (Name.Length > NAMELENGTH)
                                {
                                    Name = Name.Substring(Name.Length - NAMELENGTH, NAMELENGTH);
                                }
                                Console.ForegroundColor = ConsoleColor.White;
                                Console.Error.Write("{0} ", Selected == j ? '►' : ' ');
                                Console.ForegroundColor = StateToColor(H.State);
                                switch (H.State)
                                {
                                    case TorrentState.Metadata:
                                        break;
                                    case TorrentState.Hashing:
                                        break;
                                    case TorrentState.Downloading:
                                        if (H.HasAllPieces && (int)H.Progress == 100)
                                        {
                                            H.Stop();
                                            H.SaveRecovery();
                                            H.Start();
                                        }
                                        break;
                                    case TorrentState.Seeding:
                                        if (!H.HasAllPieces)
                                        {
                                            //Assume that this torrent is complete
                                            H.SetComplete();
                                        }
                                        break;
                                    case TorrentState.Stopped:
                                    case TorrentState.Stopping:
                                        break;
                                    case TorrentState.Paused:
                                        break;
                                    default:
                                        break;
                                }

                                Console.Error.Write("{0,-" + NAMELENGTH + "} {1,3}% {2}", Name, (int)H.Progress, Map);
                            }
                        }
                        Console.ResetColor();
                        Console.Error.WriteLine("[↑↓] Select | [SPACE] Start/Stop | [ENTER] Detail | [DEL] Delete | [ESC] Exit");
                    }
                    else
                    {
                        var H = Handler[Selected];
                        Console.ForegroundColor = StateToColor(H.State);
                        Console.Error.WriteLine("".PadRight(Console.BufferWidth * 7));
                        Console.SetCursorPosition(0, 0);
                        Console.Error.WriteLine(@"Transfer Detail

Name:     {0}
Hash:     {1}
Files:    {2,-5} ({3})
State:    {4,-20}
Progress: {5:0.00}%",
    H.TorrentName.PadRight(Console.BufferWidth - 11),
    H.InfoHash,
    H.Files,
    NiceSize(H.TotalSize),
    H.State,
    Math.Round(H.Progress, 2));
                        var Map = new string(StretchMap(H.Map, Console.BufferWidth * (Console.WindowHeight - 8)).Select(m => m ? '█' : '░').ToArray());
                        Console.Error.Write("{0}[ESC] Back", Map);
                        Console.ResetColor();
                    }

                    int i = 0;
                    //This makes the thread responsive to exit calls (cont=false)
                    while (cont && i < UI_UPDATE_INTERVAL && CurrentSelected == Selected && !update)
                    {
                        i += 100;
                        Thread.Sleep(100);
                    }
                    update = false;
                }
                #endregion
            })
            { IsBackground = true, Name = "Status" };
            T.Start();

            #region Keyboard Handler
            while (cont)
            {
                var H = Handler[Selected];
                switch (Console.ReadKey(true).Key)
                {
                    case ConsoleKey.Escape:
                        if (RenderDetail)
                        {
                            RenderDetail = false;
                            redraw = update = true;
                        }
                        else
                        {
                            cont = false;
                        }
                        break;
                    case ConsoleKey.UpArrow:
                        if (--Selected < 0)
                        {
                            Selected = 0;
                        }
                        update = true;
                        break;
                    case ConsoleKey.DownArrow:
                        if (++Selected >= Handler.Count)
                        {
                            Selected = Handler.Count - 1;
                        }
                        update = true;
                        break;
                    case ConsoleKey.Spacebar:
                        if (H.State == TorrentState.Stopped)
                        {
                            H.Start();
                            update = true;
                        }
                        else if (H.State != TorrentState.Stopping &&
                            H.State != TorrentState.Error &&
                            H.State != TorrentState.Paused)
                        {
                            H.Stop();
                            update = true;
                        }
                        break;
                    case ConsoleKey.Delete:
                        lock (Handler)
                        {
                            H.Stop();
                            H.ClearRecovery();
                            H.Dispose();
                            Handler.Remove(H);
                        }
                        update = redraw = true;
                        break;
                    case ConsoleKey.Enter:
                        RenderDetail = update = true;
                        break;
                }
            }
            #endregion
            T.Join();
            Console.Error.WriteLine("Exiting...");
            TorrentHandler.StopAll();
            foreach (var H in Handler.Where(m => m != null))
            {
                Console.Error.Write('.');
                H.SaveRecovery();
                H.Dispose();
            }
            TorrentHandler.SaveDhtNodes();
            Console.Error.WriteLine("DONE. Cleaning up...");
            return RET.SUCCESS;
        }

        private static TorrentHandler GetHandler(string Arg)
        {
            if (Arg.ToLower().StartsWith("magnet:"))
            {
                return new TorrentHandler(ParseLink(Arg));
            }
            else if (File.Exists(Arg))
            {
                return new TorrentHandler(ParseTorrent(Arg));
            }
            else if (ValidHex(Arg))
            {
                return new TorrentHandler(ParseHash(Arg));
            }
            return null;
        }

        private static void WaitForExit()
        {
            if (!LaunchedFromCmd)
            {
                Console.WriteLine("Press any key to continue");
                Console.ReadKey(true);
            }
        }

        private static string NiceSize(double SizeInBytes)
        {
            string[] Sizes = "B,KB,MB,GB,TB,EB,PB".Split(',');
            int Current = 0;
            while (SizeInBytes >= 1024.0)
            {
                SizeInBytes /= 1024.0;
                ++Current;
            }

            return $"{Math.Round(SizeInBytes, 2)} {Sizes[Current]}";
        }

        private static ConsoleColor StateToColor(TorrentState State)
        {
            switch (State)
            {
                case TorrentState.Metadata:
                    return ConsoleColor.Yellow;
                case TorrentState.Hashing:
                    return ConsoleColor.Cyan;
                case TorrentState.Downloading:
                    return ConsoleColor.Green;
                case TorrentState.Seeding:
                    return ConsoleColor.Blue;
                case TorrentState.Stopped:
                case TorrentState.Stopping:
                    return ConsoleColor.Gray;
                case TorrentState.Paused:
                    return ConsoleColor.DarkGray;
                default:
                    return ConsoleColor.Red;
            }
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
    }
}
