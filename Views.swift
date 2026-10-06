import SwiftUI
import AppKit

// Cobalt scheme. Pairs measured with APCA; see the color-schemes canvas.
private func adaptive(_ light: UInt32, _ dark: UInt32) -> Color {
    func rgb(_ v: UInt32) -> NSColor { NSColor(srgbRed: CGFloat(v >> 16 & 255) / 255, green: CGFloat(v >> 8 & 255) / 255, blue: CGFloat(v & 255) / 255, alpha: 1) }
    return Color(nsColor: NSColor(name: nil) { $0.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua ? rgb(dark) : rgb(light) })
}
let accent = adaptive(0x285cc2, 0x3d70d1)
let accentText = adaptive(0x2559bf, 0x9bbeff)
private let accentDeep = adaptive(0x163990, 0x264ba4)
let positive = adaptive(0x1d7d3e, 0x6fd087)
private let ground = adaptive(0xfaf5ef, 0x191a18)
private let sidebarGround = adaptive(0xf1e9df, 0x131311)
private let card = adaptive(0xfefcfa, 0x242421)

private func clock(_ seconds: Int) -> String { String(format: "%d:%02d", seconds / 60, seconds % 60) }

extension AppModel {
    var phaseText: String {
        switch phase {
        case "ready": "Control authorized"
        case "authorizing": "Waiting for approval…"
        case "stopping": "Releasing keyboards…"
        default: "macOS asks for approval on first lock"
        }
    }
    /// Longest remaining lock; nil if any lock lasts until unlocked.
    var longestRemaining: Int?? {
        guard !locks.isEmpty else { return .none }
        if locks.values.contains(where: { $0.remaining == nil }) { return .some(nil) }
        return .some(locks.values.compactMap(\.remaining).max())
    }
}

// MARK: - Window

struct RootView: View {
    @ObservedObject var model: AppModel
    var body: some View {
        HStack(spacing: 0) {
            sidebar.frame(width: 220)
            Divider()
            VStack(alignment: .leading, spacing: 0) {
                header
                ScrollView {
                    VStack(alignment: .leading, spacing: 20) {
                        if let issue = model.issue { IssueBanner(text: issue) { model.issue = nil } }
                        switch model.page {
                        case .keyboards: KeyboardsPage(model: model)
                        case .cleaning: CleaningPage(model: model)
                        case .automation: AutomationPage(model: model)
                        case .shortcuts: ShortcutsPage(model: model)
                        case .activity: ActivityPage(model: model)
                        case .settings: SettingsPage(model: model)
                        }
                    }.padding(.horizontal, 32).padding(.bottom, 32).padding(.top, 14)
                }
            }.background(ground)
        }
        .ignoresSafeArea()
        .tint(accent)
        .animation(.snappy(duration: 0.25), value: model.locks)
        .onChange(of: model.page) { _, _ in if model.recording != nil { model.cancelRecording() } }
    }

    private var sidebar: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 10) {
                LogoMark().frame(width: 36, height: 36)
                VStack(alignment: .leading, spacing: 1) {
                    Text("BrickMyBoard").font(.system(size: 13, weight: .semibold))
                    Text("Quiet keys, on demand").font(.system(size: 10)).foregroundStyle(.secondary)
                }
            }.padding(.horizontal, 18).padding(.top, 52).padding(.bottom, 14)
            VStack(spacing: 2) {
                ForEach(Page.allCases) { page in SidebarItem(page: page, selected: model.page == page, badge: badge(page)) { model.page = page } }
            }.padding(.horizontal, 10)
            Spacer()
            SidebarStatus(model: model).padding(12)
        }.background(sidebarGround)
    }
    private func badge(_ page: Page) -> String? {
        switch page {
        case .keyboards: model.locks.isEmpty ? nil : "\(model.locks.count)"
        case .automation: model.automatic ? "On" : nil
        default: nil
        }
    }

    private var header: some View {
        HStack(alignment: .center) {
            VStack(alignment: .leading, spacing: 4) {
                Text(model.page.rawValue).font(.system(size: 26, weight: .bold))
                Text(subtitle).font(.system(size: 13)).foregroundStyle(.secondary)
            }
            Spacer()
            if !model.locks.isEmpty || model.phase == "authorizing" {
                Button { model.unlockAll() } label: { Label("Unlock all", systemImage: "lock.open.fill") }
                    .controlSize(.large).help("Release every keyboard. Also in the menu bar.")
                    .transition(.opacity.combined(with: .scale(scale: 0.9)))
            }
        }.padding(.horizontal, 32).padding(.top, 44).padding(.bottom, 8)
    }
    private var subtitle: String {
        switch model.page {
        case .keyboards: "Pick a keyboard, give it a break."
        case .cleaning: "Black out the screen and lock every key while you wipe down."
        case .automation: "Let your desk setup take care of itself."
        case .shortcuts: "Lock and let go without reaching for the mouse."
        case .activity: "What the app did this session. Never what you typed."
        case .settings: "Permissions, behavior and the control session."
        }
    }
}

