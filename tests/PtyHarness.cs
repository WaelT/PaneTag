// Test harness: hosts a command in a headless pseudo console and prints everything it
// renders, with ESC/BEL/CR made visible. Used by smoke.ps1.
//
// Usage: PtyHarness.exe <cols> <rows> <timeoutMs> <command line> [rename title|-] [keys]
//   If a rename title is given (not "-"), it is sent over panetag's pipe after 1.5 s (the
//   command must then be panetag.exe itself, since the pipe name uses the direct child's PID).
//   If keys are given, they are typed into the pane after 3 s.
using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

static class PtyHarness
{
    [StructLayout(LayoutKind.Sequential)]
    struct COORD { public short X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO
    {
        public int cb; public string lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct STARTUPINFOEX { public STARTUPINFO StartupInfo; public IntPtr lpAttributeList; }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("kernel32.dll")] static extern bool CreatePipe(out SafeFileHandle r, out SafeFileHandle w, IntPtr a, int s);
    [DllImport("kernel32.dll")] static extern int CreatePseudoConsole(COORD c, SafeFileHandle i, SafeFileHandle o, uint f, out IntPtr h);
    [DllImport("kernel32.dll")] static extern void ClosePseudoConsole(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool InitializeProcThreadAttributeList(IntPtr l, int c, int f, ref IntPtr s);
    [DllImport("kernel32.dll")] static extern bool UpdateProcThreadAttribute(IntPtr l, uint f, IntPtr a, IntPtr v, IntPtr s, IntPtr p, IntPtr r);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool CreateProcessW(string a, StringBuilder c, IntPtr p, IntPtr t, bool i, uint f, IntPtr e, string d, ref STARTUPINFOEX s, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr h, out uint c);
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr h, uint code);

    static int Main(string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine("usage: PtyHarness <cols> <rows> <timeoutMs> <command line> [rename title]");
            return 2;
        }
        var size = new COORD { X = short.Parse(args[0]), Y = short.Parse(args[1]) };
        uint timeout = uint.Parse(args[2]);

        SafeFileHandle inRead, inWrite, outRead, outWrite;
        CreatePipe(out inRead, out inWrite, IntPtr.Zero, 0);
        CreatePipe(out outRead, out outWrite, IntPtr.Zero, 0);
        IntPtr hpc;
        CreatePseudoConsole(size, inRead, outWrite, 0, out hpc);

        var si = new STARTUPINFOEX();
        si.StartupInfo.cb = Marshal.SizeOf(typeof(STARTUPINFOEX));
        si.StartupInfo.dwFlags = 0x100; // STARTF_USESTDHANDLES with null handles
        IntPtr attrSize = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrSize);
        si.lpAttributeList = Marshal.AllocHGlobal(attrSize);
        InitializeProcThreadAttributeList(si.lpAttributeList, 1, 0, ref attrSize);
        UpdateProcThreadAttribute(si.lpAttributeList, 0, (IntPtr)0x20016, hpc, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero);

        PROCESS_INFORMATION pi;
        if (!CreateProcessW(null, new StringBuilder(args[3]), IntPtr.Zero, IntPtr.Zero, false, 0x80000, IntPtr.Zero, null, ref si, out pi))
        {
            Console.Error.WriteLine("cannot start: " + args[3]);
            return 2;
        }
        inRead.Dispose();
        outWrite.Dispose();

        var captured = new StringBuilder();
        var reader = new Thread(() =>
        {
            var stream = new FileStream(outRead, FileAccess.Read, 1);
            var buf = new byte[8192];
            var decoder = Encoding.UTF8.GetDecoder();
            var chars = new char[8192];
            int n;
            try
            {
                while ((n = stream.Read(buf, 0, buf.Length)) > 0)
                {
                    int c = decoder.GetChars(buf, 0, n, chars, 0);
                    lock (captured) captured.Append(chars, 0, c);
                }
            }
            catch (IOException) { }
        });
        reader.Start();

        if (args.Length > 5)
        {
            var keys = Encoding.UTF8.GetBytes(args[5]);
            new Thread(() =>
            {
                Thread.Sleep(3000);
                lock (captured) captured.Append("\n<<<TYPED>>>\n");
                var input = new FileStream(inWrite, FileAccess.Write, 1);
                input.Write(keys, 0, keys.Length);
                input.Flush();
            }).Start();
        }

        if (args.Length > 4 && args[4] != "-")
        {
            Thread.Sleep(1500);
            try
            {
                using (var pipe = new NamedPipeClientStream(".", "panetag-" + pi.dwProcessId, PipeDirection.Out))
                {
                    pipe.Connect(2000);
                    var w = new StreamWriter(pipe, new UTF8Encoding(false));
                    w.WriteLine("title " + args[4]);
                    w.Flush();
                }
                lock (captured) captured.Append("\n<<<RENAME SENT>>>\n");
            }
            catch (Exception ex)
            {
                lock (captured) captured.Append("\n<<<RENAME FAILED: " + ex.Message + ">>>\n");
            }
        }

        bool timedOut = WaitForSingleObject(pi.hProcess, timeout) != 0;
        if (timedOut) TerminateProcess(pi.hProcess, 259);
        uint code;
        GetExitCodeProcess(pi.hProcess, out code);
        Thread.Sleep(300);
        ClosePseudoConsole(hpc);
        reader.Join(2000);

        string text;
        lock (captured) text = captured.ToString();
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.WriteLine(text.Replace("\x1b", "\\e").Replace("\x07", "\\a").Replace("\r", "\\r"));
        Console.WriteLine("<<<EXIT " + (timedOut ? "TIMEOUT" : code.ToString()) + ">>>");
        return 0;
    }
}
