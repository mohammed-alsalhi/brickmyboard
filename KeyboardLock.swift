import Foundation
import IOKit.hid
import IOKit.hidsystem
import Darwin

struct Keyboard: Codable, Hashable, Identifiable {
    let id: UInt64
    let name: String
    let transport: String
    let builtIn: Bool
    let vendor: Int
    let product: Int
    var connection: String { builtIn ? "Built-in" : (transport.isEmpty ? "External" : transport) }
}

enum LockError: Error, CustomStringConvertible {
    case message(String)
    var description: String { switch self { case .message(let text): text } }
}

func keyboard(from properties: [String: Any], id: UInt64) -> Keyboard? {
    guard (properties[kIOHIDPrimaryUsagePageKey] as? NSNumber)?.intValue == 1,
          (properties[kIOHIDPrimaryUsageKey] as? NSNumber)?.intValue == 6 else { return nil }
    let transport = properties[kIOHIDTransportKey] as? String ?? ""
    guard transport.lowercased() != "virtual" else { return nil }
    return Keyboard(id: id, name: properties[kIOHIDProductKey] as? String ?? "Unnamed keyboard",
                    transport: transport, builtIn: (properties["Built-In"] as? NSNumber)?.boolValue == true,
                    vendor: (properties[kIOHIDVendorIDKey] as? NSNumber)?.intValue ?? 0,
                    product: (properties[kIOHIDProductIDKey] as? NSNumber)?.intValue ?? 0)
}

func properties(of service: io_service_t) throws -> [String: Any] {
    var value: Unmanaged<CFMutableDictionary>?
    guard IORegistryEntryCreateCFProperties(service, &value, kCFAllocatorDefault, 0) == KERN_SUCCESS,
          let result = value?.takeRetainedValue() as? [String: Any] else {
        throw LockError.message("Could not read the keyboard’s device information.")
    }
    return result
}

func keyboards() throws -> [Keyboard] {
    var iterator: io_iterator_t = 0
    guard IOServiceGetMatchingServices(kIOMainPortDefault, IOServiceMatching("IOHIDDevice"), &iterator) == KERN_SUCCESS else {
        throw LockError.message("Could not discover connected keyboards.")
    }
    defer { IOObjectRelease(iterator) }
    var result: [Keyboard] = []
    while case let service = IOIteratorNext(iterator), service != 0 {
        defer { IOObjectRelease(service) }
        var id: UInt64 = 0
        guard IORegistryEntryGetRegistryEntryID(service, &id) == KERN_SUCCESS,
              let values = try? properties(of: service), let device = keyboard(from: values, id: id) else { continue }
        result.append(device)
    }
    return result.sorted { $0.builtIn != $1.builtIn ? $0.builtIn : ($0.name == $1.name ? $0.id < $1.id : $0.name < $1.name) }
}

func openKeyboard(_ id: UInt64) throws -> IOHIDDevice {
    let service = IOServiceGetMatchingService(kIOMainPortDefault, IORegistryEntryIDMatching(id))
    guard service != 0 else { throw LockError.message("That keyboard was disconnected. Refresh and try again.") }
    defer { IOObjectRelease(service) }
    guard keyboard(from: try properties(of: service), id: id) != nil,
          let device = IOHIDDeviceCreate(kCFAllocatorDefault, service) else {
        throw LockError.message("The selected device is not an eligible physical keyboard.")
    }
    let result = IOHIDDeviceOpen(device, IOOptionBits(kIOHIDOptionsTypeSeizeDevice))
    guard result == kIOReturnSuccess else {
        let code = String(format: "0x%08x", UInt32(bitPattern: result))
        throw LockError.message("macOS refused access to this keyboard (\(code)). Check BrickMyBoard’s Input Monitoring permission and quit competing keyboard utilities. Some devices may be protected by macOS.")
    }
    return device
}

func requireInputMonitoring(_ access: IOHIDAccessType) throws {
    guard access == kIOHIDAccessTypeGranted else {
        throw LockError.message("Enable BrickMyBoard in System Settings → Privacy & Security → Input Monitoring, then quit and reopen the app.")
    }
}

struct Command: Codable {
    enum Action: String, Codable { case lock, unlock, unlockAll, stop }
    let action: Action
    var ids: [UInt64] = []
    var seconds: Int = 300
    var automatic = false

    func validate() throws {
        guard ids.count <= 32, Set(ids).count == ids.count, ids.allSatisfy({ $0 > 0 }),
              (0...86400).contains(seconds), action != .lock || !ids.isEmpty else {
            throw LockError.message("Invalid keyboard selection or timer. No changes applied.")
        }
    }
}

struct LockState: Codable, Equatable {
    let id: UInt64
    let remaining: Int? // nil means until unlocked; the helper still releases on disconnect.
    let automatic: Bool
}

struct Reply: Codable {
    let kind: String
    var locks: [LockState] = []
    var message: String? = nil
}