private struct SidebarItem: View {
    let page: Page; let selected: Bool; let badge: String?; let action: () -> Void
    @State private var hover = false
    var body: some View {
        Button(action: action) {
            HStack(spacing: 10) {
                Image(systemName: page.symbol).font(.system(size: 13, weight: .medium)).frame(width: 20)
                    .foregroundStyle(selected ? accentText : .secondary)
                Text(page.rawValue).font(.system(size: 13, weight: selected ? .semibold : .regular))
                Spacer()
                if let badge {
                    Text(badge).font(.system(size: 10, weight: .bold)).padding(.horizontal, 6).padding(.vertical, 2)
                        .background(accent.opacity(selected ? 0.18 : 0.12), in: Capsule()).foregroundStyle(accentText)
                }
            }
            .padding(.horizontal, 10).frame(height: 32)
            .background(RoundedRectangle(cornerRadius: 7).fill(selected ? Color.primary.opacity(0.08) : hover ? Color.primary.opacity(0.04) : .clear))
            .contentShape(Rectangle())
        }.buttonStyle(.plain).onHover { hover = $0 }
    }
}

private struct SidebarStatus: View {
    @ObservedObject var model: AppModel
    var body: some View {
        HStack(spacing: 10) {
            ZStack {
                Circle().fill(model.locks.isEmpty ? positive.opacity(0.18) : accent.opacity(0.18)).frame(width: 28, height: 28)
                Image(systemName: model.locks.isEmpty ? "checkmark" : "lock.fill").font(.system(size: 11, weight: .bold))
                    .foregroundStyle(model.locks.isEmpty ? positive : accentText)
            }
            VStack(alignment: .leading, spacing: 1) {
                Text(model.locks.isEmpty ? "All available" : "\(model.locks.count) locked").font(.system(size: 12, weight: .semibold))
                Text(model.phase == "ready" ? "Session authorized" : model.phase == "authorizing" ? "Awaiting approval" : "Not authorized")
                    .font(.system(size: 10)).foregroundStyle(.secondary)
            }
            Spacer()
        }
        .padding(10).background(Color.primary.opacity(0.04), in: RoundedRectangle(cornerRadius: 10))
    }
}

private struct IssueBanner: View {
    let text: String; let dismiss: () -> Void
    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(.orange).font(.system(size: 15))
            Text(text).font(.callout).textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading)
            Button(action: dismiss) { Image(systemName: "xmark").font(.system(size: 10, weight: .bold)) }
                .buttonStyle(.plain).foregroundStyle(.secondary).accessibilityLabel("Dismiss error")
        }
        .padding(14).background(Color.orange.opacity(0.1), in: RoundedRectangle(cornerRadius: 12))
        .overlay(RoundedRectangle(cornerRadius: 12).strokeBorder(Color.orange.opacity(0.25)))
    }
}

// MARK: - Keyboards

private struct KeyboardsPage: View {
    @ObservedObject var model: AppModel
    private let columns = [GridItem(.adaptive(minimum: 230), spacing: 14)]
    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            if !model.permitted { PermissionCard(model: model) }
            StatusHero(model: model)
            HStack {
                SectionLabel("Connected · \(model.devices.count)")
                Spacer()
                Menu {
                    Button("All keyboards") { model.selected = Set(model.devices.map(\.id)) }
                    Button("Built-in only") { model.selected = Set(model.devices.filter(\.builtIn).map(\.id)) }
                    Button("External only") { model.selected = Set(model.devices.filter { !$0.builtIn }.map(\.id)) }
                    Divider()
                    Button("Clear selection") { model.selected = [] }
                } label: { Text("Select") }.menuStyle(.borderlessButton).fixedSize()
                Button { model.refreshDevices() } label: { Image(systemName: "arrow.clockwise") }
                    .buttonStyle(.borderless).help("Refresh connected keyboards").accessibilityLabel("Refresh keyboards")
            }
            if model.devices.isEmpty {
                Card {
                    VStack(spacing: 10) {
                        Image(systemName: "keyboard").font(.system(size: 34, weight: .light)).foregroundStyle(.tertiary)
                        Text("No keyboards found").font(.headline)
                        Text("Connect a keyboard, then refresh.").font(.callout).foregroundStyle(.secondary)
                    }.frame(maxWidth: .infinity).padding(30)
                }
            } else {
                LazyVGrid(columns: columns, spacing: 14) {
                    ForEach(model.devices) { KeyboardCard(model: model, device: $0) }
                }
            }
            ActionBar(model: model)
            if !model.devices.contains(where: { !$0.builtIn }) {
                Hint("cable.connector", "Plug in a USB or Bluetooth keyboard to control it here, or to use automatic mode.")
            }
            Hint("cursorarrow.rays", "Your mouse and the menu-bar Unlock all always work. Power and Touch ID stay active.")
        }
    }
}

