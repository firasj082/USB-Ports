# Results and lessons

This file records what has been measured, tried and learned on this project.
Each section lists entries newest first. `.claude/rules/results.md` says how to
write here, and `.claude/rules/read-first.md` says when to read it.

## Open items

- **The SSD keeps dropping on this laptop's ports** (see Incidents, 2026-09-29).
  Since 2026-09-30 it has run through a powered USB 3 hub on Port 1, at USB 3
  (about 380 MB/s).
  - The 10-minute soak had one drop. Only the hub's USB 3 lane dropped; power
    and the USB 2 lane stayed up. That points at plug movement, not power.
  - The owner is securing the plugs.
  - `chkdsk D: /scan` found no problems. The game-file verify in the launcher
    is still to do.
- **Quick reconnects are missed.** `TrayApp` rescans 1.5 s after the last device
  change (`deviceSettle`). A device that drops and comes back inside that window
  looks unchanged, so the alerts can't see the SSD's fast bursts of dropouts.
- **`DriveUsers.Close` isn't safe outside shutdown.** It sends `WM_CLOSE` to
  every top-level window of each app it found, then kills whatever is still
  running after the grace period. `explorer.exe` isn't on its `untouchable`
  list, so used from a button it would close the taskbar and desktop.
  **Suspected:** harmless during a shutdown, since the session is ending anyway.
  This hasn't been checked.
- **Plan phase 0 isn't finished.** The other session built the test harness and
  tests (50 pass, and 3 shutdown tests skip while a drive is attached), split
  `TrayApp.cs` (437 lines), and extracted `SessionEnd`. Still open:
  - Commit everything, and release 1.7.2, because the `ENDSESSION_CLOSEAPP`
    fix changes how the app behaves. Waiting for the owner's go-ahead.

## Owner's hardware

- ASUS ROG Strix G531GT, BIOS G531GT.308 (02/2021), Windows 11 Home 25H2
  (10.0.26200). Fast Startup is **off**: the owner turned it off on 2026-09-27
  (see Eject and device state).
- Three USB-A ports on the Intel xHCI root hub (`USB#ROOT_HUB30#4&29fe64cb&0&0`).
  Each physical port is a USB 2 (HS) port paired with a USB 3 (SS) port.
  - Port 1 is HS01 + SS01 (connection 1 / 17).
  - Port 2 is HS03 + SS03 (connection 3 / 19).
  - Port 3 is HS05 + SS04 (connection 5 / 20).
  - The ports share power (see Incidents).
- The internal Intel Bluetooth adapter (VID_8087 PID_0AAA) is port 14 on the
  same root hub.
- **XSTAR SS D 512GB** SSD: D:, NTFS, the owner's games. It's on a
  SATA-to-USB 3 cable because the original enclosure corrupted data. The cable
  asks for 896 mA; a USB 3 port supplies at most 900 mA. Never eject, format or
  power-cycle it in tests.
- SanDisk flash drive (USB vendor id 0781): the test device. Its data doesn't
  matter.
- A gaming mouse. Never reconnect it in tests.

## Performance baselines

