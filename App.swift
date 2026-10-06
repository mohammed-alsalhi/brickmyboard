import AppKit
import SwiftUI
import Carbon
import ServiceManagement
import IOKit.hidsystem
import Darwin

enum Page: String, CaseIterable, Identifiable {
    case keyboards = "Keyboards", cleaning = "Cleaning", automation = "Automation", shortcuts = "Shortcuts", activity = "Activity", settings = "Settings"
    var id: String { rawValue }
    var symbol: String {
        switch self { case .keyboards: "keyboard"; case .cleaning: "sparkles"; case .automation: "bolt"; case .shortcuts: "command"; case .activity: "clock.arrow.circlepath"; case .settings: "gearshape" }
    }
}
struct Shortcut: Codable, Equatable {
    let key: UInt32
    let modifiers: UInt32
    let character: String
    var label: String {
        [(UInt32(controlKey), "⌃"), (UInt32(optionKey), "⌥"), (UInt32(shiftKey), "⇧"), (UInt32(cmdKey), "⌘")]
            .filter { modifiers & $0.0 != 0 }.map(\.1).joined() + character
    }
    static let defaults = ["toggle": Shortcut(key: UInt32(kVK_ANSI_K), modifiers: UInt32(controlKey | optionKey | cmdKey), character: "K"),
                           "unlock": Shortcut(key: UInt32(kVK_ANSI_U), modifiers: UInt32(controlKey | optionKey | cmdKey), character: "U")]
}
struct Activity: Identifiable {
    let id = UUID()
    let date = Date()
    let text: String
}

@MainActor
final class AppModel: NSObject, ObservableObject, NSApplicationDelegate, NSWindowDelegate {
    @Published var page: Page = .keyboards
    @Published var devices: [Keyboard] = []
    @Published var selected: Set<UInt64> = []
    @Published var locks: [UInt64: LockState] = [:]
    @Published var permitted = false
    @Published var phase = "offline"
    @Published var pending = false
    @Published var issue: String?
    @Published var events: [Activity] = []
    @Published var duration = 300
    @Published var automatic = false
    @Published var autoPaused = false
    @Published var floatWhenLocked = false
    @Published var launchAtLogin = false
    @Published var shortcuts = Shortcut.defaults
    @Published var shortcutsEnabled = true
    @Published var recording: String?
    @Published var shortcutNote = ""
    @Published var permissionRequested = false
    @Published var cleaning = false
    @Published var cleaningPending = false
    @Published var cleaningMinutes = 2
    @Published var cleaningEnds = Date()
    private var overlays: [NSWindow] = []
    private var cleaningActivity: NSObjectProtocol?
    private var window: NSWindow!
    private var statusItem: NSStatusItem!
    private let popover = NSPopover()
    private var timer: Timer?
    private var task: Process?
    private var output: Pipe?
    private var directory: URL?
    private var listener: Int32 = -1
    private var connection: Int32 = -1
    private var incoming = Data()
    private var queued: Command?
    private var lastRefresh = ContinuousClock.now
    private var previousExternal: Set<UInt64> = []
    private var initializedSelection = false
    private var hotKeys: [EventHotKeyRef] = []
    private var hotKeyHandler: EventHandlerRef?
    private var keyMonitor: Any?
    private var stopping = false
    private var lastTraffic = ContinuousClock.now
    private var authorizationStarted = ContinuousClock.now