private struct StatusHero: View {
    @ObservedObject var model: AppModel
    var body: some View {
        let locked = !model.locks.isEmpty
        HStack(spacing: 18) {
            ZStack {
                Circle().fill(locked ? Color.white.opacity(0.18) : positive.opacity(0.14)).frame(width: 56, height: 56)
                Image(systemName: locked ? "lock.fill" : "keyboard").font(.system(size: 22, weight: .semibold))
                    .foregroundStyle(locked ? Color.white : positive)
                    .contentTransition(.symbolEffect(.replace))
            }
            VStack(alignment: .leading, spacing: 4) {
                Text(locked ? "\(model.locks.count) keyboard\(model.locks.count == 1 ? "" : "s") locked" : "All keyboards available")
                    .font(.system(size: 19, weight: .semibold))
                Text(locked ? (model.locks.values.contains(where: \.automatic) ? "Locked automatically by external keyboard mode." : "Type away on anything that isn't locked.")
                     : "Nothing is locked. Pick keyboards below to give them a break.")
                    .font(.callout).foregroundStyle(locked ? Color.white.opacity(0.8) : Color.secondary)
            }
            Spacer()
            if case .some(let remaining) = model.longestRemaining {
                VStack(alignment: .trailing, spacing: 2) {
                    Text(remaining.map(clock) ?? "∞").font(.system(size: 30, weight: .semibold, design: .rounded)).monospacedDigit()
                        .contentTransition(.numericText(countsDown: true))
                    Text(remaining == nil ? "until you unlock" : "until auto-unlock").font(.caption).foregroundStyle(.white.opacity(0.75))
                }
            }
        }
        .foregroundStyle(locked ? Color.white : Color.primary)
        .padding(22)
        .background {
            RoundedRectangle(cornerRadius: 16).fill(locked ? AnyShapeStyle(LinearGradient(colors: [accent, accentDeep], startPoint: .topLeading, endPoint: .bottomTrailing))
                                                          : AnyShapeStyle(card))
        }
        .overlay(RoundedRectangle(cornerRadius: 16).strokeBorder(Color.primary.opacity(locked ? 0 : 0.07)))
        .shadow(color: locked ? accent.opacity(0.3) : .clear, radius: 14, y: 6)
    }
}

private struct KeyboardCard: View {
    @ObservedObject var model: AppModel
    let device: Keyboard
    @State private var hover = false
    var body: some View {
        let state = model.locks[device.id]
        let selected = model.selected.contains(device.id)
        VStack(alignment: .leading, spacing: 14) {
            HStack(alignment: .top) {
                ZStack {
                    RoundedRectangle(cornerRadius: 12).fill(state == nil ? Color.primary.opacity(0.06) : accent.opacity(0.14)).frame(width: 52, height: 52)
                    Image(systemName: device.builtIn ? "laptopcomputer" : "keyboard").font(.system(size: 22))
                        .foregroundStyle(state == nil ? Color.secondary : accentText)
                    if state != nil {
                        Image(systemName: "lock.circle.fill").font(.system(size: 18)).foregroundStyle(.white, accent)
                            .offset(x: 21, y: 21).transition(.scale)
                    }
                }
                Spacer()
                Button { model.toggleSelection(device.id) } label: {
                    Image(systemName: selected ? "checkmark.circle.fill" : "circle").font(.system(size: 18))
                        .foregroundStyle(selected ? accentText : Color.secondary.opacity(0.5))
                }.buttonStyle(.plain).accessibilityLabel(selected ? "Deselect \(device.name)" : "Select \(device.name)")
            }
            VStack(alignment: .leading, spacing: 4) {
                Text(device.builtIn ? "Built-in Keyboard" : device.name).font(.system(size: 14, weight: .semibold)).lineLimit(1)
                HStack(spacing: 6) {
                    Text(device.connection).font(.caption).foregroundStyle(.secondary)
                    StatePill(state: state)
                }
            }
            HStack {
                if let state {
                    Label(state.remaining.map(clock) ?? "Until unlocked", systemImage: "timer")
                        .font(.system(size: 12, weight: .medium)).monospacedDigit().foregroundStyle(accentText)
                        .contentTransition(.numericText(countsDown: true))
                } else {
                    Text(model.duration == 0 ? "Locks until unlocked" : "Locks for \(durationName(model.duration))").font(.caption).foregroundStyle(.tertiary)
                }
                Spacer()
                Button(state == nil ? "Lock" : "Unlock") {
                    if state == nil { model.lock(ids: [device.id]) } else { model.unlock(ids: [device.id]) }
                }
                .buttonStyle(.borderedProminent).tint(state == nil ? accent : Color.secondary)
                .disabled(!model.permitted || (state == nil && model.busy))
            }
        }
        .padding(16)
        .background(card, in: RoundedRectangle(cornerRadius: 14))
        .overlay(RoundedRectangle(cornerRadius: 14).strokeBorder(selected ? accent.opacity(0.7) : Color.primary.opacity(hover ? 0.14 : 0.07), lineWidth: selected ? 1.5 : 1))
        .shadow(color: .black.opacity(hover ? 0.08 : 0.03), radius: hover ? 10 : 4, y: 2)
        .contentShape(RoundedRectangle(cornerRadius: 14))
        .onTapGesture { model.toggleSelection(device.id) }
        .onHover { hover = $0 }
        .animation(.easeOut(duration: 0.15), value: hover)
        .help("\(device.name)\nVendor \(device.vendor) · Product \(device.product)")
    }
}

private struct StatePill: View {
    let state: LockState?
    var body: some View {
        let text = state == nil ? "Available" : state!.automatic ? "Auto-locked" : "Locked"
        Text(text).font(.system(size: 10, weight: .semibold))
            .padding(.horizontal, 7).padding(.vertical, 2)
            .background((state == nil ? positive : accent).opacity(0.14), in: Capsule())
            .foregroundStyle(state == nil ? positive : accentText)
    }
}

