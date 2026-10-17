# Win10 End Task

[Tiếng Việt](README.vi.md) | **English**

A tiny utility that brings Windows 11's taskbar right-click "End task" functionality to Windows 10 without breaking Jump Lists or patching system files.

---

## Why

Windows 11 added an "End task" option directly to the taskbar context menu. On Windows 10, closing an unresponsive app requires opening Task Manager or using shortcuts like `Alt + F4` (which often fails when the message loop freezes).

Existing alternatives either:
- Replace the context menu entirely with the legacy window menu (losing Recent files, Tasks, and Pinning).
- Rely on complex DLL injection frameworks into `explorer.exe` which can break on cumulative updates.

This tool sits alongside the native Jump List: right-clicking a taskbar item displays an **End task** button next to the context menu. Clicking it terminates the target process tree immediately.

---

## Features

- **Preserves native Jump Lists:** Recent items, browser tasks, and pinned shortcuts remain intact.
- **Deep multi-process termination:** Eradicates the entire process tree (`taskkill /F /T` and image sweep) so complex multi-process apps (Discord, Riot Games, browsers) cannot respawn.
- **Hookless architecture:** Uses asynchronous hardware polling (`GetAsyncKeyState`) instead of synchronous global hooks, maintaining 100% native mouse polling rate (1000Hz+) with zero cursor lag.
- **Resource footprint:** ~2.5 MB RAM, 0% CPU at idle.
- **No dependencies:** Compiles with the stock .NET Framework 4.0+ compiler pre-installed on Windows 10.
- **Tray integration:** Optional "Start with Windows" toggle and clean exit via system tray icon.

---

## Building from Source

No Visual Studio or external SDK needed. Run `build.bat` from an administrative or standard command prompt:

```cmd
build.bat
```

This invokes `csc.exe` with `app.manifest` from `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\`.

Output: `Win10EndTask.exe` (~18 KB).

---

## How it works

1. Uses an asynchronous background worker (`GetAsyncKeyState`) to detect right-clicks on the taskbar area with sub-millisecond precision.
2. Queries the taskbar button using Microsoft Active Accessibility (`IAccessible`) without blocking Explorer.
3. Automatically resolves the target application PID across titles, processes, and multi-window instances.
4. Spawns an un-activated, native topmost pill button (`EndTaskFloatingBar`) alongside the context menu.
5. On click, force-terminates the process tree and associated watchdog services. Left-clicking elsewhere or waiting 8 seconds dismisses the button automatically.

---

## License

MIT
