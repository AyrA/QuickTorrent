using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace QuickTorrent
{
    public static class Pipe
    {
        public static bool PipeOpen
        { get; private set; }

        public delegate void HashAddedHandler(string Hash);

        public static event HashAddedHandler HashAdded = delegate { };

        public static bool StartPipe()
        {
            try
            {
                var Listener = new NamedPipeServerStream("QuickTorrent_AddHash", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 1000, 1000);
                Listener.BeginWaitForConnection(ConIn, Listener);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return false;
            }
            return PipeOpen = true;
        }

        public static bool SendViaPipe(string Content)
        {
            if (!PipeOpen)
            {
                var Data = Encoding.UTF8.GetBytes(Content);
                try
                {
                    using (var Sender = new NamedPipeClientStream(".", "QuickTorrent_AddHash", PipeDirection.Out))
                    {
                        Sender.Connect(3000);
                        Sender.Write(BitConverter.GetBytes(Data.Length), 0, 4);
                        Sender.Write(Data, 0, Data.Length);
                        Sender.WaitForPipeDrain();
                        Sender.Close();
                    }
                    Debug.WriteLine("Transferred torrent " + Content, "OPB_PIPE");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message, "OPB_PIPE");
                    return false;
                }
            }
            else
            {
                HashAdded(Content);
                return true;
            }
        }

        private static void ConIn(IAsyncResult ar)
        {
            var Listener = (NamedPipeServerStream)ar.AsyncState;
            try
            {
                Listener.EndWaitForConnection(ar);
            }
            catch
            {
                PipeOpen = false;
                //Stop listening and abort
                return;
            }
            using (var BR = new BinaryReader(Listener, Encoding.UTF8, true))
            {
                try
                {
                    HashAdded(Encoding.UTF8.GetString(BR.ReadBytes(BR.ReadInt32())));
                }
                catch
                {
                    //Client sent crap
                }
            }
            Listener.Disconnect();
            //Listen for next connection
            Listener.BeginWaitForConnection(ConIn, Listener);
        }
    }
}