private struct ActionBar: View {
    @ObservedObject var model: AppModel
    var body: some View {
        HStack(spacing: 14) {
            VStack(alignment: .leading, spacing: 3) {
                Text("\(model.selected.count) selected").font(.system(size: 13, weight: .semibold))
                HStack(spacing: 5) {
                    if model.busy { ProgressView().controlSize(.mini) }
                    Text(model.phaseText).font(.caption).foregroundStyle(.secondary)
                }
            }
            Spacer()
            if model.phase == "authorizing" { Button("Cancel") { model.endSession() } }
            DurationPicker(model: model).frame(width: 150)
            Button { model.lockSelected() } label: { Label("Lock selected", systemImage: "lock.fill") }
                .buttonStyle(.borderedProminent).controlSize(.large)
                .disabled(!model.permitted || model.busy || !model.selected.contains(where: { model.locks[$0] == nil }))
                .keyboardShortcut(.return, modifiers: .command)
        }
        .padding(.horizontal, 18).padding(.vertical, 14)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 14))
        .overlay(RoundedRectangle(cornerRadius: 14).strokeBorder(Color.primary.opacity(0.07)))
    }
}

func durationName(_ seconds: Int) -> String {
    switch seconds { case 30: "30 seconds"; case 120: "2 minutes"; case 300: "5 minutes"; case 900: "15 minutes"; case 3600: "1 hour"; default: "until unlocked" }
}

private struct DurationPicker: View {
    @ObservedObject var model: AppModel
    var body: some View {
        Picker("Auto-unlock", selection: Binding(get: { model.duration }, set: model.setDuration)) {
            ForEach([30, 120, 300, 900, 3600], id: \.self) { Label(durationName($0), systemImage: "timer").tag($0) }
            Divider()
            Label("Until unlocked", systemImage: "infinity").tag(0)
        }.labelsHidden().accessibilityLabel("Auto-unlock duration")
    }
}

private struct PermissionCard: View {
    @ObservedObject var model: AppModel
    var body: some View {
        Card {
            HStack(alignment: .top, spacing: 18) {
                ZStack {
                    RoundedRectangle(cornerRadius: 14).fill(model.permitted ? positive.opacity(0.14) : accent.opacity(0.14)).frame(width: 52, height: 52)
                    Image(systemName: model.permitted ? "checkmark.shield.fill" : "hand.raised.fill").font(.system(size: 22))
                        .foregroundStyle(model.permitted ? positive : accentText)
                }
                VStack(alignment: .leading, spacing: 10) {
                    Text(model.permitted ? "Input Monitoring is on" : "One permission to get started").font(.headline)
                    if model.permitted {
                        Text("BrickMyBoard can take control of the keyboards you pick. Keystrokes are never read or logged.")
                            .font(.callout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                    } else {
                        Step(n: 1, text: "Open Privacy & Security → Input Monitoring")
                        Step(n: 2, text: "Turn on BrickMyBoard")
                        Step(n: 3, text: "Reopen the app if macOS asks")
                        HStack(spacing: 10) {
                            Button("Open Input Monitoring…") { model.setUpPermissions() }.buttonStyle(.borderedProminent)
                            if model.permissionRequested { Button("Relaunch") { model.relaunch() } }
                        }.padding(.top, 2)
                        Divider().padding(.vertical, 2)
                        Text("Already turned on? macOS ties the permission to each build of the app, so after an update the switch can look on without working.")
                            .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                        Button("Reset permission and ask again") { model.resetPermission() }.controlSize(.small)
                    }
                }
                Spacer()
            }
        }
    }
    private struct Step: View {
        let n: Int; let text: String
        var body: some View {
            HStack(spacing: 10) {
                Text("\(n)").font(.system(size: 11, weight: .bold)).frame(width: 20, height: 20)
                    .background(Color.primary.opacity(0.08), in: Circle())
                Text(text).font(.callout)
            }
        }
    }
}

// MARK: - Cleaning

private struct CleaningPage: View {
    @ObservedObject var model: AppModel
    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            if !model.permitted { PermissionCard(model: model) }
            Card {
                VStack(alignment: .leading, spacing: 20) {
                    HStack(alignment: .top, spacing: 16) {
                        IconTile("sparkles", tint: accent, size: 52)
                        VStack(alignment: .leading, spacing: 6) {
                            Text("Cleaning mode").font(.title3.weight(.semibold))
                            Text("Every screen goes black and every keyboard locks, so you can wipe keys and display without typing or clicking anything.")
                                .foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                        }
                    }
                    HStack(spacing: 14) {
                        Text("Ends after").font(.callout)
                        Picker("Ends after", selection: Binding(get: { model.cleaningMinutes }, set: model.setCleaningMinutes)) {
                            ForEach([1, 2, 5, 10], id: \.self) { Text("\($0) min").tag($0) }
                        }.pickerStyle(.segmented).labelsHidden().frame(width: 260)
                        Spacer()
                        if model.cleaningPending { ProgressView().controlSize(.small); Text("Waiting for approval…").font(.caption).foregroundStyle(.secondary) }
                        Button { model.startCleaning() } label: { Label("Start cleaning", systemImage: "sparkles") }
                            .buttonStyle(.borderedProminent).controlSize(.large)
                            .disabled(!model.permitted || model.busy || model.devices.isEmpty || model.cleaning)
                    }
                }
            }
            SectionLabel("While it's on")
            Card(padding: 0) {
                VStack(spacing: 0) {
                    RuleRow("keyboard", "Every keyboard locks", "Built-in and external. Keys you press go nowhere.")
                    Divider().padding(.leading, 60)
                    RuleRow("display", "Every screen goes black", "Dust and smudges show up best on black. The pointer hides and clicks do nothing.")
                    Divider().padding(.leading, 60)
                    HStack(spacing: 14) {
                        IconTile("hand.raised", tint: accent, size: 32)
                        VStack(alignment: .leading, spacing: 2) {
                            Text("Finish early").font(.system(size: 13, weight: .medium))
                            Text("Hold both ⌘ keys, and nothing else, for 2 seconds.").font(.caption).foregroundStyle(.secondary)
                        }
                        Spacer()
                        HStack(spacing: 6) { Keycaps(label: "⌘"); Text("+").foregroundStyle(.secondary); Keycaps(label: "⌘") }
                    }.padding(.horizontal, 14).padding(.vertical, 12)
                    Divider().padding(.leading, 60)
                    RuleRow("timer", "Ends on its own", "After \(model.cleaningMinutes) minute\(model.cleaningMinutes == 1 ? "" : "s"), everything unlocks and the screen comes back.")
                }
            }
            Hint("info.circle", "The first lock in a session asks for Touch ID or your password, before the screen goes dark.")
        }
    }
}

