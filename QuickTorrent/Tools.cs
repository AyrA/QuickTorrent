using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QuickTorrent
{
    /// <summary>
    /// A utility class to determine a process parent.
    /// </summary>
    /// <see cref="http://stackoverflow.com/a/3346055"/>
    [StructLayout(LayoutKind.Sequential)]
    public struct ParentProcessUtilities
    {
        // These members must match PROCESS_BASIC_INFORMATION
        internal IntPtr Reserved1;
        internal IntPtr PebBaseAddress;
        internal IntPtr Reserved2_0;
        internal IntPtr Reserved2_1;
        internal IntPtr UniqueProcessId;
        internal IntPtr InheritedFromUniqueProcessId;

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass, ref ParentProcessUtilities processInformation, int processInformationLength, out int returnLength);

        [DllImport("kernel32.dll")]
        private static extern bool QueryFullProcessImageName(IntPtr processHandle, int dwFlags, IntPtr exeName, out int strLength);

        public static string GetParentProcessName()
        {
            using (Process P = Process.GetCurrentProcess())
            {
                return GetParentProcessName(P.Handle);
            }
        }

        public static string GetParentProcessName(int ProcessId)
        {
            using (Process P = Process.GetProcessById(ProcessId))
            {
                return GetParentProcessName(P.Handle);
            }
        }

        public static string GetParentProcessName(IntPtr Handle)
        {
            using (var Proc = GetParentProcess(Handle))
            {
                int MAXLEN = 1000;
                IntPtr P = Marshal.AllocHGlobal(MAXLEN);
                string ret = string.Empty;
                if (QueryFullProcessImageName(Proc.Handle, 0, P, out MAXLEN))
                {
                    ret = Marshal.PtrToStringAnsi(P, MAXLEN);
                }
                Marshal.FreeHGlobal(P);
                return ret;
            }
        }

        /// <summary>
        /// Gets the parent process of the current process.
        /// </summary>
        /// <returns>An instance of the Process class.</returns>
        public static Process GetParentProcess()
        {
            using (var Proc = Process.GetCurrentProcess())
            {
                return GetParentProcess(Proc.Handle);
            }
        }

        /// <summary>
        /// Gets the parent process of specified process.
        /// </summary>
        /// <param name="id">The process id.</param>
        /// <returns>An instance of the Process class.</returns>
        public static Process GetParentProcess(int id)
        {
            using (Process process = Process.GetProcessById(id))
            {
                return GetParentProcess(process.Handle);
            }
        }

        /// <summary>
        /// Gets the parent process of a specified process.
        /// </summary>
        /// <param name="handle">The process handle.</param>
        /// <returns>An instance of the Process class or null if an error occurred.</returns>
        public static Process GetParentProcess(IntPtr handle)
        {
            ParentProcessUtilities pbi = new ParentProcessUtilities();
            int returnLength;
            int status = NtQueryInformationProcess(handle, 0, ref pbi, Marshal.SizeOf(pbi), out returnLength);
            if (status != 0)
                return null;

            try
            {
                return Process.GetProcessById(pbi.InheritedFromUniqueProcessId.ToInt32());
            }
            catch (ArgumentException)
            {
                // not found
                return null;
            }
        }
    }
}
