using System;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace QuickTorrent
{
    public static class Pipe
    {
        public delegate void HashAddedHandler(string Hash);

        public static event HashAddedHandler HashAdded = delegate { };

        public static bool StartPipe()
        {
            try
            {
                var Listener = new NamedPipeServerStream("QuickTorrent_AddHash", PipeDirection.In,NamedPipeServerStream.MaxAllowedServerInstances,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,1000,1000);
                Listener.BeginWaitForConnection(ConIn, Listener);
            }
            catch(Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return false;
            }
            return true;
        }

        static void ConIn(IAsyncResult ar)
        {
            var Listener = (NamedPipeServerStream)ar.AsyncState;
            try
            {
                Listener.EndWaitForConnection(ar);
            }
            catch
            {
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