struct CleaningOverlay: View {
    @ObservedObject var model: AppModel
    var body: some View {
        TimelineView(.periodic(from: .now, by: 1)) { context in
            let left = max(0, Int(model.cleaningEnds.timeIntervalSince(context.date).rounded(.up)))
            VStack(spacing: 22) {
                Image(systemName: "sparkles").font(.system(size: 40, weight: .light))
                Text(clock(left)).font(.system(size: 72, weight: .thin, design: .rounded)).monospacedDigit()
                HStack(spacing: 8) {
                    Text("Hold"); Keycaps(label: "⌘"); Text("+"); Keycaps(label: "⌘"); Text("for 2 seconds to finish")
                }.font(.system(size: 14))
            }
            .foregroundStyle(.white.opacity(0.28))
            .frame(maxWidth: .infinity, maxHeight: .infinity).background(Color.black)
        }
        .environment(\.colorScheme, .dark)
    }
}

// MARK: - Automation

private struct AutomationPage: View {
    @ObservedObject var model: AppModel
    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            Card {
                VStack(alignment: .leading, spacing: 18) {
                    HStack(alignment: .top, spacing: 16) {
                        IconTile("laptopcomputer.and.arrow.down", tint: accent, size: 52)
                        VStack(alignment: .leading, spacing: 6) {
                            Text("External keyboard mode").font(.title3.weight(.semibold))
                            Text("Plug in a keyboard and the built-in one locks itself. Unplug it and it comes back.")
                                .foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                        }
                        Spacer()
                        Toggle("External keyboard mode", isOn: Binding(get: { model.automatic }, set: model.setAutomatic))
                            .toggleStyle(.switch).labelsHidden().controlSize(.large)
                    }
                    FlowDiagram(active: model.automatic)
                    HStack {
                        Label(model.automationDescription, systemImage: model.automatic ? "bolt.fill" : "pause.circle")
                            .font(.callout.weight(.medium)).foregroundStyle(model.automatic ? accentText : Color.secondary)
                        Spacer()
                        if model.automatic { Button("Run now") { model.resumeAutomatic() }.disabled(!model.permitted || model.busy) }
                    }
                    .padding(12).background(Color.primary.opacity(0.04), in: RoundedRectangle(cornerRadius: 10))
                }
            }
            SectionLabel("How it behaves")
            Card(padding: 0) {
                VStack(spacing: 0) {
                    RuleRow("timer", "Uses your auto-unlock duration", "Set it on the Keyboards page or in Settings.")
                    Divider().padding(.leading, 60)
                    RuleRow("arrow.triangle.2.circlepath", "Once per connection", "When a lock times out, it won't immediately relock.")
                    Divider().padding(.leading, 60)
                    RuleRow("hand.raised", "Unlock all pauses it", "Until the next connection, or until you press Run now.")
                    Divider().padding(.leading, 60)
                    RuleRow("moon", "Sleep ends the session", "Sleeping, switching users or quitting releases everything.")
                }
            }
            Hint("info.circle", "The app has to be running. The first lock in each session asks for Touch ID or your password.")
        }
    }
}

