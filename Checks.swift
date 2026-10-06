import Foundation
import IOKit.hidsystem
import Darwin

func runChecks() throws {
    let base: [String: Any] = ["Built-In": true, "PrimaryUsagePage": 1, "PrimaryUsage": 6, "Product": "Internal", "Transport": "FIFO"]
    let internalKeyboard = keyboard(from: base, id: 1)!
    var externalProperties = base
    externalProperties["Built-In"] = false
    externalProperties["Transport"] = "USB"
    let external = keyboard(from: externalProperties, id: 2)!
    precondition(internalKeyboard.builtIn && !external.builtIn)
    var mouse = base; mouse["PrimaryUsage"] = 2
    precondition(keyboard(from: mouse, id: 3) == nil)
    var virtual = base; virtual["Transport"] = "Virtual"
    precondition(keyboard(from: virtual, id: 4) == nil)
    precondition(keyboard(from: [:], id: 5) == nil)
    precondition(autoTargets([internalKeyboard]).isEmpty)
    precondition(autoTargets([internalKeyboard, external]) == [1])
    precondition(autoTargets([external]).isEmpty)
    try requireInputMonitoring(kIOHIDAccessTypeGranted)
    for denied in [kIOHIDAccessTypeDenied, kIOHIDAccessTypeUnknown] {
        do { try requireInputMonitoring(denied); preconditionFailure("Permission was not enforced") } catch {}
    }
    for invalid in [Command(action: .lock), Command(action: .lock, ids: [1, 1]),
                    Command(action: .lock, ids: [0]), Command(action: .lock, ids: [1], seconds: -1),
                    Command(action: .lock, ids: [1], seconds: 86401)] {
        do { try invalid.validate(); preconditionFailure("Invalid command accepted") } catch {}
    }
    try Command(action: .lock, ids: [1, 2], seconds: 0).validate()
    var released: [UInt64] = []
    do {
        _ = try acquireTransaction([1, 2, 3], open: { id -> UInt64 in
            if id == 3 { throw LockError.message("Simulated device access denial") }
            return id
        }, rollback: { released.append($0) })
        preconditionFailure("Partial acquisition should fail")
    } catch {}
    precondition(Set(released) == [1, 2], "All newly acquired devices must be released on partial failure")
    let command = Command(action: .unlock, ids: [1])
    var wire = try JSONEncoder().encode(command); wire.append(10)
    var buffer = Data()
    let first = try receiveMessages(Command.self, buffer: &buffer, data: wire.prefix(4))
    precondition(first.isEmpty)
    let rest = try receiveMessages(Command.self, buffer: &buffer, data: wire.dropFirst(4) + wire)
    precondition(rest.count == 2 && rest.allSatisfy { $0.action == .unlock && $0.ids == [1] } && buffer.isEmpty)
    do {
        _ = try receiveMessages(Command.self, buffer: &buffer, data: Data(repeating: 65, count: 65537))
        preconditionFailure("Unbounded input must be rejected")
    } catch {}
    do { _ = try unixAddress(String(repeating: "x", count: 200)); preconditionFailure("Overlong path") } catch {}
    var pair: [Int32] = [0, 0]
    precondition(socketpair(AF_UNIX, SOCK_STREAM, 0, &pair) == 0)
    defer { close(pair[0]) }
    configureSocket(pair[0]); configureSocket(pair[1])
    try sendMessage(Reply(kind: "ready"), to: pair[1])
    var bytes = [UInt8](repeating: 0, count: 1024)
    let count = read(pair[0], &bytes, bytes.count)
    buffer = Data()
    let reply = try receiveMessages(Reply.self, buffer: &buffer, data: Data(bytes.prefix(count)))
    precondition(reply.first?.kind == "ready")
    close(pair[1])
    precondition(read(pair[0], &bytes, bytes.count) == 0, "Helper must observe app disconnect")
    precondition(remainingSeconds(until: .now.advanced(by: .seconds(-1))) == 0)
    precondition(remainingSeconds(until: nil) == nil)
    precondition(exitComboHeld([leftCommand, rightCommand]))
    precondition(!exitComboHeld([leftCommand]) && !exitComboHeld([leftCommand, rightCommand, 0x2C]) && !exitComboHeld([]))
    print("PASS: exit combo, discovery, automatic targeting, permissions, command validation, transactional rollback, fragmented IPC, message limits, disconnect, and deadlines.")
}
