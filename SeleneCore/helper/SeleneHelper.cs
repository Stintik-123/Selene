// Selene Helper: a small program with admin rights beside Selene Core. A game that runs as administrator is out of a normal
// program's reach: Playnite can't take the screen back from it (Home while playing) or close it (Close game). Selene Core
// registers this program once as the scheduled task Selene\Helper (one Windows prompt, after such a game's first run) and
// starts that task when it needs it; the task runs this with the game's rights.
//
// Requests are lines in %LOCALAPPDATA%\Selene\elevated.txt, written by Selene Core just before the task starts:
//   focus <hwnd>         bring that window (Playnite's) to the front
//   close <pid>          close that process (its window first, ended if it is still there after 4 s)
//   freeze <pid> <hwnd>  Home while playing: the game's window minimized (so Windows doesn't ghost it), then the game suspended
//   thaw <pid> <hwnd>    back to the game: resumed, its window restored and given the screen
// While a game is frozen the helper stays running and looks at the request file every 20 ms, so going back is instant;
// it thaws everything when Playnite is gone (closed, crashed) or after 30 minutes without a word.
//
// Command line:   (none)                                        handle the request file, then exit
//                 install <task> <xml> [<task> <xml> ...]       register scheduled tasks from XML files (run elevated by
//                                                               Selene Core: the helper's own task and a game's no-prompt
//                                                               launcher in one prompt)
// Built by tools\build.ps1 (csc /target:winexe: no console window), shipped inside SeleneCore.pext. C# 5, .NET Framework 4.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Selene
{
    public static class SeleneHelper
    {
        private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Selene");
        private static readonly string RequestFile = Path.Combine(Folder, "elevated.txt");
        private static readonly string LogFile = Path.Combine(Folder, "helper.log");

        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0] == "install") return Install(args);
                HandleRequests();
                return 0;
            }
            catch (Exception ex) { Log("failed: " + ex.Message); return 1; }
        }

        // ---- install: scheduled tasks from XML files (this process already has admin rights) ----
        private static int Install(string[] args)
        {
            int failed = 0;
            var schtasks = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
            for (int i = 1; i + 1 < args.Length; i += 2)
            {
                var name = args[i];
                var xml = args[i + 1];
                var psi = new ProcessStartInfo(schtasks, "/create /tn \"" + name + "\" /xml \"" + xml + "\" /f") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi))
                {
                    var err = p.StandardError.ReadToEnd();
                    p.WaitForExit(30000);
                    if (p.ExitCode != 0) failed++;
                    Log("install " + name + ": " + (p.ExitCode == 0 ? "done" : "failed (" + err.Trim() + ")"));
                }
            }
            return failed == 0 ? 0 : 2;
        }

        // ---- requests ----
        // The task is set to ignore a second start while one runs, so after the file is handled this keeps looking for
        // a request that arrived meanwhile, for a short while.
        private static readonly Dictionary<int, IntPtr> frozen = new Dictionary<int, IntPtr>();

        private static void HandleRequests()
        {
            var idleSince = Environment.TickCount;
            int lastPlayniteCheck = 0;
            while (true)
            {
                var lines = TakeRequests();
                if (lines.Count > 0) { Handle(lines); idleSince = Environment.TickCount; }
                int idle = Environment.TickCount - idleSince;
                if (frozen.Count == 0) { if (idle >= 1200) break; Thread.Sleep(100); continue; }
                // a game is frozen: never left that way
                if (Environment.TickCount - lastPlayniteCheck > 2000)
                {
                    lastPlayniteCheck = Environment.TickCount;
                    if (Process.GetProcessesByName("Playnite.FullscreenApp").Length == 0 && Process.GetProcessesByName("Playnite.DesktopApp").Length == 0)
                    { Log("Playnite is gone: thawing " + frozen.Count + " game(s)"); ThawAll(); break; }
                }
                if (idle > 30 * 60 * 1000) { Log("30 minutes without a request: thawing"); ThawAll(); break; }
                Thread.Sleep(20);
            }
        }

        private static void ThawAll()
        {
            foreach (var kv in new List<KeyValuePair<int, IntPtr>>(frozen)) Thaw(kv.Key, kv.Value, false);
        }

        private static void Freeze(int pid, IntPtr hwnd)
        {
            if (hwnd != IntPtr.Zero && IsWindow(hwnd))
            {
                ShowWindow(hwnd, 7);   // SW_SHOWMINNOACTIVE
                for (int i = 0; i < 25 && !IsIconic(hwnd); i++) Thread.Sleep(20);
            }
            var h = OpenProcess(0x0800 /* PROCESS_SUSPEND_RESUME */, false, pid);
            if (h == IntPtr.Zero) { Log("freeze " + pid + ": can't open the process"); return; }
            int r;
            try { r = NtSuspendProcess(h); } finally { CloseHandle(h); }
            if (r == 0) frozen[pid] = hwnd;
            Log("freeze " + pid + ": " + (r == 0 ? "frozen" : "failed (" + r + ")") + (hwnd != IntPtr.Zero ? ", minimized=" + IsIconic(hwnd) : ""));
        }

        private static void Thaw(int pid, IntPtr hwnd, bool focus)
        {
            var h = OpenProcess(0x0800, false, pid);
            int r = -1;
            if (h != IntPtr.Zero) { try { r = NtResumeProcess(h); } finally { CloseHandle(h); } }
            frozen.Remove(pid);
            if (hwnd != IntPtr.Zero && IsWindow(hwnd))
            {
                if (IsIconic(hwnd)) ShowWindowAsync(hwnd, 9);   // SW_RESTORE, async: never wait on a window still waking up
                if (focus) ForceForeground(hwnd);
            }
            Log("thaw " + pid + ": " + (r == 0 ? "resumed" : "resume failed (" + r + ")") + (focus && hwnd != IntPtr.Zero ? ", game in front=" + (GetForegroundWindow() == hwnd) : ""));
        }

        private static List<string> TakeRequests()
        {
            var lines = new List<string>();
            try
            {
                if (!File.Exists(RequestFile)) return lines;
                foreach (var l in File.ReadAllLines(RequestFile)) if (!string.IsNullOrWhiteSpace(l)) lines.Add(l.Trim());
                File.Delete(RequestFile);
            }
            catch (Exception ex) { Log("request file: " + ex.Message); }
            return lines;
        }

        private static void Handle(List<string> lines)
        {
            var close = new List<Process>();
            foreach (var l in lines)
            {
                if (l.StartsWith("focus "))
                {
                    long h;
                    if (!long.TryParse(l.Substring(6).Trim(), out h)) continue;
                    var hwnd = new IntPtr(h);
                    // only the exact window Selene named, and only brought to the front (never shown or resized: that put a
                    // game into a broken windowed state)
                    if (!IsWindow(hwnd)) { Log("focus " + h + ": no such window"); continue; }
                    ForceForeground(hwnd);
                    Log("focus " + h + ": " + (GetForegroundWindow() == hwnd ? "done" : "refused"));
                }
                else if (l.StartsWith("close "))
                {
                    int pid;
                    if (!int.TryParse(l.Substring(6).Trim(), out pid)) continue;
                    try
                    {
                        var pr = Process.GetProcessById(pid);
                        close.Add(pr);
                        if (pr.MainWindowHandle != IntPtr.Zero) pr.CloseMainWindow();
                    }
                    catch (Exception ex) { Log("close " + pid + ": " + ex.Message); }
                }
                else if (l.StartsWith("freeze ") || l.StartsWith("thaw "))
                {
                    var parts = l.Split(' ');
                    int pid; long hw = 0;
                    if (parts.Length < 2 || !int.TryParse(parts[1], out pid)) continue;
                    if (parts.Length >= 3) long.TryParse(parts[2], out hw);
                    if (parts[0] == "freeze") Freeze(pid, new IntPtr(hw)); else Thaw(pid, new IntPtr(hw), true);
                }
                else Log("unknown request: " + l);
            }
            if (close.Count == 0) return;
            var until = Environment.TickCount + 4000;
            while (Environment.TickCount < until && close.Exists(pr => { try { return !pr.HasExited; } catch { return false; } })) Thread.Sleep(200);
            int ended = 0;
            foreach (var pr in close) try { if (!pr.HasExited) { pr.Kill(); ended++; } } catch { }
            Log("close: " + close.Count + " process(es)" + (ended > 0 ? ", " + ended + " ended" : ""));
        }

        // A background program may not take the foreground; a tapped Alt key counts as user input and lifts that lock,
        // and joining the current foreground thread's input queue is the fallback.
        private static void ForceForeground(IntPtr hwnd)
        {
            SendVk(0x12, true); SendVk(0x12, false);   // Alt
            if (SetForegroundWindow(hwnd) && GetForegroundWindow() == hwnd) return;
            uint fgPid;
            uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out fgPid), me = GetCurrentThreadId();
            bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
            if (attached) AttachThreadInput(me, fgThread, false);
        }

        private static void Log(string s)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(LogFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + s + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        // ---- Windows ----
        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT { public uint type; public KEYBDINPUT ki; private readonly long pad1; private readonly long pad2; }   // the union's largest member is MOUSEINPUT (28/32 bytes): padded to match

        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from, uint to, bool attach);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("ntdll.dll")] private static extern int NtSuspendProcess(IntPtr h);
        [DllImport("ntdll.dll")] private static extern int NtResumeProcess(IntPtr h);

        private static void SendVk(ushort vk, bool down)
        {
            var k = new INPUT[1];
            k[0].type = 1;   // INPUT_KEYBOARD
            k[0].ki = new KEYBDINPUT { wVk = vk, dwFlags = down ? 0u : 0x0002u /* KEYEVENTF_KEYUP */ };
            SendInput(1, k, Marshal.SizeOf(typeof(INPUT)));
        }
    }
}