private struct FlowDiagram: View {
    let active: Bool
    var body: some View {
        HStack(spacing: 10) {
            node("keyboard", "External connects")
            Image(systemName: "arrow.right").foregroundStyle(.tertiary)
            node("laptopcomputer", "Built-in locks", locked: true)
            Image(systemName: "arrow.right").foregroundStyle(.tertiary)
            node("lock.open", "Unplug to release")
        }.frame(maxWidth: .infinity).opacity(active ? 1 : 0.55)
    }
    private func node(_ symbol: String, _ text: String, locked: Bool = false) -> some View {
        VStack(spacing: 6) {
            Image(systemName: symbol).font(.system(size: 18)).foregroundStyle(locked && active ? accentText : .secondary)
                .frame(width: 40, height: 40).background(Color.primary.opacity(0.05), in: RoundedRectangle(cornerRadius: 10))
            Text(text).font(.caption).foregroundStyle(.secondary)
        }.frame(maxWidth: .infinity)
    }
}

private struct RuleRow: View {
    let symbol, title, detail: String
    init(_ symbol: String, _ title: String, _ detail: String) { self.symbol = symbol; self.title = title; self.detail = detail }
    var body: some View {
        HStack(spacing: 14) {
            IconTile(symbol, tint: .secondary, size: 32)
            VStack(alignment: .leading, spacing: 2) {
                Text(title).font(.system(size: 13, weight: .medium))
                Text(detail).font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
        }.padding(.horizontal, 14).padding(.vertical, 12)
    }
}

// MARK: - Shortcuts

private struct ShortcutsPage: View {
    @ObservedObject var model: AppModel
    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            Card {
                HStack(spacing: 14) {
                    IconTile("command", tint: accent, size: 40)
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Global shortcuts").font(.headline)
                        Text("Work from any app, on any keyboard that isn't locked.").font(.callout).foregroundStyle(.secondary)
                    }
                    Spacer()
                    Toggle("Global shortcuts", isOn: Binding(get: { model.shortcutsEnabled }, set: model.setShortcutsEnabled))
                        .toggleStyle(.switch).labelsHidden()
                }
            }
            Card(padding: 0) {
                VStack(spacing: 0) {
                    row("Toggle selected keyboards", "Lock your selection, or unlock it if any is locked.", "toggle")
                    Divider().padding(.leading, 18)
                    row("Unlock all keyboards", "Release everything and pause automatic mode.", "unlock")
                }
            }.disabled(!model.shortcutsEnabled).opacity(model.shortcutsEnabled ? 1 : 0.55)
            HStack {
                if !model.shortcutNote.isEmpty {
                    Label(model.shortcutNote, systemImage: "info.circle").font(.callout).foregroundStyle(.secondary)
                }
                Spacer()
                if model.recording != nil { Button("Cancel recording") { model.cancelRecording() } }
                Button("Restore defaults") { model.resetShortcuts() }
            }
            Hint("cursorarrow.rays", "Locking every keyboard? Keep your mouse handy — shortcuts need a keyboard that still works.")
        }
    }
    private func row(_ title: String, _ detail: String, _ action: String) -> some View {
        HStack(spacing: 20) {
            VStack(alignment: .leading, spacing: 3) {
                Text(title).font(.system(size: 13, weight: .semibold))
                Text(detail).font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
            Button { model.beginRecording(action) } label: {
                Group {
                    if model.recording == action {
                        Text("Press keys…").font(.system(size: 12, weight: .medium)).foregroundStyle(accentText).frame(minWidth: 120, minHeight: 26)
                    } else if let shortcut = model.shortcuts[action] {
                        Keycaps(label: shortcut.label).frame(minWidth: 120, minHeight: 26)
                    } else {
                        Text("Record shortcut").font(.system(size: 12)).foregroundStyle(.secondary).frame(minWidth: 120, minHeight: 26)
                    }
                }
                .padding(.horizontal, 8).padding(.vertical, 4)
                .background(RoundedRectangle(cornerRadius: 8).fill(model.recording == action ? accent.opacity(0.1) : Color.primary.opacity(0.04)))
                .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(model.recording == action ? accent : Color.primary.opacity(0.1), style: StrokeStyle(lineWidth: 1, dash: model.recording == action ? [4, 3] : [])))
            }.buttonStyle(.plain).help("Click, then press a new shortcut")
        }.padding(.horizontal, 18).padding(.vertical, 16)
    }
}

struct Keycaps: View {
    let label: String
    var body: some View {
        HStack(spacing: 4) {
            ForEach(Array(keys.enumerated()), id: \.offset) { _, key in
                Text(key).font(.system(size: 12, weight: .medium, design: .rounded))
                    .frame(minWidth: 22, minHeight: 22).padding(.horizontal, key.count > 1 ? 5 : 0)
                    .background(RoundedRectangle(cornerRadius: 5).fill(card))
                    .overlay(RoundedRectangle(cornerRadius: 5).strokeBorder(Color.primary.opacity(0.15)))
                    .shadow(color: .black.opacity(0.12), radius: 0, y: 1)
            }
        }
    }
    private var keys: [String] {
        let mods = label.prefix { "⌃⌥⇧⌘".contains($0) }
        return mods.map(String.init) + [String(label.dropFirst(mods.count))].filter { !$0.isEmpty }
    }
}

// MARK: - Activity

