// Regression checks for the logic that does not need real hardware. Run with BrickMyBoard.exe --self-test.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BrickMyBoard
{
    public static class Checks
    {
        static void Expect(bool condition, string what)
        {
            if (!condition) throw new LockError("Self-test failed: " + what);
        }
        static void ExpectThrows(Action action, string what)
        {
            try { action(); } catch (LockError) { return; }
            throw new LockError("Self-test failed: " + what + " was accepted");
        }

        public static void Run()
        {
            // Device paths become PnP instance IDs; the interface GUID and prefix are dropped.
            Expect(Devices.InstanceId(@"\\?\HID#VID_046D&PID_C52B&MI_00#7&2de99c3&0&0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}") == @"HID\VID_046D&PID_C52B&MI_00\7&2DE99C3&0&0000", "HID path");
            Expect(Devices.InstanceId(@"\\?\ACPI#DLLK0C6B#4&21042591&0#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}") == @"ACPI\DLLK0C6B\4&21042591&0", "ACPI path");
            Expect(Devices.InstanceId("garbage") == null && Devices.InstanceId(null) == null, "invalid path");
            Expect(Devices.IsVirtual(@"ROOT\RDP_KBD\0000") && Devices.IsVirtual(@"HID\TERMINPUT_BUS&COL01\1") && !Devices.IsVirtual(@"ACPI\DLLK0C6B\4&21042591&0"), "virtual filter");
            Expect(!Devices.IsValidId("HID\\A\tB") && !Devices.IsValidId("") && !Devices.IsValidId("NOSLASH") && !Devices.IsValidId(new string('A', 301) + "\\B"), "id validation");

            // Built-in vs external classification.
            bool builtIn; string transport;
            Devices.Classify(1, new[] { "ACPI", "PCI", "ACPI_HAL" }, out builtIn, out transport);
            Expect(builtIn && transport == "", "PS/2 keyboard is built-in");
            Devices.Classify(1, new[] { "HID", "BUTTONCONVERTER", "HID", "ACPI" }, out builtIn, out transport);
            Expect(builtIn && transport == "", "I2C HID keyboard is built-in");
            Devices.Classify(3, new[] { "HID", "USB", "USB" }, out builtIn, out transport);
            Expect(!builtIn && transport == "USB", "USB keyboard is external");
            Devices.Classify(1, new[] { "HID", "USB" }, out builtIn, out transport);
            Expect(!builtIn && transport == "USB", "USB wins over removal policy");
            Devices.Classify(2, new[] { "HID", "BTHLEDEVICE", "BTH" }, out builtIn, out transport);
            Expect(!builtIn && transport == "Bluetooth", "Bluetooth keyboard");
            Devices.Classify(null, new[] { "HID" }, out builtIn, out transport);
            Expect(!builtIn && transport == "", "unknown policy is treated as external");
            Expect(Devices.IsConverted(new[] { "HID", "BUTTONCONVERTER", "HID", "ACPI" }) && !Devices.IsConverted(new[] { "ACPI", "PCI" }) && !Devices.IsConverted(new[] { "HID", "USB" }), "converted hotkey devices are hidden");

            var internalKeyboard = new Keyboard { Id = @"ACPI\A\1", Name = "Internal", BuiltIn = true, Transport = "" };
            var external = new Keyboard { Id = @"HID\B\2", Name = "External", BuiltIn = false, Transport = "USB" };
            Expect(Devices.AutoTargets(new List<Keyboard> { internalKeyboard }).Count == 0, "no automation without external");
            Expect(Devices.AutoTargets(new List<Keyboard> { internalKeyboard, external }).SequenceEqual(new[] { @"ACPI\A\1" }), "automation targets built-in");
            Expect(Devices.AutoTargets(new List<Keyboard> { external }).Count == 0, "automation never targets external");

            // Commands are validated before anything is applied.
            ExpectThrows(() => new Command { Action = CommandAction.Lock }.Validate(), "empty lock");
            ExpectThrows(() => new Command { Action = CommandAction.Lock, Ids = { @"A\1", @"A\1" } }.Validate(), "duplicate ids");
            ExpectThrows(() => new Command { Action = CommandAction.Lock, Ids = { "bad id" } }.Validate(), "invalid id");
            ExpectThrows(() => new Command { Action = CommandAction.Lock, Ids = { @"A\1" }, Seconds = -1 }.Validate(), "negative timer");
            ExpectThrows(() => new Command { Action = CommandAction.Lock, Ids = { @"A\1" }, Seconds = 86401 }.Validate(), "oversized timer");
            ExpectThrows(() => new Command { Action = CommandAction.Lock, Ids = Enumerable.Range(0, 33).Select(i => @"A\" + i).ToList() }.Validate(), "too many ids");
            new Command { Action = CommandAction.Lock, Ids = { @"A\1", @"A\2" }, Seconds = 0 }.Validate();
            var roundTrip = Command.Parse(new Command { Action = CommandAction.Lock, Ids = { @"HID\VID_1&PID_2\3", @"ACPI\X\1" }, Seconds = 120, Automatic = true }.Format());
            Expect(roundTrip.Action == CommandAction.Lock && roundTrip.Ids.Count == 2 && roundTrip.Ids[1] == @"ACPI\X\1" && roundTrip.Seconds == 120 && roundTrip.Automatic, "command round trip");
            Expect(Command.Parse("unlockall\t0\t0\t").Action == CommandAction.UnlockAll, "unlock all parses");
            ExpectThrows(() => Command.Parse("explode\t0\t0\t"), "unknown command");
            ExpectThrows(() => Command.Parse("lock\t300\t0\tA\\1\textra"), "extra fields");

            var reply = Reply.Parse(new Reply { Kind = "state", Message = "tab\tnewline\n", Locks = { new LockState { Id = @"A\1", Remaining = 30 }, new LockState { Id = @"A\2", Remaining = null, Automatic = true } } }.Format());
            Expect(reply.Kind == "state" && reply.Message == "tab newline " && reply.Locks.Count == 2 && reply.Locks[0].Remaining == 30 && !reply.Locks[1].Remaining.HasValue && reply.Locks[1].Automatic, "reply round trip");
            Expect(Reply.Parse("ready\t").Kind == "ready" && Reply.Parse("ready\t").Locks.Count == 0 && Reply.Parse("ready\t").Message == null, "ready reply");
            ExpectThrows(() => Reply.Parse("bogus\t"), "unknown reply kind");
            ExpectThrows(() => Reply.Parse("state\t\tA\\1,x,0"), "bad remaining");
            ExpectThrows(() => Reply.Parse("state\t\tA\\1,1,0\tA\\1,2,0"), "duplicate lock ids");

            // Transactional acquisition releases everything on partial failure.
            var released = new List<string>();
            ExpectThrows(() => Devices.AcquireTransaction(new[] { "a", "b", "c" }, id => { if (id == "c") throw new LockError("Simulated denial"); return id; }, released.Add), "partial acquisition");
            Expect(released.OrderBy(x => x).SequenceEqual(new[] { "a", "b" }), "all newly acquired devices released on partial failure");

            // Fragmented and oversized IPC.
            var framer = new LineFramer();
            var wire = Encoding.UTF8.GetBytes("unlock\t0\t0\tA\\1\n");
            Expect(framer.Push(wire, 4).Count == 0 && framer.Pending == 4, "partial line buffered");
            var rest = wire.Skip(4).Concat(wire).ToArray();
            var lines = framer.Push(rest, rest.Length);
            Expect(lines.Count == 2 && lines.All(l => Command.Parse(l).Action == CommandAction.Unlock) && framer.Pending == 0, "reassembled lines");
            ExpectThrows(() => framer.Push(new byte[LineFramer.Limit + 1], LineFramer.Limit + 1), "unbounded input");
            var stream = new MemoryStream();
            Wire.Send(stream, new Reply { Kind = "ready" }.Format());
            Expect(Encoding.UTF8.GetString(stream.ToArray()) == "ready\t\n", "wire framing");

            // Deadlines and the exit combo.
            Expect(Helper.RemainingSeconds(DateTime.UtcNow.AddSeconds(-1)) == 0 && !Helper.RemainingSeconds(null).HasValue, "deadlines");
            Expect(Helper.RemainingSeconds(DateTime.UtcNow.AddSeconds(29.5)) == 30, "deadline rounds up");
            Expect(Combo.ExitHeld(new HashSet<int> { Native.VK_LCONTROL, Native.VK_RCONTROL }), "both ctrl keys exit");
            Expect(!Combo.ExitHeld(new HashSet<int> { Native.VK_LCONTROL }) && !Combo.ExitHeld(new HashSet<int> { Native.VK_LCONTROL, Native.VK_RCONTROL, 0x41 }) && !Combo.ExitHeld(new HashSet<int>()), "partial combos do not exit");

            // Raw Input ↔ hook attribution.
            var pending = new List<RawKey>
            {
                new RawKey { Device = (IntPtr)1, MakeCode = 0x1E, Extended = false, Up = false, VKey = 0x41 },
                new RawKey { Device = (IntPtr)2, MakeCode = 0x1D, Extended = true, Up = false, VKey = Native.VK_CONTROL },
                new RawKey { Device = (IntPtr)3, MakeCode = 0, Extended = false, Up = true, VKey = Native.VK_SHIFT },
            };
            Expect(Combo.Match(pending, 0x1E, false, false, 0x41) == 0, "scan code match");
            Expect(Combo.Match(pending, 0x1D, true, false, Native.VK_RCONTROL) == 1, "extended scan code match");
            Expect(Combo.Match(pending, 0x1D, false, false, Native.VK_LCONTROL) == 1, "falls back to unsided virtual key");
            Expect(Combo.Match(pending, 0x2A, false, true, Native.VK_LSHIFT) == 2, "zero make code matches by virtual key");
            Expect(Combo.Match(pending, 0x1E, false, true, 0x41) == -1, "direction must match");
            Expect(Combo.Match(new List<RawKey>(), 0x1E, false, false, 0x41) == -1, "nothing pending");

            Console.WriteLine("PASS: device paths, classification, automatic targeting, command validation, protocol round trips, transactional rollback, fragmented IPC, message limits, deadlines, exit combo, and key attribution.");
        }
    }
}
