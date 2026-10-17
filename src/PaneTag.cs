// panetag: runs a shell/program inside a pseudo console that is one row shorter
// than the terminal pane, and keeps a title bar on the reserved row. Child output is
// passed through a small VT rewriter so full-screen TUIs (claude, codex, vim, ...)
// can never draw over or scroll away the bar.
//
// Compiles with the C# 5 compiler that ships with .NET Framework (see build.ps1).

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace PaneTag
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct COORD
        {
            public short X;
            public short Y;
            public COORD(int x, int y) { X = (short)x; Y = (short)y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SMALL_RECT { public short Left; public short Top; public short Right; public short Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct CONSOLE_SCREEN_BUFFER_INFO
        {
            public COORD dwSize;
            public COORD dwCursorPosition;
            public short wAttributes;
            public SMALL_RECT srWindow;
            public COORD dwMaximumWindowSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct STARTUPINFOEX
        {
            public STARTUPINFO StartupInfo;
            public IntPtr lpAttributeList;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        public const int STD_INPUT_HANDLE = -10;
        public const int STD_OUTPUT_HANDLE = -11;
        public const uint ENABLE_PROCESSED_OUTPUT = 0x1;
        public const uint ENABLE_WRAP_AT_EOL_OUTPUT = 0x2;
        public const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x4;
        public const uint DISABLE_NEWLINE_AUTO_RETURN = 0x8;
        public const uint ENABLE_VIRTUAL_TERMINAL_INPUT = 0x200;
        public const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
        public const int STARTF_USESTDHANDLES = 0x100;
        public const uint INFINITE = 0xFFFFFFFF;
        public static readonly IntPtr PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = (IntPtr)0x00020016;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

        [DllImport("kernel32.dll")]
        public static extern void GetSystemTimePreciseAsFileTime(out long time);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetConsoleMode(IntPtr h, out uint mode);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetConsoleMode(IntPtr h, uint mode);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetConsoleScreenBufferInfo(IntPtr h, out CONSOLE_SCREEN_BUFFER_INFO info);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint GetConsoleOutputCP();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetConsoleOutputCP(uint cp);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool ReadConsoleW(IntPtr h, [Out] char[] buffer, int toRead, out int read, IntPtr control);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(IntPtr h, byte[] buffer, int count, out int written, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, IntPtr attributes, int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern int CreatePseudoConsole(COORD size, SafeFileHandle input, SafeFileHandle output, uint flags, out IntPtr hpc);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern int ResizePseudoConsole(IntPtr hpc, COORD size);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern void ClosePseudoConsole(IntPtr hpc);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returnSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern void DeleteProcThreadAttributeList(IntPtr list);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CreateProcessW(string application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
            bool inheritHandles, uint flags, IntPtr environment, string currentDirectory, ref STARTUPINFOEX startupInfo, out PROCESS_INFORMATION info);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr h, uint ms);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetExitCodeProcess(IntPtr h, out uint code);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr h);

        public delegate bool ConsoleCtrlHandler(uint ctrlType);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandler handler, bool add);
    }

    internal struct Rgb
    {
        public int R, G, B;
        public Rgb(int hex) { R = (hex >> 16) & 0xFF; G = (hex >> 8) & 0xFF; B = hex & 0xFF; }
        public string Sgr(int layer) { return layer + ";2;" + R + ";" + G + ";" + B; }
    }

    internal static class Colors
    {
        static readonly string[] Names = { "blue", "green", "purple", "orange", "red", "teal", "pink", "yellow", "indigo", "cyan", "brown", "gray", "black", "white" };
        static readonly int[] Values = { 0x1F6FEB, 0x1A7F37, 0x8250DF, 0xD9730D, 0xCF222E, 0x0F8B8D, 0xBF3989, 0xD4A72C, 0x4F46E5, 0x0891B2, 0x8B5E3C, 0x57606A, 0x24292F, 0xEAEEF2 };
        const int AutoCount = 11; // first N entries are used for automatic per-title colors

        public static bool TryParse(string spec, out Rgb rgb)
        {
            rgb = new Rgb(0);
            if (string.IsNullOrEmpty(spec)) return false;
            spec = spec.Trim();
            for (int i = 0; i < Names.Length; i++)
            {
                if (string.Equals(Names[i], spec, StringComparison.OrdinalIgnoreCase)) { rgb = new Rgb(Values[i]); return true; }
            }
            string hex = spec.TrimStart('#');
            if (hex.Length == 3) hex = new string(new[] { hex[0], hex[0], hex[1], hex[1], hex[2], hex[2] });
            int value;
            if (hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out value))
            {
                rgb = new Rgb(value);
                return true;
            }
            return false;
        }

        // Stable (FNV-1a) hash so the same title always gets the same color.
        public static Rgb ForTitle(string title)
        {
            uint h = 2166136261;
            foreach (char c in title) { h ^= c; h *= 16777619; }
            return new Rgb(Values[h % AutoCount]);
        }

        public static Rgb Contrast(Rgb bg)
        {
            double lum = 0.2126 * bg.R + 0.7152 * bg.G + 0.0722 * bg.B;
            return new Rgb(lum > 150 ? 0x111111 : 0xFFFFFF);
        }

        public static string NameList { get { return string.Join(", ", Names); } }
    }

    internal static class Cells
    {
        public static int CharWidth(int cp)
        {
            if (cp < 0x20 || (cp >= 0x7F && cp < 0xA0)) return 0;
            if ((cp >= 0x0300 && cp <= 0x036F) || (cp >= 0x200B && cp <= 0x200F) || (cp >= 0xFE00 && cp <= 0xFE0F)) return 0;
            if ((cp >= 0x1100 && cp <= 0x115F) || (cp >= 0x2E80 && cp <= 0xA4CF) || (cp >= 0xAC00 && cp <= 0xD7A3) ||
                (cp >= 0xF900 && cp <= 0xFAFF) || (cp >= 0xFE30 && cp <= 0xFE4F) || (cp >= 0xFF00 && cp <= 0xFF60) ||
                (cp >= 0xFFE0 && cp <= 0xFFE6) || (cp >= 0x1F300 && cp <= 0x1F64F) || (cp >= 0x1F900 && cp <= 0x1F9FF) ||
                (cp >= 0x20000 && cp <= 0x3FFFD)) return 2;
            return 1;
        }

        static int Next(string s, int i, out int length)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                length = 2;
                return char.ConvertToUtf32(s[i], s[i + 1]);
            }
            length = 1;
            return s[i];
        }

        public static int Width(string s)
        {
            int w = 0, len;
            for (int i = 0; i < s.Length; i += len) w += CharWidth(Next(s, i, out len));
            return w;
        }

        // Truncates to at most max cells, ending with an ellipsis when cut.
        public static string Fit(string s, int max)
        {
            if (max <= 0) return "";
            if (Width(s) <= max) return s;
            var sb = new StringBuilder();
            int w = 0, len;
            for (int i = 0; i < s.Length; i += len)
            {
                int cw = CharWidth(Next(s, i, out len));
                if (w + cw > max - 1) break;
                sb.Append(s, i, len);
                w += cw;
            }
            return sb.Append('…').ToString();
        }

        public static string Sanitize(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c < 0x20 || (c >= 0x7F && c < 0xA0)) continue;
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }
    }

    internal sealed class Options
    {
        public string Title;
        public string Command;
        public string Cwd;
        public string Color;
        public string Fg;
        public bool Top = true;
    }

    internal sealed class Wrapper
    {
        // Output parser states.
        const int Ground = 0, Esc = 1, EscInter = 2, Csi = 3, Osc = 4, OscEsc = 5, Str = 6, StrEsc = 7;
        const int MaxOsc = 1 << 20;
        // Not "\x1b7": C# \x escapes are variable length and would swallow the digit.
        const string SaveCursor = "\u001b7";
        const string RestoreCursor = "\u001b8";

        static readonly Regex InputReports = new Regex(
            @"\x1b\[(?:<(\d{1,6});(\d{1,6});(\d{1,6})([Mm])|(\??)(\d{1,6});(\d{1,6})(;\d{1,6})?R)", RegexOptions.Compiled);
        static readonly Regex NonKeyInput = new Regex(
            @"\x1b\[(?:[IO]|<\d{1,6};\d{1,6};\d{1,6}[Mm]|\??\d{1,6};\d{1,6}(?:;\d{1,6})?R)", RegexOptions.Compiled);
        static readonly Regex Win32Key = new Regex(
            @"\x1b\[(\d*);(\d*);(\d*);(\d*);(\d*);(\d*)_", RegexOptions.Compiled);

        readonly object gate = new object();
        readonly Options opt;
        IntPtr hOut, hIn, hPC;
        FileStream ptyIn;
        SafeFileHandle ptyOutRead;
        uint origOutMode, origInMode, origCP;
        string origTitle;
        Native.ConsoleCtrlHandler ctrlHandler;
        string pipeName;
        volatile bool exiting;

        string title, appTitle = "";
        Rgb bg, fg;
        bool colorExplicit, fgExplicit;
        // Status overlay (e.g. "Your turn" from a Claude Code hook): recolors the bar until
        // cleared, or until the next keypress in the pane unless sticky.
        string status = "";
        Rgb statusBg = new Rgb(0xD9730D);
        bool statusSticky;
        // Hooks run async, each in its own ptag process, so their updates can arrive out of
        // order. Each carries its process creation time (FILETIME); older ones are dropped,
        // and so is a non-sticky status raised before the last keypress.
        long statusStamp, keyStamp;
        // With a timer the label grows "· 4m" once a minute has passed, so with several
        // flagged panes you can tell which has waited longest. statusSince is UTC FILETIME.
        bool statusTimer;
        long statusSince, pendingSince; // pendingSince: the current pipe batch's start time
        int drawnMinutes;
        int rows, cols;

        // Terminal state mirrored from the child's output stream.
        int childTop, childBottom; // 0 = default (full inner screen)
        bool decom, autowrap = true, altScreen, childSavedCursor;
        int pendingCpr;
        bool headerDirty, marginsDirty, tabDirty;
        DateTime dirtySince;

        int state = Ground;
        bool strIsOsc;
        readonly MemoryStream seq = new MemoryStream();
        int utf8Rem;

        public Wrapper(Options options) { opt = options; }

        int Offset { get { return opt.Top ? 1 : 0; } }
        int InnerRows { get { return Math.Max(1, rows - 1); } }
        int HeaderRow { get { return opt.Top ? 1 : rows; } }

        public int Run()
        {
            hOut = Native.GetStdHandle(Native.STD_OUTPUT_HANDLE);
            hIn = Native.GetStdHandle(Native.STD_INPUT_HANDLE);
            if (!Native.GetConsoleMode(hOut, out origOutMode) || !Native.GetConsoleMode(hIn, out origInMode))
                throw new InvalidOperationException("stdin/stdout must be a console (run this inside Windows Terminal or a console window).");
            if (!ReadSize(out cols, out rows)) throw new Win32Exception(Marshal.GetLastWin32Error(), "cannot read console size");

            title = Cells.Sanitize(opt.Title ?? "");
            Rgb c;
            if (opt.Color != null && Colors.TryParse(opt.Color, out c)) { bg = c; colorExplicit = true; }
            else bg = Colors.ForTitle(title);
            if (opt.Fg != null && Colors.TryParse(opt.Fg, out c)) { fg = c; fgExplicit = true; }
            else fg = Colors.Contrast(bg);

            try { origTitle = Console.Title; } catch (IOException) { origTitle = null; }
            Native.SetConsoleMode(hOut, origOutMode | Native.ENABLE_PROCESSED_OUTPUT | Native.ENABLE_WRAP_AT_EOL_OUTPUT |
                Native.ENABLE_VIRTUAL_TERMINAL_PROCESSING | Native.DISABLE_NEWLINE_AUTO_RETURN);
            Native.SetConsoleMode(hIn, Native.ENABLE_VIRTUAL_TERMINAL_INPUT);
            origCP = Native.GetConsoleOutputCP();
            Native.SetConsoleOutputCP(65001);
            // Ctrl+C arrives as a plain 0x03 byte (processed input is off) and is forwarded to
            // the child; ignore console signals so a stray Ctrl+Break doesn't kill the wrapper.
            ctrlHandler = type => type <= 1;
            Native.SetConsoleCtrlHandler(ctrlHandler, true);

            try
            {
                return RunChild();
            }
            finally
            {
                Restore();
            }
        }

        int RunChild()
        {
            pipeName = "panetag-" + System.Diagnostics.Process.GetCurrentProcess().Id;
            Environment.SetEnvironmentVariable("PANETAG_PIPE", pipeName);
            AddSelfToPath();

            SafeFileHandle inRead, inWrite, outRead, outWrite;
            if (!Native.CreatePipe(out inRead, out inWrite, IntPtr.Zero, 0) || !Native.CreatePipe(out outRead, out outWrite, IntPtr.Zero, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe failed");
            int hr = Native.CreatePseudoConsole(new Native.COORD(cols, InnerRows), inRead, outWrite, 0, out hPC);
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);

            Native.PROCESS_INFORMATION pi = Spawn();
            inRead.Dispose();
            outWrite.Dispose();
            ptyIn = new FileStream(inWrite, FileAccess.Write, 1);
            ptyOutRead = outRead;

            lock (gate)
            {
                var o = new MemoryStream();
                Emit(o, "\x1b[0m\x1b[H\x1b[2J");
                tabDirty = true;
                MarkDirty(true);
                TryFlushHeader(o, true);
                Emit(o, Cup(1, 1));
                WriteOut(o);
            }

            var output = StartThread(OutputLoop, false);
            StartThread(InputLoop, true);
            StartThread(PollLoop, true);
            StartThread(PipeLoop, true);

            Native.WaitForSingleObject(pi.hProcess, Native.INFINITE);
            uint code;
            Native.GetExitCodeProcess(pi.hProcess, out code);
            Native.CloseHandle(pi.hProcess);

            exiting = true;
            // Closing the pseudo console flushes its last frame and then breaks the output pipe.
            Native.ClosePseudoConsole(hPC);
            output.Join(3000);
            return (int)code;
        }

        // Makes `ptag New title` callable from any shell in the pane (bash, cmd, Claude
        // Code's ! commands) without changing the user's PATH outside it.
        static void AddSelfToPath()
        {
            string dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string entry in path.Split(';'))
            {
                if (string.Equals(entry.TrimEnd('\\'), dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
            }
            Environment.SetEnvironmentVariable("PATH", path.TrimEnd(';') + ";" + dir);
        }

        static Thread StartThread(ThreadStart body, bool background)
        {
            var t = new Thread(body);
            t.IsBackground = background;
            t.Start();
            return t;
        }

        Native.PROCESS_INFORMATION Spawn()
        {
            var si = new Native.STARTUPINFOEX();
            si.StartupInfo.cb = Marshal.SizeOf(typeof(Native.STARTUPINFOEX));
            // Null std handles + STARTF_USESTDHANDLES keeps the child from inheriting ours;
            // it gets the pseudo console's handles instead.
            si.StartupInfo.dwFlags = Native.STARTF_USESTDHANDLES;

            IntPtr size = IntPtr.Zero;
            Native.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            si.lpAttributeList = Marshal.AllocHGlobal(size);
            try
            {
                if (!Native.InitializeProcThreadAttributeList(si.lpAttributeList, 1, 0, ref size))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "InitializeProcThreadAttributeList failed");
                if (!Native.UpdateProcThreadAttribute(si.lpAttributeList, 0, Native.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, hPC,
                        (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "UpdateProcThreadAttribute failed");

                string cwd = !string.IsNullOrEmpty(opt.Cwd) && Directory.Exists(opt.Cwd) ? opt.Cwd : null;
                Native.PROCESS_INFORMATION pi;
                if (!Native.CreateProcessW(null, new StringBuilder(opt.Command), IntPtr.Zero, IntPtr.Zero, false,
                        Native.EXTENDED_STARTUPINFO_PRESENT, IntPtr.Zero, cwd, ref si, out pi))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "cannot start: " + opt.Command);
                Native.CloseHandle(pi.hThread);
                return pi;
            }
            finally
            {
                Native.DeleteProcThreadAttributeList(si.lpAttributeList);
                Marshal.FreeHGlobal(si.lpAttributeList);
            }
        }

        void Restore()
        {
            lock (gate)
            {
                var sb = new StringBuilder("\x1b[0m");
                if (altScreen) sb.Append("\x1b[?1049l");
                // Undo modes a crashed TUI may have left behind, then release the reserved row.
                sb.Append("\x1b[?1000l\x1b[?1002l\x1b[?1003l\x1b[?1006l\x1b[?2004l\x1b[?25h\x1b[?7h\x1b[?6l");
                sb.Append(SaveCursor + "\x1b[r\x1b[").Append(HeaderRow).Append(";1H\x1b[2K" + RestoreCursor);
                if (origTitle != null) sb.Append("\x1b]0;").Append(Cells.Sanitize(origTitle)).Append('\x07');
                var o = new MemoryStream();
                Emit(o, sb.ToString());
                WriteOut(o);
            }
            Native.SetConsoleMode(hOut, origOutMode);
            Native.SetConsoleMode(hIn, origInMode);
            if (origCP != 0) Native.SetConsoleOutputCP(origCP);
        }

        bool ReadSize(out int c, out int r)
        {
            Native.CONSOLE_SCREEN_BUFFER_INFO info;
            if (!Native.GetConsoleScreenBufferInfo(hOut, out info)) { c = r = 0; return false; }
            c = Math.Max(1, info.srWindow.Right - info.srWindow.Left + 1);
            r = Math.Max(2, info.srWindow.Bottom - info.srWindow.Top + 1);
            return true;
        }

        // ---------------------------------------------------------------- threads

        void OutputLoop()
        {
            var stream = new FileStream(ptyOutRead, FileAccess.Read, 1);
            var buf = new byte[65536];
            var o = new MemoryStream();
            while (true)
            {
                int n;
                try { n = stream.Read(buf, 0, buf.Length); }
                catch (IOException) { break; }
                if (n <= 0) break;
                lock (gate)
                {
                    o.SetLength(0);
                    Process(buf, n, o);
                    TryFlushHeader(o, false);
                    WriteOut(o);
                }
            }
        }

        void InputLoop()
        {
            var buf = new char[4096];
            var encoder = new UTF8Encoding(false).GetEncoder();
            while (!exiting)
            {
                int n;
                if (!Native.ReadConsoleW(hIn, buf, buf.Length, out n, IntPtr.Zero)) break;
                if (n == 0) continue;
                string s = new string(buf, 0, n);
                if (IsKeyInput(s)) OnKey();
                if (Offset > 0) s = TranslateInput(s);
                char[] chars = s.ToCharArray();
                n = chars.Length;
                if (n == 0) continue;
                var bytes = new byte[encoder.GetByteCount(chars, 0, n, false)];
                int count = encoder.GetBytes(chars, 0, n, bytes, 0, false);
                try
                {
                    ptyIn.Write(bytes, 0, count);
                    ptyIn.Flush();
                }
                catch (IOException) { break; }
                catch (ObjectDisposedException) { break; }
            }
        }

        // True for real keys/paste; false for focus events, mouse and cursor reports, and
        // modifier-only or key-up events in win32-input-mode.
        static bool IsKeyInput(string s)
        {
            bool key = false;
            s = Win32Key.Replace(s, m =>
            {
                int vk, down;
                int.TryParse(m.Groups[1].Value, out vk);
                int.TryParse(m.Groups[4].Value, out down);
                if (down == 1 && vk != 16 && vk != 17 && vk != 18 && vk != 20 && vk != 91 && vk != 92) key = true;
                return "";
            });
            return key || NonKeyInput.Replace(s, "").Length > 0;
        }

        // A keypress clears a non-sticky status.
        void OnKey()
        {
            lock (gate)
            {
                Native.GetSystemTimePreciseAsFileTime(out keyStamp);
                if (status.Length == 0 || statusSticky) return;
                status = "";
                tabDirty = true;
                MarkDirty(false);
                var o = new MemoryStream();
                if (TryFlushHeader(o, true)) WriteOut(o);
            }
        }

        // The terminal reports absolute rows; the child thinks the screen starts one row lower.
        string TranslateInput(string s)
        {
            if (s.IndexOf('\x1b') < 0) return s;
            return InputReports.Replace(s, m =>
            {
                if (m.Groups[1].Success)
                {
                    int y = int.Parse(m.Groups[3].Value) - Offset;
                    if (y < 1) return ""; // click on the title bar
                    return "\x1b[<" + m.Groups[1].Value + ";" + m.Groups[2].Value + ";" + y + m.Groups[4].Value;
                }
                lock (gate)
                {
                    if (pendingCpr <= 0) return m.Value; // not a cursor report (e.g. a modified F3 key)
                    pendingCpr--;
                }
                int row = Math.Max(1, int.Parse(m.Groups[6].Value) - Offset);
                return "\x1b[" + m.Groups[5].Value + row + ";" + m.Groups[7].Value + m.Groups[8].Value + "R";
            });
        }

        void PollLoop()
        {
            while (!exiting)
            {
                Thread.Sleep(100);
                int c, r;
                if (!ReadSize(out c, out r)) continue;
                lock (gate)
                {
                    if (exiting) return;
                    bool resized = c != cols || r != rows;
                    if (resized)
                    {
                        cols = c;
                        rows = r;
                        Native.ResizePseudoConsole(hPC, new Native.COORD(cols, InnerRows));
                        childTop = childBottom = 0;
                        MarkDirty(true);
                    }
                    bool tick = statusTimer && status.Length > 0 && StatusMinutes() != drawnMinutes;
                    if (tick)
                    {
                        tabDirty = true;
                        MarkDirty(false);
                    }
                    bool stale = (headerDirty || marginsDirty || tabDirty) && (DateTime.UtcNow - dirtySince).TotalMilliseconds > 250;
                    if (resized || stale || tick)
                    {
                        var o = new MemoryStream();
                        if (TryFlushHeader(o, resized || stale)) WriteOut(o);
                    }
                }
            }
        }

        // Accepts "title <text>", "color <spec>" and "fg <spec>" lines from Set-PaneTag.
        void PipeLoop()
        {
            while (!exiting)
            {
                try
                {
                    using (var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte))
                    {
                        server.WaitForConnection();
                        var lines = new List<string>();
                        using (var reader = new StreamReader(server, new UTF8Encoding(false)))
                        {
                            string line;
                            while ((line = reader.ReadLine()) != null) lines.Add(line);
                        }
                        ApplyCommands(lines);
                    }
                }
                catch (Exception)
                {
                    Thread.Sleep(200);
                }
            }
        }

        // One client's lines, applied together. "stamp <filetime>" orders status updates.
        void ApplyCommands(List<string> lines)
        {
            long stamp = 0;
            bool sticky = false, raises = false;
            foreach (string line in lines)
            {
                string verb, arg;
                SplitCommand(line, out verb, out arg);
                if (verb == "stamp") long.TryParse(arg.Trim(), out stamp);
                else if (verb == "sticky") sticky = arg.Trim() == "1";
                else if (verb == "status") raises = Cells.Sanitize(arg).Length > 0;
            }
            lock (gate)
            {
                // Clients without a stamp (Set-PaneTag, older ptag) are applied as they come.
                bool stale = stamp != 0 && (stamp < statusStamp || (raises && !sticky && stamp < keyStamp));
                if (stamp != 0 && !stale) statusStamp = stamp;
                Native.GetSystemTimePreciseAsFileTime(out pendingSince); // a "timer" line overrides it
                bool changed = false;
                foreach (string line in lines)
                {
                    string verb, arg;
                    SplitCommand(line, out verb, out arg);
                    if (stale && (verb == "status" || verb == "statuscolor" || verb == "sticky" || verb == "timer")) continue;
                    if (ApplyCommand(verb, arg)) changed = true;
                }
                if (!changed) return;
                MarkDirty(false);
                var o = new MemoryStream();
                if (TryFlushHeader(o, true)) WriteOut(o);
            }
        }

        static void SplitCommand(string line, out string verb, out string arg)
        {
            int sp = line.IndexOf(' ');
            verb = (sp < 0 ? line : line.Substring(0, sp)).Trim().ToLowerInvariant();
            arg = sp < 0 ? "" : line.Substring(sp + 1);
        }

        // Caller holds gate. False if the line changed nothing.
        bool ApplyCommand(string verb, string arg)
        {
            Rgb c;
            switch (verb)
            {
                case "title":
                    title = Cells.Sanitize(arg);
                    if (!colorExplicit)
                    {
                        bg = Colors.ForTitle(title);
                        if (!fgExplicit) fg = Colors.Contrast(bg);
                    }
                    tabDirty = true;
                    return true;
                case "color":
                    if (!Colors.TryParse(arg, out c)) return false;
                    bg = c;
                    colorExplicit = true;
                    if (!fgExplicit) fg = Colors.Contrast(bg);
                    return true;
                case "fg":
                    if (!Colors.TryParse(arg, out c)) return false;
                    fg = c;
                    fgExplicit = true;
                    return true;
                case "status": // empty text clears it
                {
                    string s = Cells.Sanitize(arg);
                    // Raising the same flag again (another permission prompt) keeps its age.
                    if (s != status) statusSince = pendingSince;
                    else statusSince = Math.Min(statusSince, pendingSince);
                    status = s;
                    tabDirty = true;
                    return true;
                }
                case "statuscolor":
                    if (!Colors.TryParse(arg, out c)) return false;
                    statusBg = c;
                    return true;
                case "sticky":
                    statusSticky = arg.Trim() == "1";
                    return true;
                case "timer": // "timer <FILETIME the flag was raised>", or "timer 0" for none
                {
                    long since;
                    long.TryParse(arg.Trim(), out since);
                    statusTimer = since > 0;
                    if (statusTimer) pendingSince = since;
                    return true;
                }
                default:
                    return false;
            }
        }

        // ---------------------------------------------------------------- output rewriting

        void Process(byte[] buf, int n, MemoryStream o)
        {
            for (int i = 0; i < n; i++)
            {
                byte b = buf[i];
                switch (state)
                {
                    case Ground:
                        if (b == 0x1B) { BeginSeq(); break; }
                        o.WriteByte(b);
                        if (b >= 0xC0) utf8Rem = b >= 0xF0 ? 3 : b >= 0xE0 ? 2 : 1;
                        else if (b >= 0x80) { if (utf8Rem > 0) utf8Rem--; }
                        else utf8Rem = 0;
                        break;

                    case Esc:
                        if (b == 0x1B) { RawSeq(o); BeginSeq(); break; }
                        if (b < 0x20) { o.WriteByte(b); break; } // C0 controls execute mid-sequence
                        seq.WriteByte(b);
                        if (b == '[') state = Csi;
                        else if (b == ']') state = Osc;
                        else if (b == 'P' || b == 'X' || b == '^' || b == '_') { RawSeq(o); strIsOsc = false; state = Str; }
                        else if (b <= 0x2F) state = EscInter;
                        else { state = Ground; OnEsc(b, o); }
                        break;

                    case EscInter:
                        if (b == 0x1B) { RawSeq(o); BeginSeq(); break; }
                        if (b < 0x20) { o.WriteByte(b); break; }
                        seq.WriteByte(b);
                        if (b >= 0x30) { state = Ground; OnEscInter(o); }
                        break;

                    case Csi:
                        if (b == 0x1B) { RawSeq(o); BeginSeq(); break; }
                        if (b == 0x18 || b == 0x1A) { RawSeq(o); o.WriteByte(b); state = Ground; break; }
                        if (b < 0x20) { o.WriteByte(b); break; }
                        seq.WriteByte(b);
                        if (b >= 0x40 && b <= 0x7E) { state = Ground; OnCsi(o); }
                        else if (seq.Length > 512) { RawSeq(o); state = Ground; }
                        break;

                    case Osc:
                        if (b == 0x07) { seq.WriteByte(b); state = Ground; OnOsc(o, 1); }
                        else if (b == 0x1B) state = OscEsc;
                        else if (b == 0x18 || b == 0x1A) { RawSeq(o); o.WriteByte(b); state = Ground; }
                        else
                        {
                            seq.WriteByte(b);
                            if (seq.Length > MaxOsc) { RawSeq(o); strIsOsc = true; state = Str; }
                        }
                        break;

                    case OscEsc:
                        if (b == '\\') { seq.WriteByte(0x1B); seq.WriteByte(b); state = Ground; OnOsc(o, 2); }
                        else { RawSeq(o); BeginSeq(); i--; } // ESC aborted the OSC; reparse b as an escape
                        break;

                    case Str: // DCS/APC/PM/SOS (and oversized OSC) pass straight through
                        if (b == 0x1B) { state = StrEsc; break; }
                        o.WriteByte(b);
                        if ((b == 0x07 && strIsOsc) || b == 0x18 || b == 0x1A) state = Ground;
                        break;

                    case StrEsc:
                        if (b == '\\') { o.WriteByte(0x1B); o.WriteByte(b); state = Ground; }
                        else { BeginSeq(); i--; }
                        break;
                }
            }
        }

        void BeginSeq()
        {
            seq.SetLength(0);
            seq.WriteByte(0x1B);
            state = Esc;
            utf8Rem = 0;
        }

        void RawSeq(MemoryStream o)
        {
            o.Write(seq.GetBuffer(), 0, (int)seq.Length);
            seq.SetLength(0);
        }

        void OnEsc(byte b, MemoryStream o)
        {
            RawSeq(o);
            if (b == '7') childSavedCursor = true;
            else if (b == '8') childSavedCursor = false;
            else if (b == 'c')
            {
                // RIS: the terminal dropped our margins and homed the cursor onto the bar row.
                childTop = childBottom = 0;
                decom = false;
                autowrap = true;
                altScreen = false;
                childSavedCursor = false;
                pendingCpr = 0;
                Emit(o, Margins() + Cup(1, 1));
                tabDirty = true;
                MarkDirty(false);
            }
        }

        void OnEscInter(MemoryStream o)
        {
            byte[] s = seq.ToArray();
            if (s.Length == 3 && s[1] == '#' && s[2] == '8') MarkDirty(false); // DECALN fills the whole screen
            RawSeq(o);
        }

        void OnCsi(MemoryStream o)
        {
            byte[] s = seq.ToArray();
            seq.SetLength(0);
            int len = s.Length;
            char fin = (char)s[len - 1];
            int p = 2;
            char priv = '\0';
            if (p < len - 1 && s[p] >= 0x3C && s[p] <= 0x3F) priv = (char)s[p++];
            var ps = new StringBuilder();
            var im = new StringBuilder();
            for (; p < len - 1; p++)
            {
                if (s[p] >= 0x30 && s[p] <= 0x3F) ps.Append((char)s[p]);
                else im.Append((char)s[p]);
            }
            string P = ps.ToString();
            string I = im.ToString();
            int[] a;

            if (priv == '\0' && I.Length == 0)
            {
                switch (fin)
                {
                    case 'H':
                    case 'f': // CUP / HVP
                        if (!decom && TryParams(P, out a)) { Emit(o, Cup(ClampRow(Arg(a, 0, 1)), Arg(a, 1, 1))); return; }
                        break;
                    case 'd': // VPA
                        if (!decom && TryParams(P, out a)) { Emit(o, "\x1b[" + (ClampRow(Arg(a, 0, 1)) + Offset) + "d"); return; }
                        break;
                    case 'r': // DECSTBM
                        if (TryParams(P, out a))
                        {
                            int top = Arg(a, 0, 1), bottom = Math.Min(Arg(a, 1, InnerRows), InnerRows);
                            if (top >= bottom) return; // invalid; terminals ignore it
                            childTop = top == 1 ? 0 : top;
                            childBottom = bottom == InnerRows ? 0 : bottom;
                            Emit(o, Margins() + (decom ? "" : Cup(1, 1)));
                            return;
                        }
                        break;
                    case 'J': // ED may wipe the bar row
                        MarkDirty(false);
                        break;
                    case 'n':
                        if (P == "6") pendingCpr++;
                        break;
                    case 's':
                        if (P.Length == 0) childSavedCursor = true;
                        break;
                    case 'u':
                        if (P.Length == 0) childSavedCursor = false;
                        break;
                }
            }
            else if (priv == '?' && I.Length == 0)
            {
                if (fin == 'h' || fin == 'l')
                {
                    bool on = fin == 'h';
                    foreach (string mode in P.Split(';'))
                    {
                        switch (mode)
                        {
                            case "6": decom = on; break;
                            case "7": autowrap = on; break;
                            case "47":
                            case "1047":
                            case "1049":
                                altScreen = on;
                                MarkDirty(true);
                                break;
                        }
                    }
                }
                else if (fin == 'n' && P == "6") pendingCpr++;
            }
            else if (priv == '\0' && I == "!" && fin == 'p') // DECSTR soft reset drops margins
            {
                childTop = childBottom = 0;
                decom = false;
                autowrap = true;
                childSavedCursor = false;
                MarkDirty(true);
            }
            o.Write(s, 0, len);
        }

        void OnOsc(MemoryStream o, int terminatorLength)
        {
            byte[] s = seq.ToArray();
            int bodyLength = s.Length - 2 - terminatorLength;
            string body = bodyLength > 0 ? Encoding.UTF8.GetString(s, 2, bodyLength) : "";
            int semi = body.IndexOf(';');
            string ps = semi >= 0 ? body.Substring(0, semi) : body;
            if (semi >= 0 && (ps == "0" || ps == "2"))
            {
                // Window title from the child: show it in the bar and prefix the tab title with ours.
                seq.SetLength(0);
                appTitle = Cells.Sanitize(body.Substring(semi + 1));
                tabDirty = true;
                MarkDirty(false);
                return;
            }
            RawSeq(o);
        }

        static bool TryParams(string p, out int[] values)
        {
            values = null;
            foreach (char c in p) if (c != ';' && (c < '0' || c > '9')) return false;
            string[] parts = p.Split(';');
            values = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                int v;
                values[i] = parts[i].Length > 0 && int.TryParse(parts[i], out v) ? v : 0;
            }
            return true;
        }

        static int Arg(int[] a, int i, int def) { return i < a.Length && a[i] > 0 ? a[i] : def; }

        int ClampRow(int r) { return Math.Min(Math.Max(r, 1), InnerRows); }

        string Cup(int innerRow, int col) { return "\x1b[" + (innerRow + Offset) + ";" + col + "H"; }

        string Margins()
        {
            int top = childTop > 0 ? childTop : 1;
            int bottom = childBottom > 0 ? Math.Min(childBottom, InnerRows) : InnerRows;
            return "\x1b[" + (top + Offset) + ";" + (bottom + Offset) + "r";
        }

        void MarkDirty(bool margins)
        {
            if (!headerDirty && !marginsDirty && !tabDirty) dirtySince = DateTime.UtcNow;
            headerDirty = true;
            if (margins) marginsDirty = true;
        }

        // Draws the bar. Only safe between complete sequences/characters. Saving the cursor
        // with DECSC would clobber a save the child is relying on, so unless forced (resize,
        // rename, timeout) we wait until the child has restored it.
        bool TryFlushHeader(MemoryStream o, bool force)
        {
            if (!headerDirty && !marginsDirty && !tabDirty) return false;
            if (state != Ground || utf8Rem > 0) return false;
            if (childSavedCursor && !force) return false;

            var sb = new StringBuilder();
            if (tabDirty) sb.Append("\x1b]0;").Append(TabTitle()).Append('\x07');
            sb.Append(SaveCursor);
            if (marginsDirty) sb.Append(Margins());
            if (decom) sb.Append("\x1b[?6l");
            sb.Append("\x1b[?7l\x1b[").Append(HeaderRow).Append(";1H");
            AppendHeader(sb);
            sb.Append("\x1b[0m");
            if (autowrap) sb.Append("\x1b[?7h");
            sb.Append(RestoreCursor);
            Emit(o, sb.ToString());
            headerDirty = marginsDirty = tabDirty = false;
            return true;
        }

        void AppendHeader(StringBuilder sb)
        {
            // Erase-line with the bar background paints the full width regardless of how
            // the terminal measures the text; the app title is right-aligned with CHA.
            bool flagged = status.Length > 0;
            Rgb barBg = flagged ? statusBg : bg;
            Rgb barFg = flagged ? Colors.Contrast(statusBg) : fg;
            sb.Append("\x1b[0;1;").Append(barFg.Sgr(38)).Append(';').Append(barBg.Sgr(48)).Append("m\x1b[2K");
            drawnMinutes = StatusMinutes();
            string left = Cells.Fit(" " + title + (flagged ? "  ● " + StatusLabel() : "") + " ", cols);
            sb.Append(left);
            string right = ShortAppTitle();
            int room = cols - Cells.Width(left) - 2;
            if (right.Length > 0 && room >= 6)
            {
                right = Cells.Fit(right, room - 1) + " ";
                sb.Append("\x1b[22m\x1b[").Append(cols - Cells.Width(right) + 1).Append('G').Append(right);
            }
        }

        string ShortAppTitle()
        {
            string t = appTitle;
            if (t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && (t.IndexOf('\\') >= 0 || t.IndexOf('/') >= 0))
            {
                try { t = Path.GetFileNameWithoutExtension(t); } catch (ArgumentException) { }
            }
            return string.Equals(t, title, StringComparison.OrdinalIgnoreCase) ? "" : t;
        }

        string TabTitle()
        {
            string app = ShortAppTitle();
            string t = status.Length > 0 ? title + " ● " + StatusLabel() : title;
            return app.Length > 0 ? t + " · " + app : t;
        }

        int StatusMinutes()
        {
            long now;
            Native.GetSystemTimePreciseAsFileTime(out now);
            return (int)Math.Max(0, (now - statusSince) / TimeSpan.TicksPerMinute);
        }

        // "Needs you · 4m", "Needs you · 1h 05m"; nothing added in the first minute.
        string StatusLabel()
        {
            int m = StatusMinutes();
            if (!statusTimer || m < 1) return status;
            return status + " · " + (m < 60 ? m + "m" : (m / 60) + "h " + (m % 60).ToString("00") + "m");
        }

        static void Emit(MemoryStream o, string s)
        {
            byte[] b = Encoding.UTF8.GetBytes(s);
            o.Write(b, 0, b.Length);
        }

        void WriteOut(MemoryStream o)
        {
            byte[] b = o.GetBuffer();
            int offset = 0, length = (int)o.Length;
            while (offset < length)
            {
                byte[] chunk = b;
                if (offset > 0)
                {
                    chunk = new byte[length - offset];
                    Buffer.BlockCopy(b, offset, chunk, 0, chunk.Length);
                }
                int written;
                if (!Native.WriteFile(hOut, chunk, length - offset, out written, IntPtr.Zero) || written <= 0) return;
                offset += written;
            }
        }
    }

    internal static class Program
    {
        const string Usage =
@"panetag - keeps a title bar on a terminal pane, even under full-screen apps.

Usage: panetag [TITLE...] [options] [-- command [args...]]
       (also installed as ptag)

In a pane without a bar, starts one and runs the command under it (default:
powershell.exe -NoLogo). In a pane that has a bar, changes that bar instead:

  ptag Reviewing PR          rename
  ptag Reviewing PR -c red   rename and recolor
  ptag -c green              recolor
  ptag --status Your turn    flag the bar (orange) until the next keypress in the pane
  ptag --clear-status        remove the flag

Inside a titled pane, ptag is on the PATH, so this works from any shell there,
including Claude Code's ! commands:  ! ptag Reviewing PR
--status and --clear-status do nothing outside a titled pane, so they're safe in
hooks (see Enable-PaneTagClaudeHooks in the PaneTag module).

  TITLE...                Title; quotes are optional (default: folder name)
  -t, --title TEXT        Title, as an option
  -c, --color COLOR       Bar background: {0} or #RRGGBB
                          (default: picked from the title)
      --fg COLOR          Bar text color (default: black or white for contrast)
  -s, --status TEXT       Flag the bar with a status label; words are appended
      --status-color C    Status color (default: orange)
      --sticky            Keep the status until --clear-status (default: it
                          clears on the next keypress in the pane)
      --timer             Show how long the status has been up (· 4m) after
                          the first minute
      --clear-status      Remove the status
  -p, --position POS     top (default) or bottom. Use bottom to keep native
                          scrollback: Windows Terminal drops lines that scroll
                          out of a region that doesn't start at the top row.
      --cwd DIR           Working directory for the command
      --cmd CMDLINE       Full command line as one string
  -h, --help              Show this help

-p, --cwd, --cmd and a command only apply when starting a bar. Defaults can also
come from the PANETAG_POSITION and PANETAG_COLOR environment variables.";

        static int Main(string[] args)
        {
            try
            {
                var opt = new Options();
                var command = new List<string>();
                var words = new List<string>();
                string position = Environment.GetEnvironmentVariable("PANETAG_POSITION");
                opt.Color = Environment.GetEnvironmentVariable("PANETAG_COLOR");
                string colorArg = null; // -c as typed, so updates ignore PANETAG_COLOR
                bool startOnly = false; // an option that only makes sense when starting a bar
                string statusText = null, statusColor = null;
                bool sticky = false, timer = false, clearStatus = false;

                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i];
                    if (a == "--") { command.AddRange(Rest(args, i + 1)); break; }
                    switch (a)
                    {
                        case "-t": case "--title": opt.Title = Next(args, ref i); break;
                        case "--title-b64": opt.Title = FromB64(Next(args, ref i)); break;
                        case "-c": case "--color": opt.Color = colorArg = Next(args, ref i); break;
                        case "--fg": opt.Fg = Next(args, ref i); break;
                        case "-s": case "--status": statusText = Next(args, ref i); break;
                        case "--status-color": statusColor = Next(args, ref i); break;
                        case "--sticky": sticky = true; break;
                        case "--timer": timer = true; break;
                        case "--clear-status": clearStatus = true; break;
                        case "-p": case "--position": position = Next(args, ref i); startOnly = true; break;
                        case "--cwd": opt.Cwd = Next(args, ref i); startOnly = true; break;
                        case "--cwd-b64": opt.Cwd = FromB64(Next(args, ref i)); startOnly = true; break;
                        case "--cmd": opt.Command = Next(args, ref i); startOnly = true; break;
                        case "--cmd-b64": opt.Command = FromB64(Next(args, ref i)); startOnly = true; break;
                        case "-h": case "--help": case "/?":
                            Console.WriteLine(Usage, Colors.NameList);
                            return 0;
                        default:
                            if (a.StartsWith("-")) throw new ArgumentException("unknown option " + a + " (put the command to run after --)");
                            words.Add(a);
                            break;
                    }
                }
                if (words.Count > 0)
                {
                    string joined = string.Join(" ", words.ToArray());
                    if (statusText != null) statusText += " " + joined; // ptag --status Your turn
                    else if (opt.Title != null) throw new ArgumentException("give the title either as words or with -t, not both");
                    else opt.Title = joined;
                }
                bool statusMode = statusText != null || clearStatus || statusColor != null || sticky || timer;

                var lines = new List<string>();
                if (opt.Title != null) lines.Add("title " + OneLine(opt.Title));
                if (colorArg != null) lines.Add("color " + CheckColor(colorArg));
                if (opt.Fg != null) lines.Add("fg " + CheckColor(opt.Fg));
                if (statusText != null)
                {
                    lines.Add("statuscolor " + CheckColor(statusColor ?? "orange"));
                    lines.Add("sticky " + (sticky ? "1" : "0"));
                    lines.Add("timer " + (timer ? StartStamp() : 0));
                    lines.Add("status " + OneLine(statusText));
                }
                else if (clearStatus) lines.Add("status ");
                else if (statusColor != null) CheckColor(statusColor);
                if (statusText != null || clearStatus) lines.Insert(0, "stamp " + StartStamp());

                string pipe = Environment.GetEnvironmentVariable("PANETAG_PIPE");
                if (string.IsNullOrEmpty(pipe))
                {
                    // Status updates come from hooks that also run outside titled panes: no-op.
                    if (statusMode) return 0;
                }
                else
                {
                    // Inside a titled pane, change its bar rather than nesting a second one.
                    if (startOnly || command.Count > 0)
                        throw new InvalidOperationException("this pane already has a bar; -p, --cwd and a command only apply when starting one (type exit first)");
                    if (lines.Count == 0) throw new ArgumentException("this pane already has a bar; give a new title, -c, --fg or --status to change it");
                    return SendUpdate(pipe, lines, statusMode);
                }

                if (!string.IsNullOrEmpty(position))
                {
                    if (position.Equals("top", StringComparison.OrdinalIgnoreCase)) opt.Top = true;
                    else if (position.Equals("bottom", StringComparison.OrdinalIgnoreCase)) opt.Top = false;
                    else throw new ArgumentException("position must be 'top' or 'bottom'");
                }
                if (command.Count > 0) opt.Command = string.Join(" ", command.ConvertAll(Quote).ToArray());
                if (string.IsNullOrEmpty(opt.Command)) opt.Command = "powershell.exe -NoLogo";
                if (string.IsNullOrEmpty(opt.Title))
                {
                    string dir = string.IsNullOrEmpty(opt.Cwd) ? Environment.CurrentDirectory : opt.Cwd;
                    opt.Title = Path.GetFileName(dir.TrimEnd('\\', '/'));
                    if (string.IsNullOrEmpty(opt.Title)) opt.Title = dir;
                }
                if (opt.Color != null)
                {
                    Rgb unused;
                    if (!Colors.TryParse(opt.Color, out unused)) throw new ArgumentException("unknown color '" + opt.Color + "' (use " + Colors.NameList + " or #RRGGBB)");
                }

                return new Wrapper(opt).Run();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("panetag: " + ex.Message);
                return 1;
            }
        }

        static string OneLine(string s) { return s.Replace('\r', ' ').Replace('\n', ' '); }

        // When this process was created, which follows the order Claude Code fired the hooks
        // in; the time Main is reached doesn't (runtime startup varies).
        static long StartStamp()
        {
            long creation, exit, kernel, user;
            if (Native.GetProcessTimes(Native.GetCurrentProcess(), out creation, out exit, out kernel, out user)) return creation;
            Native.GetSystemTimePreciseAsFileTime(out creation);
            return creation;
        }

        static string CheckColor(string spec)
        {
            Rgb unused;
            if (!Colors.TryParse(spec, out unused)) throw new ArgumentException("unknown color '" + spec + "' (use " + Colors.NameList + " or #RRGGBB)");
            return spec;
        }

        // Client side of the rename pipe: sends the changes to the bar of the pane we're in.
        // Quiet mode (status updates from hooks) never fails loudly.
        static int SendUpdate(string pipe, List<string> lines, bool quiet)
        {
            using (var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out))
            {
                try { client.Connect(2000); }
                catch (TimeoutException)
                {
                    if (quiet) return 0;
                    throw new InvalidOperationException("cannot reach this pane's bar (" + pipe + "). If the bar is gone, clear PANETAG_PIPE and start a new one.");
                }
                var writer = new StreamWriter(client, new UTF8Encoding(false));
                foreach (string line in lines) writer.WriteLine(line);
                writer.Flush();
            }
            return 0;
        }

        static string Next(string[] args, ref int i)
        {
            if (i + 1 >= args.Length) throw new ArgumentException(args[i] + " needs a value");
            return args[++i];
        }

        static IEnumerable<string> Rest(string[] args, int from)
        {
            for (int i = from; i < args.Length; i++) yield return args[i];
        }

        static string FromB64(string s) { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); }

        // Standard CommandLineToArgvW-compatible quoting.
        static string Quote(string a)
        {
            if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return a;
            var sb = new StringBuilder("\"");
            for (int i = 0; i < a.Length; i++)
            {
                int slashes = 0;
                while (i < a.Length && a[i] == '\\') { slashes++; i++; }
                if (i == a.Length) { sb.Append('\\', slashes * 2); break; }
                if (a[i] == '"') sb.Append('\\', slashes * 2 + 1).Append('"');
                else sb.Append('\\', slashes).Append(a[i]);
            }
            return sb.Append('"').ToString();
        }
    }
}