private struct ActivityPage: View {
    @ObservedObject var model: AppModel
    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack {
                SectionLabel("This session · \(model.events.count)")
                Spacer()
                Button { model.copyDiagnostics() } label: { Label("Copy diagnostics", systemImage: "doc.on.doc") }
                Button("Clear") { model.events = [] }.disabled(model.events.isEmpty)
            }
            Card(padding: 0) {
                if model.events.isEmpty {
                    VStack(spacing: 8) {
                        Image(systemName: "clock").font(.system(size: 28, weight: .light)).foregroundStyle(.tertiary)
                        Text("No activity yet").foregroundStyle(.secondary)
                    }.frame(maxWidth: .infinity).padding(36)
                } else {
                    VStack(alignment: .leading, spacing: 0) {
                        ForEach(Array(model.events.enumerated()), id: \.element.id) { index, event in
                            HStack(alignment: .top, spacing: 14) {
                                Text(event.date, style: .time).font(.system(size: 11)).monospacedDigit().foregroundStyle(.secondary)
                                    .frame(width: 70, alignment: .trailing).padding(.top, 1)
                                VStack(spacing: 0) {
                                    Circle().fill(tint(event.text)).frame(width: 8, height: 8).padding(.top, 4)
                                    if index < model.events.count - 1 { Rectangle().fill(Color.primary.opacity(0.1)).frame(width: 1).frame(maxHeight: .infinity) }
                                }.frame(width: 8)
                                Text(event.text).font(.callout).textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading).padding(.bottom, 14)
                            }
                        }
                    }.padding(.horizontal, 16).padding(.top, 16).padding(.bottom, 2)
                }
            }
            Hint("lock.shield", "Only app actions appear here. Keystrokes are never recorded, and this clears when you quit.")
        }
    }
    private func tint(_ text: String) -> Color {
        if text.hasPrefix("Locked") { return accent }
        if text.hasPrefix("Unlocked") || text.contains("released") { return positive }
        if text.localizedCaseInsensitiveContains("fail") || text.localizedCaseInsensitiveContains("could not") || text.contains("refused") { return .orange }
        return Color.secondary.opacity(0.5)
    }
}

// MARK: - Settings

private struct SettingsPage: View {
    @ObservedObject var model: AppModel
    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            PermissionCard(model: model)
            SectionLabel("Behavior")
            Card(padding: 0) {
                VStack(spacing: 0) {
                    SettingRow("timer", "Default auto-unlock") { DurationPicker(model: model).frame(width: 160) }
                    Divider().padding(.leading, 60)
                    SettingRow("pin", "Keep window on top while locked") {
                        Toggle("", isOn: Binding(get: { model.floatWhenLocked }, set: model.setFloating)).toggleStyle(.switch).labelsHidden()
                    }
                    Divider().padding(.leading, 60)
                    SettingRow("power", "Open at login") {
                        Toggle("", isOn: Binding(get: { model.launchAtLogin }, set: model.setLogin)).toggleStyle(.switch).labelsHidden()
                    }
                }
            }
            Hint("menubar.rectangle", "Closing the window keeps BrickMyBoard in the menu bar. Quitting releases every lock.")
            SectionLabel("Control session")
            Card {
                HStack(spacing: 14) {
                    IconTile("key.fill", tint: model.phase == "ready" ? positive : .secondary, size: 40)
                    VStack(alignment: .leading, spacing: 3) {
                        Text(model.phase == "ready" ? "Authorized" : model.phase.capitalized).font(.headline)
                        Text("Lasts only while the app runs. No driver or background service is installed.").font(.caption).foregroundStyle(.secondary)
                    }
                    Spacer()
                    Button("End & unlock all") { model.endSession() }.disabled(model.phase == "offline")
                }
            }
            HStack(spacing: 12) {
                Image(nsImage: NSApp.applicationIconImage).resizable().frame(width: 40, height: 40)
                VStack(alignment: .leading, spacing: 2) {
                    Text("BrickMyBoard 0.0.1").font(.system(size: 13, weight: .semibold))
                    Text("Built for this Mac. No accounts. No network.").font(.caption).foregroundStyle(.secondary)
                }
                Spacer()
                Button("Copy diagnostics") { model.copyDiagnostics() }
            }.padding(.top, 8)
        }
    }
}

private struct SettingRow<Trailing: View>: View {
    let symbol, title: String
    @ViewBuilder let trailing: () -> Trailing
    init(_ symbol: String, _ title: String, @ViewBuilder trailing: @escaping () -> Trailing) { self.symbol = symbol; self.title = title; self.trailing = trailing }
    var body: some View {
        HStack(spacing: 14) {
            IconTile(symbol, tint: .secondary, size: 32)
            Text(title).font(.system(size: 13))
            Spacer()
            trailing()
        }.padding(.horizontal, 14).padding(.vertical, 10)
    }
}

// MARK: - Menu bar popover