    var busy: Bool { phase == "authorizing" || phase == "stopping" || pending }
    var selectedDevices: [Keyboard] { devices.filter { selected.contains($0.id) } }
    var automationDescription: String {
        if !automatic { return "Off" }
        if autoPaused { return "Paused until the next external connection" }
        if locks.values.contains(where: \.automatic) { return "Built-in keyboard locked automatically" }
        if !devices.contains(where: { !$0.builtIn }) { return "Waiting for an external keyboard" }
        return "Ready for the next external connection"
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        let defaults = UserDefaults.standard
        if defaults.object(forKey: "duration") != nil { duration = defaults.integer(forKey: "duration") }
        if ![30, 120, 300, 900, 3600, 0].contains(duration) { duration = 300 }
        automatic = defaults.bool(forKey: "automatic")
        if [1, 2, 5, 10].contains(defaults.integer(forKey: "cleaningMinutes")) { cleaningMinutes = defaults.integer(forKey: "cleaningMinutes") }
        floatWhenLocked = defaults.bool(forKey: "floatWhenLocked")
        shortcutsEnabled = defaults.object(forKey: "shortcutsEnabled") == nil || defaults.bool(forKey: "shortcutsEnabled")
        if let data = defaults.data(forKey: "shortcuts"), let saved = try? JSONDecoder().decode([String: Shortcut].self, from: data) {
            shortcuts = saved.filter { ["toggle", "unlock"].contains($0.key) && $0.value.key <= 127 }
        }
        launchAtLogin = SMAppService.mainApp.status == .enabled
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 960, height: 690),
                          styleMask: [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView], backing: .buffered, defer: false)
        window.title = "BrickMyBoard"
        window.minSize = NSSize(width: 850, height: 620)
        window.titlebarAppearsTransparent = true
        window.titleVisibility = .hidden
        window.delegate = self
        window.isReleasedWhenClosed = false
        window.contentView = NSHostingView(rootView: RootView(model: self))
        window.setFrameAutosaveName("BrickMyBoardMain")
        buildMainMenu()
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusItem.button?.target = self
        statusItem.button?.action = #selector(togglePopover)
        popover.behavior = .transient
        popover.contentViewController = NSHostingController(rootView: MenuBarView(model: self))
        updateMenu()
        installHotKeys()
        refreshDevices()
        record("App opened. No keyboards are locked.")
        timer = Timer(timeInterval: 0.15, target: self, selector: #selector(tick), userInfo: nil, repeats: true)
        RunLoop.main.add(timer!, forMode: .common)
        window.center()
        showWindow()
        let nc = NSWorkspace.shared.notificationCenter
        nc.addObserver(self, selector: #selector(willSleep), name: NSWorkspace.willSleepNotification, object: nil)
        nc.addObserver(self, selector: #selector(willSleep), name: NSWorkspace.sessionDidResignActiveNotification, object: nil)
    }

    func record(_ text: String) {
        events.insert(Activity(text: text), at: 0)
        if events.count > 50 { events.removeLast(events.count - 50) }
    }
    func report(_ error: String) { issue = error; record(error) }
    func setDuration(_ value: Int) { duration = value; UserDefaults.standard.set(value, forKey: "duration") }
    func setFloating(_ value: Bool) { floatWhenLocked = value; UserDefaults.standard.set(value, forKey: "floatWhenLocked"); updateWindowLevel() }
    private func updateWindowLevel() { window?.level = floatWhenLocked && !locks.isEmpty ? .floating : .normal }
    func setLogin(_ value: Bool) {
        do {
            if value { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
            launchAtLogin = SMAppService.mainApp.status == .enabled
            if value && !launchAtLogin { report("Approve BrickMyBoard in System Settings → General → Login Items.") }
        } catch { report(String(describing: error)) }
    }
    func setAutomatic(_ value: Bool) {
        automatic = value; autoPaused = false
        UserDefaults.standard.set(value, forKey: "automatic")
        record(value ? "Automatic mode enabled." : "Automatic mode disabled.")
        if value { attemptAutomatic() }
        else {
            let ids = locks.values.filter(\.automatic).map(\.id)
            if !ids.isEmpty { submit(Command(action: .unlock, ids: ids)) }
        }
    }
    func setCleaningMinutes(_ value: Int) { cleaningMinutes = value; UserDefaults.standard.set(value, forKey: "cleaningMinutes") }
    func startCleaning() {
        guard permitted, !busy, !cleaning, !devices.isEmpty else { return }
        cleaningPending = true
        record("Cleaning mode requested.")
        let ids = devices.map(\.id).filter { locks[$0] == nil }
        if ids.isEmpty { showCleaning() } else { submit(Command(action: .lock, ids: ids, seconds: cleaningMinutes * 60)) }
    }
    private func showCleaning() {
        cleaningPending = false; cleaning = true
        cleaningEnds = Date().addingTimeInterval(TimeInterval(cleaningMinutes * 60))
        popover.performClose(nil)
        NSApp.activate(ignoringOtherApps: true)
        for screen in NSScreen.screens {
            let overlay = NSWindow(contentRect: screen.frame, styleMask: .borderless, backing: .buffered, defer: false)
            overlay.level = .screenSaver; overlay.backgroundColor = .black; overlay.isOpaque = true; overlay.isReleasedWhenClosed = false
            overlay.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
            overlay.contentView = NSHostingView(rootView: CleaningOverlay(model: self))
            overlay.setFrame(screen.frame, display: true)
            overlay.alphaValue = 0; overlay.orderFrontRegardless(); overlay.animator().alphaValue = 1
            overlays.append(overlay)
        }
        NSCursor.hide()
        NSApp.presentationOptions = [.hideDock, .hideMenuBar, .disableProcessSwitching]
        cleaningActivity = ProcessInfo.processInfo.beginActivity(options: [.idleDisplaySleepDisabled, .userInitiated], reason: "Cleaning mode")
        record("Cleaning mode on. Screens dimmed, keyboards locked.")
    }
    func endCleaning() {
        cleaningPending = false
        guard cleaning else { return }
        cleaning = false
        for overlay in overlays { overlay.orderOut(nil) }
        overlays = []
        NSCursor.unhide()
        NSApp.presentationOptions = []
        if let cleaningActivity { ProcessInfo.processInfo.endActivity(cleaningActivity) }
        cleaningActivity = nil
        if !locks.isEmpty { unlockAll() }
        record("Cleaning mode ended.")
    }
    func resumeAutomatic() { autoPaused = false; attemptAutomatic() }
    private func attemptAutomatic() {
        guard automatic, !autoPaused, permitted, !busy else { return }
        let targets = autoTargets(devices).filter { locks[$0] == nil }
        guard !targets.isEmpty, devices.contains(where: { !$0.builtIn && locks[$0.id] == nil }) else { return }
        submit(Command(action: .lock, ids: targets, seconds: duration, automatic: true))
    }
    @objc func refreshDevices() {
        let hadPermission = permitted
        permitted = IOHIDCheckAccess(kIOHIDRequestTypeListenEvent) == kIOHIDAccessTypeGranted
        do {
            let fresh = try keyboards()
            if fresh != devices { devices = fresh }
            selected.formIntersection(Set(fresh.map(\.id)))
            if !initializedSelection {
                selected = Set(fresh.filter(\.builtIn).map(\.id))
                initializedSelection = true
            }
            let external = Set(fresh.filter { !$0.builtIn }.map(\.id))
            let connected = !external.subtracting(previousExternal).isEmpty
            previousExternal = external
            if connected { autoPaused = false }
            if connected || (!hadPermission && permitted) { attemptAutomatic() }
        } catch { if issue == nil { report(String(describing: error)) } }
        updateMenu()
    }
    func setUpPermissions() {
        permissionRequested = true
        _ = IOHIDRequestAccess(kIOHIDRequestTypeListenEvent)
        if IOHIDCheckAccess(kIOHIDRequestTypeListenEvent) != kIOHIDAccessTypeGranted {
            NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ListenEvent")!)
        }
        refreshDevices()
    }
    /// Ad-hoc builds are tied to their exact binary, so after a rebuild the Settings switch can look on
    /// while macOS denies this build. Clearing the app's entry lets macOS ask again for the current build.
    func resetPermission() {
        let reset = Process()
        reset.executableURL = URL(fileURLWithPath: "/usr/bin/tccutil")
        reset.arguments = ["reset", "ListenEvent", Bundle.main.bundleIdentifier ?? "com.mohammedalsalhi.BrickMyBoard"]
        let pipe = Pipe(); reset.standardOutput = pipe; reset.standardError = pipe
        do { try reset.run(); reset.waitUntilExit() } catch { report("Could not reset the permission: \(error)"); return }
        guard reset.terminationStatus == 0 else {
            report("macOS would not reset the permission. In Input Monitoring, select BrickMyBoard, click −, then turn it on again.")
            NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ListenEvent")!)
            return
        }
        record("Input Monitoring entry reset for this build.")
        setUpPermissions()
    }
    func relaunch() {
        let configuration = NSWorkspace.OpenConfiguration(); configuration.createsNewApplicationInstance = true
        let url = Bundle.main.bundleURL
        endSession()
        NSWorkspace.shared.openApplication(at: url, configuration: configuration) { _, _ in
            DispatchQueue.main.async { NSApp.terminate(nil) }
        }
    }
    func toggleSelection(_ id: UInt64) { if selected.contains(id) { selected.remove(id) } else { selected.insert(id) }; updateMenu() }
    @objc func lockSelected() { lock(ids: Array(selected)) }
    func lock(ids: [UInt64]) {
        let fresh = ids.filter { locks[$0] == nil }
        guard !fresh.isEmpty else { return }
        submit(Command(action: .lock, ids: fresh, seconds: duration))
    }
    func unlock(ids: [UInt64]) {
        if ids.contains(where: { locks[$0]?.automatic == true }) { autoPaused = true }
        submit(Command(action: .unlock, ids: ids))
    }
    @objc func unlockAll() {
        autoPaused = automatic
        record("Unlock All requested.")
        if phase == "authorizing" { endSession(); return }
        if phase == "ready" { submit(Command(action: .unlockAll)) }
    }
    private func submit(_ command: Command) {
        guard phase != "stopping", !(busy && command.action == .lock) else { return }
        do {
            try command.validate()
            if phase == "ready" {
                try sendMessage(command, to: connection)
                pending = true
            } else if command.action == .lock {
                try requireInputMonitoring(IOHIDCheckAccess(kIOHIDRequestTypeListenEvent))
                queued = command
                try startSession()
            }
        } catch {
            if phase == "ready" { endSession() }
            else if task == nil { cleanUp() }
            report(String(describing: error))
        }
    }
    private func startSession() throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("bk." + UUID().uuidString.prefix(8))
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: false, attributes: [.posixPermissions: 0o700])
        directory = folder
        var address = try unixAddress(folder.appendingPathComponent("control").path)
        listener = socket(AF_UNIX, SOCK_STREAM, 0)
        guard listener >= 0, withSocketAddress(&address, { Darwin.bind(listener, $0, $1) }) == 0,
              listen(listener, 1) == 0, fcntl(listener, F_SETFL, O_NONBLOCK) == 0,
              let executable = Bundle.main.executableURL?.path else {
            cleanUp(); throw LockError.message("Could not create the keyboard-control session.")
        }
        let process = Process()
        let socketPath = folder.appendingPathComponent("control").path
        if Self.touchIDSudo {
            // pam_tid shows the Touch ID sheet; with no terminal there is no password fallback.
            process.executableURL = URL(fileURLWithPath: "/usr/bin/sudo")
            process.arguments = [executable, "--helper", socketPath]
            process.standardInput = FileHandle.nullDevice
        } else {
            process.executableURL = URL(fileURLWithPath: "/usr/bin/osascript")
            process.arguments = ["-e", """
            on run argv
                do shell script (quoted form of (item 1 of argv) & " --helper " & quoted form of (item 2 of argv)) with administrator privileges
            end run
            """, executable, socketPath]
        }
        let pipe = Pipe(); process.standardOutput = pipe; process.standardError = pipe
        do { try process.run() } catch { cleanUp(); throw error }
        task = process; output = pipe; phase = "authorizing"; stopping = false
        authorizationStarted = .now
        record(Self.touchIDSudo ? "Waiting for Touch ID." : "Waiting for administrator authorization.")
        updateMenu()
    }
    static var touchIDSudo: Bool {
        ["/etc/pam.d/sudo_local", "/etc/pam.d/sudo"].contains { path in
            ((try? String(contentsOfFile: path, encoding: .utf8)) ?? "").split(separator: "\n")
                .contains { $0.hasPrefix("auth") && $0.contains("pam_tid.so") }
        }
    }
    @objc func endSession() {
        guard task != nil else { return }
        stopping = true; queued = nil; pending = false; phase = "stopping"
        if connection >= 0 { shutdown(connection, SHUT_RDWR) }
        if listener >= 0 { close(listener); listener = -1 }
        if let directory { unlink(directory.appendingPathComponent("control").path) }
        if connection < 0 { task?.terminate() }
    }
    @objc private func tick() {
        if cleaning && Date() > cleaningEnds.addingTimeInterval(2) { endCleaning() }
        if ContinuousClock.now - lastRefresh >= .seconds(2) { lastRefresh = .now; refreshDevices() }
        guard let process = task else { return }
        if connection < 0 && listener >= 0 {
            connection = accept(listener, nil, nil)
            if connection >= 0 {
                var uid: uid_t = 0; var gid: gid_t = 0
                guard getpeereid(connection, &uid, &gid) == 0, uid == 0 else { close(connection); connection = -1; return }
                configureSocket(connection)
                guard fcntl(connection, F_SETFL, O_NONBLOCK) == 0 else { endSession(); return }
                close(listener); listener = -1
                lastTraffic = .now
            }
        }
        if connection >= 0 && !stopping {
            var bytes = [UInt8](repeating: 0, count: 8192)
            let count = read(connection, &bytes, bytes.count)
            if count > 0 {
                lastTraffic = .now
                do {
                    for reply in try receiveMessages(Reply.self, buffer: &incoming, data: Data(bytes.prefix(count))) { try handle(reply) }
                } catch { endSession(); report(String(describing: error)) }
            } else if count == 0 || (count < 0 && errno != EAGAIN && errno != EINTR) {
                endSession()
            }
        }
        if phase == "ready" && ContinuousClock.now - lastTraffic > .seconds(5) {
            endSession(); report("The keyboard helper stopped responding. Its connection has been closed to release the keyboards.")
        }
        if phase == "authorizing" && ContinuousClock.now - authorizationStarted > .seconds(120) { endSession() }
        if !process.isRunning {
            let text = output.map { String(decoding: $0.fileHandleForReading.readDataToEndOfFile(), as: UTF8.self) } ?? ""
            let failed = process.terminationStatus != 0 && !stopping
            cleanUp()
            record("Authorization ended. All keyboards released.")
            if failed {
                autoPaused = automatic
                if !text.contains("User canceled") && !text.contains("(-128)") { report(text.isEmpty ? "The helper could not start." : text) }
            }
        }
    }
    private func handle(_ reply: Reply) throws {
        guard ["ready", "state", "error"].contains(reply.kind), reply.locks.count <= 32,
              Set(reply.locks.map(\.id)).count == reply.locks.count else { throw LockError.message("Invalid helper response.") }
        if reply.kind == "ready" {
            phase = "ready"; record("Keyboard control authorized for this app session.")
            if let command = queued {
                queued = nil; try sendMessage(command, to: connection); pending = true
            }
        } else {
            let oldIDs = Set(locks.keys), newIDs = Set(reply.locks.map(\.id))
            for id in newIDs.subtracting(oldIDs) { record("Locked \(devices.first { $0.id == id }?.name ?? "keyboard").") }
            for id in oldIDs.subtracting(newIDs) { record("Unlocked \(devices.first { $0.id == id }?.name ?? "keyboard").") }
            locks = Dictionary(uniqueKeysWithValues: reply.locks.map { ($0.id, $0) })
            pending = false
            if reply.kind == "error" { autoPaused = automatic; cleaningPending = false; report(reply.message ?? "The keyboard action failed.") }
            else if let message = reply.message { record(message) }
            if cleaningPending && !devices.isEmpty && devices.allSatisfy({ locks[$0.id] != nil }) { showCleaning() }
            if cleaning && locks.isEmpty { endCleaning() }
        }
        updateMenu(); updateWindowLevel()
    }
    private func cleanUp() {
        if connection >= 0 { shutdown(connection, SHUT_RDWR); close(connection); connection = -1 }
        if listener >= 0 { close(listener); listener = -1 }
        if let directory { try? FileManager.default.removeItem(at: directory) }
        directory = nil; task = nil; output = nil; queued = nil; incoming = Data()
        locks = [:]; phase = "offline"; pending = false; stopping = false
        endCleaning()
        updateMenu(); updateWindowLevel()
    }
    @objc private func willSleep() { autoPaused = automatic; endSession() }
    @objc func showWindow() { popover.performClose(nil); window.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true) }
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool { showWindow(); return true }
    func applicationDidBecomeActive(_ notification: Notification) { if window != nil { refreshDevices() } }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
    func applicationWillTerminate(_ notification: Notification) {
        timer?.invalidate(); endSession(); cleanUp(); stopRecording(); unregisterHotKeys()
        if let hotKeyHandler { RemoveEventHandler(hotKeyHandler) }
    }

    private func buildMainMenu() {
        let main = NSMenu()
        let appItem = NSMenuItem(); let appMenu = NSMenu()
        appMenu.addItem(withTitle: "About BrickMyBoard", action: #selector(NSApplication.orderFrontStandardAboutPanel(_:)), keyEquivalent: "")
        appMenu.addItem(.separator())
        let preferences = appMenu.addItem(withTitle: "Settings…", action: #selector(openSettings), keyEquivalent: ","); preferences.target = self
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: "Hide BrickMyBoard", action: #selector(NSApplication.hide(_:)), keyEquivalent: "h")
        appMenu.addItem(withTitle: "Quit BrickMyBoard", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        appItem.submenu = appMenu; main.addItem(appItem)
        let windowItem = NSMenuItem(); windowItem.title = "Window"; let windowMenu = NSMenu(title: "Window")
        let show = windowMenu.addItem(withTitle: "Show Keyboards", action: #selector(showWindow), keyEquivalent: "0"); show.target = self
        windowMenu.addItem(withTitle: "Close Window", action: #selector(NSWindow.performClose(_:)), keyEquivalent: "w")
        windowItem.submenu = windowMenu; main.addItem(windowItem)
        NSApp.mainMenu = main
    }
    @objc private func openSettings() { page = .settings; showWindow() }
    @objc private func togglePopover() {
        guard let button = statusItem.button else { return }
        if popover.isShown { popover.performClose(nil) }
        else { refreshDevices(); popover.show(relativeTo: button.bounds, of: button, preferredEdge: .minY); NSApp.activate(ignoringOtherApps: true) }
    }
    private func updateMenu() {
        guard statusItem != nil else { return }
        statusItem.button?.image = NSImage(systemSymbolName: locks.isEmpty ? "keyboard" : "keyboard.badge.ellipsis", accessibilityDescription: "BrickMyBoard")
        statusItem.button?.title = locks.isEmpty ? "" : " \(locks.count)"
        statusItem.button?.toolTip = "BrickMyBoard · \(locks.count) locked"
    }

    private func unregisterHotKeys() { for key in hotKeys { UnregisterEventHotKey(key) }; hotKeys = [] }
    private func register(_ bindings: [String: Shortcut]) -> Bool {
        for action in ["toggle", "unlock"] {
            guard let shortcut = bindings[action] else { continue }
            var reference: EventHotKeyRef?
            let id = EventHotKeyID(signature: 0x424B4252, id: action == "toggle" ? 1 : 2)
            let result = RegisterEventHotKey(shortcut.key, shortcut.modifiers, id, GetEventDispatcherTarget(), 0, &reference)
            guard result == noErr, let reference else { unregisterHotKeys(); return false }
            hotKeys.append(reference)
        }
        return true
    }
    private func installHotKeys() {
        if hotKeyHandler == nil {
            var type = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
            let result = InstallEventHandler(GetEventDispatcherTarget(), { _, event, context in
                guard let event, let context else { return OSStatus(eventNotHandledErr) }
                var id = EventHotKeyID()
                guard GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID), nil,
                                        MemoryLayout<EventHotKeyID>.size, nil, &id) == noErr else { return OSStatus(eventNotHandledErr) }
                guard id.signature == 0x424B4252 else { return OSStatus(eventNotHandledErr) }
                MainActor.assumeIsolated {
                    let model = Unmanaged<AppModel>.fromOpaque(context).takeUnretainedValue()
                    if id.id == 1 { model.toggleSelectedShortcut() } else if id.id == 2 { model.unlockAll() }
                }
                return noErr
            }, 1, &type, Unmanaged.passUnretained(self).toOpaque(), &hotKeyHandler)
            if result != noErr { shortcutNote = "macOS could not register shortcut handling."; return }
        }
        unregisterHotKeys()
        if shortcutsEnabled && !register(shortcuts) { shortcutNote = "A shortcut is already used by another app. Choose a different combination." }
    }
    func setShortcutsEnabled(_ value: Bool) {
        shortcutsEnabled = value; UserDefaults.standard.set(value, forKey: "shortcutsEnabled"); shortcutNote = ""; installHotKeys()
    }
    private func toggleSelectedShortcut() {
        record("Toggle selected shortcut received.")
        if phase == "authorizing" { endSession(); return }
        if selected.contains(where: { locks[$0] != nil }) { unlock(ids: Array(selected)) } else { lockSelected() }
    }
    func beginRecording(_ action: String) {
        stopRecording(); recording = action; shortcutNote = "Press a key with at least two modifiers. Escape cancels; Delete clears."
        unregisterHotKeys()
        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            let consumed = MainActor.assumeIsolated {
                guard let self else { return false }; return self.capture(event) == nil
            }
            return consumed ? nil : event
        }
    }
    private func capture(_ event: NSEvent) -> NSEvent? {
        guard let action = recording else { return event }
        if event.keyCode == UInt16(kVK_Escape) { stopRecording(); installHotKeys(); shortcutNote = ""; return nil }
        var changed = shortcuts
        if event.keyCode == UInt16(kVK_Delete) { changed.removeValue(forKey: action) }
        else {
            let flags = event.modifierFlags.intersection([.control, .option, .shift, .command])
            guard [.control, .option, .shift, .command].filter({ flags.contains($0) }).count >= 2 else { return nil }
            let special: [UInt16: String] = [49: "Space", 36: "Return", 48: "Tab", 123: "←", 124: "→", 125: "↓", 126: "↑"]
            guard let character = special[event.keyCode] ?? event.charactersIgnoringModifiers?.uppercased(), !character.isEmpty else { return nil }
            let modifiers = (flags.contains(.control) ? UInt32(controlKey) : 0) | (flags.contains(.option) ? UInt32(optionKey) : 0) |
                            (flags.contains(.shift) ? UInt32(shiftKey) : 0) | (flags.contains(.command) ? UInt32(cmdKey) : 0)
            guard !changed.contains(where: { $0.key != action && $0.value.key == UInt32(event.keyCode) && $0.value.modifiers == modifiers }) else {
                shortcutNote = "That shortcut is already assigned to the other action."; return nil
            }
            changed[action] = Shortcut(key: UInt32(event.keyCode), modifiers: modifiers, character: character)
        }
        stopRecording()
        if !shortcutsEnabled || register(changed) {
            shortcuts = changed; UserDefaults.standard.set(try? JSONEncoder().encode(changed), forKey: "shortcuts"); shortcutNote = "Shortcut saved."
        } else { installHotKeys(); shortcutNote = "That shortcut is in use. Your previous shortcuts are unchanged." }
        return nil
    }
    func cancelRecording() { stopRecording(); shortcutNote = ""; installHotKeys() }
    func stopRecording() {
        if let keyMonitor { NSEvent.removeMonitor(keyMonitor) }
        keyMonitor = nil; recording = nil
    }
    func resetShortcuts() {
        stopRecording(); shortcuts = Shortcut.defaults
        UserDefaults.standard.removeObject(forKey: "shortcuts"); shortcutNote = ""; installHotKeys()
    }
    func copyDiagnostics() {
        let text = "BrickMyBoard 0.0.1\nmacOS: \(ProcessInfo.processInfo.operatingSystemVersionString)\nInput Monitoring: \(permitted)\nSession: \(phase)\nDevices:\n" +
            devices.map { "\($0.name) · \($0.connection) · ID \($0.id) · vendor \($0.vendor) product \($0.product)" }.joined(separator: "\n") +
            "\nActivity:\n" + events.map { "\($0.date): \($0.text)" }.joined(separator: "\n")
        NSPasteboard.general.clearContents(); NSPasteboard.general.setString(text, forType: .string)
        record("Diagnostics copied. No keystrokes are recorded.")
    }
}

@main
struct BrickMyBoardApp {
    @MainActor static func main() {
        if CommandLine.arguments.count > 1 {
            do { try runCLI() } catch { FileHandle.standardError.write(Data("\(error)\n".utf8)); exit(1) }
            return
        }
        let application = NSApplication.shared; application.setActivationPolicy(.regular)
        let delegate = AppModel(); application.delegate = delegate
        withExtendedLifetime(delegate) { application.run() }
    }
}