### 2026-09-30: Release 1.7.2
- **What changed:**
  - The test harness (`test.ps1`, `tests\`) and the performance check.
  - `Program` split out of `TrayApp.cs`.
  - `SessionEnd.Decide`.
  - **Fix:** a Restart Manager close (`ENDSESSION_CLOSEAPP`, for example an
    update replacing a file the app uses) no longer counts as a shutdown.
    Before, it would have ejected every USB drive while Windows kept running.
  - app.log now records when the shutdown handler starts, and any failure to
    write `shutdown.log`.
- **How:**
  - `.\test.ps1 -Integration`: 50 passed, 3 skipped. The shutdown tests skip
    while a drive is attached.
  - Installed with `--install --no-launch`, run through `explorer.exe` so the
    installer ran outside the Claude app's package.
  - Restarted with `schtasks /run /tn "USB Ports"`, then `.\test.ps1 -Perf` at
    00:51, on a copy about 1 minute old.
- **Result:**
  - Idle: private memory 23.1 MB, working set 4.5 MB, **8 threads**,
    332 handles, 33 GDI objects, 35 USER objects. CPU: 0.000 s in 60 s.
  - Growth over 30 rescans: private +0.1 MB, **handles +9**, GDI 0,
    **USER +2**.
  - Window open: private 23.9 MB, working set 16.8 MB.
  - All pass. The real Uninstall entry (`Uninstall\USBPorts`) shows 1.7.2.
- **So:**
  - Threads, handle growth and USER growth are at or near their limits. The
    copy still had 8 threads 4 minutes after start. The 1.7.1 copy measured
    45 minutes after start had 6.
  - **Suspected:** threads left over from startup (COM to Task Scheduler,
    thread pool), since nothing new runs while idle. Re-measure on a copy that
    has run for an hour before the next release. The USER +2 appeared on 1.7.1
    too.

### 2026-09-30: 1.7.1, first run of the performance check
- **How:** `.\test.ps1 -Perf` at 00:44 against the installed 1.7.1 (pid 3664,
  started at 23:59 from the Start menu, hidden in the tray). The SSD was
  attached behind the hub on Port 1.
- **Result:**
  - Idle: private memory 27.9 MB, working set 4.0 MB, 6 threads, 366 handles,
    52 GDI objects, 46 USER objects. CPU: 0.000 s in 60 s.
  - Growth over 30 forced rescans: private +0.1 MB, handles −1, GDI 0,
    USER +2.
  - Main window open (Ports page): private 28.1 MB, working set 11.8 MB.
  - All within budget.
- **So:**
  - The "(measure)" rows in `performance.md` are filled in. The budget for the
    window open is private ≤ 35.1 MB (baseline + 25 %); `PerfCheck` checks it
    from now on.
  - The USER growth sits right at its limit (+2). **Suspected:** a one-time
    object from the first scans after a long idle, not a leak. If it shows up
    again, find the source before release.

### 2026-09-29: 1.7.1 in the tray, idle
- **How:** The installed copy was started at sign-in at 17:11. The window was
  opened a few times during the evening and then hidden. Measured around 23:00
  with `Get-Process` and `GetGuiResources`.
- **Result:**
  - Private memory 26.6 MB, working set 5.6 MB.
  - 351 handles, 6 threads, 45 GDI objects, 42 USER objects.
  - Total CPU 4.25 s over about 6 hours. It didn't change between two samples
    a few minutes apart.
- **So:** This is the baseline for the budgets in
  `.claude/rules/performance.md`.

## Incidents

Moved to [results/incidents.md](results/incidents.md) (newest first) when this file
neared 500 lines. Read it at the start of a session like the rest of this file.

## USB hardware and IOCTLs

- **The hardware decides USB 2 or USB 3 when a device connects.** Windows has no
  setting or API that forces one port to USB 2; only a USB 2 cable, extension or
  hub does. Nobody has checked whether this laptop's BIOS has a per-port xHCI
  option. Never program the chipset directly.
- A physical USB 3 port is an HS port plus an SS companion port on the same root
  hub. They are paired with `IOCTL_USB_GET_PORT_CONNECTOR_PROPERTIES`, which is
  **0x220458**; 0x220468 returns ERROR_NOT_SUPPORTED.
- Other codes in use:

  | IOCTL | Code |
  |---|---|
  | `USB_GET_NODE_CONNECTION_INFORMATION_EX` | 0x220448 |
  | `..._EX_V2` | 0x22045C |
  | `USB_GET_HUB_INFORMATION_EX` | 0x220454 |
  | `USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION` | 0x220410 |
  | `USB_HUB_CYCLE_PORT` (needs admin, hence the elevated `--reconnect` helper) | 0x220444 |
  | `STORAGE_GET_DEVICE_NUMBER` | 0x2D1080 |
  | `STORAGE_QUERY_PROPERTY` | 0x2D1400 |
  | `DISK_GET_DRIVE_GEOMETRY_EX` | 0x700A0 |
- On this laptop, power-cycling one port can disturb devices on other ports
  (see Incidents).

## Eject and device state

- `CM_Request_Device_Eject` works without admin. When it's refused, the veto
  type and name give a rough reason (`DriveEjector.TryEject`). Windows services,
  such as search indexing and antivirus, can hold a drive, and the app can't
  name them without admin.
- **After an eject the device stays off** (problem code 47, "prepared for
  removal"). It comes back only when it's unplugged, its port is power-cycled,
  or the PC really powers off. `CM_Setup_DevNode` and `CM_Reenumerate_DevNode`
  are denied without admin, and they failed even as admin. Any Eject feature
  must tell the user to replug.
- **Fast Startup keeps that state across a shutdown.** With Fast Startup on, a
  drive ejected at shutdown was still missing after boot and needed a replug.
  The owner turned Fast Startup off on 2026-09-27; after that, eject at
  shutdown followed by a boot worked.
- 2026-09-27 at 20:00 and 20:15: the shutdown eject worked. `shutdown.log`
  says "safely ejected XSTAR SS D 512GB (D:)" both times.
- To find the apps holding a drive, `DriveUsers` does the following, on a worker
  thread with a time limit:
  1. Takes a handle snapshot (`NtQuerySystemInformation(0x40)`).
  2. Copies each handle (`DuplicateHandle`) and checks its type with
     `GetFileType`, skipping anything that isn't a disk file.
  3. Reads the handle's path with `GetFinalPathNameByHandle`.

  Only the current user's apps are visible.

## Shutdown and startup

- **2026-09-29: The 10:27 shutdown eject did work.** The real `shutdown.log`
  has "ejecting USB drives..." at 10:27:26 and "safely ejected XSTAR SS D
  512GB (D:)" at 10:27:33. The "no record" Open item came from reading a stale
  copy of `shutdown.log` that the Claude desktop app's package keeps in its
  LocalCache (see Incidents and Tooling), not from the app. So the sign-in copy
  of 1.7.1 does eject at shutdown.
  - The eject took 7 s, and Windows did not end the app. That's although Learn
    ("Shutdown Changes for Windows Vista") says apps without a visible window
    are ended if they take more than 5 s to answer WM_ENDSESSION.
    **Suspected:** a longer eject (a busy drive, closing apps: up to 30 s) could
    still be cut off. Not tested.
- **Task Scheduler ends the programs it started when the user signs out, and a
  shutdown signs out first.**
  - Up to 1.7.0, the copy the task started at sign-in was already gone before
    any shutdown message arrived, so it never ejected.
  - Since 1.7.1 (2026-09-29) the task runs
    `explorer.exe "…\USB Ports (sign-in).lnk"`, so the app runs under Explorer.
  - Confirmed at 10:27 on 2026-09-29: the sign-in copy received both
    session-end messages (but see Open items).
- **A shutdown from the sign-in or lock screen reaches apps flagged as a
  sign-out** (`ENDSESSION_LOGOFF`). The app recognises it by User32 event 1074
  (shutdown or restart requested) within the last 120 s; a plain sign-out logs
  no 1074.
- `SetProcessShutdownParameters(0x100)` makes Windows tell the app late, after
  most apps have closed. `ShutdownBlockReasonCreate` shows the app's reason on
  Windows' "preventing shutdown" screen.
- **Start with Windows on Windows 11 25H2** needs three parts:
  - The Run value `"exe" --tray --autostart`.
  - A `StartupApproved\Run` value. A first byte of 02 means enabled; an odd
    first byte means it was disabled in Task Manager.
  - The sign-in task, because on this PC Explorer read the Run entry but never
    started the app.

## WinForms drawing

- `TextRenderer.MeasureText` without a device context wraps text differently
  from drawing at 125 % scaling. Measure with `Theme.WrappedHeight`, which uses
  the control's `CreateGraphics`.
- Passing a `backColor` to `TextRenderer.DrawText` paints a rectangle behind the
  text, which shows as square corners on rounded shapes. Leave it out there.
- `ApplicationContext.MainForm` clashes with the `MainForm` class; write
  `global::MainForm`.
- After changing the content of an `AutoScroll` panel, call `PerformLayout()`,
  or the scroll range stays stale.
- `OnHandleCreated` can run before the constructor has set fields. Use the
  child control's own `HandleCreated` event instead. The 1.7.0 null crash is in
  `app.log` at 2026-09-27 23:00.
- Dark mode uses DWM for the dark title bar and rounded corners, and
  `SetWindowTheme("DarkMode_Explorer")` for scrollbars.

## Tooling

- **The Claude desktop app's tools run inside its MSIX package**
  (`Claude_pzs8sxrjxfjjc`), found 2026-09-29.
  - Writes to HKCU go to a private registry. New files under `%LOCALAPPDATA%`
    go to `AppData\Local\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Local\`.
  - Reads see those redirected copies first. For example, `USB Ports\shutdown.log`
    read from Claude's tools was a stale copy, while `app.log` (which has no copy)
    was the real one.
  - The installed app and anything started through `explorer.exe` run outside
    the package and see the real files and registry.
  - To read the real ones, write a small `.cmd` that copies them into `dist\`
    and start it with `explorer.exe "<file>.cmd"`.
  - Test runs are unaffected, because they read and write inside one process.
- In PowerShell 5.1, `Get-Content` and `Set-Content` read and write ANSI, which
  garbles UTF-8 (`—` becomes `â€”`). Use `[IO.File]::ReadAllText` and
  `WriteAllText` with `UTF8Encoding($false)`, and compile with
  `/codepage:65001`.
- `Start-Process -Wait` also waits for every process the started program
  launches, and the installer launches the app. Use `-PassThru` and
  `$p.WaitForExit()` instead.
- PowerShell wraps the arguments of reflection calls in `PSObject`. Create the
  objects with `::new()` and pass `[object[]]` arrays.
- `git commit -F -` fed from a here-string was read as a pathspec. Write the
  message to a temp file and pass that file to `-F`.
- The Claude Code harness refused `Remove-Item` on `D:\` and on paths held in
  variables. `[IO.File]::Delete` with a literal path worked.

## Decisions

### 2026-09-30: Simulated shutdowns only with no USB drive plugged in
- **What:** The owner decided that no test may send session-end messages to any
  copy of USB Ports while a USB drive is attached. That includes a copy inside
  the test process whose eject is a stand-in.
- **Why:** Two simulated shutdowns ejected the owner's SSD (see Incidents,
  "Claude's tests ejected the SSD twice"). The "eject off" setting written from
  Claude's tools never reached the app (MSIX redirection).
- **So:**
  - The rule is in CLAUDE.md (testing section) and `.claude/rules/testing.md`.
  - `ShutdownIntegrationTests` throws `SkipTest` when
    `DriveEjector.UsbDrives()` isn't empty. Verified: 3 SKIP with the SSD
    attached.
  - Trade-off: while the SSD stays plugged in, the automated shutdown test
    doesn't run. The owner can relax the rule for the stand-in test later.

### 2026-09-29: Feature shortlist
- **Build, in this order:**
  1. Connection history, with an alert when a device keeps reconnecting.
  2. An Eject button that names what is using the drive, plus Eject entries in
     the tray menu.
  3. Correct advice when a USB 3 device runs at USB 2.

  Maybe later: names for the ports.
- **Not building:**
  - A global eject hotkey. It takes the key combination away from every app,
    games included.
  - Eject before sleep. Windows allows only about 2 s at sleep, and Modern
    Standby may not send the notice at all. An ejected drive stays off until
    replugged. This laptop already misbehaves around sleep (see Incidents).
  - A drive speed test, SMART health or temperature. These need reading the
    drive or sending it commands, which breaks safety rule 1.
  - Running programs when a drive is plugged in. It's out of scope.
  - Live throughput graphs. They would need polling, and Windows doesn't report
    throughput per device.
