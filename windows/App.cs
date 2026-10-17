// App lifecycle, control session with the elevated helper, tray icon, cleaning mode, automation, shortcuts and settings.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BrickMyBoard
{
    public enum Page { Keyboards, Cleaning, Automation, Shortcuts, Activity, Settings }

    public sealed class Shortcut
    {
        public uint Modifiers;   // MOD_* flags.
        public int Key;          // Virtual-key code.
        public string Character; // Display name of the key.

        public string Label
        {
            get
            {
                var parts = new List<string>();
                if ((Modifiers & Native.MOD_CONTROL) != 0) parts.Add("Ctrl");
                if ((Modifiers & Native.MOD_ALT) != 0) parts.Add("Alt");
                if ((Modifiers & Native.MOD_SHIFT) != 0) parts.Add("Shift");
                if ((Modifiers & Native.MOD_WIN) != 0) parts.Add("Win");
                parts.Add(Character);
                return string.Join("+", parts);
            }
        }
        public string Serialize() { return Modifiers + "," + Key + "," + Character; }
        public static Shortcut Deserialize(string text)
        {
            var parts = text.Split(',');
            uint modifiers; int key;
            if (parts.Length != 3 || !uint.TryParse(parts[0], out modifiers) || !int.TryParse(parts[1], out key) || key <= 0 || key > 254 || parts[2].Length == 0 || parts[2].Length > 12) return null;
            return new Shortcut { Modifiers = modifiers & 15, Key = key, Character = parts[2] };
        }
        public static Dictionary<string, Shortcut> Defaults()
        {
            return new Dictionary<string, Shortcut>
            {
                { "toggle", new Shortcut { Modifiers = Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_WIN, Key = (int)Keys.K, Character = "K" } },
                { "unlock", new Shortcut { Modifiers = Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_WIN, Key = (int)Keys.U, Character = "U" } },
            };
        }
    }

    public sealed class Activity
    {
        public DateTime Date = DateTime.Now;
        public string Text;
    }

    public static class Durations
    {
        public static readonly int[] Options = { 30, 120, 300, 900, 3600, 0 };
        public static string Name(int seconds)
        {
            switch (seconds)
            {
                case 30: return "30 seconds";
                case 120: return "2 minutes";
                case 300: return "5 minutes";
                case 900: return "15 minutes";
                case 3600: return "1 hour";
                default: return "Until unlocked";
            }
        }
        public static string Clock(int seconds) { return (seconds / 60) + ":" + (seconds % 60).ToString("00"); }
    }

    /// <summary>Receives WM_HOTKEY for the global shortcuts.</summary>
    sealed class HotkeyWindow : NativeWindow
    {
        public event Action<int> Pressed;
        public HotkeyWindow() { CreateHandle(new CreateParams { Caption = "BrickMyBoard shortcuts" }); }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY) { var handler = Pressed; if (handler != null) handler((int)m.WParam); }
            base.WndProc(ref m);
        }
    }

    public sealed class AppModel : IDisposable
    {
        public const string Version = "0.0.1";
        const string SettingsKey = @"Software\BrickMyBoard";
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);

        public readonly ApplicationContext Context = new ApplicationContext();
        public MainForm Window;
        public Page Page = Page.Keyboards;
        public List<Keyboard> Devices = new List<Keyboard>();
        public HashSet<string> Selected = new HashSet<string>();
        public Dictionary<string, LockState> Locks = new Dictionary<string, LockState>();
        public string Phase = "offline"; // offline, authorizing, ready, stopping
        public bool Pending;
        public string Issue;
        public List<Activity> Events = new List<Activity>();
        public int Duration = 300;
        public bool Automatic, AutoPaused, FloatWhenLocked, LaunchAtLogin, ShortcutsEnabled = true, Elevate = true;
        public string HelperLevel; // "elevated" or "standard" while a session is ready.
        public Dictionary<string, Shortcut> Shortcuts = Shortcut.Defaults();
        public string Recording;
        public string ShortcutNote = "";
        public bool Cleaning, CleaningPending;
        public int CleaningMinutes = 2;
        public DateTime CleaningEnds;
        public int Unattributed;
        /// <summary>Raised on the UI thread whenever state changes; the window redraws from the model.</summary>
        public event Action Changed;

        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 150 };
        readonly NotifyIcon tray = new NotifyIcon();
        readonly HotkeyWindow hotkeys = new HotkeyWindow();
        readonly List<CleaningOverlay> overlays = new List<CleaningOverlay>();
        readonly Queue<string> incoming = new Queue<string>();
        readonly object incomingLock = new object();
        readonly EventWaitHandle showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\BrickMyBoard.Show");
        NamedPipeServerStream server;
        Process task;
        Thread reader;
        bool connected, closed, stopping, initializedSelection, hotkeysRegistered;
        Command queued;
        DateTime lastTraffic, authorizationStarted, lastRefresh;
        HashSet<string> previousExternal = new HashSet<string>();
        string startupError;

        public bool Busy { get { return Phase == "authorizing" || Phase == "stopping" || Pending; } }
        public List<Keyboard> SelectedDevices { get { return Devices.Where(d => Selected.Contains(d.Id)).ToList(); } }
        public string PhaseText
        {
            get
            {
                switch (Phase)
                {
                    case "ready": return "Control authorized";
                    case "authorizing": return "Waiting for approval…";
                    case "stopping": return "Releasing keyboards…";
                    default: return Elevate && !Elevation.IsElevated() ? "Windows asks for approval on first lock" : "No approval prompt; helper runs as you";
                }
            }
        }
        public string AutomationDescription
        {
            get
            {
                if (!Automatic) return "Off";
                if (AutoPaused) return "Paused until the next external connection";
                if (Locks.Values.Any(l => l.Automatic)) return "Built-in keyboard locked automatically";
                if (!Devices.Any(d => !d.BuiltIn)) return "Waiting for an external keyboard";
                return "Ready for the next external connection";
            }
        }
        /// <summary>Longest remaining lock in seconds; null when some lock lasts until unlocked; no value when nothing is locked.</summary>
        public bool LongestRemaining(out int? remaining)
        {
            remaining = null;
            if (Locks.Count == 0) return false;
            if (Locks.Values.Any(l => !l.Remaining.HasValue)) return true;
            remaining = Locks.Values.Max(l => l.Remaining.Value);
            return true;
        }

        public AppModel()
        {
            LoadSettings();
            Window = new MainForm(this);
            Context.MainForm = null; // Closing the window keeps the app in the tray.
            tray.Icon = Window.Icon;
            tray.Visible = true;
            tray.DoubleClick += (s, e) => ShowWindow();
            tray.ContextMenuStrip = new ContextMenuStrip();
            tray.ContextMenuStrip.Opening += (s, e) => BuildTrayMenu();
            hotkeys.Pressed += id => { if (id == 1) ToggleSelectedShortcut(); else if (id == 2) UnlockAll(); };
            InstallHotKeys();
            RefreshDevices();
            Record("App opened. No keyboards are locked.");
            timer.Tick += (s, e) => Tick();
            timer.Start();
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            var waiter = new Thread(() => { while (showSignal.WaitOne()) { try { Window.BeginInvoke(new Action(ShowWindow)); } catch (Exception) { return; } } }) { IsBackground = true };
            waiter.Start();
            ShowWindow();
            UpdateTray();
        }

        public static void SignalExisting()
        {
            try { using (var signal = EventWaitHandle.OpenExisting("Local\\BrickMyBoard.Show")) signal.Set(); } catch (Exception) { }
        }

        void Notify() { var handler = Changed; if (handler != null) handler(); }

        // MARK: Settings

        void LoadSettings()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(SettingsKey))
            {
                if (key != null)
                {
                    Duration = key.GetValue("duration") as int? ?? 300;
                    if (!Durations.Options.Contains(Duration)) Duration = 300;
                    Automatic = (key.GetValue("automatic") as int? ?? 0) == 1;
                    var minutes = key.GetValue("cleaningMinutes") as int? ?? 2;
                    if (new[] { 1, 2, 5, 10 }.Contains(minutes)) CleaningMinutes = minutes;
                    FloatWhenLocked = (key.GetValue("floatWhenLocked") as int? ?? 0) == 1;
                    ShortcutsEnabled = (key.GetValue("shortcutsEnabled") as int? ?? 1) == 1;
                    Elevate = (key.GetValue("elevate") as int? ?? 1) == 1;
                    var saved = key.GetValue("shortcuts") as string;
                    if (saved != null)
                    {
                        var parsed = new Dictionary<string, Shortcut>();
                        foreach (var entry in saved.Split(';'))
                        {
                            var eq = entry.IndexOf('=');
                            if (eq <= 0) continue;
                            var action = entry.Substring(0, eq);
                            var shortcut = Shortcut.Deserialize(entry.Substring(eq + 1));
                            if ((action == "toggle" || action == "unlock") && shortcut != null) parsed[action] = shortcut;
                        }
                        Shortcuts = parsed;
                    }
                }
            }
            using (var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                LaunchAtLogin = run != null && run.GetValue("BrickMyBoard") != null;
        }
        void Save(string name, object value)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(SettingsKey))
            {
                if (value == null) key.DeleteValue(name, false);
                else key.SetValue(name, value is bool ? ((bool)value ? 1 : 0) : value);
            }
        }
        public void SetDuration(int value) { Duration = value; Save("duration", value); Notify(); }
        public void SetFloating(bool value) { FloatWhenLocked = value; Save("floatWhenLocked", value); UpdateWindowLevel(); Notify(); }
        public void SetElevate(bool value) { Elevate = value; Save("elevate", value); Record(value ? "Locks will ask for administrator approval." : "Locks will run without administrator approval. Keys on locked keyboards can still reach windows that run as administrator."); Notify(); }
        public void SetCleaningMinutes(int value) { CleaningMinutes = value; Save("cleaningMinutes", value); Notify(); }
        public void SetLogin(bool value)
        {
            try
            {
                using (var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (value) run.SetValue("BrickMyBoard", "\"" + Application.ExecutablePath + "\"");
                    else run.DeleteValue("BrickMyBoard", false);
                }
                LaunchAtLogin = value;
            }
            catch (Exception error) { Report(error.Message); }
            Notify();
        }
        public void SetAutomatic(bool value)
        {
            Automatic = value; AutoPaused = false;
            Save("automatic", value);
            Record(value ? "Automatic mode enabled." : "Automatic mode disabled.");
            if (value) AttemptAutomatic();
            else
            {
                var ids = Locks.Values.Where(l => l.Automatic).Select(l => l.Id).ToList();
                if (ids.Count > 0) Submit(new Command { Action = CommandAction.Unlock, Ids = ids });
            }
            Notify();
        }
        void UpdateWindowLevel() { if (Window != null && !Window.IsDisposed) Window.TopMost = FloatWhenLocked && Locks.Count > 0; }

        // MARK: Activity

        public void Record(string text)
        {
            Events.Insert(0, new Activity { Text = text });
            if (Events.Count > 50) Events.RemoveRange(50, Events.Count - 50);
            Notify();
        }
        public void Report(string error) { Issue = error; Record(error); }
        public void ClearEvents() { Events.Clear(); Notify(); }
        public void CopyDiagnostics()
        {
            var text = "BrickMyBoard " + Version + " for Windows\nWindows: " + Environment.OSVersion.VersionString + "\nSession: " + Phase +
                "\nUnattributed key events: " + Unattributed + "\nDevices:\n" +
                string.Join("\n", Devices.Select(d => d.Name + " · " + d.Connection + " · ID " + d.Id + " · vendor " + d.Vendor + " product " + d.Product)) +
                "\nActivity:\n" + string.Join("\n", Events.Select(e => e.Date.ToString("HH:mm:ss") + ": " + e.Text));
            try { Clipboard.SetText(text); Record("Diagnostics copied. No keystrokes are recorded."); }
            catch (Exception) { Report("Could not access the clipboard."); }
        }

        // MARK: Devices and selection

        public void RefreshDevices()
        {
            try
            {
                var fresh = BrickMyBoard.Devices.Enumerate();
                var changed = !fresh.SequenceEqual(Devices);
                Devices = fresh;
                Selected.IntersectWith(fresh.Select(d => d.Id));
                if (!initializedSelection)
                {
                    foreach (var d in fresh.Where(d => d.BuiltIn)) Selected.Add(d.Id);
                    initializedSelection = true;
                }
                var external = new HashSet<string>(fresh.Where(d => !d.BuiltIn).Select(d => d.Id));
                var connected = external.Except(previousExternal).Any();
                previousExternal = external;
                if (connected) { AutoPaused = false; AttemptAutomatic(); }
                if (changed) Notify();
            }
            catch (LockError error) { if (Issue == null) Report(error.Message); }
            UpdateTray();
        }
        public void ToggleSelection(string id) { if (!Selected.Remove(id)) Selected.Add(id); Notify(); }
        public void Select(IEnumerable<string> ids) { Selected = new HashSet<string>(ids); Notify(); }

        // MARK: Locking

        public void LockSelected() { Lock(Selected.ToList()); }
        public void Lock(List<string> ids)
        {
            var fresh = ids.Where(id => !Locks.ContainsKey(id)).ToList();
            if (fresh.Count == 0) return;
            Submit(new Command { Action = CommandAction.Lock, Ids = fresh, Seconds = Duration });
        }
        public void Unlock(List<string> ids)
        {
            if (ids.Any(id => Locks.ContainsKey(id) && Locks[id].Automatic)) AutoPaused = true;
            Submit(new Command { Action = CommandAction.Unlock, Ids = ids });
        }
        public void UnlockAll()
        {
            AutoPaused = Automatic;
            Record("Unlock All requested.");
            if (Phase == "authorizing") { EndSession(); return; }
            if (Phase == "ready") Submit(new Command { Action = CommandAction.UnlockAll });
        }
        void ToggleSelectedShortcut()
        {
            Record("Toggle selected shortcut received.");
            if (Phase == "authorizing") { EndSession(); return; }
            if (Selected.Any(id => Locks.ContainsKey(id))) Unlock(Selected.ToList()); else LockSelected();
        }
        void AttemptAutomatic()
        {
            if (!Automatic || AutoPaused || Busy) return;
            var targets = BrickMyBoard.Devices.AutoTargets(Devices).Where(id => !Locks.ContainsKey(id)).ToList();
            if (targets.Count == 0 || !Devices.Any(d => !d.BuiltIn && !Locks.ContainsKey(d.Id))) return;
            Submit(new Command { Action = CommandAction.Lock, Ids = targets, Seconds = Duration, Automatic = true });
        }
        public void ResumeAutomatic() { AutoPaused = false; AttemptAutomatic(); Notify(); }

        // MARK: Cleaning

        public void StartCleaning()
        {
            if (Busy || Cleaning || Devices.Count == 0) return;
            CleaningPending = true;
            Record("Cleaning mode requested.");
            var ids = Devices.Select(d => d.Id).Where(id => !Locks.ContainsKey(id)).ToList();
            if (ids.Count == 0) ShowCleaning();
            else Submit(new Command { Action = CommandAction.Lock, Ids = ids, Seconds = CleaningMinutes * 60 });
            Notify();
        }
        void ShowCleaning()
        {
            CleaningPending = false; Cleaning = true;
            CleaningEnds = DateTime.Now.AddMinutes(CleaningMinutes);
            foreach (var screen in Screen.AllScreens)
            {
                var overlay = new CleaningOverlay(this, screen);
                overlays.Add(overlay);
                overlay.Show();
            }
            if (overlays.Count > 0) { overlays[0].Activate(); Native.SetForegroundWindow(overlays[0].Handle); }
            Cursor.Hide();
            Native.SetThreadExecutionState(Native.ES_CONTINUOUS | Native.ES_DISPLAY_REQUIRED | Native.ES_SYSTEM_REQUIRED);
            Record("Cleaning mode on. Screens dimmed, keyboards locked.");
        }
        public void EndCleaning()
        {
            CleaningPending = false;
            if (!Cleaning) return;
            Cleaning = false;
            foreach (var overlay in overlays) overlay.Close();
            overlays.Clear();
            Cursor.Show();
            Native.SetThreadExecutionState(Native.ES_CONTINUOUS);
            if (Locks.Count > 0) UnlockAll();
            Record("Cleaning mode ended.");
        }

        // MARK: Control session

        void Submit(Command command)
        {
            if (Phase == "stopping" || (Busy && command.Action == CommandAction.Lock)) return;
            try
            {
                command.Validate();
                if (Phase == "ready")
                {
                    Wire.Send(server, command.Format());
                    Pending = true;
                }
                else if (command.Action == CommandAction.Lock)
                {
                    queued = command;
                    StartSession();
                }
            }
            catch (LockError error)
            {
                if (Phase == "ready") EndSession(); else if (task == null) CleanUp();
                Report(error.Message);
            }
            catch (Exception error)
            {
                if (Phase == "ready") EndSession(); else if (task == null) CleanUp();
                Report(error.Message);
            }
            Notify();
        }

        void StartSession()
        {
            var pipeName = "BrickMyBoard" + Guid.NewGuid().ToString("N");
            var security = new PipeSecurity();
            // Only this user's processes may connect; the elevated helper runs as the same user.
            security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.ReadWrite, AccessControlType.Allow));
            try
            {
                server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 65536, 65536, security);
            }
            catch (Exception) { CleanUp(); throw new LockError("Could not create the keyboard-control session."); }
            connected = false; closed = false; stopping = false; startupError = null;
            lock (incomingLock) incoming.Clear();
            Phase = "authorizing"; authorizationStarted = DateTime.UtcNow;
            var prompt = Elevate && !Elevation.IsElevated();
            Record(prompt ? "Waiting for administrator approval." : "Starting the keyboard helper.");
            var exe = Application.ExecutablePath;
            var launcher = new Thread(() =>
            {
                try
                {
                    var info = prompt
                        ? new ProcessStartInfo(exe, "--helper " + pipeName) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden }
                        : new ProcessStartInfo(exe, "--helper " + pipeName) { UseShellExecute = false, CreateNoWindow = true };
                    var process = Process.Start(info);
                    lock (incomingLock) task = process;
                }
                catch (Win32Exception error) { lock (incomingLock) startupError = error.NativeErrorCode == 1223 ? "" : error.Message; }
                catch (Exception error) { lock (incomingLock) startupError = error.Message; }
            }) { IsBackground = true };
            launcher.Start();
            server.BeginWaitForConnection(OnConnected, server);
            UpdateTray();
        }
        void OnConnected(IAsyncResult result)
        {
            var pipe = (NamedPipeServerStream)result.AsyncState;
            try { pipe.EndWaitForConnection(result); }
            catch (Exception) { return; }
            lock (incomingLock) { connected = true; lastTraffic = DateTime.UtcNow; }
            reader = new Thread(() =>
            {
                var framer = new LineFramer(); var bytes = new byte[8192];
                try
                {
                    while (true)
                    {
                        var count = pipe.Read(bytes, 0, bytes.Length);
                        if (count <= 0) break;
                        var lines = framer.Push(bytes, count);
                        lock (incomingLock) { foreach (var line in lines) incoming.Enqueue(line); lastTraffic = DateTime.UtcNow; }
                    }
                }
                catch (Exception) { }
                lock (incomingLock) closed = true;
            }) { IsBackground = true };
            reader.Start();
        }
        public void EndSession()
        {
            if (Phase == "offline") return;
            stopping = true; queued = null; Pending = false; Phase = "stopping";
            try { if (connected && server != null) Wire.Send(server, new Command { Action = CommandAction.Stop }.Format()); } catch (Exception) { }
            try { if (server != null) server.Dispose(); } catch (Exception) { } // The helper exits when its pipe closes; it cannot be terminated from here.
            server = null;
            Notify();
        }
        void Tick()
        {
            if (Cleaning && DateTime.Now > CleaningEnds.AddSeconds(2)) EndCleaning();
            if ((DateTime.UtcNow - lastRefresh).TotalSeconds >= 2) { lastRefresh = DateTime.UtcNow; RefreshDevices(); }
            if (Phase == "offline") { Notify(); return; }
            List<string> lines; bool wasClosed, isConnected; string failure; Process process;
            lock (incomingLock) { lines = incoming.ToList(); incoming.Clear(); wasClosed = closed; isConnected = connected; failure = startupError; process = task; }
            if (failure != null)
            {
                CleanUp();
                AutoPaused = Automatic;
                if (failure.Length == 0) Record("Authorization canceled. Nothing locked.");
                else Report(failure.Length == 0 ? "The helper could not start." : failure);
                Notify(); return;
            }
            if (!stopping)
            {
                foreach (var line in lines)
                {
                    try { Handle(Reply.Parse(line)); }
                    catch (LockError error) { EndSession(); Report(error.Message); break; }
                }
            }
            if (isConnected && Phase == "ready" && (DateTime.UtcNow - lastTraffic).TotalSeconds > 5)
            {
                EndSession(); Report("The keyboard helper stopped responding. Its connection has been closed to release the keyboards.");
            }
            if (Phase == "authorizing" && (DateTime.UtcNow - authorizationStarted).TotalSeconds > 120) EndSession();
            var finished = wasClosed || (process != null && process.HasExited) || (Phase == "stopping" && !isConnected);
            if (finished && (process == null || process.HasExited || wasClosed))
            {
                var failed = process != null && process.HasExited && process.ExitCode != 0 && !stopping;
                CleanUp();
                Record("Authorization ended. All keyboards released.");
                if (failed) { AutoPaused = Automatic; Report("The helper stopped unexpectedly. Run BrickMyBoard.exe --check from a terminal for details."); }
            }
            Notify();
        }
        void Handle(Reply reply)
        {
            if (reply.Kind == "ready")
            {
                Phase = "ready"; HelperLevel = reply.Message == "elevated" ? "elevated" : "standard";
                Record(HelperLevel == "elevated" ? "Keyboard control authorized for this app session." : "Keyboard control started without administrator approval. Windows that run as administrator stay reachable from locked keyboards.");
                if (queued != null) { var command = queued; queued = null; Wire.Send(server, command.Format()); Pending = true; }
            }
            else
            {
                var oldIds = new HashSet<string>(Locks.Keys); var newIds = new HashSet<string>(reply.Locks.Select(l => l.Id));
                foreach (var id in newIds.Except(oldIds)) Record("Locked " + NameOf(id) + ".");
                foreach (var id in oldIds.Except(newIds)) Record("Unlocked " + NameOf(id) + ".");
                Locks = reply.Locks.ToDictionary(l => l.Id);
                Pending = false;
                if (reply.Kind == "error") { AutoPaused = Automatic; CleaningPending = false; Report(reply.Message ?? "The keyboard action failed."); }
                else if (reply.Message != null) Record(reply.Message);
                if (CleaningPending && Devices.Count > 0 && Devices.All(d => Locks.ContainsKey(d.Id))) ShowCleaning();
                if (Cleaning && Locks.Count == 0) EndCleaning();
            }
            UpdateTray(); UpdateWindowLevel();
        }
        string NameOf(string id)
        {
            var device = Devices.FirstOrDefault(d => d.Id == id);
            return device == null ? "keyboard" : device.DisplayName;
        }
        void CleanUp()
        {
            try { if (server != null) server.Dispose(); } catch (Exception) { }
            server = null; task = null; queued = null; connected = false; closed = false; stopping = false; startupError = null;
            lock (incomingLock) incoming.Clear();
            Locks = new Dictionary<string, LockState>(); Phase = "offline"; Pending = false; HelperLevel = null;
            EndCleaning();
            UpdateTray(); UpdateWindowLevel();
        }
        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Suspend) { AutoPaused = Automatic; EndSession(); } }
        void OnSessionSwitch(object sender, SessionSwitchEventArgs e) { if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.ConsoleDisconnect) { AutoPaused = Automatic; EndSession(); } }

        // MARK: Window and tray

        public void ShowWindow()
        {
            if (Window == null || Window.IsDisposed) Window = new MainForm(this);
            Window.Show();
            if (Window.WindowState == FormWindowState.Minimized) Window.WindowState = FormWindowState.Normal;
            Window.Activate();
            Native.SetForegroundWindow(Window.Handle);
        }
        public void OpenSettings() { Page = Page.Settings; ShowWindow(); Notify(); }
        public void Quit() { Context.ExitThread(); }
        void UpdateTray()
        {
            tray.Text = "BrickMyBoard · " + Locks.Count + " locked";
        }
        void BuildTrayMenu()
        {
            RefreshDevices();
            var menu = tray.ContextMenuStrip;
            menu.Items.Clear();
            menu.Items.Add("Open BrickMyBoard", null, (s, e) => ShowWindow());
            menu.Items.Add(new ToolStripSeparator());
            if (Devices.Count == 0) menu.Items.Add(new ToolStripMenuItem("No keyboards found") { Enabled = false });
            foreach (var device in Devices)
            {
                var id = device.Id;
                var locked = Locks.ContainsKey(id);
                var item = new ToolStripMenuItem("Lock " + device.DisplayName) { Checked = locked, Enabled = locked || !Busy };
                item.Click += (s, e) => { if (locked) Unlock(new List<string> { id }); else Lock(new List<string> { id }); };
                menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Lock selected", null, (s, e) => LockSelected()) { Enabled = !Busy && Selected.Any(id => !Locks.ContainsKey(id)) });
            menu.Items.Add(new ToolStripMenuItem("Unlock all", null, (s, e) => UnlockAll()) { Enabled = Locks.Count > 0 || Phase == "authorizing" });
            menu.Items.Add(new ToolStripMenuItem("Clean for " + CleaningMinutes + " min", null, (s, e) => StartCleaning()) { Enabled = !Busy && !Cleaning && Devices.Count > 0 });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("End authorization", null, (s, e) => EndSession()) { Enabled = Phase != "offline" });
            menu.Items.Add("Quit", null, (s, e) => Quit());
        }

        // MARK: Shortcuts

        void UnregisterHotKeys()
        {
            if (!hotkeysRegistered) return;
            Native.UnregisterHotKey(hotkeys.Handle, 1); Native.UnregisterHotKey(hotkeys.Handle, 2);
            hotkeysRegistered = false;
        }
        bool Register(Dictionary<string, Shortcut> bindings)
        {
            foreach (var action in new[] { "toggle", "unlock" })
            {
                Shortcut shortcut;
                if (!bindings.TryGetValue(action, out shortcut)) continue;
                if (!Native.RegisterHotKey(hotkeys.Handle, action == "toggle" ? 1 : 2, shortcut.Modifiers | Native.MOD_NOREPEAT, (uint)shortcut.Key)) { hotkeysRegistered = true; UnregisterHotKeys(); return false; }
                hotkeysRegistered = true;
            }
            return true;
        }
        void InstallHotKeys()
        {
            UnregisterHotKeys();
            if (ShortcutsEnabled && !Register(Shortcuts)) ShortcutNote = "A shortcut is already used by another app. Choose a different combination.";
        }
        public void SetShortcutsEnabled(bool value) { ShortcutsEnabled = value; Save("shortcutsEnabled", value); ShortcutNote = ""; InstallHotKeys(); Notify(); }
        public void BeginRecording(string action)
        {
            Recording = action; ShortcutNote = "Press a key with at least two modifiers. Escape cancels; Delete clears.";
            UnregisterHotKeys();
            Notify();
        }
        /// <summary>Handles a key press while recording. Returns true when the key was consumed.</summary>
        public bool Capture(KeyEventArgs e)
        {
            var action = Recording;
            if (action == null) return false;
            if (e.KeyCode == Keys.Escape) { CancelRecording(); return true; }
            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu || e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin) return true;
            var changed = new Dictionary<string, Shortcut>(Shortcuts);
            if (e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back) changed.Remove(action);
            else
            {
                uint modifiers = 0;
                if (e.Control) modifiers |= Native.MOD_CONTROL;
                if (e.Alt) modifiers |= Native.MOD_ALT;
                if (e.Shift) modifiers |= Native.MOD_SHIFT;
                if ((GetAsyncKeyState(Native.VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(Native.VK_RWIN) & 0x8000) != 0) modifiers |= Native.MOD_WIN;
                var count = new[] { Native.MOD_CONTROL, Native.MOD_ALT, Native.MOD_SHIFT, Native.MOD_WIN }.Count(m => (modifiers & m) != 0);
                if (count < 2) return true;
                var character = KeyName(e.KeyCode);
                if (character == null) return true;
                if (changed.Any(p => p.Key != action && p.Value.Key == (int)e.KeyCode && p.Value.Modifiers == modifiers))
                {
                    ShortcutNote = "That shortcut is already assigned to the other action."; Notify(); return true;
                }
                changed[action] = new Shortcut { Modifiers = modifiers, Key = (int)e.KeyCode, Character = character };
            }
            Recording = null;
            if (!ShortcutsEnabled || Register(changed))
            {
                Shortcuts = changed;
                Save("shortcuts", string.Join(";", changed.Select(p => p.Key + "=" + p.Value.Serialize())));
                ShortcutNote = "Shortcut saved.";
            }
            else { InstallHotKeys(); ShortcutNote = "That shortcut is in use. Your previous shortcuts are unchanged."; }
            Notify();
            return true;
        }
        static string KeyName(Keys key)
        {
            if (key >= Keys.A && key <= Keys.Z) return key.ToString();
            if (key >= Keys.D0 && key <= Keys.D9) return key.ToString().Substring(1);
            if (key >= Keys.F1 && key <= Keys.F24) return key.ToString();
            switch (key)
            {
                case Keys.Space: return "Space";
                case Keys.Enter: return "Enter";
                case Keys.Tab: return "Tab";
                case Keys.Left: return "←";
                case Keys.Right: return "→";
                case Keys.Up: return "↑";
                case Keys.Down: return "↓";
                case Keys.Home: return "Home";
                case Keys.End: return "End";
                case Keys.PageUp: return "PgUp";
                case Keys.PageDown: return "PgDn";
                case Keys.Insert: return "Ins";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                case Keys.OemPipe: return "\\";
                case Keys.Oemtilde: return "`";
                default: return null;
            }
        }
        public void CancelRecording() { Recording = null; ShortcutNote = ""; InstallHotKeys(); Notify(); }
        public void ResetShortcuts() { Recording = null; Shortcuts = Shortcut.Defaults(); Save("shortcuts", null); ShortcutNote = ""; InstallHotKeys(); Notify(); }

        public void Dispose()
        {
            timer.Stop();
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            EndSession(); CleanUp(); UnregisterHotKeys();
            tray.Visible = false; tray.Dispose();
            hotkeys.DestroyHandle();
            showSignal.Dispose();
        }
    }
}
