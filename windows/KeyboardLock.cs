// BrickMyBoard for Windows: keyboard discovery, lock protocol, input filter and the elevated helper.
// Compiles with the C# 5 compiler that ships with Windows (.NET Framework 4.8).
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace BrickMyBoard
{
    public sealed class Keyboard : IEquatable<Keyboard>
    {
        public string Id;          // PnP device instance ID, upper-case.
        public string Name;
        public string Transport;   // "USB", "Bluetooth", "" when unknown.
        public bool BuiltIn;
        public int Vendor;
        public int Product;
        public IntPtr Handle;      // Raw Input device handle; valid only within one process and until disconnect.

        public string Connection { get { return BuiltIn ? "Built-in" : (Transport.Length == 0 ? "External" : Transport); } }
        public string DisplayName { get { return Name; } }

        public bool Equals(Keyboard other)
        {
            return other != null && Id == other.Id && Name == other.Name && Transport == other.Transport && BuiltIn == other.BuiltIn && Vendor == other.Vendor && Product == other.Product;
        }
        public override bool Equals(object obj) { return Equals(obj as Keyboard); }
        public override int GetHashCode() { return Id.GetHashCode(); }
    }

    public sealed class LockError : Exception
    {
        public LockError(string message) : base(message) { }
    }

    public sealed class LockState
    {
        public string Id;
        public int? Remaining;   // null means until unlocked; the helper still releases on disconnect.
        public bool Automatic;
    }

    public enum CommandAction { Lock, Unlock, UnlockAll, Stop }

    /// <summary>App → helper message. Wire format: action TAB seconds TAB automatic TAB id;id;id</summary>
    public sealed class Command
    {
        public CommandAction Action;
        public List<string> Ids = new List<string>();
        public int Seconds = 300;
        public bool Automatic;

        public void Validate()
        {
            if (Ids.Count > 32 || Ids.Distinct().Count() != Ids.Count || !Ids.All(Devices.IsValidId)
                || Seconds < 0 || Seconds > 86400 || (Action == CommandAction.Lock && Ids.Count == 0))
                throw new LockError("Invalid keyboard selection or timer. No changes applied.");
        }

        public string Format()
        {
            return string.Join("\t", new[] { Action.ToString().ToLowerInvariant(), Seconds.ToString(), Automatic ? "1" : "0", string.Join(";", Ids) });
        }

        public static Command Parse(string line)
        {
            var parts = line.Split('\t');
            if (parts.Length != 4) throw new LockError("Malformed command.");
            var command = new Command();
            switch (parts[0])
            {
                case "lock": command.Action = CommandAction.Lock; break;
                case "unlock": command.Action = CommandAction.Unlock; break;
                case "unlockall": command.Action = CommandAction.UnlockAll; break;
                case "stop": command.Action = CommandAction.Stop; break;
                default: throw new LockError("Unknown command.");
            }
            if (!int.TryParse(parts[1], out command.Seconds)) throw new LockError("Malformed command.");
            command.Automatic = parts[2] == "1";
            command.Ids = parts[3].Length == 0 ? new List<string>() : parts[3].Split(';').ToList();
            command.Validate();
            return command;
        }
    }

    /// <summary>Helper → app message. Wire format: kind TAB message TAB id,remaining,automatic TAB ...</summary>
    public sealed class Reply
    {
        public string Kind;      // ready, state, error
        public List<LockState> Locks = new List<LockState>();
        public string Message;

        public string Format()
        {
            var fields = new List<string> { Kind, (Message ?? "").Replace('\t', ' ').Replace('\n', ' ') };
            fields.AddRange(Locks.Select(l => l.Id + "," + (l.Remaining.HasValue ? l.Remaining.Value.ToString() : "-") + "," + (l.Automatic ? "1" : "0")));
            return string.Join("\t", fields);
        }

        public static Reply Parse(string line)
        {
            var parts = line.Split('\t');
            if (parts.Length < 2 || !(parts[0] == "ready" || parts[0] == "state" || parts[0] == "error")) throw new LockError("Invalid helper response.");
            var reply = new Reply { Kind = parts[0], Message = parts[1].Length == 0 ? null : parts[1] };
            for (var i = 2; i < parts.Length; i++)
            {
                var fields = parts[i].Split(',');
                if (fields.Length != 3 || !Devices.IsValidId(fields[0])) throw new LockError("Invalid helper response.");
                int remaining;
                var state = new LockState { Id = fields[0], Automatic = fields[2] == "1" };
                if (fields[1] != "-") { if (!int.TryParse(fields[1], out remaining)) throw new LockError("Invalid helper response."); state.Remaining = remaining; }
                reply.Locks.Add(state);
            }
            if (reply.Locks.Count > 32 || reply.Locks.Select(l => l.Id).Distinct().Count() != reply.Locks.Count) throw new LockError("Invalid helper response.");
            return reply;
        }
    }

    /// <summary>Splits a byte stream into newline-terminated UTF-8 lines with a hard size limit.</summary>
    public sealed class LineFramer
    {
        readonly MemoryStream buffer = new MemoryStream();
        public const int Limit = 65536;

        public List<string> Push(byte[] data, int count)
        {
            buffer.Write(data, 0, count);
            if (buffer.Length > Limit) throw new LockError("Control message exceeded its size limit.");
            var bytes = buffer.ToArray();
            var lines = new List<string>();
            var start = 0;
            for (var i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] != 10) continue;
                lines.Add(Encoding.UTF8.GetString(bytes, start, i - start));
                start = i + 1;
            }
            buffer.SetLength(0);
            buffer.Write(bytes, start, bytes.Length - start);
            return lines;
        }
        public long Pending { get { return buffer.Length; } }
    }

    public static class Wire
    {
        public static void Send(Stream stream, string line)
        {
            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            try { stream.Write(bytes, 0, bytes.Length); stream.Flush(); }
            catch (Exception) { throw new LockError("The keyboard-control connection closed."); }
        }
    }

    public static class Native
    {
        public const int WM_INPUT = 0x00FF, WM_INPUT_DEVICE_CHANGE = 0x00FE, WM_HOTKEY = 0x0312, WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
        public const uint RIM_TYPEKEYBOARD = 1, RIDI_DEVICENAME = 0x20000007, RIDEV_INPUTSINK = 0x00000100, RIDEV_DEVNOTIFY = 0x00002000, RID_INPUT = 0x10000003;
        public const uint PM_REMOVE = 1;
        public const int WH_KEYBOARD_LL = 13, LLKHF_EXTENDED = 0x01, LLKHF_INJECTED = 0x10, RI_KEY_BREAK = 1, RI_KEY_E0 = 2;
        public const int VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3, VK_CONTROL = 0x11, VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_SHIFT = 0x10, VK_LMENU = 0xA4, VK_RMENU = 0xA5, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
        public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;
        public const uint ES_CONTINUOUS = 0x80000000, ES_DISPLAY_REQUIRED = 0x00000002, ES_SYSTEM_REQUIRED = 0x00000001;

        [StructLayout(LayoutKind.Sequential)] public struct RAWINPUTDEVICELIST { public IntPtr hDevice; public uint dwType; }
        [StructLayout(LayoutKind.Sequential)] public struct RAWINPUTDEVICE { public ushort usUsagePage, usUsage; public uint dwFlags; public IntPtr hwndTarget; }
        [StructLayout(LayoutKind.Sequential)] public struct RAWINPUTHEADER { public uint dwType, dwSize; public IntPtr hDevice, wParam; }
        [StructLayout(LayoutKind.Sequential)] public struct RAWKEYBOARD { public ushort MakeCode, Flags, Reserved, VKey; public uint Message, ExtraInformation; }
        [StructLayout(LayoutKind.Sequential)] public struct RAWINPUTKEYBOARD { public RAWINPUTHEADER header; public RAWKEYBOARD keyboard; }
        [StructLayout(LayoutKind.Sequential)] public struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public UIntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }
        [StructLayout(LayoutKind.Sequential)] public struct DEVPROPKEY { public Guid fmtid; public uint pid; }

        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)] public static extern uint GetRawInputDeviceList([Out] RAWINPUTDEVICELIST[] list, ref uint count, uint size);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, StringBuilder data, ref uint size);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);
        [DllImport("user32.dll", SetLastError = true)] public static extern uint GetRawInputData(IntPtr rawInput, uint command, out RAWINPUTKEYBOARD data, ref uint size, uint headerSize);
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] public static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint filterMin, uint filterMax, uint remove);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] public static extern int CM_Locate_DevNodeW(out uint devInst, string id, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] public static extern int CM_Get_DevNode_PropertyW(uint devInst, ref DEVPROPKEY key, out uint type, byte[] buffer, ref uint size, uint flags);
        [DllImport("cfgmgr32.dll")] public static extern int CM_Get_Parent(out uint parent, uint devInst, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] public static extern int CM_Get_Device_IDW(uint devInst, StringBuilder buffer, uint length, uint flags);

        static readonly Guid DeviceProps = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0");
        public static DEVPROPKEY DeviceDesc = new DEVPROPKEY { fmtid = DeviceProps, pid = 2 };
        public static DEVPROPKEY FriendlyName = new DEVPROPKEY { fmtid = DeviceProps, pid = 14 };
        public static DEVPROPKEY RemovalPolicy = new DEVPROPKEY { fmtid = DeviceProps, pid = 33 };
        public const uint DEVPROP_TYPE_STRING = 0x12, DEVPROP_TYPE_UINT32 = 0x7;
        public const uint CM_REMOVAL_POLICY_EXPECT_NO_REMOVAL = 1;

        public static string StringProperty(uint devInst, DEVPROPKEY key)
        {
            uint type; var buffer = new byte[1024]; var size = (uint)buffer.Length;
            if (CM_Get_DevNode_PropertyW(devInst, ref key, out type, buffer, ref size, 0) != 0 || type != DEVPROP_TYPE_STRING || size < 2) return null;
            return Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0');
        }
        public static uint? UIntProperty(uint devInst, DEVPROPKEY key)
        {
            uint type; var buffer = new byte[4]; var size = 4u;
            if (CM_Get_DevNode_PropertyW(devInst, ref key, out type, buffer, ref size, 0) != 0 || type != DEVPROP_TYPE_UINT32) return null;
            return BitConverter.ToUInt32(buffer, 0);
        }
        public static string ParentId(uint devInst, out uint parent)
        {
            if (CM_Get_Parent(out parent, devInst, 0) != 0) return null;
            var buffer = new StringBuilder(512);
            return CM_Get_Device_IDW(parent, buffer, (uint)buffer.Capacity, 0) == 0 ? buffer.ToString() : null;
        }
    }

    public static class Devices
    {
        const string IdChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789\\&#_{}().-:";

        public static bool IsValidId(string id)
        {
            return !string.IsNullOrEmpty(id) && id.Length <= 300 && id.All(c => IdChars.IndexOf(c) >= 0) && id.Contains('\\');
        }

        /// <summary>Turns a Raw Input device path such as \\?\HID#VID_1&PID_2#7&abc#{guid} into the PnP instance ID HID\VID_1&PID_2\7&ABC.</summary>
        public static string InstanceId(string devicePath)
        {
            if (devicePath == null) return null;
            var path = devicePath.StartsWith(@"\\?\") || devicePath.StartsWith(@"\\.\") ? devicePath.Substring(4) : devicePath;
            var parts = path.Split('#').ToList();
            if (parts.Count >= 2 && parts[parts.Count - 1].StartsWith("{")) parts.RemoveAt(parts.Count - 1);
            var id = string.Join("\\", parts).ToUpperInvariant();
            return IsValidId(id) ? id : null;
        }

        /// <summary>Terminal-services and software keyboards look like keyboards but cannot be held physically.</summary>
        public static bool IsVirtual(string instanceId)
        {
            var id = instanceId.ToUpperInvariant();
            return id.StartsWith("ROOT\\") || id.Contains("RDP_KBD") || id.Contains("TERMINPUT") || id.Contains("VIRTUAL") || id.Contains("REMOTE");
        }

        /// <summary>Classification shared with the self-test: removal policy 1 means the firmware promised the device never leaves.</summary>
        public static void Classify(uint? removalPolicy, IEnumerable<string> enumerators, out bool builtIn, out string transport)
        {
            transport = "";
            foreach (var e in enumerators.Select(x => (x ?? "").ToUpperInvariant()))
            {
                if (e == "USB") { transport = "USB"; break; }
                if (e.StartsWith("BTH")) { transport = "Bluetooth"; break; }
            }
            builtIn = transport.Length == 0 && removalPolicy == Native.CM_REMOVAL_POLICY_EXPECT_NO_REMOVAL;
        }

        public static string Enumerator(string instanceId)
        {
            var slash = instanceId.IndexOf('\\');
            return slash > 0 ? instanceId.Substring(0, slash) : instanceId;
        }

        static void ParseVendorProduct(string id, out int vendor, out int product)
        {
            vendor = 0; product = 0;
            foreach (var token in id.Split('\\', '&'))
            {
                if (token.StartsWith("VID_") && token.Length >= 8) int.TryParse(token.Substring(4, 4), System.Globalization.NumberStyles.HexNumber, null, out vendor);
                if (token.StartsWith("PID_") && token.Length >= 8) int.TryParse(token.Substring(4, 4), System.Globalization.NumberStyles.HexNumber, null, out product);
            }
        }

        public static Keyboard Describe(string instanceId, IntPtr handle)
        {
            var keyboard = new Keyboard { Id = instanceId, Handle = handle, Name = "Keyboard", Transport = "" };
            ParseVendorProduct(instanceId, out keyboard.Vendor, out keyboard.Product);
            uint devInst;
            if (Native.CM_Locate_DevNodeW(out devInst, instanceId, 0) != 0) return keyboard;
            keyboard.Name = Native.StringProperty(devInst, Native.FriendlyName) ?? Native.StringProperty(devInst, Native.DeviceDesc) ?? "Keyboard";
            var enumerators = new List<string> { Enumerator(instanceId) };
            var current = devInst;
            for (var depth = 0; depth < 6; depth++)
            {
                uint parent; var parentId = Native.ParentId(current, out parent);
                if (parentId == null) break;
                enumerators.Add(Enumerator(parentId.ToUpperInvariant()));
                current = parent;
            }
            Classify(Native.UIntProperty(devInst, Native.RemovalPolicy), enumerators, out keyboard.BuiltIn, out keyboard.Transport);
            return keyboard;
        }

        /// <summary>Every physical keyboard Windows currently delivers Raw Input for, sorted built-in first.</summary>
        public static List<Keyboard> Enumerate()
        {
            uint count = 0; var size = (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICELIST));
            if (Native.GetRawInputDeviceList(null, ref count, size) == uint.MaxValue) throw new LockError("Could not discover connected keyboards.");
            var list = new Native.RAWINPUTDEVICELIST[count];
            if (count > 0 && Native.GetRawInputDeviceList(list, ref count, size) == uint.MaxValue) throw new LockError("Could not discover connected keyboards.");
            var result = new List<Keyboard>();
            foreach (var entry in list.Take((int)count))
            {
                if (entry.dwType != Native.RIM_TYPEKEYBOARD) continue;
                uint length = 0;
                Native.GetRawInputDeviceInfo(entry.hDevice, Native.RIDI_DEVICENAME, null, ref length);
                if (length == 0 || length > 1024) continue;
                var builder = new StringBuilder((int)length + 1);
                if (Native.GetRawInputDeviceInfo(entry.hDevice, Native.RIDI_DEVICENAME, builder, ref length) == uint.MaxValue) continue;
                var id = InstanceId(builder.ToString());
                if (id == null || IsVirtual(id) || result.Any(k => k.Id == id)) continue;
                result.Add(Describe(id, entry.hDevice));
            }
            return result.OrderBy(k => k.BuiltIn ? 0 : 1).ThenBy(k => k.Name, StringComparer.OrdinalIgnoreCase).ThenBy(k => k.Id, StringComparer.Ordinal).ToList();
        }

        /// <summary>Automatic mode locks built-in keyboards only, and only while an external one is connected.</summary>
        public static List<string> AutoTargets(List<Keyboard> devices)
        {
            if (!devices.Any(d => !d.BuiltIn)) return new List<string>();
            return devices.Where(d => d.BuiltIn).Select(d => d.Id).ToList();
        }

        public static Dictionary<string, T> AcquireTransaction<T>(IEnumerable<string> ids, Func<string, T> open, Action<T> rollback)
        {
            var opened = new Dictionary<string, T>();
            try { foreach (var id in ids) opened[id] = open(id); return opened; }
            catch { foreach (var device in opened.Values) rollback(device); throw; }
        }
    }

    public static class Elevation
    {
        public static bool IsElevated()
        {
            using (var identity = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>One pending Raw Input keyboard event, waiting to be matched with the low-level hook call for the same key.</summary>
    public struct RawKey
    {
        public IntPtr Device; public ushort MakeCode; public bool Extended; public bool Up; public int VKey; public int Time;
    }

    public static class Combo
    {
        /// <summary>Exit combo: both Ctrl keys and nothing else, so a cleaning cloth can't trigger it.</summary>
        public static bool ExitHeld(HashSet<int> keys) { return keys.Count == 2 && keys.Contains(Native.VK_LCONTROL) && keys.Contains(Native.VK_RCONTROL); }

        public static int Unsided(int vk)
        {
            switch (vk)
            {
                case Native.VK_LCONTROL: case Native.VK_RCONTROL: return Native.VK_CONTROL;
                case Native.VK_LSHIFT: case Native.VK_RSHIFT: return Native.VK_SHIFT;
                case Native.VK_LMENU: case Native.VK_RMENU: return Native.VK_MENU;
                default: return vk;
            }
        }

        /// <summary>Finds the Raw Input event that produced a hook call: same scan code and direction, or the same unsided virtual key.</summary>
        public static int Match(List<RawKey> pending, uint scanCode, bool extended, bool up, int vk)
        {
            for (var i = 0; i < pending.Count; i++)
            {
                var r = pending[i];
                if (r.Up == up && r.MakeCode != 0 && r.MakeCode == scanCode && r.Extended == extended) return i;
            }
            for (var i = 0; i < pending.Count; i++)
            {
                var r = pending[i];
                if (r.Up == up && Unsided(r.VKey) == Unsided(vk)) return i;
            }
            return -1;
        }
    }

    /// <summary>
    /// Blocks key presses from chosen keyboards. Raw Input tells which keyboard a key came from; the low-level hook
    /// decides whether the key goes any further. Windows removes the hook the moment this process ends, so a crashed
    /// helper can never leave a keyboard locked. Key releases always pass so no key is left stuck down.
    /// </summary>
    public sealed class InputFilter : NativeWindow, IDisposable
    {
        readonly HashSet<IntPtr> locked = new HashSet<IntPtr>();
        readonly List<RawKey> pending = new List<RawKey>();
        readonly Native.HookProc proc;
        IntPtr hook;
        public bool BlockAll;                                  // Cleaning: every keyboard is locked, so attribution is not needed.
        public readonly HashSet<int> HeldKeys = new HashSet<int>(); // Keys currently held on locked keyboards.
        public int Unattributed;                               // Diagnostics: hook calls with no Raw Input to match (passed through).
        public event Action DevicesChanged;

        public InputFilter()
        {
            CreateHandle(new CreateParams { Caption = "BrickMyBoard input filter" });
            var registration = new[] { new Native.RAWINPUTDEVICE { usUsagePage = 1, usUsage = 6, dwFlags = Native.RIDEV_INPUTSINK | Native.RIDEV_DEVNOTIFY, hwndTarget = Handle } };
            if (!Native.RegisterRawInputDevices(registration, 1, (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICE)))) throw new LockError("Could not register for keyboard events.");
            proc = HookCallback;
            hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, proc, Native.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) throw new LockError("Windows refused to install the keyboard filter (" + Marshal.GetLastWin32Error() + ").");
        }

        public void SetLocked(IEnumerable<IntPtr> handles)
        {
            locked.Clear();
            foreach (var h in handles) locked.Add(h);
            if (locked.Count == 0) { HeldKeys.Clear(); BlockAll = false; }
        }
        public int LockedCount { get { return locked.Count; } }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_INPUT) { Collect(m.LParam); }
            else if (m.Msg == Native.WM_INPUT_DEVICE_CHANGE) { var handler = DevicesChanged; if (handler != null) handler(); }
            base.WndProc(ref m);
        }

        void Collect(IntPtr lParam)
        {
            Native.RAWINPUTKEYBOARD data; var size = (uint)Marshal.SizeOf(typeof(Native.RAWINPUTKEYBOARD));
            if (Native.GetRawInputData(lParam, Native.RID_INPUT, out data, ref size, (uint)Marshal.SizeOf(typeof(Native.RAWINPUTHEADER))) == uint.MaxValue) return;
            if (data.header.dwType != Native.RIM_TYPEKEYBOARD || data.header.hDevice == IntPtr.Zero) return;
            pending.Add(new RawKey
            {
                Device = data.header.hDevice, MakeCode = data.keyboard.MakeCode, Extended = (data.keyboard.Flags & Native.RI_KEY_E0) != 0,
                Up = (data.keyboard.Flags & Native.RI_KEY_BREAK) != 0, VKey = data.keyboard.VKey, Time = Environment.TickCount
            });
            if (pending.Count > 64) pending.RemoveRange(0, pending.Count - 64);
        }

        /// <summary>Raw Input is posted before the hook runs; pull any queued WM_INPUT so the pending list is current.</summary>
        void Drain()
        {
            Native.MSG msg;
            while (Native.PeekMessage(out msg, Handle, Native.WM_INPUT, Native.WM_INPUT, Native.PM_REMOVE)) Collect(msg.lParam);
            var now = Environment.TickCount;
            pending.RemoveAll(r => now - r.Time > 500);
        }

        IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code < 0 || locked.Count == 0) return Native.CallNextHookEx(hook, code, wParam, lParam);
            var info = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
            if ((info.flags & Native.LLKHF_INJECTED) != 0) return Native.CallNextHookEx(hook, code, wParam, lParam); // Software keyboards and remote sessions stay usable.
            var message = (int)wParam;
            var up = message == Native.WM_KEYUP || message == Native.WM_SYSKEYUP;
            Drain();
            var index = Combo.Match(pending, info.scanCode, (info.flags & Native.LLKHF_EXTENDED) != 0, up, (int)info.vkCode);
            var fromLocked = BlockAll;
            if (index >= 0) { fromLocked = fromLocked || locked.Contains(pending[index].Device); pending.RemoveAt(index); }
            else if (!BlockAll) Unattributed++;
            if (!fromLocked) return Native.CallNextHookEx(hook, code, wParam, lParam);
            if (up) HeldKeys.Remove((int)info.vkCode); else HeldKeys.Add((int)info.vkCode);
            return up ? Native.CallNextHookEx(hook, code, wParam, lParam) : (IntPtr)1;
        }

        public void Dispose()
        {
            if (hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
            if (Handle != IntPtr.Zero) DestroyHandle();
        }
    }

    /// <summary>
    /// The elevated helper. It connects back to the app's private named pipe, holds the keyboard filter, enforces timers
    /// and the cleaning exit combo, and releases everything when the pipe closes or the process ends.
    /// </summary>
    public static class Helper
    {
        sealed class Lease { public string Id; public IntPtr Handle; public DateTime? Deadline; public bool Automatic; }

        public static int? RemainingSeconds(DateTime? deadline)
        {
            if (!deadline.HasValue) return null;
            return Math.Max(0, (int)Math.Ceiling((deadline.Value - DateTime.UtcNow).TotalSeconds));
        }

        public static void Run(string pipeName)
        {
            if (pipeName.Length > 64 || !pipeName.All(char.IsLetterOrDigit)) throw new LockError("Invalid control pipe name.");
            using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                try { pipe.Connect(3000); }
                catch (Exception) { throw new LockError("The app closed before authorization finished. Nothing locked."); }
                var incoming = new Queue<string>(); var incomingLock = new object(); var closed = false;
                var reader = new Thread(() =>
                {
                    var framer = new LineFramer(); var bytes = new byte[8192];
                    try
                    {
                        while (true)
                        {
                            var count = pipe.Read(bytes, 0, bytes.Length);
                            if (count <= 0) break;
                            var lines = framer.Push(bytes, count);
                            lock (incomingLock) foreach (var line in lines) incoming.Enqueue(line);
                        }
                    }
                    catch (Exception) { }
                    lock (incomingLock) closed = true;
                }) { IsBackground = true };
                reader.Start();

                var locked = new Dictionary<string, Lease>();
                var filter = new InputFilter();
                var sendLock = new object();
                Action<Reply> send = reply => { lock (sendLock) Wire.Send(pipe, reply.Format()); };
                Func<List<LockState>> states = () => locked.Values.OrderBy(l => l.Id, StringComparer.Ordinal)
                    .Select(l => new LockState { Id = l.Id, Remaining = RemainingSeconds(l.Deadline), Automatic = l.Automatic }).ToList();
                Action applyFilter = () => filter.SetLocked(locked.Values.Select(l => l.Handle));
                Action<IEnumerable<string>> unlock = ids => { foreach (var id in ids.ToList()) locked.Remove(id); applyFilter(); };
                var devicesDirty = true;
                filter.DevicesChanged += () => devicesDirty = true;
                try
                {
                    send(new Reply { Kind = "ready", Message = Elevation.IsElevated() ? "elevated" : "standard" });
                    var lastScan = DateTime.UtcNow; DateTime? comboSince = null;
                    var exit = false;
                    using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
                    {
                        timer.Tick += (s, e) =>
                        {
                            try
                            {
                                if (locked.Count > 0 && Combo.ExitHeld(filter.HeldKeys))
                                {
                                    if (comboSince.HasValue && (DateTime.UtcNow - comboSince.Value).TotalSeconds >= 2)
                                    {
                                        comboSince = null; unlock(locked.Keys);
                                        send(new Reply { Kind = "state", Locks = states(), Message = "Released by holding both Ctrl keys." });
                                    }
                                    else if (!comboSince.HasValue) comboSince = DateTime.UtcNow;
                                }
                                else comboSince = null;
                                List<string> lines; bool done;
                                lock (incomingLock) { lines = incoming.ToList(); incoming.Clear(); done = closed; }
                                foreach (var line in lines)
                                {
                                    try
                                    {
                                        var command = Command.Parse(line);
                                        switch (command.Action)
                                        {
                                            case CommandAction.Lock:
                                                var devices = Devices.Enumerate(); devicesDirty = false;
                                                if (!command.Ids.All(id => devices.Any(d => d.Id == id))) throw new LockError("A selected keyboard disconnected. Refresh your selection.");
                                                if (command.Automatic && !command.Ids.All(id => Devices.AutoTargets(devices).Contains(id)))
                                                    throw new LockError("Automatic mode requires a connected external keyboard and targets only built-in keyboards.");
                                                var fresh = command.Ids.Where(id => !locked.ContainsKey(id)).ToList();
                                                var opened = Devices.AcquireTransaction(fresh, id =>
                                                {
                                                    var device = devices.First(d => d.Id == id);
                                                    if (device.Handle == IntPtr.Zero) throw new LockError("Windows does not deliver input events for " + device.Name + ", so it cannot be locked.");
                                                    return device.Handle;
                                                }, h => { });
                                                foreach (var pair in opened)
                                                    locked[pair.Key] = new Lease { Id = pair.Key, Handle = pair.Value, Deadline = command.Seconds == 0 ? (DateTime?)null : DateTime.UtcNow.AddSeconds(command.Seconds), Automatic = command.Automatic };
                                                applyFilter();
                                                filter.BlockAll = devices.All(d => locked.ContainsKey(d.Id));
                                                break;
                                            case CommandAction.Unlock: unlock(command.Ids); break;
                                            case CommandAction.UnlockAll: unlock(locked.Keys); break;
                                            case CommandAction.Stop: exit = true; break;
                                        }
                                        send(new Reply { Kind = "state", Locks = states() });
                                    }
                                    catch (LockError error) { send(new Reply { Kind = "error", Locks = states(), Message = error.Message }); }
                                }
                                if (done) exit = true;
                                if ((DateTime.UtcNow - lastScan).TotalSeconds >= 1 || devicesDirty)
                                {
                                    lastScan = DateTime.UtcNow; devicesDirty = false;
                                    var devices = Devices.Enumerate();
                                    var connected = devices.ToDictionary(d => d.Id, d => d.Handle);
                                    foreach (var lease in locked.Values) { IntPtr h; if (connected.TryGetValue(lease.Id, out h)) lease.Handle = h; } // Handles can change after sleep.
                                    var externalAvailable = devices.Any(d => !d.BuiltIn && !locked.ContainsKey(d.Id));
                                    unlock(locked.Values.Where(l => !connected.ContainsKey(l.Id) || RemainingSeconds(l.Deadline) == 0 || (l.Automatic && !externalAvailable)).Select(l => l.Id));
                                    applyFilter();
                                    filter.BlockAll = locked.Count > 0 && devices.All(d => locked.ContainsKey(d.Id));
                                    send(new Reply { Kind = "state", Locks = states() });
                                }
                            }
                            catch (Exception) { exit = true; }
                            if (exit) Application.ExitThread();
                        };
                        timer.Start();
                        Application.Run();
                    }
                }
                finally { unlock(locked.Keys.ToList()); filter.Dispose(); }
            }
        }
    }

    public static class Cli
    {
        /// <summary>Returns true when the arguments were a diagnostic command and the app should not open its window.</summary>
        public static bool Run(string[] args)
        {
            if (args.Length == 0) return false;
            if (args.Length == 1 && args[0] == "--self-test") { Checks.Run(); return true; }
            if (args.Length == 1 && args[0] == "--check")
            {
                foreach (var device in Devices.Enumerate()) Console.WriteLine(device.Id + ": " + device.Name + " [" + device.Connection + "]");
                return true;
            }
            if (args.Length == 2 && args[0] == "--helper") { Helper.Run(args[1]); return true; }
            throw new LockError("Open BrickMyBoard.exe. Diagnostic options: --check, --self-test.");
        }
    }
}
