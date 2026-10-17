# BrickMyBoard

A native app for locking physical keyboards, one at a time or all at once. Use it to type on an external keyboard placed on top of your laptop, or to wipe a keyboard and screen clean without typing anything.

The macOS app lives at the top of this repository; the Windows app lives in [`windows/`](windows/). See [Windows](#windows) below.

## Install

1. Download `BrickMyBoard.zip` from the [latest release](../../releases/latest), unzip it, and move **BrickMyBoard.app** to Applications.
2. Open it. The app isn't notarized, so macOS blocks the first launch. Go to **System Settings → Privacy & Security**, scroll down, and click **Open Anyway**.

Requires macOS 14 or later on Apple silicon.

## Cleaning mode

The **Cleaning** page locks every keyboard and blacks out every screen for 1, 2, 5 or 10 minutes. To finish early, hold both ⌘ keys, and nothing else, for 2 seconds. The menu-bar popover can start it too.

## Touch ID

Locking needs a short-lived administrator helper. If Touch ID is enabled for `sudo`, the app asks for your fingerprint instead of your password:

```sh
sed "s/^#auth/auth/" /etc/pam.d/sudo_local.template | sudo tee /etc/pam.d/sudo_local
```

This also turns on Touch ID for `sudo` in Terminal.

## First use

1. Click **Open permission settings…** and enable **BrickMyBoard** under **Privacy & Security → Input Monitoring**. Reopen the app if macOS asks. If the app still says permission is missing, click **Reset permission and ask again**, then **Relaunch**.
2. Select a keyboard and a duration, then click **Lock** or **Lock selected**.
3. Approve with Touch ID or your password. The same authorized helper supports subsequent lock/unlock actions until you end the session or quit.
4. Use the row’s **Unlock** button, **Unlock all**, the menu-bar controls, or the global shortcut to release keyboards.

Start with a 30-second lock in an empty document. Verify the intended physical keyboard stops typing and the other one still works. Independent media-key interfaces, Power, and Touch ID may remain active.

## Controls

- **Keyboards:** discover built-in, USB, and Bluetooth keyboard HID devices; select one or several; lock or unlock individual keyboards; refresh and inspect device details in tooltips. Virtual-transport and non-keyboard devices are excluded.
- **Timers:** 30 seconds, 2/5/15 minutes, 1 hour, or until manually unlocked. Each new lock gets its own deadline. Changing the default does not change existing locks.
- **Menu bar:** open the window, toggle any connected keyboard, lock the selection, unlock everything, end authorization, or quit. Closing the window keeps the app running in the menu bar.
- **Automation:** optional built-in locking when an external keyboard connects. It releases the built-in keyboard when no unlocked external keyboard remains. It uses the selected duration and runs once per connection; it does not immediately relock after a timer expires. Manual Unlock all pauses automation until a new external connection or **Run now / resume**. The app must be running, and the first lock still requires administrator authorization. Off by default.
- **Shortcuts:** configurable global toggle-selection and Unlock-all shortcuts. Click a shortcut to record it; use at least two modifiers. Escape cancels, Delete clears, Restore defaults resets both. Duplicate bindings are rejected and OS registration conflicts are reported.
- **Settings:** default timer, keep the window on top during locks, optional launch at login, and explicit end-authorization control. Launch at login is off by default.
- **Activity:** the latest 50 app actions in memory, clearable and available in copied diagnostics. No keystrokes are recorded; quitting clears activity. Diagnostics include OS version, permission/session status, device names/IDs, and app actions.

Default global shortcuts:

| Action | Shortcut |
| --- | --- |
| Toggle selected keyboards | Control–Option–Command–K |
| Unlock all keyboards | Control–Option–Command–U |

Global shortcuts need a keyboard that is not locked. If you lock every keyboard, use the mouse/menu bar or wait for the timer.

## Release behavior and permissions

No driver, daemon, account, or network service is installed. The app’s short-lived administrator helper opens only the requested keyboard device IDs. The app and helper exchange bounded, validated messages over a private local Unix socket.

Device selection is revalidated by the helper before acquisition. If one device in a new group cannot be acquired, all newly acquired devices in that group are released; existing locks stay unchanged. Timers are enforced in the helper. Disconnecting a device removes its lock. Closing the app/helper connection or exiting the helper releases all devices. Quitting, system sleep, or user switching ends the session. Merely closing the main window does not quit.

macOS may refuse exclusive access on a protected device or when another utility already owns it. The app reports that error and does not label the failed acquisition as locked. Input Monitoring approval does not guarantee every device is supported.

## Build

Requires Xcode's command-line tools. Run `./build.command` to compile the Swift 6 sources, generate the icon, apply an ad-hoc signature and run the self-checks. Each rebuild changes the signature, so macOS asks for Input Monitoring again.

- `App.swift`: app lifecycle, session management, menu bar, cleaning mode, settings, shortcuts.
- `Views.swift`: SwiftUI screens hosted in an AppKit window.
- `KeyboardLock.swift`: keyboard discovery, privileged helper, timers, exit combo, validated IPC.
- `Checks.swift`: regression checks.

## Status

The self-checks pass. They cover the logic but not real hardware: locking a physical keyboard, the cleaning exit combo and Touch ID are checked by hand.

## Windows

The same app for Windows 10 and 11, in [`windows/`](windows/): a single `BrickMyBoard.exe` with the same pages (Keyboards, Cleaning, Automation, Shortcuts, Activity, Settings), a tray icon instead of the menu bar, and the same safety rules.

### Install

Build it (below) or download `BrickMyBoard-windows.zip` from the latest release, unzip it anywhere, and run **BrickMyBoard.exe**. Nothing is installed; delete the folder to remove it. Windows SmartScreen may warn on first launch because the app is not signed: choose **More info → Run anyway**.

Requires Windows 10 or later with .NET Framework 4.8, which both Windows 10 and 11 include.

### Build

Run `windows\build.cmd`. It needs nothing beyond Windows itself: it compiles the C# sources with the compiler that ships with .NET Framework 4.8, generates the icon from `Assets/AppIcon.png`, and runs the self-checks and a device listing.

- `windows/KeyboardLock.cs`: keyboard discovery, the lock protocol, the input filter and the helper.
- `windows/App.cs`: app lifecycle, control session, tray icon, cleaning mode, automation, shortcuts, settings.
- `windows/Views.cs`: the WinForms window and the cleaning overlay.
- `windows/Checks.cs`: regression checks (`BrickMyBoard.exe --self-test`). `BrickMyBoard.exe --check` lists keyboards.

### How a lock works on Windows

Windows has no equivalent of macOS's exclusive device open, so BrickMyBoard uses the two input APIs together: **Raw Input** reports which physical keyboard every key came from, and a **low-level keyboard hook** stops the key from going any further when that keyboard is locked. The filter runs in a short-lived helper process; Windows removes the hook the instant the helper ends, so a crashed or killed helper can never leave a keyboard locked. Key releases always pass through so no key is left stuck down, and injected input (on-screen keyboard, remote desktop) is never blocked.

The helper asks for administrator approval (a UAC prompt) once per app session, so locks also cover windows that run as administrator. Turn that off in **Settings** to skip the prompt; locks then still work for normal windows, but keys on a locked keyboard can reach windows that run as administrator. Either way the helper runs as you, uses a private named pipe that only your account can open, exchanges bounded, validated messages, and exits when the app's side of the pipe closes.

Built-in keyboards are the ones Windows reports as non-removable and not on USB or Bluetooth. On many laptops the internal keyboard appears as **Standard PS/2 Keyboard**, sometimes next to a second built-in **HID Keyboard Device** for the function row.

### Cleaning mode

Same as on the Mac, with the exit combo mapped to **both Ctrl keys**: hold left Ctrl and right Ctrl, and nothing else, for 2 seconds. Every screen goes black and the display stays awake. Ctrl+Alt+Del, Win+L and the UAC secure desktop are handled by Windows outside any app and keep working.

### Controls

Everything on the macOS list applies, with these differences:

- **Tray icon:** replaces the menu bar. Right-click for per-keyboard toggles, Lock selected, Unlock all, cleaning, ending the authorization, and Quit. Double-click opens the window. Closing the window keeps the app in the tray.
- **Shortcuts:** default **Ctrl+Alt+Win+K** (toggle selected) and **Ctrl+Alt+Win+U** (unlock all). Click a shortcut to record a new one with at least two of Ctrl, Alt, Shift and Win.
- **Open at sign-in:** a `Run` entry under your user account, off by default.
- **Settings** are stored under `HKEY_CURRENT_USER\Software\BrickMyBoard`. Crash details, if any, go to `%LOCALAPPDATA%\BrickMyBoard\crash.log`.
- **Sleep, locking the screen or disconnecting the session** ends the control session and releases every keyboard.

### Limits

- A low-level hook cannot reach the secure desktop (Ctrl+Alt+Del, UAC prompts, the sign-in screen). That is by design on Windows and also means you can always get out.
- Keys that the keyboard firmware handles itself (brightness, airplane mode, some Fn combinations) never reach Windows and are not affected.
- Precision touchpad gestures can still trigger Windows actions during cleaning; the black overlay stays on top of them.
- Remote Desktop and other virtual keyboards are not listed and are never blocked.
- The self-checks cover the logic, not hardware. Verify a lock with a 30-second timer in an empty document before relying on it, exactly as on the Mac.

References: [Apple’s device-open API](https://developer.apple.com/documentation/iokit/1588670-iohiddeviceopen), [Input Monitoring access](https://developer.apple.com/documentation/iokit/3181574-iohidrequestaccess), and [native login-item service](https://developer.apple.com/documentation/servicemanagement/smappservice/mainapp).