func acquireTransaction<T>(_ ids: [UInt64], open: (UInt64) throws -> T, rollback: (T) -> Void) throws -> [UInt64: T] {
    var opened: [UInt64: T] = [:]
    do {
        for id in ids { opened[id] = try open(id) }
        return opened
    } catch {
        for device in opened.values { rollback(device) }
        throw error
    }
}

func autoTargets(_ devices: [Keyboard]) -> [UInt64] {
    guard devices.contains(where: { !$0.builtIn }) else { return [] }
    return devices.filter(\.builtIn).map(\.id)
}

func unixAddress(_ path: String) throws -> sockaddr_un {
    var address = sockaddr_un()
    let bytes = path.utf8CString
    guard bytes.count <= MemoryLayout.size(ofValue: address.sun_path), !path.contains("\0") else {
        throw LockError.message("Invalid control socket path.")
    }
    address.sun_family = sa_family_t(AF_UNIX)
    address.sun_len = UInt8(MemoryLayout<sockaddr_un>.size)
    withUnsafeMutableBytes(of: &address.sun_path) { target in
        for (index, byte) in bytes.enumerated() { target[index] = UInt8(bitPattern: byte) }
    }
    return address
}

func withSocketAddress<T>(_ address: inout sockaddr_un, _ body: (UnsafePointer<sockaddr>, socklen_t) -> T) -> T {
    withUnsafePointer(to: &address) {
        $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { body($0, socklen_t(MemoryLayout<sockaddr_un>.size)) }
    }
}

func configureSocket(_ fd: Int32) {
    var enabled: Int32 = 1
    setsockopt(fd, SOL_SOCKET, SO_NOSIGPIPE, &enabled, socklen_t(MemoryLayout<Int32>.size))
    var timeout = timeval(tv_sec: 1, tv_usec: 0)
    setsockopt(fd, SOL_SOCKET, SO_SNDTIMEO, &timeout, socklen_t(MemoryLayout<timeval>.size))
}

func sendMessage<T: Encodable>(_ value: T, to fd: Int32) throws {
    var data = try JSONEncoder().encode(value)
    data.append(10)
    try data.withUnsafeBytes { bytes in
        var offset = 0
        while offset < bytes.count {
            let sent = send(fd, bytes.baseAddress!.advanced(by: offset), bytes.count - offset, 0)
            if sent < 0 && errno == EINTR { continue }
            guard sent > 0 else { throw LockError.message("The keyboard-control connection closed.") }
            offset += sent
        }
    }
}

func receiveMessages<T: Decodable>(_ type: T.Type, buffer: inout Data, data: Data) throws -> [T] {
    buffer.append(data)
    guard buffer.count <= 65536 else { throw LockError.message("Control message exceeded its size limit.") }
    var messages: [T] = []
    while let newline = buffer.firstIndex(of: 10) {
        messages.append(try JSONDecoder().decode(T.self, from: buffer[..<newline]))
        buffer.removeSubrange(...newline)
    }
    return messages
}

func remainingSeconds(until deadline: ContinuousClock.Instant?) -> Int? {
    guard let deadline else { return nil }
    let parts = ContinuousClock.now.duration(to: deadline).components
    return max(0, Int(parts.seconds) + (parts.attoseconds > 0 ? 1 : 0))
}

/// HID usages (page 7) currently held on locked keyboards. The helper is single-threaded.
nonisolated(unsafe) var heldKeys = Set<UInt32>()
let leftCommand: UInt32 = 0xE3, rightCommand: UInt32 = 0xE7
/// Exit combo: both ⌘ keys and nothing else, so a cleaning cloth can't trigger it.
func exitComboHeld(_ keys: Set<UInt32>) -> Bool { keys == [leftCommand, rightCommand] }

func watchKeys(_ device: IOHIDDevice) {
    IOHIDDeviceRegisterInputValueCallback(device, { _, _, _, value in
        let element = IOHIDValueGetElement(value)
        let usage = IOHIDElementGetUsage(element)
        guard IOHIDElementGetUsagePage(element) == 7, (4...0xE7).contains(usage) else { return }
        if IOHIDValueGetIntegerValue(value) != 0 { heldKeys.insert(usage) } else { heldKeys.remove(usage) }
    }, nil)
    IOHIDDeviceScheduleWithRunLoop(device, CFRunLoopGetCurrent(), CFRunLoopMode.defaultMode.rawValue)
}

