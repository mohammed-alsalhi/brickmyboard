# BrickMyBoard

A native macOS app for locking physical keyboards, one at a time or all at once. Use it to type on an external keyboard placed on top of your MacBook, or to wipe a keyboard and screen clean without typing anything.

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

References: [Apple’s device-open API](https://developer.apple.com/documentation/iokit/1588670-iohiddeviceopen), [Input Monitoring access](https://developer.apple.com/documentation/iokit/3181574-iohidrequestaccess), and [native login-item service](https://developer.apple.com/documentation/servicemanagement/smappservice/mainapp).
