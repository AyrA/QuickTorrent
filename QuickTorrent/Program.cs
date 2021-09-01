using MonoTorrent;
using MonoTorrent.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace QuickTorrent
{
    class Program
    {
        private enum DisplayMode
        {
            Overview,
            Details,
            Tracker
        }

        private const int UI_UPDATE_INTERVAL = 2000;

        private static DisplayMode CurrentMode = DisplayMode.Overview;
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
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
            LaunchedFromCmd = (ParentProcessUtilities.GetParentProcessName().ToLower() == Environment.ExpandEnvironmentVariables("%COMSPEC%").ToLower());
#if DEBUG
            args = new string[] {
                //2018-04-18-raspbian-stretch-lite.zip
                "05E76C8B1795CE49E203BE4C39E378F7A97CBED5"
            };
#endif
            Console.WriteLine("Grabbing public trackers");
            using (var CL = new HttpClient())
            {
                CL.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "AyrA-QuickTorrent/1.0 +https://github.com/AyrA/QuickTorrent");
                try
                {
                    var Result = CL
                        .GetAsync("https://cable.ayra.ch/tracker/list.php?prot[]=https&prot[]=http")
                        .Result;
                    if (Result.IsSuccessStatusCode)
                    {
                        TorrentHandler.PublicTrackers = new List<string>(Result
                            .Content.ReadAsStringAsync().Result.Split('\n')
                            .Select(m => m.Trim())
                            .Where(m => !string.IsNullOrEmpty(m))
                            .Distinct());
                    }
                    else
                    {
                        throw new Exception("Unable to download Trackers");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Unable to download public tracker list");
                    while (ex != null)
                    {
                        Console.WriteLine("{0}: {1}", ex.GetType().FullName, ex.Message);
                        ex = ex.InnerException;
                    }
                    Thread.Sleep(5000);
                }
            }


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

            Console.Clear();

#if !DEBUG
            foreach (var H in Handler)
            {
                H.Start();
            }
#endif
            //Thread will exit if set to false
            bool cont = true;
            //Exists the Wait loops and updates the screen once
            bool update = false;
            //Completely redraws the screen once
            bool redraw = false;
            //Selected Torrent
            int Selected = 0;
            Thread T = new Thread(delegate ()
            {
                #region Torrent Loop
                const int NAMELENGTH = 30;
                while (cont && Handler.Count(m => m != null) > 0)
                {
                    Console.Title = $"QuickTorrent: {Handler.Count(m => m != null)} transfers";

                    //Pause this thread while in the tracker selection
                    while (CurrentMode == DisplayMode.Tracker)
                    {
                        Thread.Sleep(100);
                    }

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
                    lock (Handler)
                    {
                        //var H = Handler[j];
                        switch (CurrentMode)
                        {
                            case DisplayMode.Overview:
                                for (int j = 0; j < Handler.Count; j++)
                                {

                                    if (Handler[j] != null)
                                    {
                                        string Name = Handler[j].TorrentName == null ? "" : Handler[j].TorrentName;
                                        //Subtraction is: name length, percentage and the 4 spaces
                                        var LineMap = new string(StretchMap(Handler[j].Map, Console.BufferWidth - NAMELENGTH - 8).Select(m => m ? '█' : '░').ToArray());
                                        if (Name.Length > NAMELENGTH)
                                        {
                                            Name = Name.Substring(Name.Length - NAMELENGTH, NAMELENGTH);
                                        }
                                        Console.ForegroundColor = ConsoleColor.White;
                                        Console.Error.Write("{0} ", Selected == j ? '►' : ' ');
                                        Console.ForegroundColor = StateToColor(Handler[j].State);
                                        switch (Handler[j].State)
                                        {
                                            case TorrentState.Metadata:
                                                break;
                                            case TorrentState.Hashing:
                                                break;
                                            case TorrentState.Downloading:
                                                if (Handler[j].HasAllPieces && (int)Handler[j].Progress == 100)
                                                {
                                                    Handler[j].Stop();
                                                    Handler[j].SaveRecovery();
                                                    Handler[j].Start();
                                                }
                                                break;
                                            case TorrentState.Seeding:
                                                if (!Handler[j].HasAllPieces)
                                                {
                                                    //Assume that this torrent is complete
                                                    Handler[j].SetComplete();
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
                                        Console.Error.Write("{0,-" + NAMELENGTH + "} {1,3}% {2}", Name, (int)Handler[j].Progress, LineMap);
                                    }
                                }

                                Console.ResetColor();
                                Console.Error.WriteLine("[↑↓] Select | [SPACE] Start/Stop | [ENTER] Detail | [T] Tracker | [DEL] Delete | [ESC] Exit");
                                break;
                            case DisplayMode.Details:
                                Console.ForegroundColor = StateToColor(Handler[Selected].State);
                                Console.Error.WriteLine("".PadRight(Console.BufferWidth * 7));
                                Console.SetCursorPosition(0, 0);
                                Console.Error.WriteLine(@"Transfer Detail

Name:     {0}
Hash:     {1}
Files:    {2,-5} ({3})
State:    {4,-20}
Progress: {5:0.00}%",
                                    Handler[Selected].TorrentName.PadRight(Console.BufferWidth - 11),
                                    Handler[Selected].InfoHash,
                                    Handler[Selected].Files,
                                    NiceSize(Handler[Selected].TotalSize),
                                    Handler[Selected].State,
                                    Math.Round(Handler[Selected].Progress, 2));
                                var FullMap = new string(StretchMap(Handler[Selected].Map, Console.BufferWidth * (Console.WindowHeight - 8)).Select(m => m ? '█' : '░').ToArray());
                                Console.Error.Write("{0}[ESC] Back | [T] Tracker", FullMap);
                                Console.ResetColor();
                                break;
                            case DisplayMode.Tracker:
                                //Don't do anything on tracker, this is done outside of the update loop
                                break;
                            default:
                                throw new NotImplementedException($"Unimplemented Mode: {CurrentMode}");
                        }
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
            { IsBackground = true, Name = "StatusLoop" };
            T.Start();

            #region Keyboard Handler
            while (cont && Handler.Count(m => m != null) > 0)
            {
                var H = Handler[Selected];
                switch (Console.ReadKey(true).Key)
                {
                    case ConsoleKey.T:
                        var OldMode = CurrentMode;
                        CurrentMode = DisplayMode.Tracker;
                        ManageTracker(H);
                        CurrentMode = OldMode;
                        redraw = update = true;
                        break;
                    case ConsoleKey.Escape:
                        if (CurrentMode == DisplayMode.Details || CurrentMode == DisplayMode.Tracker)
                        {
                            CurrentMode = DisplayMode.Overview;
                            redraw = update = true;
                        }
                        else if (CurrentMode == DisplayMode.Overview)
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
                        if (H.State == TorrentState.Stopped || H.State == TorrentState.Error)
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
                        if (Handler.Count > 0)
                        {
                            if (Selected >= Handler.Count)
                            {
                                Selected = Handler.Count - 1;
                            }
                        }
                        else
                        {
                            cont = false;
                        }
                        update = redraw = true;
                        break;
                    case ConsoleKey.Enter:
                        CurrentMode = DisplayMode.Details;
                        update = true;
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

        private static void ManageTracker(TorrentHandler H)
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Adding and removing trackers is not supported on MonoTorrent");
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("List of public trackers we know:");
            if (TorrentHandler.PublicTrackers != null)
            {
                foreach (var T in TorrentHandler.PublicTrackers)
                {
                    Console.WriteLine(T);
                }
            }
            else
            {
                Console.WriteLine("none (Download of list failed)");
            }
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Trackers registered for this torrent");
            foreach (var T in H.GetTracker())
            {
                Console.WriteLine("{0,-30} {1}", T.Status, T.Uri);
            }
            Console.Write("Press any key to go back");
            Console.ReadKey();
            /*
            bool cont = true;
            while (cont)
            {
                Console.Clear();
                Console.SetCursorPosition(0, Console.WindowHeight - 1);
                Console.Write("Enter a new tracker URL to add or nothing to go back");
                Console.SetCursorPosition(0, 0);
                foreach (var T in H.GetTracker())
                {
                    Console.WriteLine(T);
                }
                var URL = Console.ReadLine();
                if (!string.IsNullOrEmpty(URL))
                {
                    try
                    {
                        H.AddTracker(new Uri(URL));
                    }
                    catch
                    {
                        //NOOP
                    }
                }
                else
                {
                    cont = false;
                }
            }
            //*/
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
            //Add out public trackers to the link
            if (TorrentHandler.PublicTrackers != null)
            {
                var Trackers = string.Join("&", TorrentHandler.PublicTrackers.Select(m => $"tr={m}"));
                if (Arg.Contains("?"))
                {
                    Arg += (Arg.EndsWith("?") ? "" : "&") + Trackers;
                }
                else
                {
                    Arg += "?" + Trackers;
                }
            }
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
