using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace Selene
{
    // The controller's battery, as exactly as Windows lets it be read. Sources, best first:
    //   1. the pad's own HID report: DualSense / DualShock 4 (USB and Bluetooth), Switch Pro (Bluetooth): a percentage or fine level
    //   2. Windows' Bluetooth battery property, the figure Settings > Bluetooth shows: Xbox pads over Bluetooth, 8BitDo in X-input
    //      mode over Bluetooth, anything else that publishes one
    //   3. Windows.Gaming.Input's battery report: Xbox pads on the wireless adapter or a cable (capacities, a percentage from them)
    //   4. XInput's four buckets (empty / low / medium / full), the least
    // A pad on a 2.4 GHz dongle shows Windows a wired pad and keeps its charge to the maker's own protocol: nothing to read.
    // All of it fails soft; run off the UI thread (device enumeration takes a moment).
    public static class PadPower
    {
        public class Reading
        {
            public string Name, Source;
            public int Percent = -1;   // exact, when a source gives one
            public int Level = -1;     // 0 empty, 1 low, 2 medium, 3 full (from Percent when that's known)
            public bool Charging, Wired, Present;
            public int Bars { get { return Level >= 3 ? 3 : Level == 2 ? 2 : Level == 1 ? 1 : 0; } }
            public bool Low { get { return Percent >= 0 ? Percent <= 20 : Level >= 0 && Level <= 1; } }
            public override string ToString() { return (Present ? (Name ?? "pad") + " " + (Percent >= 0 ? Percent + "%" : "level " + Level) + (Charging ? " charging" : "") + (Wired ? " wired" : "") + " (" + Source + ")" : "none"); }
        }

        private static readonly DateTime start = DateTime.Now;

        public static Reading Best()
        {
            // %TEMP%\selene-dev-battery: a wireless pad played, 90% for the first 45 s and 15% after (the cards, seen on any PC)
            try
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "selene-dev-battery")))
                {
                    int pct = (DateTime.Now - start).TotalSeconds < 45 ? 90 : 15;
                    return new Reading { Present = true, Name = "Played pad", Percent = pct, Level = LevelOf(pct), Source = "dev" };
                }
            }
            catch { }
            Reading r = null;
            try { r = Hid(); } catch (Exception ex) { HomeNav.Log("battery: hid: " + ex.Message); }
            if (r == null) { try { r = Bluetooth(); } catch (Exception ex) { HomeNav.Log("battery: bluetooth: " + ex.Message); } }
            if (r == null) { try { r = Gaming(); } catch (Exception ex) { HomeNav.Log("battery: gaming input: " + ex.Message); } }
            if (r == null) { try { r = XInput(); } catch (Exception ex) { HomeNav.Log("battery: xinput: " + ex.Message); } }
            return r ?? new Reading { Present = false, Source = "none" };
        }

        private static int LevelOf(int percent) { return percent > 66 ? 3 : percent > 33 ? 2 : percent > 10 ? 1 : 0; }

        // ---------------- 1. the pad's own HID report ----------------
        private const ushort Sony = 0x054C, Nintendo = 0x057E;

        private static Reading Hid()
        {
            Guid g; HidD_GetHidGuid(out g);
            var set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return null;
            try
            {
                var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA)) };
                for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref data); i++)
                {
                    var detail = new SP_DEVICE_INTERFACE_DETAIL_DATA { cbSize = IntPtr.Size == 8 ? 8 : 6 };
                    int req;
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, ref detail, Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DETAIL_DATA)), out req, IntPtr.Zero)) continue;
                    var path = detail.DevicePath;
                    if (path == null) continue;
                    var lower = path.ToLowerInvariant();
                    bool sony = lower.Contains("vid_054c"), nintendo = lower.Contains("vid_057e");
                    if (!sony && !nintendo) continue;
                    var r = sony ? SonyPad(path) : SwitchPad(path);
                    if (r != null) return r;
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return null;
        }

        // DualSense (0CE6, Edge 0DF2) and DualShock 4 (05C4, 09CC): the battery byte in the input report; over Bluetooth the full
        // report only comes once a calibration feature report has been asked for (as every tool does)
        private static Reading SonyPad(string path)
        {
            using (var h = Open(path))
            {
                if (h == null) return null;
                var a = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES)) };
                if (!HidD_GetAttributes(h, ref a)) return null;
                bool dualSense = a.ProductID == 0x0CE6 || a.ProductID == 0x0DF2, ds4 = a.ProductID == 0x05C4 || a.ProductID == 0x09CC;
                if (!dualSense && !ds4) return null;
                var caps = Caps(h);
                if (caps.InputReportByteLength < 10) return null;   // (the audio / other collections of the same pad)
                bool bluetooth = caps.InputReportByteLength > 64;
                if (bluetooth)
                {
                    var feat = new byte[Math.Max(41, (int)caps.FeatureReportByteLength)]; feat[0] = dualSense ? (byte)0x05 : (byte)0x02;
                    HidD_GetFeature(h, feat, feat.Length);
                }
                string name = dualSense ? "DualSense" : "DualShock 4";
                for (int tries = 0; tries < 6; tries++)
                {
                    var buf = Read(h, caps.InputReportByteLength, 400);
                    if (buf == null) break;
                    if (dualSense)
                    {
                        int at = buf[0] == 0x01 && !bluetooth ? 53 : buf[0] == 0x31 ? 54 : -1;
                        if (at < 0 || at >= buf.Length) continue;
                        int status = buf[at], level = status & 0x0F, state = (status >> 4) & 0x0F;
                        int pct = Math.Min(100, level * 10 + 5);
                        if (state == 2) pct = 100;
                        return new Reading { Present = true, Name = name, Percent = pct, Level = LevelOf(pct), Charging = state == 1, Wired = !bluetooth, Source = "hid" };
                    }
                    else
                    {
                        int at = buf[0] == 0x01 && !bluetooth ? 30 : buf[0] == 0x11 ? 32 : -1;
                        if (at < 0 || at >= buf.Length) continue;
                        int status = buf[at], level = status & 0x0F; bool cable = (status & 0x10) != 0;
                        int pct = cable ? Math.Min(100, level * 10) : Math.Min(100, (level + 1) * 10);
                        return new Reading { Present = true, Name = name, Percent = pct, Level = LevelOf(pct), Charging = cable && level < 11, Wired = !bluetooth, Source = "hid" };
                    }
                }
            }
            return null;
        }

        // Switch Pro (2009) and Joy-Cons (2006 / 2007) over Bluetooth: the standard full report (0x30) carries the battery in
        // eight steps; it's switched on with the "set input report mode" subcommand (what every driver sends first)
        private static Reading SwitchPad(string path)
        {
            using (var h = Open(path))
            {
                if (h == null) return null;
                var a = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES)) };
                if (!HidD_GetAttributes(h, ref a)) return null;
                if (a.ProductID != 0x2009 && a.ProductID != 0x2006 && a.ProductID != 0x2007) return null;
                var caps = Caps(h);
                if (caps.OutputReportByteLength < 11 || caps.InputReportByteLength < 13) return null;
                string name = a.ProductID == 0x2009 ? "Pro Controller" : "Joy-Con";
                var cmd = new byte[caps.OutputReportByteLength];
                cmd[0] = 0x01; cmd[1] = 0x01;
                byte[] rumble = { 0x00, 0x01, 0x40, 0x40, 0x00, 0x01, 0x40, 0x40 };
                Array.Copy(rumble, 0, cmd, 2, 8);
                cmd[10] = 0x03; cmd[11] = 0x30;
                Write(h, cmd);
                for (int tries = 0; tries < 8; tries++)
                {
                    var buf = Read(h, caps.InputReportByteLength, 400);
                    if (buf == null) break;
                    if (buf[0] != 0x30 && buf[0] != 0x21) continue;
                    int b = buf[2], step = b >> 4; bool charging = (b & 0x10) != 0;
                    step = (step >> 1) & 0x07;   // 8 full, 6 medium, 4 low, 2 critical, 0 empty -> 4..0
                    int pct = Math.Min(100, step * 25);
                    return new Reading { Present = true, Name = name, Percent = pct, Level = LevelOf(pct), Charging = charging, Source = "hid" };
                }
            }
            return null;
        }

        // ---------------- 2. Windows' Bluetooth battery property ----------------
        private static readonly Regex PadName = new Regex(@"xbox|controller|gamepad|8bitdo|dualsense|dualshock|pro controller|joy-con|wireless controller", RegexOptions.IgnoreCase);

        private static Reading Bluetooth()
        {
            var set = SetupDiGetClassDevsAll(IntPtr.Zero, null, IntPtr.Zero, 0x06);   // every present device
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return null;
            try
            {
                var info = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf(typeof(SP_DEVINFO_DATA)) };
                var battery = new DEVPROPKEY { fmtid = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), pid = 2 };
                var nameKey = new DEVPROPKEY { fmtid = new Guid("b725f130-47ef-101a-a5f1-02608c9eebac"), pid = 10 };
                var buf = new byte[1024];
                for (int i = 0; SetupDiEnumDeviceInfo(set, i, ref info); i++)
                {
                    uint type; int size;
                    if (!SetupDiGetDevicePropertyW(set, ref info, ref battery, out type, buf, buf.Length, out size, 0) || size < 1) continue;
                    int pct = buf[0];
                    string name = null;
                    if (SetupDiGetDevicePropertyW(set, ref info, ref nameKey, out type, buf, buf.Length, out size, 0) && size > 2) name = Encoding.Unicode.GetString(buf, 0, size).TrimEnd('\0');
                    if (name == null || !PadName.IsMatch(name)) continue;
                    return new Reading { Present = true, Name = name, Percent = pct, Level = LevelOf(pct), Source = "bluetooth" };
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return null;
        }

        // ---------------- 3. Windows.Gaming.Input (by reflection: no WinRT reference at build time) ----------------
        private static Reading Gaming()
        {
            var type = Type.GetType("Windows.Gaming.Input.RawGameController, Windows, ContentType=WindowsRuntime");
            if (type == null) return null;
            var list = type.GetProperty("RawGameControllers").GetValue(null, null) as System.Collections.IEnumerable;
            if (list == null) return null;
            foreach (var pad in list)
            {
                var report = pad.GetType().GetMethod("TryGetBatteryReport").Invoke(pad, null);
                if (report == null) continue;
                var rt = report.GetType();
                int status = Convert.ToInt32(rt.GetProperty("Status").GetValue(report, null));   // 0 none, 1 discharging, 2 idle, 3 charging
                if (status == 0) continue;
                var remaining = rt.GetProperty("RemainingCapacityInMilliwattHours").GetValue(report, null);
                var full = rt.GetProperty("FullChargeCapacityInMilliwattHours").GetValue(report, null);
                if (remaining == null || full == null || Convert.ToInt32(full) <= 0) continue;
                int pct = Math.Max(0, Math.Min(100, (int)Math.Round(100.0 * Convert.ToInt32(remaining) / Convert.ToInt32(full))));
                string name = null;
                try { name = pad.GetType().GetProperty("DisplayName").GetValue(pad, null) as string; } catch { }
                bool wireless = false;
                try { wireless = (bool)pad.GetType().GetProperty("IsWireless").GetValue(pad, null); } catch { }
                return new Reading { Present = true, Name = name ?? "Controller", Percent = pct, Level = LevelOf(pct), Charging = status == 3, Wired = !wireless && status == 3, Source = "gaming input" };
            }
            return null;
        }

        // ---------------- 4. XInput's buckets ----------------
        [StructLayout(LayoutKind.Sequential)] private struct XInputBattery { public byte Type; public byte Level; }
        [DllImport("xinput1_4.dll")] private static extern int XInputGetBatteryInformation(int userIndex, byte devType, ref XInputBattery info);

        private static Reading XInput()
        {
            for (int i = 0; i < 4; i++)
            {
                var info = new XInputBattery();
                if (XInputGetBatteryInformation(i, 0, ref info) != 0 || info.Type == 0) continue;   // 0: nothing in that slot
                if (info.Type == 1) return new Reading { Present = true, Name = "Controller", Wired = true, Source = "xinput" };   // on a cable or a dongle: no charge to read
                return new Reading { Present = true, Name = "Controller", Level = Math.Min((int)info.Level, 3), Source = "xinput" };
            }
            return null;
        }

        // ---------------- HID plumbing ----------------
        private static SafeFileHandle Open(string path)
        {
            var h = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);   // read / write, overlapped (reads can be given up)
            if (h.IsInvalid) { h.Dispose(); return null; }
            return h;
        }

        private static HIDP_CAPS Caps(SafeFileHandle h)
        {
            var caps = new HIDP_CAPS();
            IntPtr pp;
            if (HidD_GetPreparsedData(h, out pp)) { HidP_GetCaps(pp, ref caps); HidD_FreePreparsedData(pp); }
            return caps;
        }

        // one input report, or null after 'ms' without one
        private static byte[] Read(SafeFileHandle h, int length, int ms)
        {
            var buf = new byte[length];
            var ev = CreateEvent(IntPtr.Zero, true, false, null);
            var ov = new NativeOverlapped { EventHandle = ev };
            var pin = GCHandle.Alloc(ov, GCHandleType.Pinned);
            try
            {
                int n;
                bool ok = ReadFile(h, buf, buf.Length, out n, pin.AddrOfPinnedObject());
                if (!ok)
                {
                    if (Marshal.GetLastWin32Error() != 997) return null;   // not "pending": a real failure
                    if (WaitForSingleObject(ev, ms) != 0) { CancelIo(h); return null; }
                    if (!GetOverlappedResult(h, pin.AddrOfPinnedObject(), out n, false)) return null;
                }
                return n > 0 ? buf : null;
            }
            finally { pin.Free(); CloseHandle(ev); }
        }

        private static void Write(SafeFileHandle h, byte[] report)
        {
            var ev = CreateEvent(IntPtr.Zero, true, false, null);
            var ov = new NativeOverlapped { EventHandle = ev };
            var pin = GCHandle.Alloc(ov, GCHandleType.Pinned);
            try
            {
                int n;
                if (!WriteFile(h, report, report.Length, out n, pin.AddrOfPinnedObject()) && Marshal.GetLastWin32Error() == 997)
                {
                    if (WaitForSingleObject(ev, 400) != 0) CancelIo(h);
                    else GetOverlappedResult(h, pin.AddrOfPinnedObject(), out n, false);
                }
            }
            finally { pin.Free(); CloseHandle(ev); }
        }

        [StructLayout(LayoutKind.Sequential)] private struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct SP_DEVICE_INTERFACE_DETAIL_DATA { public int cbSize; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)] public string DevicePath; }
        [StructLayout(LayoutKind.Sequential)] private struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public int DevInst; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)] private struct DEVPROPKEY { public Guid fmtid; public uint pid; }
        [StructLayout(LayoutKind.Sequential)] private struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID, ProductID, VersionNumber; }
        [StructLayout(LayoutKind.Sequential)] private struct HIDP_CAPS { public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved; public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices, NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices, NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices; }
        [StructLayout(LayoutKind.Sequential)] private struct NativeOverlapped { public IntPtr Internal, InternalHigh; public int OffsetLow, OffsetHigh; public IntPtr EventHandle; }

        [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr pp);
        [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr pp);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr pp, ref HIDP_CAPS caps);
        [DllImport("hid.dll")] private static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buf, int len);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr h, int flags);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetClassDevsW")] private static extern IntPtr SetupDiGetClassDevsAll(IntPtr g, string e, IntPtr h, int flags);
        [DllImport("setupapi.dll")] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid g, int index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, ref SP_DEVICE_INTERFACE_DETAIL_DATA detail, int size, out int required, IntPtr info);
        [DllImport("setupapi.dll")] private static extern bool SetupDiEnumDeviceInfo(IntPtr set, int index, ref SP_DEVINFO_DATA info);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] private static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref SP_DEVINFO_DATA info, ref DEVPROPKEY key, out uint type, byte[] buf, int size, out int required, int flags);
        [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadFile(SafeFileHandle h, byte[] buf, int n, out int read, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteFile(SafeFileHandle h, byte[] buf, int n, out int written, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetOverlappedResult(SafeFileHandle h, IntPtr overlapped, out int n, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CancelIo(SafeFileHandle h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateEvent(IntPtr sa, bool manual, bool initial, string name);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern int WaitForSingleObject(IntPtr h, int ms);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr h);
    }
}