struct MenuBarView: View {
    @ObservedObject var model: AppModel
    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 10) {
                Image(systemName: model.locks.isEmpty ? "keyboard" : "lock.fill").font(.system(size: 14, weight: .semibold))
                    .foregroundStyle(model.locks.isEmpty ? positive : accentText).frame(width: 30, height: 30)
                    .background((model.locks.isEmpty ? positive : accent).opacity(0.15), in: Circle())
                VStack(alignment: .leading, spacing: 1) {
                    Text(model.locks.isEmpty ? "All keyboards available" : "\(model.locks.count) locked").font(.system(size: 13, weight: .semibold))
                    Text(model.phaseText).font(.system(size: 11)).foregroundStyle(.secondary)
                }
                Spacer()
                if case .some(let remaining) = model.longestRemaining {
                    Text(remaining.map(clock) ?? "∞").font(.system(size: 15, weight: .semibold, design: .rounded)).monospacedDigit().foregroundStyle(accentText)
                }
            }.padding(12)
            Divider()
            VStack(spacing: 2) {
                ForEach(model.devices) { device in
                    let state = model.locks[device.id]
                    HStack(spacing: 10) {
                        Image(systemName: device.builtIn ? "laptopcomputer" : "keyboard").frame(width: 20).foregroundStyle(state == nil ? Color.secondary : accentText)
                        VStack(alignment: .leading, spacing: 0) {
                            Text(device.builtIn ? "Built-in Keyboard" : device.name).font(.system(size: 12, weight: .medium)).lineLimit(1)
                            Text(state.map { $0.remaining.map(clock) ?? "Locked" } ?? "Available").font(.system(size: 10)).monospacedDigit()
                                .foregroundStyle(state == nil ? Color.secondary : accentText)
                        }
                        Spacer()
                        Toggle("Lock \(device.name)", isOn: Binding(get: { state != nil }, set: { on in on ? model.lock(ids: [device.id]) : model.unlock(ids: [device.id]) }))
                            .toggleStyle(.switch).controlSize(.mini).labelsHidden().disabled(!model.permitted || (state == nil && model.busy))
                    }.padding(.horizontal, 12).padding(.vertical, 6)
                }
            }.padding(.vertical, 6)
            Divider()
            HStack {
                Button { model.lockSelected() } label: { Label("Lock selected", systemImage: "lock.fill").frame(maxWidth: .infinity) }
                    .buttonStyle(.borderedProminent).disabled(!model.permitted || model.selected.isEmpty || model.busy)
                Button { model.unlockAll() } label: { Label("Unlock all", systemImage: "lock.open").frame(maxWidth: .infinity) }
                    .disabled(model.locks.isEmpty && model.phase != "authorizing")
            }.controlSize(.large).padding(12)
            Button { model.startCleaning() } label: {
                Label("Clean for \(model.cleaningMinutes) min", systemImage: "sparkles").frame(maxWidth: .infinity)
            }.disabled(!model.permitted || model.busy || model.devices.isEmpty).padding(.horizontal, 12).padding(.bottom, 12)
            Divider()
            HStack {
                Button("Open BrickMyBoard") { model.showWindow() }.buttonStyle(.borderless)
                Spacer()
                Button("Quit") { NSApp.terminate(nil) }.buttonStyle(.borderless).foregroundStyle(.secondary)
            }.font(.system(size: 12)).padding(.horizontal, 12).padding(.vertical, 9)
        }
        .frame(width: 300).tint(accent)
    }
}

// MARK: - Shared pieces

private struct Card<Content: View>: View {
    var padding: CGFloat = 18
    @ViewBuilder let content: () -> Content
    var body: some View {
        content().frame(maxWidth: .infinity, alignment: .leading).padding(padding)
            .background(card, in: RoundedRectangle(cornerRadius: 14))
            .overlay(RoundedRectangle(cornerRadius: 14).strokeBorder(Color.primary.opacity(0.07)))
    }
}

private struct SectionLabel: View {
    let text: String
    init(_ text: String) { self.text = text }
    var body: some View { Text(text.uppercased()).font(.system(size: 11, weight: .semibold)).tracking(0.4).foregroundStyle(.secondary) }
}

private struct Hint: View {
    let symbol, text: String
    init(_ symbol: String, _ text: String) { self.symbol = symbol; self.text = text }
    var body: some View {
        Label(text, systemImage: symbol).font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
    }
}

private struct IconTile: View {
    let symbol: String; let tint: Color; let size: CGFloat
    init(_ symbol: String, tint: Color, size: CGFloat) { self.symbol = symbol; self.tint = tint; self.size = size }
    var body: some View {
        Image(systemName: symbol).font(.system(size: size * 0.42, weight: .medium)).foregroundStyle(tint)
            .frame(width: size, height: size).background(tint.opacity(0.12), in: RoundedRectangle(cornerRadius: size * 0.26))
    }
}


/// The logo from Assets/logo.svg; on dark backgrounds the block turns cream and the keys charcoal.
struct LogoMark: View {
    @Environment(\.colorScheme) private var scheme
    private static let svg = Bundle.main.url(forResource: "logo", withExtension: "svg").flatMap { try? String(contentsOf: $0, encoding: .utf8) } ?? ""
    private static let light = NSImage(data: Data(svg.utf8))
    private static let dark = NSImage(data: Data(svg.replacingOccurrences(of: "#292a28", with: "#TMP").replacingOccurrences(of: "#f3e9dd", with: "#292a28").replacingOccurrences(of: "#TMP", with: "#f3e9dd").utf8))
    var body: some View {
        if let image = scheme == .dark ? Self.dark : Self.light { Image(nsImage: image).resizable().scaledToFit() }
    }
}
