# USB Ports: project rules

A small Windows tray app (C#, WinForms) that shows what is plugged into each
USB port, how fast it runs, and safely ejects USB drives at shutdown. It talks to
real hardware on the owner's laptop, so the safety rules below come first.

## Build and release

- Built with the C# compiler that ships with Windows (.NET Framework 4.x,
  `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`). No SDK, no NuGet,
  no project file. `.\build.ps1` compiles `src\*.cs` to `dist\USB-Ports-Setup.exe`.
- The compiler only understands **C# 5**: no string interpolation (`$"..."`),
  `?.`, `nameof`, `=>` members, `out var`, tuples, or auto-property
  initializers. Use `string.Format` / concatenation and anonymous `delegate`s.
- Sources are UTF-8 and compiled with `/codepage:65001`. When editing files with
  PowerShell, read and write them as UTF-8 (`[IO.File]::ReadAllText` /
  `WriteAllText` with `UTF8Encoding($false)`); `Get-Content` in PowerShell 5.1
  reads them as ANSI and garbles `·`, `—` and the icon glyphs.
- Release steps: bump the version in `src\AssemblyInfo.cs`, run
  `.\build.ps1 -Release` (also copies the exe to `download\`, which the README's
  Download button links to), install it with
  `dist\USB-Ports-Setup.exe --install --no-launch`, restart the app, commit, push.
  `dist\` is not committed.
- Keep each file under 500 lines; split along natural seams (a control, a helper
  class) rather than growing a file.

## Safety rules (hardware)

1. **Never read or write a drive's data.** Scans use USB hub IOCTLs, Plug and
   Play properties and volume metadata only. Details reads USB descriptors and
   cached disk properties only.
2. **Nothing disconnects hardware on its own**, except the shutdown eject the user
   switched on. Eject and Reconnect (port power-cycle) happen only when the user
   clicks.
3. **Reconnect must keep the shared-power guard**: the ports on this laptop share
   power, and power-cycling the SSD's port once corrupted a flash drive on
   another port. Never power-cycle while another USB drive is attached (unless
   the user turned "USB ports share power" off), never cycle two ports back to
   back, never cycle a hub that has drives behind it.
4. **The shutdown path must stay bounded and quiet**: at most 30 s, use
   `UsbScanner.ReadForShutdown` (no file-system queries, so a stuck drive cannot
   hang it), never throw, and run from `MainForm.WndProc` before `base.WndProc`.
5. **The running app must never be started by Task Scheduler.** Task Scheduler
   ends the programs it started at sign-out, and a shutdown signs out first, so
   such a copy is gone before the shutdown messages arrive. The sign-in task
   therefore only asks Explorer to open `USB Ports (sign-in).lnk`.
6. **Shutdown from the sign-in or lock screen arrives flagged as a sign-out.**
   Treat it as a shutdown when Windows has just logged event 1074 (shutdown or
   restart requested).
7. Closing apps at shutdown touches only the current user's programs, never
   system processes, never USB Ports itself.
8. Start with Windows respects Task Manager: if the user disabled it there
   (`StartupApproved` flag), don't turn it back on.

## Testing on the owner's laptop

- **Never eject, format or power-cycle the owner's SSD** (XSTAR SS D 512GB, D:)
  or reconnect the mouse without asking first. Test disruptive things on the
  SanDisk flash drive, with test processes, or with fake paths.
- To test the shutdown code without shutting down: send `WM_QUERYENDSESSION` then
  `WM_ENDSESSION` to the app's hidden main window, with "Safely eject USB drives
  at shutdown" turned off first (so no drive is ejected); check `app.log` and
  `shutdown.log`; turn it back on and restart the app with `--tray`.
- Check UI changes by rendering the forms off-screen through the installed exe
  (load it with reflection, `PrintWindow` or `DrawToBitmap`) and looking at the
  images; nobody should have to click through them by hand.
- Logs: `%LOCALAPPDATA%\USB Ports\app.log` (starts, exits, errors, how each
  session end was read) and `shutdown.log` (every shutdown eject).

## Code conventions

- Colours come from `Theme` (a mutable object switched live by `Theme.Apply`);
  icons are `Glyphs` constants (Segoe Fluent Icons / MDL2 code points). No
  hard-coded colours in controls.
- Scale every pixel value with `S(...)` (DPI). Measure wrapped text with
  `Theme.WrappedHeight` (it uses the control's drawing surface; the plain
  `TextRenderer.MeasureText` overload wraps differently at 125%).
- Idle costs nothing: no polling in the tray, timers run only while a window is
  visible or an animation is moving, scans run on the thread pool, and hidden
  scans use `UsbScanner.ReadQuick`.
- Anything the user can change lives in `Settings` (HKCU\Software\UsbPorts) and
  gets a row on the Settings page.
