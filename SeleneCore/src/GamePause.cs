// Guide = pause the game like a console: its audio session is muted (Windows per-app volume) and, unless it is an
// online game, the process is suspended (NtSuspendProcess) until the user goes back to it.
// Used by HomeNav.cs (GoHomeFromGuide / ReturnToGame / RestartGame / application exit).
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Selene
{
    public static class GamePause
    {
        // online games are only muted: freezing them drops the connection (and can upset anti-cheat)
        private static readonly string[] OnlineGames = {
            "path of exile", "marvel rivals", "arc raiders", "apex", "battlefield", "the finals", "finals", "nba 2k", "overwatch",
            "destiny", "genshin", "honkai", "counter-strike", "rainbow six", "valorant", "fortnite", "warframe", "dota", "league of legends",
            "call of duty", "naraka", "delta force", "wuthering", "zenless", "rocket league", "pubg", "dead by daylight", "deadlock",
            "lost ark", "new world", "final fantasy xiv", "world of warcraft", "elden ring nightreign", "helldivers", "the division", "xdefiant" };

        private static int pausedPid;
        private static bool suspended, lowered;

        public static bool IsOnline(string gameName)
        {
            var n = (gameName ?? "").ToLowerInvariant();
            foreach (var o in OnlineGames) if (n.Contains(o)) return true;
            return false;
        }

        public static void Pause(int pid, string gameName) { Pause(pid, gameName, true); }

        // allowFreeze=false: mute only (the game window could not be minimised first)
        public static void Pause(int pid, string gameName, bool allowFreeze)
        {
            if (pid <= 0) return;
            Resume();  // never leave an older one frozen
            pausedPid = pid;
            SetProcessMute(pid, true);
            if (allowFreeze && !IsOnline(gameName)) suspended = Suspend(pid, true);
            // not frozen (God of War un-minimises itself, online games): it kept rendering 4K at full speed behind the
            // home and Playnite stuttered -> lowest CPU and GPU priority while Playnite is in front
            if (!suspended) lowered = SetPriority(pid, false);
            HomeNav.Log("pause: " + gameName + " (pid " + pid + ") muted" + (suspended ? ", frozen" : IsOnline(gameName) ? ", online: not frozen" : !allowFreeze ? ", not minimised: not frozen" : ", freeze failed"));
        }

        public static void Resume()
        {
            if (pausedPid == 0) return;
            int pid = pausedPid;
            pausedPid = 0;
            if (suspended) { Suspend(pid, false); suspended = false; }
            if (lowered) { SetPriority(pid, true); lowered = false; }
            SetProcessMute(pid, false);
            HomeNav.Log("resume: pid " + pid);
        }

        // ---- priority (CPU + GPU scheduling) ----
        [DllImport("gdi32.dll")] private static extern int D3DKMTSetProcessSchedulingPriorityClass(IntPtr process, int priority);  // 0 idle .. 2 normal .. 4 high

        private static bool SetPriority(int pid, bool normal)
        {
            bool cpu = false, gpu = false;
            try { System.Diagnostics.Process.GetProcessById(pid).PriorityClass = normal ? System.Diagnostics.ProcessPriorityClass.Normal : System.Diagnostics.ProcessPriorityClass.Idle; cpu = true; } catch { }
            var h = OpenProcess(0x0200 | 0x1000, false, pid);  // PROCESS_SET_INFORMATION | PROCESS_QUERY_LIMITED_INFORMATION
            if (h != IntPtr.Zero) { try { gpu = D3DKMTSetProcessSchedulingPriorityClass(h, normal ? 2 : 0) == 0; } catch { } finally { CloseHandle(h); } }
            HomeNav.Log("game priority " + (normal ? "normal" : "lowest") + " (pid " + pid + ", cpu=" + cpu + " gpu=" + gpu + ")");
            return cpu || gpu;
        }

        // ---- suspend / resume ----
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("ntdll.dll")] private static extern int NtSuspendProcess(IntPtr h);
        [DllImport("ntdll.dll")] private static extern int NtResumeProcess(IntPtr h);

        private static bool Suspend(int pid, bool suspend)
        {
            var h = OpenProcess(0x0800, false, pid);  // PROCESS_SUSPEND_RESUME
            if (h == IntPtr.Zero) return false;
            try { return (suspend ? NtSuspendProcess(h) : NtResumeProcess(h)) == 0; }
            finally { CloseHandle(h); }
        }

        // ---- per-process mute (Core Audio sessions on the default render device) ----
        private static void SetProcessMute(int pid, bool mute)
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                IMMDevice device;
                if (enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out device) != 0) return;
                var iid = typeof(IAudioSessionManager2).GUID;
                object o;
                if (device.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out o) != 0) return;
                var manager = (IAudioSessionManager2)o;
                IAudioSessionEnumerator sessions;
                if (manager.GetSessionEnumerator(out sessions) != 0) return;
                int count;
                sessions.GetCount(out count);
                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl ctl;
                    if (sessions.GetSession(i, out ctl) != 0) continue;
                    var ctl2 = ctl as IAudioSessionControl2;
                    if (ctl2 == null) continue;
                    uint spid;
                    if (ctl2.GetProcessId(out spid) != 0 || spid != (uint)pid) continue;
                    var vol = ctl as ISimpleAudioVolume;
                    if (vol != null) { var ctx = Guid.Empty; vol.SetMute(mute, ref ctx); }
                }
            }
            catch (Exception ex) { HomeNav.Log("mute failed: " + ex.Message); }
        }

        // Loudest audio peak (0..1) of these processes right now on the default output: "the game is making sound"
        // (the launch loading screen uses it to tell when a game is really showing something, e.g. its intro logos)
        public static float PeakOf(ICollection<int> pids)
        {
            float max = 0;
            if (pids == null || pids.Count == 0) return 0;
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                IMMDevice device;
                if (enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out device) != 0) return 0;
                var iid = typeof(IAudioSessionManager2).GUID;
                object o;
                if (device.Activate(ref iid, 1, IntPtr.Zero, out o) != 0) return 0;
                IAudioSessionEnumerator sessions;
                if (((IAudioSessionManager2)o).GetSessionEnumerator(out sessions) != 0) return 0;
                int count;
                sessions.GetCount(out count);
                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl ctl;
                    if (sessions.GetSession(i, out ctl) != 0) continue;
                    var ctl2 = ctl as IAudioSessionControl2;
                    uint spid;
                    if (ctl2 == null || ctl2.GetProcessId(out spid) != 0 || !pids.Contains((int)spid)) continue;
                    var meter = ctl as IAudioMeterInformation;
                    float peak;
                    if (meter != null && meter.GetPeakValue(out peak) == 0 && peak > max) max = peak;
                }
            }
            catch { }
            return max;
        }

        [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioMeterInformation
        {
            int GetPeakValue(out float peak);
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumerator { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionManager2
        {
            int GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr control);
            int GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out IntPtr volume);
            int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionEnumerator
        {
            int GetCount(out int count);
            int GetSession(int index, out IAudioSessionControl session);
        }

        [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl
        {
            int GetState(out int state);
            int GetDisplayName(out IntPtr name);
            int SetDisplayName(IntPtr name, ref Guid ctx);
            int GetIconPath(out IntPtr path);
            int SetIconPath(IntPtr path, ref Guid ctx);
            int GetGroupingParam(out Guid param);
            int SetGroupingParam(ref Guid param, ref Guid ctx);
            int RegisterAudioSessionNotification(IntPtr client);
            int UnregisterAudioSessionNotification(IntPtr client);
        }

        [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl2
        {
            // IAudioSessionControl
            int GetState(out int state);
            int GetDisplayName(out IntPtr name);
            int SetDisplayName(IntPtr name, ref Guid ctx);
            int GetIconPath(out IntPtr path);
            int SetIconPath(IntPtr path, ref Guid ctx);
            int GetGroupingParam(out Guid param);
            int SetGroupingParam(ref Guid param, ref Guid ctx);
            int RegisterAudioSessionNotification(IntPtr client);
            int UnregisterAudioSessionNotification(IntPtr client);
            // IAudioSessionControl2
            int GetSessionIdentifier(out IntPtr id);
            int GetSessionInstanceIdentifier(out IntPtr id);
            int GetProcessId(out uint pid);
            int IsSystemSoundsSession();
            int SetDuckingPreference(bool optOut);
        }

        [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISimpleAudioVolume
        {
            int SetMasterVolume(float level, ref Guid ctx);
            int GetMasterVolume(out float level);
            int SetMute(bool mute, ref Guid ctx);
            int GetMute(out bool mute);
        }
    }

    // Playnite always smooth:
    //  - no Windows power throttling (EcoQoS) for Playnite, a little GPU priority over background work
    //  - frame watchdog: logs when the home animates below 45 fps (with the cause candidates), and restarts Playnite when
    //    WPF has fallen back to software rendering (happens after sleep / display switches; it never recovers by itself)
    public static class Smoothness
    {
        [StructLayout(LayoutKind.Sequential)] private struct PowerThrottling { public uint Version, ControlMask, StateMask; }
        [DllImport("kernel32.dll")] private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PowerThrottling info, int size);
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("gdi32.dll")] private static extern int D3DKMTSetProcessSchedulingPriorityClass(IntPtr process, int priority);

        private static double lastFrame, sum;
        private static int frames, slowWindows;
        private static System.Windows.Threading.DispatcherTimer tierTimer;
        private static int softwareSeconds;
        private static bool restarting;

        public static void Start()
        {
            try
            {
                var pt = new PowerThrottling { Version = 1, ControlMask = 1 | 4, StateMask = 0 };  // execution speed + timer resolution: never throttled
                SetProcessInformation(GetCurrentProcess(), 4 /* ProcessPowerThrottling */, ref pt, Marshal.SizeOf(typeof(PowerThrottling)));
                D3DKMTSetProcessSchedulingPriorityClass(GetCurrentProcess(), 3);  // above normal
            }
            catch (Exception ex) { HomeNav.Log("smooth: setup failed " + ex.Message); }
            HomeNav.Log("smooth: render tier " + Tier());
            System.Windows.Media.RenderCapability.TierChanged += (s, e) => HomeNav.Log("smooth: render tier changed -> " + Tier());
            System.Windows.Media.CompositionTarget.Rendering += OnFrame;
            tierTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            tierTimer.Tick += (s, e) => CheckSoftware();
            tierTimer.Start();
        }

        private static int Tier() { return System.Windows.Media.RenderCapability.Tier >> 16; }  // 2 = full GPU, 0 = software

        private static bool GameRunning()
        {
            var api = HomeNav.Api;
            if (api != null) foreach (var g in api.Database.Games) if (g.IsRunning) return true;
            return false;
        }

        private static bool PlayniteOnScreen()
        {
            var w = System.Windows.Application.Current != null ? System.Windows.Application.Current.MainWindow : null;
            return w != null && w.IsVisible && w.WindowState != System.Windows.WindowState.Minimized && !CloseToTray.IsHidden;
        }

        private static void OnFrame(object sender, EventArgs e)
        {
            var re = e as System.Windows.Media.RenderingEventArgs;
            if (re == null) return;
            double t = re.RenderingTime.TotalMilliseconds;
            if (t == lastFrame) return;  // same frame reported twice
            double d = t - lastFrame;
            lastFrame = t;
            if (d > 250 || d <= 0) { frames = 0; sum = 0; return; }  // nothing was animating for a while
            frames++; sum += d;
            if (sum < 4000) return;
            double fps = frames * 1000.0 / sum;
            frames = 0; sum = 0;
            if (fps >= 45 || !PlayniteOnScreen()) { slowWindows = 0; return; }
            if (++slowWindows > 3) return;  // reported enough for this slow spell
            string game = "none";
            var api = HomeNav.Api;
            if (api != null) foreach (var g in api.Database.Games) if (g.IsRunning) { game = g.Name; break; }
            double cpu = 0;
            try { var me = System.Diagnostics.Process.GetCurrentProcess(); cpu = me.TotalProcessorTime.TotalSeconds; } catch { }
            HomeNav.Log("smooth: SLOW " + fps.ToString("0") + " fps on the " + (HomeNav.FindFocusedName() ?? "?") + " | render tier " + Tier()
                + " | game running: " + game + " | screen " + System.Windows.SystemParameters.PrimaryScreenWidth + " | playnite cpu " + cpu.ToString("0") + " s");
        }

        private static void CheckSoftware()
        {
            if (restarting) return;
            if (Tier() > 0 || !PlayniteOnScreen() || GameRunning()) { softwareSeconds = 0; return; }
            if (++softwareSeconds < 8) return;
            restarting = true;
            HomeNav.Log("smooth: Playnite is drawing without the GPU (software rendering) -> restarting it");
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SelenePaths.Helper, "restart-playnite") { UseShellExecute = false }); }
            catch (Exception ex) { HomeNav.Log("smooth: restart failed " + ex.Message); restarting = false; }
        }
    }
}
