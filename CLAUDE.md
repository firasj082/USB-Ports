# USB Ports: project rules

A small Windows tray app (C#, WinForms) that shows what is plugged into each
USB port, how fast it runs, and safely ejects USB drives at shutdown. It talks to
real hardware on the owner's laptop, so the safety rules below come first.

## More rules, and the results log

The files in `.claude/rules/` are part of these rules and load with this one:

- `read-first.md`: what to read before changing each area, and when to read the
  official docs.
- `testing.md`: every change ships with unit and integration tests. Also covers
  how the test harness works.
- `performance.md`: the RAM and CPU budgets and how to measure them.
  Performance comes first in every design choice, after hardware safety.
- `file-size.md`: no file may exceed 500 lines.
- `results.md`: how to record results in `docs/RESULTS.md`, the project's log
  of measurements, incidents, lessons and decisions. Read that log at the start
  of every session.

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
- Release steps:
  1. Run `.\test.ps1 -Integration`.
  2. Bump the version in `src\AssemblyInfo.cs`.
  3. Run `.\build.ps1 -Release`. This also copies the exe to `download\`, which
     the README's Download button links to.
  4. Install it with `dist\USB-Ports-Setup.exe --install --no-launch`.
  5. Restart the app.
  6. Run `.\test.ps1 -Perf`, and keep the result within budget.
  7. Add the results to `docs\RESULTS.md`.
  8. Commit and push. `dist\` is not committed.
- No file may exceed 500 lines (`.claude/rules/file-size.md`). `.\test.ps1`
  checks this. Split along natural seams, such as a control or a helper class,
  instead of growing a file.

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
- **A simulated shutdown is allowed only when no USB drive is plugged in.** This
  is the owner's rule. It covers sending `WM_QUERYENDSESSION` / `WM_ENDSESSION` to
  any copy of USB Ports, including a `TrayApp` inside the test process.
  - Check immediately before sending that `DriveEjector.UsbDrives()` is empty.
  - If a drive is attached, skip the test and say so. Never ask the owner to
    unplug the SSD just for this.
  - Turning "Safely eject USB drives at shutdown" off is **not** a safeguard.
    Claude's tools run inside the Claude app's MSIX package, so their registry
    writes never reach USB Ports.
  - A simulated shutdown ejected the owner's SSD twice on 2026-09-29/30 (see
    `docs/RESULTS.md`).
  - The automated version is `tests\ShutdownIntegrationTests.cs`. It uses a
    stand-in eject and skips itself while a drive is attached.
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