func runHelper(socketPath: String) throws {
    guard geteuid() == 0 else { throw LockError.message("Authorize keyboard control from the app.") }
    try requireInputMonitoring(IOHIDCheckAccess(kIOHIDRequestTypeListenEvent))
    var address = try unixAddress(socketPath)
    let fd = socket(AF_UNIX, SOCK_STREAM, 0)
    guard fd >= 0 else { throw LockError.message("Could not create the control connection.") }
    defer { close(fd) }
    guard withSocketAddress(&address, { connect(fd, $0, $1) }) == 0 else {
        throw LockError.message("The app closed before authorization finished. Nothing locked.")
    }
    configureSocket(fd)
    var locked: [UInt64: (device: IOHIDDevice, deadline: ContinuousClock.Instant?, automatic: Bool)] = [:]
    func unlock(_ ids: [UInt64]) {
        for id in ids {
            if let lease = locked.removeValue(forKey: id) {
                IOHIDDeviceUnscheduleFromRunLoop(lease.device, CFRunLoopGetCurrent(), CFRunLoopMode.defaultMode.rawValue)
                IOHIDDeviceClose(lease.device, IOOptionBits(kIOHIDOptionsTypeNone))
            }
        }
        if locked.isEmpty { heldKeys = [] }
    }
    defer { unlock(Array(locked.keys)) }
    func states() -> [LockState] {
        locked.map { LockState(id: $0.key, remaining: remainingSeconds(until: $0.value.deadline), automatic: $0.value.automatic) }.sorted { $0.id < $1.id }
    }
    try sendMessage(Reply(kind: "ready"), to: fd)
    var buffer = Data()
    var lastScan = ContinuousClock.now
    var comboSince: ContinuousClock.Instant?
    while true {
        var input = pollfd(fd: fd, events: Int16(POLLIN), revents: 0)
        let result = poll(&input, 1, 50)
        while CFRunLoopRunInMode(.defaultMode, 0, true) == .handledSource {}
        if exitComboHeld(heldKeys) {
            if let since = comboSince, ContinuousClock.now - since >= .seconds(2) {
                comboSince = nil
                unlock(Array(locked.keys))
                try sendMessage(Reply(kind: "state", locks: states(), message: "Released by holding both ⌘ keys."), to: fd)
            } else if comboSince == nil { comboSince = .now }
        } else { comboSince = nil }
        if result < 0 && errno != EINTR { return }
        if input.revents & Int16(POLLHUP | POLLERR | POLLNVAL) != 0 { return }
        if input.revents & Int16(POLLIN) != 0 {
            var bytes = [UInt8](repeating: 0, count: 8192)
            let count = read(fd, &bytes, bytes.count)
            guard count > 0 else { return }
            for command in try receiveMessages(Command.self, buffer: &buffer, data: Data(bytes.prefix(count))) {
                do {
                    try command.validate()
                    switch command.action {
                    case .lock:
                        let devices = try keyboards()
                        guard Set(command.ids).isSubset(of: Set(devices.map(\.id))) else {
                            throw LockError.message("A selected keyboard disconnected. Refresh your selection.")
                        }
                        if command.automatic {
                            guard Set(command.ids).isSubset(of: Set(autoTargets(devices))) else {
                                throw LockError.message("Automatic mode requires a connected external keyboard and targets only built-in keyboards.")
                            }
                        }
                        let fresh = command.ids.filter { locked[$0] == nil }
                        let opened = try acquireTransaction(fresh, open: openKeyboard) {
                            IOHIDDeviceClose($0, IOOptionBits(kIOHIDOptionsTypeNone))
                        }
                        for (id, device) in opened {
                            watchKeys(device)
                            locked[id] = (device, command.seconds == 0 ? nil : .now.advanced(by: .seconds(command.seconds)), command.automatic)
                        }
                    case .unlock: unlock(command.ids)
                    case .unlockAll: unlock(Array(locked.keys))
                    case .stop: return
                    }
                    try sendMessage(Reply(kind: "state", locks: states()), to: fd)
                } catch {
                    try sendMessage(Reply(kind: "error", locks: states(), message: String(describing: error)), to: fd)
                }
            }
        }
        if ContinuousClock.now - lastScan >= .seconds(1) {
            lastScan = .now
            let devices = try keyboards()
            let connected = Set(devices.map(\.id))
            let externalAvailable = devices.contains { !$0.builtIn && locked[$0.id] == nil }
            unlock(locked.compactMap { id, lease in
                (!connected.contains(id) || (remainingSeconds(until: lease.deadline) == 0) || (lease.automatic && !externalAvailable)) ? id : nil
            })
            try sendMessage(Reply(kind: "state", locks: states()), to: fd)
        }
    }
}

func runCLI() throws {
    let args = Array(CommandLine.arguments.dropFirst())
    if args == ["--self-test"] { try runChecks(); return }
    if args == ["--check"] {
        for device in try keyboards() { print("\(device.id): \(device.name) [\(device.connection)]") }
        return
    }
    if args.count == 2 && args[0] == "--helper" { try runHelper(socketPath: args[1]); return }
    throw LockError.message("Open BrickMyBoard.app. Diagnostic options: --check, --self-test.")
}

#if !NATIVE_APP
@main struct KeyboardLockCLI {
    static func main() {
        do { try runCLI() }
        catch { FileHandle.standardError.write(Data("\(error)\n".utf8)); exit(1) }
    }
}
#endif
